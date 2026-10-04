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
    /// Queues production-template edits (<see cref="ProductionTemplateCommand"/>) and applies
    /// them to the client's own EmpireData. A slot's content order carries the whole slot, so an
    /// earlier order for the same slot still in the stack is dropped; likewise for the default
    /// choice.
    /// </summary>
    public static class TemplateOrders
    {
        /// <summary>Stores a slot's new content.</summary>
        public static void SetSlot(ClientData clientState, int slot, ProductionTemplate template)
        {
            Queue(clientState, new ProductionTemplateCommand(slot, template));
        }

        /// <summary>Chooses the default slot (or ProductionTemplateSet.NoDefault).</summary>
        public static void SetDefault(ClientData clientState, int defaultSlot)
        {
            Queue(clientState, new ProductionTemplateCommand(defaultSlot));
        }

        private static void Queue(ClientData clientState, ProductionTemplateCommand command)
        {
            if (!command.IsValid(clientState.EmpireState))
            {
                return;
            }

            List<ICommand> kept = clientState.Commands
                .Where(existing => !Supersedes(command, existing))
                .Reverse()
                .ToList();
            clientState.Commands.Clear();
            foreach (ICommand existing in kept)
            {
                clientState.Commands.Push(existing);
            }

            clientState.Commands.Push(command);
            command.ApplyToState(clientState.EmpireState);
        }

        private static bool Supersedes(ProductionTemplateCommand newer, ICommand existing)
        {
            if (!(existing is ProductionTemplateCommand older))
            {
                return false;
            }

            bool sameSlot = newer.Slot >= 0 && older.Slot == newer.Slot && !older.DefaultSlot.HasValue;
            bool sameDefault = newer.Slot < 0 && newer.DefaultSlot.HasValue && older.Slot < 0;
            return sameSlot || sameDefault;
        }
    }
}
