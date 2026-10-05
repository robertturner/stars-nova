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

namespace Nova.Server
{
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// The original's shared special-object table (fleet-movement-scanning-cargo.md, "special-object
    /// table"): minefields, mineral packets, deep-space salvage/wreckage, wormholes and the Mystery
    /// Trader all draw their per-owner serial numbers from one pool, and the table itself holds a
    /// bounded number of records of all kinds. The allocator (<c>FUN_1118_0000</c>) refuses a new
    /// record when the table already holds more than <see cref="MaxRecordsBeforeAllocation"/>
    /// (so at most 4,050 objects of all kinds can exist), or when the owner already holds
    /// <see cref="SerialNumbersPerOwner"/> records of that kind - serials 0-510; 511 is never
    /// issued. Nova keeps the kinds in separate dictionaries, so the table's own rules are
    /// reconstructed here from their combined counts (behavior-specs-11/production-queue.md
    /// section 10b, turn-generation-engine.md sections 3 and 5a).
    /// </summary>
    public static class SpecialObjectTable
    {
        /// <summary>The allocator refuses once the table already holds more than this many records
        /// of all kinds (4,049), so at most 4,050 objects can exist.</summary>
        public const int MaxRecordsBeforeAllocation = 4049;

        /// <summary>The serial numbers available to one owner for one kind of special object:
        /// 0-510 (511 is never issued).</summary>
        public const int SerialNumbersPerOwner = 511;

        /// <summary>Every special object of every kind, across all owners.</summary>
        public static int TotalRecords(ServerData serverState)
        {
            if (serverState == null)
            {
                return 0;
            }

            return serverState.AllMinefields.Count
                + serverState.AllMineralPackets.Count
                + serverState.AllDeepSpaceMinerals.Count
                + serverState.AllWormholes.Count
                + serverState.AllMysteryTraders.Count;
        }

        /// <summary>True when no kind may gain a new record: the table already holds more than
        /// <see cref="MaxRecordsBeforeAllocation"/> records.</summary>
        public static bool IsFull(ServerData serverState)
        {
            return TotalRecords(serverState) > MaxRecordsBeforeAllocation;
        }

        /// <summary>True when <paramref name="owner"/> may allocate another mineral packet (it
        /// owns fewer than 511 packets).</summary>
        public static bool CanAllocatePacket(ServerData serverState, ushort owner)
        {
            return CountPackets(serverState, owner) < SerialNumbersPerOwner;
        }

        /// <summary>True when <paramref name="owner"/> may allocate another minefield (it owns
        /// fewer than 511 minefields of any type).</summary>
        public static bool CanAllocateMinefield(ServerData serverState, ushort owner)
        {
            return CountMinefields(serverState, owner) < SerialNumbersPerOwner;
        }

        /// <summary>The packets <paramref name="owner"/> currently has.</summary>
        public static int CountPackets(ServerData serverState, ushort owner)
        {
            if (serverState == null)
            {
                return 0;
            }

            return serverState.AllMineralPackets.Keys.Count(key => key.Owner() == owner);
        }

        /// <summary>The minefields (of any type) <paramref name="owner"/> currently has.</summary>
        public static int CountMinefields(ServerData serverState, ushort owner)
        {
            if (serverState == null)
            {
                return 0;
            }

            return serverState.AllMinefields.Values.Count(field => field.Owner == owner);
        }
    }
}
