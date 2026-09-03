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
            int techAjustment = 0;

            foreach (int levelAttained in totalLevels)
            {
                techAjustment += levelAttained * 10;
            }

            int baseCost = BaseCost[level] + techAjustment;
            int costFactor = race.ResearchCosts[field];

            return (baseCost * costFactor) / 100;
        }
    }
}
