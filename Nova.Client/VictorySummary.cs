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

    /// <summary>One line of the in-session victory-conditions settings summary.</summary>
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
    /// The shade a per-player condition check is drawn in - the victory-conditions view of
    /// behavior-specs-11/victory-conditions.md section 3. A check is drawn only where the score
    /// record's met bit for that condition is set (bits 6-12 of the record header, i.e. bits 0-6 of
    /// <see cref="ScoreRecord.MetMask"/>); it is grey under a condition that is not enabled or on a
    /// player who is out, blue for a player carrying the winner mark, black otherwise.
    /// </summary>
    public enum VictoryCheckShade
    {
        /// <summary>No check is drawn (the condition's met bit is clear).</summary>
        None,

        /// <summary>The disabled-text grey: the condition is not enabled, or the player is out.</summary>
        Grey,

        /// <summary>An enabled condition, on a player without the winner mark.</summary>
        Black,

        /// <summary>An enabled condition, on a player carrying the winner mark.</summary>
        Blue,
    }

    /// <summary>
    /// One of the seven check columns on a player's line, in the spec's order: owned-planets share,
    /// tech level, score, lead over second place, production capacity, capital ships, highest score
    /// after the set years (victory-conditions.md section 3).
    /// </summary>
    public sealed class VictoryConditionMark
    {
        public VictoryConditionMark(bool met, bool enabled, bool winner, bool outOfGame)
        {
            Met = met;
            Enabled = enabled;
            Winner = winner;
            Out = outOfGame;

            if (!met)
            {
                Shade = VictoryCheckShade.None;
            }
            else if (outOfGame || !enabled)
            {
                // "A check under a condition that is not enabled, or on a player who is out, is
                // drawn in the grey of disabled text" (section 3).
                Shade = VictoryCheckShade.Grey;
            }
            else if (winner)
            {
                // "a check under an enabled condition is black, or blue for a player with the
                // winner mark" (section 3).
                Shade = VictoryCheckShade.Blue;
            }
            else
            {
                Shade = VictoryCheckShade.Black;
            }
        }

        /// <summary>Whether the record's met bit for this condition is set.</summary>
        public bool Met { get; }

        /// <summary>Whether the corresponding condition is enabled in the game settings.</summary>
        public bool Enabled { get; }

        /// <summary>Whether the player carries the persistent winner mark.</summary>
        public bool Winner { get; }

        /// <summary>Whether the player is out of the game.</summary>
        public bool Out { get; }

        public VictoryCheckShade Shade { get; }
    }

    /// <summary>One player's line in the victory-conditions view: the name plus the seven check columns.</summary>
    public sealed class VictoryPlayerRow
    {
        public VictoryPlayerRow(string name, bool outOfGame, bool winner, IReadOnlyList<VictoryConditionMark> marks)
        {
            Name = name;
            Out = outOfGame;
            Winner = winner;
            Marks = marks;
        }

        /// <summary>The player's race name (or a stand-in when no report names it).</summary>
        public string Name { get; }

        /// <summary>Grey name when the player is out of the game.</summary>
        public bool Out { get; }

        /// <summary>Blue name when the player carries the winner mark.</summary>
        public bool Winner { get; }

        /// <summary>The seven check columns, in the spec's order.</summary>
        public IReadOnlyList<VictoryConditionMark> Marks { get; }
    }

    /// <summary>
    /// One of the nine condition-list lines drawn below the player lines
    /// (victory-conditions.md section 3, the table of lines 1-9).
    /// </summary>
    public sealed class VictoryConditionLine
    {
        public VictoryConditionLine(int number, string text, bool grey, bool extraGapBefore = false)
        {
            Number = number;
            Text = text;
            Grey = grey;
            ExtraGapBefore = extraGapBefore;
        }

        /// <summary>The line number, 1-9.</summary>
        public int Number { get; }

        public string Text { get; }

        /// <summary>Lines 1-7 are grey when their condition is disabled; lines 8 and 9 are always black.</summary>
        public bool Grey { get; }

        /// <summary>Line 8 is drawn with an extra half-line gap before it (section 3).</summary>
        public bool ExtraGapBefore { get; }
    }

    /// <summary>
    /// The in-session Victory Conditions view (behavior-specs-11/victory-conditions.md section 3:
    /// the Score window's victory-conditions view - one line per player with seven check columns
    /// over a nine-line condition list; distinct from the setup wizard's page, which
    /// <see cref="Rows"/> still serves). The per-player marks come from the client's own intel
    /// score records (<c>ClientData.InputTurn.AllScores</c>), the same records the score screen
    /// reads. Read-only: built from the game's settings (<see cref="GameSettings"/>, the same
    /// record VictoryCheck evaluates).
    ///
    /// SPEC GAP (behavior-specs-11/victory-conditions.md section 3): the dialog's own strings
    /// (938-963 and 965) were not recovered, so the line wording here is a paraphrase of the
    /// section's own "Meaning and value shown" column and is a named seam, not the original text.
    ///
    /// SEAM (section 3): the view greys a player who "is out of the game", but the client's score
    /// record carries no eliminated flag (the flag lives on the server's EmpireData). The four
    /// figure words that the server's own elimination test reads (Planets, Unarmed ships, Escort
    /// ships, Capital ships, all zero) do reach the client, so <see cref="IsOut"/> derives the
    /// same test client-side. No field was added outside these files.
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

        /// <summary>
        /// The seven toggles in the spec's check-column order:
        /// owned-planets share, tech level, score, lead over second place, production capacity,
        /// capital ships, highest score after the set years (victory-conditions.md section 2,
        /// "Which bits the evaluation sets", bits 6-12; condition 3 has no column of its own).
        /// </summary>
        public static bool[] EnabledConditions(GameSettings settings)
        {
            if (settings == null)
            {
                return new bool[7];
            }

            return new[]
            {
                settings.PlanetsOwned.IsChecked,
                settings.TechLevels.IsChecked,
                settings.TotalScore.IsChecked,
                settings.SecondPlaceScore.IsChecked,
                settings.ProductionCapacity.IsChecked,
                settings.CapitalShips.IsChecked,
                settings.HighestScore.IsChecked,
            };
        }

        /// <summary>
        /// Whether a player is out of the game, from the only elimination evidence the client's
        /// score record carries: no planets and no ships of any class (the server's own test in
        /// VictoryCheck.IsEliminated, victory-conditions.md section 2, "Elimination flag"). The
        /// eliminated flag itself is not sent per record; see this class's SEAM note.
        /// </summary>
        public static bool IsOut(ScoreRecord record)
        {
            if (record == null)
            {
                return false;
            }

            return record.Planets == 0
                && record.UnarmedShips == 0
                && record.EscortShips == 0
                && record.CapitalShips == 0;
        }

        /// <summary>
        /// One line per player, in the order of <paramref name="scores"/> (the client's visible
        /// score records), with the seven check columns. <paramref name="nameFor"/> resolves a
        /// player id to its race name; a null or empty result falls back to "Empire N".
        /// </summary>
        public static List<VictoryPlayerRow> PlayerRows(GameSettings settings, IReadOnlyList<ScoreRecord> scores, Func<int, string> nameFor)
        {
            List<VictoryPlayerRow> rows = new List<VictoryPlayerRow>();
            if (scores == null)
            {
                return rows;
            }

            bool[] enabled = EnabledConditions(settings);

            foreach (ScoreRecord record in scores)
            {
                bool outOfGame = IsOut(record);
                bool winner = record.Winner;

                List<VictoryConditionMark> marks = new List<VictoryConditionMark>(7);
                for (int bit = 0; bit < 7; bit++)
                {
                    bool met = (record.MetMask & (1 << bit)) != 0;
                    marks.Add(new VictoryConditionMark(met, enabled[bit], winner, outOfGame));
                }

                string name = nameFor != null ? nameFor(record.EmpireId) : null;
                if (string.IsNullOrEmpty(name))
                {
                    name = "Empire " + record.EmpireId.ToString(CultureInfo.InvariantCulture);
                }

                rows.Add(new VictoryPlayerRow(name, outOfGame, winner, marks));
            }

            return rows;
        }

        /// <summary>
        /// The nine condition-list lines (victory-conditions.md section 3, the line table). Lines
        /// 1-7 are grey when their condition is disabled; lines 8 and 9 are always black, and line
        /// 8 takes an extra gap before it. <paramref name="planetsInUniverse"/> is the number of
        /// planets in the galaxy that line 1 scales the percentage against; line 1 prints
        /// N = percentage x planets / 100 rounded down.
        /// </summary>
        public static List<VictoryConditionLine> ConditionLines(GameSettings settings, int planetsInUniverse)
        {
            List<VictoryConditionLine> lines = new List<VictoryConditionLine>();
            if (settings == null)
            {
                return lines;
            }

            int planets = Math.Max(0, planetsInUniverse);
            int owned = (settings.PlanetsOwned.NumericValue * planets) / 100;

            lines.Add(new VictoryConditionLine(1, "Owns " + owned.ToString(CultureInfo.InvariantCulture) + " planets",
                !settings.PlanetsOwned.IsChecked));
            lines.Add(new VictoryConditionLine(2, "Attains tech level " + settings.TechLevels.NumericValue.ToString(CultureInfo.InvariantCulture)
                + " in " + settings.NumberOfFields.NumericValue.ToString(CultureInfo.InvariantCulture) + " fields",
                !settings.TechLevels.IsChecked));
            lines.Add(new VictoryConditionLine(3, "Exceeds a score of " + settings.TotalScore.NumericValue.ToString(CultureInfo.InvariantCulture),
                !settings.TotalScore.IsChecked));
            lines.Add(new VictoryConditionLine(4, "Exceeds the second-place score by " + settings.SecondPlaceScore.NumericValue.ToString(CultureInfo.InvariantCulture) + "%",
                !settings.SecondPlaceScore.IsChecked));
            lines.Add(new VictoryConditionLine(5, "Has a production capacity of " + settings.ProductionCapacity.NumericValue.ToString(CultureInfo.InvariantCulture) + " thousand",
                !settings.ProductionCapacity.IsChecked));
            lines.Add(new VictoryConditionLine(6, "Owns " + settings.CapitalShips.NumericValue.ToString(CultureInfo.InvariantCulture) + " capital ships",
                !settings.CapitalShips.IsChecked));
            lines.Add(new VictoryConditionLine(7, "Has the highest score after " + settings.HighestScore.NumericValue.ToString(CultureInfo.InvariantCulture) + " years",
                !settings.HighestScore.IsChecked));
            lines.Add(new VictoryConditionLine(8, "The winner must meet " + ConditionsToMeet(settings).ToString(CultureInfo.InvariantCulture)
                + " of the selected criteria", grey: false, extraGapBefore: true));
            lines.Add(new VictoryConditionLine(9, "At least " + settings.MinimumGameTime.ToString(CultureInfo.InvariantCulture)
                + " years must pass before a winner is declared", grey: false));

            return lines;
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
