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
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Common.Commands
{
    using System.Globalization;
    using System.Xml;

    /// <summary>
    /// Sets a planet's "contribute only leftover resources to research" flag - the per-planet
    /// checkbox in the production dialog (behavior-specs-10/production-queue.md section 10k and
    /// the WinForms ProductionDialog's onlyLeftovers control). It is a planet order for the
    /// player's own star; when set, the planet funds research only after the production queue has
    /// taken what it needs (Star.UpdateResearch).
    /// </summary>
    public class OnlyLeftoverCommand : ICommand
    {
        public OnlyLeftoverCommand(string starKey, bool onlyLeftover)
        {
            StarKey = starKey;
            OnlyLeftover = onlyLeftover;
        }

        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        public OnlyLeftoverCommand(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;

            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "starkey":
                        StarKey = subnode.FirstChild?.Value;
                        break;

                    case "onlyleftover":
                        OnlyLeftover = bool.Parse(subnode.FirstChild.Value);
                        break;
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>The planet.</summary>
        public string StarKey { get; set; }

        /// <summary>True: contribute only what the queue leaves over to research.</summary>
        public bool OnlyLeftover { get; set; }

        /// <summary>The planet must be the player's own.</summary>
        public bool IsValid(EmpireData empire)
        {
            return StarKey != null && empire != null && empire.OwnedStars.Contains(StarKey);
        }

        public void ApplyToState(EmpireData empire)
        {
            if (IsValid(empire))
            {
                empire.OwnedStars[StarKey].OnlyLeftover = OnlyLeftover;
            }
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "OnlyLeftover");
            Global.SaveData(xmldoc, xmlelCom, "StarKey", StarKey);
            Global.SaveData(xmldoc, xmlelCom, "OnlyLeftover", OnlyLeftover.ToString(CultureInfo.InvariantCulture));
            return xmlelCom;
        }
    }
}
