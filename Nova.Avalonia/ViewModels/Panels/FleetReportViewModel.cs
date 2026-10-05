using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Read-only table of every owned fleet (excluding starbases) - ports FleetReport.cs. The column
/// set and order follow behavior-specs-11/client-ui-dialog-catalog.md "Reports" (dynamic strings
/// 1138-1149: fleet name, id, location, destination, ETA, task, fuel, cargo, composition, cloak,
/// battle plan, mass).
/// </summary>
public class FleetReportViewModel : Tool
{
    public IReadOnlyList<FleetReportRowViewModel> Fleets { get; }

    public FleetReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;

        Fleets = clientState.EmpireState.OwnedFleets.Values
            .Where(fleet => fleet.Owner == clientState.EmpireState.Id && fleet.Type != ItemType.Starbase)
            .Select(fleet => new FleetReportRowViewModel(fleet))
            .ToList();

        // Title carries the row count and a plural marker (client-ui-dialog-catalog.md Reports
        // line 369; the recovered template is in ReportTitles).
        Title = ReportTitles.Summary("Fleet", Fleets.Count, "Fleet");
    }
}
