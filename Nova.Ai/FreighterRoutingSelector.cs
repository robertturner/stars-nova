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
    using Nova.Common.Components;
    using Nova.Common.Waypoints;

    /// <summary>
    /// One decided freighter order: go to <see cref="Destination"/> and, on arrival, either load
    /// (<see cref="CargoMode.Load"/>) or unload (<see cref="CargoMode.Unload"/>) the minerals in
    /// <see cref="Amount"/>. Categories 0 and 1 may also load colonists at the hub before leaving
    /// (<see cref="ColonistsToLoadAtHubKt"/>) and unload every colonist aboard at the destination
    /// (<see cref="UnloadColonistsAtDestination"/>), §5 step 6.
    /// </summary>
    public class FreighterRun
    {
        /// <summary>The destination planet: an own <see cref="Star"/>, or a
        /// <see cref="StarIntel"/> report for a planet the AI does not own.</summary>
        public Mappable Destination { get; }

        public bool IsHub { get; }

        public CargoMode Mode { get; }

        public Cargo Amount { get; }

        /// <summary>Colonists (kT, 1 kT = 100 colonists) loaded at the hub, immediately, before
        /// the freighter leaves; already capped by the hub's population and the free hold.</summary>
        public int ColonistsToLoadAtHubKt { get; }

        /// <summary>Unload All for colonists at the destination.</summary>
        public bool UnloadColonistsAtDestination { get; }

        public FreighterRun(Mappable destination, bool isHub, CargoMode mode, Cargo amount, int colonistsToLoadAtHubKt = 0, bool unloadColonistsAtDestination = false)
        {
            Destination = destination;
            IsHub = isHub;
            Mode = mode;
            Amount = amount;
            ColonistsToLoadAtHubKt = colonistsToLoadAtHubKt;
            UnloadColonistsAtDestination = unloadColonistsAtDestination;
        }
    }

    /// <summary>
    /// The shared freighter router `FUN_1090_19c8` (behavior-specs-10/ai-opponent-behavior.md
    /// §5, "How the router scores and orders"), used by categories 0-3: a freighter collects
    /// minerals from the AI's own planets and brings them to its hub. Every candidate is scored
    /// as value ÷ travel time T, T being the distance in whole light-years in 25-ly legs
    /// rounded up (at least 1); the highest score wins, a tie keeping the earlier planet.
    /// </summary>
    /// <remarks>
    /// Replaces an earlier selector built on a misreading (700-unit shortfall/surplus thresholds
    /// and a 180-ly cutoff, which the spec now attributes to personality 4's packet routine and
    /// the warship hunter). Ported: personality 0's 6,500 value for a weakly populated planet it
    /// does not own, the urgent-supply flat 25,000 score (categories 1-3) and the category 0/1
    /// colonist loads at the hub. Not ported: the floating-salvage candidates (`FUN_1090_23de`;
    /// the client has no reports of deep-space salvage), personality 1's remote-mining mark and
    /// the "flagged as habitable for itself" branch for planets the AI does not own (the flag is
    /// set by the personality 1-3 drivers, which Nova does not run). Hubs use the stateless rebuild recommended by §16: from turn
    /// 20, every owned starbase planet, then every owned planet meeting the §5 thresholds,
    /// in table order; each freighter joins the nearest hub with fewer than 8 freighters, and
    /// a freighter with no hub uses the first owned planet with a starbase.
    /// </remarks>
    public class FreighterRoutingSelector
    {
        public const int MaxFreightersPerHub = 8;

        private static readonly ResourceType[] Minerals = { ResourceType.Ironium, ResourceType.Boranium, ResourceType.Germanium };

        private readonly ClientData clientState;
        private readonly int category;
        private readonly int yearCounter;
        private readonly List<Star> hubs;
        private readonly Dictionary<string, int> freightersPerHub = new Dictionary<string, int>();

        public FreighterRoutingSelector(ClientData clientState, int category, int yearCounter)
        {
            this.clientState = clientState;
            this.category = category;
            this.yearCounter = yearCounter;
            hubs = BuildHubs();
        }

        /// <summary>The number of freighter hubs this turn (§5; personality 2's freighter quota
        /// reads it, §12).</summary>
        public int HubCount
        {
            get { return hubs.Count; }
        }

        /// <summary>
        /// The order for one idle freighter, or null for none (no hub, no cargo hold, or no
        /// candidate worth a trip).
        /// </summary>
        public FreighterRun SelectRun(Fleet freighter)
        {
            int capacity = freighter.TotalCargoCapacity;
            if (capacity <= 0)
            {
                return null;
            }

            Star hub = AssignHub(freighter);
            if (hub == null)
            {
                return null;
            }

            int free = Math.Max(0, capacity - freighter.Cargo.Mass);
            int fullness = 100 - (int)(100L * free / capacity);
            string currentPlanet = freighter.InOrbit?.Name;
            bool atHub = currentPlanet == hub.Name;

            int level = ScarcityLevel(hub.ResourcesOnHand, out ResourceType scarce);
            HashSet<string> marked = MarkedPlanets(freighter, hub);
            int hubUnits = hub.Colonists / 100;

            Mappable best = null;
            long bestScore = 0;

            foreach (Mappable candidate in PlanetTable())
            {
                if (candidate.Name == currentPlanet || marked.Contains(candidate.Name))
                {
                    continue;
                }

                long score;
                Star planet = candidate as Star;
                if (planet == null)
                {
                    // A planet the AI does not own (§5 step 4, second bullet): personality 0's
                    // 6,500 value only; the "flagged as habitable" branch is not ported.
                    long value = ForeignPlanetValue(category, atHub, hubUnits, ReportedPopulationFigure((StarIntel)candidate));
                    if (value <= 0)
                    {
                        continue;
                    }

                    score = value / TravelTime(PointUtilities.DistanceSquare(freighter.Position, candidate.Position));
                }
                else if (planet.Name == hub.Name)
                {
                    // The hub, when the freighter is elsewhere and more than 34% full.
                    if (atHub || fullness <= 34)
                    {
                        continue;
                    }

                    int t = TravelTime(PointUtilities.DistanceSquare(freighter.Position, planet.Position));
                    score = fullness >= 100 ? 25000 : 20L * fullness / t;
                }
                else
                {
                    // Another own planet: skipped with a starbase or a starbase heading its queue.
                    if (planet.Starbase != null || QueueHeadIsStarbase(planet))
                    {
                        continue;
                    }

                    if (atHub && HasUrgentSupplyFlag(category, planet, clientState.EmpireState.Race))
                    {
                        // The urgent-supply flag: a flat 25,000, not divided by the travel time.
                        score = UrgentSupplyScore;
                    }
                    else
                    {
                        long m = CollectableAmount(planet.ResourcesOnHand, level, scarce);
                        if (m < 10)
                        {
                            continue;
                        }

                        long value = 100 * Math.Min(100 * m / capacity, 100 - fullness);
                        score = value / TravelTime(PointUtilities.DistanceSquare(freighter.Position, planet.Position));
                    }
                }

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best == null)
            {
                return null;
            }

            if (best.Name == hub.Name)
            {
                Cargo unload = new Cargo();
                unload.Ironium = freighter.Cargo.Ironium;
                unload.Boranium = freighter.Cargo.Boranium;
                unload.Germanium = freighter.Cargo.Germanium;
                return new FreighterRun(best, true, CargoMode.Unload, unload);
            }

            // §5 step 6, colonists: loaded at the hub, immediately, by categories 0 and 1 only,
            // capped by the hub's population and the free hold; a colonist-carrying run to a
            // planet other than the hub also unloads every colonist at the destination.
            int colonistLoad = 0;
            if (atHub)
            {
                Star ownDestination = best as Star;
                StarIntel report = best as StarIntel;
                bool ownedByAi = ownDestination != null;
                bool ownedByAnother = report != null && report.Owner != Global.Nobody && report.Owner != clientState.EmpireState.Id;
                int destinationUnits = ownedByAi ? ownDestination.Colonists / 100 : (report != null ? report.Colonists / 100 : 0);

                colonistLoad = HubColonistLoadUnits(category, hubUnits, ownedByAi, ownedByAnother, destinationUnits);
                colonistLoad = Math.Max(0, Math.Min(colonistLoad, Math.Min(hubUnits, free)));
            }

            bool carriesColonists = colonistLoad > 0 || freighter.Cargo.ColonistsInKilotons > 0;
            bool unloadColonists = carriesColonists && (category == AiCategory.Robotoids || category == AiCategory.Turindrones);

            // Minerals: Load All anywhere but the hub, narrowed by the scarcity level. Nova knows
            // (and may take) the surface stock of its own planets only.
            Cargo load = best is Star ownPlanet
                ? LoadAmounts(freighter, ownPlanet.ResourcesOnHand, level, scarce, capacity, free)
                : new Cargo();

            return new FreighterRun(best, false, CargoMode.Load, load, colonistLoad, unloadColonists);
        }

        /// <summary>§5 step 4: the urgent-supply score.</summary>
        public const int UrgentSupplyScore = 25000;

        /// <summary>§5 step 4: personality 0's value for a weakly populated planet it does not
        /// own.</summary>
        public const int WeakPlanetValue = 6500;

        /// <summary>
        /// The urgent-supply flag (§5, §6): an owned planet with negative habitability, set only
        /// by categories 1-3. Habitability is the integer evaluator <see cref="Race.HabPercent"/>.
        /// </summary>
        public static bool HasUrgentSupplyFlag(int category, Star planet, Race race)
        {
            return category >= AiCategory.Turindrones && category <= AiCategory.Rototills
                && race != null && race.HabPercent(planet) < 0;
        }

        /// <summary>
        /// The reported population figure (§13) of a planet the AI does not own: about the stored
        /// population ÷ 4, i.e. colonists ÷ 400, clamped to 1-4,090; 0 for an empty planet. Nova's
        /// reports carry no separate fuzzed figure, so the report's colonist count is used (§13
        /// reimplementation note). An unowned planet's figure is 0.
        /// </summary>
        public static int ReportedPopulationFigure(StarIntel report)
        {
            if (report == null || report.Owner == Global.Nobody || report.Colonists <= 0)
            {
                return 0;
            }

            return Math.Max(1, Math.Min(4090, report.Colonists / 400));
        }

        /// <summary>
        /// §5 step 4, a planet the AI does not own: for personality 0 only, 6,500 when the
        /// freighter is at its hub and either the hub's population is over 1,100 units and the
        /// planet's reported figure is at most 61, or the hub's population is over 300 units and
        /// the figure is at most 14; otherwise 0 (not a candidate).
        /// </summary>
        public static long ForeignPlanetValue(int category, bool atHub, int hubUnits, int figure)
        {
            if (category != AiCategory.Robotoids || !atHub)
            {
                return 0;
            }

            if ((hubUnits > 1100 && figure <= 61) || (hubUnits > 300 && figure <= 14))
            {
                return WeakPlanetValue;
            }

            return 0;
        }

        /// <summary>
        /// §5 step 6, colonists loaded at the hub (stored units, 1 unit = 100 colonists = 1 kT),
        /// before the caps by the hub's population and the free hold:
        /// - Category 1: 1,000 units if the hub's population exceeds 1,200 units and the
        ///   destination is owned by any player with a smaller population.
        /// - Category 0, destination owned by the AI: none if the destination's population is
        ///   over 999 units, the hub's is at most 1.5 x the destination's, or the hub's is under
        ///   501 units; otherwise hub x p ÷ 100 with p 10 (hub over 2,000), 8 (over 1,500), 5
        ///   (over 1,000) or (hub - 400) ÷ 100.
        /// - Category 0, destination owned by another player: 100 units, or 300 if the hub's
        ///   population is over 900 units.
        /// Every other case (an unowned destination included) loads none.
        /// </summary>
        public static int HubColonistLoadUnits(int category, int hubUnits, bool destinationOwnedByAi, bool destinationOwnedByAnother, int destinationUnits)
        {
            if (category == AiCategory.Turindrones)
            {
                bool owned = destinationOwnedByAi || destinationOwnedByAnother;
                return owned && hubUnits > 1200 && destinationUnits < hubUnits ? 1000 : 0;
            }

            if (category != AiCategory.Robotoids)
            {
                return 0;
            }

            if (destinationOwnedByAnother)
            {
                return hubUnits > 900 ? 300 : 100;
            }

            if (!destinationOwnedByAi)
            {
                return 0;
            }

            // 2 x hub <= 3 x destination is "hub at most 1.5 x destination" without rounding.
            if (destinationUnits > 999 || 2L * hubUnits <= 3L * destinationUnits || hubUnits < 501)
            {
                return 0;
            }

            int p;
            if (hubUnits > 2000)
            {
                p = 10;
            }
            else if (hubUnits > 1500)
            {
                p = 8;
            }
            else if (hubUnits > 1000)
            {
                p = 5;
            }
            else
            {
                p = (hubUnits - 400) / 100;
            }

            return (int)((long)hubUnits * p / 100);
        }

        /// <summary>
        /// The planet table in order (§5 step 4: one pass over it): every known planet, the AI's
        /// own as their <see cref="Star"/> and the rest as their reports, then any own planet
        /// with no report.
        /// </summary>
        private IEnumerable<Mappable> PlanetTable()
        {
            Dictionary<string, Star> owned = new Dictionary<string, Star>();
            foreach (Star star in OwnedStars())
            {
                owned[star.Name] = star;
            }

            HashSet<string> seen = new HashSet<string>();
            foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
            {
                if (report == null || report.Name == null || !seen.Add(report.Name))
                {
                    continue;
                }

                if (owned.TryGetValue(report.Name, out Star star))
                {
                    yield return star;
                }
                else if (report.Position != null)
                {
                    yield return report;
                }
            }

            foreach (Star star in owned.Values)
            {
                if (seen.Add(star.Name))
                {
                    yield return star;
                }
            }
        }

        /// <summary>
        /// §5 step 2, hub scarcity: the scarcest of the hub's surface minerals is the first
        /// strictly smallest in Ironium, Boranium, Germanium order. It is compared with the
        /// smallest of the minerals before it (effectively infinite when Ironium is the
        /// scarcest): level 2 below a quarter of that, level 1 below a half, else 0 (both
        /// rounded down).
        /// </summary>
        public static int ScarcityLevel(Resources hubStock, out ResourceType scarce)
        {
            int[] stock = { hubStock.Ironium, hubStock.Boranium, hubStock.Germanium };
            int index = 0;
            for (int i = 1; i < stock.Length; i++)
            {
                if (stock[i] < stock[index])
                {
                    index = i;
                }
            }

            scarce = Minerals[index];

            long comparison = long.MaxValue;
            for (int i = 0; i < index; i++)
            {
                comparison = Math.Min(comparison, stock[i]);
            }

            if (stock[index] < comparison / 4)
            {
                return 2;
            }

            if (stock[index] < comparison / 2)
            {
                return 1;
            }

            return 0;
        }

        /// <summary>§5 step 3: T = (d + 24) ÷ 25 with d the truncated distance, at least 1.</summary>
        public static int TravelTime(double distanceSquared)
        {
            int d = (int)Math.Sqrt(distanceSquared);
            return Math.Max(1, (d + 24) / 25);
        }

        /// <summary>The mineral amount M a candidate offers at the given scarcity level: the
        /// scarce mineral alone (2), the scarce mineral plus half of each other (1), or all three
        /// (0).</summary>
        public static long CollectableAmount(Resources stock, int level, ResourceType scarce)
        {
            long total = 0;
            foreach (ResourceType mineral in Minerals)
            {
                int amount = Amount(stock, mineral);
                if (mineral == scarce || level == 0)
                {
                    total += amount;
                }
                else if (level == 1)
                {
                    total += amount / 2;
                }
            }

            return total;
        }

        /// <summary>
        /// §5 step 6: Load All of each mineral, narrowed when the level is not 0. At level 2, or
        /// when free space is no more than the destination's stock of the scarce mineral, only the
        /// scarce mineral is loaded. Otherwise every mineral is filled up to a percentage of the
        /// hold: 66% for the scarce mineral and 33% for each other.
        /// </summary>
        private static Cargo LoadAmounts(Fleet freighter, Resources stock, int level, ResourceType scarce, int capacity, int free)
        {
            Cargo load = new Cargo();
            int remaining = free;

            foreach (ResourceType mineral in Minerals)
            {
                int wanted;
                if (level == 0)
                {
                    wanted = Amount(stock, mineral);
                }
                else if (level == 2 || free <= Amount(stock, scarce))
                {
                    wanted = mineral == scarce ? Amount(stock, mineral) : 0;
                }
                else
                {
                    int percent = mineral == scarce ? 66 : 33;
                    wanted = Math.Max(0, (capacity * percent / 100) - Carried(freighter.Cargo, mineral));
                    wanted = Math.Min(wanted, Amount(stock, mineral));
                }

                int amount = Math.Max(0, Math.Min(wanted, remaining));
                Set(load, mineral, amount);
                remaining -= amount;
            }

            return load;
        }

        private IEnumerable<Star> OwnedStars()
        {
            return clientState.EmpireState.OwnedStars.Values
                .Where(star => star.Owner == clientState.EmpireState.Id);
        }

        /// <summary>§5 "Hubs", rebuilt statelessly (§16): nothing before turn 20.</summary>
        private List<Star> BuildHubs()
        {
            List<Star> result = new List<Star>();
            if (yearCounter < 20)
            {
                return result;
            }

            result.AddRange(OwnedStars().Where(star => star.Starbase != null));

            foreach (Star star in OwnedStars().Where(star => star.Starbase == null))
            {
                if (!QualifiesAsHub(star))
                {
                    continue;
                }

                // Category 0 only: no existing hub within 50 ly.
                if (category == AiCategory.Robotoids
                    && result.Any(hub => PointUtilities.DistanceSquare(hub.Position, star.Position) <= 50 * 50))
                {
                    continue;
                }

                result.Add(star);
            }

            return result;
        }

        /// <summary>§5: population over 79 units, at least 20 mines and 20 factories, and surface
        /// minerals plus 4 × the square of each concentration totalling at least 7,000.</summary>
        public static bool QualifiesAsHub(Star star)
        {
            if (star.Colonists / 100 <= 79 || star.Mines < 20 || star.Factories < 20)
            {
                return false;
            }

            long score = 0;
            foreach (ResourceType mineral in Minerals)
            {
                long concentration = Amount(star.MineralConcentration ?? new Resources(), mineral);
                score += Amount(star.ResourcesOnHand, mineral) + (4 * concentration * concentration);
            }

            return score >= 7000;
        }

        private Star AssignHub(Fleet freighter)
        {
            Star hub = hubs
                .Where(candidate => Count(candidate) < MaxFreightersPerHub)
                .OrderBy(candidate => PointUtilities.DistanceSquare(candidate.Position, freighter.Position))
                .FirstOrDefault();

            if (hub != null)
            {
                freightersPerHub[hub.Name] = Count(hub) + 1;
                return hub;
            }

            // A freighter with no hub uses the AI's first owned planet with a starbase.
            return OwnedStars().FirstOrDefault(star => star.Starbase != null);
        }

        private int Count(Star hub)
        {
            return freightersPerHub.TryGetValue(hub.Name, out int count) ? count : 0;
        }

        /// <summary>§5 step 1: planets another own fleet with two or more waypoints already has
        /// a Transport order to, when it holds ships of this freighter's first design. The hub is
        /// never marked.</summary>
        private HashSet<string> MarkedPlanets(Fleet freighter, Star hub)
        {
            HashSet<string> marked = new HashSet<string>();
            long firstDesign = freighter.Composition.Values
                .Where(token => token.Quantity > 0 && token.Design != null)
                .Select(token => token.Design.Key)
                .FirstOrDefault();

            foreach (Fleet other in clientState.EmpireState.OwnedFleets.Values)
            {
                if (other == freighter || other.Owner != clientState.EmpireState.Id || other.Waypoints.Count < 2)
                {
                    continue;
                }

                Waypoint next = other.Waypoints[1];
                if (!(next.Task is CargoTask) || next.Destination == hub.Name)
                {
                    continue;
                }

                if (other.Composition.Values.Any(token => token.Quantity > 0 && token.Design != null && token.Design.Key == firstDesign))
                {
                    marked.Add(next.Destination);
                }
            }

            return marked;
        }

        /// <summary>Whether the first item of the planet's queue is a starbase design.</summary>
        private bool QueueHeadIsStarbase(Star planet)
        {
            ShipProductionUnit unit = planet.ManufacturingQueue?.Queue.FirstOrDefault()?.Unit as ShipProductionUnit;
            return unit != null
                && clientState.EmpireState.Designs.TryGetValue(unit.DesignKey, out ShipDesign design)
                && design.IsStarbase;
        }

        private static int Amount(Resources stock, ResourceType mineral)
        {
            switch (mineral)
            {
                case ResourceType.Ironium:
                    return stock.Ironium;
                case ResourceType.Boranium:
                    return stock.Boranium;
                default:
                    return stock.Germanium;
            }
        }

        private static int Carried(Cargo cargo, ResourceType mineral)
        {
            switch (mineral)
            {
                case ResourceType.Ironium:
                    return cargo.Ironium;
                case ResourceType.Boranium:
                    return cargo.Boranium;
                default:
                    return cargo.Germanium;
            }
        }

        private static void Set(Cargo cargo, ResourceType mineral, int amount)
        {
            switch (mineral)
            {
                case ResourceType.Ironium:
                    cargo.Ironium = amount;
                    break;
                case ResourceType.Boranium:
                    cargo.Boranium = amount;
                    break;
                default:
                    cargo.Germanium = amount;
                    break;
            }
        }
    }
}
