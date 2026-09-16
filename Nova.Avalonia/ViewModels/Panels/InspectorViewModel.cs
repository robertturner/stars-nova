using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using Nova.Common.Components;
using Nova.Common.DataStructures;
using Nova.Common.Waypoints;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Inspector panel: detail for whatever the Navigator (or the Star Map) currently has
/// selected via the shared <see cref="SelectionService"/>. For an owned fleet, also offers
/// order-editing (waypoints, rename) - see the "Fleet orders" region below - mirroring the
/// WinForms FleetDetail/StarMap.DrawFleet pattern of pushing an ICommand onto
/// ClientData.Commands and immediately applying it locally for optimistic UI feedback.
/// </summary>
public class InspectorViewModel : Tool
{
    private readonly ClientData clientState;
    private readonly SelectionService selection;

    /// <summary>
    /// The Production panel, embedded here as a tab (see InspectorView.axaml) rather than
    /// living in its own always-visible region - on Mobile, Production used to sit in a sibling
    /// Grid row that grew with the queue and squeezed this panel down to a sliver; as a tab it
    /// gets its own independent scroll region instead. Null on Desktop, which keeps Production as
    /// its own separate, independently-resizable dock tab (see NovaDockFactory) - nothing there
    /// suffers from Mobile's fixed-row squeeze, so there's no reason to duplicate it as a tab
    /// there too. The "Production" TabItem itself is only visible when this is non-null AND the
    /// selected planet is actually colonized (see IsVisible bindings in the view).
    /// </summary>
    public ProductionViewModel? Production { get; }

    private int selectedTabIndex;

    /// <summary>
    /// Which of the (kind-dependent) tabs is showing - reset to 0 ("Overview") whenever the
    /// selection changes to a genuinely different object (see Refresh), but left alone across a
    /// same-object refresh (e.g. after pushing a waypoint/cargo command) so working through a
    /// multi-step edit on the Orders/Cargo tab doesn't keep bouncing back to Overview.
    /// </summary>
    public int SelectedTabIndex
    {
        get => selectedTabIndex;
        set => SetProperty(ref selectedTabIndex, value);
    }

    private string kind = "";

    public string Kind
    {
        get => kind;
        private set => SetProperty(ref kind, value);
    }

    private string name = "Nothing selected";

    public string Name
    {
        get => name;
        private set => SetProperty(ref name, value);
    }

    private IReadOnlyList<InspectorRow> rows = System.Array.Empty<InspectorRow>();

    public IReadOnlyList<InspectorRow> Rows
    {
        get => rows;
        private set => SetProperty(ref rows, value);
    }

    private IReadOnlyList<RangeBarViewModel> environmentBars = Array.Empty<RangeBarViewModel>();

    /// <summary>Gravity/Temperature/Radiation as colored range bars (current value against this
    /// empire's own tolerance) - only populated for a Star/StarIntel selection, matching the
    /// original client's planet-detail gauges (see PlanetSummary.cs) rather than the plain
    /// numeric Rows above.</summary>
    public IReadOnlyList<RangeBarViewModel> EnvironmentBars
    {
        get => environmentBars;
        private set => SetProperty(ref environmentBars, value);
    }

    private IReadOnlyList<RangeBarViewModel> mineralBars = Array.Empty<RangeBarViewModel>();

    /// <summary>Mineral concentration (0-100%) and current surface stockpile, also as bars -
    /// only populated for an owned Star (a report never reveals mineral data for a planet you
    /// don't own).</summary>
    public IReadOnlyList<RangeBarViewModel> MineralBars
    {
        get => mineralBars;
        private set => SetProperty(ref mineralBars, value);
    }

    private IReadOnlyList<OrbitingFleetRowViewModel> orbitingFleets = Array.Empty<OrbitingFleetRowViewModel>();

    /// <summary>
    /// Every owned fleet sitting at the selected planet (the starbase excluded - it already has
    /// its own "View Starbase" button) - lets a player jump straight to one of them from here,
    /// the same way a star or fleet marker on the map already selects itself, instead of always
    /// having to go by way of the Navigator's Fleets tab. Only populated for an owned Star; see
    /// ShowStar.
    /// </summary>
    public IReadOnlyList<OrbitingFleetRowViewModel> OrbitingFleets
    {
        get => orbitingFleets;
        private set
        {
            SetProperty(ref orbitingFleets, value);
            HasOrbitingFleets = value.Count > 0;
        }
    }

    private bool hasOrbitingFleets;

    public bool HasOrbitingFleets
    {
        get => hasOrbitingFleets;
        private set => SetProperty(ref hasOrbitingFleets, value);
    }

    #region Starbase

    private Fleet? selectedStarbase;

    private bool hasStarbase;

    public bool HasStarbase
    {
        get => hasStarbase;
        private set => SetProperty(ref hasStarbase, value);
    }

    public IRelayCommand ViewStarbaseCommand { get; }

    /// <summary>Opens the shared read-only hull viewer on the starbase's own design - genuinely
    /// new relative to both the original client and this port (research turned up no dialog in
    /// either that ever showed a starbase's actual component layout, only summary stats), unlike
    /// ViewStarbaseCommand above which just re-selects it as a Fleet.</summary>
    public IRelayCommand ViewStarbaseComponentsCommand { get; }

    public HullViewerViewModel HullViewer { get; } = new HullViewerViewModel();

    private void ViewStarbase()
    {
        if (selectedStarbase != null)
        {
            selection.Selected = selectedStarbase;
        }
    }

    /// <summary>
    /// Appends a "Starbase" row (and, if present, a Stargate/Mass Driver capability row for
    /// each) to a planet's Rows - shared between ShowStar and ShowStarReport. Also updates
    /// HasStarbase/selectedStarbase/ViewStarbaseCommand so the view's "View Starbase" button
    /// only enables once there's a REAL Fleet to jump to - an unresolved report placeholder
    /// (see EmpireData.LinkReferences' own comment on this exact gap) has an empty Composition,
    /// so Design is never available to drill into anyway.
    /// </summary>
    private void AddStarbaseRows(List<InspectorRow> rowList, Fleet? starbase)
    {
        selectedStarbase = starbase;
        HasStarbase = starbase != null;
        ViewStarbaseCommand.NotifyCanExecuteChanged();
        ViewStarbaseComponentsCommand.NotifyCanExecuteChanged();

        if (starbase == null)
        {
            rowList.Add(new InspectorRow("Starbase", "None"));
            return;
        }

        ShipDesign? design = starbase.Composition.Values.FirstOrDefault()?.Design;
        design?.Update();

        rowList.Add(new InspectorRow("Starbase", design?.Name ?? "Present"));

        if (design != null && design.Summary.Properties.TryGetValue("Gate", out ComponentProperty gateProperty) && gateProperty is Gate gate)
        {
            rowList.Add(new InspectorRow("  Stargate", $"{gate.SafeHullMass:0}/{gate.SafeRange:0}"));
        }

        if (design != null && design.Summary.Properties.TryGetValue("Mass Driver", out ComponentProperty driverProperty) && driverProperty is MassDriver driver)
        {
            rowList.Add(new InspectorRow("  Mass Driver", $"Level {driver.Value}"));
        }
    }

    #endregion

    #region Fleet orders

    // Populated only while an owned Fleet (not a bare FleetIntel report) is selected - see
    // ShowFleet/Refresh. The view binds its whole "orders" section's IsVisible to
    // IsFleetSelected.
    private Fleet? selectedFleet;

    private bool isFleetSelected;

    public bool IsFleetSelected
    {
        get => isFleetSelected;
        private set => SetProperty(ref isFleetSelected, value);
    }

    private IReadOnlyList<FleetWaypointRowViewModel> waypointRows = Array.Empty<FleetWaypointRowViewModel>();

    public IReadOnlyList<FleetWaypointRowViewModel> WaypointRows
    {
        get => waypointRows;
        private set => SetProperty(ref waypointRows, value);
    }

    /// <summary>Every known star's name, for the "Add Waypoint" destination picker.</summary>
    public IReadOnlyList<string> DestinationOptions { get; }

