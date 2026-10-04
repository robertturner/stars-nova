#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars-Nova.
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

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The client side of mineral packets (behavior-specs-10/production-queue.md §10 types 6 and
    /// 14-17, §10a, §10b; fleet-movement-scanning-cargo.md "With a planet selected, Shift+left-
    /// click sets the planet's packet destination"), kept free of UI types so it is unit tested:
    /// <list type="bullet">
    /// <item>the production catalog's packet items: the AUTO "Mineral Packets" entry (always
    ///   mixed) and the four MANUAL items, offered only on a planet whose starbase carries a mass
    ///   driver (§10b's requirement; see <see cref="CatalogItems"/> for the ambiguity);</item>
    /// <item>the planet's packet destination and speed order (<see cref="PacketDestinationCommand"/>):
    ///   the destination choices, the speed choices 5..(best driver warp + 3), and the speed a
    ///   packet would actually fly at;</item>
    /// <item>the map's Shift+click destination pick (planets only, no distance limit, clicking
    ///   the planet itself clears it);</item>
    /// <item>the Inspector rows for a packet in flight (EmpireData.MineralPacketReports).</item>
    /// </list>
    /// </summary>
    public static class PacketOrders
    {
        /// <summary>The "no destination" entry of the destination choices.</summary>
        public const string NoDestination = "(none)";

        /// <summary>
        /// The packet items the production catalog offers for <paramref name="star"/>: the auto
        /// "Mineral Packets" entry, then the manual Mixed / Ironium / Boranium / Germanium items,
        /// each priced for <paramref name="race"/>. Empty unless the planet's starbase carries a
        /// mass driver (§10b: "Requirements: a starbase on the planet whose design carries a mass
        /// driver ... and a destination set on the planet").
        /// AMBIGUITY: the spec states the purchase-time requirement (an auto entry buys nothing,
        /// a manual one is deleted with message 297), not whether the catalog itself hides the
        /// items; read as "offered only with a mass driver", the destination NOT required (it can
        /// be set after queueing, and the order waits or is cancelled at turn time as the spec
        /// says).
        /// </summary>
        public static IReadOnlyList<PacketProductionUnit> CatalogItems(Star star, Race race)
        {
            if (!MineralPacketRules.HasAccelerator(star))
            {
                return Array.Empty<PacketProductionUnit>();
            }

            return new[]
            {
                new PacketProductionUnit(race, PacketMineral.Mixed, true),
                new PacketProductionUnit(race, PacketMineral.Mixed, false),
                new PacketProductionUnit(race, PacketMineral.Ironium, false),
                new PacketProductionUnit(race, PacketMineral.Boranium, false),
                new PacketProductionUnit(race, PacketMineral.Germanium, false),
            };
        }

        /// <summary>A fresh unit for a new queue order of the same kind as a catalog unit (packet
        /// units carry their own partial progress, so two orders must never share one).</summary>
        public static PacketProductionUnit FreshUnit(PacketProductionUnit catalogUnit, Race race)
        {
            return new PacketProductionUnit(race, catalogUnit.Mineral, catalogUnit.AutoBuild);
        }

        /// <summary>
        /// The unit a queued packet order would carry after its Auto/Manual toggle, or null when
        /// it cannot be toggled. Only the mixed packet has both forms (§10: "the only auto-build
        /// packet type is the mixed one"; the auto type 6 pairs with the manual type 17), so a
        /// single-mineral order stays manual. The toggled order starts a fresh unit (its cost is
        /// the same, so the queue's edit check accepts it); partial progress is not carried over.
        /// </summary>
        public static PacketProductionUnit ToggledUnit(ProductionOrder order, Race race)
        {
            if (order?.Unit is not PacketProductionUnit packet)
            {
                return null;
            }

            if (packet.Mineral != PacketMineral.Mixed)
            {
                return null;
            }

            return new PacketProductionUnit(race, PacketMineral.Mixed, !order.IsAutoBuild);
        }

        /// <summary>True when the planet's starbase carries a mass driver, so a packet
        /// destination may be set (PacketDestinationCommand.IsValid).</summary>
        public static bool CanSetDestination(Star star)
        {
            return MineralPacketRules.HasAccelerator(star);
        }

        /// <summary>
        /// The speeds a planet may choose: 5..(best driver warp + 3) (§10b: a chosen speed outside
        /// that range is ignored at launch in favour of the launch rating). Empty without a mass
        /// driver.
        /// </summary>
        public static IReadOnlyList<int> SpeedChoices(Star star)
        {
            int best = star == null ? 0 : MineralPacketRules.BestDriverWarp(star.Starbase);
            if (best <= 0)
            {
                return Array.Empty<int>();
            }

            int top = best + MineralPacketRules.MaximumSpeedAboveDriver;
            int bottom = MineralPacketRules.MinimumChosenSpeed;
            return top < bottom ? Array.Empty<int>() : Enumerable.Range(bottom, top - bottom + 1).ToList();
        }

        /// <summary>The speed a packet launched now would fly at (§10b: the chosen speed when it
        /// lies in 5..(best driver warp + 3), otherwise the launch rating); 0 without a driver.</summary>
        public static int LaunchSpeed(Star star)
        {
            if (!MineralPacketRules.HasAccelerator(star))
            {
                return 0;
            }

            return MineralPacketRules.LaunchSpeed(
                star.PacketWarp,
                MineralPacketRules.BestDriverWarp(star.Starbase),
                MineralPacketRules.LaunchRating(star.Starbase));
        }

        /// <summary>
        /// The planets a destination may be chosen from: every planet this empire knows of
        /// (PacketDestinationCommand requires a StarReports entry), except the launching planet,
        /// sorted by name, with <see cref="NoDestination"/> first.
        /// </summary>
        public static IReadOnlyList<string> DestinationChoices(Star star, EmpireData empire)
        {
            var choices = new List<string> { NoDestination };
            if (star == null || empire == null)
            {
                return choices;
            }

            choices.AddRange(empire.StarReports.Keys
                .Where(name => name != star.Name)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase));
            return choices;
        }

        /// <summary>
        /// The order for a destination choice and speed. <see cref="NoDestination"/>, an empty
        /// name or the planet itself clears the setting (the command treats both as "clear"). A
        /// warp of 0 keeps "not chosen" (the launch rating is used).
        /// </summary>
        public static PacketDestinationCommand DestinationOrder(Star star, string destination, int warp)
        {
            bool clears = string.IsNullOrEmpty(destination) || destination == NoDestination || destination == star.Name;
            return clears
                ? new PacketDestinationCommand(star.Name, null, 0)
                : new PacketDestinationCommand(star.Name, destination, warp);
        }

        /// <summary>
        /// The map's Shift+click pick (fleet-movement-scanning-cargo.md: "Both searches cover
        /// planets only, with no distance limit, and clicking the planet itself clears the
        /// setting"): the planet nearest to the map point (<paramref name="x"/>,
        /// <paramref name="y"/>) among <paramref name="planets"/>, by name; null when there are
        /// none. Ties keep the first.
        /// </summary>
        public static string NearestPlanet(IEnumerable<StarIntel> planets, double x, double y)
        {
            string nearest = null;
            double best = double.MaxValue;
            foreach (StarIntel planet in planets ?? Enumerable.Empty<StarIntel>())
            {
                double dx = planet.Position.X - x;
                double dy = planet.Position.Y - y;
                double distanceSquared = (dx * dx) + (dy * dy);
                if (distanceSquared < best)
                {
                    best = distanceSquared;
                    nearest = planet.Name;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Queues a packet order the way every other client order is sent (pushed onto
        /// ClientData.Commands for the orders file) and applies it to the client's own copy of
        /// the empire at once. An order the command itself rejects (no mass driver, an unknown
        /// planet) is not queued. Returns whether it was queued.
        /// </summary>
        public static bool Issue(ClientData clientState, PacketDestinationCommand command)
        {
            if (!command.IsValid(clientState.EmpireState))
            {
                return false;
            }

            clientState.Commands.Push(command);
            command.ApplyToState(clientState.EmpireState);
            return true;
        }

        /// <summary>The planet's current packet setting for the Inspector: the destination, or
        /// "None".</summary>
        public static string DestinationText(Star star)
        {
            return MineralPacketRules.HasTarget(star) ? star.PacketDestination : "None";
        }

        /// <summary>
        /// The Inspector rows for a packet in flight: owner, origin, destination, speed, the three
        /// mineral amounts and their total, and the distance still to go. A sighting carries what
        /// the server copied into the turn (EmpireData.MineralPacketReports); a missing origin or
        /// target reads "Unknown".
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> DescribePacket(MineralPacket packet, string ownerName)
        {
            var rows = new List<KeyValuePair<string, string>>();
            if (packet == null)
            {
                return rows;
            }

            rows.Add(Row("Owner", string.IsNullOrEmpty(ownerName) ? "Unknown" : ownerName));
            rows.Add(Row("From", string.IsNullOrEmpty(packet.OriginName) ? "Unknown" : packet.OriginName));
            rows.Add(Row("To", string.IsNullOrEmpty(packet.TargetName) ? "Unknown" : packet.TargetName));
            rows.Add(Row("Speed", "Warp " + packet.Warp.ToString(CultureInfo.InvariantCulture)));
            rows.Add(Row("Ironium", Kilotons(packet.Minerals.Ironium)));
            rows.Add(Row("Boranium", Kilotons(packet.Minerals.Boranium)));
            rows.Add(Row("Germanium", Kilotons(packet.Minerals.Germanium)));
            rows.Add(Row("Total", Kilotons(packet.TotalKilotons)));

            if (!string.IsNullOrEmpty(packet.TargetName) && packet.Destination != null)
            {
                double distance = PointUtilities.Distance(packet.Position, packet.Destination);
                rows.Add(Row("Distance to go", distance.ToString("0.0", CultureInfo.InvariantCulture) + " ly"));
            }

            return rows;
        }

        private static string Kilotons(int amount)
        {
            return amount.ToString(CultureInfo.InvariantCulture) + " kT";
        }

        private static KeyValuePair<string, string> Row(string label, string value)
        {
            return new KeyValuePair<string, string>(label, value);
        }
    }
}
