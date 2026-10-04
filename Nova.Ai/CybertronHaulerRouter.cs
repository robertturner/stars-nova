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
    /// Personality 4's own hauler router `FUN_10a8_23d0` (behavior-specs-10/ai-opponent-
    /// behavior.md §12, personality 4 "Haulers"; predicates `FUN_10a8_272c`, `FUN_10a8_27ba`,
    /// `FUN_10a8_2858`): a colonist shuttle taking 1,000-unit loads from large starbase worlds to
    /// the nearest small own colonies, with opportunistic ground assaults and no mineral hauling.
    /// All population figures are stored units (1 unit = 100 colonists = 1 kT, §13).
    /// </summary>
    /// <remarks>
    /// The original writes every inbound-hauler counter and the "nothing to deliver" counter N - 1
    /// entries past the array the predicates read (§12 indexing defect), so the predicates always
    /// see no hauler inbound. As §12's reimplementation note recommends, that effective behaviour
    /// is reproduced: the inbound tests always pass and no counter is kept. Not ported: setting the
    /// battle plan to 4 (Nova's fleets carry a battle-plan name, and the AI issues no battle-plan
    /// command), and the Alternate Reality owner test (Nova's reports carry no owner race, so a
    /// foreign owner is never taken as AR, as in PlanetAttackHandler).
    /// </remarks>
    public static class CybertronHaulerRouter
    {
        public const int LoadUnits = 1000;
        public const int LoadingPlanetUnits = 2000;
        public const int EmptyTargetUnits = 2200;
        public const int SmallColonyUnits = 200;
        public const int ColonyUnits = 1000;
        public const double RangeSquared = 160000;

        /// <summary>
        /// The order for one idle hauler (no ships in slots 4-13), or null for none.
        /// <list type="bullet">
        /// <item>In deep space: the nearest planet (the nearest-object finder, class mask 0x21,
        /// read as the nearest planet).</item>
        /// <item>At an own planet of more than 2,000 units: load up to 1,000 units ("loaded").
        /// At a smaller own planet: unload up to 1,000 units ("empty").</item>
        /// <item>At a planet it does not own: unowned or with a starbase, "loaded" when it carries
        /// colonists, else "empty". Otherwise a ground assault: Unload All colonists there, then
        /// "empty" (the empty-mode destination replaces the original's home leg).</item>
        /// <item>Empty: the nearest own starbase planet with more than 2,200 units.</item>
        /// <item>Loaded: the nearest own planet within 400 ly with under 200 units, else the
        /// nearest own planet within 400 ly under 1,000 units; with neither, a hauler at its own
        /// planet unloads up to 1,000 units there and gets no order.</item>
        /// </list>
        /// The destination is written with no task, at the efficient warp; the next run loads or
        /// unloads on arrival.
        /// </summary>
        public static FleetOrder Route(Fleet hauler, AiFleetContext context)
        {
            EmpireData empire = context.Empire;
            StarIntel here = context.CurrentPlanet(hauler);
            if (here == null)
            {
                StarIntel nearest = context.Nearest(hauler.Position, planet => true);
                return nearest == null ? null : FleetOrder.MoveTo(nearest, "cybertron hauler: deep space, nearest object");
            }

            int carried = hauler.Cargo.ColonistsInKilotons;
            int free = Math.Max(0, hauler.TotalCargoCapacity - hauler.Cargo.Mass);
            FleetOrder order = new FleetOrder();
            bool loaded;

            Star own = context.OwnStar(here.Name);
            if (own != null)
            {
                int units = AiFleetContext.PopulationUnits(own);
                if (units > LoadingPlanetUnits)
                {
                    order.LoadColonistsKt = Math.Min(LoadUnits, Math.Min(free, units));
                    loaded = true;
                }
                else
                {
                    order.UnloadColonistsKt = Math.Min(LoadUnits, carried);
                    loaded = false;
                }
            }
            else if (here.Owner == Global.Nobody || context.HasStarbase(here))
            {
                loaded = carried > 0;
            }
            else
            {
                // Ground assault: Unload All colonists at this planet ("whatever it carries, even
                // nothing"; nothing to write when it carries none).
                order.UnloadColonistsKt = carried;
                loaded = false;
            }

            if (!loaded)
            {
                order.Destination = context.NearestOwnStarbasePlanet(hauler.Position, star => AiFleetContext.PopulationUnits(star) > EmptyTargetUnits);
                order.Reason = "cybertron hauler: empty, to a large starbase world";
            }
            else
            {
                order.Destination = NearestOwnPlanet(hauler.Position, here.Name, empire, units => units < SmallColonyUnits)
                    ?? NearestOwnPlanet(hauler.Position, here.Name, empire, units => units < ColonyUnits);
                order.Reason = "cybertron hauler: loaded, to a small colony";

                if (order.Destination == null && own != null)
                {
                    order.LoadColonistsKt = 0;
                    order.UnloadColonistsKt = Math.Min(LoadUnits, carried);
                    order.Reason = "cybertron hauler: nothing to deliver, unloads here";
                }
            }

            if (order.Destination == null && order.LoadColonistsKt <= 0 && order.UnloadColonistsKt <= 0)
            {
                return null;
            }

            return order;
        }

        /// <summary>`FUN_10a8_27ba` from a planet, as the planet pass uses it: some own planet
        /// other than this one lies within 400 ly with under 1,000 units (no hauler is ever seen
        /// inbound, §12).</summary>
        public static bool HasSmallOwnPlanetWithin400(NovaPoint from, string self, EmpireData empire)
        {
            return NearestOwnPlanet(from, self, empire, units => units < ColonyUnits) != null;
        }

        private static StarIntel NearestOwnPlanet(NovaPoint from, string exclude, EmpireData empire, Func<int, bool> acceptUnits)
        {
            StarIntel best = null;
            double bestDistance = double.MaxValue;
            foreach (Star star in empire.OwnedStars.Values)
            {
                if (star.Owner != empire.Id || star.Name == exclude || !acceptUnits(AiFleetContext.PopulationUnits(star)))
                {
                    continue;
                }

                double distance = PointUtilities.DistanceSquare(from, star.Position);
                if (distance > RangeSquared || distance >= bestDistance)
                {
                    continue;
                }

                if (empire.StarReports.TryGetValue(star.Name, out StarIntel report))
                {
                    best = report;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
