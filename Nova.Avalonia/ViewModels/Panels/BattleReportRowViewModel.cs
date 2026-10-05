using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nova.Common;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Battle Report list - ports BattleReport.cs's (BattleReportDialog) OnLoad
/// column-by-column. Also carries the underlying BattleReport so selecting a row can drive a
/// step-by-step log (see BattleReportViewModel.BuildStepLog), replacing the original's separate
/// double-click-to-open BattleViewer dialog.
///
/// The column set and order follow behavior-specs-11/client-ui-dialog-catalog.md "Reports"
/// (dynamic strings 1162-1176, fifteen columns): location, starbase present, sides, units, ours,
/// theirs, unarmed, scout, warship, bomber, utility, our dead, their dead, ours left, theirs left.
/// SPEC GAP: the spec names the columns but not the value each battle column renders, so the
/// unlabelled semantics below ("units" = total starting ships; "ours"/"theirs" = starting ships per
/// side; "... left" = starting minus the report's losses) are the port's best reading.
/// </summary>
public class BattleReportRowViewModel
{
    public BattleReport Report { get; }

    public string Location { get; }

    public string StarbasePresent { get; }

    public string Sides { get; }

    public string Units { get; }

    public string Ours { get; }

    public string Theirs { get; }

    public string Unarmed { get; }

    public string Scout { get; }

    public string Warship { get; }

    public string Bomber { get; }

    public string Utility { get; }

    public string OurDead { get; }

    public string TheirDead { get; }

    public string OursLeft { get; }

    public string TheirsLeft { get; }

    public BattleReportRowViewModel(BattleReport report, ushort empireId)
    {
        Report = report;
        Location = report.Location;

        var countSides = new HashSet<ushort>();
        int ourShips = 0;
        int theirShips = 0;
        bool starbasePresent = false;

        foreach (Stack stack in report.Stacks.Values)
        {
            countSides.Add(stack.Owner);

            // A stack holds exactly one ship token, so its ship count is that token's quantity
            // (the previous reading used Composition.Count, which is always 1 per stack).
            int quantity = stack.Token == null ? 0 : stack.Token.Quantity;
            if (stack.Token != null && stack.Token.Design.IsStarbase)
            {
                starbasePresent = true;
            }

            if (stack.Owner == empireId)
            {
                ourShips += quantity;
            }
            else
            {
                theirShips += quantity;
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

        StarbasePresent = starbasePresent ? "Yes" : "No";
        Sides = countSides.Count.ToString(CultureInfo.InvariantCulture);
        Units = (ourShips + theirShips).ToString(CultureInfo.InvariantCulture);
        Ours = ourShips.ToString(CultureInfo.InvariantCulture);
        Theirs = theirShips.ToString(CultureInfo.InvariantCulture);

        // SEAM / SPEC GAP: class counts need the design's hull class, which the port does not model
        // (see ShipHullClass). The columns render the shared unknown marker.
        Unarmed = ShipHullClass.Unrecovered;
        Scout = ShipHullClass.Unrecovered;
        Warship = ShipHullClass.Unrecovered;
        Bomber = ShipHullClass.Unrecovered;
        Utility = ShipHullClass.Unrecovered;

        OurDead = ourLosses.ToString(CultureInfo.InvariantCulture);
        TheirDead = theirLosses.ToString(CultureInfo.InvariantCulture);
        OursLeft = System.Math.Max(0, ourShips - ourLosses).ToString(CultureInfo.InvariantCulture);
        TheirsLeft = System.Math.Max(0, theirShips - theirLosses).ToString(CultureInfo.InvariantCulture);
    }
}
