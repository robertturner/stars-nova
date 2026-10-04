#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010 stars-nova
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
    using System.Collections.Generic;
    using System.Text;
    using System.Xml;
 
    /// <summary>
    /// This module holds the settings for a player. 
    /// </summary>
    [Serializable]
    public class PlayerSettings
    {
        public string RaceName; // The path & file name of the race.
        public string AiProgram; // The path & file name of the AI application or "Human"
        public ushort PlayerNumber; // The order number of the player from 1 - Global.MaxPlayers        
        
        /// <summary>
        /// The computer player's AI category (behavior-specs-10/ai-opponent-behavior.md section 1a:
        /// the archetype 0-5 picked in the New Game player row becomes the category the personality
        /// dispatcher switches on). -1 (the default, and every older file) means "not set": the AI
        /// launcher then passes no personality code and DefaultAi plays its own default. Only
        /// meaningful when AiProgram is not "Human".
        /// </summary>
        public int AiCategory = -1;

        /// <summary>
        /// The computer player's skill tier (behavior-specs-10/turn-generation-engine.md §5a, the
        /// AI submenu's tier list): 0 Easy, 1 Standard, 2 Tough, 3 Expert. -1 (the default, and
        /// every older file) means "not recorded". Read by the Mystery Trader's planet trade, which
        /// only Tough and Expert computer players make. Only meaningful when AiProgram is not
        /// "Human".
        /// </summary>
        public int AiSkill = -1;
    
        /// <summary>
        /// Default constructor. 
        /// </summary>
        public PlayerSettings()
        {
        }
        
        /// <summary>
        /// Load: constructor to load PlayerSettings from an XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a PlayerSettings representation (from a save file).</param>
        public PlayerSettings(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "racename":
                            RaceName = subnode.FirstChild.Value;
                            break;
                        case "aiprogram":
                            AiProgram = subnode.FirstChild.Value;
                            break;
                        case "aicategory":
                            AiCategory = int.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "aiskill":
                            AiSkill = int.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "playernumber":
                            PlayerNumber = ushort.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                    }
                }
                catch
                {
                    // ignore incomplete or unset values
                }

                subnode = subnode.NextSibling;
            }
        }
        
        /// <summary>
        /// Save: Generate an XmlElement representation of the PlayerSettings.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representing the PlayerSettings (to be written to file).</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelPlayerSettings = xmldoc.CreateElement("PlayerSettings");
            
            Global.SaveData(xmldoc, xmlelPlayerSettings, "RaceName", RaceName);
            Global.SaveData(xmldoc, xmlelPlayerSettings, "AiProgram", AiProgram);
            Global.SaveData(xmldoc, xmlelPlayerSettings, "PlayerNumber", PlayerNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (AiCategory >= 0)
            {
                Global.SaveData(xmldoc, xmlelPlayerSettings, "AiCategory", AiCategory.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (AiSkill >= 0)
            {
                Global.SaveData(xmldoc, xmlelPlayerSettings, "AiSkill", AiSkill.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        
            return xmlelPlayerSettings;
        }
    }
}
