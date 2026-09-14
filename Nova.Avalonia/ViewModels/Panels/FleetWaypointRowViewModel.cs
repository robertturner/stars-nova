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
/// </summary>
public class FleetWaypointRowViewModel
{
    public string Destination { get; }

    public int Warp { get; }

    public string Task { get; }

    public IRelayCommand DeleteCommand { get; }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public FleetWaypointRowViewModel(Waypoint waypoint, System.Action onDelete, System.Action? onMoveUp, System.Action? onMoveDown)
    {
        Destination = waypoint.Destination;
        Warp = waypoint.WarpFactor;
        Task = waypoint.Task?.Name ?? "None";
        DeleteCommand = new RelayCommand(onDelete);
        MoveUpCommand = new RelayCommand(onMoveUp ?? (() => { }), () => onMoveUp != null);
        MoveDownCommand = new RelayCommand(onMoveDown ?? (() => { }), () => onMoveDown != null);
    }
}
