#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
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

namespace Nova.Common
{
    /// <summary>
    /// One fleet's attempt to colonize a star this turn, registered by ColoniseTask.Perform and
    /// resolved later (once every fleet has had a chance to arrive) - see Star.PendingColonizations.
    /// </summary>
    public class ColonizationAttempt
    {
        public Fleet Fleet;
        public EmpireData Sender;

        /// <summary>
        /// The colonists landing, in cargo units (kT, 100 colonists each), captured when the
        /// attempt is registered - the fleet itself is dismantled at the task pass, before the
        /// landing is resolved (behavior-specs-9/fleet-movement-scanning-cargo.md §5, "Colonize
        /// resolution, fleet side", step 4: the record goes into the pending-landing ledger).
        /// </summary>
        public int ColonistUnits;

        public ColonizationAttempt(Fleet fleet, EmpireData sender)
        {
            Fleet = fleet;
            Sender = sender;
            ColonistUnits = fleet.Cargo.ColonistsInKilotons;
        }
    }
}
