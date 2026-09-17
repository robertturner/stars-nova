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

    /// <summary>Press-and-hold on a "My Designs" row shows its full component layout here - same
    /// shared HullViewerView/ViewModel already used from the Star Map, Navigator, and Inspector
    /// (each panel owns its own instance rather than a singleton, matching those).</summary>
    public HullViewerViewModel HullViewer { get; } = new HullViewerViewModel();

    // Pan/zoom for the hull slot grid - same pattern as StarMapDocumentViewModel's own Zoom
    // (ScrollViewer for panning, a bound ScaleTransform for zoom, +/- buttons since a touchscreen
    // never raises a mouse-wheel event at all - see that class's own comment on why the buttons
    // exist alongside the wheel handler, not instead of it).
    public const double MinZoom = 0.5;
    public const double MaxZoom = 3.0;

    private double zoom = 1.0;

    public double Zoom
    {
        get => zoom;
        set => SetProperty(ref zoom, Math.Clamp(value, MinZoom, MaxZoom));
    }

    public IRelayCommand ResetZoomCommand { get; }

    public IRelayCommand ZoomInCommand { get; }

    public IRelayCommand ZoomOutCommand { get; }

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

    public IReadOnlyList<ComponentCategoryViewModel> Categories { get; }

    private ComponentListItemViewModel? armedItem;

    private Component? armedComponent;

    /// <summary>The component a list-item tap last "armed" - the next hull-slot tap places one
    /// of these (see OnSlotTapped). Null means nothing is armed, in which case tapping a filled
    /// slot clears it instead.</summary>
    public Component? ArmedComponent
    {
        get => armedComponent;
        private set
        {
            if (SetProperty(ref armedComponent, value))
            {
                OnPropertyChanged(nameof(HasArmedComponent));
                OnPropertyChanged(nameof(ArmedComponentName));
            }
        }
    }

    public bool HasArmedComponent => armedComponent != null;

    public string ArmedComponentName => armedComponent?.Name ?? "";

    public IRelayCommand<HullSlotRowViewModel> SlotTappedCommand { get; }

    /// <summary>Backs the press-and-hold info popup (see ShipDesignView.axaml.cs's long-press
    /// handling) - one shared instance, repopulated on each ShowInfo call rather than allocated
    /// per component, since only one can ever be showing at a time.</summary>
    public ComponentInfoPopupViewModel InfoPopup { get; } = new ComponentInfoPopupViewModel();

    /// <summary>
    /// Populates and shows InfoPopup for a press-and-held component. BuildDetailLines covers
    /// every Properties key components.xml actually uses (confirmed by grepping every real
    /// &lt;Property&gt;&lt;Type&gt; value in it - some of ShipDesign.SumProperty's own case labels,
    /// e.g. "Driver"/"Robot"/"Movement", turned out to be dead aliases nothing in the data file
    /// ever produces), not just Engine - components.xml's own &lt;Description&gt; text is real prose
    /// for barely 18% of components (Scanners and Armor mostly) so it can't carry this alone.
    /// </summary>
    public void ShowInfo(Component component)
    {
        InfoPopup.Name = component.Name;
        InfoPopup.TypeAndCost = $"{component.Type.ToDescription()} - {ResourceFormat.Cost(component.Cost)}, {component.Mass}kT";
        InfoPopup.Description = component.Description;
        InfoPopup.Icon = component.ComponentImage;
        InfoPopup.DetailLines = BuildDetailLines(component);
        InfoPopup.IsVisible = true;
    }

    public void HideInfo()
    {
        InfoPopup.IsVisible = false;
    }

    private static List<string> BuildDetailLines(Component component)
    {
        var lines = new List<string>();
        var properties = component.Properties;

        if (properties.TryGetValue("Engine", out ComponentProperty? engineProperty) && engineProperty is Engine engine)
        {
            lines.Add($"Optimal speed: Warp {engine.OptimalSpeed}");
            lines.Add($"Fastest safe speed: Warp {engine.FastestSafeSpeed}");
            lines.Add(engine.FreeWarpSpeed > 0 ? $"Free warp up to: Warp {engine.FreeWarpSpeed}" : "No free warp speed");
            lines.Add(engine.RamScoop ? "Ram scoop: refuels in flight" : "No ram scoop");
            for (int warp = 1; warp <= 10; warp++)
            {
                if (engine.FuelConsumption[warp - 1] > 0)
                {
                    lines.Add($"Warp {warp} fuel use: {engine.FuelConsumption[warp - 1]} mg/year/100kT");
                }
            }
        }

        if (properties.TryGetValue("Weapon", out ComponentProperty? weaponProperty) && weaponProperty is Weapon weapon)
        {
            lines.Add($"Type: {WeaponGroupName(weapon.Group)}");
            lines.Add($"Power: {weapon.Power}");
            lines.Add($"Range: {weapon.Range}");
            lines.Add($"Initiative: {weapon.Initiative}");
            lines.Add($"Accuracy: {weapon.Accuracy}%");
        }

        if (properties.TryGetValue("Scanner", out ComponentProperty? scannerProperty) && scannerProperty is Scanner scanner)
        {
            lines.Add($"Normal scan range: {scanner.NormalScan}");
            lines.Add($"Penetrating scan range: {scanner.PenetratingScan}");
        }

        if (properties.TryGetValue("Armor", out ComponentProperty? armorProperty) && armorProperty is IntegerProperty armor)
        {
            lines.Add($"Armor: {armor.Value}");
        }

        if (properties.TryGetValue("Shield", out ComponentProperty? shieldProperty) && shieldProperty is IntegerProperty shield)
        {
            lines.Add($"Shield: {shield.Value}");
        }

        if (properties.TryGetValue("Cargo", out ComponentProperty? cargoProperty) && cargoProperty is IntegerProperty cargo)
        {
            lines.Add($"Cargo capacity: {cargo.Value}kT");
        }

        // Not IntegerProperty like Armor/Shield/Cargo above - a fuel tank's Properties["Fuel"] is
        // the dedicated Fuel class (Capacity + Generation), confirmed via Component.cs's own XML
        // property-type switch.
        if (properties.TryGetValue("Fuel", out ComponentProperty? fuelProperty) && fuelProperty is Fuel fuel)
        {
            lines.Add($"Fuel capacity: {fuel.Capacity}mg");
            if (fuel.Generation > 0)
            {
                lines.Add($"Fuel generation: {fuel.Generation}mg/year");
            }
        }

        if (properties.TryGetValue("Computer", out ComponentProperty? computerProperty) && computerProperty is Computer computer)
        {
            lines.Add($"Initiative: {computer.Initiative}");
            lines.Add($"Accuracy: {computer.Accuracy}%");
        }

        if (properties.TryGetValue("Defense", out ComponentProperty? defenseProperty) && defenseProperty is Defense defense)
        {
            lines.Add($"Defense against invasion: {defense.Value}%");
        }

        if (properties.TryGetValue("Cloak", out ComponentProperty? cloakProperty) && cloakProperty is ProbabilityProperty cloak)
        {
            lines.Add($"Cloaking: {cloak.Value:0.#}%");
        }

        if (properties.TryGetValue("Jammer", out ComponentProperty? jammerProperty) && jammerProperty is ProbabilityProperty jammer)
        {
            lines.Add($"Enemy targeting jammed: {jammer.Value:0.#}%");
        }

        if (properties.TryGetValue("Beam Deflector", out ComponentProperty? deflectorProperty) && deflectorProperty is ProbabilityProperty deflector)
        {
            lines.Add($"Beam damage deflected: {deflector.Value:0.#}%");
        }

        if (properties.TryGetValue("Tachyon Detector", out ComponentProperty? tachyonProperty) && tachyonProperty is ProbabilityProperty tachyon)
        {
            lines.Add($"Enemy cloaking reduced by: {tachyon.Value:0.#}%");
        }

        if (properties.TryGetValue("Capacitor", out ComponentProperty? capacitorProperty) && capacitorProperty is CapacitorProperty capacitor)
        {
            lines.Add($"Beam weapon damage boost: +{capacitor.Value:0.#}%");
        }

        if (properties.TryGetValue("Mass Driver", out ComponentProperty? massDriverProperty) && massDriverProperty is MassDriver massDriver)
        {
            lines.Add($"Packet warp speed: Warp {massDriver.Value}");
        }

        if (properties.TryGetValue("Mine Layer", out ComponentProperty? mineLayerProperty) && mineLayerProperty is MineLayer mineLayer)
        {
            lines.Add($"Lays {mineLayer.LayerRate} mines/year");
            lines.Add($"Hit chance: {mineLayer.HitChance}%");
            lines.Add($"Safe speed through own minefield: Warp {mineLayer.SafeSpeed}");
            lines.Add($"Damage per hit: {mineLayer.DamagePerEngine} (min {mineLayer.MinFleetDamage}/fleet)");
        }

        if (properties.TryGetValue("Mine Layer Efficiency", out ComponentProperty? mineEfficiencyProperty) && mineEfficiencyProperty is DoubleProperty mineEfficiency)
        {
            lines.Add($"Minefield decay reduced by: {mineEfficiency.Value:0.#}%");
        }

        if (properties.TryGetValue("Radiation", out ComponentProperty? radiationProperty) && radiationProperty is Radiation radiation)
        {
            lines.Add($"Radiation shielding: {radiation.Value:0.#}mR");
        }

        if (properties.TryGetValue("Terraforming", out ComponentProperty? terraformProperty) && terraformProperty is Terraform terraform)
        {
            lines.Add($"Max gravity adjustment: ±{terraform.MaxModifiedGravity}");
            lines.Add($"Max temperature adjustment: ±{terraform.MaxModifiedTemperature}");
            lines.Add($"Max radiation adjustment: ±{terraform.MaxModifiedRadiation}");
        }

        if (properties.TryGetValue("Bomb", out ComponentProperty? bombProperty) && bombProperty is Bomb bomb)
        {
            if (bomb.IsSmart)
            {
                lines.Add($"Smart bomb - kills {bomb.PopKill:0.#}% of population (installations safe)");
            }
            else
            {
                lines.Add($"Kills {bomb.PopKill:0.#}% of population (min {bomb.MinimumKill})");
                lines.Add($"Destroys up to {bomb.Installations} installations");
            }
        }

        if (properties.TryGetValue("Colonizer", out ComponentProperty? colonizerProperty) && colonizerProperty is Colonizer colonizer)
        {
            lines.Add(colonizer.Orbital ? "Orbital colonization - no landing required" : "Standard colonization module");
        }

        if (properties.TryGetValue("Gate", out ComponentProperty? gateProperty) && gateProperty is Gate gate)
        {
            lines.Add(gate.SafeHullMass <= 0 ? "Safe for any hull mass" : $"Safe hull mass: up to {gate.SafeHullMass}kT");
            lines.Add(gate.SafeRange <= 0 ? "Unlimited range" : $"Safe range: up to {gate.SafeRange}ly");
        }

        if (properties.TryGetValue("Hull Affinity", out ComponentProperty? hullAffinityProperty) && hullAffinityProperty is HullAffinity hullAffinity
            && !string.IsNullOrEmpty(hullAffinity.Value))
        {
            lines.Add($"Only fits: {hullAffinity.Value}");
        }

        if (properties.TryGetValue("Battle Movement", out ComponentProperty? movementProperty) && movementProperty is DoubleProperty movement)
        {
            lines.Add($"Battle movement bonus: +{movement.Value:0.#}");
        }

        if (properties.TryGetValue("Energy Dampener", out ComponentProperty? dampenerProperty) && dampenerProperty is DoubleProperty dampener)
        {
            lines.Add($"Reduces enemy battle speed by: {dampener.Value:0.#}");
        }

        if (properties.TryGetValue("Orbital Adjuster", out ComponentProperty? orbitalAdjusterProperty) && orbitalAdjusterProperty is IntegerProperty orbitalAdjuster)
        {
            lines.Add($"Orbit adjustment: {orbitalAdjuster.Value}mg/year");
        }

        if (properties.TryGetValue("Mining Robot", out ComponentProperty? miningRobotProperty) && miningRobotProperty is IntegerProperty miningRobot)
        {
            lines.Add($"Mining rate: {miningRobot.Value} kT/year per mineral");
        }

        if (properties.ContainsKey("Transport Ships Only"))
        {
            lines.Add("Restricted to transport-hull ships");
        }

        return lines;
    }

    private static string WeaponGroupName(WeaponType group) => group switch
    {
        WeaponType.standardBeam => "Beam weapon",
        WeaponType.shieldSapper => "Shield sapper",
        WeaponType.gatlingGun => "Gatling gun",
        WeaponType.torpedo => "Torpedo",
        WeaponType.missile => "Missile",
        _ => group.ToString(),
    };

    public ShipDesignViewModel(string id, string title, ClientData clientState, SelectionService selection)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        this.selection = selection;

        SaveCommand = new RelayCommand(SaveDesign);
        SlotTappedCommand = new RelayCommand<HullSlotRowViewModel>(OnSlotTapped);
        ResetZoomCommand = new RelayCommand(() => Zoom = 1.0);
        const double zoomStep = 1.15;
        ZoomInCommand = new RelayCommand(() => Zoom *= zoomStep);
        ZoomOutCommand = new RelayCommand(() => Zoom /= zoomStep);

        // Hulls are just Components (from components.xml) whose Properties["Hull"] is set -
        // enumerating AvailableComponents (not the raw global AllComponents) means this list
        // is already correctly tech/race-gated, matching RaceComponents.DetermineRaceComponents.
        HullOptions = clientState.EmpireState.AvailableComponents.Values
            .Where(c => c.Properties.ContainsKey("Hull"))
            .Select(c => new HullOptionViewModel(c))
            .OrderBy(h => h.Name)
            .ToList();

        // The component list itself doesn't depend on which hull is selected (a hull only
        // changes which of these fit which slot - see BuildOptionsForSlot), so it's built once
        // here rather than rebuilt every time SelectedHull changes - mirrors
        // ShipDesignDialog.PopulateComponentList running once at dialog construction.
        Categories = clientState.EmpireState.AvailableComponents.Values
            .Where(c => !c.Properties.ContainsKey("Hull"))
            .GroupBy(c => c.Type.ToDescription())
            .OrderBy(g => g.Key)
            .Select(g => new ComponentCategoryViewModel(
                g.Key,
                g.OrderBy(c => c.Name).Select(c => new ComponentListItemViewModel(c, ArmComponent)).ToList()))
            .ToList();

        RebuildOwnedDesigns();
        SelectedHull = HullOptions.FirstOrDefault();
    }

    /// <summary>
    /// Arms (or, tapped a second time, disarms) a component from the list - the touch-friendly
    /// stand-in for "picking it up" to drag (see ComponentListItemViewModel's own comment).
    /// Recomputes every slot's IsCompatibleWithArmed highlight so the player can see at a glance
    /// where it would actually fit before tapping a slot.
    /// </summary>
    private void ArmComponent(ComponentListItemViewModel item)
    {
        bool wasArmed = ReferenceEquals(armedItem, item);

        if (armedItem != null)
        {
            armedItem.IsArmed = false;
        }

        armedItem = wasArmed ? null : item;
        ArmedComponent = armedItem?.Component;

        if (armedItem != null)
        {
            armedItem.IsArmed = true;
        }

        RefreshSlotHighlights();
    }

    private void RefreshSlotHighlights()
    {
        foreach (HullSlotRowViewModel row in SlotRows)
        {
            row.IsCompatibleWithArmed = armedComponent != null && row.Options.Any(o => o.Component == armedComponent);
        }
    }

    /// <summary>
    /// A hull slot was tapped (or dropped onto - see ShipDesignView.axaml.cs's drag handlers,
    /// which call this same method): with something armed, place one of it there (rejecting and
    /// reporting via SaveStatusMessage if this slot doesn't accept it); with nothing armed,
    /// clear whatever's already there instead - mirrors HullGrid's own drag-drop/right-click
    /// "Clear Cell" pair as two ends of one tap gesture rather than two separate controls.
    /// </summary>
    private void OnSlotTapped(HullSlotRowViewModel? slot)
    {
        if (slot == null)
        {
            return;
        }

        if (armedComponent != null)
        {
            if (slot.TryPlace(armedComponent, 1))
            {
                SaveStatusMessage = "";
            }
            else
            {
                SaveStatusMessage = $"{armedComponent.Name} doesn't fit there.";
            }
        }
        else if (slot.IsFilled)
        {
            slot.Clear();
        }
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
        RefreshSlotHighlights();
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
