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

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common.Components;

    /// <summary>
    /// Acquiring tech levels from other empires by scrapping, destroying in battle, or invading
    /// — independent of resources spent on research. See
    /// docs/behavior-specs/research-tech-tree.md §6.
    /// </summary>
    /// <remarks>
    /// Only one tech level, from one field, from one source, can be gained per turn regardless
    /// of how many qualifying events occur (tracked via EmpireData.TechGainedThisTurn, reset at
    /// the start of each turn in TurnGenerator.Generate()).
    /// </remarks>
    public static class TechTrading
    {
        private static readonly Random Rand = new Random();

        /// <summary>
        /// The highest tech level required by any component (including the hull itself) across
        /// every ship in the fleet, per field — the "source" tech profile a receiving empire's
        /// own levels are compared against.
        /// </summary>
        public static TechLevel HighestRequiredTech(Fleet fleet)
        {
            TechLevel highest = new TechLevel(0);

            foreach (ShipToken token in fleet.Composition.Values)
            {
                AccumulateMax(highest, token.Design.Blueprint.RequiredTech);

                foreach (HullModule module in token.Design.Hull.Modules)
                {
                    if (module.AllocatedComponent != null)
                    {
                        AccumulateMax(highest, module.AllocatedComponent.RequiredTech);
                    }
                }
            }

            return highest;
        }

        private static void AccumulateMax(TechLevel highest, TechLevel candidate)
        {
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (candidate[field] > highest[field])
                {
                    highest[field] = candidate[field];
                }
            }
        }

        /// <summary>
        /// Attempts to grant <paramref name="receiver"/> one traded tech level, given the tech
        /// profile of whatever was scrapped/destroyed/invaded. First a flat 50% chance that
        /// nothing at all is learned; if that passes, each field the source is strictly ahead in
        /// gets an independent 50% chance of being the one learned (so P(learn) = 0.5*(1-0.5^n)
        /// for n advantaged fields). No-ops if the receiver already gained a tech level this turn.
        /// </summary>
        /// <returns>The field learned, or null if nothing was learned this attempt.</returns>
        public static TechLevel.ResearchField? AttemptTechGain(EmpireData receiver, TechLevel sourceRequiredTech)
        {
            if (receiver.TechGainedThisTurn)
            {
                return null;
            }

            if (Rand.Next(2) != 0)
            {
                return null;
            }

            List<TechLevel.ResearchField> advantagedFields = new List<TechLevel.ResearchField>();
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (sourceRequiredTech[field] > receiver.ResearchLevels[field])
                {
                    advantagedFields.Add(field);
                }
            }

            foreach (TechLevel.ResearchField field in advantagedFields.OrderBy(f => Rand.Next()))
            {
                if (Rand.Next(2) == 0)
                {
                    receiver.ResearchLevels[field]++;
                    receiver.TechGainedThisTurn = true;
                    return field;
                }
            }

            return null;
        }
    }
}
