#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
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
// The Mystery Trader (behavior-specs-10/turn-generation-engine.md §5a). It is
// not a fleet: it has no ships or design. In the original it is one 18-byte
// special-object entry of type 3 (category 0x60) holding a position, a
// destination, a speed (a warp number), a bitmask of the races it has already
// served and the one item it carries. This class holds exactly those fields.
// Creation, movement and encounters live in the server's MysteryTraderStep /
// MysteryTraderMovementStep.
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Xml;

    using Nova.Common.DataStructures;

    [Serializable]
    public class MysteryTrader : Mappable
    {
        /// <summary>
        /// The cargo value meaning "technology" (the original's item value 0). Any other value is
        /// a gift-mask bit index 0-12 (ship-design-and-components.md §14a: bits 0-11 the twelve
        /// rare parts, bit 12 the "auxiliary ships" item). The original stores 1 &lt;&lt; bit, or 0
        /// for technology; this port stores the bit index, so technology needs its own marker.
        /// </summary>
        public const int TechnologyItem = -1;

        /// <summary>Gift-mask bit index of the "auxiliary ships" item (value 0x1000).</summary>
        public const int ShipsItem = 12;

        /// <summary>Where the Trader is heading (the original's +6/+8 fields).</summary>
        public NovaPoint Destination = new NovaPoint();

        /// <summary>Speed as a warp number (low nibble of +0xa); it moves speed² ly a year.</summary>
        public int Speed;

        /// <summary>
        /// The races (empire ids) this Trader has already served (the +0xc bitmask). Never reset,
        /// not even by "another pass" (§5a "After arrival").
        /// </summary>
        public HashSet<int> ServedRaces = new HashSet<int>();

        /// <summary>The one item it carries (+0xe): <see cref="TechnologyItem"/> or a bit 0-12.</summary>
        public int Item = TechnologyItem;

        public MysteryTrader()
        {
            Name = "Mystery Trader";
        }

        /// <summary>True when the Trader carries technology rather than a gift-mask item.</summary>
        public bool CarriesTechnology
        {
            get { return Item == TechnologyItem; }
        }

        /// <summary>
        /// Save: Generate an XmlElement representation of the Trader for saving to file.
        /// </summary>
        public new XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelTrader = xmldoc.CreateElement("MysteryTrader");

            xmlelTrader.AppendChild(base.ToXml(xmldoc));
            xmlelTrader.AppendChild(Destination.ToXml(xmldoc, "Destination"));

            Global.SaveData(xmldoc, xmlelTrader, "Speed", Speed.ToString(CultureInfo.InvariantCulture));
            // Saved as "Cargo", not "Item": Item(XmlNode) looks for the first child named "Item" for the key.
            Global.SaveData(xmldoc, xmlelTrader, "Cargo", Item.ToString(CultureInfo.InvariantCulture));

            XmlElement xmlelServed = xmldoc.CreateElement("ServedRaces");
            foreach (int race in ServedRaces)
            {
                Global.SaveData(xmldoc, xmlelServed, "Race", race.ToString(CultureInfo.InvariantCulture));
            }
            xmlelTrader.AppendChild(xmlelServed);

            return xmlelTrader;
        }

        /// <summary>
        /// Load: initializing Constructor from an xml node.
        /// </summary>
        public MysteryTrader(XmlNode node)
            : base(node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "destination":
                            Destination = new NovaPoint(subnode);
                            break;

                        case "speed":
                            Speed = int.Parse(((XmlText)subnode.FirstChild).Value, CultureInfo.InvariantCulture);
                            break;

                        case "cargo":
                            Item = int.Parse(((XmlText)subnode.FirstChild).Value, CultureInfo.InvariantCulture);
                            break;

                        case "servedraces":
                            XmlNode raceNode = subnode.FirstChild;
                            while (raceNode != null)
                            {
                                ServedRaces.Add(int.Parse(((XmlText)raceNode.FirstChild).Value, CultureInfo.InvariantCulture));
                                raceNode = raceNode.NextSibling;
                            }
                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error("Error loading Mystery Trader : " + e.Message);
                }
                subnode = subnode.NextSibling;
            }
        }
    }
}
