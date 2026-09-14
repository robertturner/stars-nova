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

namespace Nova.Common.Commands
{
    using System;
    using System.Xml;

    /// <summary>
    /// A command to change this empire's stance toward another empire (Enemy/Neutral/Friend).
    /// docs/behavior-specs-3/diplomacy-relations.md §2 (verified against a decompile of the
    /// exported client) confirms a relationship change is a queued turn order, taking effect at
    /// the next turn generation - not an immediate change, which is what both the WinForms
    /// PlayerRelations dialog and the Avalonia EmpireRelationRowViewModel did before this command
    /// existed (direct EmpireIntel.Relation mutation on the spot). This follows the same
    /// push-then-apply-locally pattern already used everywhere else in this codebase (e.g.
    /// WaypointCommand/RenameFleetCommand) for optimistic UI feedback ahead of the real
    /// server-side turn processing.
    /// </summary>
    public class RelationCommand : ICommand
    {
        /// <summary>
        /// The empire whose EmpireIntel.Relation (as seen by the empire issuing this command)
        /// is being changed.
        /// </summary>
        public ushort TargetEmpireId
        {
            get;
            set;
        }

        /// <summary>
        /// The new relationship stance.
        /// </summary>
        public PlayerRelation NewRelation
        {
            get;
            set;
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="targetEmpireId">The empire whose relation is being changed.</param>
        /// <param name="newRelation">The new relationship stance.</param>
        public RelationCommand(ushort targetEmpireId, PlayerRelation newRelation)
        {
            TargetEmpireId = targetEmpireId;
            NewRelation = newRelation;
        }

        /// <summary>
        /// Determine if this relation change is valid.
        /// </summary>
        /// <param name="empire">The empire issuing the relation change.</param>
        /// <returns>True if the empire has a report on the target empire to change.</returns>
        public bool IsValid(EmpireData empire)
        {
            return empire.EmpireReports.ContainsKey(TargetEmpireId);
        }

        /// <summary>
        /// Perform the relation change.
        /// </summary>
        /// <param name="empire">The empire issuing the relation change.</param>
        public void ApplyToState(EmpireData empire)
        {
            if (IsValid(empire))
            {
                empire.EmpireReports[TargetEmpireId].Relation = NewRelation;
            }
        }

        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        /// <param name="node">An <see cref="XmlNode"/> defining a <see cref="RelationCommand"/>.</param>
        public RelationCommand(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;

            while (subnode != null)
            {
                switch (subnode.Name.ToLower())
                {
                    case "targetempireid":
                        TargetEmpireId = ushort.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;

                    case "newrelation":
                        NewRelation = (PlayerRelation)Enum.Parse(typeof(PlayerRelation), subnode.FirstChild.Value);
                        break;
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>
        /// Save: Serialize this <see cref="RelationCommand"/> to an <see cref="XmlElement"/>.
        /// </summary>
        /// <param name="xmldoc">The parent <see cref="XmlDocument"/>.</param>
        /// <returns>An <see cref="XmlElement"/> representation of the Command.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "Relation");
            Global.SaveData(xmldoc, xmlelCom, "TargetEmpireId", TargetEmpireId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelCom, "NewRelation", NewRelation.ToString());

            return xmlelCom;
        }
    }
}
