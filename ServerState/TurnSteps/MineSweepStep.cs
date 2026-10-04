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
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Mine sweeping by fleets and starbases: turn-generation step 24 (FUN_10b8_465e), after the
    /// post-movement stage (battle, bombardment, Mystery Trader) and before repair (step 25) -
    /// behavior-specs-10/turn-generation-engine.md section 1 and section 11 "Mine sweeping".
    /// </summary>
    /// <remarks>
    /// Spec rules implemented exactly: a fleet or starbase with a sweep rate removes mines from
    /// every minefield it sits inside (squared distance to the centre not above the mine count,
    /// the same inside test the rest of the minefield code uses) whose owner it treats as an
    /// enemy - a fleet by its battle plan's Attack Who setting (BattleEngine.IsLegitimateTarget),
    /// a starbase from any owner it has not rated Friend. The amount is the rate, one third of
    /// it against the third field type (Speed Bump), at least 2, but never more than is needed to
    /// shrink the field until the sweeper's own position lies outside it, and never more than the
    /// field holds. The sweeper's owner gets message 194 (fleet) or 244 (starbase), the field's
    /// owner message 190 (an unidentified party swept mines from the field). A field reduced to
    /// nothing is deleted. Sweepers are processed in fleet-table order, each against every field
    /// in turn, so a later sweeper sees the earlier sweepers' reductions.
    /// NOT given by behavior-specs-10 (FUN_1080_1ca2 / FUN_1080_1d1c are named but not read
    /// out): the sweep rate itself. This step uses the community-documented Stars! rule - each
    /// beam weapon sweeps (its damage) x (its range) squared mines a year, shield sappers none,
    /// missiles and torpedoes none - with the starbase's +1 range (combat-resolution.md section 3:
    /// starbases get "+1 ... to minesweeping rate"). The "at least 2" floor is applied after the
    /// one-third reduction (the spec's order is not explicit).
    /// </remarks>
    public class MineSweepStep : ITurnStep
    {
        /// <summary>Message type: "Minefield", so Message.ToXml saves the field's key as the
        /// Goto event.</summary>
        public const string FleetSweptType = "Minefield";

        public void Process(ServerData serverState)
        {
            if (serverState.AllMinefields.Count == 0)
            {
                return;
            }

            foreach (Fleet sweeper in serverState.IterateAllFleets().ToList())
            {
                int rate = SweepRate(sweeper);
                if (rate <= 0 || sweeper.Position == null)
                {
                    continue;
                }

                foreach (Minefield field in serverState.AllMinefields.Values.ToList())
                {
                    if (field.NumberOfMines <= 0 || !TreatsAsEnemy(serverState, sweeper, field.Owner))
                    {
                        continue;
                    }

                    // Positions are whole light-years, so the squared distance is an integer.
                    long distanceSquare = (long)Math.Round(PointUtilities.DistanceSquare(sweeper.Position, field.Position));
                    if (distanceSquare > field.NumberOfMines)
                    {
                        continue;
                    }

                    int swept = MinesSwept(rate, field.FieldType, field.NumberOfMines, distanceSquare);
                    if (swept <= 0)
                    {
                        continue;
                    }

                    field.NumberOfMines -= swept;
                    SendMessages(serverState, sweeper, field, swept);

                    if (field.NumberOfMines <= 0)
                    {
                        serverState.AllMinefields.Remove(field.Key);
                    }
                }
            }
        }

        /// <summary>
        /// The mines one sweeper removes from one field it sits inside: its rate (a third of it
        /// against a Speed Bump field), at least 2, capped at what shrinks the field until the
        /// sweeper is outside it (a field of m mines with the sweeper at squared distance d
        /// excludes it once m &lt; d, so m - d + 1 mines) and at the field's own mine count.
        /// </summary>
        public static int MinesSwept(int rate, MinefieldType fieldType, int mines, long distanceSquare)
        {
            if (rate <= 0 || mines <= 0)
            {
                return 0;
            }

            int amount = fieldType == MinefieldType.SpeedBump ? rate / 3 : rate;
            amount = Math.Max(2, amount);

            long neededToExclude = mines - Math.Max(0L, distanceSquare) + 1;
            long result = Math.Min(amount, Math.Min(neededToExclude, mines));
            return (int)Math.Max(0L, result);
        }

        /// <summary>A fleet's (or starbase's) yearly sweep rate: the sum over its ships.</summary>
        public static int SweepRate(Fleet fleet)
        {
            if (fleet == null)
            {
                return 0;
            }

            long rate = 0;
            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (token.Quantity > 0 && token.Design != null)
                {
                    rate += (long)SweepRate(token.Design, fleet.IsStarbase) * token.Quantity;
                }
            }

            return (int)Math.Min(int.MaxValue, rate);
        }

        /// <summary>
        /// One ship's sweep rate: for each beam weapon slot (shield sappers excluded), the slot's
        /// damage (Weapon.Power, already multiplied by the components in the slot) times its
        /// range squared, the range raised by 1 on a starbase.
        /// </summary>
        public static int SweepRate(ShipDesign design, bool starbase)
        {
            if (design?.Weapons == null)
            {
                return 0;
            }

            long rate = 0;
            foreach (Weapon weapon in design.Weapons)
            {
                if (weapon == null || !weapon.IsBeam || weapon.Group == WeaponType.shieldSapper)
                {
                    continue;
                }

                long range = weapon.Range + (starbase ? 1 : 0);
                rate += weapon.Power * range * range;
            }

            return (int)Math.Min(int.MaxValue, rate);
        }

        /// <summary>
        /// Whether the sweeper sweeps a field of <paramref name="fieldOwner"/>: never its own; a
        /// starbase sweeps any owner it has not rated Friend, a fleet only an owner its battle
        /// plan would attack.
        /// </summary>
        public static bool TreatsAsEnemy(ServerData serverState, Fleet sweeper, ushort fieldOwner)
        {
            if (sweeper.Owner == fieldOwner || !serverState.AllEmpires.TryGetValue(sweeper.Owner, out EmpireData owner))
            {
                return false;
            }

            if (sweeper.IsStarbase)
            {
                return !(owner.EmpireReports.TryGetValue(fieldOwner, out EmpireIntel intel) && intel.Relation == PlayerRelation.Friend);
            }

            return BattleEngine.IsLegitimateTarget(serverState, sweeper.Owner, sweeper.BattlePlan, fieldOwner);
        }

        private static void SendMessages(ServerData serverState, Fleet sweeper, Minefield field, int swept)
        {
            string where = " at " + field.Position.X + ", " + field.Position.Y;

            Message toSweeper = new Message();
            toSweeper.Audience = sweeper.Owner;
            toSweeper.Type = FleetSweptType;
            toSweeper.Event = field;
            toSweeper.Text = sweeper.IsStarbase
                ? "Your starbase " + sweeper.Name + " has swept " + swept + " mines from a minefield" + where + "." // 244
                : "Your fleet " + sweeper.Name + " has swept " + swept + " mines from a minefield" + where + "."; // 194
            serverState.AllMessages.Add(toSweeper);

            // 190: the field's owner is not told who swept.
            Message toOwner = new Message();
            toOwner.Audience = field.Owner;
            toOwner.Type = FleetSweptType;
            toOwner.Event = field;
            toOwner.Text = "Someone has swept " + swept + " mines from your minefield" + where + "."
                + (field.NumberOfMines <= 0 ? " The field is gone." : string.Empty);
            serverState.AllMessages.Add(toOwner);
        }
    }
}
