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

    using Nova.Common;
    using Nova.Common.Combat;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The yearly repair pass (behavior-specs-10/turn-generation-engine.md §1 step 25, §11
    /// "Repair, units and exact rule"). It runs once per generation AFTER the post-movement stage
    /// (battle, bombardment, Mystery Trader) and before the year counter, not inside the movement
    /// loop as it used to - so a fleet is no longer healed before it fights.
    /// </summary>
    /// <remarks>
    /// Which fleets: every surviving fleet with a damaged stack, EXCEPT a fleet that fought this
    /// generation (was enrolled in a battle) or "saw action" during movement (hit a minefield, or
    /// completed a Stargate jump - fleet-movement-scanning-cargo.md §5).
    /// Location rate, in percent of one ship's armor (the spec's 5/10/15/25/40/100 units of 1/500
    /// of armor): moved this year 1; else deep space 2; orbiting a planet it does not own (allied
    /// or not) 3; its own planet 5 when that planet has no starbase OR its starbase fought this
    /// generation; its own planet with a fresh starbase 8, or 20 when the starbase hull has a dock.
    /// Inner Strength doubles the location rate only; then the fleet-wide transport bonus is added
    /// (also to a fleet that moved): +5 if any occupied stack is a Fuel Transport, +10 if any is a
    /// Super-Fuel Transport (10, not 15, with both).
    /// Starbase self-repair: a starbase that did not fight this generation repairs 10% of its
    /// armor (15% for Inner Strength).
    /// The rate is an absolute amount per damaged ship, subtracted from the token's damage word
    /// (ShipToken.PackedDamage, see <see cref="Repair"/>); there is no one-armor-point minimum
    /// (the old code added one).
    /// </remarks>
    public class RepairStep : ITurnStep
    {
        private readonly ISet<long> fleetsThatMoved;

        private readonly ISet<long> fleetsThatSawAction;

        /// <summary>A step with no movement information: every fleet counts as not moved.</summary>
        public RepairStep()
            : this(new HashSet<long>(), new HashSet<long>())
        {
        }

        /// <summary>
        /// The turn generator's step: <paramref name="fleetsThatMoved"/> and
        /// <paramref name="fleetsThatSawAction"/> are the generator's own per-turn sets, filled
        /// during movement and cleared at the start of the next movement pass.
        /// </summary>
        public RepairStep(ISet<long> fleetsThatMoved, ISet<long> fleetsThatSawAction)
        {
            this.fleetsThatMoved = fleetsThatMoved ?? new HashSet<long>();
            this.fleetsThatSawAction = fleetsThatSawAction ?? new HashSet<long>();
        }

        public void Process(ServerData serverState)
        {
            HashSet<long> fought = FleetsThatFoughtThisTurn(serverState);

            foreach (Fleet fleet in serverState.IterateAllFleets())
            {
                bool skipped = fought.Contains(fleet.Key) || fleetsThatSawAction.Contains(fleet.Key);
                if (skipped)
                {
                    continue;
                }

                Repair(fleet, RepairRatePercent(serverState, fleet, fleetsThatMoved.Contains(fleet.Key), fought));
            }
        }

        /// <summary>
        /// The fleets (by key, starbases included) enrolled in a battle this generation: every
        /// stack copied into one of this turn's battle reports (EmpireData.BattleReports is
        /// cleared at the start of each generation; BattleEngine copies each enrolled stack,
        /// with its parent fleet's key, into the report).
        /// </summary>
        public static HashSet<long> FleetsThatFoughtThisTurn(ServerData serverState)
        {
            HashSet<long> fought = new HashSet<long>();
            HashSet<BattleReport> seen = new HashSet<BattleReport>();

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                foreach (BattleReport report in empire.BattleReports)
                {
                    if (report == null || !seen.Add(report))
                    {
                        continue;
                    }

                    foreach (Stack stack in report.Stacks.Values)
                    {
                        fought.Add(stack.ParentKey);
                    }
                }
            }

            return fought;
        }

        /// <summary>
        /// The yearly repair rate in percent of armor for a fleet that is not skipped (see the
        /// class remarks). <paramref name="foughtThisTurn"/> decides whether the starbase of the
        /// planet the fleet orbits counts as "fresh".
        /// </summary>
        public static int RepairRatePercent(ServerData serverState, Fleet fleet, bool movedThisYear, ISet<long> foughtThisTurn)
        {
            EmpireData owner;
            bool innerStrength = serverState.AllEmpires.TryGetValue(fleet.Owner, out owner)
                && owner.Race != null && owner.Race.HasTrait("IS");

            if (fleet.IsStarbase)
            {
                // 50 damage units (10%) a year, 75 (15%) for Inner Strength - not a plain doubling.
                return innerStrength ? 15 : 10;
            }

            Star star = null;
            if (fleet.InOrbit != null)
            {
                serverState.AllStars.TryGetValue(fleet.InOrbit.Name, out star);
            }

            int rate;
            if (movedThisYear)
            {
                rate = 1;
            }
            else if (star == null)
            {
                rate = 2;
            }
            else if (star.Owner != fleet.Owner)
            {
                rate = 3;
            }
            else if (star.Starbase == null || (foughtThisTurn != null && foughtThisTurn.Contains(star.Starbase.Key)))
            {
                rate = 5;
            }
            else
            {
                rate = star.Starbase.CanRefuel ? 20 : 8;
            }

            if (innerStrength)
            {
                rate *= 2;
            }

            bool hasFuelTransport = false;
            bool hasSuperFuelTransport = false;
            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (token.Quantity > 0 && token.Design != null && token.Design.IsFuelTransportHull)
                {
                    if (token.Design.Blueprint.Name == "Super-Fuel Transport")
                    {
                        hasSuperFuelTransport = true;
                    }
                    else
                    {
                        hasFuelTransport = true;
                    }
                }
            }

            return rate + (hasSuperFuelTransport ? 10 : (hasFuelTransport ? 5 : 0));
        }

        /// <summary>
        /// Repairs every damaged token on its damage word (turn-generation-engine.md section 11,
        /// "Repair, units and exact rule"): the rate, <paramref name="ratePercent"/>% = 5 x that
        /// many 1/500-of-armor units, is subtracted from the units each damaged ship carries; if
        /// the rate is at least the units the whole word is cleared (which also forgets the
        /// damaged-ship percentage). The percentage of damaged ships otherwise never changes, and
        /// the pooled armor is set back from the word (<see cref="DamageWord.Store"/>), so a
        /// damaged ship's damage stays a whole number of armor points (units x armor / 500,
        /// truncated, at least 1).
        /// </summary>
        public static void Repair(Fleet fleet, int ratePercent)
        {
            if (ratePercent <= 0)
            {
                return;
            }

            int rateUnits = ratePercent * (DamageWord.UnitsPerArmor / 100);

            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (token.Design == null || token.Quantity <= 0)
                {
                    continue;
                }

                DamageWord word = DamageWord.For(token);
                if (word.IsUndamaged)
                {
                    continue;
                }

                DamageWord repaired = word.Units <= rateUnits
                    ? new DamageWord(0, 0)
                    : new DamageWord(word.Percent, word.Units - rateUnits);
                DamageWord.Store(token, repaired);
            }
        }
    }
}
