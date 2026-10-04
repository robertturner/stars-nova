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
    using System.Collections.Generic;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>A wormhole end as the AI sees it.</summary>
    public class WormholeSighting : Mappable
    {
        /// <summary>The stability tier 0 ("Rock Solid") to 6, as last detected
        /// (WormholeIntel.StabilityTier, recorded by the scan step).</summary>
        public int StabilityTier { get; set; }

        /// <summary>This race's bit is set in the wormhole's per-race bitmask, word +10 (it has
        /// used it): WormholeIntel.UsedByUs, from Wormhole.UsedBy.</summary>
        public bool Known { get; set; }
    }

    /// <summary>
    /// The colony ships' wormhole diversion `FUN_1090_0c4c` (behavior-specs-10/ai-opponent-
    /// behavior.md §12, personality 0 "Colony ships" step 3; also personality 5, and personality
    /// 3 when its colony design's engine index is 2 or more): before year 120, a colony fleet at
    /// an own planet may divert to a wormhole instead of its colonization target.
    /// </summary>
    public static class WormholeDiversion
    {
        /// <summary>About 216 ly: squared distance at most 46,656.</summary>
        public const double MaxDistanceSquared = 46656;

        /// <summary>The diversion is offered only before this year.</summary>
        public const int LastYear = 120;

        /// <summary>
        /// The wormhole ends an empire has detected (EmpireData.WormholeReports) as the AI sees
        /// them. "Known" is the report's UsedByUs flag, which ScanStep refreshes every year from
        /// Wormhole.UsedBy (the per-race bitmask of ai-opponent-behavior.md §12; the engine sets
        /// a race's bit when one of its fleets transits the wormhole - which code sets the bit in
        /// the original is not named by the spec, so that trigger is an interpretation). The tier
        /// is WormholeIntel.StabilityTier, the tier the scan step recorded when the end was last
        /// detected, so a known wormhole scores 70 - 10 x that tier.
        /// </summary>
        public static List<WormholeSighting> Sightings(EmpireData empire)
        {
            List<WormholeSighting> sightings = new List<WormholeSighting>();
            if (empire?.WormholeReports == null)
            {
                return sightings;
            }

            foreach (WormholeIntel report in empire.WormholeReports.Values)
            {
                if (report?.Position == null)
                {
                    continue;
                }

                sightings.Add(new WormholeSighting
                {
                    Key = report.Key,
                    Name = string.IsNullOrEmpty(report.Name) ? "Wormhole " + report.Key.ToString(System.Globalization.CultureInfo.InvariantCulture) : report.Name,
                    Position = new NovaPoint(report.Position),
                    Known = report.UsedByUs,
                    StabilityTier = report.StabilityTier,
                });
            }

            return sightings;
        }

        /// <summary>A wormhole's score: a known one 70 − 10 × its stability tier; any other 90
        /// when it is no farther than the planet, else 50.</summary>
        public static int Score(WormholeSighting wormhole, double wormholeDistanceSquared, double planetDistanceSquared)
        {
            if (wormhole.Known)
            {
                return 70 - (10 * wormhole.StabilityTier);
            }

            return wormholeDistanceSquared <= planetDistanceSquared ? 90 : 50;
        }

        /// <summary>
        /// The wormhole the fleet diverts to, or null. Candidates lie within twice the distance
        /// to the colonization target and within about 216 ly; the best score wins, ties going to
        /// the nearer wormhole; the diversion is taken when a 0-99 roll is below the score. With
        /// no planet target (<paramref name="planet"/> null) only the 216 ly limit applies and an
        /// unknown wormhole scores 50 (an interpretation; the spec scores against the planet).
        /// </summary>
        public static WormholeSighting Choose(NovaPoint from, NovaPoint planet, IEnumerable<WormholeSighting> wormholes, Random random)
        {
            double planetDistance = planet != null ? PointUtilities.DistanceSquare(from, planet) : double.MaxValue;
            WormholeSighting best = null;
            int bestScore = int.MinValue;
            double bestDistance = double.MaxValue;

            foreach (WormholeSighting wormhole in wormholes ?? new List<WormholeSighting>())
            {
                if (wormhole?.Position == null)
                {
                    continue;
                }

                double distance = PointUtilities.DistanceSquare(from, wormhole.Position);
                if (distance > MaxDistanceSquared || (planet != null && distance > 4 * planetDistance))
                {
                    continue;
                }

                int score = Score(wormhole, distance, planet != null ? planetDistance : -1);
                if (score > bestScore || (score == bestScore && distance < bestDistance))
                {
                    best = wormhole;
                    bestScore = score;
                    bestDistance = distance;
                }
            }

            return best != null && random.Next(100) < bestScore ? best : null;
        }
    }
}
