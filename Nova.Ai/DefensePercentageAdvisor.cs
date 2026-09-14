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
    using System;

    /// <summary>
    /// Ports ai-opponent-behavior.md section 6's defense-percentage-target advisor: the AI does
    /// not act to close a defense shortfall every single turn - the spec describes the chance of
    /// acting as decreasing "the longer the gap has persisted", falling to a flat 5% once the
    /// planet is already at or above its target defense percentage.
    ///
    /// This AI runs as a stateless per-turn process (see ShipDesignRefresher's own class comment
    /// for the same constraint elsewhere in this rebuild) with nowhere to persist a per-planet
    /// "turns since the gap opened" counter, so the elapsed-time axis is reconstructed here from
    /// the current shortfall itself instead: a large shortfall (assumed to reflect a gap that only
    /// just opened - e.g. a freshly-colonized or freshly-attacked planet) gets a high acting
    /// chance, shrinking toward the spec's own 5% floor as the shortfall closes. This is an
    /// explicitly approximate stand-in for genuine turn-tracking, not a spec-verified formula.
    /// </summary>
    public static class DefensePercentageAdvisor
    {
        public const int MinActChancePercent = 5;

        /// <summary>The percent chance (0-100) that the AI acts this turn to close a defense
        /// shortfall, given the planet's current and maximum defenses.</summary>
        public static int ActChancePercent(int currentDefenses, int maxDefenses)
        {
            int defenseToBuild = maxDefenses - currentDefenses;
            if (defenseToBuild <= 0)
            {
                return MinActChancePercent;
            }

            return Math.Max(MinActChancePercent, (100 * defenseToBuild) / maxDefenses);
        }
    }
}
