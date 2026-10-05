using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Read-only table of every owned planet - ports PlanetReport.cs. The column set and order follow
/// behavior-specs-11/client-ui-dialog-catalog.md "Reports" (dynamic strings 1113-1127). The table
/// also supports the report grid's shared behaviours: per-column show/hide and header
/// sort/reverse (dynamic strings 1133-1137).
/// </summary>
public class PlanetReportViewModel : Tool
{
    private readonly ReportTable<PlanetReportRowViewModel> table;

    public ObservableCollection<PlanetReportRowViewModel> Planets => table.Rows;

    public IReadOnlyList<ReportColumn> Columns => table.Columns;

    public string? SortColumnKey => table.SortColumnKey;

    public ReportSortDirection SortDirection => table.SortDirection;

    public PlanetReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;

        Race race = clientState.EmpireState.Race;
        IEnumerable<PlanetReportRowViewModel> rows = clientState.EmpireState.OwnedStars.Values
            .Where(star => star.Owner == clientState.EmpireState.Id)
            .Select(star => new PlanetReportRowViewModel(star, race));

        table = new ReportTable<PlanetReportRowViewModel>(rows, new (string Key, string Header, Func<PlanetReportRowViewModel, string> Value)[]
        {
            ("Name", "Planet Name", r => r.Name),
            ("Starbase", "Starbase", r => r.Starbase),
            ("Population", "Population", r => r.Population),
            ("Cap", "Cap", r => r.Cap),
            ("Value", "Value", r => r.Value),
            ("Production", "Production", r => r.Production),
            ("Mines", "Mines", r => r.Mines),
            ("Factories", "Factories", r => r.Factories),
            ("Defenses", "Defense", r => r.Defenses),
            ("Minerals", "Minerals", r => r.Minerals),
            ("MiningRate", "Mining Rate", r => r.MiningRate),
            ("Concentration", "Mineral Concentration", r => r.Concentration),
            ("Resources", "Resources", r => r.Resources),
            ("DriverDestination", "Driver Destination", r => r.DriverDestination),
            ("RoutingDestination", "Routing Destination", r => r.RoutingDestination),
        });

        // behavior-specs-11/client-ui-dialog-catalog.md "Reports" line 369: the title is dynamic
        // strings 1177-1180 with the row count and a plural marker. The exact wording is recovered
        // (with a stated caveat) at behavior-specs-8/client-interface.md line 209 and applied by
        // ReportTitles below.
        Title = ReportTitles.Summary("Planet", table.Rows.Count, "Planet");
    }

    /// <summary>Header sort: sorts by the column ascending (re-selecting the current column
    /// reverses it). Spec strings 1133-1137 "sort".</summary>
    public void Sort(string columnKey) => table.Sort(columnKey);

    /// <summary>Header reverse sort: flips the current column's direction. Spec strings 1133-1137
    /// "reverse sort".</summary>
    public void ReverseSort() => table.ReverseSort();

    /// <summary>Header hide/show: toggles one column's visibility. Spec strings 1133-1137
    /// "hide or show of that column".</summary>
    public void ToggleColumn(string columnKey) => table.ToggleColumn(columnKey);

    public bool IsColumnVisible(string columnKey) => table.Columns
        .First(column => column.Key == columnKey)
        .IsVisible;
}

/// <summary>
/// One report-grid column: a functional key, the displayed caption and a visibility flag. The
/// caption is the port's own label because the spec gives the captions only by dynamic-string id
/// (behavior-specs-11/client-ui-dialog-catalog.md "Reports" line 369 and the column table).
/// </summary>
public sealed class ReportColumn : INotifyPropertyChanged
{
    private bool isVisible = true;

    public ReportColumn(string key, string header)
    {
        Key = key;
        Header = header;
    }

    public string Key { get; }

    public string Header { get; }

