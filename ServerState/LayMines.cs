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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Server
{
    using Nova.Common;

    /// <summary>
    /// Applies a fleet's Lay Mines waypoint task: adds this turn's worth of newly-laid mines
    /// (Fleet.NumberOfMines) to an existing minefield of ours nearby, or starts a new one.
    ///
    /// This lives here rather than in Common/Waypoints/LayMinesTask.cs (where the equivalent logic
    /// used to sit, commented out, behind a "TODO: Implement per empire minefields" note) because
    /// that class has no way to reach ServerData.AllMinefields - Common has no dependency on
    /// ServerState, so a waypoint task genuinely cannot look up or create a minefield itself. This
    /// mirrors how Bombing.cs and CheckForMinefields.cs already live in this layer for the same
    /// reason. TurnGenerator calls Lay() right after LayMinesTask.Perform() succeeds, in the same
    /// place it already dispatches every other waypoint task's actual game-state effect.
    /// </summary>
    public class LayMines
    {
        private readonly ServerData serverState;

        public LayMines(ServerData serverState)
        {
            this.serverState = serverState;
        }

        /// <summary>
        /// Lays this fleet's current mine-laying output at its present position. Only meaningful
        /// once LayMinesTask.IsValid has already confirmed the fleet actually has mine-laying
        /// capability - a fleet with none contributes nothing here.
        /// </summary>
        /// <param name="fleet">The fleet executing a Lay Mines order this turn.</param>
        public void Lay(Fleet fleet)
        {
            int minesToLay = fleet.NumberOfMines;
            if (minesToLay <= 0)
            {
                return;
            }

            // See if a minefield of ours already exists here (allowing the same position
            // tolerance IsNear uses everywhere else a waypoint's exact placement can't be relied
            // on) - if so, this turn's mines are added to it rather than starting a new field.
            foreach (Minefield minefield in serverState.AllMinefields.Values)
            {
                if (minefield.Owner == fleet.Owner && PointUtilities.IsNear(fleet.Position, minefield.Position))
                {
                    minefield.NumberOfMines += minesToLay;
                    return;
                }
            }

            Minefield newField = new Minefield();
            newField.Key = serverState.AllEmpires[fleet.Owner].GetNextMinefieldKey();
            newField.Position = fleet.Position;
            newField.NumberOfMines = minesToLay;
            serverState.AllMinefields[newField.Key] = newField;
        }
    }
}
