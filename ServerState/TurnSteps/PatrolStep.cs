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

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The Patrol-mark pass and the fleet-target revalidation of turn-generation step 39
    /// (`FUN_1070_374a`, `:47182`-`47500`; behavior-specs-10/fleet-movement-scanning-cargo.md §5,
    /// Patrol "Complete rule" items 0-7, and "Pursuit", step 39 revalidation). It runs after the
    /// visibility pass (ScanStep), for each player in index order, handling that player's fleets
    /// one by one in fleet-table order: first the patrol scan, then the revalidation of the
    /// fleet's fleet-targeted waypoints.
    /// </summary>
    public class PatrolStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            FleetPursuit pursuit = new FleetPursuit(serverState);
            Dictionary<long, Fleet> live = pursuit.LiveFleetsByKey();

            // The global fleet table: owner, then fleet number.
            List<Fleet> fleetTable = live.Values.OrderBy(f => f.Owner).ThenBy(f => f.Id).ToList();

            foreach (EmpireData empire in serverState.AllEmpires.Values.OrderBy(e => e.Id).ToList())
            {
                // The patrol mark (fleet byte 5 bit 0x80) is wiped by every player's visibility
                // pass, so it only spreads one player's patrols over different targets.
                HashSet<long> marked = new HashSet<long>();

                foreach (Fleet fleet in empire.OwnedFleets.Values.OrderBy(f => f.Id).ToList())
                {
                    if (fleet.Composition.Count == 0)
                    {
                        continue;
                    }

                    Scan(serverState, pursuit, empire, fleet, fleetTable, marked);
                    pursuit.Revalidate(empire, fleet, live);
                }
            }
        }

        private static void Scan(ServerData serverState, FleetPursuit pursuit, EmpireData empire, Fleet fleet, List<Fleet> fleetTable, HashSet<long> marked)
        {
            if (fleet.Waypoints.Count == 0 || fleet.IsStarbase)
            {
                return;
            }

            // 1. Which fleets patrol: waypoint 0 carries Patrol, or carries no task while the next
            // waypoint carries Patrol (copied onto waypoint 0 first: the look-ahead promotion).
            Waypoint current = fleet.Waypoints[0];
            Waypoint next = fleet.Waypoints.Count > 1 ? fleet.Waypoints[1] : null;
            PatrolTask patrol = current.Task as PatrolTask;
            if (patrol == null)
            {
                if (!(current.Task == null || current.Task is NoTask) || !(next?.Task is PatrolTask nextPatrol))
                {
                    return;
                }

                patrol = new PatrolTask(nextPatrol);
                current.Task = patrol;
            }

            // An intercept already under way is not re-scanned.
            if (next != null && next.IsFleetTarget)
            {
                return;
            }

            // 2. Search origin: the fleet's own position, except a fleet in deep space with more
            // than one waypoint and Repeat Orders on, which searches from its next waypoint.
            NovaPoint origin = fleet.Position;
            if (fleet.InOrbit == null && fleet.Waypoints.Count > 1 && fleet.RepeatOrders)
            {
                origin = fleet.Waypoints[1].Position;
            }

            // 3. Candidates: every fleet of another player this player currently sees.
            string primary = pursuit.PrimaryTargetOf(fleet);
            Fleet best = null;
            double bestDistance = 0;
            bool bestMarked = false;

            foreach (Fleet candidate in fleetTable)
            {
                if (candidate.Owner == fleet.Owner || candidate.IsStarbase || !pursuit.IsSeenBy(empire, candidate))
                {
                    continue;
                }

                double distance = PointUtilities.Distance(origin, candidate.Position);
                bool candidateMarked = marked.Contains(candidate.Key);

                // (a) preference: an unmarked fleet beats every marked one at any distance;
                // within the same mark state only a strictly nearer one wins.
                if (best != null)
                {
                    bool better = (bestMarked && !candidateMarked)
                        || (bestMarked == candidateMarked && distance < bestDistance);
                    if (!better)
                    {
                        continue;
                    }
                }

                // (b) target type and (c) Attack Who.
                if (!FleetPursuit.FitsTargetType(candidate, primary)
                    || !BattleEngine.IsLegitimateTarget(serverState, fleet.Owner, fleet.BattlePlan, candidate.Owner))
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
                bestMarked = candidateMarked;
            }

            if (best == null)
            {
                return;
            }

            // 4. Mark it now, before the range test. (The built-in tutorial does not mark; this
            // port has no tutorial.)
            marked.Add(best.Key);

            // 5. Range test: at most the range and not zero.
            if (bestDistance <= 0 || bestDistance > patrol.RangeInLightYears)
            {
                return;
            }

            // 6. Intercept leg as waypoint 1.
            int efficientWarp = EfficientWarp(fleet);
            int speed = patrol.Speed != 0 ? patrol.Speed : efficientWarp;
            bool onlyCurrent = fleet.Waypoints.Count == 1;

            Waypoint leg;
            if (onlyCurrent)
            {
                leg = new Waypoint();
                leg.Task = new PatrolTask(patrol);
            }
            else
            {
                leg = fleet.Waypoints[1].CloneWithTask();
            }

            leg.AimAtFleet(best);
            leg.WarpFactor = speed;
            fleet.Waypoints.Insert(1, leg);

            if (onlyCurrent && fleet.RepeatOrders)
            {
                // The patrol post, appended so the fleet returns to it afterwards.
                Waypoint post = new Waypoint(current);
                post.Task = new PatrolTask(patrol);
                post.WarpFactor = efficientWarp;
                post.TargetFleetKey = Global.None;
                if (post.TargetKind == WaypointTargetKind.Fleet)
                {
                    post.TargetKind = WaypointTargetKind.DeepSpace;
                }

                fleet.Waypoints.Add(post);
            }

            Message message = new Message();
            message.Audience = fleet.Owner;
            message.Text = "Your patrolling fleet " + fleet.Name + " has targeted " + best.Name + " for interception.";
            message.Type = "Patrol";
            serverState.AllMessages.Add(message);
        }

        /// <summary>
        /// The efficient warp `FUN_1050_69c2` (behavior-specs-10/ai-opponent-behavior.md §12 step
        /// 1): the highest warp, at most 10, at which every engine uses at most 120% fuel, with the
        /// warp-10 cap (10 becomes 9 unless the engine is one of the five warp-10 engines). The
        /// free-speed preference is not applied (the spec does not say which variant Patrol uses).
        /// Nova.Ai.EfficientWarp holds the same rule for the AI; the server cannot reference it.
        /// </summary>
        public static int EfficientWarp(Fleet fleet)
        {
            int candidate = 10;
            bool any = false;

            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (token.Quantity <= 0 || token.Design == null)
                {
                    continue;
                }

                any = true;
                Engine engine;
                try
                {
                    engine = token.Design.Engine;
                }
                catch (Exception)
                {
                    engine = null;
                }

                int[] table = engine?.FuelConsumption;
                if (table == null)
                {
                    return 0;
                }

                while (candidate > 0 && Fuel(table, candidate) > 120)
                {
                    candidate--;
                }

                if (candidate == 10 && !WarpTenEngines.Contains(EngineName(token.Design) ?? string.Empty))
                {
                    candidate = 9;
                }
            }

            return any ? candidate : 0;
        }

        private static readonly HashSet<string> WarpTenEngines = new HashSet<string>
        {
            "Interspace-10", "Enigma Pulsar", "Trans-Star 10", "Trans-Galactic Mizer Scoop", "Galaxy Scoop",
        };

        private static int Fuel(int[] table, int warp)
        {
            if (warp <= 0)
            {
                return 0;
            }

            return warp - 1 < table.Length ? table[warp - 1] : int.MaxValue;
        }

        private static string EngineName(ShipDesign design)
        {
            try
            {
                return design.Hull?.Modules?
                    .Select(module => module.AllocatedComponent)
                    .FirstOrDefault(component => component != null && component.Properties != null && component.Properties.ContainsKey("Engine"))?
                    .Name;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
