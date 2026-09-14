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
    using System.Linq;

    using Nova.Client;
    using Nova.Common;

    /// <summary>
    /// One decided freighter run: load <see cref="Mineral"/> at <see cref="Source"/>, deliver it
    /// to <see cref="Target"/>.
    /// </summary>
    public class FreighterRun
    {
        public Star Source { get; }

        public Star Target { get; }

        public ResourceType Mineral { get; }

        public FreighterRun(Star source, Star target, ResourceType mineral)
        {
            Source = source;
            Target = target;
            Mineral = mineral;
        }
    }

    /// <summary>
    /// Assigns idle transport-capable fleets a mineral-delivery run - ports
    /// docs/behavior-specs-3/ai-opponent-behavior.md section 5 ("Automated mineral/cargo
    /// transport"). As with <see cref="ColonizationTargetSelector"/>, the spec's own figures are
    /// approximate ("roughly 180 light-years squared") since it's reconstructed from decompiled
    /// logic, not exact source; the mineral shortfall/surplus thresholds below reuse section 3's
    /// own explicit ~700-unit figures (that section is about starbases specifically, this one
    /// about any owned planet, but the spec gives no separate number for this mechanic, and
    /// reusing an already-stated figure is more defensible than inventing an unrelated one).
    /// </summary>
    public class FreighterRoutingSelector
    {
        public const int ShortfallThreshold = 700;
        public const int SurplusThreshold = 700;

        /// <summary>Reachability cutoff (light-years squared) - the spec's own "roughly 180".
        /// Within this radius the best value-per-time-of-arrival shortfall planet is chosen;
        /// beyond it, a random shortfall planet is picked instead (see SelectTarget), matching
        /// the spec's "falling back to a random pick if nothing scores above a cutoff."</summary>
        private const double ReachableCutoffSquared = 180;

        /// <summary>Normalizes the value-per-time-of-arrival score as if every route were
        /// travelled at warp 5 (25 = 5 squared, matching the game's warp-squared movement rule -
        /// light-years covered per turn scales with warp-factor squared) - the spec's own stated
        /// constant, given as the reason 25 appears in this scoring function at all.</summary>
        private const double AssumedWarpSquared = 25;

        private static readonly ResourceType[] Minerals = { ResourceType.Ironium, ResourceType.Boranium, ResourceType.Germanium };

        private readonly ClientData clientState;
        private readonly Random random;

        public FreighterRoutingSelector(ClientData clientState, Random random)
        {
            this.clientState = clientState;
            this.random = random;
        }

        /// <summary>
        /// Picks a mineral-delivery run for the given idle transport fleet: which mineral to
        /// carry, where to load it from (the nearest owned planet with a surplus of that
        /// mineral), and where to deliver it (a planet running short) - or null if no owned
        /// planet currently has both a shortfall and somewhere else has a surplus to send.
        /// </summary>
        public FreighterRun SelectRun(Fleet fleet, HashSet<string> alreadyClaimedTargets)
        {
            List<Star> shortfallPlanets = OwnedStars()
                .Where(star => !alreadyClaimedTargets.Contains(star.Name))
                .Where(HasShortfall)
                .ToList();

            if (shortfallPlanets.Count == 0)
            {
                return null;
            }

            Star target = SelectTarget(fleet, shortfallPlanets);
            ResourceType mineral = MostDeficientMineral(target);

            Star source = OwnedStars()
                .Where(star => star.Name != target.Name)
                .Where(star => MineralAmount(star, mineral) >= SurplusThreshold)
                .OrderBy(star => PointUtilities.DistanceSquare(fleet.Position, star.Position))
                .FirstOrDefault();

            if (source == null)
            {
                // Nothing to spare anywhere right now - nothing useful this fleet can do.
                return null;
            }

            return new FreighterRun(source, target, mineral);
        }

        private IEnumerable<Star> OwnedStars()
        {
            return clientState.EmpireState.OwnedStars.Values
                .Where(star => star.Owner == clientState.EmpireState.Id);
        }

        private static bool HasShortfall(Star star)
        {
            return Minerals.Any(mineral => MineralAmount(star, mineral) < ShortfallThreshold);
        }

        public static int MineralAmount(Star star, ResourceType mineral)
        {
            switch (mineral)
            {
                case ResourceType.Ironium:
                    return star.ResourcesOnHand.Ironium;
                case ResourceType.Boranium:
                    return star.ResourcesOnHand.Boranium;
                case ResourceType.Germanium:
                    return star.ResourcesOnHand.Germanium;
                default:
                    return int.MaxValue;
            }
        }

        private static ResourceType MostDeficientMineral(Star star)
        {
            return Minerals.OrderBy(mineral => MineralAmount(star, mineral)).First();
        }

        /// <summary>"Searches for the nearest reachable planet needing that cargo type, falling
        /// back to a random pick if nothing scores above a cutoff" - among planets within
        /// <see cref="ReachableCutoffSquared"/>, picks the one maximizing a value-per-time-of-
        /// arrival score (see ValuePerTimeOfArrival); otherwise picks uniformly at random among
        /// every shortfall planet regardless of distance, so a fleet is never left with nothing
        /// to do just because nothing scored well enough nearby.</summary>
        private Star SelectTarget(Fleet fleet, List<Star> shortfallPlanets)
        {
            List<Star> reachable = shortfallPlanets
                .Where(star => PointUtilities.DistanceSquare(fleet.Position, star.Position) <= ReachableCutoffSquared)
                .ToList();

            if (reachable.Count > 0)
            {
                return reachable
                    .OrderByDescending(star => ValuePerTimeOfArrival(fleet, star))
                    .First();
            }

            return shortfallPlanets[random.Next(shortfallPlanets.Count)];
        }

        /// <summary>Bigger shortfalls and shorter assumed (warp-5) travel times score higher -
        /// the spec's own "companion value-per-time-of-arrival scoring function," described as
        /// normalizing by 25 (see AssumedWarpSquared) but not given in exact closed form beyond
        /// that; this is a reasonable reconstruction of the described shape, not a bit-verified
        /// original formula.</summary>
        private double ValuePerTimeOfArrival(Fleet fleet, Star star)
        {
            double shortfallSize = (Minerals.Length * ShortfallThreshold) - Minerals.Sum(mineral => MineralAmount(star, mineral));
            double distance = Math.Sqrt(PointUtilities.DistanceSquare(fleet.Position, star.Position));
            double timeOfArrival = Math.Max(distance / AssumedWarpSquared, 0.01);
            return shortfallSize / timeOfArrival;
        }
    }
}
