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
    /// The colony and invasion rules of personalities 1-3 (behavior-specs-10/ai-opponent-
    /// behavior.md §12: personality 1 `FUN_1088_2350`, `:60326`-`60389` and `:60020`-`60136`;
    /// personality 2 `:66423`-`66518` and `:66701`-`66738`; personality 3 `:67106`-`67159` and
    /// `:67361`-`67401`). Population figures are stored units (1 unit = 100 colonists, §13).
    /// </summary>
    /// <remarks>
    /// Stand-ins and skips: "flagged habitable for the AI" is the colonization search's
    /// non-negative terraformed habitability test; no report tells whether an owner is
    /// Alternate Reality, so that condition always passes; personality 2's "a slot-1 design not
    /// on the Medium Freighter hull is scrapped" is not ported (Nova's colony design is a Colony
    /// Ship, which would be scrapped every turn); personality 1's last fallback (a wormhole
    /// found by an earlier exploration pass, one time in ten) is not ported - no exploration pass
    /// records wormholes in Nova.
    /// </remarks>
    public static class ColonyFleetRules
    {
        /// <summary>Personality 1's invasion legs fly at warp 6.</summary>
        public const int TransportWarp = 6;

        /// <summary>
        /// The first fleet pass (personality 1 "Fleets already under way"; personalities 2 and 3
        /// "Invasion", for every own fleet whether idle or not): a colony fleet or a hauler at, or
        /// bound for, another player's planet that the AI flags as habitable, carrying colonists,
        /// whose owner is not Alternate Reality (and, for personality 1, which has no starbase),
        /// unloads all colonists there by Transport (personality 1 adds a return leg to the
        /// nearest own starbase planet). A colony fleet there that fails the habitability test,
        /// or a qualifying fleet failing the other conditions, has its route cut and its task
        /// cleared. Null when the rule does not apply.
        /// </summary>
        public static FleetOrder InvasionUnderWay(Fleet fleet, AiFleetContext context)
        {
            bool colonyFleet = fleet.CanColonize;
            if (!colonyFleet && !FleetRolePredicates.IsHaulerFleet(fleet.Composition.Values))
            {
                return null;
            }

            StarIntel planet = context.AtOrBoundFor(fleet);
            if (!context.IsForeign(planet))
            {
                return null;
            }

            bool habitable = ColonizationTargetSelector.PassesHabitabilityFilter(context.Empire.Race, planet);
            if (!habitable)
            {
                return colonyFleet ? CutRoute("invasion: foreign planet not habitable") : null;
            }

            int colonists = fleet.Cargo.ColonistsInKilotons;
            bool starbaseBlocks = context.Category == AiCategory.Turindrones && context.HasStarbase(planet);
            if (colonists <= 0 || starbaseBlocks)
            {
                return CutRoute("invasion: conditions not met");
            }

            FleetOrder order = new FleetOrder { Reason = "invasion: unloads colonists" };
            StarIntel here = context.CurrentPlanet(fleet);
            if (here != null && here.Name == planet.Name)
            {
                order.UnloadColonistsKt = colonists;
            }
            else
            {
                order.Destination = planet;
                order.DestinationTask = UnloadColonists(colonists);
            }

            if (context.Category == AiCategory.Turindrones)
            {
                StarIntel home = context.NearestOwnStarbasePlanet(planet.Position);
                if (home != null)
                {
                    if (order.Destination == null)
                    {
                        order.Destination = home;
                    }
                    else
                    {
                        order.ReturnLeg = home;
                    }
                }
            }

            return order;
        }

        /// <summary>
        /// An idle colony fleet of personality 1, 2 or 3. <paramref name="wormholes"/> is the
        /// diversion candidates (personality 3 only, before year 120, at an own planet, when the
        /// colony design's engine index is 2 or more). Null means it waits.
        /// </summary>
        public static FleetOrder IdleColonyFleet(Fleet fleet, AiFleetContext context, ColonizationTargetSelector selector, IEnumerable<WormholeSighting> wormholes)
        {
            int category = context.Category;
            int colonists = fleet.Cargo.ColonistsInKilotons;
            StarIntel here = context.CurrentPlanet(fleet);
            Star ownStar = context.IsOwn(here) ? context.OwnStar(here.Name) : null;
            int ownUnits = AiFleetContext.PopulationUnits(ownStar);
            int emptyThreshold = category == AiCategory.Automitrons ? 200 : 50;
            int loadUnits = AiCategory.ColonistLoadUnits(category, ownUnits);

            bool searches = colonists > 0 || (ownStar != null && ownUnits >= emptyThreshold);
            if (!searches)
            {
                return EmptyColonyFleet(fleet, context, here);
            }

            int loadKt = ownStar != null ? DefaultFleetAI.ColonistsToLoadKt(fleet, loadUnits) : 0;
            StarIntel target = selector.SelectTarget(fleet);

            if (category == AiCategory.Rototills && ownStar != null && context.Year < WormholeDiversion.LastYear
                && ColonyDesignEngineIndex(fleet) >= 2)
            {
                WormholeSighting wormhole = WormholeDiversion.Choose(fleet.Position, target?.Position, wormholes, context.Random);
                if (wormhole != null)
                {
                    return new FleetOrder { LoadColonistsKt = loadKt, Destination = wormhole, Reason = "colony: wormhole diversion" };
                }
            }

            if (target != null)
            {
                selector.MarkClaimed(target);
                return new FleetOrder
                {
                    LoadColonistsKt = loadKt,
                    Destination = target,
                    DestinationTask = new ColoniseTask(),
                    Reason = "colony: colonize",
                };
            }

            if (category == AiCategory.Turindrones)
            {
                // `FUN_1088_3c86`: carry the colonists to the nearest foreign planet with no
                // starbase that is habitable for the AI, from the current planet or, elsewhere,
                // from the first own starbase planet.
                NovaPoint origin = here?.Position ?? context.FirstOwnStarbasePlanet()?.Position ?? fleet.Position;
                StarIntel victim = context.Nearest(origin, planet =>
                    context.IsForeign(planet) && !context.HasStarbase(planet)
                    && ColonizationTargetSelector.PassesHabitabilityFilter(context.Empire.Race, planet));
                int aboard = colonists + loadKt;
                if (victim != null && aboard > 0)
                {
                    return new FleetOrder
                    {
                        LoadColonistsKt = loadKt,
                        Destination = victim,
                        DestinationTask = UnloadColonists(aboard),
                        Warp = TransportWarp,
                        Reason = "colony: no target, lands on a foreign planet",
                    };
                }
            }

            return null; // keeps its colonists and tries again next turn
        }

        /// <summary>
        /// An empty colony fleet (personality 1: no colonists and in deep space, at a planet it
        /// does not own, or at an own planet under 50 units; personality 2: under 200 units;
        /// personality 3: 50). At an own starbase planet it waits; elsewhere it flies to the
        /// nearest own starbase planet when its engine index is high enough (3 for personality
        /// 1, 2 for personalities 2 and 3) and one exists, else it is scrapped where it stands.
        /// </summary>
        private static FleetOrder EmptyColonyFleet(Fleet fleet, AiFleetContext context, StarIntel here)
        {
            if (context.IsOwn(here) && context.HasStarbase(here))
            {
                return null;
            }

            int engineNeeded = context.Category == AiCategory.Turindrones ? 3 : 2;
            StarIntel home = context.NearestOwnStarbasePlanet(fleet.Position);
            if (home != null && ColonyDesignEngineIndex(fleet) >= engineNeeded)
            {
                return FleetOrder.MoveTo(home, "colony: empty, flies home");
            }

            return new FleetOrder { Scrap = true, Reason = "colony: empty, scrapped" };
        }

        /// <summary>The engine index of the fleet's (first) colony design.</summary>
        public static int ColonyDesignEngineIndex(Fleet fleet)
        {
            ShipToken colony = fleet.Composition.Values.FirstOrDefault(token => token.Quantity > 0 && token.Design != null && token.Design.CanColonize);
            return colony != null ? FleetRolePredicates.EngineIndexOf(colony.Design) : -1;
        }

        private static CargoTask UnloadColonists(int kilotons)
        {
            CargoTask task = new CargoTask();
            task.Mode = CargoMode.Unload;
            task.Amount.ColonistsInKilotons = kilotons;
            return task;
        }

        private static FleetOrder CutRoute(string reason)
        {
            return new FleetOrder { ClearTask = true, Reason = reason };
        }
    }
}
