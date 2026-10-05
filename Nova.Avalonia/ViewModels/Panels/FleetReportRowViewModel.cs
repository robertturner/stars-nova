using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Nova.Common;
using Nova.Common.Waypoints;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Fleet Report - ports FleetReport.cs's OnLoad column-by-column. The column set
/// and order follow behavior-specs-11/client-ui-dialog-catalog.md "Reports" (dynamic strings
/// 1138-1149: fleet name, id, location, destination, ETA, task, fuel, cargo, composition, cloak,
/// battle plan, mass).
/// </summary>
public class FleetReportRowViewModel
{
    public string Name { get; }

    public string Id { get; }

    public string Location { get; }

    public string Destination { get; }

    public string Eta { get; }

    public string Task { get; }

    public string Fuel { get; }

    public string Cargo { get; }

    public string Composition { get; }

    public string Cloak { get; } = "-";

    public string BattlePlan { get; }

    public string Mass { get; }

    /// <summary>Legacy own-report ship-stack count, kept for callers that already read it. The
    /// spec's own-Fleets column set carries "composition" (per design) instead, not a count.</summary>
    public string Ships { get; }

    public FleetReportRowViewModel(Fleet fleet)
    {
        Name = fleet.Name;
        Id = fleet.Id.ToString(CultureInfo.InvariantCulture);
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

        Composition = FormatComposition(fleet.Composition.Values);

        Fuel = fleet.FuelAvailable.ToString("f1");
        Ships = fleet.Composition.Count.ToString(CultureInfo.InvariantCulture);
        BattlePlan = fleet.BattlePlan;
        Mass = fleet.Mass.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Per-design "name xN" listing ordered by design name; "-" when the fleet is empty.
    /// AMBIGUITY: the spec names the "composition" column but not its wording.</summary>
    internal static string FormatComposition(IEnumerable<ShipToken> tokens)
    {
        var parts = tokens
            .OrderBy(token => token.Design.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(token => string.Format(CultureInfo.InvariantCulture, "{0} x{1}", token.Design.Name, token.Quantity))
            .ToList();

        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }
}
