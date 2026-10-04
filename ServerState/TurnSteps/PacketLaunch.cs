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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The completion effect of a mineral-packet queue item (behavior-specs-10/production-queue.md
    /// §10 types 6 and 14-17, §10b), run by Manufacture when units of a packet order complete. The
    /// completion routine is called once with the unit count, so all the units an entry bought this
    /// year go into one packet:
    /// <list type="number">
    /// <item>No accelerator: the packet "disintegrates" (message 209). An accelerator but no
    ///   resolvable destination: message 210. Both are last-resort paths: the hub and the purchase
    ///   already refuse such orders (§10k item 4).</item>
    /// <item>Speed: the planet's chosen packet speed if it lies in 5..(best driver warp + 3),
    ///   otherwise the launch rating; the overspeed class is how far the speed exceeds the launch
    ///   rating (one more for Interstellar Traveler), capped at 3.</item>
    /// <item>The payload (units x the unit payload, each mineral capped at 32,760 kT) merges into
    ///   an existing packet of the same owner leaving the same planet with the same speed, target
    ///   and class, provided that packet's mass BEFORE the merge is still under 16,300 kT (message
    ///   212; the new minerals are then added with no further test); otherwise a new packet is
    ///   created at the planet (message 211).</item>
    /// </list>
    /// "Leaving the same planet" is read as "launched from this planet and not yet moved" (a
    /// packet launched this turn, before step 21's half step) - see the report's ambiguity list.
    /// </summary>
    public static class PacketLaunch
    {
        /// <summary>The Message.Type of every mineral-packet notice.</summary>
        public const string MessageType = "Mineral Packet";

        /// <summary>
        /// Launches (or enlarges) a packet for <paramref name="units"/> completed units of
        /// <paramref name="unit"/> at <paramref name="star"/>. Returns the packet, or null when
        /// the order disintegrated.
        /// </summary>
        public static MineralPacket Launch(ServerData serverState, Star star, PacketProductionUnit unit, int units)
        {
            if (serverState == null || star == null || unit == null || units <= 0)
            {
                return null;
            }

            int launchRating = MineralPacketRules.LaunchRating(star.Starbase);
            if (launchRating <= 0)
            {
                // Message 209.
                Post(serverState, star.Owner, "The mineral packet ordered at " + star.Name
                    + " disintegrated because the planet has no mass driver.");
                return null;
            }

            Star target = null;
            if (!MineralPacketRules.HasTarget(star) || !serverState.AllStars.TryGetValue(star.PacketDestination, out target))
            {
                // Message 210.
                Post(serverState, star.Owner, "The mineral packet ordered at " + star.Name
                    + " disintegrated because the mass driver has no destination. Use Set Dest to choose one.");
                return null;
            }

            serverState.AllEmpires.TryGetValue(star.Owner, out EmpireData owner);
            Race race = owner?.Race ?? star.ThisRace;
            bool interstellarTraveler = race != null && race.HasTrait("IT");

            int bestWarp = MineralPacketRules.BestDriverWarp(star.Starbase);
            int speed = MineralPacketRules.LaunchSpeed(star.PacketWarp, bestWarp, launchRating);
            int overspeedClass = MineralPacketRules.OverspeedClass(speed, launchRating, interstellarTraveler);

            Resources payload = new Resources(
                Cap((long)unit.UnitPayload.Ironium * units),
                Cap((long)unit.UnitPayload.Boranium * units),
                Cap((long)unit.UnitPayload.Germanium * units),
                0);
            int payloadKilotons = payload.Ironium + payload.Boranium + payload.Germanium;

            MineralPacket existing = serverState.AllMineralPackets.Values
                .Where(p => p.Owner == star.Owner && !p.HasMoved && p.OriginName == star.Name
                    && p.TargetName == target.Name && p.Warp == speed && p.OverspeedClass == overspeedClass
                    && p.TotalKilotons < MineralPacketRules.MergeMassLimit)
                .OrderBy(p => p.Key)
                .FirstOrDefault();

            if (existing != null)
            {
                existing.Minerals = new Resources(
                    Cap((long)existing.Minerals.Ironium + payload.Ironium),
                    Cap((long)existing.Minerals.Boranium + payload.Boranium),
                    Cap((long)existing.Minerals.Germanium + payload.Germanium),
                    0);

                // Message 212.
                Post(serverState, star.Owner, star.Name + " has added " + payloadKilotons
                    + "kT of minerals to the mineral packet bound for " + target.Name + ".");
                return existing;
            }

            MineralPacket packet = new MineralPacket();
            packet.Key = NextPacketKey(serverState, star.Owner);
            packet.Position = new NovaPoint(star.Position);
            packet.Minerals = payload;
            packet.Warp = speed;
            packet.OverspeedClass = overspeedClass;
            packet.OriginName = star.Name;
            packet.TargetName = target.Name;
            packet.Destination = new NovaPoint(target.Position);
            packet.HasMoved = false;
            serverState.AllMineralPackets[packet.Key] = packet;

            // Message 211.
            Post(serverState, star.Owner, star.Name + " has launched a mineral packet of " + payloadKilotons
                + "kT bound for " + target.Name + " at warp " + speed + ".");
            return packet;
        }

        /// <summary>
        /// A key for a new packet of <paramref name="owner"/>: the owner in the high bits and the
        /// smallest id that owner's packets are not using.
        /// </summary>
        public static long NextPacketKey(ServerData serverState, ushort owner)
        {
            HashSet<uint> inUse = new HashSet<uint>(serverState.AllMineralPackets.Keys
                .Where(k => k.Owner() == owner)
                .Select(k => k.Id()));

            uint id = 1;
            while (inUse.Contains(id))
            {
                id++;
            }

            return ((long)0).SetOwner(owner).SetId(id);
        }

        private static int Cap(long kilotons)
        {
            return (int)Math.Max(0, Math.Min(MineralPacketRules.MaxKilotonsPerMineral, kilotons));
        }

        /// <summary>A notice to one race; never to "nobody" (Audience 0 means everyone).</summary>
        internal static void Post(ServerData serverState, int audience, string text)
        {
            if (audience == Global.Nobody)
            {
                return;
            }

            Message message = new Message();
            message.Audience = audience;
            message.Text = text;
            message.Type = MessageType;
            serverState.AllMessages.Add(message);
        }
    }
}
