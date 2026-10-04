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
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// One decided fleet order, in the four order shapes of behavior-specs-10/ai-opponent-
    /// behavior.md §12 ("a single destination, which replaces every later waypoint; a task on
    /// the current waypoint; immediate cargo transfers ...; cancelling a route"). Nova has no
    /// immediate transfer, so a transfer is a zero-distance cargo waypoint at the fleet's current
    /// planet, performed by turn generation before the fleet moves on (as the colony ships'
    /// colonist load already is). Executed by <see cref="DefaultFleetAI.Apply"/>.
    /// </summary>
    public class FleetOrder
    {
        /// <summary>Leave the fleet's orders exactly as they are.</summary>
        public bool Keep { get; set; }

        /// <summary>Clear the task on the current waypoint (e.g. Lay Mines).</summary>
        public bool ClearTask { get; set; }

        /// <summary>Colonists (kT = stored units) loaded at the current planet.</summary>
        public int LoadColonistsKt { get; set; }

        /// <summary>Colonists (kT) unloaded at the current planet (a ground assault on a foreign
        /// planet).</summary>
        public int UnloadColonistsKt { get; set; }

        /// <summary>Scrap Fleet where the fleet stands.</summary>
        public bool Scrap { get; set; }

        /// <summary>A pursuit order aimed at this foreign fleet (Waypoint.AimAtFleet).</summary>
        public FleetIntel PursueFleet { get; set; }

        /// <summary>A destination (planet, or a point in space such as a wormhole).</summary>
        public Mappable Destination { get; set; }

        /// <summary>The task written on the destination waypoint (default none).</summary>
        public IWaypointTask DestinationTask { get; set; }

        /// <summary>An extra leg after the destination (personality 1's return leg home).</summary>
        public Mappable ReturnLeg { get; set; }

        /// <summary>The warp for the legs; 0 means the efficient warp of `FUN_1050_69c2`.</summary>
        public int Warp { get; set; }

        /// <summary>Explore (the exploration pick); <see cref="ExploreFallback"/> applies when
        /// there is nothing left to explore.</summary>
        public bool Explore { get; set; }

        public Func<FleetOrder> ExploreFallback { get; set; }

        /// <summary>Which rule produced the order (tests and debugging only).</summary>
        public string Reason { get; set; }

        public static FleetOrder KeepOrders(string reason)
        {
            return new FleetOrder { Keep = true, Reason = reason };
        }

        /// <summary>Stay where it is: the route is cut back to the current position.</summary>
        public static FleetOrder Stay(string reason)
        {
            return new FleetOrder { Reason = reason };
        }

        public static FleetOrder MoveTo(Mappable destination, string reason)
        {
            return new FleetOrder { Destination = destination, Reason = reason };
        }

        public static FleetOrder Pursue(FleetIntel target, string reason)
        {
            return new FleetOrder { PursueFleet = target, Reason = reason };
        }

        public bool Moves
        {
            get { return Destination != null || PursueFleet != null; }
        }
    }

    /// <summary>
    /// What the fleet passes of §12 read about the AI's world in one run: its own fleets in
    /// plain table order (§8), the foreign fleets it can see, the planets, the year counter
    /// (0 in the first year), skill and category, and the run's Random.
    /// </summary>
    public class AiFleetContext
    {
        public AiFleetContext(ClientData clientState, int category, int skill, Random random, IList<Fleet> ownFleets)
        {
            ClientState = clientState;
            Category = category;
            Skill = skill;
            Random = random;
            OwnFleets = ownFleets ?? new List<Fleet>();
            ForeignFleets = Empire.FleetReports.Values
                .Where(report => report.Owner != Empire.Id && !report.IsStarbase && report.Position != null)
                .ToList();
        }

        public ClientData ClientState { get; }

        public EmpireData Empire
        {
            get { return ClientState.EmpireState; }
        }

        public int Category { get; }

        public int Skill { get; }

        public Random Random { get; }

        public IList<Fleet> OwnFleets { get; }

        /// <summary>Every foreign (non-starbase) fleet the AI can see (§12 hunter step 1).</summary>
        public IList<FleetIntel> ForeignFleets { get; }

        /// <summary>The year counter: 0 in the first year.</summary>
        public int Year
        {
            get { return Empire.TurnYear - Global.StartingYear; }
        }

        /// <summary>"Already targeted" marks of the planet target search, per run (§12).</summary>
        public HashSet<string> TargetedPlanets { get; } = new HashSet<string>();

        public IEnumerable<StarIntel> Planets
        {
            get { return Empire.StarReports.Values.Where(report => report.Position != null); }
        }

        public bool IsOwn(StarIntel planet)
        {
            return planet != null && planet.Owner == Empire.Id;
        }

        /// <summary>Owned by another player (not unowned).</summary>
        public bool IsForeign(StarIntel planet)
        {
            return planet != null && planet.Owner != Empire.Id && planet.Owner != Global.Nobody;
        }

        public Star OwnStar(string name)
        {
            if (name == null)
            {
                return null;
            }

            Star star = Empire.OwnedStars.Values.FirstOrDefault(s => s.Name == name);
            return star != null && star.Owner == Empire.Id ? star : null;
        }

        public bool HasStarbase(StarIntel planet)
        {
            if (planet == null)
            {
                return false;
            }

            Star own = IsOwn(planet) ? OwnStar(planet.Name) : null;
            return own != null ? own.Starbase != null : planet.Starbase != null;
        }

        public StarIntel Planet(string name)
        {
            return name != null && Empire.StarReports.TryGetValue(name, out StarIntel report) ? report : null;
        }

        /// <summary>The planet the fleet orbits, or null in deep space.</summary>
        public StarIntel CurrentPlanet(Fleet fleet)
        {
            return fleet.InOrbit is Star star ? Planet(star.Name) : null;
        }

        /// <summary>
        /// The fleet's next waypoint: the first after the current-position waypoint that is not
        /// at the fleet's position (a zero-distance cargo waypoint stands in for an immediate
        /// transfer, so the real next leg is the one after it).
        /// </summary>
        public static Waypoint NextWaypoint(Fleet fleet)
        {
            for (int index = 1; index < fleet.Waypoints.Count; index++)
            {
                Waypoint waypoint = fleet.Waypoints[index];
                if (waypoint.IsFleetTarget)
                {
                    return waypoint;
                }

                NovaPoint position = waypoint.Position;
                bool here = position != null && fleet.Position != null && position.X == fleet.Position.X && position.Y == fleet.Position.Y;
                if (!here)
                {
                    return waypoint;
                }
            }

            return null;
        }

        /// <summary>The planet the fleet's next waypoint is bound for, or null.</summary>
        public StarIntel BoundFor(Fleet fleet)
        {
            Waypoint next = NextWaypoint(fleet);
            return next == null || next.IsFleetTarget ? null : Planet(next.Destination);
        }

        /// <summary>"At, or heading for" a planet: the next waypoint's planet, else the current one.</summary>
        public StarIntel AtOrBoundFor(Fleet fleet)
        {
            return BoundFor(fleet) ?? (NextWaypoint(fleet) == null ? CurrentPlanet(fleet) : null);
        }

        /// <summary>The fleet is chasing a fleet (its next waypoint is a pursuit).</summary>
        public static bool IsChasingFleet(Fleet fleet)
        {
            Waypoint next = NextWaypoint(fleet);
            return next != null && next.IsFleetTarget;
        }

        public FleetIntel ForeignFleet(long key)
        {
            return ForeignFleets.FirstOrDefault(report => report.Key == key);
        }

        public static double DistanceSquared(NovaPoint a, NovaPoint b)
        {
            return PointUtilities.DistanceSquare(a, b);
        }

        /// <summary>`FUN_1090_452c`'s search: the nearest own planet with a starbase, optionally
        /// filtered.</summary>
        public StarIntel NearestOwnStarbasePlanet(NovaPoint from, Func<Star, bool> filter = null)
        {
            StarIntel best = null;
            double bestDistance = double.MaxValue;
            foreach (Star star in Empire.OwnedStars.Values)
            {
                if (star.Owner != Empire.Id || star.Starbase == null || (filter != null && !filter(star)))
                {
                    continue;
                }

                StarIntel report = Planet(star.Name);
                if (report == null)
                {
                    continue;
                }

                double distance = DistanceSquared(from, report.Position);
                if (distance < bestDistance)
                {
                    best = report;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>The AI's first own planet with a starbase, in table order.</summary>
        public StarIntel FirstOwnStarbasePlanet()
        {
            Star star = Empire.OwnedStars.Values.FirstOrDefault(s => s.Owner == Empire.Id && s.Starbase != null);
            return star != null ? Planet(star.Name) : null;
        }

        public StarIntel Nearest(NovaPoint from, Func<StarIntel, bool> accept)
        {
            StarIntel best = null;
            double bestDistance = double.MaxValue;
            foreach (StarIntel report in Planets)
            {
                if (!accept(report))
                {
                    continue;
                }

                double distance = DistanceSquared(from, report.Position);
                if (distance < bestDistance)
                {
                    best = report;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>`FUN_1090_3870`: the nearest foreign fleet.</summary>
        public FleetIntel NearestForeignFleet(NovaPoint from)
        {
            return ForeignFleets
                .OrderBy(report => DistanceSquared(from, report.Position))
                .FirstOrDefault();
        }

        /// <summary>A uniformly chosen planet, or null.</summary>
        public StarIntel RandomPlanet()
        {
            List<StarIntel> planets = Planets.ToList();
            return planets.Count == 0 ? null : planets[Random.Next(planets.Count)];
        }

        /// <summary>
        /// The 12-bit reported population figure (§13): about stored population ÷ 4, i.e. raw
        /// colonists ÷ 400, clamped to 1-4,090, and 0 for an empty planet. Nova's reports are not
        /// fuzzed, so this is the §13 reimplementation note's "true population ÷ 400".
        /// </summary>
        public static int ReportedFigure(StarIntel planet)
        {
            if (planet == null || planet.Colonists <= 0)
            {
                return 0;
            }

            return Math.Max(1, Math.Min(4090, planet.Colonists / 400));
        }

        /// <summary>A planet's colonist population in stored units of 100 colonists (§13).</summary>
        public static int PopulationUnits(Star star)
        {
            return star == null ? 0 : star.Colonists / 100;
        }
    }
}
