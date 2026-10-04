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
    /// Class for constructing 1 defense unit.
    /// </summary>
    public class DefenseProductionUnit : IProductionUnit
    {
        public Resources Cost
        {
            private set;
            get;
        }
                
        public Resources RemainingCost
        {
            private set;
            get;
        }
        
        public string Name
        {
            get {return "Defenses";}
        }
        
        /// <summary>
        /// Load: Read in a ProductionUnit from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionUnit</param>
        public DefenseProductionUnit(XmlNode node)
        {
            XmlNode mainNode = node.FirstChild;
            while (mainNode != null)
            {
                switch (mainNode.Name.ToLowerInvariant())
                {
                    case "cost":
                        Cost = new Resources(mainNode);
                        break;

                    case "remainingcost":
                        RemainingCost = new Resources(mainNode);
                        break;
                }

                mainNode = mainNode.NextSibling;
            }
        }


        /// <summary>
        /// initializing constructor.
        /// </summary>
        /// <param name="race">The race building the defense (Inner Strength pays 3/5 of the
        /// price).</param>
        public DefenseProductionUnit(Race race)
        {
            // behavior-specs-8/production-queue.md section 5: a defense costs the SDI component's
            // own record - 15 resources plus 5 kT each of the three minerals - whatever defense
            // technology the race has learned (the previous revision's PRT-keyed 25/44/48 figures
            // were Mineral Packet kilotonnages, read off the wrong case group).
            int ironium = Global.DefenseIroniumCost;
            int boranium = Global.DefenseBoraniumCost;
            int germanium = Global.DefenseGermaniumCost;
            int energy = Global.DefenseEnergyCost;

            // Inner Strength's "planetary defenses cost 40% less" lives in the cost calculator
            // itself: all four fields are multiplied by 3 and integer-divided by 5, so an Inner
            // Strength defense costs 9 resources plus 3 kT of each mineral.
            if (race != null && race.HasTrait("IS"))
            {
                ironium = ironium * 3 / 5;
                boranium = boranium * 3 / 5;
                germanium = germanium * 3 / 5;
                energy = energy * 3 / 5;
            }

            Cost = RemainingCost = new Resources(ironium, boranium, germanium, energy);
        }

        /// <summary>
        /// Return true if this production item is to be skipped.
        /// </summary>
        public bool IsSkipped(Star star)
        {
            if (star.Defenses >= star.GetMaxDefenses())
            {
                return true;
            }

            // A defense needs resources AND 5 kT of each mineral (Global.DefenseIroniumCost).
            if (star.ResourcesOnHand.Energy <= 0
                || (Cost.Ironium > 0 && star.ResourcesOnHand.Ironium <= 0)
                || (Cost.Boranium > 0 && star.ResourcesOnHand.Boranium <= 0)
                || (Cost.Germanium > 0 && star.ResourcesOnHand.Germanium <= 0))
            {
                return true;
            }

            return false;
        }

        public int? CurrentCount(Star star)
        {
            return star.Defenses;
        }

        /// <summary>One defense per 2,500 colonists of next year's projected population, rounded
        /// up and never above the planet's defense cap - the auto-build clamp. See IProductionUnit.
        /// SupportableCount's own comment.</summary>
        public int? SupportableCount(Star star)
        {
            return star.GetFutureOperableDefenses();
        }

        /// <summary>Four times the planet's habitability percent, clamped to 10..100 (0 for
        /// Alternate Reality) - a manual order is cut to this minus what is already built.</summary>
        public int? BuildCap(Star star)
        {
            return star.GetMaxDefenses();
        }

        /// <summary>
        /// Construct a defense unit on the Star.
        /// </summary>
        /// <param name="star">The star building the defense unit.</param>
        /// <returns>Returns true if the unit is completed, otherwise false.</returns>
        public bool Construct(Star star)
        {
            // Partial build.
            if (!(star.ResourcesOnHand >= RemainingCost))
            {
                // used to temporarily store the amount of resources we are short
                Resources lacking = RemainingCost - star.ResourcesOnHand;

                // Normalized; 1.0 = 100%
                double percentBuildable = 1.0;

                // determine which resource limits production (i.e. is able to complete the smallest percentage of production)
                if (percentBuildable > (1 - ((double)lacking.Ironium / RemainingCost.Ironium)) && lacking.Ironium > 0)
                {
                    percentBuildable = 1 - ((double)lacking.Ironium / RemainingCost.Ironium);
                }

                if (percentBuildable > (1 - ((double)lacking.Boranium / RemainingCost.Boranium)) && lacking.Boranium > 0)
                {
                    percentBuildable = 1 - ((double)lacking.Boranium / RemainingCost.Boranium);
                }

                if (percentBuildable > (1 - ((double)lacking.Germanium / RemainingCost.Germanium)) && lacking.Germanium > 0)
                {
                    percentBuildable = 1 - ((double)lacking.Germanium / RemainingCost.Germanium);
                }

                if (percentBuildable > (1 - ((double)lacking.Energy / RemainingCost.Energy)) && lacking.Energy > 0)
                {
                    percentBuildable = 1 - ((double)lacking.Energy / RemainingCost.Energy);
                }

                // What we spend on the partial builld.
                Resources partialPayment = Resources.PartialPayment(RemainingCost, percentBuildable, star.ResourcesOnHand);
                star.ResourcesOnHand -= partialPayment;
                RemainingCost -= partialPayment;

                return false;
            }
            else // Fully build this unit.
            {
                star.ResourcesOnHand -= RemainingCost;
                star.Defenses++;
                RemainingCost = Cost;
                return true;
            }

        }
                
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelUnit = xmldoc.CreateElement("DefenseUnit");

            xmlelUnit.AppendChild(Cost.ToXml(xmldoc, "Cost"));

            xmlelUnit.AppendChild(RemainingCost.ToXml(xmldoc, "RemainingCost"));

            return xmlelUnit;
        }
    }
}
