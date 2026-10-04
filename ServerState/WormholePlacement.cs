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

namespace Nova.Server
{
    using System;
    using System.Collections.Generic;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The placement scorer for wormhole ends (behavior-specs-11/fleet-movement-scanning-cargo.md
    /// "Wormhole lifecycle, complete rule" item 2, original FUN_1118_0456): lower is better. 15
    /// when the point is outside the galaxy or exactly on a planet, fleet or another special
    /// object; otherwise the OR of an edge bit and per-object squared-distance tiers. A score of 0
    /// therefore means at least 28 ly from every planet, 30 ly from other wormholes, 70 ly from
    /// its partner and 10 ly from the edge. Used for a new game's initial placement (item 1) and
    /// for each end's annual jump (item 4).
    /// </summary>
    public static class WormholePlacement
    {
        /// <summary>The score reserved for "unusable": outside the galaxy or exactly on an object.</summary>
        public const int OutsideGalaxyScore = 15;

        /// <summary>
        /// The placement score of <paramref name="candidate"/> (item 2). <paramref name="partnerKey"/>
        /// is the other end of the same pair (0 <see cref="Global.Nobody"/> when it is not placed
        /// yet); <paramref name="excludedKey"/> is the end being placed (or jumped), which scores
        /// itself 0 and is otherwise ignored.
        /// </summary>
        public static int Score(
            NovaPoint candidate,
            int mapWidth,
            int mapHeight,
            IEnumerable<Star> planets,
            IEnumerable<Wormhole> wormholes,
            IEnumerable<Fleet> fleets,
            long partnerKey,
            long excludedKey)
        {
            if (candidate == null)
            {
                return OutsideGalaxyScore;
            }

            if (candidate.X < 0 || candidate.X > mapWidth || candidate.Y < 0 || candidate.Y > mapHeight)
            {
                return OutsideGalaxyScore;
            }

            int score = 0;

            // 4 when within 10 ly of an edge.
            if (candidate.X < 10 || candidate.X > mapWidth - 10 || candidate.Y < 10 || candidate.Y > mapHeight - 10)
            {
                score |= 4;
            }

            if (planets != null)
            {
                foreach (Star planet in planets)
                {
                    if (planet == null || planet.Position == null)
                    {
                        continue;
                    }

                    double squared = PointUtilities.DistanceSquare(candidate, planet.Position);
                    if (squared == 0)
                    {
                        return OutsideGalaxyScore;
                    }

                    if (squared < 25)
                    {
                        score |= 8;
                    }
                    else if (squared < 100)
                    {
                        score |= 4;
                    }
                    else if (squared < 400)
                    {
                        score |= 2;
                    }
                    else if (squared < 784)
                    {
                        score |= 1;
                    }
                }
            }

            if (wormholes != null)
            {
                foreach (Wormhole end in wormholes)
                {
                    if (end == null || end.Position == null || end.Key == excludedKey)
                    {
                        continue;
                    }

                    double squared = PointUtilities.DistanceSquare(candidate, end.Position);
                    if (squared == 0)
                    {
                        return OutsideGalaxyScore;
                    }

                    if (end.Key == partnerKey)
                    {
                        // The other end of its own pair: 8 < 25, 4 < 100, 2 < 900, 1 < 4,900.
                        if (squared < 25)
                        {
                            score |= 8;
                        }
                        else if (squared < 100)
                        {
                            score |= 4;
                        }
                        else if (squared < 900)
                        {
                            score |= 2;
                        }
                        else if (squared < 4900)
                        {
                            score |= 1;
                        }
                    }
                    else
                    {
                        // Every other wormhole end: 8 < 16, 4 < 64, 2 < 225, 1 < 900.
                        if (squared < 16)
                        {
                            score |= 8;
                        }
                        else if (squared < 64)
                        {
                            score |= 4;
                        }
                        else if (squared < 225)
                        {
                            score |= 2;
                        }
                        else if (squared < 900)
                        {
                            score |= 1;
                        }
                    }
                }
            }

            if (fleets != null)
            {
                foreach (Fleet fleet in fleets)
                {
                    if (fleet == null || fleet.Position == null)
                    {
                        continue;
                    }

                    if (PointUtilities.DistanceSquare(candidate, fleet.Position) == 0)
                    {
                        return OutsideGalaxyScore;
                    }
                }
            }

            return score;
        }

        /// <summary>
        /// The 100-draw rule of item 1: draw up to 100 uniform points over the galaxy and take the
        /// first that scores 0, otherwise the lowest-scoring one.
        /// </summary>
        public static NovaPoint DrawAnywhere(
            Random random,
            int mapWidth,
            int mapHeight,
            IEnumerable<Star> planets,
            IEnumerable<Wormhole> wormholes,
            IEnumerable<Fleet> fleets,
            long partnerKey,
            long excludedKey)
        {
            NovaPoint best = null;
            int bestScore = int.MaxValue;

            for (int draw = 0; draw < 100; draw++)
            {
                NovaPoint candidate = new NovaPoint(random.Next(0, mapWidth + 1), random.Next(0, mapHeight + 1));
                int score = Score(candidate, mapWidth, mapHeight, planets, wormholes, fleets, partnerKey, excludedKey);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }

                if (score == 0)
                {
                    break;
                }
            }

            return best;
        }

        /// <summary>
        /// The annual drift of item 4: up to 100 candidates offset from the year's starting
        /// position by a uniform -12 to +12 ly on each axis (the all-zero offset is skipped); the
        /// first that scores 0 is taken, otherwise the lowest-scoring one. Returns the origin when
        /// no candidate is usable.
        /// </summary>
        public static NovaPoint DrawDrift(
            NovaPoint origin,
            Random random,
            int mapWidth,
            int mapHeight,
            IEnumerable<Star> planets,
            IEnumerable<Wormhole> wormholes,
            IEnumerable<Fleet> fleets,
            long partnerKey,
            long excludedKey)
        {
            NovaPoint best = origin;
            int bestScore = int.MaxValue;

            for (int draw = 0; draw < 100; draw++)
            {
                int dx = random.Next(-12, 13);
                int dy = random.Next(-12, 13);
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                NovaPoint candidate = new NovaPoint(origin.X + dx, origin.Y + dy);
                int score = Score(candidate, mapWidth, mapHeight, planets, wormholes, fleets, partnerKey, excludedKey);
                if (score == OutsideGalaxyScore)
                {
                    continue; // outside the galaxy or exactly on an object: never a drift target
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }

                if (score == 0)
                {
                    break;
                }
            }

            return best;
        }
    }
}
