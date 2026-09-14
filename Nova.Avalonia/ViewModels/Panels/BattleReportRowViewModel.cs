using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nova.Common;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>One row in the Battle Report list - ports BattleReport.cs's (BattleReportDialog)
/// OnLoad column-by-column. Also carries the underlying BattleReport so selecting a row can
/// drive a step-by-step log (see BattleReportViewModel.BuildStepLog), replacing the original's
/// separate double-click-to-open BattleViewer dialog.</summary>
public class BattleReportRowViewModel
{
    public BattleReport Report { get; }

    public string Location { get; }

    public string Sides { get; }

    public string OurShips { get; }

    public string TheirShips { get; }

    public string OurLosses { get; }

    public string TheirLosses { get; }

    public BattleReportRowViewModel(BattleReport report, ushort empireId)
    {
        Report = report;
        Location = report.Location;

        var countSides = new HashSet<ushort>();
        int ourShips = 0;
        int theirShips = 0;

        foreach (Stack stack in report.Stacks.Values)
        {
            countSides.Add(stack.Owner);
            if (stack.Owner == empireId)
            {
                ourShips += stack.Composition.Count;
            }
            else
            {
                theirShips += stack.Composition.Count;
            }
        }

        int ourLosses = 0;
        int theirLosses = 0;

        foreach (long lossEmpireId in report.Losses.Keys)
        {
            if (lossEmpireId == empireId)
            {
                ourLosses += report.Losses[lossEmpireId];
            }
            else
            {
                theirLosses += report.Losses[lossEmpireId];
            }
        }

        Sides = countSides.Count.ToString(CultureInfo.InvariantCulture);
        OurShips = ourShips.ToString(CultureInfo.InvariantCulture);
        TheirShips = theirShips.ToString(CultureInfo.InvariantCulture);
        OurLosses = ourLosses.ToString(CultureInfo.InvariantCulture);
        TheirLosses = theirLosses.ToString(CultureInfo.InvariantCulture);
    }
}
