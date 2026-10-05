using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Read-only table of every owned planet - ports PlanetReport.cs. The column set and order follow
/// behavior-specs-11/client-ui-dialog-catalog.md "Reports" (dynamic strings 1113-1127).
/// </summary>
public class PlanetReportViewModel : Tool
{
    public IReadOnlyList<PlanetReportRowViewModel> Planets { get; }

    public PlanetReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;

        Race race = clientState.EmpireState.Race;
        Planets = clientState.EmpireState.OwnedStars.Values
            .Where(star => star.Owner == clientState.EmpireState.Id)
            .Select(star => new PlanetReportRowViewModel(star, race))
            .ToList();

        // behavior-specs-11/client-ui-dialog-catalog.md "Reports" line 369: the title is dynamic
        // strings 1177-1180 with the row count and a plural marker. The exact wording is recovered
        // (with a stated caveat) at behavior-specs-8/client-interface.md line 209 and applied by
        // ReportTitles below.
        Title = ReportTitles.Summary("Planet", Planets.Count, "Planet");
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
