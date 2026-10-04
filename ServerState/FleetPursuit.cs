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
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Pursuit of fleet-targeted waypoints (behavior-specs-10/fleet-movement-scanning-cargo.md §5,
    /// "Waypoint placement and editing (client)", item Pursuit). A waypoint aimed at a fleet
    /// stores the target's key and its position at the moment of the order; the host keeps it
    /// pursuing in four ways:
    /// <list type="number">
    /// <item>Step 10 re-lock (<see cref="ReLockTargets"/>), before movement.</item>
    /// <item>Movement steers toward the stored point; the end-of-movement refresh
    /// (<see cref="RefreshAndResolveArrivals"/>) then copies each target's new position into the
    /// fleet-targeted waypoints 1 onward and only then tests arrival.</item>
    /// <item>Jump freeze (<see cref="FreezePursuers"/>) when the target gates or enters a
    /// wormhole.</item>
    /// <item>Step 39 revalidation (<see cref="Revalidate"/>, run per player by
    /// Nova.Server.TurnSteps.PatrolStep).</item>
    /// </list>
    /// </summary>
    public class FleetPursuit
    {
        private readonly ServerData serverState;

        public FleetPursuit(ServerData serverState)
        {
            this.serverState = serverState;
        }

        /// <summary>
        /// The generation's follow marks (fleet byte 5 bit 0x80 as set by turn-generation step 9 and
        /// trimmed by step 11; see <see cref="Nova.Server.TurnSteps.FollowOrderStep"/>). The
        /// end-of-movement refresh leaves the fleet-targeted legs of a marked fleet alone.
        /// </summary>
        public HashSet<long> FollowMarked { get; } = new HashSet<long>();

        /// <summary>Every live fleet (not starbases on stars) by key.</summary>
        private Dictionary<long, Fleet> LiveFleets()
        {
            Dictionary<long, Fleet> fleets = new Dictionary<long, Fleet>();
            foreach (Fleet fleet in serverState.IterateAllFleets())
            {
                if (fleet.Composition.Count > 0)
                {
                    fleets[fleet.Key] = fleet;
                }
            }

            return fleets;
        }

        /// <summary>
        /// Step 10 re-lock (`FUN_1038_42e2`, `:20812`-`20906`). For every fleet-targeted waypoint 1
        /// onward whose target no longer exists or is not at the stored position, re-aim it at a
        /// fleet of the same owner standing at the stored position: preferring fleets that fit the
        /// pursuer's battle-plan primary-target type, chosen at random among them, otherwise any
        /// fleet there at random. If none is there the stale key is kept.
        /// </summary>
        /// <remarks>
        /// Not implemented: the third trigger ("a foreign fleet that a split or merge order changed
        /// this turn"), because this port applies splits and merges outside the order stream; and
        /// the score `FUN_1038_4dbe` that ranks the primary-type fits, which the spec does not
        /// define - the fits are drawn at random instead.
        /// </remarks>
        public void ReLockTargets(Random random)
        {
            Dictionary<long, Fleet> live = LiveFleets();

            foreach (Fleet pursuer in serverState.IterateAllFleets())
            {
                for (int i = 1; i < pursuer.Waypoints.Count; i++)
                {
                    Waypoint waypoint = pursuer.Waypoints[i];
                    if (!waypoint.IsFleetTarget)
                    {
                        continue;
                    }

                    if (live.TryGetValue(waypoint.TargetFleetKey, out Fleet target) && target.Position == waypoint.Position)
                    {
                        continue;
                    }

                    ushort targetOwner = waypoint.TargetFleetKey.Owner();
                    List<Fleet> there = live.Values
                        .Where(f => f.Owner == targetOwner && f.Key != pursuer.Key && f.Position == waypoint.Position && !f.IsStarbase)
                        .OrderBy(f => f.Key)
                        .ToList();

                    if (there.Count == 0)
                    {
                        continue;
                    }

                    string primary = PrimaryTargetOf(pursuer);
                    List<Fleet> fits = there.Where(f => FitsTargetType(f, primary)).ToList();
                    List<Fleet> pool = fits.Count > 0 ? fits : there;
                    Fleet chosen = pool[random.Next(pool.Count)];

                    waypoint.TargetFleetKey = chosen.Key;
                    waypoint.Destination = chosen.Name;
                }
            }
        }

        /// <summary>
        /// The end-of-movement refresh (`FUN_1080_1060`, `:56699`-`56737`), once per generation
        /// after all fleets have moved: on every fleet with more than one waypoint, each
        /// fleet-targeted waypoint 1 onward that is not frozen takes its target's new position; one
        /// whose target is gone becomes a deep-space point. Only then is arrival tested: a fleet
        /// standing on its fleet-targeted waypoint 1 has arrived, the waypoint becomes waypoint 0,
        /// its task runs, and it becomes a planet or deep-space point unless its task is Transport
        /// or Merge with Fleet. Repeat Orders re-appends a reached non-Patrol leg (a pending Patrol
        /// intercept is not recycled), under the same count and last-point tests as an ordinary
        /// arrival (behavior-specs-11/fleet-movement-scanning-cargo.md §5, "Arrival and Repeat
        /// Orders").
        /// </summary>
        public void RefreshAndResolveArrivals()
        {
            Dictionary<long, Fleet> live = LiveFleets();

            foreach (Fleet fleet in serverState.IterateAllFleets().ToList())
            {
                if (fleet.Waypoints.Count < 2)
                {
                    continue;
                }

                // Fleets carrying this generation's follow mark are skipped by the refresh
                // (fleet-movement-scanning-cargo.md §5, "The mark"; FollowOrderStep).
                bool followMarked = FollowMarked.Contains(fleet.Key);

                for (int i = 1; i < fleet.Waypoints.Count; i++)
                {
                    Waypoint waypoint = fleet.Waypoints[i];
                    if (!waypoint.IsFleetTarget || waypoint.PursuitFrozen || followMarked)
                    {
                        continue;
                    }

                    if (live.TryGetValue(waypoint.TargetFleetKey, out Fleet target))
                    {
                        waypoint.Position = target.Position;
                        waypoint.Destination = target.Name;
                    }
                    else
                    {
                        waypoint.MakeFixedPoint(null);
                    }
                }

                Waypoint next = fleet.Waypoints[1];
                if (next.IsFleetTarget && fleet.Position == next.Position)
                {
                    Arrive(fleet, next);
                }
            }
        }

        private void Arrive(Fleet fleet, Waypoint reached)
        {
            // Repeat Orders: the reached leg is re-appended (with its task, settings, warp and
            // target as they stood) unless it is a pending Patrol intercept, unless fewer than
            // three waypoints remain, or unless the last waypoint stands on the same point. The
            // copy is taken before the arrival conversion below.
            bool patrolIntercept = reached.IsFleetTarget && reached.Task is PatrolTask;
            Waypoint recycled = null;
            if (fleet.RepeatOrders && !patrolIntercept && fleet.Waypoints.Count >= 3
                && fleet.Waypoints[fleet.Waypoints.Count - 1].Position != reached.Position)
            {
                recycled = reached.CloneWithTask();
                recycled.WarpFactor = reached.WarpFactor;
            }

            // The reached leg becomes waypoint 0 (the current-position placeholder goes).
            fleet.Waypoints.RemoveAt(0);

            Star star = StarAt(fleet.Position);
            fleet.InOrbit = star;

            EmpireData sender = serverState.AllEmpires[fleet.Owner];
            EmpireData receiver = null;
            if (star != null)
            {
                serverState.AllEmpires.TryGetValue(star.Owner, out receiver);
            }

            IWaypointTask task = reached.Task ?? new NoTask();
            bool keepsFleetTarget = task is CargoTask || task is SplitMergeTask;

            // A Transport task aimed at a fleet works against that fleet (cargo between fleets,
            // and cargo theft with a Pick Pocket / Robber Baron Scanner).
            Mappable taskTarget = star;
            if (task is CargoTask transport && transport.Instructions != null)
            {
                Fleet targetFleet = serverState.IterateAllFleets().FirstOrDefault(other => other.Key == reached.TargetFleetKey);
                if (targetFleet != null)
                {
                    taskTarget = targetFleet;
                    serverState.AllEmpires.TryGetValue(targetFleet.Owner, out receiver);
                }
            }

            bool taskValid = task.IsValid(fleet, taskTarget, sender, receiver);
            if (taskValid)
            {
                task.Perform(fleet, taskTarget, sender, receiver);
            }

            serverState.AllMessages.AddRange(task.Messages);

            // Same clearing rule as an ordinary arrival (TurnGenerator.UpdateFleet): Lay Mine
            // Field and Patrol stay on the current waypoint.
            if (!(taskValid && task is LayMinesTask) && !(task is PatrolTask))
            {
                reached.Task = new NoTask();
            }

            if (!keepsFleetTarget)
            {
                reached.MakeFixedPoint(star);
            }

            reached.WarpFactor = 0;

            if (recycled != null)
            {
                fleet.Waypoints.Add(recycled);
            }
        }

        /// <summary>
        /// Jump freeze (`FUN_1080_133e`, `:56808`-`56859`): when <paramref name="jumper"/> jumps
        /// through a stargate or enters a wormhole, every other player's waypoint aimed at it is
        /// set to the point it left and frozen, so the refresh leaves it alone.
        /// </summary>
        public void FreezePursuers(Fleet jumper, NovaPoint departurePoint)
        {
            foreach (Fleet pursuer in serverState.IterateAllFleets())
            {
                if (pursuer.Owner == jumper.Owner)
                {
                    continue;
                }

                foreach (Waypoint waypoint in pursuer.Waypoints)
                {
                    if (waypoint.IsFleetTarget && waypoint.TargetFleetKey == jumper.Key)
                    {
                        waypoint.Position = departurePoint;
                        waypoint.PursuitFrozen = true;
                    }
                }
            }
        }

        /// <summary>
        /// Step 39 revalidation for one fleet of <paramref name="empire"/> (`:47457`-`47493`):
        /// clears the freeze mark on every fleet-targeted waypoint; one whose target is destroyed
        /// becomes a fixed point at its stored position (message 40); one whose target this player
        /// no longer sees becomes the planet the target orbits (message 41) or a fixed point at its
        /// stored position (message 42).
        /// </summary>
        public void Revalidate(EmpireData empire, Fleet fleet, Dictionary<long, Fleet> live)
        {
            foreach (Waypoint waypoint in fleet.Waypoints)
            {
                if (!waypoint.IsFleetTarget)
                {
                    continue;
                }

                waypoint.PursuitFrozen = false;

                if (!live.TryGetValue(waypoint.TargetFleetKey, out Fleet target))
                {
                    waypoint.MakeFixedPoint(null);
                    Notify(fleet, "Fleet " + fleet.Name + "'s target has been destroyed; its waypoint is now the target's last known location.", 40);
                    continue;
                }

                if (IsSeenBy(empire, target))
                {
                    continue;
                }

                Star orbit = target.InOrbit != null ? StarAt(target.Position) : null;
                if (orbit != null)
                {
                    waypoint.MakeFixedPoint(orbit);
                    Notify(fleet, "Fleet " + fleet.Name + "'s target has gone into orbit around " + orbit.Name + "; its waypoint is now that planet.", 41);
                }
                else
                {
                    waypoint.MakeFixedPoint(null);
                    Notify(fleet, "Fleet " + fleet.Name + "'s target is out of scanner range; its waypoint is now the target's last known location.", 42);
                }
            }
        }

        /// <summary>True when the empire owns the fleet or scanned it this year (ScanStep).</summary>
        public bool IsSeenBy(EmpireData empire, Fleet target)
        {
            if (target.Owner == empire.Id)
            {
                return true;
            }

            return empire.FleetReports.TryGetValue(target.Key, out FleetIntel report) && report.Year == serverState.TurnYear;
        }

        public Dictionary<long, Fleet> LiveFleetsByKey()
        {
            return LiveFleets();
        }

        private void Notify(Fleet fleet, string text, int messageNumber)
        {
            Message message = new Message();
            message.Audience = fleet.Owner;
            message.Text = text;
            message.Type = "Fleet Target " + messageNumber;
            serverState.AllMessages.Add(message);
        }

        private Star StarAt(NovaPoint position)
        {
            return serverState.AllStars.Values.FirstOrDefault(star => star.Position == position);
        }

        /// <summary>The primary target type of the fleet's battle plan ("Any" if unknown).</summary>
        public string PrimaryTargetOf(Fleet fleet)
        {
            if (serverState.AllEmpires.TryGetValue(fleet.Owner, out EmpireData owner)
                && owner.BattlePlans.TryGetValue(fleet.BattlePlan ?? string.Empty, out BattlePlan plan))
            {
                return plan.PrimaryTarget;
            }

            return "Any";
        }

        /// <summary>
        /// The fleet classifier in the mode the patrol scan and the re-lock use (`FUN_1038_4134`):
        /// None, Any and Starbase accept every fleet; otherwise the fleet fits when one of its
        /// designs is of that type, judged as the battle engine judges a stack.
        /// </summary>
        public static bool FitsTargetType(Fleet fleet, string targetType)
        {
            switch (targetType)
            {
                case null:
                case "None":
                case "Any":
                case "Starbase":
                    return true;
            }

            foreach (ShipToken token in fleet.Composition.Values)
            {
                ShipDesign design = token.Design;
                if (design == null || token.Quantity <= 0)
                {
                    continue;
                }

                bool armed = design.Weapons.Count > 0;
                bool fits;
                switch (targetType)
                {
                    case "Armed Ships":
                        fits = armed;
                        break;
                    case "Unarmed Ships":
                        fits = !armed;
                        break;
                    case "Bombers":
                        fits = design.IsBomber;
                        break;
                    case "Freighters":
                        fits = !armed && !design.IsBomber && design.CargoCapacity > 0;
                        break;
                    case "Fuel Transports":
                        fits = !armed && !design.IsBomber && design.CargoCapacity == 0 && design.FuelCapacity > 0;
                        break;
                    default:
                        fits = false;
                        break;
                }

                if (fits)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
