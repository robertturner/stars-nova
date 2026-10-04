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

namespace Nova.Common.Commands
{
    using System.Globalization;
    using System.Xml;

    /// <summary>
    /// Sets (or clears) a planet's mineral-packet destination and chosen packet speed - a planet
    /// order, not a waypoint (behavior-specs-10/fleet-movement-scanning-cargo.md: with a planet
    /// selected, Shift+left-click sets the planet's packet destination, only when its starbase has
    /// a mass driver; clicking the planet itself clears it). The speed is stored as chosen; at
    /// launch it is used only when it lies in 5..(best driver warp + 3), otherwise the launch
    /// rating is used (production-queue.md §10b). An empty destination (or the planet itself)
    /// clears the setting.
    /// </summary>
    public class PacketDestinationCommand : ICommand
    {
        public PacketDestinationCommand(string starKey, string destination, int warp)
        {
            StarKey = starKey;
            Destination = destination;
            Warp = warp;
        }

        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        public PacketDestinationCommand(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;

            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "starkey":
                        StarKey = subnode.FirstChild?.Value;
                        break;
                    case "destination":
                        Destination = subnode.FirstChild?.Value;
                        break;
                    case "warp":
                        Warp = int.Parse(subnode.FirstChild.Value, CultureInfo.InvariantCulture);
                        break;
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>The launching planet.</summary>
        public string StarKey { get; set; }

        /// <summary>The destination planet's name; null or empty clears it.</summary>
        public string Destination { get; set; }

        /// <summary>The chosen packet speed (0 = not chosen).</summary>
        public int Warp { get; set; }

        private bool Clears
        {
            get { return string.IsNullOrEmpty(Destination) || Destination == StarKey; }
        }

        /// <summary>
        /// The planet must be the player's own; a destination may be set only while its starbase
        /// carries a mass driver, and must be a planet the player knows of. Clearing is always
        /// allowed.
        /// </summary>
        public bool IsValid(EmpireData empire)
        {
            if (StarKey == null || !empire.OwnedStars.Contains(StarKey))
            {
                return false;
            }

            if (Clears)
            {
                return true;
            }

            return MineralPacketRules.HasAccelerator(empire.OwnedStars[StarKey])
                && empire.StarReports.ContainsKey(Destination);
        }

        public void ApplyToState(EmpireData empire)
        {
            if (!IsValid(empire))
            {
                return;
            }

            Star star = empire.OwnedStars[StarKey];
            if (Clears)
            {
                star.PacketDestination = null;
                star.PacketWarp = 0;
                return;
            }

            star.PacketDestination = Destination;
            star.PacketWarp = Warp;
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "PacketDestination");
            Global.SaveData(xmldoc, xmlelCom, "StarKey", StarKey);
            if (!string.IsNullOrEmpty(Destination))
            {
                Global.SaveData(xmldoc, xmlelCom, "Destination", Destination);
            }

            Global.SaveData(xmldoc, xmlelCom, "Warp", Warp.ToString(CultureInfo.InvariantCulture));
            return xmlelCom;
        }
    }
}
