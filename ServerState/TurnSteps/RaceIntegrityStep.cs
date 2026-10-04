#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars! Nova.
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

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.RaceDefinition;

    /// <summary>
    /// The race-definition integrity check (behavior-specs-10/turn-generation-engine.md §1 step 13),
    /// run for every race after the pre-movement stage and before any movement.
    /// <list type="number">
    /// <item>(a) The stored race settings are clamped into their legal ranges - the slot table of
    ///   race-designer-ui-and-availability.md, "Economic-settings stage": colonists per resource
    ///   7-25 (x 100 here), factory output 5-15, factory cost 5-25, factories operated 5-25, mine
    ///   output 5-25, mine cost 2-15, mines operated 5-25, research cost class 0-2 per field.
    ///   (b) the growth rate is clamped to 1-20.</item>
    /// <item>(c) The advantage-point total of race-traits.md §1a is recomputed. A race is illegal
    ///   when that total is negative or a clamp had to change a value, and it is not a computer
    ///   player.</item>
    /// <item>An illegal race gets message 279, every other non-computer race message 386, and it is
    ///   degraded, re-scoring after each change, until its total reaches at least
    ///   <see cref="RepairedTotal"/>: colonists per resource raised one step (100 colonists) at a
    ///   time up to 2,500, then the growth rate lowered one point at a time to 1, then the six
    ///   research cost classes set to 0 ("costs 75% extra", 175% here) one field at a time in the
    ///   original's field order.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Port notes: the leftover-resources-to-research percentage of (b) (race offset 0x38, reset to
    /// 15 outside 0-100) has no field in this port's Race, and slot 7 (the leftover-point choice)
    /// is a name here, not a number, so neither is checked. Slot 14 (the PRT) is a trait object and
    /// cannot be out of range. A research cost that is not one of the three classes (50 / 100 /
    /// 175, or the legacy 150) is clamped into 50-175 numerically. The status bit 0x10 the original
    /// raises is not persisted: the spec says only a fresh out-of-range value or a negative total
    /// can trigger the notices again, which this step decides from the race's current values.
    /// A slot with no PlayerSettings record (so neither human nor computer can be told) and a race
    /// with no primary trait (an incomplete record) are skipped.
    /// Ambiguity (reported): "until its total reaches at least 500" is read in the units the
    /// legality test uses, the displayed advantage points (the calculator's total / 3).
    /// </remarks>
    public class RaceIntegrityStep : ITurnStep
    {
        /// <summary>The degrade loop stops once the total reaches this.</summary>
        public const int RepairedTotal = 500;

        public const int MinimumColonistsPerResource = 700;
        public const int MaximumColonistsPerResource = 2500;
        public const int ColonistsPerResourceStep = 100;

        /// <summary>Research cost class 0, "costs 75% extra", as this port stores it.</summary>
        public const int ExpensiveResearchCost = 175;
        public const int CheapResearchCost = 50;

        public const string MessageType = "Race Integrity";

        public void Process(ServerData serverState)
        {
            List<EmpireData> illegal = new List<EmpireData>();

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                bool? computer = IsComputerPlayer(serverState, empire);
                if (computer == null || computer.Value || empire.Race == null || empire.Race.Traits.Primary is null)
                {
                    continue;
                }

                if (CheckAndRepair(empire.Race))
                {
                    illegal.Add(empire);
                }
            }

            foreach (EmpireData offender in illegal)
            {
                // Message 279 to the race itself.
                serverState.AllMessages.Add(new Message(
                    offender.Id,
                    "Your race definition has been tampered with. It has been corrected and its settings lowered until it is legal again.",
                    MessageType,
                    null));

                // Message 386 to every other non-computer race.
                foreach (EmpireData other in serverState.AllEmpires.Values)
                {
                    if (other.Id != offender.Id && IsComputerPlayer(serverState, other) == false)
                    {
                        serverState.AllMessages.Add(new Message(
                            other.Id,
                            "A hacked race has been discovered: the " + offender.Race.PluralName + " race definition was illegal and has been corrected.",
                            MessageType,
                            null));
                    }
                }
            }
        }

        /// <summary>
        /// Clamps, re-scores and, when the race is illegal, degrades it (steps 13a-13c and the
        /// degrade loop). Returns true when the race was illegal (the caller sends the notices).
        /// </summary>
        public static bool CheckAndRepair(Race race)
        {
            bool changed = Clamp(race);
            int total = Score(race);

            if (total >= 0 && !changed)
            {
                return false;
            }

            Degrade(race, total);
            return true;
        }

        /// <summary>Steps 13a/13b. Returns true when any value had to change.</summary>
        public static bool Clamp(Race race)
        {
            bool changed = false;

            race.ColonistsPerResource = ClampValue(race.ColonistsPerResource, MinimumColonistsPerResource, MaximumColonistsPerResource, ref changed);
            race.FactoryProduction = ClampValue(race.FactoryProduction, 5, 15, ref changed);
            race.FactoryBuildCost = ClampValue(race.FactoryBuildCost, 5, 25, ref changed);
            race.OperableFactories = ClampValue(race.OperableFactories, 5, 25, ref changed);
            race.MineProductionRate = ClampValue(race.MineProductionRate, 5, 25, ref changed);
            race.MineBuildCost = ClampValue(race.MineBuildCost, 2, 15, ref changed);
            race.OperableMines = ClampValue(race.OperableMines, 5, 25, ref changed);

            foreach (TechLevel.ResearchField field in MysteryTraderStep.FieldOrder)
            {
                race.ResearchCosts[field] = ClampValue(race.ResearchCosts[field], CheapResearchCost, ExpensiveResearchCost, ref changed);
            }

            double growth = Math.Max(1, Math.Min(20, race.GrowthRate));
            if (growth != race.GrowthRate)
            {
                race.GrowthRate = growth;
                changed = true;
            }

            return changed;
        }

        /// <summary>The advantage-point total (race-traits.md §1a, the displayed figure).</summary>
        public static int Score(Race race)
        {
            return new RaceAdvantagePointCalculator().calculateAdvantagePoints(race);
        }

        /// <summary>
        /// The degrade loop: one change at a time, re-scored after each, until the total reaches
        /// <see cref="RepairedTotal"/> or there is nothing left to lower.
        /// </summary>
        public static void Degrade(Race race, int total)
        {
            while (total < RepairedTotal)
            {
                if (race.ColonistsPerResource < MaximumColonistsPerResource)
                {
                    race.ColonistsPerResource = Math.Min(MaximumColonistsPerResource, race.ColonistsPerResource + ColonistsPerResourceStep);
                }
                else if (race.GrowthRate > 1)
                {
                    race.GrowthRate = Math.Max(1, race.GrowthRate - 1);
                }
                else
                {
                    TechLevel.ResearchField? next = MysteryTraderStep.FieldOrder
                        .Cast<TechLevel.ResearchField?>()
                        .FirstOrDefault(field => race.ResearchCosts[field.Value] != ExpensiveResearchCost);
                    if (next == null)
                    {
                        return;
                    }

                    race.ResearchCosts[next.Value] = ExpensiveResearchCost;
                }

                total = Score(race);
            }
        }

        private static int ClampValue(int value, int minimum, int maximum, ref bool changed)
        {
            int clamped = Math.Max(minimum, Math.Min(maximum, value));
            if (clamped != value)
            {
                changed = true;
            }

            return clamped;
        }

        /// <summary>True for a computer player, false for a human, null when the slot has no PlayerSettings record.</summary>
        private static bool? IsComputerPlayer(ServerData serverState, EmpireData empire)
        {
            PlayerSettings settings = serverState.AllPlayers.FirstOrDefault(player => player.PlayerNumber == empire.Id);
            if (settings == null)
            {
                return null;
            }

            return !string.IsNullOrEmpty(settings.AiProgram) && settings.AiProgram != "Human";
        }
    }
}
