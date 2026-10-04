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
        /// The PRT-conditional exclusion from the Research dialog's field-selection candidates
        /// (research-tech-tree.md section 7, FUN_10d0_010c): Alternate Reality's list skips the
        /// first three fields of the original's order (Energy, Weapons, Propulsion) and Claim
        /// Adjuster's skips the last two (Electronics, Biotechnology). The spec leaves open
        /// whether "excluded" hides a field entirely; here it only keeps the field out of the
        /// automatic "lowest field" choice - a field can always still be picked by hand.
        /// </summary>
        public static bool IsExcludedFromFieldList(Race race, TechLevel.ResearchField field)
        {
            if (race == null)
            {
                return false;
            }

            if (race.HasTrait("AR"))
            {
                return field == TechLevel.ResearchField.Energy
                    || field == TechLevel.ResearchField.Weapons
                    || field == TechLevel.ResearchField.Propulsion;
            }

            if (race.HasTrait("CA"))
            {
                return field == TechLevel.ResearchField.Electronics
                    || field == TechLevel.ResearchField.Biotechnology;
            }

            return false;
        }

        /// <summary>
        /// The "lowest field" auto-target (research-tech-tree.md section 4): the field with the
        /// lowest level, among fields not excluded for the race's PRT and not yet at the top
        /// level; ties go to the earlier field in the original's order. Null if none qualifies.
        /// </summary>
        public static TechLevel.ResearchField? LowestField(TechLevel levels, Race race)
        {
            TechLevel.ResearchField? lowest = null;
            foreach (TechLevel.ResearchField field in OriginalFieldOrder)
            {
                if (levels[field] >= TechLevel.MaxLevel || IsExcludedFromFieldList(race, field))
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
        /// <see cref="NextFieldLowest"/>). Null means "stay on the current field".
        /// </summary>
        public static TechLevel.ResearchField? NextTarget(int nextFieldSetting, TechLevel levels, Race race)
        {
            if (nextFieldSetting == NextFieldLowest)
            {
                return LowestField(levels, race);
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
