#region Copyright Notice
// ============================================================================
// Copyright (C) 2009 - 2017 stars-nova
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

namespace Nova.Ai
{
    using Nova.Common;

    /// <summary>
    /// The "what would it cost to get there" query (behavior-specs-10/research-tech-tree.md §7's
    /// detail-card note, cost routine `FUN_10d8_4a86`; ai-opponent-behavior.md §9 names it as the
    /// helper the retracted "design tech-upgrade budgeting" text was describing): the research
    /// resources still needed to bring every field below a target up to it - the cost of every
    /// missing level, field by field (Energy, Weapons, Propulsion, Construction, Electronics,
    /// Biotechnology), each level priced at the simulated levels reached so far, less what is
    /// already banked in that field and never below zero per field. The caller's levels are not
    /// changed (the original saves and restores them around the simulation).
    /// </summary>
    /// <remarks>
    /// The §9 ambition ladder (59/71/84/95/108 summed levels, 5,000 / 3,500 budgets) is NOT an AI
    /// rule: §9 attributes it to the Mystery Trader encounter, so it is deliberately not ported
    /// here. "Slow Tech Advance" (cost doubled) is not modelled because GameSettings has no such
    /// option.
    /// </remarks>
    public static class ResearchCostToReach
    {
        /// <summary>Returned when a target level is above 26 (an item that cannot be
        /// researched); the detail card shows it as "unavailable".</summary>
        public const long Unreachable = -1;

        private static readonly TechLevel.ResearchField[] SpecOrder =
        {
            TechLevel.ResearchField.Energy, TechLevel.ResearchField.Weapons, TechLevel.ResearchField.Propulsion,
            TechLevel.ResearchField.Construction, TechLevel.ResearchField.Electronics, TechLevel.ResearchField.Biotechnology
        };

        /// <param name="race">Supplies the per-field cost factors.</param>
        /// <param name="current">The empire's current levels (not modified).</param>
        /// <param name="banked">Resources already banked toward the next level of each field
        /// (EmpireData.ResearchResources); null for none.</param>
        /// <param name="target">The required levels, e.g. a component's RequiredTech.</param>
        public static long Cost(Race race, TechLevel current, TechLevel banked, TechLevel target)
        {
            foreach (TechLevel.ResearchField field in SpecOrder)
            {
                if (target[field] > TechLevel.MaxLevel)
                {
                    return Unreachable;
                }
            }

            TechLevel simulated = current.Clone();
            long total = 0;
            foreach (TechLevel.ResearchField field in SpecOrder)
            {
                long fieldCost = 0;
                while (simulated[field] < target[field])
                {
                    fieldCost += Research.Cost(field, race, simulated, simulated[field] + 1);
                    simulated[field] = simulated[field] + 1;
                }

                if (fieldCost > 0 && banked != null)
                {
                    fieldCost -= banked[field];
                }

                if (fieldCost > 0)
                {
                    total += fieldCost;
                }
            }

            return total;
        }

        /// <summary>The query for an empire's own race, levels and banked resources.</summary>
        public static long Cost(EmpireData empire, TechLevel target)
        {
            return Cost(empire.Race, empire.ResearchLevels, empire.ResearchResources, target);
        }
    }
}
