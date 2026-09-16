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

#region Module Description
// ===========================================================================
// Definition of a battle plan.
// ===========================================================================
#endregion

namespace Nova.Common
{
    #region Using Statements
    using System;
    using System.Xml;
    #endregion

    [Serializable]
    public class BattlePlan
    {
        // FIXME:(priority 2) This should all be enums!
        public string Name            = "Default";
        public string PrimaryTarget   = "Armed Ships";
        public string SecondaryTarget = "Any";
        public string Tactic          = "Maximise Damage";
        public string Attack          = "Enemies";
        public int TargetId;

        /// <summary>
        /// The real dialog's fifth control (docs/behavior-specs-5/client-ui-dialog-catalog.md:
        /// "Primary Target, Secondary Target, Tactic, and Attack Who - plus a Dump Cargo checkbox
        /// and a Rename... button"), missing from this port entirely until now. Like
        /// PrimaryTarget/SecondaryTarget above, added for UI parity with the real dialog; nothing
        /// in ServerState/BattleEngine.cs currently reads this field to actually jettison cargo
        /// during combat - that would be a separate combat-mechanic change, not a dialog-parity
        /// one.
        /// </summary>
        public bool DumpCargo;

        /// <summary>
        /// Valid values for PrimaryTarget/SecondaryTarget - the sole source of truth for these
        /// (confirmed via ServerState/BattleEngine.cs: neither field is ever switched on there,
        /// they're pure display/storage strings). Shared here so both the WinForms and Avalonia
        /// editors read the same list instead of duplicating it in generated UI code.
        /// </summary>
        public static readonly string[] TargetOptions =
        {
            "Any", "Armed Ships", "Bombers", "Freighters", "None", "Starbase", "Unarmed Ships"
        };

        /// <summary>Valid values for Tactic - confirmed the only two ServerState/BattleEngine.cs
        /// actually switches on ("Disengage", "Disengage if Challenged"); the rest affect
        /// damage-ordering, not string comparison.</summary>
        public static readonly string[] TacticOptions =
        {
            "Disengage", "Disengage if Challenged", "Maximise Damage",
            "Maximise Damage Ratio", "Maximise Net Damage", "Minimise Damage to Self"
        };

        /// <summary>
        /// Valid values for Attack - the "legitimate enemies" category confirmed against a
        /// decompile of the exported client (docs/behavior-specs-3/combat-resolution.md): five
        /// settings - no targets, every Enemy-relationship race, every Enemy-or-Neutral race,
        /// all races, or one specific race (via TargetId, checked independently of this list -
        /// see BattleEngine.AreEnemies). "None" was previously missing from this list entirely,
        /// and "Enemies and Neutrals" was previously unhandled by AreEnemies (silently behaved
        /// like "None" instead of its intended "everyone except my Friends" meaning) - both
        /// fixed together.
        /// </summary>
        public static readonly string[] AttackOptions =
        {
            "None", "Enemies", "Enemies and Neutrals", "Everyone"
        };

        #region Construction

        /// <summary>
        /// Default constructor.
        /// </summary>
        public BattlePlan() 
        { 
        }

        #endregion

        #region Load Save Xml

        /// <summary>
        /// Load: initializing constructor from an XmlNode.
        /// </summary>
        /// <param name="node">An XmlNode representing a BattlePlan.</param>
        public BattlePlan(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLower())
                    {
                        case "name":
                            Name = subnode.FirstChild.Value;
                            break;
                        case "primarytarget":
                            PrimaryTarget = subnode.FirstChild.Value;
                            break;
                        case "secondarytarget":
                            SecondaryTarget = subnode.FirstChild.Value;
                            break;
                        case "tactic":
                            Tactic = subnode.FirstChild.Value;
                            break;
                        case "attack":
                            Attack = subnode.FirstChild.Value;
                            break;
                        case "targetid":
                            TargetId = int.Parse(subnode.FirstChild.Value, System.Globalization.NumberStyles.HexNumber);
                            break;
                        case "dumpcargo":
                            DumpCargo = bool.Parse(subnode.FirstChild.Value);
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
        /// Save: Generate an XmlElement representation of a battle plan for saving.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representation of the BattlePlan.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelBattlePlan = xmldoc.CreateElement("BattlePlan");

            Global.SaveData(xmldoc, xmlelBattlePlan, "Name", Name);
            Global.SaveData(xmldoc, xmlelBattlePlan, "PrimaryTarget", PrimaryTarget);
            Global.SaveData(xmldoc, xmlelBattlePlan, "SecondaryTarget", SecondaryTarget);
            Global.SaveData(xmldoc, xmlelBattlePlan, "Tactic", Tactic);
            Global.SaveData(xmldoc, xmlelBattlePlan, "Attack", Attack);
            Global.SaveData(xmldoc, xmlelBattlePlan, "TargetId", TargetId.ToString("X"));
            Global.SaveData(xmldoc, xmlelBattlePlan, "DumpCargo", DumpCargo.ToString());

            return xmlelBattlePlan;
        }

        #endregion
    }
}
