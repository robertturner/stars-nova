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
    using System;
    using System.Xml;

    /// <summary>
    /// Sets or clears a fleet's Repeat Orders flag: the original's order record type 10
    /// (fleet id, new flag), written when the fleet's check box changed
    /// (behavior-specs-10/fleet-movement-scanning-cargo.md §5, "Orders written"). The host accepts
    /// it for any fleet the player owns.
    /// </summary>
    public class RepeatOrdersCommand : ICommand
    {
        public RepeatOrdersCommand(long fleetKey, bool repeatOrders)
        {
            FleetKey = fleetKey;
            RepeatOrders = repeatOrders;
        }

        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        public RepeatOrdersCommand(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;

            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "fleetkey":
                        FleetKey = long.Parse(subnode.FirstChild.Value, System.Globalization.NumberStyles.HexNumber);
                        break;
                    case "repeatorders":
                        RepeatOrders = bool.Parse(subnode.FirstChild.Value);
                        break;
                }

                subnode = subnode.NextSibling;
            }
        }

        public long FleetKey { get; set; }

        public bool RepeatOrders { get; set; }

        /// <inheritdoc />
        public bool IsValid(EmpireData empire)
        {
            return empire.OwnedFleets.ContainsKey(FleetKey);
        }

        /// <inheritdoc />
        public void ApplyToState(EmpireData empire)
        {
            if (IsValid(empire))
            {
                empire.OwnedFleets[FleetKey].RepeatOrders = RepeatOrders;
            }
        }

        /// <inheritdoc />
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "RepeatOrders");
            Global.SaveData(xmldoc, xmlelCom, "FleetKey", FleetKey.ToString("X"));
            Global.SaveData(xmldoc, xmlelCom, "RepeatOrders", RepeatOrders.ToString());
            return xmlelCom;
        }
    }
}
