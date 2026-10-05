using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Read-only list of past battles - ports BattleReportDialog (BattleReport.cs). Selecting a
/// battle shows a step-by-step textual log in the same panel, replacing the original's
/// double-click-to-open modal BattleViewer: that dialog's real content is a hand-drawn ship-icon
/// battlefield stepped one BattleStep at a time by a "Next" button - a plain text log carries the
/// same information (who moved where, who shot whom for how much damage against which defence
/// layer, who was destroyed) without reimplementing the icon/position graphics, consistent with
/// how other WinForms-specific graphical UI has been simplified elsewhere in this port.
///
/// The fifteen-column grid follows behavior-specs-11/client-ui-dialog-catalog.md "Reports"
/// (dynamic strings 1162-1176) and supports the section's shared per-column show/hide and header
/// sort/reverse (dynamic strings 1133-1137).
/// </summary>
public class BattleReportViewModel : Tool
{
    private readonly ushort empireId;
    private readonly ReportTable<BattleReportRowViewModel> table;

    public ObservableCollection<BattleReportRowViewModel> Battles => table.Rows;

    public IReadOnlyList<ReportColumn> Columns => table.Columns;

    public string? SortColumnKey => table.SortColumnKey;

    public ReportSortDirection SortDirection => table.SortDirection;

    private BattleReportRowViewModel? selectedBattle;

    public BattleReportRowViewModel? SelectedBattle
    {
        get => selectedBattle;
        set
        {
            if (SetProperty(ref selectedBattle, value))
            {
                StepLog = value == null ? new List<string>() : BuildStepLog(value.Report);
            }
        }
    }

    private IReadOnlyList<string> stepLog = new List<string>();

    public IReadOnlyList<string> StepLog
    {
        get => stepLog;
        private set => SetProperty(ref stepLog, value);
    }

    public BattleReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        empireId = clientState.EmpireState.Id;

        IEnumerable<BattleReportRowViewModel> rows = clientState.EmpireState.BattleReports
            .Select(report => new BattleReportRowViewModel(report, empireId));

        table = new ReportTable<BattleReportRowViewModel>(rows, new (string Key, string Header, Func<BattleReportRowViewModel, string> Value)[]
        {
            ("Location", "Location", r => r.Location),
            ("StarbasePresent", "Starbase Present", r => r.StarbasePresent),
            ("Sides", "Sides", r => r.Sides),
            ("Units", "Units", r => r.Units),
            ("Ours", "Ours", r => r.Ours),
            ("Theirs", "Theirs", r => r.Theirs),
            ("Unarmed", "Unarmed", r => r.Unarmed),
            ("Scout", "Scout", r => r.Scout),
            ("Warship", "Warship", r => r.Warship),
            ("Bomber", "Bomber", r => r.Bomber),
            ("Utility", "Utility", r => r.Utility),
            ("OurDead", "Our Dead", r => r.OurDead),
            ("TheirDead", "Their Dead", r => r.TheirDead),
            ("OursLeft", "Ours Left", r => r.OursLeft),
            ("TheirsLeft", "Theirs Left", r => r.TheirsLeft),
        });

        // Title carries the row count and a plural marker (client-ui-dialog-catalog.md Reports
        // line 369; the recovered template is in ReportTitles).
        Title = ReportTitles.Summary("Battle", table.Rows.Count, "Battle");
    }

    public void Sort(string columnKey) => table.Sort(columnKey);

    public void ReverseSort() => table.ReverseSort();

    public void ToggleColumn(string columnKey) => table.ToggleColumn(columnKey);

    public bool IsColumnVisible(string columnKey) => table.Columns
        .First(column => column.Key == columnKey)
        .IsVisible;

    /// <summary>
    /// Selects the row for a specific BattleReport - used when a Messages panel battle message
    /// is tapped (see NovaDockFactory.CreateLayout's MessagesViewModel.BattleReplayRequested
    /// subscription) so the player lands directly on that battle's step log instead of having
    /// to find it by hand in the list. Reference equality is enough here since a message's
    /// Event and this panel's Battles are both ultimately resolved from the same
    /// clientState.EmpireState.BattleReports list (see Nova.Client.IntelReader.
    /// LinkIntelReferences).
    /// </summary>
    public void SelectBattle(BattleReport report)
    {
        SelectedBattle = Battles.FirstOrDefault(row => ReferenceEquals(row.Report, report));
    }

    /// <summary>
    /// One line per BattleStep, mirroring what BattleViewer's "Next Step" button reveals -
    /// movement, target acquisition, weapons fire (against shields or armor), and destruction -
    /// identifying each stack by its design name and quantity rather than by icon position.
    /// </summary>
    private List<string> BuildStepLog(BattleReport report)
    {
        var log = new List<string>();

        foreach (BattleStep step in report.Steps)
        {
            switch (step)
            {
                case BattleStepMovement movement:
                    log.Add($"{StackLabel(report, movement.StackKey)} moved to {movement.Position}.");
                    break;

                case BattleStepWeapons weapons:
                    string defence = weapons.Targeting == BattleStepWeapons.TokenDefence.Shields ? "shields" : "armor";
                    log.Add($"{StackLabel(report, weapons.WeaponTarget.StackKey)} fired {weapons.Damage:F1} damage at " +
                            $"{StackLabel(report, weapons.WeaponTarget.TargetKey)}'s {defence}.");
                    break;

                case BattleStepDestroy destroy:
                    log.Add($"{StackLabel(report, destroy.StackKey)} was destroyed.");
                    break;

                case BattleStepTarget target:
                    log.Add($"{StackLabel(report, target.StackKey)} targeted {StackLabel(report, target.TargetKey)}.");
                    break;

                default:
                    log.Add(step.Type);
                    break;
            }
        }

        return log;
    }

    private string StackLabel(BattleReport report, long stackKey)
    {
        if (!report.Stacks.TryGetValue(stackKey, out Stack? stack) || stack.Token == null)
        {
            return "An unknown stack";
        }

        string side = stack.Owner == empireId ? "Our" : "Enemy";
        return $"{side} {stack.Token.Design.Name} ({stack.Token.Quantity})";
    }
}
