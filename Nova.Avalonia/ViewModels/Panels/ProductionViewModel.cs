using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
/// it locally for immediate feedback. Quantity is an editable field instead of the original's
/// Shift/Ctrl-click x10/x100 - both it and every already-queued row's own quantity instead use
/// a RepeatButton +/- pair whose step size ramps up with the current value itself as it's held
/// (see NextStep's own comment). Auto-build (docs/behavior-specs-4/production-queue.md §9) IS exposed here - see
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

    /// <summary>Bound to a RepeatButton (see ProductionView.axaml), not a plain Button - a quick
    /// tap fires this once (a plain +1, since NextStep(anything &lt; 10) is 1), and holding it
    /// down has Avalonia's own RepeatButton re-invoke it on a timer for as long as the pointer
    /// stays down, with each invocation reading AddQuantity fresh - so the step size ramps up
    /// with the value itself as it climbs (see NextStep's own comment), entirely through
    /// Avalonia's already-correct, natively-handled repeat mechanism rather than a hand-rolled
    /// DispatcherTimer coordinated across PointerPressed/PointerReleased. That hand-rolled
    /// version (this class's own prior implementation) turned out not to actually repeat at all
    /// on Android - confirmed live: the held pointer's own gesture tracking appears to starve the
    /// ViewModel-owned DispatcherTimer of ticks for as long as the touch stays down, so nothing
    /// ever fired until release, which then ran the button's own Click (a single +1) - exactly
    /// matching the reported "holding doesn't speed anything up" symptom.</summary>
    public IRelayCommand IncrementAddQuantityCommand { get; }

    public IRelayCommand DecrementAddQuantityCommand { get; }

    /// <summary>By ones below 10, by tens from 10 up to 100, by hundreds beyond that - so a
    /// small nudge near zero stays precise, but reaching a large batch via a held RepeatButton
    /// doesn't take hundreds of individual ticks.</summary>
    private static int NextStep(int currentValue) => currentValue switch
    {
        < 10 => 1,
        < 100 => 10,
        _ => 100,
    };

    public ProductionViewModel(string id, string title, ClientData clientState, SelectionService selection)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        this.selection = selection;

        AddToQueueCommand = new RelayCommand(AddToQueue);
        IncrementAddQuantityCommand = new RelayCommand(() => AddQuantity = Math.Clamp(AddQuantity + NextStep(AddQuantity), 1, 1000));
        DecrementAddQuantityCommand = new RelayCommand(() => AddQuantity = Math.Clamp(AddQuantity - NextStep(AddQuantity), 1, 1000));

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

    private void Refresh(object? selected)
    {
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

    /// <summary>
    /// Updates an existing row in place whenever the queue's own length hasn't changed - see
    /// ProductionItemViewModel's own top comment for why: replacing it (and every other row) with
    /// a brand new instance on every single quantity nudge was destroying the very RepeatButton a
    /// held +/- press depends on to keep repeating, capping every hold at a single tick. A length
    /// change (add/delete) still gets a full rebuild - not something a held +/- press itself ever
    /// causes mid-hold (decrementing to 0 calls DeleteItem, ending that hold on its own already).
    /// </summary>
    private void RebuildQueueRows(Star star)
    {
        int count = star.ManufacturingQueue.Queue.Count;
        Race race = clientState.EmpireState.Race;
        int researchBudget = clientState.EmpireState.ResearchBudget;

        bool canReuse = queue.Count == count && queue is List<ProductionItemViewModel>;
        var rows = canReuse ? (List<ProductionItemViewModel>)queue : new List<ProductionItemViewModel>(count);

        for (int i = 0; i < count; i++)
        {
            int index = i; // captured per-row, not the loop variable
            ProductionOrder order = star.ManufacturingQueue.Queue[i];
            bool canMoveUp = index >= 1;
            bool canMoveDown = index < count - 1;

            // Every row's own estimate depends on everything ahead of it in the queue too (a
            // blocked item stops all funding downstream - docs/behavior-specs-5/
            // production-queue.md §8), so this is recomputed for the whole queue on every
            // rebuild rather than cached per-row.
            ProductionCompletionEstimate estimate = ProductionCompletionEstimator.Estimate(star, index, race, researchBudget);

            if (canReuse)
            {
                rows[index].Update(order, estimate);
            }
            else
            {
                rows.Add(new ProductionItemViewModel(
                    order,
                    estimate,
                    onIncrement: () => AdjustQuantity(index, +1),
                    onDecrement: () => AdjustQuantity(index, -1),
                    onDelete: () => DeleteItem(index),
                    onMoveUp: canMoveUp ? () => SwapQueueItems(index, index - 1) : null,
                    onMoveDown: canMoveDown ? () => SwapQueueItems(index, index + 1) : null,
                    onToggleAutoBuild: () => ToggleAutoBuild(index)));
            }
        }

        if (!canReuse)
        {
            Queue = rows;
        }

        Message = count == 0 ? "Nothing queued." : "";
        HasMessage = count == 0;
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

    /// <summary>
    /// <paramref name="direction"/> is +1 (the "+" RepeatButton) or -1 (the "−" one) - the actual
    /// step size ramps up with the order's own current Quantity (see NextStep's own comment), the
    /// same way IncrementAddQuantityCommand/DecrementAddQuantityCommand do for AddQuantity.
    /// </summary>
    private void AdjustQuantity(int index, int direction)
    {
        if (selectedStar == null)
        {
            return;
        }

        ProductionOrder existing = selectedStar.ManufacturingQueue.Queue[index];
        int newQuantity = existing.Quantity + (direction * NextStep(existing.Quantity));
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
