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
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// Mass-packet decay in flight, the packet half of FUN_10b8_433a (behavior-specs-10/
    /// turn-generation-engine.md §1 step 18 and §3; production-queue.md §10b, FUN_10b8_4200): after
    /// movement and before the production hub, every packet loses, per mineral, nothing at
    /// overspeed class 0, otherwise 10% / 25% / 50% (classes 1-3) with a minimum of 10 kT, or half
    /// the percentage and a 5 kT minimum for a Packet Physics owner. A packet with nothing left is
    /// destroyed. (A "resting" packet - speed 0 - is a salvage record in the original; this port
    /// keeps salvage as DeepSpaceMinerals, decayed by DeepSpaceMineralDecayStep.)
    /// </summary>
    public class PacketDecayStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            foreach (long key in serverState.AllMineralPackets.Keys.OrderBy(k => k).ToList())
            {
                MineralPacket packet = serverState.AllMineralPackets[key];
                serverState.AllEmpires.TryGetValue(packet.Owner, out EmpireData owner);
                bool packetPhysics = owner?.Race != null && owner.Race.HasTrait("PP");

                MineralPacketRules.Decay(packet, packetPhysics);

                if (packet.IsEmpty)
                {
                    serverState.AllMineralPackets.Remove(key);
                }
            }
        }
    }
}
