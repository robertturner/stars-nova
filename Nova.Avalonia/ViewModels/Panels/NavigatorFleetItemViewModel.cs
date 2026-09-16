using System.Linq;
using Nova.Common;
using Nova.Common.Components;
using Nova.Common.Waypoints;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Navigator's Fleets list: a fleet this empire owns, with a short status
/// (orbiting/en route/holding) matching what the WinForms StarMap conveys visually. Rebuilt
/// fresh by NavigatorViewModel whenever anything might have changed (a mutated fleet, one
/// created or removed by a split/merge) rather than mutated in place.
/// </summary>
public class NavigatorFleetItemViewModel
{
    public Fleet Fleet { get; }

    public string Name => Fleet.Name;

    public string Status { get; }

    /// <summary>The fleet's first ship type - see StarMapFleetViewModel.PrimaryDesign's own
    /// comment (same reasoning, an owned Fleet always has real composition data available).
    /// Shown as a quick "what hull is this" icon; press-and-hold shows its full component
    /// layout via NavigatorViewModel.HullViewer.</summary>
    public ShipDesign? PrimaryDesign => Fleet.Composition.Values.FirstOrDefault()?.Design;

    public object? Icon => Fleet.Icon?.Image;

    public NavigatorFleetItemViewModel(Fleet fleet)
    {
        Fleet = fleet;
        Status = BuildStatus(fleet);
    }

    private static string BuildStatus(Fleet fleet)
    {
        // Waypoints[0] is always the fleet's current position; a further waypoint with a
        // non-zero warp factor means it's actually under way (see FleetDetail.DisplayLegDetails).
        if (fleet.Waypoints.Count > 1 && fleet.Waypoints[1].WarpFactor > 0)
        {
            Waypoint destination = fleet.Waypoints[1];
            double distance = PointUtilities.Distance(fleet.Waypoints[0].Position, destination.Position);
            double years = distance / (destination.WarpFactor * destination.WarpFactor);
            return $"→ {destination.Destination}, ETA {years:0.0}y";
        }

        if (fleet.InOrbit != null)
        {
            return $"orbiting {fleet.InOrbit.Name}";
        }

        return "holding position";
    }
}
