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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The waypoint-task pass's final mode (behavior-specs-10/turn-generation-engine.md §1 step 23h;
    /// fleet-movement-scanning-cargo.md §5, task 9): every fleet whose current waypoint carries a
    /// Transfer Fleet task is given to its recipient, after movement and the battle, so a fleet
    /// destroyed in battle gives nothing. Each attempt settles the task, transferred or refused.
    /// </summary>
    public class TransferFleetStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            List<Fleet> candidates = serverState.IterateAllFleets()
                .Where(fleet => fleet.Waypoints.Count > 0 && fleet.Waypoints[0].Task is TransferFleetTask)
                .ToList();

            foreach (Fleet fleet in candidates)
            {
                if (!serverState.AllEmpires.TryGetValue(fleet.Owner, out EmpireData sender)
                    || !sender.OwnedFleets.ContainsKey(fleet.Key))
                {
                    continue;
                }

                Waypoint current = fleet.Waypoints[0];
                TransferFleetTask task = (TransferFleetTask)current.Task;
                task.Messages.Clear();

                serverState.AllEmpires.TryGetValue(task.RecipientId, out EmpireData recipient);

                // The task is settled either way (on success the source fleet is gone).
                current.Task = new NoTask();
                task.Transfer(fleet, sender, recipient);

                serverState.AllMessages.AddRange(task.Messages);
            }
        }
    }
}
