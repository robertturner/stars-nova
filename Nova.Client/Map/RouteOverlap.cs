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

namespace Nova.Client.Map
{
    using System.Collections.Generic;

    using Nova.Common.DataStructures;

    /// <summary>
    /// Route-overlap dashing (behavior-specs-10/client-interface.md, "Route-overlap overlay",
    /// view-option bit 0x80): when two legs of the plotted route share the same pair of endpoints
    /// (the same leg travelled again, or reversed), the overlapping leg is drawn with a dashed pen.
    /// Ambiguity (documented): "the overlapping leg" is read as every LATER leg whose endpoint pair
    /// was already used by an earlier leg; the first use stays solid. Zero-length legs never dash.
    /// </summary>
    public static class RouteOverlap
    {
        /// <summary>One flag per leg (leg i runs from waypoints[i] to waypoints[i + 1]).</summary>
        public static bool[] DashedLegs(IReadOnlyList<NovaPoint> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                return new bool[0];
            }

            var dashed = new bool[waypoints.Count - 1];
            var seen = new HashSet<(int, int, int, int)>();

            for (int i = 0; i < dashed.Length; i++)
            {
                NovaPoint a = waypoints[i];
                NovaPoint b = waypoints[i + 1];
                if (a.X == b.X && a.Y == b.Y)
                {
                    continue;
                }

                // Unordered endpoint pair, so a reversal (B to A after A to B) matches too.
                bool aFirst = a.X < b.X || (a.X == b.X && a.Y < b.Y);
                (int, int, int, int) key = aFirst ? (a.X, a.Y, b.X, b.Y) : (b.X, b.Y, a.X, a.Y);

                if (!seen.Add(key))
                {
                    dashed[i] = true;
                }
            }

            return dashed;
        }
    }
}
