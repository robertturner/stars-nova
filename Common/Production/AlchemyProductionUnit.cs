#region Copyright Notice
// ============================================================================
// COpyright (C) 2010 Pavel Kazlou
// Copyright (C) 2011, 2012 The Stars-Nova Project
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
    using System.Xml;

    /// <summary>
    /// Converts resources directly into minerals. See
    /// docs/behavior-specs/production-queue.md §7: "Each unit of mineral alchemy will turn a
    /// mere 100 of your resources (25 if you have the Mineral Alchemy trait) into 1 kT of each
    /// of the three minerals."
    /// </summary>
    public class AlchemyProductionUnit : IProductionUnit
    {
        private Resources cost;
        private Resources remainingCost;

        public Resources Cost
        {
            get { return cost; }
        }

        public Resources RemainingCost
        {
            get { return remainingCost; }
        }

        public string Name
        {
            get { return "Mineral Alchemy"; }
        }

        /// <summary>
        /// initializing constructor.
        /// </summary>
        /// <param name="race">Race performing the conversion (Mineral Alchemy trait lowers the cost).</param>
        public AlchemyProductionUnit(Race race)
        {
            int resourceCost = race.HasTrait("MA") ? 25 : 100;
            cost = new Resources(0, 0, 0, resourceCost);
            remainingCost = cost;
        }

        /// <summary>
        /// Load: Read in a ProductionUnit from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionUnit</param>
        public AlchemyProductionUnit(XmlNode node)
        {
            XmlNode mainNode = node.FirstChild;
            while (mainNode != null)
            {
                switch (mainNode.Name.ToLower())
                {
                    case "cost":
                        cost = new Resources(mainNode);
                        break;

                    case "remainingcost":
                        remainingCost = new Resources(mainNode);
                        break;
                }

                mainNode = mainNode.NextSibling;
            }
        }

        /// <summary>
        /// Returns true if this production item will be skipped (no resources to convert).
        /// </summary>
        public bool IsSkipped(Star star)
        {
            return star.ResourcesOnHand.Energy <= 0;
        }

        // No persistent "current count" concept - Mineral Alchemy is a continuous resource-to-
        // mineral conversion, not a countable installation, so an auto-build order for it keeps
        // its original consume-to-zero-then-remove behavior (see ProductionOrder.Process).
        public int? CurrentCount(Star star)
        {
            return null;
        }

        /// <summary>
        /// Convert resources into 1 kT of each mineral. Like the other production units,
        /// a turn that can't fully afford one unit banks partial progress toward it.
        /// </summary>
        public bool Construct(Star star)
        {
            if (star.ResourcesOnHand.Energy < remainingCost.Energy)
            {
                remainingCost.Energy -= star.ResourcesOnHand.Energy;
                star.ResourcesOnHand.Energy = 0;
                return false;
            }
            else
            {
                star.ResourcesOnHand.Energy -= remainingCost.Energy;
                star.ResourcesOnHand.Ironium += 1;
                star.ResourcesOnHand.Boranium += 1;
                star.ResourcesOnHand.Germanium += 1;
                remainingCost = cost;
                return true;
            }
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelUnit = xmldoc.CreateElement("AlchemyUnit");

            xmlelUnit.AppendChild(cost.ToXml(xmldoc, "Cost"));

            xmlelUnit.AppendChild(remainingCost.ToXml(xmldoc, "RemainingCost"));

            return xmlelUnit;
        }
    }
}
