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

namespace Nova.Common
{
    using System;
    using System.Globalization;
    using System.Xml;

    /// <summary>
    /// One mineral packet (behavior-specs-10/production-queue.md §10, types 6 and 14-17; §10a,
    /// §10b, §10k item 4). One class covers the five queue items:
    /// - the AUTO "Mineral Packets" entry (type 6, always mixed; <see cref="AutoBuild"/>): N units
    ///   per turn, limited by minerals and resources; with no mass driver or no destination its
    ///   room is 0, so it buys nothing, posts nothing and stays queued (status 2);
    /// - the MANUAL "Mixed Mineral Packet" (17) and "Ironium / Boranium / Germanium Mineral
    ///   Packet" (14-16): one-off orders, deleted with message 297 by the hub before purchase
    ///   when the planet has no mass driver or no destination
    ///   (<see cref="IProductionUnit.RoomForManualOrder"/> = 0; Manufacture posts 297).
    /// Completing units does not touch the planet: the server's Manufacture step launches (or
    /// enlarges) the packet with <see cref="UnitPayload"/> x units (PacketLaunch).
    /// Partial payment follows the record-layout percentage rule of §10: a unit that cannot be
    /// finished stores the largest whole percentage for which the rounded-down share of every
    /// cost component is covered, and pays exactly those rounded-down amounts. An AUTO entry held
    /// back by a MINERAL pays nothing (status 3/4: the walk goes on); one held back by resources
    /// pays its share (status 5-7).
    /// </summary>
    public class PacketProductionUnit : IProductionUnit
    {
        private Resources cost;

        /// <summary>Percent complete of the unit being built (0-99).</summary>
        private int percentComplete;

        /// <summary>A mixed / Ironium / Boranium / Germanium packet.</summary>
        public PacketMineral Mineral { get; private set; }

        /// <summary>True for the auto-build "Mineral Packets" entry (type 6).</summary>
        public bool AutoBuild { get; private set; }

        /// <summary>The minerals one completed unit adds to the packet.</summary>
        public Resources UnitPayload { get; private set; }

        public Resources Cost
        {
            get { return cost; }
        }

        /// <summary>The cost still to pay on the unit being built: each component's cost minus
        /// its cost x percent / 100, rounded down (§10 record layout).</summary>
        public Resources RemainingCost
        {
            get { return cost - PaidFor(cost, percentComplete); }
        }

        /// <summary>The head unit's percent complete (0-99).</summary>
        public int PercentComplete
        {
            get { return percentComplete; }
        }

        public string Name
        {
            get
            {
                if (AutoBuild)
                {
                    return "Mineral Packets";
                }

                switch (Mineral)
                {
                    case PacketMineral.Ironium: return "Ironium Mineral Packet";
                    case PacketMineral.Boranium: return "Boranium Mineral Packet";
                    case PacketMineral.Germanium: return "Germanium Mineral Packet";
                    default: return "Mixed Mineral Packet";
                }
            }
        }

        /// <summary>
        /// A packet item priced for <paramref name="race"/> (Packet Physics and Interstellar
        /// Traveler have their own prices and payloads). The auto entry is always mixed.
        /// </summary>
        public PacketProductionUnit(Race race, PacketMineral mineral, bool autoBuild)
        {
            AutoBuild = autoBuild;
            Mineral = autoBuild ? PacketMineral.Mixed : mineral;
            cost = MineralPacketRules.UnitCost(race, Mineral);
            UnitPayload = MineralPacketRules.UnitPayload(race, Mineral);
        }

        /// <summary>
        /// Load: Read in a ProductionUnit from and XmlNode representation.
        /// </summary>
        public PacketProductionUnit(XmlNode node)
        {
            cost = new Resources();
            UnitPayload = new Resources();

            XmlNode mainNode = node.FirstChild;
            while (mainNode != null)
            {
                switch (mainNode.Name.ToLowerInvariant())
                {
                    case "cost":
                        cost = new Resources(mainNode);
                        break;

                    case "payload":
                        UnitPayload = new Resources(mainNode);
                        break;

                    case "mineral":
                        Mineral = (PacketMineral)Enum.Parse(typeof(PacketMineral), mainNode.FirstChild.Value);
                        break;

                    case "autobuild":
                        AutoBuild = bool.Parse(mainNode.FirstChild.Value);
                        break;

                    case "percentcomplete":
                        percentComplete = int.Parse(mainNode.FirstChild.Value, CultureInfo.InvariantCulture);
                        break;
                }

                mainNode = mainNode.NextSibling;
            }
        }

        /// <summary>
        /// Skipped this year: no accelerator or no destination, or nothing on hand of a cost
        /// component the unit still needs.
        /// </summary>
        public bool IsSkipped(Star star)
        {
            if (!MineralPacketRules.CanLaunch(star))
            {
                return true;
            }

            Resources remaining = RemainingCost;
            return (remaining.Energy > 0 && star.ResourcesOnHand.Energy <= 0)
                || (remaining.Ironium > 0 && star.ResourcesOnHand.Ironium <= 0)
                || (remaining.Boranium > 0 && star.ResourcesOnHand.Boranium <= 0)
                || (remaining.Germanium > 0 && star.ResourcesOnHand.Germanium <= 0);
        }

