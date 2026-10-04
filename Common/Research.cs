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

#region Module Description
// ===========================================================================
// A class with static methods for handling research.
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.Collections;

    public class Research
    {
        /// <summary>
        /// Base cost (before the totalLevels surcharge and per-field cost factor) to reach each
        /// tech level, indexed 1-26. Index 0 is unused/zero. Sourced from starsfaq.com's "Guts of
        /// research costs" (credited to Bob Martin) — see docs/behavior-specs/research-tech-tree.md
        /// §3. The sequence approximates Fibonacci growth through ~level 12, then flattens.
        /// </summary>
        private static readonly int[] BaseCost =
        {
            0,
            50, 80, 130, 210, 340, 550, 890, 1440, 2330, 3770,
            6100, 9870, 13850, 18040, 22440, 27050, 31870, 36900, 42140, 47590,
            53250, 59120, 65200, 71490, 77990, 84700
        };

        /// <summary>
        /// Return the total resource cost for researching a level (taking into account
        /// the cost factor specified in the race designer and the empire's total tech
        /// investment across all fields).
        /// </summary>
        /// <param name="level">The level to be researched (1-26).</param>
        /// <returns>The resource cost to reach that level.</returns>
        public static int Cost(TechLevel.ResearchField field, Race race, TechLevel totalLevels, int level)
        {
            return Cost(field, race, totalLevels, level, GameSettings.Data.SlowTechAdvance);
        }

        /// <summary>
        /// As <see cref="Cost(TechLevel.ResearchField, Race, TechLevel, int)"/>, with the game's
        /// "Slow Tech Advance" option given explicitly: when set, the finished cost is doubled
        /// as the very last step, after the per-field cost factor (behavior-specs-10/research-
        /// tech-tree.md section 3 and Open Questions, FUN_10d8_1580).
        /// </summary>
        public static int Cost(TechLevel.ResearchField field, Race race, TechLevel totalLevels, int level, bool slowTechAdvance)
        {
            int techAjustment = 0;

            foreach (int levelAttained in totalLevels)
            {
                techAjustment += levelAttained * 10;
            }

            int baseCost = BaseCost[level] + techAjustment;
            int costFactor = race.ResearchCosts[field];

            int cost = (baseCost * costFactor) / 100;
            return slowTechAdvance ? cost * 2 : cost;
        }

        /// <summary>"Next field to research": stay on the current field (the default).</summary>
        public const int NextFieldSame = -1;

        /// <summary>"Next field to research": the lowest field (research-tech-tree.md section 4).</summary>
        public const int NextFieldLowest = 6;

        /// <summary>The six fields in the original's order (Energy, Weapons, Propulsion,
        /// Construction, Electronics, Biotechnology) - the order of its field lists, and so the
        /// tie order of the "lowest field" choice.</summary>
        public static readonly TechLevel.ResearchField[] OriginalFieldOrder =
        {
            TechLevel.ResearchField.Energy,
            TechLevel.ResearchField.Weapons,
            TechLevel.ResearchField.Propulsion,
            TechLevel.ResearchField.Construction,
            TechLevel.ResearchField.Electronics,
            TechLevel.ResearchField.Biotechnology,
        };

        /// <summary>
        /// The "lowest field" auto-target (research-tech-tree.md section 4): the field with the
        /// lowest level among all six, ties going to the earlier field in the original's order;
        /// a field already at the top level is skipped. There is no PRT-conditional exclusion:
        /// the Research dialog's AR/CA field-list exclusion was a misreading and is withdrawn
        /// (section 7 - the 7-entry loop is the production catalog's auto-build list).
        /// </summary>
        public static TechLevel.ResearchField? LowestField(TechLevel levels)
        {
            TechLevel.ResearchField? lowest = null;
            foreach (TechLevel.ResearchField field in OriginalFieldOrder)
            {
                if (levels[field] >= TechLevel.MaxLevel)
                {
                    continue;
                }

                if (lowest == null || levels[field] < levels[lowest.Value])
                {
                    lowest = field;
                }
            }

            return lowest;
        }

        /// <summary>
        /// The field research switches to once the current target has gained a level, from the
        /// empire's "next field" setting (<see cref="NextFieldSame"/>, a field index, or
        /// <see cref="NextFieldLowest"/>). Null means "stay on the current field". The caller
        /// handles "same field" and its level-cap switch, since it knows the current field.
        /// </summary>
        public static TechLevel.ResearchField? NextTarget(int nextFieldSetting, TechLevel levels)
        {
            if (nextFieldSetting == NextFieldLowest)
            {
                return LowestField(levels);
            }

            if (nextFieldSetting >= 0 && nextFieldSetting < OriginalFieldOrder.Length
                && Enum.IsDefined(typeof(TechLevel.ResearchField), nextFieldSetting))
            {
                return (TechLevel.ResearchField)nextFieldSetting;
            }

            return null;
        }

        /// <summary>
        /// The fraction of a per-turn resource contribution that actually lands on the actively-
        /// researched field. Generalized Research splits every contribution 50% to the target
        /// field and 15% each to the other five (see the identical 0.5/0.15 split in
        /// ServerState/TurnSteps/StarUpdateStep.cs's ContributeResearch - the only other place
        /// this fraction is expressed; keep the two in sync if GR's split is ever revised).
        /// Non-GR races put the whole contribution into the target field.
        /// </summary>
        public static double TargetFieldContributionFraction(Race race)
        {
            return race.HasTrait("GR") ? 0.5 : 1.0;
        }
    }
}
