using System.Collections.Generic;
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
/// </summary>
public class BattleReportViewModel : Tool
{
    private readonly ushort empireId;

    public IReadOnlyList<BattleReportRowViewModel> Battles { get; }

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
        Title = title;
        empireId = clientState.EmpireState.Id;

        Battles = clientState.EmpireState.BattleReports
            .Select(report => new BattleReportRowViewModel(report, empireId))
            .ToList();
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
