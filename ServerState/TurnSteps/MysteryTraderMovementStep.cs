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

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Mystery Trader movement: the Trader branch of the special-object pass, mode 0
    /// (FUN_10b0_0f9a(0), behavior-specs-10/turn-generation-engine.md §1 step 15 and §5a "Mystery
    /// Trader movement"), run each year BEFORE fleets move. Not gated by "No Random Events" (§1b).
    /// For each Trader, in table (key) order:
    /// <list type="number">
    /// <item>Course change: at speed 12 or less, Next(25) == 0 raises the speed by 1 and every
    ///   player gets message 304; then Next(3) == 0 also picks a new destination on a random edge
    ///   (<see cref="MysteryTraderStep.RandomEdgePoint"/>).</item>
    /// <item>Arrival: when the remaining distance is no more than speed², the Trader arrives. If
    ///   any other Trader exists it is removed; if it is the only one, Next(2) == 0 removes it
    ///   (the first-listed outcome is read as 0), otherwise it makes "another pass": it moves to
    ///   the destination, its speed becomes max(speed - 1, 7), it gets a fresh random-edge
    ///   destination and every player gets message 192; it does not move again that year.</item>
    /// <item>Otherwise it steps speed² ly straight toward the destination, each coordinate step
    ///   rounded to the nearest whole number (half away from zero: the original's ±0.5 constants).</item>
    /// </list>
    /// The set of races it has served is never reset.
    /// </summary>
    public class MysteryTraderMovementStep : ITurnStep
    {
        /// <summary>The course-change roll applies only at speed 12 or less.</summary>
        public const int CourseChangeSpeedLimit = 12;

        /// <summary>"Another pass" never drops the speed below 7.</summary>
        public const int MinimumPassSpeed = 7;

        // The injected test random, or null: each Process then takes the game's seeded
        // "MysteryTraderMovement" stream (ServerData.CreateRandom), so movement is repeatable.
        private readonly Random injectedRandom;
        private Random random;

        public MysteryTraderMovementStep() : this(null)
        {
        }

        /// <summary>Overload for deterministic testing (draw order in the class summary).</summary>
        public MysteryTraderMovementStep(Random random)
        {
            this.injectedRandom = random;
            this.random = random;
        }

        public void Process(ServerData serverState)
        {
            random = injectedRandom ?? serverState.CreateRandom("MysteryTraderMovement");

            foreach (long key in serverState.AllMysteryTraders.Keys.OrderBy(k => k).ToList())
            {
                MysteryTrader trader;
                if (serverState.AllMysteryTraders.TryGetValue(key, out trader))
                {
                    Move(serverState, trader);
                }
            }
        }

        private void Move(ServerData serverState, MysteryTrader trader)
        {
            // 1. Course change.
            if (trader.Speed <= CourseChangeSpeedLimit && random.Next(25) == 0)
            {
                trader.Speed++;
                bool newDestination = random.Next(3) == 0;
                if (newDestination)
                {
                    trader.Destination = MysteryTraderStep.RandomEdgePoint(random);
                }

                // Message 304.
                MysteryTraderStep.Broadcast(serverState, "The Mystery Trader has changed course and is now travelling at warp "
                    + trader.Speed + (newDestination ? ", heading for " + trader.Destination : string.Empty) + ".");
            }

            double step = trader.Speed * trader.Speed;
            double remaining = PointUtilities.Distance(trader.Position, trader.Destination);

            // 2. Step (the original's 0.0001 zero guard: an exhausted distance counts as arrival).
            if (remaining > step && remaining > 0.0001)
            {
                int dx = (int)Math.Round((trader.Destination.X - trader.Position.X) * step / remaining, MidpointRounding.AwayFromZero);
                int dy = (int)Math.Round((trader.Destination.Y - trader.Position.Y) * step / remaining, MidpointRounding.AwayFromZero);
                trader.Position = new NovaPoint(trader.Position.X + dx, trader.Position.Y + dy);
                return;
            }

            // 3/4. Arrival.
            if (serverState.AllMysteryTraders.Count > 1 || random.Next(2) == 0)
            {
                serverState.AllMysteryTraders.Remove(trader.Key);
                return;
            }

            trader.Position = new NovaPoint(trader.Destination);
            trader.Speed = Math.Max(trader.Speed - 1, MinimumPassSpeed);
            trader.Destination = MysteryTraderStep.RandomEdgePoint(random);

            // Message 192.
            MysteryTraderStep.Broadcast(serverState, "The Mystery Trader has turned around at the edge of the galaxy to make another pass, heading for "
                + trader.Destination + ".");
        }
    }
}
