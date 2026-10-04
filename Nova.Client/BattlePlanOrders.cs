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

namespace Nova.Client
{
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Commands;

    /// <summary>
    /// Queues the client's current battle plans and fleet assignments as one
    /// <see cref="BattlePlansCommand"/>, replacing any earlier one still in the stack (the
    /// command carries the whole state, so only the newest matters). The client's own
    /// EmpireData already holds the edited state; the command is not re-applied to it.
    /// </summary>
    public static class BattlePlanOrders
    {
        public static BattlePlansCommand Queue(ClientData clientState)
        {
            EmpireData empire = clientState.EmpireState;
            BattlePlansCommand command = new BattlePlansCommand(
                empire.BattlePlans.Values,
                empire.OwnedFleets.Values.ToDictionary(fleet => fleet.Key, fleet => fleet.BattlePlan));

            // ClientData.Commands enumerates newest first; rebuild it oldest first without the
            // superseded command, then push the new one on top.
            List<ICommand> kept = clientState.Commands.Where(existing => !(existing is BattlePlansCommand)).Reverse().ToList();
            clientState.Commands.Clear();
            foreach (ICommand existing in kept)
            {
                clientState.Commands.Push(existing);
            }

            clientState.Commands.Push(command);
            return command;
        }
    }
}
