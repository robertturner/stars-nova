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
    using System;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The Lay Mine Field handler (the original's FUN_10b0_3f3a task 6, dispatcher mode 3, after
    /// movement): behavior-specs-10/turn-generation-engine.md section 3, "Mine laying, exact
    /// rule", and fleet-movement-scanning-cargo.md section 5 (task 0 and task 6 rows).
    ///
    /// This lives here rather than in Common/Waypoints/LayMinesTask.cs because that class has no
    /// way to reach ServerData.AllMinefields (Common has no dependency on ServerState).
    /// TurnGenerator.ProcessFleet calls <see cref="Process"/> once per fleet per year, after the
    /// fleet's movement, with whether it moved.
    /// </summary>
    public class LayMines
    {
        /// <summary>A field holding more than this many mines is never added to: a new field is started.</summary>
        public const int MaxMinesBeforeNewField = 999999;

        /// <summary>Serial numbers available for minefields (511, as for every special-object
        /// kind; see <see cref="SpecialObjectTable.SerialNumbersPerOwner"/>).</summary>
        public const int MaxMinefieldSerials = SpecialObjectTable.SerialNumbersPerOwner;

        private readonly ServerData serverState;

        public LayMines(ServerData serverState)
        {
            this.serverState = serverState;
        }

        /// <summary>
        /// One year of mine laying for a fleet, after movement.
        /// - The fleet lays when its current waypoint's task is Lay Mine Field, or (Space
        ///   Demolition only) when its current waypoint has no task and the next one's is Lay
        ///   Mine Field (laying en route).
        /// - A fleet that did not move lays its full total; a fleet that moved lays nothing unless
        ///   its owner is Space Demolition, which lays half of each type, rounded down. So on the
        ///   arrival turn a non-SD fleet lays nothing.
        /// - A fleet with no mine-laying part has the order cancelled (message 191).
        /// - The current waypoint's duration counter is read before laying: 0 is the last year
        ///   (the task is cleared), 5 is never decremented, anything else is decremented.
        /// </summary>
        /// <param name="fleet">The fleet, after this year's movement.</param>
        /// <param name="movedThisYear">Whether the fleet moved (or jumped) this year.</param>
        public void Process(Fleet fleet, bool movedThisYear)
        {
            if (fleet == null || fleet.Waypoints.Count == 0 || fleet.Composition.Count == 0)
            {
                return;
            }

            Race race = null;
            if (serverState.AllEmpires.TryGetValue(fleet.Owner, out EmpireData empire))
            {
                race = empire.Race;
            }

            bool spaceDemolition = race != null && race.HasTrait("SD");

            Waypoint current = fleet.Waypoints[0];
            LayMinesTask task = current.Task as LayMinesTask;
            bool enRoute = task == null
                && spaceDemolition
                && fleet.Waypoints.Count >= 2
                && current.Task is NoTask
                && fleet.Waypoints[1].Task is LayMinesTask;

            if (task == null && !enRoute)
            {
                return;
            }

            // A fleet that moved lays nothing unless it is Space Demolition (:75911-75914).
            if (movedThisYear && !spaceDemolition)
            {
                return;
            }

            if (fleet.NumberOfMines == 0)
            {
                // En route, the order belongs to the next waypoint, and is cancelled (with the
                // same message) when the fleet arrives there.
                if (task != null)
                {
                    Message message = new Message();
                    message.Audience = fleet.Owner;
                    message.Type = "Minefield";
                    message.Text = fleet.Name + " attempted to lay mines. The order has been canceled because no ship in the fleet has a mine laying pod.";
                    serverState.AllMessages.Add(message);
                    current.Task = new NoTask();
                }

                return;
            }

            if (task != null)
            {
                if (task.Duration == 0)
                {
                    current.Task = new NoTask();
                }
                else if (task.Duration != LayMinesTask.Indefinitely)
                {
                    task.Duration--;
                }
            }

            foreach (MinefieldType fieldType in Enum.GetValues(typeof(MinefieldType)))
            {
                int amount = fleet.MinesPerYear(fieldType);

                // The halving is per field type, rounded down (:75941-75943).
                if (movedThisYear)
                {
                    amount /= 2;
                }

                if (amount > 0)
                {
                    Lay(fleet, fieldType, amount);
                }
            }
        }

        /// <summary>
        /// Lays this fleet's full yearly output of every type at its present position, without
        /// any of the order, movement or duration rules (see <see cref="Process"/>).
        /// </summary>
        public void Lay(Fleet fleet)
        {
            foreach (MinefieldType fieldType in Enum.GetValues(typeof(MinefieldType)))
            {
                int amount = fleet.MinesPerYear(fieldType);
                if (amount > 0)
                {
                    Lay(fleet, fieldType, amount);
                }
            }
        }

        /// <summary>
        /// Adds <paramref name="amount"/> mines of one type at the fleet's position: to the
        /// nearest existing field of the same owner and type whose circle covers the fleet
        /// (squared distance at most the mine count), moving its centre to the mine-weighted
        /// average of the old centre and the fleet's position (message 196); or, if there is none
        /// or it already holds more than 999,999 mines, to a new field at the fleet (message 195).
        /// </summary>
        public Minefield Lay(Fleet fleet, MinefieldType fieldType, int amount)
        {
            if (amount <= 0)
            {
                return null;
            }

            Minefield nearest = null;
            double nearestSquare = double.MaxValue;
            foreach (Minefield minefield in serverState.AllMinefields.Values)
            {
                if (minefield.Owner != fleet.Owner || minefield.FieldType != fieldType)
                {
                    continue;
                }

                double square = PointUtilities.DistanceSquare(fleet.Position, minefield.Position);
                if (square <= minefield.NumberOfMines && square < nearestSquare)
                {
                    nearest = minefield;
                    nearestSquare = square;
                }
            }

            Message message = new Message();
            message.Audience = fleet.Owner;
            message.Type = "Minefield";

            if (nearest == null || nearest.NumberOfMines > MaxMinesBeforeNewField)
            {
                if (!CanCreateField(fleet.Owner))
                {
                    // Message 382: no field record could be created.
                    message.Text = fleet.Name + " failed to lay mines this year due to technical difficulties.";
                    serverState.AllMessages.Add(message);
                    return null;
                }

                Minefield newField = new Minefield();
                newField.Key = serverState.AllEmpires[fleet.Owner].GetNextMinefieldKey();
                newField.Position = new NovaPoint(fleet.Position);
                newField.NumberOfMines = amount;
                newField.FieldType = fieldType;
                newField.SafeSpeed = Minefield.SafeWarpByType[(int)fieldType];
                serverState.AllMinefields[newField.Key] = newField;

                message.Event = newField;
                message.Text = fleet.Name + " has dispersed " + amount + " " + TypeLabel(fieldType) + " mines.";
                serverState.AllMessages.Add(message);
                return newField;
            }

            long oldMines = nearest.NumberOfMines;
            long total = oldMines + amount;
            nearest.Position = new NovaPoint(
                (int)(((nearest.Position.X * oldMines) + ((long)fleet.Position.X * amount)) / total),
                (int)(((nearest.Position.Y * oldMines) + ((long)fleet.Position.Y * amount)) / total));
            nearest.NumberOfMines += amount;

            message.Event = nearest;
            message.Text = fleet.Name + " has increased a " + TypeLabel(fieldType) + " minefield by " + amount + " mines.";
            serverState.AllMessages.Add(message);
            return nearest;
        }

        /// <summary>
        /// Whether a new field record can be created (turn-generation-engine.md section 3, "Where
        /// the mines go"; the object-table limits of section 5a): creation fails once the shared
        /// special-object table already holds more than 4,049 records of all kinds, or once all 511
        /// serial numbers for minefields are in use for this owner. See
        /// <see cref="SpecialObjectTable"/>.
        /// </summary>
        public bool CanCreateField(ushort owner)
        {
            return !SpecialObjectTable.IsFull(serverState)
                && SpecialObjectTable.CanAllocateMinefield(serverState, owner);
        }

        private static string TypeLabel(MinefieldType fieldType)
        {
            switch (fieldType)
            {
                case MinefieldType.Heavy:
                    return "heavy";
                case MinefieldType.SpeedBump:
                    return "speed bump";
                default:
                    return "standard";
            }
        }
    }
}
