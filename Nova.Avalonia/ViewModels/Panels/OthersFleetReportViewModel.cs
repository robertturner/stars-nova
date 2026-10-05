using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Read-only table of every other player's known fleet - the spec's fourth report type. The column
/// set and order follow behavior-specs-11/client-ui-dialog-catalog.md "Reports" (dynamic strings
/// 1150-1161: fleet name, id, location, warp, mass, composition, number of ships, unarmed, scout,
/// warship, bomber, utility).
/// </summary>
public class OthersFleetReportViewModel : Tool
{
    public IReadOnlyList<OthersFleetReportRowViewModel> Fleets { get; }

    public OthersFleetReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;

        ushort ownId = clientState.EmpireState.Id;
        Fleets = clientState.EmpireState.FleetReports.Values
            .Where(intel => intel.Owner != ownId && !intel.IsStarbase)
            .Select(intel => new OthersFleetReportRowViewModel(intel))
            .ToList();

        // Title carries the row count and a plural marker (client-ui-dialog-catalog.md Reports
        // line 369; the recovered template is in ReportTitles).
        Title = ReportTitles.Summary("Others' Fleets", Fleets.Count, "Fleet");
    }
}

/// <summary>
/// One row in the Others' Fleets report (behavior-specs-11/client-ui-dialog-catalog.md Reports,
/// dynamic strings 1150-1161).
/// </summary>
public class OthersFleetReportRowViewModel
{
    public string Name { get; }

    public string Id { get; }

    /// <summary>AMBIGUITY: FleetIntel stores only an in-orbit flag, not the planet name, so a fleet
    /// in orbit shows the generic "In orbit" rather than the planet's name.</summary>
    public string Location { get; }

    public string Warp { get; }

    public string Mass { get; }

    public string Composition { get; }

    public string NumberOfShips { get; }

    public string Unarmed { get; }

    public string Scout { get; }

    public string Warship { get; }

    public string Bomber { get; }

    public string Utility { get; }

    public OthersFleetReportRowViewModel(FleetIntel intel)
    {
        Name = intel.Name;
        Id = intel.Id.ToString(CultureInfo.InvariantCulture);
        Location = intel.InOrbit ? "In orbit" : "Space at " + intel.Position;
        Warp = intel.Speed == Global.Unset ? "-" : intel.Speed.ToString(CultureInfo.InvariantCulture);
        Mass = intel.Mass.ToString(CultureInfo.InvariantCulture);
        Composition = FleetReportRowViewModel.FormatComposition(intel.Composition.Values);
        NumberOfShips = intel.Count.ToString(CultureInfo.InvariantCulture);

        Unarmed = ShipHullClass.Unrecovered;
        Scout = ShipHullClass.Unrecovered;
        Warship = ShipHullClass.Unrecovered;
        Bomber = ShipHullClass.Unrecovered;
        Utility = ShipHullClass.Unrecovered;
    }
}

/// <summary>
/// SEAM / SPEC GAP: the Others' Fleets and Battles class-count columns classify ships by hull class
/// (behavior-specs-11/client-ui-dialog-catalog.md Reports: unarmed = hull class 0, 1 or above 5;
/// scout = 2; warship = 3; utility = 4; bomber = 5; "the hull class being bits 2-5 of hull byte
/// 0x7c"; the eighth-class list of client-interface.md line 224: colony ship, freighter, scout,
/// warship, utility, bomber, miner, fuel transport). The port's ShipDesign/Hull carries no hull
/// class, so no exact count can be computed and every class cell is this named unknown marker until
/// that field is modelled.
/// </summary>
public static class ShipHullClass
{
    public const string Unrecovered = "-";
}