    /// <summary>
    /// The waypoint task types cheap to support without their own dedicated UI (no extra
    /// parameters beyond the task itself) - CargoTask (needs an amount/resource picker) and
    /// SplitMergeTask (needs a composition editor) are deferred to their own phases.
    /// </summary>
    public IReadOnlyList<string> TaskOptions { get; } = new[] { "None", "Colonise", "Scrap", "Lay Mines", "Invade" };

    private string? newWaypointDestination;

    public string? NewWaypointDestination
    {
        get => newWaypointDestination;
        set => SetProperty(ref newWaypointDestination, value);
    }

    private int newWaypointWarp = 6;

    public int NewWaypointWarp
    {
        get => newWaypointWarp;
        set => SetProperty(ref newWaypointWarp, value);
    }

    private string newWaypointTask = "None";

    public string NewWaypointTask
    {
        get => newWaypointTask;
        set => SetProperty(ref newWaypointTask, value);
    }

    public IRelayCommand AddWaypointCommand { get; }

    // -1 = nothing selected. Tracks the real Fleet.Waypoints index (see FleetWaypointRowViewModel.
    // Index) so a change of Task can be pushed for exactly that waypoint, and so "Add Waypoint"
    // knows to insert in front of it rather than append - both per the user's own spec. Paired
    // with selectedWaypointFleetKey so a genuine switch to a DIFFERENT fleet clears the selection
    // (a waypoint index from one fleet means nothing for another) while a same-fleet refresh
    // (e.g. after pushing a command) preserves it - see ShowFleet's own use of both.
    private int selectedWaypointIndex = -1;

    private long? selectedWaypointFleetKey;

    private bool hasSelectedWaypoint;

    public bool HasSelectedWaypoint
    {
        get => hasSelectedWaypoint;
        private set => SetProperty(ref hasSelectedWaypoint, value);
    }

    private bool suppressWaypointTaskChange;

    private string selectedWaypointTaskOption = "None";

    /// <summary>The Task of whichever waypoint row is currently selected - a separate property
    /// from NewWaypointTask (which is for the waypoint about to be added) so changing an
    /// ALREADY-added waypoint's task doesn't disturb whatever's queued up to add next. Setting
    /// this immediately pushes a WaypointCommand.Edit, mirroring FleetDetail.WaypointTaskChanged's
    /// own immediate-apply behavior.</summary>
    public string SelectedWaypointTaskOption
    {
        get => selectedWaypointTaskOption;
        set
        {
            if (SetProperty(ref selectedWaypointTaskOption, value) && !suppressWaypointTaskChange)
            {
                ApplySelectedWaypointTask();
            }
        }
    }

    private bool suppressWaypointWarpChange;

    private int selectedWaypointWarp;

    /// <summary>The WarpFactor of whichever waypoint row is currently selected - same shape and
    /// immediate-apply behavior as <see cref="SelectedWaypointTaskOption"/>, for the same reason:
    /// before this, an already-queued waypoint's speed could only be set once, at creation time
    /// (NewWaypointWarp), with no way to speed up or slow down a leg after the fact short of
    /// deleting and re-adding it.</summary>
    public int SelectedWaypointWarp
    {
        get => selectedWaypointWarp;
        set
        {
            if (SetProperty(ref selectedWaypointWarp, value) && !suppressWaypointWarpChange)
            {
                ApplySelectedWaypointWarp();
            }
        }
    }

    public IRelayCommand ArmMapWaypointCommand { get; }

    public IRelayCommand CancelMapWaypointCommand { get; }

    /// <summary>Mirrors SelectionService.IsAddingWaypoint - see OnSelectionChanged. The Star Map
    /// shows a "tap a planet" banner while this is true; the button here that arms it flips to a
    /// "Cancel" label the same way.</summary>
    public bool IsAddingWaypoint => selection.IsAddingWaypoint;

    private string newFleetName = "";

    public string NewFleetName
    {
        get => newFleetName;
        set => SetProperty(ref newFleetName, value);
    }

    public IRelayCommand SubmitRenameCommand { get; }

    #endregion

    #region Cargo transfer

    // Populated only when the selected fleet is orbiting one of its own empire's stars -
    // matches CargoDialog.SetTarget's own gate (CargoTask.IsValid rejects any non-Star
    // target outright, so there's nothing valid to build if this fleet isn't in orbit).
    private bool canTransferCargo;

    public bool CanTransferCargo
    {
        get => canTransferCargo;
        private set
        {
            if (SetProperty(ref canTransferCargo, value))
            {
                OnPropertyChanged(nameof(HasCargoOptions));
            }
        }
    }

    /// <summary>Whether the "Cargo" tab has anything to show at all - either transfer mode has
    /// its own more specific gate (see CanTransferCargo/CanTransferCargoToFleet's own comments),
    /// but the tab itself should disappear entirely rather than show an empty pane when neither
    /// applies (a fleet that's not in orbit and has no other fleet sharing its position).</summary>
    public bool HasCargoOptions => CanTransferCargo || CanTransferCargoToFleet;

    private IReadOnlyList<CargoResourceRowViewModel> cargoRows = Array.Empty<CargoResourceRowViewModel>();

    public IReadOnlyList<CargoResourceRowViewModel> CargoRows
    {
        get => cargoRows;
        private set => SetProperty(ref cargoRows, value);
    }

    private int cargoCapacity;

    public int CargoCapacity
    {
        get => cargoCapacity;
        private set => SetProperty(ref cargoCapacity, value);
    }

    private string cargoStatusMessage = "";

    public string CargoStatusMessage
    {
        get => cargoStatusMessage;
        private set
        {
            if (SetProperty(ref cargoStatusMessage, value))
            {
                HasCargoStatusMessage = !string.IsNullOrEmpty(value);
            }
        }
    }

    private bool hasCargoStatusMessage;

    public bool HasCargoStatusMessage
    {
        get => hasCargoStatusMessage;
        private set => SetProperty(ref hasCargoStatusMessage, value);
    }

    public IRelayCommand ApplyCargoCommand { get; }

    #endregion

    #region Cargo transfer to another fleet

    // Ports CargoTransferDialog.cs (fleet-to-fleet, as opposed to the fleet-to-planet transfer
    // above) - available whenever another of this empire's own, non-starbase fleets shares this
    // fleet's position. Like the WinForms original, this mutates both fleets' live state
    // directly with no ICommand/queued order involved (the original dialog does the same - see
    // FleetDetail.ButtonCargoXfer_Click) rather than introducing a new command type unasked-for.
    private bool canTransferCargoToFleet;

    public bool CanTransferCargoToFleet
    {
        get => canTransferCargoToFleet;
        private set
        {
            if (SetProperty(ref canTransferCargoToFleet, value))
            {
                OnPropertyChanged(nameof(HasCargoOptions));
            }
        }
    }

    /// <summary>Every other owned, non-starbase fleet at this fleet's position - same
    /// population rule as SplitMergeTargets, minus the "New Fleet" placeholder (a transfer
    /// needs a real other fleet to move cargo to/from).</summary>
    private IReadOnlyList<SplitMergeTargetOption> fleetCargoTransferTargets = Array.Empty<SplitMergeTargetOption>();

    public IReadOnlyList<SplitMergeTargetOption> FleetCargoTransferTargets
    {
        get => fleetCargoTransferTargets;
        private set => SetProperty(ref fleetCargoTransferTargets, value);
    }

    private SplitMergeTargetOption? selectedFleetCargoTransferTarget;

    public SplitMergeTargetOption? SelectedFleetCargoTransferTarget
    {
        get => selectedFleetCargoTransferTarget;
        set
        {
            if (SetProperty(ref selectedFleetCargoTransferTarget, value))
            {
                RebuildFleetTransferRows();
            }
        }
    }

