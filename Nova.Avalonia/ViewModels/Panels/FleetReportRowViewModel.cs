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
/// battle plan, mass), plus the additional per-row idle/status glyph the same section documents
/// ("the Fleets report additionally derives a per-row status indicator ... a distinguished
/// 'idle/no useful orders' state is highlighted", client-ui-dialog-catalog.md line 355).
/// </summary>
public class FleetReportRowViewModel
{
    /// <summary>The ETA cell's "never" sentinel (Spec: a "large sentinel value (effectively
    /// 'never') when no valid estimate applies", client-ui-dialog-catalog.md line 355). SPEC GAP:
    /// the exact sentinel value is not recovered; 9999 stands in for it.</summary>
    public const string EtaNever = "9999";

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

    /// <summary>The per-row idle/status glyph (spec line 355/357). See <see cref="FleetReportStatus"/>.</summary>
    public string Status { get; }

    /// <summary>True when the fleet has no useful orders; the spec highlights this state. The
    /// glyph's warning colour is bound to this flag in the view.</summary>
    public bool IsIdle { get; }

    /// <summary>True when a fuel check for the next leg fails, so the ETA cell shows a warning
    /// colour (spec line 355: "shown in a warning color when a fuel or threshold check fails").
    /// SPEC GAP: the spec's "or threshold check" is not defined; only the fuel check is modelled.</summary>
    public bool EtaWarning { get; }

    /// <summary>Legacy own-report ship-stack count, kept for callers that already read it. The
    /// spec's own-Fleets column set carries "composition" (per design) instead, not a count.</summary>
    public string Ships { get; }

    public FleetReportRowViewModel(Fleet fleet, Race? race = null)
    {
        Name = fleet.Name;
        Id = fleet.Id.ToString(CultureInfo.InvariantCulture);
        Location = fleet.InOrbit != null ? fleet.InOrbit.Name : "Space at " + fleet.Position;

        IsIdle = FleetReportStatus.IsIdle(fleet);
        Status = FleetReportStatus.Glyph(IsIdle);

        Destination = "-";
        Eta = EtaNever;
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

            // A warp-0 leg (for example a Stargate) has no turns-to-arrival estimate; the spec
            // leaves the sentinel ("never") in place when no valid estimate applies.
            if (speed > 0)
            {
                double time = distance / speed;
                Eta = time.ToString("F1");
            }

            // Fuel check for the next leg (the spec's companion column's warning state).
            if (race != null && fleet.FuelRequiredForRoute(race, 1) > fleet.FuelAvailable)
            {
                EtaWarning = true;
            }
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

/// <summary>
/// The Fleets report's idle/status indicator (behavior-specs-11/client-ui-dialog-catalog.md
/// "Reports" line 355: "a per-row status indicator ... a distinguished 'idle/no useful orders'
/// state").
///
/// SPEC GAP: the spec says the glyph is "a single character selected from a small fixed set" but
/// does not recover that set, so the two port glyphs below are a named stand-in. Likewise the exact
/// definition of "no useful orders" is not in the spec; idle here means no next waypoint (the
/// fleet's order list holds only its current position).
/// </summary>
public static class FleetReportStatus
{
    /// <summary>The glyph for the distinguished idle/no-useful-orders state. STAND-IN for the
    /// spec's unrecovered glyph set.</summary>
    public const string IdleGlyph = "I";

    /// <summary>The glyph for a fleet with orders. STAND-IN for the spec's unrecovered glyph set.</summary>
    public const string ActiveGlyph = "M";

    public static bool IsIdle(Fleet fleet) => fleet.Waypoints.Count <= 1;

    public static string Glyph(bool isIdle) => isIdle ? IdleGlyph : ActiveGlyph;
}
