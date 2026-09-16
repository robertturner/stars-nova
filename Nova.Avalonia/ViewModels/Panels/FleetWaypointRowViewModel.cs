using CommunityToolkit.Mvvm.Input;
using Nova.Common.Waypoints;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One editable row in the Inspector's fleet-orders section - a waypoint beyond the fleet's
/// current position (index 0, never shown/editable here - see InspectorViewModel.ShowFleet).
/// Move Up/Down mirror the WinForms FleetDetail port exactly: the first editable row (list
/// index 1) can never move up, since that would swap it into the immovable current-position
/// waypoint at index 0, and the last row can never move down - InspectorViewModel only supplies
/// an onMoveUp/onMoveDown delegate when the move is actually legal, and its absence is what
/// disables the corresponding button here.
///
/// Tapping a row selects it (see SelectCommand/IsSelected) - InspectorViewModel then shows a
/// Task picker for just this waypoint (so an already-added waypoint's task can be changed, not
/// just set at creation time) and, per the user's own spec, makes the NEXT "Add Waypoint"
/// insert in front of whichever row is selected rather than always appending at the end.
/// </summary>
public class FleetWaypointRowViewModel : ViewModelBase
{
    /// <summary>This waypoint's real index in Fleet.Waypoints (index 0 is always the current
    /// position and never appears as a row) - what InspectorViewModel's delete/move/select
    /// callbacks and WaypointCommand itself actually operate on.</summary>
    public int Index { get; }

    public string Destination { get; }

    public int Warp { get; }

    public string Task { get; }

    /// <summary>Estimated fuel (mg) remaining once the fleet reaches this waypoint, matching
    /// FleetDetail.DisplayLegDetails's own "leg fuel"/"total route fuel" calculation in the
    /// WinForms original (straight-line per-leg fuel use at that leg's own warp factor, summed
    /// cumulatively - no in-transit turn-splitting or ramscoop regeneration, same simplification
    /// the original UI panel itself makes). Can go negative - that's not clamped to zero, since a
    /// negative figure is exactly the useful signal here: the fleet doesn't have enough fuel to
    /// reach this waypoint at its planned warp (see HasFuelShortfall).</summary>
    public double FuelUponArrival { get; }

    /// <summary>True once cumulative fuel use up to and including this waypoint would exceed the
    /// fleet's current fuel - mirrors FleetDetail's own red "not enough fuel for this route"
    /// warning color, but per-waypoint rather than only for the route as a whole.</summary>
    public bool HasFuelShortfall => FuelUponArrival < 0;

    /// <summary>Estimated years from the fleet's CURRENT position (cumulative across every leg
    /// up to and including this one, not just this leg alone) until arrival here - the same
    /// straight-line distance/warp² formula and "no in-transit turn-splitting" simplification
    /// FuelUponArrival's own comment already documents, applied to time instead of fuel, and
    /// summed the same cumulative way.</summary>
    public double YearsUntilArrival { get; }

    public string YearsUntilArrivalDisplay => $"{YearsUntilArrival:0.0} yr" + (YearsUntilArrival == 1.0 ? "" : "s");

    public IRelayCommand DeleteCommand { get; }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public IRelayCommand SelectCommand { get; }

    private bool isSelected;

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public FleetWaypointRowViewModel(int index, Waypoint waypoint, double fuelUponArrival, double yearsUntilArrival, System.Action onDelete, System.Action? onMoveUp, System.Action? onMoveDown, System.Action onSelect)
    {
        Index = index;
        Destination = waypoint.Destination;
        Warp = waypoint.WarpFactor;
        Task = waypoint.Task?.Name ?? "None";
        FuelUponArrival = fuelUponArrival;
        YearsUntilArrival = yearsUntilArrival;
        DeleteCommand = new RelayCommand(onDelete);
        MoveUpCommand = new RelayCommand(onMoveUp ?? (() => { }), () => onMoveUp != null);
        MoveDownCommand = new RelayCommand(onMoveDown ?? (() => { }), () => onMoveDown != null);
        SelectCommand = new RelayCommand(onSelect);
    }
}
