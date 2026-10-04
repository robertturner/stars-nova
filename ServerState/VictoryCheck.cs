#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010 stars-nova
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

namespace Nova.Server
{
    using System.Collections.Generic;

    using Nova.Common;
    using Nova.Server;

    /// <summary>
    /// Check for a victor (doesn't mean the end of a game, though).
    /// </summary>
    public class VictoryCheck
    {
        private readonly ServerData serverState;
        private readonly Scores Scores;
        private bool messageSent;

        public VictoryCheck(ServerData serverState, Scores scores)
        {
            this.serverState = serverState;
            this.Scores = scores;
        }

        /// <summary>
        /// Check for victor.
        /// </summary>
        public void Victor()
        {
            // One score pass per evaluation: every condition below reads this same record set,
            // as the original's conditions all read the one per-race score buffer
            // (behavior-specs-11/victory-conditions.md section 2).
            List<ScoreRecord> allScores = Scores.GetScores();

            CheckEliminations(allScores);

            // check for last man standing - doesn't matter the year
            List<ushort> remainingEmpires = new List<ushort>();
            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Owner == Global.Nobody)
                {
                    continue;
                }
                if (!remainingEmpires.Contains(star.Owner))
                {
                    remainingEmpires.Add(star.Owner);
                }
            }

            if (remainingEmpires.Count == 1)
            {
                EmpireData empire = serverState.AllEmpires[remainingEmpires[0]];
                empire.Winner = true;
                ScoreRecord record = FindRecord(allScores, empire.Id);
                if (record != null)
                {
                    record.Winner = true;
                }

                if (!messageSent)
                {
                    messageSent = true;
                    DeclareWinner(empire);
                }

                return;
            }

            // The winner test runs only while at least two races remain (victory-conditions.md
            // section 2, "Which bits the evaluation sets"); a sole survivor is handled above and
            // none remaining has nothing to evaluate.
            if (remainingEmpires.Count < 2)
            {
                return;
            }

            int gameTime = serverState.TurnYear - Global.StartingYear;
            bool yearGateReached = gameTime >= GameSettings.Data.MinimumGameTime;
            int targetsToMeet = ConditionsToMeet();

            // Which conditions are enabled for 1-7 in the spec's bit order.
            bool[] enabled =
            {
                GameSettings.Data.PlanetsOwned.IsChecked,
                GameSettings.Data.TechLevels.IsChecked,
                GameSettings.Data.TotalScore.IsChecked,
                GameSettings.Data.SecondPlaceScore.IsChecked,
                GameSettings.Data.ProductionCapacity.IsChecked,
                GameSettings.Data.CapitalShips.IsChecked,
                GameSettings.Data.HighestScore.IsChecked,
            };

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                ScoreRecord record = FindRecord(allScores, empire.Id);
                if (record == null)
                {
                    continue;
                }

                // A currently met condition sets its bit whether or not it is enabled.
                int mask = 0;
                if (OccupiedPlanets(record))
                {
                    mask |= 1 << 0;
                }
                if (AttainedTechLevel(empire.Id))
                {
                    mask |= 1 << 1;
                }
                if (ScoreExceeded(record))
                {
                    mask |= 1 << 2;
                }
                if (ExceedsSecondPlace(record, allScores))
                {
                    mask |= 1 << 3;
                }
                if (ProductionCapacity(record))
                {
                    mask |= 1 << 4;
                }
                if (CapitalShips(record))
                {
                    mask |= 1 << 5;
                }
                if (HighestScore(record, allScores, gameTime))
                {
                    mask |= 1 << 6;
                }

                record.MetMask = mask;
                empire.ConditionsMetMask = mask;

                // The winner mark persists once set and is never cleared.
                if (empire.Winner)
                {
                    record.Winner = true;
                    continue;
                }

                if (!yearGateReached || targetsToMeet <= 0)
                {
                    continue;
                }

                int enabledMet = 0;
                for (int bit = 0; bit < enabled.Length; bit++)
                {
                    if (enabled[bit] && (mask & (1 << bit)) != 0)
                    {
                        enabledMet++;
                    }
                }