    public bool IsVisible
    {
        get => isVisible;
        internal set
        {
            if (isVisible == value)
            {
                return;
            }

            isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Sort direction of a report grid (spec strings 1133-1137: sort / reverse sort).</summary>
public enum ReportSortDirection
{
    None,
    Ascending,
    Descending,
}

/// <summary>
/// The report grid's shared state: the column set with per-column visibility, and the
/// sort/reverse/hide operations of behavior-specs-11/client-ui-dialog-catalog.md "Reports"
/// (dynamic strings 1133-1137). Rows are re-ordered in place so an Avalonia DataGrid's
/// ObservableCollection view follows the header sort.
///
/// SPEC GAP: the spec gives the header commands by dynamic-string id only, so the exact menu
/// wording and the persisted per-report column-visibility bitfield are not reproduced; the
/// in-memory behaviour is implemented. Sort order is the port's natural order (numbers compare
/// numerically, other cells compare as ordinal text) because the spec does not state a collation.
/// </summary>
public sealed class ReportTable<TRow> where TRow : class
{
    private readonly List<TRow> source;
    private readonly Dictionary<string, Func<TRow, string>> values;
    private readonly NaturalComparer comparer = new NaturalComparer();

    public ReportTable(
        IEnumerable<TRow> rows,
        IReadOnlyList<(string Key, string Header, Func<TRow, string> Value)> columns)
    {
        source = rows.ToList();
        Columns = columns.Select(column => new ReportColumn(column.Key, column.Header)).ToList();
        values = columns.ToDictionary(column => column.Key, column => column.Value);

        foreach (TRow row in source)
        {
            Rows.Add(row);
        }
    }

    public IReadOnlyList<ReportColumn> Columns { get; }

    public ObservableCollection<TRow> Rows { get; } = new ObservableCollection<TRow>();

    public string? SortColumnKey { get; private set; }

    public ReportSortDirection SortDirection { get; private set; } = ReportSortDirection.None;

    /// <summary>Sort ascending by the column; selecting the column already sorted ascending
    /// reverses it (matching the original's separate sort / reverse-sort commands).</summary>
    public void Sort(string columnKey)
    {
        if (!values.ContainsKey(columnKey))
        {
            return;
        }

        if (SortColumnKey == columnKey && SortDirection == ReportSortDirection.Ascending)
        {
            SortBy(columnKey, ReportSortDirection.Descending);
        }
        else
        {
            SortBy(columnKey, ReportSortDirection.Ascending);
        }
    }

    /// <summary>Reverses the current sort direction; a no-op when nothing is sorted yet.</summary>
    public void ReverseSort()
    {
        if (SortColumnKey == null)
        {
            return;
        }

        SortDirection = SortDirection == ReportSortDirection.Descending
            ? ReportSortDirection.Ascending
            : ReportSortDirection.Descending;
        Apply();
    }

    public void SortBy(string columnKey, ReportSortDirection direction)
    {
        if (!values.ContainsKey(columnKey) || direction == ReportSortDirection.None)
        {
            return;
        }

        SortColumnKey = columnKey;
        SortDirection = direction;
        Apply();
    }

    public void ToggleColumn(string columnKey)
    {
        ReportColumn? column = Columns.FirstOrDefault(candidate => candidate.Key == columnKey);
        if (column != null)
        {
            column.IsVisible = !column.IsVisible;
        }
    }

    private void Apply()
    {
        Func<TRow, string> selector = values[SortColumnKey!];
        IEnumerable<TRow> ordered = SortDirection == ReportSortDirection.Descending
            ? source.OrderByDescending(selector, comparer)
            : source.OrderBy(selector, comparer);

        Rows.Clear();
        foreach (TRow row in ordered)
        {
            Rows.Add(row);
        }
    }

    /// <summary>Compares two report cells: numerically when both parse as a number, else as
    /// ordinal text (the spec states no collation).</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (double.TryParse(x, NumberStyles.Any, CultureInfo.InvariantCulture, out double numberX)
                && double.TryParse(y, NumberStyles.Any, CultureInfo.InvariantCulture, out double numberY))
            {
                return numberX.CompareTo(numberY);
            }

            return string.CompareOrdinal(x, y);
        }
    }
}

/// <summary>
/// The shared report-window caption helper. SEAM / SPEC GAP: behavior-specs-11's Reports section
/// (client-ui-dialog-catalog.md line 369) says the window title is dynamic strings 1177-1180 with
/// the row count and a plural marker but does not quote the wording. behavior-specs-8/
/// client-interface.md line 209 gives the template explicitly as
/// "&lt;Category&gt; Summary Report -- %d &lt;Category&gt;%c" (the trailing %c the singular/plural
/// marker) and names the four captions, but flags that they were matched by content rather than by
/// a traced call site. The four spec-11 captions ("Planet Summary Report", "Fleet Summary Report",
/// "Others' Fleets Summary Report", "Battle Summary Report") are used here; the exact singular
/// noun and marker per report are the open part of the seam.
/// </summary>
public static class ReportTitles
{
    public const string Template = "{0} Summary Report -- {1} {2}{3}";

    public static string Summary(string category, int count, string noun)
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            Template,
            category,
            count,
            noun,
            count == 1 ? string.Empty : "s");
    }
}
