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
    using System.Linq;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The planet-attack handler of behavior-specs-10/ai-opponent-behavior.md §12: personality
    /// 0's `FUN_1088_1b72`, the identical copies of personalities 5 and 4 (`FUN_10a0_299a`,
    /// `FUN_10a8_32ac`) with their listed differences, the shared target search
    /// (`FUN_1090_3a12` with `FUN_1088_216e`), the turn-scaled thresholds K, Q and q, the attack
    /// value, and personality 2's bomber rule for the inline search of personalities 1-3.
    /// </summary>
    /// <remarks>
    /// Units are the original's stored units (1 unit = 100 colonists = 1 kT of hold, §13).
    /// Stand-ins: Nova's planet reports carry no 4-bit coarse defence value, so the invasion
    /// estimate uses n = 0 (no defences); no report says whether an owner is Alternate
    /// Reality, so that test always passes; the nearest-object finder (`FUN_1038_2988`, class
    /// mask 0x21) is the nearest planet. The Computer Players Form Alliances option does not
    /// exist in Nova, so the target search uses `FUN_1088_216e` (any planet with an attack
    /// value) only.
    /// </remarks>
    public static class PlanetAttackHandler
    {
        /// <summary>Personality 0's base for K; personality 2 and 4 use 3 and personality 5 6.</summary>
        public static int StrengthBase(int category)
        {
            switch (category)
            {
                case AiCategory.Automitrons:
                case AiCategory.Cybertrons:
                    return 3;
                case AiCategory.Macinti:
                    return 6;
                default:
                    return 4;
            }
        }

        /// <summary>The base for the bomber quota Q: 6, or 3 for personality 2 ("K and Q follow
        /// personality 0's year formulas but with base 3"). Not stated for 4 and 5, so 6.</summary>
        public static int BomberQuotaBase(int category)
        {
            return category == AiCategory.Automitrons ? 3 : 6;
        }

        /// <summary>Required strength K: the base, or base + (year − 120) ÷ 20 after year 130,
        /// capped at 50.</summary>
        public static int StrengthK(int year, int strengthBase)
        {
            return year > 130 ? Math.Min(50, strengthBase + ((year - 120) / 20)) : strengthBase;
        }

        /// <summary>Bomber quota Q: the base, or base + (year − 100) ÷ 22 after year 115, capped at 12.</summary>
        public static int BomberQuotaQ(int year, int quotaBase)
        {
            return year > 115 ? Math.Min(12, quotaBase + ((year - 100) / 22)) : quotaBase;
        }

        /// <summary>Minimum bomber count q = min(3, Q ÷ 2 − 1).</summary>
        public static int MinimumBombersQ(int quotaQ)
        {
            return Math.Min(3, (quotaQ / 2) - 1);
        }

        /// <summary>
        /// A planet's attack value: personality 0 (`:58235`-`58251`) gives every planet owned by
        /// another player min(6, reported figure ÷ 250 + 1), plus 1 with a starbase; personality
        /// 1 gives 1 without a starbase and 2 with one. Other personalities' values are not
        /// stated; personality 0's rule is used. Zero for own and unowned planets.
        /// </summary>
        public static int AttackValue(StarIntel planet, AiFleetContext context)
        {
            if (!context.IsForeign(planet))
            {
                return 0;
            }

            int starbase = context.HasStarbase(planet) ? 1 : 0;
            if (context.Category == AiCategory.Turindrones)
            {
                return 1 + starbase;
            }

            return Math.Min(6, (AiFleetContext.ReportedFigure(planet) / 250) + 1) + starbase;
        }

        /// <summary>The target-search distance bonus: +7 within 50 ly, +5 within 100, +4 within
        /// 150, +3 within 200, +2 within 300, +1 within 500.</summary>
        public static int DistanceBonus(double distanceSquared)
        {
            if (distanceSquared <= 50 * 50)
            {
                return 7;
            }

            if (distanceSquared <= 100 * 100)
            {
                return 5;
            }

            if (distanceSquared <= 150 * 150)
            {
                return 4;
            }

            if (distanceSquared <= 200 * 200)
            {
                return 3;
            }

            if (distanceSquared <= 300 * 300)
            {
                return 2;
            }

            return distanceSquared <= 500 * 500 ? 1 : 0;
        }

        /// <summary>
        /// The target search (`FUN_1090_3a12` with `FUN_1088_216e`), scored from
        /// <paramref name="origin"/>: attack value plus the distance bonus; a planet already
        /// marked this turn is rejected three times in four and otherwise gains 128; ties go to
        /// the nearer planet; the winner is marked. Null when no planet has an attack value.
        /// </summary>
        public static StarIntel TargetSearch(NovaPoint origin, AiFleetContext context)
        {
            StarIntel best = null;
            int bestScore = int.MinValue;
            double bestDistance = double.MaxValue;

            foreach (StarIntel planet in context.Planets)
            {
                int value = AttackValue(planet, context);
                if (value <= 0)
                {
                    continue;
                }

                double distance = AiFleetContext.DistanceSquared(origin, planet.Position);
                int score = value + DistanceBonus(distance);
                if (context.TargetedPlanets.Contains(planet.Name))
                {
                    if (context.Random.Next(4) != 0)
                    {
                        continue;
                    }

                    score += 128;
                }

                if (score > bestScore || (score == bestScore && distance < bestDistance))
                {
                    best = planet;
                    bestScore = score;
                    bestDistance = distance;
                }
            }

            if (best != null)
            {
                context.TargetedPlanets.Add(best.Name);
            }

            return best;
        }

        /// <summary>Step 6 as an order: the target search's winner, else a chase of the nearest
        /// foreign fleet (`FUN_1090_3870`), else stay.</summary>
        public static FleetOrder TargetSearchOrder(Fleet fleet, AiFleetContext context, FleetOrder before = null)
        {
            StarIntel target = TargetSearch(fleet.Position, context);
            FleetOrder order;
            if (target != null)
            {
                order = FleetOrder.MoveTo(target, "attack: target search");
            }
            else
            {
                FleetIntel enemy = context.NearestForeignFleet(fleet.Position);
                order = enemy != null ? FleetOrder.Pursue(enemy, "attack: no planet, chases the nearest foreign fleet") : FleetOrder.Stay("attack: nothing to attack");
            }

            if (before != null)
            {
                order.LoadColonistsKt = before.LoadColonistsKt;
                order.ClearTask = before.ClearTask;
            }

            return order;
        }

        /// <summary>
        /// The invasion estimate E = figure × 400 ÷ (100 − truncated (n + 1) × 18 ÷ 4), in stored
        /// units, n being the 4-bit coarse defence value.
        /// </summary>
        public static int InvasionEstimate(int reportedFigure, int coarseDefence)
        {
            int coverage = ((coarseDefence + 1) * 18) / 4;
            return (reportedFigure * 400) / (100 - coverage);
        }

        /// <summary>
        /// The invasion check at another player's planet: null when the fleet stays and keeps
        /// bombing, else the colonists (stored units) it lands, max(C ÷ 2, E × 5 ÷ 4) capped at
        /// C and 30,000.
        /// </summary>
        public static int? InvasionLanding(int colonistsAboard, int estimate)
        {
            int c = colonistsAboard;
            int e = estimate;
            if (c / 5 <= e && (e >= 200 || c < 351))
            {
                if (e > 9 || c < 151)
                {
                    return null;
                }
            }

            return Math.Min(Math.Min(c, 30000), Math.Max(c / 2, (e * 5) / 4));
        }

        /// <summary>Troops loaded at an own starbase planet by a strong fleet: P ÷ 10 above 3,000
        /// units, P ÷ 15 above 2,000, P ÷ 20 above 1,000, else none.</summary>
        public static int TroopLoadAtHome(int populationUnits)
        {
            if (populationUnits > 3000)
            {
                return populationUnits / 10;
            }

            if (populationUnits > 2000)
            {
                return populationUnits / 15;
            }

            return populationUnits > 1000 ? populationUnits / 20 : 0;
        }

        /// <summary>
        /// The handler for one fleet (personality 0, or the personality 4/5 copy when
        /// <paramref name="copy"/> is true). S = strike ships, heavy ones counted twice; B = bombers.
        /// </summary>
        public static FleetOrder Handle(Fleet fleet, AiFleetContext context, bool copy)
        {
            var roles = AiFleetRoles.CountRoles(fleet.Composition.Values, context.Category);
            int s = AiFleetRoles.Count(roles, AiFleetRole.StrikeLight) + (2 * AiFleetRoles.Count(roles, AiFleetRole.StrikeHeavy));
            int b = AiFleetRoles.Count(roles, AiFleetRole.Bomber);
            int c = fleet.Cargo.ColonistsInKilotons;

            int k = StrengthK(context.Year, StrengthBase(context.Category));
            int q = BomberQuotaQ(context.Year, BomberQuotaBase(context.Category));
            int minimumBombers = MinimumBombersQ(q);

            // 1. Existing orders.
            FleetOrder existing = ExistingOrders(fleet, context);
            if (existing != null)
            {
                return existing;
            }

            StarIntel here = context.CurrentPlanet(fleet);

            // 2. In deep space.
            if (here == null)
            {
                double radius = copy ? 450 : 150;
                StarIntel foreign = context.Nearest(fleet.Position, planet => context.IsForeign(planet));
                if (foreign != null && AiFleetContext.DistanceSquared(fleet.Position, foreign.Position) <= radius * radius)
                {
                    return FleetOrder.MoveTo(foreign, "attack: deep space, nearest foreign planet");
                }

                StarIntel nearest = context.Nearest(fleet.Position, planet => true);
                return nearest != null ? FleetOrder.MoveTo(nearest, "attack: deep space, nearest object") : FleetOrder.Stay("attack: deep space, nowhere to go");
            }

            bool atOwn = context.IsOwn(here);
            bool atOwnStarbase = atOwn && context.HasStarbase(here);

            // 3. At an own starbase planet (the copies: at any own planet).
            if (atOwnStarbase || (copy && atOwn))
            {
                if (s < k || b < q)
                {
                    return WeakAtHome(fleet, context, s, k);
                }

                FleetOrder troops = new FleetOrder();
                if (!copy)
                {
                    Star star = context.OwnStar(here.Name);
                    troops.LoadColonistsKt = CapLoad(fleet, star, TroopLoadAtHome(AiFleetContext.PopulationUnits(star)));
                }

                return TargetSearchOrder(fleet, context, troops);
            }

            // 4. At any other planet, strong enough to act away from home.
            if (s >= k / 2 && b >= minimumBombers)
            {
                if (context.IsForeign(here))
                {
                    if (copy)
                    {
                        return FleetOrder.Stay("attack copy: stays at a foreign planet");
                    }

                    int estimate = InvasionEstimate(AiFleetContext.ReportedFigure(here), 0);
                    int? landing = InvasionLanding(c, estimate);
                    FleetOrder stay = FleetOrder.Stay(landing == null ? "attack: stays and bombs" : "attack: ground assault");
                    stay.UnloadColonistsKt = landing ?? 0;
                    return stay;
                }

                FleetOrder before = new FleetOrder();
                if (atOwn)
                {
                    Star star = context.OwnStar(here.Name);
                    int population = AiFleetContext.PopulationUnits(star);
                    before.LoadColonistsKt = population > 1000 ? CapLoad(fleet, star, population / 5) : 0;
                }

                return TargetSearchOrder(fleet, context, before);
            }

            // 5. A weak fleet away from a home starbase.
            return WeakAway(fleet, context, s, k);
        }

        /// <summary>Step 3's weak-fleet rule: stay below skill 2; at skill 2+ stay if S ≤ 2K and
        /// S &lt; 60, else launch on a 1-in-2 roll, then 7 in 10 when S &gt; 3K, then 7 in 10
        /// when S ≥ 121.</summary>
        private static FleetOrder WeakAtHome(Fleet fleet, AiFleetContext context, int s, int k)
        {
            if (context.Skill < 2 || (s <= 2 * k && s < 60))
            {
                return FleetOrder.Stay("attack: weak at home, stays");
            }

            bool launch = context.Random.Next(2) == 0
                || (s > 3 * k && context.Random.Next(10) < 7)
                || (s >= 121 && context.Random.Next(10) < 7);

            return launch ? TargetSearchOrder(fleet, context) : FleetOrder.Stay("attack: weak at home, stays");
        }

        /// <summary>Step 5: task cleared; at skill 2+ press on (5 in 10 if S &gt; 2K, else 7 in
        /// 10 if S &gt; 4K, else 7 in 10 if S &gt; 120); otherwise retreat to the nearest own
        /// starbase planet, or chase the nearest foreign fleet when there is none.</summary>
        private static FleetOrder WeakAway(Fleet fleet, AiFleetContext context, int s, int k)
        {
            if (context.Skill >= 2)
            {
                bool pressOn = (s > 2 * k && context.Random.Next(10) < 5)
                    || (s > 4 * k && context.Random.Next(10) < 7)
                    || (s > 120 && context.Random.Next(10) < 7);
                if (pressOn)
                {
                    return TargetSearchOrder(fleet, context, new FleetOrder { ClearTask = true });
                }
            }

            StarIntel home = context.NearestOwnStarbasePlanet(fleet.Position);
            FleetOrder order;
            if (home != null)
            {
                order = FleetOrder.MoveTo(home, "attack: weak away, retreats");
            }
            else
            {
                FleetIntel enemy = context.NearestForeignFleet(fleet.Position);
                order = enemy != null ? FleetOrder.Pursue(enemy, "attack: weak away, no home, chases") : FleetOrder.Stay("attack: weak away, nowhere to go");
            }

            order.ClearTask = true;
            return order;
        }

        /// <summary>
        /// Step 1: a fleet chasing an enemy fleet within 250 ly keeps chasing; one bound for
        /// another player's planet or an own starbase planet keeps going; one bound for an
        /// unowned or starbase-less own planet keeps going unless that planet's report is from
        /// this year. Null when the fleet re-plans.
        /// </summary>
        private static FleetOrder ExistingOrders(Fleet fleet, AiFleetContext context)
        {
            Waypoint next = AiFleetContext.NextWaypoint(fleet);
            if (next == null)
            {
                return null;
            }

            if (next.IsFleetTarget)
            {
                FleetIntel target = context.ForeignFleet(next.TargetFleetKey);
                NovaPoint position = target?.Position ?? next.Position;
                return position != null && AiFleetContext.DistanceSquared(fleet.Position, position) < 250.0 * 250.0
                    ? FleetOrder.KeepOrders("attack: keeps chasing")
                    : null;
            }

            StarIntel planet = context.Planet(next.Destination);
            if (planet == null)
            {
                return null;
            }

            if (context.IsForeign(planet) || (context.IsOwn(planet) && context.HasStarbase(planet)))
            {
                return FleetOrder.KeepOrders("attack: keeps going");
            }

            return planet.Year == context.Empire.TurnYear ? null : FleetOrder.KeepOrders("attack: keeps going (stale report)");
        }

        /// <summary>
        /// Personality 2's bomber rule, used for the inline target search of personalities 1-3:
        /// bombers run the target search from deep space, from an unowned planet, from an own
        /// planet without a starbase, and from an own starbase planet once escorted (two or more
        /// bombers and more than two strength-2 ships); at another player's planet they move on
        /// only when a foreign combat fleet stands at exactly the same position (§11, tested on
        /// the foreign fleet's real designs), otherwise they stay and keep bombing. A bomber
        /// group that already has a route keeps it (an assumption: the driver's re-planning of
        /// a fleet under way is not described).
        /// </summary>
        public static FleetOrder BomberRule(Fleet fleet, AiFleetContext context)
        {
            if (AiFleetContext.NextWaypoint(fleet) != null)
            {
                return FleetOrder.KeepOrders("bombers: under way");
            }

            StarIntel here = context.CurrentPlanet(fleet);
            if (here == null || here.Owner == Global.Nobody)
            {
                return TargetSearchOrder(fleet, context);
            }

            if (context.IsOwn(here))
            {
                if (!context.HasStarbase(here))
                {
                    return TargetSearchOrder(fleet, context);
                }

                var roles = AiFleetRoles.CountRoles(fleet.Composition.Values, context.Category);
                bool escorted = AiFleetRoles.Count(roles, AiFleetRole.Bomber) >= 2 && AiFleetRoles.Count(roles, AiFleetRole.StrikeHeavy) > 2;
                return escorted ? TargetSearchOrder(fleet, context) : FleetOrder.Stay("bombers: waiting for an escort");
            }

            bool contested = context.ForeignFleets.Any(report =>
                report.Position != null && report.Position.X == fleet.Position.X && report.Position.Y == fleet.Position.Y
                && report.Composition != null && FleetRolePredicates.IsCombatFleet(report.Composition.Values));

            return contested ? TargetSearchOrder(fleet, context) : FleetOrder.Stay("bombers: keep bombing");
        }

        /// <summary>A colonist load capped by the planet's population and the free hold.</summary>
        private static int CapLoad(Fleet fleet, Star star, int units)
        {
            if (star == null || units <= 0)
            {
                return 0;
            }

            int freeHold = Math.Max(0, fleet.TotalCargoCapacity - fleet.Cargo.Mass);
            return Math.Max(0, Math.Min(units, Math.Min(freeHold, star.Colonists / Global.ColonistsPerKiloton)));
        }
    }
}
