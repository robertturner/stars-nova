using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using Nova.Common.Components;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Production panel: the build queue for whichever planet the Navigator/Inspector
/// currently has selected (via the shared <see cref="SelectionService"/>), editable the same
/// way ProductionDialog.cs is - push a ProductionCommand onto ClientData.Commands and apply
/// it locally for immediate feedback. Quantity is a NumericUpDown instead of the original's
/// Shift/Ctrl-click x10/x100 (the per-row +/- buttons' press-and-hold covers rapid bulk edits
/// instead). Auto-build (docs/behavior-specs-4/production-queue.md §9) IS exposed here - see
/// AutoBuildOnAdd/ToggleAutoBuild - even though neither the WinForms client nor this port
/// originally surfaced it despite the engine (ProductionOrder.IsAutoBuild) always supporting it.
/// </summary>
public class ProductionViewModel : Tool
{
    private readonly ClientData clientState;
    private readonly SelectionService selection;
    private Star? selectedStar;

    private string planetName = "";

    public string PlanetName
    {
        get => planetName;
        private set => SetProperty(ref planetName, value);
    }

    private bool hasPlanet;

    public bool HasPlanet
    {
        get => hasPlanet;
        private set => SetProperty(ref hasPlanet, value);
    }

    private bool hasColonizedPlanet;

    /// <summary>
    /// Like HasPlanet, but false for an owned star with no population (Colonists == 0) - the
    /// same "colonized" test NavigatorPlanetItemViewModel's own Status already uses. Lets a host
    /// screen (see MobileMainView, where Map stacks Inspector/Production together with no
    /// Navigator alongside them) hide the whole Production panel rather than show it with
    /// nothing meaningful in it for a planet that has no production queue to speak of.
    /// </summary>
    public bool HasColonizedPlanet
    {
        get => hasColonizedPlanet;
        private set => SetProperty(ref hasColonizedPlanet, value);
    }

    private string message = "Select a planet to see its production queue.";

    public string Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    private bool hasMessage = true;

    public bool HasMessage
    {
        get => hasMessage;
        private set => SetProperty(ref hasMessage, value);
    }

    private IReadOnlyList<ProductionItemViewModel> queue = Array.Empty<ProductionItemViewModel>();

    public IReadOnlyList<ProductionItemViewModel> Queue
    {
        get => queue;
        private set => SetProperty(ref queue, value);
    }

    private IReadOnlyList<ProductionCatalogItemViewModel> availableItems = Array.Empty<ProductionCatalogItemViewModel>();

    public IReadOnlyList<ProductionCatalogItemViewModel> AvailableItems
    {
        get => availableItems;
        private set => SetProperty(ref availableItems, value);
    }

    private ProductionCatalogItemViewModel? selectedAvailableItem;

    public ProductionCatalogItemViewModel? SelectedAvailableItem
    {
        get => selectedAvailableItem;
        set => SetProperty(ref selectedAvailableItem, value);
    }

    private int addQuantity = 1;

    public int AddQuantity
    {
        get => addQuantity;
        set => SetProperty(ref addQuantity, value);
    }

    private bool autoBuildOnAdd;

    /// <summary>
    /// docs/behavior-specs-4/production-queue.md §9: an auto-build item is added the same way
    /// as an ordinary one, just flagged so it never blocks the queue when it can't be afforded
    /// that year (ProductionOrder.IsBlocking) - "Factories (Auto Build) Up to 10" reads exactly
    /// like a normal "Factory x10" order with this box checked. Neither the WinForms client nor
    /// this port originally exposed a way to set this at all (see this class's own top-of-file
    /// comment, predating this fix) - AddQuantity here plays the same "Up to N" role.
    /// </summary>
    public bool AutoBuildOnAdd
    {
        get => autoBuildOnAdd;
        set => SetProperty(ref autoBuildOnAdd, value);
    }

    public IRelayCommand AddToQueueCommand { get; }

    /// <summary>
    /// Press-and-hold acceleration for the per-row +/- buttons: a quick click already does its
    /// normal +1/-1 via IncrementCommand/DecrementCommand (untouched), but holding one down
    /// keeps rapidly bulk-adjusting by <see cref="HoldStep"/> every <see cref="HoldInterval"/>
    /// for as long as the pointer stays down - a fast way to add/remove large quantities.
    /// Owned here (the panel VM, which outlives every edit) rather than on the row itself,
    /// since every single tick's AdjustQuantity call rebuilds the whole Queue - and with it,
    /// every row object and its on-screen Button - out from under whichever row started the
    /// hold. The View only needs to tell us which index/direction to keep adjusting; it never
    /// needs to keep the original row or Button alive for the timer to keep working.
    /// </summary>
    private const int HoldStep = 10;

