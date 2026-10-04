#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars-Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;

    /// <summary>One line of the in-session victory-conditions summary.</summary>
    public sealed class VictorySummaryRow
    {
        public VictorySummaryRow(string condition, bool enabled, string threshold)
        {
            Condition = condition;
            Enabled = enabled;
            Threshold = threshold;
        }

        public string Condition { get; }

        public bool Enabled { get; }

        public string Threshold { get; }
    }

    /// <summary>
    /// The in-session Victory Conditions summary (behavior-specs-10/client-ui-dialog-catalog.md
    /// "Additional confirmed surfaces": distinct from the setup wizard's page; victory-conditions.md
    /// section 1: seven toggleable conditions with thresholds, condition 3 riding on condition 2,
    /// "must meet N of the above" = min(N, enabled count), and the minimum-years gate). Read-only:
    /// built from the game's settings (GameSettings, the same record VictoryCheck evaluates).
    /// SPEC GAP: the dialog's own labels are runtime strings that were not recovered; the wording
    /// is the same as this port's New Game page. What the dialog's "Switch" button switches is not
    /// described; it is not reproduced.
    /// </summary>
    public static class VictorySummary
    {
        public static List<VictorySummaryRow> Rows(GameSettings settings)
        {
            List<VictorySummaryRow> rows = new List<VictorySummaryRow>();
            if (settings == null)
            {
                return rows;
            }

            rows.Add(Row("Owns this percentage of all planets", settings.PlanetsOwned, value => value + "%"));
            rows.Add(Row("Attains a tech level in a number of fields", settings.TechLevels,
                value => "Level " + value + " in " + settings.NumberOfFields.NumericValue.ToString(CultureInfo.InvariantCulture) + " fields"));
            rows.Add(Row("Exceeds a score of", settings.TotalScore, value => value.ToString(CultureInfo.InvariantCulture)));
            rows.Add(Row("Exceeds second place's score by", settings.SecondPlaceScore, value => value + "%"));
            rows.Add(Row("Has a production capacity of", settings.ProductionCapacity, value => value + " thousand resources"));
            rows.Add(Row("Owns this many capital ships", settings.CapitalShips, value => value.ToString(CultureInfo.InvariantCulture)));
            rows.Add(Row("Has the highest score after", settings.HighestScore, value => value + " years"));
            return rows;
        }

        /// <summary>The enabled conditions among the seven toggleable ones.</summary>
        public static int EnabledCount(GameSettings settings)
        {
            return Rows(settings).Count(row => row.Enabled);
        }

        /// <summary>
        /// How many enabled conditions a race must meet at once: the stored figure, held to
        /// 1..(enabled count) (victory-conditions.md section 1, the derived meta-setting).
        /// </summary>
        public static int ConditionsToMeet(GameSettings settings)
        {
            if (settings == null)
            {
                return 1;
            }

            return Math.Max(1, Math.Min(settings.TargetsToMeet, Math.Max(1, EnabledCount(settings))));
        }

        /// <summary>"Winner must meet N of the above" line.</summary>
        public static string ConditionsLine(GameSettings settings)
        {
            return "A winner must meet " + ConditionsToMeet(settings).ToString(CultureInfo.InvariantCulture)
                + " of the " + EnabledCount(settings).ToString(CultureInfo.InvariantCulture) + " enabled conditions.";
        }

        /// <summary>The minimum-years gate line.</summary>
        public static string YearGateLine(GameSettings settings)
        {
            int years = settings?.MinimumGameTime ?? 0;
            return "No victory can be declared before " + years.ToString(CultureInfo.InvariantCulture)
                + " years have passed (year " + (Global.StartingYear + years).ToString(CultureInfo.InvariantCulture) + ").";
        }

        private static VictorySummaryRow Row(string condition, EnabledValue value, Func<int, string> format)
        {
            if (value == null)
            {
                return new VictorySummaryRow(condition, false, string.Empty);
            }

            return new VictorySummaryRow(condition, value.IsChecked, format(value.NumericValue));
        }
    }
}