                if (enabledMet >= targetsToMeet)
                {
                    empire.Winner = true;
                    record.Winner = true;
                    if (!messageSent)
                    {
                        messageSent = true;
                        DeclareWinner(empire);
                    }
                }
            }
        }

        private void DeclareWinner(EmpireData empire)
        {
            Message message = new Message();
            message.Audience = Global.Everyone;
            message.Text = "The " + empire.Race.PluralName + " have won the game";
            serverState.AllMessages.Add(message);
        }

        /// <summary>
        /// Elimination (victory-conditions.md section 2, "Elimination flag"; the test itself is
        /// client-ui-dialog-catalog.md's: Planets, Unarmed, Escort and Capital all zero). A race
        /// newly meeting it has its persisted status bit set and every other race is told (message
        /// 187). The "you alone are left" message 188 goes to the one race still standing when an
        /// elimination leaves exactly one. The bit is raised once, so the notices are not repeated
        /// on later turns. Message 188's exact wording and trigger are not given by the spec; the
        /// text and the "sent when the elimination leaves a single survivor" rule are assumptions.
        /// </summary>
        private void CheckEliminations(List<ScoreRecord> allScores)
        {
            bool anyNewlyEliminated = false;

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                if (empire.Eliminated)
                {
                    continue;
                }

                ScoreRecord record = FindRecord(allScores, empire.Id);
                if (record == null || !IsEliminated(record))
                {
                    continue;
                }

                empire.Eliminated = true;
                anyNewlyEliminated = true;

                foreach (EmpireData other in serverState.AllEmpires.Values)
                {
                    if (other.Id == empire.Id)
                    {
                        continue;
                    }

                    Message message = new Message();
                    message.Audience = other.Id;
                    message.Text = "All traces of the " + empire.Race.PluralName +
                                   " have been eliminated from the galaxy.";
                    serverState.AllMessages.Add(message);
                }
            }

            if (!anyNewlyEliminated || serverState.AllEmpires.Count < 2)
            {
                return;
            }

            List<EmpireData> survivors = new List<EmpireData>();
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                if (!empire.Eliminated)
                {
                    survivors.Add(empire);
                }
            }

            if (survivors.Count == 1)
            {
                Message message = new Message();
                message.Audience = survivors[0].Id;
                message.Text = "All other races have been eliminated from the galaxy. The " +
                               survivors[0].Race.PluralName + " alone remain.";
                serverState.AllMessages.Add(message);
            }
        }

        /// <summary>
        /// The elimination test: no planets and no ships of any class.
        /// </summary>
        public static bool IsEliminated(ScoreRecord record)
        {
            return record.Planets == 0
                && record.UnarmedShips == 0
                && record.EscortShips == 0
                && record.CapitalShips == 0;
        }

        /// <summary>
        /// The derived "must meet N of the above" figure (victory-conditions.md section 1 table):
        /// min(stored figure, number of enabled conditions), condition 3 (NumberOfFields) not
        /// counted because it is not independently toggleable. The runtime compares against this
        /// derived figure (section 2), so a stored figure above the enabled count no longer makes
        /// victory unreachable. Held to at least 1 (the table's range is 1-7), so a game with no
        /// condition enabled has no condition winner. The seven counted toggles include condition
        /// 8, as Nova.Client.VictorySummary does (the spec's "among 1-7 excluding condition 3"
        /// against its stated range 1-7 is ambiguous on this point).
        /// </summary>
        private static int ConditionsToMeet()
        {
            GameSettings settings = GameSettings.Data;
            int enabled = 0;
            foreach (EnabledValue condition in new[]
            {
                settings.PlanetsOwned, settings.TechLevels, settings.TotalScore, settings.SecondPlaceScore,
                settings.ProductionCapacity, settings.CapitalShips, settings.HighestScore,
            })
            {
                if (condition != null && condition.IsChecked)
                {
                    enabled++;
                }
            }

            return System.Math.Max(1, System.Math.Min(settings.TargetsToMeet, enabled));
        }

        private static ScoreRecord FindRecord(List<ScoreRecord> allScores, int empireId)
        {
            foreach (ScoreRecord scoreDetail in allScores)
            {
                if (scoreDetail.EmpireId == empireId)
                {
                    return scoreDetail;
                }
            }
            return null;
        }

        /// <summary>
        /// Condition 1: the player owns the required percentage of planets (the record's Planets
        /// word). Computed whether or not the condition is enabled.
        /// </summary>
        private bool OccupiedPlanets(ScoreRecord record)
        {
            if (serverState.AllStars.Count == 0)
            {
                return false;
            }

            int percentage = (record.Planets * 100)
                           / serverState.AllStars.Count;

            return percentage >= GameSettings.Data.PlanetsOwned.NumericValue;
        }

        /// <summary>
        /// Check to see if the player has attained the required tech level in the
        /// specified number of fields.
        /// </summary>
        /// <param name="empireId">The empire to check.</param>
        /// <returns>True when the race has the required tech in enough fields, whether or not the
        /// condition is enabled.</returns>
        private bool AttainedTechLevel(int empireId)
        {
            int targetLevel = GameSettings.Data.TechLevels.NumericValue;

            // Condition 3 has no checkbox of its own (victory-conditions.md section 1): its
            // field count always applies while condition 2 is enabled, so NumberOfFields'
            // IsChecked flag is not read (it used to drop the requirement to one field).
            int numberOfFields = GameSettings.Data.NumberOfFields.NumericValue;

            int highestFields = 0;
            TechLevel raceTechLevels = serverState.AllEmpires[empireId].ResearchLevels;

            foreach (int level in raceTechLevels)
            {
                if (level >= targetLevel)
                {
                    highestFields++;
                }
            }

            return highestFields >= numberOfFields;
        }

        /// <summary>
        /// Condition 4: the player's Score is at least the required score.
        /// </summary>
        private bool ScoreExceeded(ScoreRecord record)
        {
            return record.Score >= GameSettings.Data.TotalScore.NumericValue;
        }

        /// <summary>
        /// Condition 6: production capacity of N thousand - the record's Resources figure (the
        /// summed resource OUTPUT of every owned planet) divided by 1,000
        /// (client-ui-dialog-catalog.md, "Score display"). Previously this summed each planet's
        /// floor(leftover ResourcesOnHand / 1000), which both used the wrong figure and lost up
        /// to 999 resources per planet to per-planet rounding.
        /// </summary>
        /// <returns>True when the required production capacity has been met, whether or not the
        /// condition is enabled.</returns>
        private bool ProductionCapacity(ScoreRecord record)
        {
            int capacity = record.Resources / 1000;
            return capacity >= GameSettings.Data.ProductionCapacity.NumericValue;
        }

        /// <summary>
        /// Condition 7: the record's Capital ships count (design weapon rating 2,000 or more)
        /// meets the required number.
        /// </summary>
        /// <returns>True when the required number of capital ships has been met, whether or not
        /// the condition is enabled.</returns>
        private bool CapitalShips(ScoreRecord record)
        {
            return record.CapitalShips >= GameSettings.Data.CapitalShips.NumericValue;
        }

        /// <summary>
        /// Condition 8: the player is the SOLE leader ("a 'sole leader after N years'
        /// comparison", victory-conditions.md section 2) after the specified number of years. A
        /// race tied for the top score does not qualify. (The previous loop broke out after the
        /// first record regardless of empire, so only whichever race happened to be listed first
        /// could ever be credited with its own score.)
        /// </summary>
        /// <param name="years">Number of game years/turns that have passed.</param>
        /// <returns>True when this race is the sole highest score after the set years, whether or
        /// not the condition is enabled.</returns>
        private bool HighestScore(ScoreRecord record, List<ScoreRecord> allScores, int years)
        {
            if (years < GameSettings.Data.HighestScore.NumericValue)
            {
                return false;
            }

            foreach (ScoreRecord scoreDetail in allScores)
            {
                if (scoreDetail.EmpireId != record.EmpireId && scoreDetail.Score >= record.Score)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Condition 5: the player's score exceeds the second-place score by the specified
        /// percentage. "Second place" is the best score among the OTHER races: ranks are shared
        /// (1 + the number of races with a strictly higher score), so there may be no race with
        /// Rank 2 at all (e.g. two tied leaders are both rank 1, the next race rank 3), and the
        /// old "Rank == 2" lookup would then treat second place as scoring 0.
        /// </summary>
        /// <returns>True when the second place score is exceeded by the required amount, whether
        /// or not the condition is enabled.</returns>
        private bool ExceedsSecondPlace(ScoreRecord record, List<ScoreRecord> allScores)
        {
            int ourScore = record.Score;
            int secondPlaceScore = 0;

            foreach (ScoreRecord scoreDetail in allScores)
            {
                if (scoreDetail.EmpireId != record.EmpireId && scoreDetail.Score > secondPlaceScore)
                {
                    secondPlaceScore = scoreDetail.Score;
                }
            }

            // NumericValue is a PERCENTAGE ("exceeds the second-place race's score by this
            // percentage" - docs/behavior-specs-4/victory-conditions.md's condition 5), so the
            // default of 100 should require double the second-place score, not a flat
            // multiplication by 100 (which made this condition effectively unreachable).
            long threshold = (long)secondPlaceScore * (100 + GameSettings.Data.SecondPlaceScore.NumericValue) / 100;

            return ourScore > threshold;
        }
    }
}
