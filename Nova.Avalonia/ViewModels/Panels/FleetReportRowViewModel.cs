using System.Globalization;
using System.Text;
using Nova.Common;
using Nova.Common.Waypoints;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>One row in the Fleet Report - ports FleetReport.cs's OnLoad column-by-column.</summary>
public class FleetReportRowViewModel
{
    public string Name { get; }

    public string Location { get; }

    public string Destination { get; }

    public string Eta { get; }

    public string Task { get; }

    public string Fuel { get; }

    public string Cargo { get; }

    public string Ships { get; }

    public string Cloak { get; } = "-";

    public string BattlePlan { get; }

    public string Mass { get; }

    public FleetReportRowViewModel(Fleet fleet)
    {
        Name = fleet.Name;
        Location = fleet.InOrbit != null ? fleet.InOrbit.Name : "Space at " + fleet.Position;

        Destination = "-";
        Eta = "-";
        Task = "-";

        if (fleet.Waypoints.Count > 1)
        {
            Waypoint waypoint = fleet.Waypoints[1];

            Destination = waypoint.Destination;
            if (!(waypoint.Task is NoTask))
            {
                Task = waypoint.Task.Name;
            }

            double distance = PointUtilities.Distance(waypoint.Position, fleet.Position);
            double speed = waypoint.WarpFactor * waypoint.WarpFactor;
            double time = distance / speed;

            Eta = time.ToString("F1");
        }

        Nova.Common.Cargo cargo = fleet.Cargo;
        var cargoText = new StringBuilder();
        cargoText.AppendFormat("{0} {1} {2} {3}", cargo.Ironium, cargo.Boranium, cargo.Germanium, cargo.ColonistsInKilotons);
        Cargo = cargoText.ToString();

        Fuel = fleet.FuelAvailable.ToString("f1");
        Ships = fleet.Composition.Count.ToString(CultureInfo.InvariantCulture);
        BattlePlan = fleet.BattlePlan;
        Mass = fleet.Mass.ToString(CultureInfo.InvariantCulture);
    }
}