    /// <summary>Ironium/Boranium/Germanium/Colonists rows - reuses CargoResourceRowViewModel
    /// (its "Total"/"FleetAmount"/"PlanetAmount" naming was written for the fleet-vs-planet
    /// case, but the shape - a conserved total split between two sides via one slider - is
    /// exactly what a fleet-vs-fleet transfer needs too; "PlanetAmount" here just means "the
    /// other fleet's amount").</summary>
    private IReadOnlyList<CargoResourceRowViewModel> fleetTransferRows = Array.Empty<CargoResourceRowViewModel>();

    public IReadOnlyList<CargoResourceRowViewModel> FleetTransferRows
    {
        get => fleetTransferRows;
        private set => SetProperty(ref fleetTransferRows, value);
    }

    private CargoResourceRowViewModel? fleetTransferFuelRow;

    public CargoResourceRowViewModel? FleetTransferFuelRow
    {
        get => fleetTransferFuelRow;
        private set => SetProperty(ref fleetTransferFuelRow, value);
    }

    private string fleetTransferStatusMessage = "";

    public string FleetTransferStatusMessage
    {
        get => fleetTransferStatusMessage;
        private set
        {
            if (SetProperty(ref fleetTransferStatusMessage, value))
            {
                HasFleetTransferStatusMessage = !string.IsNullOrEmpty(value);
            }
        }
    }

    private bool hasFleetTransferStatusMessage;

    public bool HasFleetTransferStatusMessage
    {
        get => hasFleetTransferStatusMessage;
        private set => SetProperty(ref hasFleetTransferStatusMessage, value);
    }

    public IRelayCommand ApplyFleetCargoTransferCommand { get; }

    #endregion

    #region Split / Merge

    private bool canSplitMerge;

    public bool CanSplitMerge
    {
        get => canSplitMerge;
        private set => SetProperty(ref canSplitMerge, value);
    }

    private IReadOnlyList<SplitMergeRowViewModel> splitMergeRows = Array.Empty<SplitMergeRowViewModel>();

    public IReadOnlyList<SplitMergeRowViewModel> SplitMergeRows
    {
        get => splitMergeRows;
        private set => SetProperty(ref splitMergeRows, value);
    }

    /// <summary>"New Fleet" plus every other owned, non-starbase fleet at this fleet's
    /// position - matches FleetDetail.cs's comboOtherFleets population exactly.</summary>
    private IReadOnlyList<SplitMergeTargetOption> splitMergeTargets = Array.Empty<SplitMergeTargetOption>();

    public IReadOnlyList<SplitMergeTargetOption> SplitMergeTargets
    {
        get => splitMergeTargets;
        private set => SetProperty(ref splitMergeTargets, value);
    }

    private SplitMergeTargetOption? selectedSplitMergeTarget;

    public SplitMergeTargetOption? SelectedSplitMergeTarget
    {
        get => selectedSplitMergeTarget;
        set => SetProperty(ref selectedSplitMergeTarget, value);
    }

    private string splitMergeStatusMessage = "";

    public string SplitMergeStatusMessage
    {
        get => splitMergeStatusMessage;
        private set
        {
            if (SetProperty(ref splitMergeStatusMessage, value))
            {
                HasSplitMergeStatusMessage = !string.IsNullOrEmpty(value);
            }
        }
    }

    private bool hasSplitMergeStatusMessage;

    public bool HasSplitMergeStatusMessage
    {
        get => hasSplitMergeStatusMessage;
        private set => SetProperty(ref hasSplitMergeStatusMessage, value);
    }

    public IRelayCommand ApplySplitMergeCommand { get; }

    #endregion

    public InspectorViewModel(string id, string title, ClientData clientState, SelectionService selection, ProductionViewModel? production = null)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        this.selection = selection;
        Production = production;

        DestinationOptions = clientState.EmpireState.StarReports.Keys.OrderBy(n => n).ToList();
        AddWaypointCommand = new RelayCommand(AddWaypoint);
        SubmitRenameCommand = new RelayCommand(SubmitRename);
        ApplyCargoCommand = new RelayCommand(ApplyCargoTransfer);
        ApplyFleetCargoTransferCommand = new RelayCommand(ApplyFleetCargoTransfer);
        ApplySplitMergeCommand = new RelayCommand(ApplySplitMerge);
        ViewStarbaseCommand = new RelayCommand(ViewStarbase, () => selectedStarbase != null && selectedStarbase.Composition.Count > 0);
        ViewStarbaseComponentsCommand = new RelayCommand(
            () => HullViewer.Show(selectedStarbase?.Composition.Values.FirstOrDefault()?.Design),
            () => selectedStarbase != null && selectedStarbase.Composition.Count > 0);
        ArmMapWaypointCommand = new RelayCommand(ArmMapWaypoint, () => selectedFleet != null);
        CancelMapWaypointCommand = new RelayCommand(() => selection.CancelWaypointTarget());

