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

namespace Nova.Common
{
    using System.Collections.Generic;

    using Nova.Common.Waypoints;

    /// <summary>
    /// Message 78, the "the named fleet has completed its orders" notice of
    /// behavior-specs-11/turn-generation-engine.md §5a "Who sees a Trader", "Message 78":
    ///
    /// The waypoint-task pass posts message 78 when a fleet whose only remaining waypoint carries a
    /// task finishes it; the task is then cleared. Just before posting, any earlier 78 for that
    /// fleet in the same generation is withdrawn, so a fleet gets at most one per generation. The
    /// same withdrawal runs, with nothing posted, when the fleet leaves play: colonising or being
    /// scrapped, being merged into another fleet, or being absorbed by a Trader.
    ///
    /// The message's <see cref="Message.Event"/> is the fleet's <see cref="Fleet.Key"/> (the
    /// original keys message 78 by race and fleet), which is what makes the withdrawal a per-fleet
    /// match. The helper is pure: it operates on a message list the caller owns, so the server's
    /// turn processing and the tests share exactly one rule.
    /// </summary>
    public static class FleetOrdersNotice
    {
        /// <summary>The <see cref="Message.Type"/> of message 78 (a fleet-level notice, distinct
        /// from the production notices 62/63).</summary>
        public const string MessageType = "FleetOrdersComplete";

        /// <summary>
        /// Post message 78 for <paramref name="fleet"/>, withdrawing any earlier one for the same
        /// fleet first ("never posted twice", §5a). Call only when the fleet actually finished its
        /// orders.
        /// </summary>
        public static void Post(List<Message> messages, Fleet fleet)
        {
            if (messages == null || fleet == null)
            {
                return;
            }

            Withdraw(messages, fleet.Key);
            messages.Add(new Message(
                fleet.Owner,
                (fleet.Name ?? "A fleet") + " has completed its orders.",
                MessageType,
                fleet.Key));
        }

        /// <summary>Withdraw every message 78 already posted this generation for one fleet.</summary>
        public static void Withdraw(List<Message> messages, long fleetKey)
        {
            if (messages == null)
            {
                return;
            }

            messages.RemoveAll(message =>
                message != null
                && message.Type == MessageType
                && message.Event is long key
                && key == fleetKey);
        }

        /// <summary>
        /// The whole message-78 lifecycle at the end of one performed waypoint task. A fleet that
        /// left play - its composition was cleared by scrapping or colonising - has any earlier 78
        /// withdrawn and none posted; a merge withdraws the absorbed fleet's 78; and a survivor
        /// whose finished task was on its only remaining waypoint gets the notice.
        /// </summary>
        /// <param name="messages">The generation's message list (serverState.AllMessages).</param>
        /// <param name="task">The task that was just performed (inspected for a merge).</param>
        /// <param name="fleet">The fleet that performed the task.</param>
        /// <param name="onlyRemainingWaypoint">True when the task sat on the fleet's last waypoint.</param>
        /// <param name="sender">The performing fleet's empire.</param>
        /// <param name="receiver">The empire owning the task's target, if any.</param>
        public static void OnTaskFinished(
            List<Message> messages,
            IWaypointTask task,
            Fleet fleet,
            bool onlyRemainingWaypoint,
            EmpireData sender,
            EmpireData receiver)
        {
            if (messages == null || fleet == null)
            {
                return;
            }

            WithdrawAbsorbedMerge(messages, task, sender, receiver);

            // The performing fleet itself left play (scrapped or colonised): withdraw, post nothing.
            if (fleet.Composition.Count == 0)
            {
                Withdraw(messages, fleet.Key);
                return;
            }

            // Only a waypoint that actually CARRIES a task counts (§5a): an ordinary move to a
            // destination with no task is not "finishing orders", so a NoTask posts nothing.
            if (onlyRemainingWaypoint && !(task is NoTask))
            {
                Post(messages, fleet);
            }
        }

        /// <summary>
        /// Withdraw the notice of a fleet merged away by <paramref name="task"/>: a merge empties
        /// the absorbed (other) fleet's ships, and the spec withdraws its 78. Returns true when a
        /// fleet was withdrawn.
        /// </summary>
        private static bool WithdrawAbsorbedMerge(
            List<Message> messages,
            IWaypointTask task,
            EmpireData sender,
            EmpireData receiver)
        {
            if (!(task is SplitMergeTask merge) || merge.OtherFleetKey == 0)
            {
                return false;
            }

            Fleet absorbed = null;
            if (sender != null && sender.OwnedFleets.TryGetValue(merge.OtherFleetKey, out Fleet own))
            {
                absorbed = own;
            }
            else if (receiver != null && receiver.OwnedFleets.TryGetValue(merge.OtherFleetKey, out Fleet other))
            {
                absorbed = other;
            }

            // Only a merge that actually emptied the other fleet counts as "leaves play"; a partial
            // or aborted merge (fuel shortfall) leaves it alive.
            if (absorbed == null || absorbed.Composition.Count != 0)
            {
                return false;
            }

            Withdraw(messages, merge.OtherFleetKey);
            return true;
        }
    }
}
