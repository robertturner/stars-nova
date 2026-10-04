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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The warship enemy-fleet hunter `FUN_1090_1438` (personalities 0, 4 and 5) and its variant
    /// `FUN_1090_3c80` (personalities 1-3), behavior-specs-10/ai-opponent-behavior.md §12,
    /// "Hunting enemy fleets".
    /// </summary>
    /// <remarks>
    /// Not ported: the Computer Players Form Alliances option (Nova has none, so the option is
    /// off and no candidate is excluded). Interpretations: the 1-in-15 roll is made only for a
    /// candidate that survived the 1-in-3 roll; "the nearest planet the personality has not
    /// marked" (personality 0 marks every planet owned by another player; the other
    /// personalities' marks are not given, so the same rule is used) leaves out the planet the
    /// fleet already stands at; "falling back to step 4 if that is its own planet" is read as the
    /// planet it stands at. The variant's exploration pick `FUN_1090_0df2` is not traced; the
    /// order asks the caller to explore (Nova's scout logic) with the variant's own fallbacks.
    /// </remarks>
    public static class EnemyFleetHunter
    {
        /// <summary>180 ly: the engagement radius, squared distance below 32,400.</summary>
        public const double EngageRangeSquared = 180.0 * 180.0;

        /// <summary>
        /// One hunter decision for <paramref name="fleet"/>.
        /// </summary>
        /// <param name="ownCombatFleets">The AI's own combat fleets (§11), in table order.</param>
        /// <param name="variant">True for `FUN_1090_3c80` (personalities 1-3).</param>
        public static FleetOrder Hunt(Fleet fleet, AiFleetContext context, IList<Fleet> ownCombatFleets, bool variant)
        {
            if (variant && !fleet.Composition.Values.Any(token => token.Quantity > 0 && FleetRolePredicates.HasBeamOrTorpedo(token.Design)))
            {
                // `FUN_1090_3c1e`: no beam or torpedo weapon, so it explores instead.
                return new FleetOrder { Explore = true, Reason = "hunter variant: unarmed, explores" };
            }

            int ownShips = FleetRolePredicates.ShipCount(fleet.Composition.Values);
            FleetIntel best = null;
            double bestDistance = double.MaxValue;

            foreach (FleetIntel candidate in context.ForeignFleets)
            {
                int committed = ShipsAlreadyOrderedTo(candidate, fleet, ownCombatFleets);
                if (committed > 0)
                {
                    if (context.Random.Next(3) == 0)
                    {
                        continue; // skipped one time in three (the variant keeps it two times in three)
                    }

                    if (!variant && ownShips < 5 * committed && context.Random.Next(15) == 0)
                    {
                        continue;
                    }
                }

                double distance = AiFleetContext.DistanceSquared(fleet.Position, candidate.Position);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            FleetOrder order;
            if (best == null)
            {
                order = NoCandidate(fleet, context, variant);
            }
            else if (bestDistance < EngageRangeSquared)
            {
                order = FleetOrder.Pursue(best, "hunter: enemy fleet within 180 ly");
            }
            else if (fleet.FuelAvailable * 2 < fleet.TotalFuelCapacity)
            {
                StarIntel refuel = context.NearestOwnStarbasePlanet(fleet.Position);
                order = refuel != null
                    ? FleetOrder.MoveTo(refuel, "hunter: below half fuel, refuels")
                    : NoCandidate(fleet, context, variant);
            }
            else
            {
                StarIntel current = context.CurrentPlanet(fleet);
                HashSet<string> bound = PlanetsBoundForByOtherCombatFleets(fleet, context, ownCombatFleets);
                StarIntel nearEnemy = context.Nearest(best.Position, planet => !bound.Contains(planet.Name));
                order = nearEnemy == null || (current != null && nearEnemy.Name == current.Name)
                    ? NoCandidate(fleet, context, variant)
                    : FleetOrder.MoveTo(nearEnemy, "hunter: planet nearest the enemy beyond 180 ly");
            }

            // Step 6: a fleet already bound for a planet keeps its orders when the new pick is
            // also a planet.
            if (order.Destination is StarIntel && context.BoundFor(fleet) != null)
            {
                return FleetOrder.KeepOrders("hunter: already bound for a planet");
            }

            return order;
        }

        /// <summary>Ships of the AI's other combat fleets already ordered to that fleet.</summary>
        public static int ShipsAlreadyOrderedTo(FleetIntel candidate, Fleet self, IList<Fleet> ownCombatFleets)
        {
            int ships = 0;
            foreach (Fleet other in ownCombatFleets)
            {
                if (other == self)
                {
                    continue;
                }

                Waypoint next = AiFleetContext.NextWaypoint(other);
                if (next != null && next.IsFleetTarget && next.TargetFleetKey == candidate.Key)
                {
                    ships += FleetRolePredicates.ShipCount(other.Composition.Values);
                }
            }

            return ships;
        }

        private static HashSet<string> PlanetsBoundForByOtherCombatFleets(Fleet self, AiFleetContext context, IList<Fleet> ownCombatFleets)
        {
            HashSet<string> bound = new HashSet<string>();
            foreach (Fleet other in ownCombatFleets)
            {
                StarIntel planet = other == self ? null : context.BoundFor(other);
                if (planet != null)
                {
                    bound.Add(planet.Name);
                }
            }

            return bound;
        }

        /// <summary>Step 4 (no candidate at all), or the variant's fallback chain.</summary>
        private static FleetOrder NoCandidate(Fleet fleet, AiFleetContext context, bool variant)
        {
            if (variant)
            {
                // `FUN_1090_0df2`, then the best attack target around the home planet, then a
                // random planet.
                return new FleetOrder { Explore = true, ExploreFallback = () => VariantFallback(context), Reason = "hunter variant: no candidate, explores" };
            }

            StarIntel current = context.CurrentPlanet(fleet);
            StarIntel foreign = context.Nearest(fleet.Position, planet => context.IsForeign(planet));
            if (foreign != null && (current == null || foreign.Name != current.Name))
            {
                return FleetOrder.MoveTo(foreign, "hunter: nearest planet owned by another player");
            }

            StarIntel unmarked = context.Nearest(
                fleet.Position,
                planet => !context.IsForeign(planet) && (current == null || planet.Name != current.Name));
            if (unmarked != null)
            {
                return FleetOrder.MoveTo(unmarked, "hunter: nearest unmarked planet");
            }

            return RandomPlanetOrder(context);
        }

        /// <summary>The variant's fallback once there is nothing to explore: the best attack
        /// target around the AI's home planet (its first own starbase planet), else a random
        /// planet.</summary>
        public static FleetOrder VariantFallback(AiFleetContext context)
        {
            StarIntel home = context.FirstOwnStarbasePlanet();
            StarIntel target = home != null ? PlanetAttackHandler.TargetSearch(home.Position, context) : null;
            return target != null
                ? FleetOrder.MoveTo(target, "hunter variant: best attack target around home")
                : RandomPlanetOrder(context);
        }

        private static FleetOrder RandomPlanetOrder(AiFleetContext context)
        {
            StarIntel random = context.RandomPlanet();
            return random != null ? FleetOrder.MoveTo(random, "hunter: random planet") : FleetOrder.Stay("hunter: nothing to do");
        }
    }
}