        selection.PropertyChanged += OnSelectionChanged;
        Refresh(selection.Selected);
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionService.Selected))
        {
            Refresh((sender as SelectionService)?.Selected);
        }
        else if (e.PropertyName == nameof(SelectionService.IsAddingWaypoint))
        {
            OnPropertyChanged(nameof(IsAddingWaypoint));
        }
    }

    /// <summary>
    /// Arms map-tap targeting for the currently selected fleet - the picked star just becomes
    /// NewWaypointDestination and funnels into the exact same AddWaypoint() the dropdown-based
    /// "Add Waypoint" button already uses (Warp/Task/insert-position all still apply), so the
    /// two entry points share every bit of validation and insertion logic.
    /// </summary>
    private void ArmMapWaypoint()
    {
        if (selectedFleet == null)
        {
            return;
        }

        selection.ArmWaypointTarget(starName =>
        {
            NewWaypointDestination = starName;
            AddWaypoint();
        });
    }

    private object? lastSelectionForTabReset;

    private void Refresh(object? selected)
    {
        // A genuinely different selected object (a different fleet, a different planet, or
        // switching kind entirely) resets to the "Overview" tab - a same-object re-Refresh (e.g.
        // after pushing a waypoint/cargo command, which calls ShowFleet directly and then
        // NotifyMutated's echo brings us back through here with the identical reference) leaves
        // whichever tab was open alone, so an in-progress multi-step edit doesn't keep getting
        // bounced back to Overview.
        if (!ReferenceEquals(selected, lastSelectionForTabReset))
        {
            SelectedTabIndex = 0;
            lastSelectionForTabReset = selected;
        }

        if (selected is not Fleet)
        {
            selectedFleet = null;
            IsFleetSelected = false;
            ArmMapWaypointCommand.NotifyCanExecuteChanged();

            // Cargo/Split-Merge are all fleet-specific - without this, selecting a star (or
            // anything else) right after a fleet left its Cargo/Split-Merge tabs showing that
            // fleet's rows/targets/status message untouched, since ShowStar/ShowStarReport/
            // ShowMinefield never populate any of these themselves (only ShowFleet does).
            CanTransferCargo = false;
            CargoRows = Array.Empty<CargoResourceRowViewModel>();
            CargoCapacity = 0;
            CargoStatusMessage = "";
            CanTransferCargoToFleet = false;
            FleetCargoTransferTargets = Array.Empty<SplitMergeTargetOption>();
            SelectedFleetCargoTransferTarget = null;
            FleetTransferStatusMessage = "";
            CanSplitMerge = false;
            SplitMergeRows = Array.Empty<SplitMergeRowViewModel>();
            SplitMergeTargets = Array.Empty<SplitMergeTargetOption>();
            SelectedSplitMergeTarget = null;
            SplitMergeStatusMessage = "";
        }

        // ShowStar/ShowStarReport (below) populate these themselves - reset here so a Fleet/
        // Minefield/nothing selection doesn't keep showing the previously-viewed planet's bars
        // and "View Starbase" button.
        if (selected is not Star && selected is not StarIntel)
        {
            EnvironmentBars = Array.Empty<RangeBarViewModel>();
            MineralBars = Array.Empty<RangeBarViewModel>();
            selectedStarbase = null;
            HasStarbase = false;
            ViewStarbaseCommand.NotifyCanExecuteChanged();
            ViewStarbaseComponentsCommand.NotifyCanExecuteChanged();
        }

        // Both ShowStar and ShowStarReport (below) populate this for this empire's own fleets in
        // orbit there - a scout can easily be sitting at a neutral/unowned/unexplored star, which
        // only ever has a StarIntel report (never a real Star), and still needs to be selectable
        // from here. Reset for every other kind of selection.
        if (selected is not Star && selected is not StarIntel)
        {
            OrbitingFleets = Array.Empty<OrbitingFleetRowViewModel>();
        }

        switch (selected)
        {
            case Star star:
                ShowStar(star);
                break;
            case Fleet fleet:
                ShowFleet(fleet);
                break;
            case StarIntel report:
                ShowStarReport(report);
                break;
            case Minefield minefield:
                ShowMinefield(minefield);
                break;
            default:
                Kind = "";
                Name = "Nothing selected";
                Rows = System.Array.Empty<InspectorRow>();
                break;
        }
    }

    /// <summary>
    /// A star clicked on the map that isn't one of this empire's own - all we know is
    /// whatever's in the report (possibly none of it, if it's never been scanned).
    /// </summary>
    private void ShowStarReport(StarIntel report)
    {
        Kind = "Planet (report)";
        Name = report.Name;

        // Matches the "own fleet in orbit" ring's own gate (StarMapDocumentViewModel) - a scout
        // can easily be sitting at a neutral/unowned/unexplored star, which never has a real Star
        // object for us, only this report, and still needs to be selectable here. Matched by name
        // rather than reference, since a fleet's own InOrbit may point at this very report object
        // (see EmpireData.LinkReferences) or at a placeholder, never at something we could compare
        // by identity against a StarIntel we don't own.
        OrbitingFleets = clientState.EmpireState.OwnedFleets.Values
            .Where(fleet => fleet.InOrbit != null && fleet.InOrbit.Name == report.Name)
            .Select(fleet => new OrbitingFleetRowViewModel(fleet.Name, () => selection.Selected = fleet))
            .ToList();

        if (report.Year == Global.Unset)
        {
            Rows = new List<InspectorRow> { new InspectorRow("Status", "Unexplored") };
            EnvironmentBars = Array.Empty<RangeBarViewModel>();
            MineralBars = Array.Empty<RangeBarViewModel>();
            selectedStarbase = null;
            HasStarbase = false;
            ViewStarbaseCommand.NotifyCanExecuteChanged();
            ViewStarbaseComponentsCommand.NotifyCanExecuteChanged();
            return;
        }

        string owner = report.Owner == Global.Nobody
            ? "Unowned"
            : report.Owner == clientState.EmpireState.Id
                ? "You"
                : $"Empire #{report.Owner}";

        var rowList = new List<InspectorRow>
        {
            new InspectorRow("Report age", report.Year == clientState.EmpireState.TurnYear
                ? "Current"
                : $"{clientState.EmpireState.TurnYear - report.Year} year(s) old"),
            new InspectorRow("Owner", owner),
            new InspectorRow("Population", report.Owner == Global.Nobody ? "Uninhabited" : $"{report.Colonists:N0}"),
            new InspectorRow("Habitability", $"{clientState.EmpireState.Race.HabitalValue(report) * 100.0:0}%"),
        };

        AddStarbaseRows(rowList, report.Starbase);
        Rows = rowList;

        Race race = clientState.EmpireState.Race;
        EnvironmentBars = new List<RangeBarViewModel>
        {
            RangeBarViewModel.ForEnvironment("Gravity", report.Gravity, race.GravityTolerance.MinimumValue, race.GravityTolerance.MaximumValue, race.GravityTolerance.Immune, Gravity.FormatWithUnit(report.Gravity)),
            RangeBarViewModel.ForEnvironment("Temperature", report.Temperature, race.TemperatureTolerance.MinimumValue, race.TemperatureTolerance.MaximumValue, race.TemperatureTolerance.Immune, Temperature.FormatWithUnit(report.Temperature)),
            RangeBarViewModel.ForEnvironment("Radiation", report.Radiation, race.RadiationTolerance.MinimumValue, race.RadiationTolerance.MaximumValue, race.RadiationTolerance.Immune, $"{report.Radiation}mR"),
        };

        // Concentration (0-100%, how mineral-rich the ground is) IS revealed for a star we don't
        // own - StarIntel.Update sets it the moment a fleet is merely in orbit (ScanLevel.InPlace,
        // no scanners even needed), the exact same gate Gravity/Temperature/Radiation above are
        // already set under, on the exact same report - so if we've gotten this far (past the
        // Year == Unset return above), it's already there to show. Only the SURFACE STOCKPILE
        // (ResourcesOnHand - how much is actually sitting mined-and-ready) genuinely never reaches
        // a report at any scan level (see StarIntel.Update's own logic) - that half of ShowStar's
        // own mineral bars stays owned-only.
        MineralBars = new List<RangeBarViewModel>
        {
            RangeBarViewModel.ForMineral("Ironium concentration", report.MineralConcentration.Ironium, 100, $"{report.MineralConcentration.Ironium}%", Brushes.IndianRed),
            RangeBarViewModel.ForMineral("Boranium concentration", report.MineralConcentration.Boranium, 100, $"{report.MineralConcentration.Boranium}%", Brushes.YellowGreen),
            RangeBarViewModel.ForMineral("Germanium concentration", report.MineralConcentration.Germanium, 100, $"{report.MineralConcentration.Germanium}%", Brushes.SteelBlue),
        };
    }

    private void ShowStar(Star star)
    {
        Kind = "Planet";
        Name = star.Name;

        Race race = clientState.EmpireState.Race;
        double habitability = clientState.EmpireState.StarReports.TryGetValue(star.Name, out StarIntel report)
            ? race.HabitalValue(report) * 100.0
            : 0.0;

        var rowList = new List<InspectorRow>
        {
            new InspectorRow("Population", $"{star.Colonists:N0}"),
            new InspectorRow("Habitability", $"{habitability:0}%"),
            // "built / population-operable-cap" (docs/behavior-specs-5/production-queue.md §3) -
            // a star can physically hold more of either than its current population can actually
            // run (Star.GetOperableMines/Factories), so the built count alone doesn't say whether
            // there's room to keep growing into more, or whether it's already population-capped.
            new InspectorRow("Mines", $"{star.Mines} / {star.GetOperableMines()}"),
            new InspectorRow("Factories", $"{star.Factories} / {star.GetOperableFactories()}"),
            new InspectorRow("Defenses", star.DefenseType),
        };

        AddStarbaseRows(rowList, star.Starbase);
        Rows = rowList;

        OrbitingFleets = clientState.EmpireState.OwnedFleets.Values
            .Where(fleet => fleet.InOrbit == star && fleet != star.Starbase)
            .Select(fleet => new OrbitingFleetRowViewModel(fleet.Name, () => selection.Selected = fleet))
            .ToList();

        EnvironmentBars = new List<RangeBarViewModel>
        {
            RangeBarViewModel.ForEnvironment("Gravity", star.Gravity, race.GravityTolerance.MinimumValue, race.GravityTolerance.MaximumValue, race.GravityTolerance.Immune, Gravity.FormatWithUnit(star.Gravity)),
            RangeBarViewModel.ForEnvironment("Temperature", star.Temperature, race.TemperatureTolerance.MinimumValue, race.TemperatureTolerance.MaximumValue, race.TemperatureTolerance.Immune, Temperature.FormatWithUnit(star.Temperature)),
            RangeBarViewModel.ForEnvironment("Radiation", star.Radiation, race.RadiationTolerance.MinimumValue, race.RadiationTolerance.MaximumValue, race.RadiationTolerance.Immune, $"{star.Radiation}mR"),
        };

        // "Mineral density" (concentration, 0-100%) wasn't shown anywhere in the Inspector before
        // this - only the surface stockpile was. Both are now bars: concentration colored per
        // mineral, capped at its own natural 100% maximum; surface stockpile against an assumed
        // 1000kT "visually full" ceiling, since stockpiles have no fixed real maximum to scale
        // against.
        const double surfaceStockpileVisualMax = 1000;
        MineralBars = new List<RangeBarViewModel>
        {
            RangeBarViewModel.ForMineral("Ironium concentration", star.MineralConcentration.Ironium, 100, $"{star.MineralConcentration.Ironium}%", Brushes.IndianRed),
            RangeBarViewModel.ForMineral("Boranium concentration", star.MineralConcentration.Boranium, 100, $"{star.MineralConcentration.Boranium}%", Brushes.YellowGreen),
            RangeBarViewModel.ForMineral("Germanium concentration", star.MineralConcentration.Germanium, 100, $"{star.MineralConcentration.Germanium}%", Brushes.SteelBlue),
            RangeBarViewModel.ForMineral("Ironium (surface)", star.ResourcesOnHand.Ironium, surfaceStockpileVisualMax, $"{star.ResourcesOnHand.Ironium}kT", Brushes.IndianRed),
            RangeBarViewModel.ForMineral("Boranium (surface)", star.ResourcesOnHand.Boranium, surfaceStockpileVisualMax, $"{star.ResourcesOnHand.Boranium}kT", Brushes.YellowGreen),
            RangeBarViewModel.ForMineral("Germanium (surface)", star.ResourcesOnHand.Germanium, surfaceStockpileVisualMax, $"{star.ResourcesOnHand.Germanium}kT", Brushes.SteelBlue),
        };
    }

    /// <summary>
    /// Read-only, like the WinForms MinefieldInspector - nothing lets a player directly edit an
    /// existing minefield's own stats (only lay/detonate whole fields, a fleet order). Shown
    /// regardless of ownership - the map only offers a minefield as selectable at all once it's
    /// already visible (owned, or within a scanner's range), same rule StarMap.cs's
    /// DetermineVisibleMinefields applies.
    /// </summary>
    private void ShowMinefield(Minefield minefield)
    {
        Kind = "Minefield";
        Name = string.IsNullOrEmpty(minefield.Name) ? $"Minefield #{minefield.Key:X}" : minefield.Name;

        string owner = minefield.Owner == clientState.EmpireState.Id ? "You" : $"Empire #{minefield.Owner}";

        Rows = new List<InspectorRow>
        {
            new InspectorRow("Owner", owner),
            new InspectorRow("Position", minefield.Position.ToString()),
            new InspectorRow("Radius", $"{minefield.Radius}"),
            new InspectorRow("Number of mines", $"{minefield.NumberOfMines}"),
            new InspectorRow("Safe speed", $"Warp {minefield.SafeSpeed}"),
        };
    }

    private void ShowFleet(Fleet fleet)
    {
        Kind = "Fleet";
        Name = fleet.Name;
        selectedFleet = fleet;
        IsFleetSelected = true;
        ArmMapWaypointCommand.NotifyCanExecuteChanged();

        // A genuine switch to a different fleet clears the waypoint selection (an index into
        // THIS fleet's Waypoints means nothing for another); a same-fleet refresh (every other
        // call site re-invokes ShowFleet after pushing a command) preserves it, so editing a
        // waypoint's Task or watching Move Up/Down keeps that row highlighted instead of
        // silently dropping the selection on every edit.
        if (selectedWaypointFleetKey != fleet.Key)
        {
            selectedWaypointIndex = -1;
            selectedWaypointFleetKey = fleet.Key;
        }

        var rowList = new List<InspectorRow>
        {
            new InspectorRow("Fuel", $"{fleet.FuelAvailable:0}/{fleet.TotalFuelCapacity}"),
        };

        // Always shown (even at 0 used) so the fleet's hold size itself is visible without
        // opening the Cargo tab - same "used/capacity" shape as the Fuel row just above, which
        // already answers "what's used" as a total; the per-resource rows below break that total
        // down by Ironium/Boranium/Germanium/Colonists whenever any of them is actually nonzero.
        if (fleet.TotalCargoCapacity > 0)
        {
            rowList.Add(new InspectorRow("Cargo", $"{fleet.Cargo.Mass}/{fleet.TotalCargoCapacity}kT"));
        }

        if (fleet.InOrbit != null)
        {
            rowList.Add(new InspectorRow("Orbiting", fleet.InOrbit.Name));
        }

        // Waypoints[0] is always the fleet's current position/task; further waypoints (if
        // any) are where it's actually headed - see FleetDetail.DisplayLegDetails.
        Waypoint current = fleet.Waypoints[0];
        rowList.Add(new InspectorRow("Waypoint", current.Destination));
        rowList.Add(new InspectorRow("Task", current.Task?.Name ?? "None"));
        rowList.Add(new InspectorRow("Warp", fleet.Waypoints.Count > 1 ? $"{fleet.Waypoints[1].WarpFactor}" : "0"));

        foreach ((string label, int amount) in new (string, int)[]
        {
            ("Ironium (cargo)", fleet.Cargo.Ironium),
            ("Boranium (cargo)", fleet.Cargo.Boranium),
            ("Germanium (cargo)", fleet.Cargo.Germanium),
            ("Colonists (cargo)", fleet.Cargo.ColonistsInKilotons),
        })
        {
            if (amount > 0)
            {
                rowList.Add(new InspectorRow(label, $"{amount}kT"));
            }
        }

        Rows = rowList;

        // Waypoints beyond index 0 (the current position) are the ones an order can change.
        // Fuel-upon-arrival and years-until-arrival are both tracked cumulatively leg by leg,
        // starting from the fleet's actual current fuel/position (Waypoints[0]) - same per-leg
        // formula as FleetDetail's own "leg fuel" panel in the WinForms original (see
        // FleetWaypointRowViewModel.FuelUponArrival/YearsUntilArrival).
        var editableRows = new List<FleetWaypointRowViewModel>();
        double runningFuel = fleet.FuelAvailable;
        double runningYears = 0;
        NovaPoint previousPosition = fleet.Waypoints[0].Position;
        Race race = clientState.EmpireState.Race;
        for (int i = 1; i < fleet.Waypoints.Count; i++)
        {
            int index = i; // capture for the closures below
            bool canMoveUp = index >= 2; // index 1 moving up would swap into the immovable index 0
            bool canMoveDown = index < fleet.Waypoints.Count - 1;

            Waypoint waypoint = fleet.Waypoints[i];
            if (waypoint.WarpFactor > 0)
            {
                double distance = PointUtilities.Distance(previousPosition, waypoint.Position);
                double time = distance / (waypoint.WarpFactor * waypoint.WarpFactor);
                runningFuel -= fleet.FuelConsumption(waypoint.WarpFactor, race) * time;
                runningYears += time;
            }
            previousPosition = waypoint.Position;

            editableRows.Add(new FleetWaypointRowViewModel(
                index,
                waypoint,
                runningFuel,
                runningYears,
                onDelete: () => DeleteWaypoint(index),
                onMoveUp: canMoveUp ? () => MoveWaypoint(index, index - 1) : null,
                onMoveDown: canMoveDown ? () => MoveWaypoint(index, index + 1) : null,
                onSelect: () => SelectWaypoint(index)));
        }

        WaypointRows = editableRows;
        ApplyWaypointSelectionState();
        NewFleetName = fleet.Name;
        NewWaypointDestination = DestinationOptions.FirstOrDefault();
        NewWaypointWarp = 6;
        NewWaypointTask = "None";

        if (fleet.InOrbit is Star orbitStar)
        {
            CanTransferCargo = true;
            CargoCapacity = fleet.TotalCargoCapacity;

            // targetCapacity is left null for every row - a planet's stockpile isn't bounded by
            // anything this transfer needs to enforce, only the fleet's own hold size is (see
            // CargoResourceRowViewModel's own comment on why the slider range is now capacity-
            // aware instead of just clamped to each resource's own conserved total).
            var cargoRows = new List<CargoResourceRowViewModel>
            {
                new CargoResourceRowViewModel("Ironium", fleet.Cargo.Ironium + orbitStar.ResourcesOnHand.Ironium, fleet.Cargo.Ironium, fleet.TotalCargoCapacity),
                new CargoResourceRowViewModel("Boranium", fleet.Cargo.Boranium + orbitStar.ResourcesOnHand.Boranium, fleet.Cargo.Boranium, fleet.TotalCargoCapacity),
                new CargoResourceRowViewModel("Germanium", fleet.Cargo.Germanium + orbitStar.ResourcesOnHand.Germanium, fleet.Cargo.Germanium, fleet.TotalCargoCapacity),
                new CargoResourceRowViewModel("Colonists", fleet.Cargo.ColonistsInKilotons + (orbitStar.Colonists / Global.ColonistsPerKiloton), fleet.Cargo.ColonistsInKilotons, fleet.TotalCargoCapacity),
            };
            foreach (CargoResourceRowViewModel row in cargoRows)
            {
                row.AttachSiblings(cargoRows);
            }

            CargoRows = cargoRows;
        }
        else
        {
            CanTransferCargo = false;
            CargoRows = Array.Empty<CargoResourceRowViewModel>();
        }

        CargoStatusMessage = "";

        var rows = new List<SplitMergeRowViewModel>();
        foreach (KeyValuePair<long, ShipToken> entry in fleet.Composition)
        {
            rows.Add(new SplitMergeRowViewModel(entry.Key, entry.Value.Design, entry.Value.Quantity));
        }

        SplitMergeRows = rows;
        CanSplitMerge = rows.Count > 0;

        var targets = new List<SplitMergeTargetOption> { new SplitMergeTargetOption(null, "New Fleet") };
        foreach (Fleet other in clientState.EmpireState.OwnedFleets.Values)
        {
            if (other.Key != fleet.Key && !other.IsStarbase && other.Position == fleet.Position)
            {
                targets.Add(new SplitMergeTargetOption(other, other.Name));
            }
        }

        SplitMergeTargets = targets;
        SelectedSplitMergeTarget = targets[0];
        SplitMergeStatusMessage = "";

        var transferTargets = new List<SplitMergeTargetOption>();
        foreach (Fleet other in clientState.EmpireState.OwnedFleets.Values)
        {
            if (other.Key != fleet.Key && !other.IsStarbase && other.Position == fleet.Position)
            {
                transferTargets.Add(new SplitMergeTargetOption(other, other.Name));
            }
        }

        FleetCargoTransferTargets = transferTargets;
        CanTransferCargoToFleet = transferTargets.Count > 0;
        SelectedFleetCargoTransferTarget = transferTargets.FirstOrDefault();
        FleetTransferStatusMessage = "";
    }

    /// <summary>
    /// Rebuilds the transfer rows for the currently selected fleet/target pair - each row's
    /// Total is the conserved (selectedFleet + target) amount for that resource, split via one
    /// slider (see CargoResourceRowViewModel). Called whenever the target fleet changes, since
    /// the conserved totals depend on which two fleets are involved.
    /// </summary>
    private void RebuildFleetTransferRows()
    {
        if (selectedFleet == null || SelectedFleetCargoTransferTarget?.Fleet is not Fleet target)
        {
            FleetTransferRows = Array.Empty<CargoResourceRowViewModel>();
            FleetTransferFuelRow = null;
            return;
        }

        // Both sides are real fleets here (unlike the fleet-vs-planet case above), each with its
        // own real cargo-hold size - so unlike there, targetCapacity is set too.
        var rows = new List<CargoResourceRowViewModel>
        {
            new CargoResourceRowViewModel("Ironium", selectedFleet.Cargo.Ironium + target.Cargo.Ironium, selectedFleet.Cargo.Ironium, selectedFleet.TotalCargoCapacity, target.TotalCargoCapacity),
            new CargoResourceRowViewModel("Boranium", selectedFleet.Cargo.Boranium + target.Cargo.Boranium, selectedFleet.Cargo.Boranium, selectedFleet.TotalCargoCapacity, target.TotalCargoCapacity),
            new CargoResourceRowViewModel("Germanium", selectedFleet.Cargo.Germanium + target.Cargo.Germanium, selectedFleet.Cargo.Germanium, selectedFleet.TotalCargoCapacity, target.TotalCargoCapacity),
            new CargoResourceRowViewModel("Colonists", selectedFleet.Cargo.ColonistsInKilotons + target.Cargo.ColonistsInKilotons, selectedFleet.Cargo.ColonistsInKilotons, selectedFleet.TotalCargoCapacity, target.TotalCargoCapacity),
        };
        foreach (CargoResourceRowViewModel row in rows)
        {
            row.AttachSiblings(rows);
        }

        FleetTransferRows = rows;

        // Fuel isn't part of the cargo-mass budget above (a fleet's fuel tank and cargo hold are
        // separate capacities in this game) - its own row gets its own capacity pair and isn't
        // wired to the cargo rows' sibling group, just its own (single-row) one.
        var fuelRow = new CargoResourceRowViewModel(
            "Fuel",
            (int)(selectedFleet.FuelAvailable + target.FuelAvailable),
            (int)selectedFleet.FuelAvailable,
            selectedFleet.TotalFuelCapacity,
            target.TotalFuelCapacity);
        fuelRow.AttachSiblings(new[] { fuelRow });
        FleetTransferFuelRow = fuelRow;

        FleetTransferStatusMessage = "";
    }

    /// <summary>
    /// Applies the fleet-to-fleet transfer - validates neither fleet ends up over its own cargo/
    /// fuel capacity (CargoResourceRowViewModel only clamps each slider to [0, Total], the
    /// combined amount - same simplification ApplyCargoTransfer's own doc comment already
    /// accepts for the fleet-vs-planet case) then mutates both fleets directly, matching
    /// CargoTransferDialog's own no-ICommand behavior.
    /// </summary>
    private void ApplyFleetCargoTransfer()
    {
        if (selectedFleet == null || SelectedFleetCargoTransferTarget?.Fleet is not Fleet target || FleetTransferRows.Count != 4 || FleetTransferFuelRow == null)
        {
            return;
        }

        int newFleetMass = FleetTransferRows.Sum(r => r.FleetAmount);
        int newTargetMass = FleetTransferRows.Sum(r => r.PlanetAmount);

        if (newFleetMass > selectedFleet.TotalCargoCapacity)
        {
            FleetTransferStatusMessage = $"Too much cargo for {selectedFleet.Name}: {newFleetMass}kT exceeds its {selectedFleet.TotalCargoCapacity}kT capacity.";
            return;
        }

        if (newTargetMass > target.TotalCargoCapacity)
        {
            FleetTransferStatusMessage = $"Too much cargo for {target.Name}: {newTargetMass}kT exceeds its {target.TotalCargoCapacity}kT capacity.";
            return;
        }

        if (FleetTransferFuelRow.FleetAmount > selectedFleet.TotalFuelCapacity)
        {
            FleetTransferStatusMessage = $"Too much fuel for {selectedFleet.Name}: exceeds its {selectedFleet.TotalFuelCapacity}mg capacity.";
            return;
        }

        if (FleetTransferFuelRow.PlanetAmount > target.TotalFuelCapacity)
        {
            FleetTransferStatusMessage = $"Too much fuel for {target.Name}: exceeds its {target.TotalFuelCapacity}mg capacity.";
            return;
        }

        selectedFleet.Cargo.Ironium = FleetTransferRows[0].FleetAmount;
        target.Cargo.Ironium = FleetTransferRows[0].PlanetAmount;
        selectedFleet.Cargo.Boranium = FleetTransferRows[1].FleetAmount;
        target.Cargo.Boranium = FleetTransferRows[1].PlanetAmount;
        selectedFleet.Cargo.Germanium = FleetTransferRows[2].FleetAmount;
        target.Cargo.Germanium = FleetTransferRows[2].PlanetAmount;
        selectedFleet.Cargo.ColonistsInKilotons = FleetTransferRows[3].FleetAmount;
        target.Cargo.ColonistsInKilotons = FleetTransferRows[3].PlanetAmount;

        selectedFleet.FuelAvailable = FleetTransferFuelRow.FleetAmount;
        target.FuelAvailable = FleetTransferFuelRow.PlanetAmount;

        ShowFleet(selectedFleet);
        selection.NotifyMutated();
        FleetTransferStatusMessage = "Transfer applied.";
    }

    private void AddWaypoint()
    {
        if (selectedFleet == null || string.IsNullOrEmpty(NewWaypointDestination))
        {
            return;
        }

        if (!clientState.EmpireState.StarReports.TryGetValue(NewWaypointDestination, out StarIntel destinationReport))
        {
            return;
        }

        var waypoint = new Waypoint
        {
            Position = destinationReport.Position,
            Destination = destinationReport.Name,
            WarpFactor = NewWaypointWarp,
            Task = BuildTask(NewWaypointTask),
        };

        // "In front of" the selected waypoint, per the user's own spec: the new one takes that
        // row's list position and everything from there on shifts one later - CommandMode.Insert
        // does exactly this (unlike Add, which always appends - see WaypointCommand's own doc
        // comment on the difference). With nothing selected, append as before.
        if (HasSelectedWaypoint)
        {
            int insertAt = selectedWaypointIndex;
            selectedWaypointIndex = -1; // the insert shifts every row after it - nothing stays "the" selected one
            ApplyCommand(new WaypointCommand(CommandMode.Insert, waypoint, selectedFleet.Key, insertAt));
        }
        else
        {
            ApplyCommand(new WaypointCommand(CommandMode.Add, waypoint, selectedFleet.Key));
        }
    }

    /// <summary>
    /// Tapping a waypoint row selects it (tapping the same row again deselects) - selecting
    /// shows a Task picker for just this waypoint (see SelectedWaypointTaskOption) and changes
    /// where the next "Add Waypoint" inserts (see AddWaypoint's own comment).
    /// </summary>
    private void SelectWaypoint(int index)
    {
        selectedWaypointIndex = selectedWaypointIndex == index ? -1 : index;
        ApplyWaypointSelectionState();
    }

    /// <summary>
    /// Re-applies selectedWaypointIndex to the current WaypointRows/SelectedWaypointTaskOption
    /// without toggling anything - called after every rebuild (ShowFleet) so an edit elsewhere
    /// (Move Up/Down, a Task change) doesn't silently drop the row's highlight, and so a waypoint
    /// that no longer exists (e.g. just deleted) cleanly clears the selection instead of leaving
    /// HasSelectedWaypoint stuck true for a row that isn't there anymore.
    /// </summary>
    private void ApplyWaypointSelectionState()
    {
        bool matched = false;
        foreach (FleetWaypointRowViewModel row in WaypointRows)
        {
            row.IsSelected = row.Index == selectedWaypointIndex;
            matched |= row.IsSelected;
        }

        if (!matched)
        {
            selectedWaypointIndex = -1;
        }

        HasSelectedWaypoint = selectedWaypointIndex >= 0;

        if (HasSelectedWaypoint && selectedFleet != null)
        {
            suppressWaypointTaskChange = true;
            SelectedWaypointTaskOption = selectedFleet.Waypoints[selectedWaypointIndex].Task?.Name ?? "None";
            suppressWaypointTaskChange = false;

            suppressWaypointWarpChange = true;
            SelectedWaypointWarp = selectedFleet.Waypoints[selectedWaypointIndex].WarpFactor;
            suppressWaypointWarpChange = false;
        }
    }

    /// <summary>
    /// Pushes a Task change for whichever waypoint is currently selected - the "change an
    /// already-added waypoint's task" half of the user's spec, mirroring FleetDetail.
    /// WaypointTaskChanged's own immediate-apply behavior (no separate "Apply" button). Reuses
    /// CloneWaypointFully/PushWaypointEdit exactly as MoveWaypoint does, since this is the same
    /// "replace one waypoint in place, changing nothing but one field" operation.
    /// </summary>
    private void ApplySelectedWaypointTask()
    {
        if (selectedFleet == null || !HasSelectedWaypoint)
        {
            return;
        }

        Waypoint edited = CloneWaypointFully(selectedFleet.Waypoints[selectedWaypointIndex]);
        edited.Task = BuildTask(SelectedWaypointTaskOption);
        PushWaypointEdit(edited, selectedWaypointIndex);

        ShowFleet(selectedFleet);
        selection.NotifyMutated();
    }

    /// <summary>
    /// Pushes a WarpFactor change for whichever waypoint is currently selected - lets an
    /// already-queued leg's speed be sped up or slowed down after the fact, rather than only
    /// settable once at creation time (NewWaypointWarp). Same immediate-apply, clone-and-replace
    /// pattern as ApplySelectedWaypointTask.
    /// </summary>
    private void ApplySelectedWaypointWarp()
    {
        if (selectedFleet == null || !HasSelectedWaypoint)
        {
            return;
        }

        Waypoint edited = CloneWaypointFully(selectedFleet.Waypoints[selectedWaypointIndex]);
        edited.WarpFactor = SelectedWaypointWarp;
        PushWaypointEdit(edited, selectedWaypointIndex);

        ShowFleet(selectedFleet);
        selection.NotifyMutated();
    }

    private void DeleteWaypoint(int index)
    {
        if (selectedFleet == null)
        {
            return;
        }

        ApplyCommand(new WaypointCommand(CommandMode.Delete, selectedFleet.Key, index));
    }

    /// <summary>
    /// Swap the waypoints at index/otherIndex by pushing an Edit command for each carrying the
    /// other's payload - mirrors FleetDetail.SwapWaypoints (the WinForms port of this same
    /// feature) exactly, including reusing CloneWaypointFully to work around Waypoint's own
    /// copy constructor dropping Task ("used for editing purposes" - wrong for a reorder, which
    /// must change nothing but list position).
    /// </summary>
    private void MoveWaypoint(int index, int otherIndex)
    {
        if (selectedFleet == null)
        {
            return;
        }

        Waypoint waypointAtIndex = CloneWaypointFully(selectedFleet.Waypoints[index]);
        Waypoint waypointAtOther = CloneWaypointFully(selectedFleet.Waypoints[otherIndex]);

        PushWaypointEdit(waypointAtOther, index);
        PushWaypointEdit(waypointAtIndex, otherIndex);

        ShowFleet(selectedFleet);
        selection.NotifyMutated();
    }

    private static Waypoint CloneWaypointFully(Waypoint source)
    {
        return new Waypoint(source) { Task = source.Task };
    }

    private void PushWaypointEdit(Waypoint waypoint, int index)
    {
        var command = new WaypointCommand(CommandMode.Edit, waypoint, selectedFleet!.Key, index);
        clientState.Commands.Push(command);
        if (command.IsValid(clientState.EmpireState))
        {
            command.ApplyToState(clientState.EmpireState);
        }
    }

    private void SubmitRename()
    {
        if (selectedFleet == null || string.IsNullOrWhiteSpace(NewFleetName))
        {
            return;
        }

        ApplyCommand(new RenameFleetCommand(selectedFleet, NewFleetName));
    }

    /// <summary>
    /// Mirrors CargoDialog.OkButton_Click's diff-and-build logic exactly: for each resource,
    /// however much the fleet's target amount differs from what it holds now goes into either
    /// a Load task (fleet gains, planet loses) or an Unload task (fleet loses, planet gains) -
    /// never both for the same resource. Only a task with a non-zero total gets turned into a
    /// command. Like the original, this wraps each task in a *copy* of Waypoints[0] and pushes
    /// it via CommandMode.Add rather than editing waypoint zero in place - a known quirk in the
    /// original (flagged there as a TODO) that ends up appending an extra waypoint entry rather
    /// than attaching the task to the current position; kept as-is for parity rather than
    /// "fixed" into different behavior than the reference implementation.
    /// </summary>
    private void ApplyCargoTransfer()
    {
        if (selectedFleet == null || !CanTransferCargo || CargoRows.Count != 4)
        {
            return;
        }

        int totalFleetMass = CargoRows.Sum(r => r.FleetAmount);
        if (totalFleetMass > selectedFleet.TotalCargoCapacity)
        {
            CargoStatusMessage = $"Too much cargo: {totalFleetMass}kT exceeds this fleet's {selectedFleet.TotalCargoCapacity}kT capacity.";
            return;
        }

        var loadTask = new CargoTask { Mode = CargoMode.Load };
        var unloadTask = new CargoTask { Mode = CargoMode.Unload };

        ApplyCargoDiff(selectedFleet.Cargo.Ironium, CargoRows[0].FleetAmount, loadTask.Amount, unloadTask.Amount, (cargo, value) => cargo.Ironium = value);
        ApplyCargoDiff(selectedFleet.Cargo.Boranium, CargoRows[1].FleetAmount, loadTask.Amount, unloadTask.Amount, (cargo, value) => cargo.Boranium = value);
        ApplyCargoDiff(selectedFleet.Cargo.Germanium, CargoRows[2].FleetAmount, loadTask.Amount, unloadTask.Amount, (cargo, value) => cargo.Germanium = value);
        ApplyCargoDiff(selectedFleet.Cargo.ColonistsInKilotons, CargoRows[3].FleetAmount, loadTask.Amount, unloadTask.Amount, (cargo, value) => cargo.ColonistsInKilotons = value);

        bool appliedAny = false;
        foreach (CargoTask task in new[] { loadTask, unloadTask })
        {
            if (task.Amount.Mass == 0)
            {
                continue;
            }

            var waypoint = new Waypoint(selectedFleet.Waypoints[0]);
            waypoint.Task = task;
            var command = new WaypointCommand(CommandMode.Add, waypoint, selectedFleet.Key, 0);

            clientState.Commands.Push(command);
            if (!command.IsValid(clientState.EmpireState))
            {
                continue;
            }

            command.ApplyToState(clientState.EmpireState);
            if (task.IsValid(selectedFleet, selectedFleet.InOrbit, clientState.EmpireState, null))
            {
                task.Perform(selectedFleet, selectedFleet.InOrbit, clientState.EmpireState, null);
                appliedAny = true;
            }
        }

        ShowFleet(selectedFleet);
        selection.NotifyMutated();
        CargoStatusMessage = appliedAny ? "Transfer applied." : "No changes to transfer.";
    }

    private static void ApplyCargoDiff(int currentFleetAmount, int targetFleetAmount, Cargo loadAmount, Cargo unloadAmount, System.Action<Cargo, int> setter)
    {
        int diff = targetFleetAmount - currentFleetAmount;
        if (diff > 0)
        {
            setter(loadAmount, diff);
        }
        else if (diff < 0)
        {
            setter(unloadAmount, -diff);
        }
    }

    /// <summary>
    /// Mirrors FleetDetail.DoSplitMerge exactly, including its bookkeeping after Perform:
    /// SplitMergeTask.IsValid is an unconditional stub (always true, per its own TODO comment)
    /// and Perform enforces no constraints either (a "split" can legally leave the original
    /// fleet with zero ships - that's just "move everything to the other side") - so the
    /// AddOrUpdateFleet/RemoveFleet housekeeping below is what actually keeps
    /// EmpireData.OwnedFleets consistent, exactly as FleetDetail.cs does it: the original
    /// fleet and the merge target (if any) each get removed if left with 0 ships, added/
    /// updated otherwise; any brand-new fleet Perform created goes through the same check via
    /// EmpireData.TemporaryFleets. Unlike the original, TemporaryFleets is cleared afterward -
    /// safe since every entry has already been moved into OwnedFleets or discarded, and
    /// nothing else in a running session reads that list.
    /// </summary>
    private void ApplySplitMerge()
    {
        if (selectedFleet == null || SplitMergeRows.Count == 0)
        {
            return;
        }

        if (!SplitMergeRows.Any(row => row.OtherQuantity > 0))
        {
            SplitMergeStatusMessage = "Nothing to move - adjust at least one row.";
            return;
        }

        // SplitMergeTask.Perform's ReassignShips only looks at keys actually present in
        // LeftComposition (SourceComposition here) - a row kept entirely on the source side
        // still needs its key present with Quantity 0, or ReassignShips never even considers
        // it and nothing moves. So every row goes into both dictionaries regardless of value,
        // matching what the original dialog's composition editor always builds (a row per
        // design that was in the fleet, not just the ones the player touched).
        var sourceComposition = new Dictionary<long, ShipToken>();
        var otherComposition = new Dictionary<long, ShipToken>();
        foreach (SplitMergeRowViewModel row in SplitMergeRows)
        {
            sourceComposition[row.CompositionKey] = new ShipToken(row.Design, row.KeepInSource);
            otherComposition[row.CompositionKey] = new ShipToken(row.Design, row.OtherQuantity);
        }

        Fleet? otherFleet = SelectedSplitMergeTarget?.Fleet;
        long otherFleetKey = otherFleet?.Key ?? 0;

        var task = new SplitMergeTask(sourceComposition, otherComposition, otherFleetKey);
        var waypoint = new Waypoint(selectedFleet.Waypoints[0]);
        waypoint.Task = task;
        var command = new WaypointCommand(CommandMode.Add, waypoint, selectedFleet.Key, 0);

        Fleet? newlyCreatedFleet = null;

        clientState.Commands.Push(command);
        if (command.IsValid(clientState.EmpireState))
        {
            command.ApplyToState(clientState.EmpireState);

            EmpireData empire = clientState.EmpireState;
            task.Perform(selectedFleet, otherFleet, empire, empire);

            UpdateFleetAfterSplitMerge(selectedFleet);
            if (otherFleet != null)
            {
                UpdateFleetAfterSplitMerge(otherFleet);
            }

            foreach (Fleet created in empire.TemporaryFleets)
            {
                UpdateFleetAfterSplitMerge(created);
                if (created.Composition.Count > 0)
                {
                    newlyCreatedFleet = created;
                }
            }

            empire.TemporaryFleets.Clear();
        }

        selection.NotifyMutated();

        if (selectedFleet.Composition.Count == 0)
        {
            // The original fleet gave everything away and no longer exists - show whatever it
            // merged into, or the fleet it just spun off, or "nothing selected" as a last resort.
            selection.Selected = otherFleet ?? newlyCreatedFleet;
        }
        else
        {
            ShowFleet(selectedFleet);

            // A fuel-shortfall stranding (see SplitMergeTask.MergeFleets) pushes its own graduated
            // message onto the task - surface that instead of the generic text when present,
            // rather than silently overwriting it.
            SplitMergeStatusMessage = task.Messages.Count > 0
                ? task.Messages[^1].Text
                : otherFleetKey == 0 ? "Split into a new fleet." : "Merged.";
        }
    }

    private void UpdateFleetAfterSplitMerge(Fleet fleet)
    {
        if (fleet.Composition.Count == 0)
        {
            clientState.EmpireState.RemoveFleet(fleet);
        }
        else
        {
            clientState.EmpireState.AddOrUpdateFleet(fleet);
        }
    }

    /// <summary>
    /// The WinForms pattern every order-issuing control follows (e.g. StarMap.cs's
    /// WaypointCommand push, FleetDetail.cs's edits): queue the command for the eventual
    /// .orders file, then apply it to the local EmpireData immediately so the UI reflects it
    /// without waiting on a server round-trip.
    /// </summary>
    private void ApplyCommand(ICommand command)
    {
        clientState.Commands.Push(command);
        if (command.IsValid(clientState.EmpireState))
        {
            command.ApplyToState(clientState.EmpireState);
        }

        ShowFleet(selectedFleet!);
        selection.NotifyMutated();
    }

    private static IWaypointTask BuildTask(string taskName)
    {
        return taskName switch
        {
            "Colonise" => new ColoniseTask(),
            "Scrap" => new ScrapTask(),
            "Lay Mines" => new LayMinesTask(),
            "Invade" => new InvadeTask(),
            _ => new NoTask(),
        };
    }
}
