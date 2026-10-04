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
    /// Mass-packet movement, the packet branch of the special-object pass FUN_10b0_0f9a
    /// (behavior-specs-10/turn-generation-engine.md §1 steps 15 and 21):
    /// <list type="bullet">
    /// <item><b>Full step</b> (mode 0, step 15, before fleets move): every packet with cargo
    ///   moves Warp² ly toward its target planet.</item>
    /// <item><b>Half step</b> (mode 1, step 21, after the production hub): only packets that
    ///   have never moved (launched this turn) move (Warp² / 2) ly.</item>
    /// </list>
    /// A step that reaches the target is an arrival (PacketArrival) and the packet is removed.
    /// Either kind of step sets the packet's sticky moved-mark. Each coordinate step is rounded
    /// to the nearest whole number (half away from zero), as for the Mystery Trader, which moves
    /// in the same pass (the packet rounding is not stated separately: Ambiguity, see report).
    /// No decay is applied on arrival: in-flight decay is step 18's (PacketDecayStep), so a packet
    /// arriving in step 15 has already had the previous year's share (Ambiguity, see report).
    /// </summary>
    public class PacketMovementStep : ITurnStep
    {
        private readonly bool halfStep;
        // The injected test random, or null: each Process then takes the game's seeded stream
        // for this pass (ServerData.CreateRandom), so arrivals are repeatable.
        private readonly Random injectedRandom;
        private Random random;

        /// <param name="halfStep">False for step 15's full step, true for step 21's half step of
        /// packets launched this turn.</param>
        public PacketMovementStep(bool halfStep) : this(halfStep, null)
        {
        }

        /// <summary>Overload for deterministic testing (the arrival's one-in-20 defense roll).</summary>
        public PacketMovementStep(bool halfStep, Random random)
        {
            this.halfStep = halfStep;
            this.injectedRandom = random;
            this.random = random;
        }

        public void Process(ServerData serverState)
        {
            random = injectedRandom ?? serverState.CreateRandom(halfStep ? "PacketHalfStep" : "PacketFullStep");

            foreach (long key in serverState.AllMineralPackets.Keys.OrderBy(k => k).ToList())
            {
                if (!serverState.AllMineralPackets.TryGetValue(key, out MineralPacket packet))
                {
                    continue;
                }

                if (packet.IsEmpty || (halfStep && packet.HasMoved))
                {
                    continue;
                }

                double step = packet.Warp * packet.Warp;
                if (halfStep)
                {
                    step /= 2.0;
                }

                packet.HasMoved = true;
                double remaining = PointUtilities.Distance(packet.Position, packet.Destination);

                if (remaining > step && remaining > 0.0001)
                {
                    int dx = (int)Math.Round((packet.Destination.X - packet.Position.X) * step / remaining, MidpointRounding.AwayFromZero);
                    int dy = (int)Math.Round((packet.Destination.Y - packet.Position.Y) * step / remaining, MidpointRounding.AwayFromZero);
                    packet.Position = new NovaPoint(packet.Position.X + dx, packet.Position.Y + dy);
                    continue;
                }

                packet.Position = new NovaPoint(packet.Destination);
                PacketArrival.Arrive(serverState, packet, random);
                serverState.AllMineralPackets.Remove(key);
            }
        }
    }
}
