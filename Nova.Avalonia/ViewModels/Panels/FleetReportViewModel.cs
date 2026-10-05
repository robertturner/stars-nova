using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Read-only table of every owned fleet (excluding starbases) - ports FleetReport.cs. The column
/// set and order follow behavior-specs-11/client-ui-dialog-catalog.md "Reports" (dynamic strings
/// 1138-1149: fleet name, id, location, destination, ETA, task, fuel, cargo, composition, cloak,
/// battle plan, mass), with the section's additional per-row idle/status glyph appended.
/// </summary>
public class FleetReportViewModel : Tool
{
    private readonly ReportTable<FleetReportRowViewModel> table;

    public ObservableCollection<FleetReportRowViewModel> Fleets => table.Rows;

    public IReadOnlyList<ReportColumn> Columns => table.Columns;

    public string? SortColumnKey => table.SortColumnKey;

    public ReportSortDirection SortDirection => table.SortDirection;

    public FleetReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;

        Race race = clientState.EmpireState.Race;
        IEnumerable<FleetReportRowViewModel> rows = clientState.EmpireState.OwnedFleets.Values
            .Where(fleet => fleet.Owner == clientState.EmpireState.Id && fleet.Type != ItemType.Starbase)
            .Select(fleet => new FleetReportRowViewModel(fleet, race));

        table = new ReportTable<FleetReportRowViewModel>(rows, new (string Key, string Header, Func<FleetReportRowViewModel, string> Value)[]
        {
            ("Name", "Fleet Name", r => r.Name),
            ("Id", "Id", r => r.Id),
            ("Location", "Location", r => r.Location),
            ("Destination", "Destination", r => r.Destination),
            ("Eta", "ETA", r => r.Eta),
            ("Task", "Task", r => r.Task),
            ("Fuel", "Fuel", r => r.Fuel),
            ("Cargo", "Cargo", r => r.Cargo),
            ("Composition", "Composition", r => r.Composition),
            ("Cloak", "Cloak", r => r.Cloak),
            ("BattlePlan", "Battle Plan", r => r.BattlePlan),
            ("Mass", "Mass", r => r.Mass),
            ("Status", "Status", r => r.Status),
        });

        // Title carries the row count and a plural marker (client-ui-dialog-catalog.md Reports
        // line 369; the recovered template is in ReportTitles).
        Title = ReportTitles.Summary("Fleet", table.Rows.Count, "Fleet");
    }

    public void Sort(string columnKey) => table.Sort(columnKey);

    public void ReverseSort() => table.ReverseSort();

    public void ToggleColumn(string columnKey) => table.ToggleColumn(columnKey);

    public bool IsColumnVisible(string columnKey) => table.Columns
        .First(column => column.Key == columnKey)
        .IsVisible;
}
