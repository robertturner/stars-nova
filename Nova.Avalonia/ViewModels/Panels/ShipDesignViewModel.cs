using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Ship Design + Design Manager combined into one panel: a "My Designs" list (with per-row
/// delete, mirroring DesignManager.cs) and a "Create Design" form below it (hull picker, one
/// row per hull slot, live cost/stats, save - mirroring ShipDesignDialog.cs's actual data
/// model, not its pixel-grid drag-and-drop UI - see HullSlotRowViewModel).
///
/// The original never actually wires up CommandMode.Edit for designs anywhere (confirmed by
/// grep - a built design is add-only + delete, matching classic Stars! rules: you make a new
/// design instead of editing an old one), so this panel only supports Add and Delete too.
/// </summary>
public class ShipDesignViewModel : Tool
{
    private readonly ClientData clientState;
    private readonly SelectionService selection;

    private IReadOnlyList<OwnedDesignRowViewModel> ownedDesigns = Array.Empty<OwnedDesignRowViewModel>();

    public IReadOnlyList<OwnedDesignRowViewModel> OwnedDesigns
    {
        get => ownedDesigns;
        private set => SetProperty(ref ownedDesigns, value);
    }

    public IReadOnlyList<HullOptionViewModel> HullOptions { get; }

    private HullOptionViewModel? selectedHull;
    private Component? currentHullComponent;

    public HullOptionViewModel? SelectedHull
    {
        get => selectedHull;
        set
        {
            if (SetProperty(ref selectedHull, value))
            {
                RebuildSlots();
            }
        }
    }

    private IReadOnlyList<HullSlotRowViewModel> slotRows = Array.Empty<HullSlotRowViewModel>();

    public IReadOnlyList<HullSlotRowViewModel> SlotRows
    {
        get => slotRows;
        private set => SetProperty(ref slotRows, value);
    }

    private string designName = "";

    public string DesignName
    {
        get => designName;
        set => SetProperty(ref designName, value);
    }

    private string costSummary = "";

    public string CostSummary
    {
        get => costSummary;
        private set => SetProperty(ref costSummary, value);
    }

    private int massValue;

    public int MassValue
    {
        get => massValue;
        private set => SetProperty(ref massValue, value);
    }

    private int armorValue;

    public int ArmorValue
    {
        get => armorValue;
        private set => SetProperty(ref armorValue, value);
    }

    private int shieldValue;

    public int ShieldValue
    {
        get => shieldValue;
        private set => SetProperty(ref shieldValue, value);
    }

    private int cargoValue;

    public int CargoValue
    {
        get => cargoValue;
        private set => SetProperty(ref cargoValue, value);
    }

    private int fuelValue;

    public int FuelValue
    {
        get => fuelValue;
        private set => SetProperty(ref fuelValue, value);
    }

    private bool hasEngine;

    public bool HasEngine
    {
        get => hasEngine;
        private set => SetProperty(ref hasEngine, value);
    }

    private string saveStatusMessage = "";

    public string SaveStatusMessage
    {
        get => saveStatusMessage;
        private set
        {
            if (SetProperty(ref saveStatusMessage, value))
            {
                HasSaveStatusMessage = !string.IsNullOrEmpty(value);
            }
        }
    }

    private bool hasSaveStatusMessage;

    public bool HasSaveStatusMessage
    {
        get => hasSaveStatusMessage;
        private set => SetProperty(ref hasSaveStatusMessage, value);
    }

    public IRelayCommand SaveCommand { get; }

    public ShipDesignViewModel(string id, string title, ClientData clientState, SelectionService selection)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        this.selection = selection;

        SaveCommand = new RelayCommand(SaveDesign);

        // Hulls are just Components (from components.xml) whose Properties["Hull"] is set -
        // enumerating AvailableComponents (not the raw global AllComponents) means this list
        // is already correctly tech/race-gated, matching RaceComponents.DetermineRaceComponents.
        HullOptions = clientState.EmpireState.AvailableComponents.Values
            .Where(c => c.Properties.ContainsKey("Hull"))
            .Select(c => new HullOptionViewModel(c))
            .OrderBy(h => h.Name)
            .ToList();

        RebuildOwnedDesigns();
        SelectedHull = HullOptions.FirstOrDefault();
    }

    private void RebuildSlots()
    {
        if (SelectedHull == null)
        {
            currentHullComponent = null;
            SlotRows = Array.Empty<HullSlotRowViewModel>();
            DesignName = "";
            RefreshStats();
            return;
        }

        // Always take a fresh copy of the master hull Component before editing it - mirrors
        // ShipDesignDialog.HullList_SelectedValueChanged's own comment ("Ensure we take a copy
        // of the hull design so that we don't end up messing with the master copy"). Component's
        // own copy constructor already deep-clones every property (including Hull, via its own
        // ICloneable.Clone) so hull.Modules here is already a fresh list - no need to re-clone it.
        var hullComponent = new Component(SelectedHull.Component) { Name = SelectedHull.Name };
        var hull = (Hull)hullComponent.Properties["Hull"];

        var rows = new List<HullSlotRowViewModel>();
        foreach (HullModule module in hull.Modules)
        {
            var options = BuildOptionsForSlot(module, hull.Modules, SelectedHull.Name);
            var row = new HullSlotRowViewModel(module, options, RefreshStats);
            // Reflect this module's own (cloned) pre-existing allocation into the picker, if any.
            row.SelectExisting(module.AllocatedComponent);
            rows.Add(row);
        }

        currentHullComponent = hullComponent;
        SlotRows = rows;
        DesignName = SelectedHull.Name;
        RefreshStats();
    }

    /// <summary>
    /// Ports HullGrid.Grid_DragEnter's compatibility checks to plain data logic (no
    /// drag-and-drop needed): a slot's ComponentType string names what fits there, with a few
    /// special cases (a "Weapon" slot also accepts Beam Weapons/Torpedoes; "General Purpose"
    /// accepts anything except Engines; a component with a "Hull Affinity" property only fits
    /// the hull(s) it's flagged for; "Transport Ships Only" components refuse to sit alongside
    /// an allocated weapon anywhere on the hull). That last check only looks at how the other
    /// slots are allocated *right now* (when this hull was selected) rather than staying live
    /// as the user fills in other slots afterward - a simplification given how rarely this
    /// specific interaction comes up.
    /// </summary>
    private List<ComponentOptionViewModel> BuildOptionsForSlot(HullModule slot, List<HullModule> allModulesOnHull, string hullName)
    {
        var options = new List<ComponentOptionViewModel> { new ComponentOptionViewModel(null) };

        foreach (Component component in clientState.EmpireState.AvailableComponents.Values)
        {
            if (IsCompatible(component, slot, hullName, allModulesOnHull))
            {
                options.Add(new ComponentOptionViewModel(component));
            }
        }

        return options;
    }

    private static bool IsCompatible(Component component, HullModule slot, string hullName, IEnumerable<HullModule> allModulesOnHull)
    {
        if (component.Properties.ContainsKey("Hull"))
        {
            return false; // hulls themselves never go in a slot
        }

        bool baseTypeMatches = slot.ComponentType.Contains(component.Type.ToDescription())
            || (slot.ComponentType.Contains("Weapon") && (component.Type == ItemType.BeamWeapons || component.Type == ItemType.Torpedoes))
            || slot.ComponentType == "General Purpose";

        if (!baseTypeMatches)
        {
            return false;
        }

        if (slot.ComponentType == "General Purpose" && component.Type == ItemType.Engine)
        {
            return false;
        }

        if (component.Properties.TryGetValue("Hull Affinity", out ComponentProperty? affinityProperty)
            && affinityProperty is HullAffinity affinity
            && affinity.Value != hullName)
        {
            return false;
        }

        if (component.Properties.ContainsKey("Transport Ships Only"))
        {
            foreach (HullModule otherSlot in allModulesOnHull)
            {
                if (otherSlot.AllocatedComponent != null && otherSlot.AllocatedComponent.Properties.ContainsKey("Weapon"))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void RefreshStats()
    {
        if (currentHullComponent == null)
        {
            CostSummary = "";
            MassValue = 0;
            ArmorValue = 0;
            ShieldValue = 0;
            CargoValue = 0;
            FuelValue = 0;
            HasEngine = false;
            return;
        }

        // A throwaway preview ShipDesign - ShipDesign.Update(race) is the one method that
        // computes real Cost/Mass/Armor/Shield/Cargo/Fuel/Engine from a hull's Blueprint, so
        // reuse it directly instead of re-deriving that summation by hand (which is what
        // ShipDesignDialog.cs itself does, apparently only because it doesn't have a real
        // ShipDesign yet at that point in its own flow - not a constraint we have here).
        var preview = new ShipDesign(0) { Blueprint = currentHullComponent };
        preview.Update(clientState.EmpireState.Race);

        CostSummary = ResourceFormat.Cost(preview.Cost);
        MassValue = preview.Mass;
        ArmorValue = preview.Armor;
        ShieldValue = preview.Shield;
        CargoValue = preview.CargoCapacity;
        FuelValue = preview.IsStarbase ? 0 : preview.FuelCapacity;
        HasEngine = preview.Engine != null;
    }

    private void SaveDesign()
    {
        if (currentHullComponent == null || string.IsNullOrWhiteSpace(DesignName))
        {
            SaveStatusMessage = "Pick a hull and enter a name first.";
            return;
        }

        var hull = (Hull)currentHullComponent.Properties["Hull"];

        var design = new ShipDesign(clientState.EmpireState.GetNextDesignKey())
        {
            Name = DesignName,
            Owner = clientState.EmpireState.Id,
            Blueprint = currentHullComponent,
            // ShipDesign.ToXml dereferences Icon unconditionally, so every saved design needs
            // one even though this panel has no icon picker (deferred per the plan) - default
            // to whatever icon the hull itself would show, matching
            // ShipDesignDialog.UpdateHullFields's own lookup.
            Icon = AllShipIcons.Data.GetIconBySource(currentHullComponent.ImageFile),
        };
        design.Update(clientState.EmpireState.Race);
        design.Type = hull.IsStarbase ? ItemType.Starbase : ItemType.Ship;

        if (!hull.IsStarbase && design.Engine == null)
        {
            SaveStatusMessage = "A ship design must have an engine.";
            return;
        }

        var command = new DesignCommand(CommandMode.Add, design);
        if (!command.IsValid(clientState.EmpireState))
        {
            SaveStatusMessage = "Couldn't save that design - try again.";
            return;
        }

        clientState.Commands.Push(command);
        command.ApplyToState(clientState.EmpireState);

        RebuildOwnedDesigns();
        selection.NotifyMutated();
        SaveStatusMessage = $"Saved \"{design.Name}\".";
    }

    private void RebuildOwnedDesigns()
    {
        OwnedDesigns = clientState.EmpireState.Designs.Values
            .Select(design => new OwnedDesignRowViewModel(design, CountDesignUsage(design.Key), () => DeleteDesign(design)))
            .OrderBy(row => row.Name)
            .ToList();
    }

    private int CountDesignUsage(long designKey)
    {
        int count = 0;
        foreach (Fleet fleet in clientState.EmpireState.OwnedFleets.Values)
        {
            if (fleet.Composition.TryGetValue(designKey, out ShipToken token))
            {
                count += token.Quantity;
            }
        }

        return count;
    }

    /// <summary>
    /// Mirrors DesignManager.Delete_Click: DesignCommand's own ApplyToState cascades the
    /// removal into every fleet using this design (and removes any fleet left with zero ships
    /// as a result) via UpdateFleetCompositions - unlike Split/Merge, there's no extra
    /// RemoveFleet/AddOrUpdateFleet bookkeeping needed here, the command handles it internally.
    /// </summary>
    private void DeleteDesign(ShipDesign design)
    {
        var command = new DesignCommand(CommandMode.Delete, design.Key);
        if (!command.IsValid(clientState.EmpireState))
        {
            SaveStatusMessage = "Couldn't delete that design - try again.";
            return;
        }

        clientState.Commands.Push(command);
        command.ApplyToState(clientState.EmpireState);

        RebuildOwnedDesigns();
        selection.NotifyMutated();
        SaveStatusMessage = $"Deleted \"{design.Name}\".";
    }
}
