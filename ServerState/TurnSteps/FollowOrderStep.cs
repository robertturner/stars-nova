#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars! Nova.
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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Follow orders (behavior-specs-10/turn-generation-engine.md §1 steps 9 and 11;
    /// fleet-movement-scanning-cargo.md §5, "Target": choosing a fleet for waypoint 0 of a fleet
    /// with no further waypoints creates a follow order).
    /// <list type="number">
    /// <item>Step 9, the mark: every fleet whose ONLY waypoint targets another fleet is marked;
    ///   every other fleet is unmarked.</item>
    /// <item>Step 11, propagation, up to <see cref="MaximumPasses"/> passes: a marked fleet copies
    ///   its followed fleet's onward waypoint (the leader's waypoint 1) in as its own second leg.
    ///   Passes repeat so a chain of followers settles (C leads B leads A: pass 1 gives B C's
    ///   leg, pass 2 gives A the copy B now holds).</item>
    /// <item>A follower whose target is gone, or is not going anywhere (no onward waypoint once
    ///   the passes are over), gets message 312 and loses the mark.</item>
    /// </list>
    /// The marks are shared with <see cref="FleetPursuit"/>: the end-of-movement refresh skips the
    /// fleet-targeted legs of a marked fleet (fleet-movement-scanning-cargo.md §5, "The mark").
    /// They live for one generation only (step 39's visibility pass wipes them); this step
    /// rebuilds them from scratch every turn.
    /// </summary>
    /// <remarks>
    /// Ambiguity (reported): "copies its followed fleet's onward waypoint in as its own second
    /// leg" does not say whether the leader's waypoint TASK comes along. Copying a Colonise, Scrap
    /// or Transfer Fleet task onto a follower would make it act on the leader's orders, so this
    /// port copies the leg (position, destination, target and warp) and gives it no task.
    /// Not reproduced: the movement pass ordering "later passes move followers once the fleet
    /// they follow has moved" - in this port the follower owns an independent copy of the leg,
    /// so the order in which the two fleets move does not change where either ends up.
    /// </remarks>
    public class FollowOrderStep : ITurnStep
    {
        /// <summary>Follow-order propagation runs at most this many passes (§1 step 11).</summary>
        public const int MaximumPasses = 8;

        /// <summary>The message type of notice 312.</summary>
        public const string MessageType = "Follow Fleet";

        private readonly HashSet<long> followMarks;

        /// <summary>A step with its own private mark set (tests).</summary>
        public FollowOrderStep() : this(new HashSet<long>())
        {
        }

        /// <param name="followMarks">The generation's follow marks, shared with
        /// <see cref="FleetPursuit.FollowMarked"/>; cleared and rebuilt here.</param>
        public FollowOrderStep(HashSet<long> followMarks)
        {
            this.followMarks = followMarks;
        }

        /// <summary>The fleets marked as followers by the last <see cref="Process"/>.</summary>
        public HashSet<long> FollowMarks
        {
            get { return followMarks; }
        }

        public void Process(ServerData serverState)
        {
            // Step 9: clear every mark, then mark every fleet whose only waypoint targets a fleet.
            followMarks.Clear();
            List<Fleet> fleetTable = serverState.IterateAllFleets().Where(fleet => fleet.Composition.Count > 0).ToList();
            Dictionary<long, Fleet> live = fleetTable.ToDictionary(fleet => fleet.Key);

            List<Fleet> pending = new List<Fleet>();
            foreach (Fleet fleet in fleetTable)
            {
                if (fleet.Waypoints.Count == 1 && fleet.Waypoints[0].IsFleetTarget && fleet.Waypoints[0].TargetFleetKey != fleet.Key)
                {
                    followMarks.Add(fleet.Key);
                    pending.Add(fleet);
                }
            }

            // Step 11: up to eight passes, in fleet-table order.
            for (int pass = 0; pass < MaximumPasses && pending.Count > 0; pass++)
            {
                bool progress = false;
                foreach (Fleet follower in pending.ToList())
                {
                    if (!live.TryGetValue(follower.Waypoints[0].TargetFleetKey, out Fleet leader) || leader.Waypoints.Count < 2)
                    {
                        continue;
                    }

                    Waypoint leg = new Waypoint(leader.Waypoints[1]);
                    leg.Position = new NovaPoint(leader.Waypoints[1].Position);
                    leg.Task = new NoTask();
                    follower.Waypoints.Insert(1, leg);
                    pending.Remove(follower);
                    progress = true;
                }

                if (!progress)
                {
                    break;
                }
            }

            // Whatever is left followed a fleet that is gone or not going anywhere: message 312,
            // and the mark goes.
            foreach (Fleet follower in pending)
            {
                followMarks.Remove(follower.Key);

                Message message = new Message();
                message.Audience = follower.Owner;
                message.Type = MessageType;
                message.Text = follower.Name + " was ordered to follow a fleet that did not move, so it has stayed where it was.";
                serverState.AllMessages.Add(message);
            }
        }
    }
}
