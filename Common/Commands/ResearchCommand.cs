 #region Copyright Notice
 // ============================================================================
 // Copyright (C) 2011 The Stars-Nova Project
 //
 // This file is part of Stars-Nova.
 // See <http://sourceforge.net/projects/stars-nova/>;.
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
 // along with this program. If not, see <http://www.gnu.org/licenses/>;
 // ===========================================================================
 #endregion
 
namespace Nova.Common.Commands
{
    using System;
    using System.Xml;
    
    /// <summary>
    /// Command that describes the change of research state. Includes both
    /// new(if at all) budget and target.
    /// </summary>
    public class ResearchCommand : ICommand
    {
        public int Budget
        {
            get;
            set;
        }
        
        
        public TechLevel Topics
        {
            get;
            set;
        }

        /// <summary>
        /// The "next field to research" setting (EmpireData.ResearchNextField:
        /// Research.NextFieldSame, a field index, or Research.NextFieldLowest), or null to leave
        /// the empire's setting unchanged (an order file that does not carry it).
        /// </summary>
        public int? NextField
        {
            get;
            set;
        }
        
        
        public ResearchCommand()
        {
            Budget = 10;
            Topics = new TechLevel(0, 0, 1, 0, 0, 0);
        }
        
        
        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        /// <param name="node">An <see cref="XmlNode"/> within
        /// a Nova component definition file (xml document).
        /// </param>
        public ResearchCommand(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;

            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "budget":
                      Budget = int.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                    break;

                    case "topics":
                        Topics = new TechLevel(subnode);
                    break;

                    case "nextfield":
                        NextField = int.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                }
            
                subnode = subnode.NextSibling;
            }
        }
        
        
        public bool IsValid(EmpireData empire)
        {
            if (Budget < 0 || Budget > 100)
            {
                return false;
            }
            
            // Invalidate if nothing really changed.
            bool nextFieldChanged = NextField.HasValue && NextField.Value != empire.ResearchNextField;
            if (Budget == empire.ResearchBudget && Topics == empire.ResearchTopics && !nextFieldChanged)
            {
                return false;
            }
            
            return true;
        }
        
        
        public void ApplyToState(EmpireData empire)
        {
            empire.ResearchBudget = Budget;
            empire.ResearchTopics = Topics;
            if (NextField.HasValue)
            {
                empire.ResearchNextField = NextField.Value;
            }
        }
        
        
        /// <summary>
        /// Save: Serialize this property to an <see cref="XmlElement"/>.
        /// </summary>
        /// <param name="xmldoc">The parent <see cref="XmlDocument"/>.</param>
        /// <returns>An <see cref="XmlElement"/> representation of the Property.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "Research");
            Global.SaveData(xmldoc, xmlelCom, "Budget", Budget.ToString(System.Globalization.CultureInfo.InvariantCulture));
            xmlelCom.AppendChild(Topics.ToXml(xmldoc, "Topics"));            
            if (NextField.HasValue)
            {
                Global.SaveData(xmldoc, xmlelCom, "NextField", NextField.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            return xmlelCom;
        }
    }
}