    private static readonly TimeSpan HoldInterval = TimeSpan.FromSeconds(1);

    private DispatcherTimer? holdTimer;
    private int holdIndex;
    private int holdDirection;

    public ProductionViewModel(string id, string title, ClientData clientState, SelectionService selection)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        this.selection = selection;

        AddToQueueCommand = new RelayCommand(AddToQueue);

        selection.PropertyChanged += OnSelectionChanged;
        Refresh(selection.Selected);
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionService.Selected))
        {
            Refresh((sender as SelectionService)?.Selected);
        }
    }

    /// <summary>
    /// Starts (or restarts) the hold-repeat timer for the row at <paramref name="index"/>.
    /// <paramref name="direction"/> is +1 (the "+" button) or -1 (the "−" button); the actual
    /// per-tick step is <see cref="HoldStep"/>, matching the WinForms QueueList's Shift-click
    /// x10 precedent (see ProductionItemViewModel's own doc comment).
    /// </summary>
    public void BeginHold(int index, int direction)
    {
        EndHold();

        holdIndex = index;
        holdDirection = direction;
        holdTimer = new DispatcherTimer { Interval = HoldInterval };
        holdTimer.Tick += OnHoldTick;
        holdTimer.Start();
    }

    public void EndHold()
    {
        if (holdTimer == null)
        {
            return;
        }

        holdTimer.Stop();
        holdTimer.Tick -= OnHoldTick;
        holdTimer = null;
    }

    private void OnHoldTick(object? sender, EventArgs e)
    {
        if (selectedStar == null || holdIndex >= selectedStar.ManufacturingQueue.Queue.Count)
        {
            // The row we were adjusting is gone (deleted by an earlier tick, or the selected
            // planet changed) - nothing left to keep repeating on.
            EndHold();
            return;
        }

        int countBefore = selectedStar.ManufacturingQueue.Queue.Count;
        AdjustQuantity(holdIndex, holdDirection * HoldStep);

        if (selectedStar == null || selectedStar.ManufacturingQueue.Queue.Count < countBefore)
        {
            // This tick's decrement reached 0 and deleted the row - don't let the next tick
            // slide onto whatever item now occupies the same index.
            EndHold();
        }
    }

    private void Refresh(object? selected)
    {
        EndHold();

        if (selected is Star star)
        {
            selectedStar = star;
            PlanetName = star.Name;
            HasPlanet = true;
            HasColonizedPlanet = star.Colonists > 0;
            AvailableItems = BuildCatalog(star);
            SelectedAvailableItem = AvailableItems.FirstOrDefault();
            AddQuantity = 1;
            AutoBuildOnAdd = false;
            RebuildQueueRows(star);
        }
        else
        {
            selectedStar = null;
            PlanetName = "";
            HasPlanet = false;
            HasColonizedPlanet = false;
            AvailableItems = Array.Empty<ProductionCatalogItemViewModel>();
            Queue = Array.Empty<ProductionItemViewModel>();
            Message = selected switch
            {
                Fleet => "Fleets don't have a production queue - select a planet instead.",
                StarIntel => "Only visible for planets you own.",
                _ => "Select a planet to see its production queue.",
            };
            HasMessage = true;
        }
    }

    /// <summary>
    /// Mirrors ProductionDialog.OnLoad's design-list construction: the 5 fixed installations,
    /// plus every owned ship design that isn't this star's own starbase and isn't too big for
    /// its dock capacity (0 if there's no starbase at all, so no ship designs qualify).
    /// </summary>
    private List<ProductionCatalogItemViewModel> BuildCatalog(Star star)
    {
        Race race = clientState.EmpireState.Race;
        var items = new List<ProductionCatalogItemViewModel>
        {
            new ProductionCatalogItemViewModel(new FactoryProductionUnit(race)),
            new ProductionCatalogItemViewModel(new MineProductionUnit(race)),
            new ProductionCatalogItemViewModel(new DefenseProductionUnit(race)),
            new ProductionCatalogItemViewModel(new AlchemyProductionUnit(race)),
            new ProductionCatalogItemViewModel(new TerraformProductionUnit(race)),
        };

        Fleet? starbase = star.Starbase;
        int dockCapacity = starbase?.TotalDockCapacity ?? 0;
        long starbaseDesignId = Global.None;
        if (starbase != null && starbase.Composition.Count > 0)
        {
            starbaseDesignId = starbase.Composition.Values.First().Design.Id;
        }

        foreach (ShipDesign design in clientState.EmpireState.Designs.Values)
        {
            if (design.Id == starbaseDesignId)
            {
                continue; // this design is the starbase already in orbit here
            }

            if (!design.IsStarbase && dockCapacity < design.Mass)
            {
                continue; // too big for this star's dock (or there's no starbase at all)
            }

            items.Add(new ProductionCatalogItemViewModel(new ShipProductionUnit(design)));
        }

        return items;
    }

    private void RebuildQueueRows(Star star)
    {
        var rows = new List<ProductionItemViewModel>();
        int count = star.ManufacturingQueue.Queue.Count;
        for (int i = 0; i < count; i++)
        {
            int index = i; // captured per-row, not the loop variable
            ProductionOrder order = star.ManufacturingQueue.Queue[i];
            bool canMoveUp = index >= 1;
            bool canMoveDown = index < count - 1;

            rows.Add(new ProductionItemViewModel(
                index,
                order,
                onIncrement: () => AdjustQuantity(index, 1),
                onDecrement: () => AdjustQuantity(index, -1),
                onDelete: () => DeleteItem(index),
                onMoveUp: canMoveUp ? () => SwapQueueItems(index, index - 1) : null,
                onMoveDown: canMoveDown ? () => SwapQueueItems(index, index + 1) : null,
                onToggleAutoBuild: () => ToggleAutoBuild(index)));
        }

        Queue = rows;
        Message = rows.Count == 0 ? "Nothing queued." : "";
        HasMessage = rows.Count == 0;
    }

    private void AddToQueue()
    {
        if (selectedStar == null || SelectedAvailableItem == null || AddQuantity <= 0)
        {
            return;
        }

        var order = new ProductionOrder(AddQuantity, SelectedAvailableItem.Unit, AutoBuildOnAdd);
        var command = new ProductionCommand(CommandMode.Add, order, selectedStar.Name, selectedStar.ManufacturingQueue.Queue.Count);
        ApplyCommand(command);
    }

    /// <summary>
    /// Flips an already-queued order between manual and auto-build in place - same Unit and
    /// Quantity, just the flag - pushed as an Edit like AdjustQuantity's own edits. Safe under
    /// ProductionCommand.Edit's own anti-cheat validity check (it only compares Unit.Cost/
    /// RemainingCost, both unchanged here since Unit itself isn't replaced).
    /// </summary>
    private void ToggleAutoBuild(int index)
    {
        if (selectedStar == null)
        {
            return;
        }

        ProductionOrder existing = selectedStar.ManufacturingQueue.Queue[index];
        var edited = new ProductionOrder(existing.Quantity, existing.Unit, !existing.IsAutoBuild);
        ApplyCommand(new ProductionCommand(CommandMode.Edit, edited, selectedStar.Name, index));
    }

    private void AdjustQuantity(int index, int delta)
    {
        if (selectedStar == null)
        {
            return;
        }

        ProductionOrder existing = selectedStar.ManufacturingQueue.Queue[index];
        int newQuantity = existing.Quantity + delta;
        if (newQuantity <= 0)
        {
            DeleteItem(index);
            return;
        }

        var edited = new ProductionOrder(newQuantity, existing.Unit, existing.IsAutoBuild);
        ApplyCommand(new ProductionCommand(CommandMode.Edit, edited, selectedStar.Name, index));
    }

    private void DeleteItem(int index)
    {
        if (selectedStar == null)
        {
            return;
        }

        ApplyCommand(new ProductionCommand(CommandMode.Delete, null!, selectedStar.Name, index));
    }

    /// <summary>
    /// Swaps the queue entries at index/otherIndex - a single atomic ProductionCommand(Swap)
    /// rather than two paired Edit commands (mirrors the WinForms QueueList.SwapProductionOrders
    /// fix). ProductionCommand's own Edit validity check blocks any edit that would *decrease*
    /// the remaining/total cost at an index (an anti-cheat guard against quietly substituting a
    /// cheaper order) - which would also block half of all legitimate reorders whenever the two
    /// adjacent orders have different costs, since exactly one of a paired-Edit swap would then
    /// be moving a cheaper order into a pricier order's slot. A real swap changes no order's
    /// cost at all, so it needs Swap's own validity rule instead of going through Edit's.
    /// </summary>
    private void SwapQueueItems(int index, int otherIndex)
    {
        if (selectedStar == null)
        {
            return;
        }

        ApplyCommand(new ProductionCommand(CommandMode.Swap, selectedStar.Name, index, otherIndex));
    }

    /// <summary>
    /// Same WinForms pattern as everywhere else in this shell: queue the command for the
    /// eventual .orders file, apply it to the local EmpireData immediately, then re-render
    /// from the (now-mutated-in-place) Star so the queue always reflects real, current
    /// indices rather than tracking them by hand across edits.
    /// </summary>
    private void ApplyCommand(ICommand command)
    {
        clientState.Commands.Push(command);
        if (command.IsValid(clientState.EmpireState))
        {
            command.ApplyToState(clientState.EmpireState);
        }

        RebuildQueueRows(selectedStar!);
        selection.NotifyMutated();
    }
}
