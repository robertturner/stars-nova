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

#region Module Description
// ===========================================================================
// A mineral (mass) packet in flight: in the original one 18-byte special-object
// entry holding a position, the three mineral amounts, a speed (warp), the
// target planet, the overspeed class and a "has moved" mark
// (behavior-specs-10/production-queue.md §10b and §10k item 4,
// turn-generation-engine.md §1 steps 15, 18 and 21). Launch, movement, decay
// and arrival live in the server's Packet*Step / PacketLaunch / PacketArrival.
// The same class is used, as a copy, for the packet sightings an empire's turn
// file carries (EmpireData.MineralPacketReports).
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.Globalization;
    using System.Xml;

    using Nova.Common.DataStructures;

    [Serializable]
    public class MineralPacket : Mappable
    {
        /// <summary>The three mineral amounts in kT (Energy is unused and kept at 0).</summary>
        public Resources Minerals = new Resources();

        /// <summary>The packet's speed as a warp number; it moves Warp² ly a year.</summary>
        public int Warp;

        /// <summary>
        /// How far the launch speed exceeded the launching driver's rating, 0-3 (one more for an
        /// Interstellar Traveler owner, capped at 3). Class 0 does not decay (production-queue.md §10b).
        /// </summary>
        public int OverspeedClass;

        /// <summary>The name of the planet the packet was launched from.</summary>
        public string OriginName;

        /// <summary>The name of the planet the packet is bound for.</summary>
        public string TargetName;

        /// <summary>The target planet's position (planets do not move).</summary>
        public NovaPoint Destination = new NovaPoint();

        /// <summary>
        /// The moved-mark (bit 0x40 of the entry's byte +7): clear on a freshly launched packet,
        /// set the first time it takes a step and never cleared again. The year-end half step
        /// (turn-generation-engine.md §1 step 21) moves only packets without it.
        /// </summary>
        public bool HasMoved;

        public MineralPacket()
        {
            Name = "Mineral Packet";
        }

        /// <summary>Copy constructor (used for the sightings written into a player's turn).</summary>
        public MineralPacket(MineralPacket existing)
            : base(existing)
        {
            if (existing == null)
            {
                return;
            }

            Key = existing.Key;
            Minerals = new Resources(existing.Minerals);
            Warp = existing.Warp;
            OverspeedClass = existing.OverspeedClass;
            OriginName = existing.OriginName;
            TargetName = existing.TargetName;
            Destination = new NovaPoint(existing.Destination);
            HasMoved = existing.HasMoved;
        }

        /// <summary>The packet's total mass in kT (the three minerals).</summary>
        public int TotalKilotons
        {
            get { return Minerals.Ironium + Minerals.Boranium + Minerals.Germanium; }
        }

        /// <summary>True when every mineral is gone; such a packet is destroyed.</summary>
        public bool IsEmpty
        {
            get { return Minerals.Ironium <= 0 && Minerals.Boranium <= 0 && Minerals.Germanium <= 0; }
        }

        /// <summary>
        /// The range of the penetrating scanner every packet of a Packet Physics race carries: the
        /// square of the packet's warp (fleet-movement-scanning-cargo.md §3; race-traits.md §2).
        /// Callers check the owner's trait.
        /// </summary>
        public int ScannerRange
        {
            get { return Warp * Warp; }
        }

        /// <summary>
        /// Save: Generate an XmlElement representation of the packet for saving to file.
        /// </summary>
        public new XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelPacket = xmldoc.CreateElement("MineralPacket");

            xmlelPacket.AppendChild(base.ToXml(xmldoc));
            xmlelPacket.AppendChild(Minerals.ToXml(xmldoc, "Minerals"));
            xmlelPacket.AppendChild(Destination.ToXml(xmldoc, "Destination"));

            Global.SaveData(xmldoc, xmlelPacket, "Warp", Warp.ToString(CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelPacket, "OverspeedClass", OverspeedClass.ToString(CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelPacket, "HasMoved", HasMoved.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(OriginName))
            {
                Global.SaveData(xmldoc, xmlelPacket, "OriginName", OriginName);
            }

            if (!string.IsNullOrEmpty(TargetName))
            {
                Global.SaveData(xmldoc, xmlelPacket, "TargetName", TargetName);
            }

            return xmlelPacket;
        }

        /// <summary>
        /// Load: initializing Constructor from an xml node.
        /// </summary>
        public MineralPacket(XmlNode node)
            : base(node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "minerals":
                            Minerals = new Resources(subnode);
                            break;

                        case "destination":
                            Destination = new NovaPoint(subnode);
                            break;

                        case "warp":
                            Warp = int.Parse(((XmlText)subnode.FirstChild).Value, CultureInfo.InvariantCulture);
                            break;

                        case "overspeedclass":
                            OverspeedClass = int.Parse(((XmlText)subnode.FirstChild).Value, CultureInfo.InvariantCulture);
                            break;

                        case "hasmoved":
                            HasMoved = bool.Parse(((XmlText)subnode.FirstChild).Value);
                            break;

                        case "originname":
                            OriginName = ((XmlText)subnode.FirstChild).Value;
                            break;

                        case "targetname":
                            TargetName = ((XmlText)subnode.FirstChild).Value;
                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error("Error loading Mineral Packet : " + e.Message);
                }

                subnode = subnode.NextSibling;
            }
        }
    }
}
