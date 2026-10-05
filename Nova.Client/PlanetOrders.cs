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
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Client
{
    using Nova.Common;
    using Nova.Common.Commands;

    /// <summary>
    /// Queueing helpers for the production dialog's per-planet settings that are not production
    /// orders - currently the "contribute only leftover resources to research" checkbox
    /// (behavior-specs-10/production-queue.md section 10k). Mirrors
    /// <see cref="PacketOrders.Issue"/>: an order the command rejects is not queued.
    /// </summary>
    public static class PlanetOrders
    {
        /// <summary>Queues an only-leftover order and applies it to the client's own copy.</summary>
        public static bool Issue(ClientData clientState, OnlyLeftoverCommand command)
        {
            if (!command.IsValid(clientState.EmpireState))
            {
                return false;
            }

            clientState.Commands.Push(command);
            command.ApplyToState(clientState.EmpireState);
            return true;
        }

        /// <summary>The order for a checkbox change on <paramref name="star"/>.</summary>
        public static OnlyLeftoverCommand OnlyLeftoverOrder(Star star, bool onlyLeftover)
        {
            return new OnlyLeftoverCommand(star.Name, onlyLeftover);
        }
    }
}