        /// <summary>Zero, so the auto entry's room is <see cref="SupportableCount"/>.</summary>
        public int? CurrentCount(Star star)
        {
            return 0;
        }

        /// <summary>
        /// The auto entry's room: 0 without an accelerator or a destination (§10k item 4: it
        /// buys nothing and stays queued), otherwise unlimited (N per turn).
        /// </summary>
        public int? SupportableCount(Star star)
        {
            return MineralPacketRules.CanLaunch(star) ? (int?)null : 0;
        }

        /// <summary>A manual packet order with no accelerator or no destination is deleted
        /// before purchase (message 297); otherwise it is not limited.</summary>
        public int? RoomForManualOrder(Star star)
        {
            return MineralPacketRules.CanLaunch(star) ? (int?)null : 0;
        }

        /// <summary>
        /// Pays for one unit. Returns true when it completes (the server launches it); otherwise
        /// banks the percentage rule's partial payment and returns false.
        /// </summary>
        public bool Construct(Star star)
        {
            Resources paid = PaidFor(cost, percentComplete);
            Resources onHand = star.ResourcesOnHand;

            if (onHand >= cost - paid)
            {
                star.ResourcesOnHand = onHand - (cost - paid);
                percentComplete = 0;
                return true;
            }

            if (AutoBuild && ScarcestIsAMineral(cost - paid, onHand))
            {
                // Status 3/4: an auto entry short of a mineral buys nothing more this year.
                return false;
            }

            int newPercent = Math.Min(99, Math.Max(percentComplete, AffordablePercent(cost, paid + onHand)));
            Resources newPaid = PaidFor(cost, newPercent);
            star.ResourcesOnHand = onHand - (newPaid - paid);
            percentComplete = newPercent;
            return false;
        }

        /// <summary>
        /// The largest whole percentage p (0-100) for which cost x p / 100, rounded down, is
        /// covered by <paramref name="available"/> for every component (§10: for the limiting
        /// component with cost c and paid-plus-available x, the larger of
        /// ((x + 1) x 100 / c) - 1 and x x 100 / c, each rounded down).
        /// </summary>
        public static int AffordablePercent(Resources cost, Resources available)
        {
            int percent = 100;
            percent = Math.Min(percent, ComponentPercent(cost.Ironium, available.Ironium));
            percent = Math.Min(percent, ComponentPercent(cost.Boranium, available.Boranium));
            percent = Math.Min(percent, ComponentPercent(cost.Germanium, available.Germanium));
            percent = Math.Min(percent, ComponentPercent(cost.Energy, available.Energy));
            return Math.Max(0, percent);
        }

        private static int ComponentPercent(int cost, int available)
        {
            if (cost <= 0)
            {
                return 100;
            }

            long x = Math.Max(0, available);
            if (x >= cost)
            {
                return 100;
            }

            long a = ((x + 1) * 100 / cost) - 1;
            long b = x * 100 / cost;
            return (int)Math.Min(100, Math.Max(a, b));
        }

        /// <summary>Each component's cost x percent / 100, rounded down.</summary>
        private static Resources PaidFor(Resources cost, int percent)
        {
            return new Resources(
                cost.Ironium * percent / 100,
                cost.Boranium * percent / 100,
                cost.Germanium * percent / 100,
                cost.Energy * percent / 100);
        }

        /// <summary>
        /// True when the scarcest still-needed component (lowest whole-percent affordable, order
        /// ironium, boranium, germanium, resources; a later one wins only when strictly lower) is
        /// a mineral - the same test ProductionOrder.IsShortOfAMineral makes.
        /// </summary>
        private static bool ScarcestIsAMineral(Resources needed, Resources onHand)
        {
            int[] need = { needed.Ironium, needed.Boranium, needed.Germanium, needed.Energy };
            int[] have = { onHand.Ironium, onHand.Boranium, onHand.Germanium, onHand.Energy };
            int scarcest = -1;
            long lowest = long.MaxValue;
            for (int index = 0; index < 4; index++)
            {
                if (need[index] <= 0)
                {
                    continue;
                }

                long percent = Math.Min(100L, Math.Max(0L, (long)have[index]) * 100 / need[index]);
                if (percent < lowest)
                {
                    lowest = percent;
                    scarcest = index;
                }
            }

            return scarcest >= 0 && scarcest < 3;
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelUnit = xmldoc.CreateElement("PacketUnit");

            xmlelUnit.AppendChild(cost.ToXml(xmldoc, "Cost"));
            xmlelUnit.AppendChild(UnitPayload.ToXml(xmldoc, "Payload"));
            Global.SaveData(xmldoc, xmlelUnit, "Mineral", Mineral.ToString());
            Global.SaveData(xmldoc, xmlelUnit, "AutoBuild", AutoBuild.ToString(CultureInfo.InvariantCulture));
            if (percentComplete != 0)
            {
                Global.SaveData(xmldoc, xmlelUnit, "PercentComplete", percentComplete.ToString(CultureInfo.InvariantCulture));
            }

            return xmlelUnit;
        }
    }
}
