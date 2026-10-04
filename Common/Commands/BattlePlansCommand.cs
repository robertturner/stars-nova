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
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Xml;

    /// <summary>
    /// The player's whole battle-plan list, in order, and every owned fleet's plan assignment
    /// (behavior-specs-10/client-interface.md "Battle Plans and Relations dialogs": ordered,
    /// named records, the first cannot be removed, at most the plan limit; deleting a plan keeps
    /// the fleets' assignments consistent). Before this command the Battle Plans editor and any
    /// assignment only changed the client's copy: no order carried them, so the server always
    /// fought with the default plan.
    /// Applied as a whole: the plans replace the empire's list, then each listed assignment is set
    /// on the empire's own fleet, and any fleet left naming a plan that no longer exists is moved
    /// to the first plan (the battle engine looks plans up by name).
    /// </summary>
    public class BattlePlansCommand : ICommand
    {
        public BattlePlansCommand(IEnumerable<BattlePlan> plans, IDictionary<long, string> assignments)
        {
            Plans = (plans ?? Enumerable.Empty<BattlePlan>()).Select(Copy).ToList();
            Assignments = new Dictionary<long, string>(assignments ?? new Dictionary<long, string>());
        }

        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        public BattlePlansCommand(XmlNode node)
        {
            Plans = new List<BattlePlan>();
            Assignments = new Dictionary<long, string>();

            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "battleplan":
                        Plans.Add(new BattlePlan(subnode));
                        break;

                    case "assignment":
                        long key = long.Parse(subnode.Attributes["Fleet"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        Assignments[key] = subnode.Attributes["Plan"].Value;
                        break;
                }

                subnode = subnode.NextSibling;
            }
        }

        public List<BattlePlan> Plans { get; }

        /// <summary>Fleet key to plan name.</summary>
        public Dictionary<long, string> Assignments { get; }

        public bool IsValid(EmpireData empire)
        {
            if (empire == null || Plans.Count == 0 || Plans.Count > Global.MaxBattlePlans)
            {
                return false;
            }

            HashSet<string> names = new HashSet<string>();
            foreach (BattlePlan plan in Plans)
            {
                if (plan == null || string.IsNullOrWhiteSpace(plan.Name) || !names.Add(plan.Name))
                {
                    return false;
                }
            }

            return true;
        }

        public void ApplyToState(EmpireData empire)
        {
            empire.BattlePlans.Clear();
            foreach (BattlePlan plan in Plans)
            {
                empire.BattlePlans[plan.Name] = Copy(plan);
            }

            string firstPlan = Plans[0].Name;
            foreach (KeyValuePair<long, string> assignment in Assignments)
            {
                if (empire.OwnedFleets.ContainsKey(assignment.Key))
                {
                    empire.OwnedFleets[assignment.Key].BattlePlan = empire.BattlePlans.ContainsKey(assignment.Value ?? string.Empty)
                        ? assignment.Value
                        : firstPlan;
                }
            }

            foreach (Fleet fleet in empire.OwnedFleets.Values)
            {
                if (fleet.BattlePlan == null || !empire.BattlePlans.ContainsKey(fleet.BattlePlan))
                {
                    fleet.BattlePlan = firstPlan;
                }
            }
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "BattlePlans");
            foreach (BattlePlan plan in Plans)
            {
                xmlelCom.AppendChild(plan.ToXml(xmldoc));
            }

            foreach (KeyValuePair<long, string> assignment in Assignments.OrderBy(entry => entry.Key))
            {
                XmlElement xmlelAssignment = xmldoc.CreateElement("Assignment");
                xmlelAssignment.SetAttribute("Fleet", assignment.Key.ToString("X", CultureInfo.InvariantCulture));
                xmlelAssignment.SetAttribute("Plan", assignment.Value ?? string.Empty);
                xmlelCom.AppendChild(xmlelAssignment);
            }

            return xmlelCom;
        }

        private static BattlePlan Copy(BattlePlan plan)
        {
            return new BattlePlan
            {
                Name = plan.Name,
                PrimaryTarget = plan.PrimaryTarget,
                SecondaryTarget = plan.SecondaryTarget,
                Tactic = plan.Tactic,
                Attack = plan.Attack,
                TargetId = plan.TargetId,
                DumpCargo = plan.DumpCargo,
            };
        }
    }
}
