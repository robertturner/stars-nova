using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The in-session Victory Conditions view (behavior-specs-11/victory-conditions.md section 3:
/// the Score window's victory-conditions view; client-ui-dialog-catalog.md row 60's view 1).
/// One line per player with the seven check columns, over the nine-line condition list. The
/// per-player marks come from the client's own intel score records
/// (<c>ClientData.InputTurn.AllScores</c>, which carry the met bits 6-12 as
/// <see cref="ScoreRecord.MetMask"/> and the persistent winner mark); the condition list and the
/// column-enabled flags come from the game's settings. Read-only.
///
/// The settings are re-read whenever the panel is refreshed (the Star Map restores them from the
/// game's .settings file when the game opens). The <see cref="ClientData"/> is supplied by the
/// wiring (the same object every other panel is built with) so the player rows can be filled.
/// </summary>
public class VictoryConditionsViewModel : Tool
{
    // The spec's three shades: "the grey of disabled text", black for an enabled condition, and
    // blue for the winner mark (victory-conditions.md section 3). Black is the spec's word; the
    // enabled/default shade and the disabled grey are these named brushes so the view and the
    // tests agree on exactly three non-empty shades.
    private static readonly IBrush EnabledShade = Brushes.Black;
    private static readonly IBrush DisabledShade = Brushes.Gray;
    private static readonly IBrush WinnerShade = Brushes.DodgerBlue;

    private ClientData? clientState;

    public VictoryConditionsViewModel(string id, string title, ClientData? clientState = null)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    /// <summary>The client whose intel supplies the per-player score records. Setting it refreshes
    /// the view; null (the setup-time default) leaves the player lines empty.</summary>
    public ClientData? ClientState
    {
        get => clientState;
        set
        {
            clientState = value;
            Refresh();
        }
    }

    private IReadOnlyList<VictoryConditionsPlayerRowViewModel> playerRows = Array.Empty<VictoryConditionsPlayerRowViewModel>();

    /// <summary>One row per visible player, or empty before a client is supplied.</summary>
    public IReadOnlyList<VictoryConditionsPlayerRowViewModel> PlayerRows
    {
        get => playerRows;
        private set => SetProperty(ref playerRows, value);
    }

    private IReadOnlyList<VictoryConditionLineViewModel> conditionLines = Array.Empty<VictoryConditionLineViewModel>();

    /// <summary>The nine condition-list lines (1-9); lines 8 and 9 are always black.</summary>
    public IReadOnlyList<VictoryConditionLineViewModel> ConditionLines
    {
        get => conditionLines;
        private set => SetProperty(ref conditionLines, value);
    }

    /// <summary>Whether any player line is shown (false for the settings-only setup-time view).</summary>
    public bool HasPlayers => PlayerRows.Count > 0;

    // ---- The setup/settings summary, kept for the port's existing surface (VictorySummary.Rows).

    private IReadOnlyList<VictorySummaryRow> rows = Array.Empty<VictorySummaryRow>();

    public IReadOnlyList<VictorySummaryRow> Rows
    {
        get => rows;
        private set => SetProperty(ref rows, value);
    }

    private string conditionsLine = "";

    public string ConditionsLine
    {
        get => conditionsLine;
        private set => SetProperty(ref conditionsLine, value);
    }

    private string yearGateLine = "";

    public string YearGateLine
    {
        get => yearGateLine;
        private set => SetProperty(ref yearGateLine, value);
    }

    public IRelayCommand RefreshCommand { get; }

    public void Refresh()
    {
        GameSettings settings = GameSettings.Data;

        Rows = VictorySummary.Rows(settings);
        ConditionsLine = VictorySummary.ConditionsLine(settings);
        YearGateLine = VictorySummary.YearGateLine(settings);

        IReadOnlyList<ScoreRecord> scores = clientState?.InputTurn?.AllScores ?? (IReadOnlyList<ScoreRecord>)Array.Empty<ScoreRecord>();
        PlayerRows = VictorySummary.PlayerRows(settings, scores, NameFor)
            .Select(row => new VictoryConditionsPlayerRowViewModel(row))
            .ToList();

        // Line 1 scales the configured percentage against the number of planets in the universe.
        // The client only ever knows its own stars and reports, so it uses the generated galaxy's
        // star count (one Star is one planet; GameSettings.NumberOfStars is the generator's
        // target, which the 12-ly separation sweep can leave slightly under). The server's own
        // condition 1 uses AllStars.Count; this is the closest client-visible equivalent.
        ConditionLines = VictorySummary.ConditionLines(settings, settings?.NumberOfStars ?? 0)
            .Select(line => new VictoryConditionLineViewModel(line))
            .ToList();

        OnPropertyChanged(nameof(HasPlayers));
    }

    /// <summary>The player's race name: the viewer's own from its empire state, another empire's
    /// from the empire report. Empty lets VictorySummary fall back to "Empire N".</summary>
    private string NameFor(int empireId)
    {
        if (clientState == null)
        {
            return string.Empty;
        }

        if (clientState.EmpireState.Id == empireId)
        {
            return clientState.EmpireState.Race.Name;
        }

        if (clientState.EmpireState.EmpireReports.TryGetValue((ushort)empireId, out EmpireIntel? report)
            && !string.IsNullOrEmpty(report.RaceName))
        {
            return report.RaceName;
        }

        return string.Empty;
    }
}

/// <summary>One player's line: the name and the seven check columns.</summary>
public sealed class VictoryConditionsPlayerRowViewModel
{
    public VictoryConditionsPlayerRowViewModel(VictoryPlayerRow row)
    {
        Name = row.Name;
        IsOut = row.Out;
        IsWinner = row.Winner;
        NameForeground = row.Out
            ? Brushes.Gray
            : (row.Winner ? Brushes.DodgerBlue : Brushes.Black);
        Marks = row.Marks.Select(mark => new VictoryConditionMarkViewModel(mark)).ToList();
    }

    public string Name { get; }

    /// <summary>True when the player is out of the game (the name is grey).</summary>
    public bool IsOut { get; }

    /// <summary>True when the player carries the winner mark (the name is blue).</summary>
    public bool IsWinner { get; }

    /// <summary>Grey for an out player, blue for the winner, black otherwise (section 3).</summary>
    public IBrush NameForeground { get; }

    /// <summary>The seven check columns, in the spec's order.</summary>
    public IReadOnlyList<VictoryConditionMarkViewModel> Marks { get; }
}

/// <summary>One check column cell: the check mark, if drawn, in its shade.</summary>
public sealed class VictoryConditionMarkViewModel
{
    public VictoryConditionMarkViewModel(VictoryConditionMark mark)
    {
        Shade = mark.Shade;
        // The check is drawn only where the record's met bit is set; the glyph matches the
        // Messages panel's own check (U+2714).
        Text = mark.Met ? "\u2714" : string.Empty;
        Foreground = mark.Shade == VictoryCheckShade.Grey
            ? Brushes.Gray
            : mark.Shade == VictoryCheckShade.Blue
                ? Brushes.DodgerBlue
                : mark.Shade == VictoryCheckShade.Black
                    ? Brushes.Black
                    : Brushes.Transparent;
    }

    public string Text { get; }

    public VictoryCheckShade Shade { get; }

    public IBrush Foreground { get; }
}

/// <summary>One of the nine condition-list lines, with its shade and the extra gap before line 8.</summary>
public sealed class VictoryConditionLineViewModel
{
    public VictoryConditionLineViewModel(VictoryConditionLine line)
    {
        Number = line.Number;
        Text = line.Text;
        IsGrey = line.Grey;
        TopMargin = line.ExtraGapBefore ? new Thickness(0, 8, 0, 0) : new Thickness(0);
    }

    public int Number { get; }

    public string Text { get; }

    /// <summary>Lines 1-7 grey when their condition is disabled; lines 8 and 9 are always black.</summary>
    public bool IsGrey { get; }

    /// <summary>Grey for a disabled condition's line, black otherwise (section 3).</summary>
    public IBrush Foreground => IsGrey ? Brushes.Gray : Brushes.Black;

    /// <summary>Line 8's extra half-line gap.</summary>
    public Thickness TopMargin { get; }
}
