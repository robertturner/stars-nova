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
    /// <remarks>
    /// Where an AUTO Alchemy entry sits decides what it does (behavior-specs-10/production-
    /// queue.md section 7, "How the code does it"): in last position (or alone) it is bought like
    /// any item with its quantity forced to 1,000; anywhere else it buys nothing itself and only
    /// arms an <see cref="AlchemyShortfallConversion"/> for the entry right behind it (see
    /// ProductionOrder.Process and Manufacture.Items). A MANUAL Alchemy entry is always an
    /// ordinary item.
    /// </remarks>
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

        /// <summary>Resources one unit costs: 100, or 25 with the Mineral Alchemy trait.</summary>
        public int ResourcesPerUnit
        {
            get { return cost.Energy; }
        }

        /// <summary>
        /// initializing constructor.
        /// </summary>
        /// <param name="race">Race performing the conversion (Mineral Alchemy trait lowers the cost).</param>
        public AlchemyProductionUnit(Race race)
        {
            int resourceCost = race.HasTrait("MA") ? 25 : 100;
            cost = new Resources(0, 0, 0, resourceCost);
            remainingCost = new Resources(cost);
        }

        /// <summary>A unit with the given price and progress - the one-unit manual entry that
        /// carries an auto entry's leftover resources.</summary>
        internal AlchemyProductionUnit(Resources cost, Resources remainingCost)
        {
            this.cost = new Resources(cost);
            this.remainingCost = new Resources(remainingCost);
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
                switch (mainNode.Name.ToLowerInvariant())
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
        // mineral conversion, not a countable installation.
        public int? CurrentCount(Star star)
        {
            return null;
        }

        /// <summary>No population-scaled cap applies to Mineral Alchemy - see
        /// IProductionUnit.SupportableCount's own comment.</summary>
        public int? SupportableCount(Star star)
        {
            return null;
        }

        /// <summary>An auto Mineral Alchemy entry in LAST position "consumes all remaining
        /// resources": the original forces its quantity to 1,000 (behavior-specs-10/production-
        /// queue.md section 10a). ProductionOrder.Process only buys an auto Alchemy entry at all
        /// when it is last, so this limit applies there and nowhere else.</summary>
        public int? AutoBuildPerTurnLimit
        {
            get { return 1000; }
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
                remainingCost = new Resources(cost);
                return true;
            }
        }

        /// <summary>
        /// Moves this unit's partial progress (resources paid toward an unfinished unit) into a
        /// new unit and resets this one, or returns null if nothing was paid. An auto entry never
        /// holds progress itself: when it stops part-way through a unit, that progress becomes a
        /// one-unit MANUAL Mineral Alchemy entry at the top of the queue (production-queue.md
        /// section 8, status codes 5-7, and section 7).
        /// </summary>
        public AlchemyProductionUnit TakePartialProgress()
        {
            if (remainingCost == cost)
            {
                return null;
            }

            AlchemyProductionUnit partial = new AlchemyProductionUnit(cost, remainingCost);
            remainingCost = new Resources(cost);
            return partial;
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelUnit = xmldoc.CreateElement("AlchemyUnit");

            xmlelUnit.AppendChild(cost.ToXml(xmldoc, "Cost"));

            xmlelUnit.AppendChild(remainingCost.ToXml(xmldoc, "RemainingCost"));

            return xmlelUnit;
        }
    }

    /// <summary>
    /// The shortfall conversion an auto Mineral Alchemy entry that is NOT last in the queue arms
    /// for the entry right behind it (behavior-specs-10/production-queue.md section 7, "How the
    /// code does it"; FUN_10b8_0756). While armed, that entry is bought normally; when it is short
    /// and its scarcest component is a mineral, the remaining resources are converted at
    /// <see cref="ResourcesPerUnit"/> each - the smaller of what they pay for and the scarcest
    /// mineral's shortfall, each unit adding 1 kT of every mineral. If that covers the shortfall
    /// the purchase is retried; otherwise the leftover resources (less than one unit's price)
    /// become <see cref="Leftover"/>, a part-paid unit for a one-unit manual entry at the top of
    /// the queue.
    /// </summary>
    public sealed class AlchemyShortfallConversion
    {
        private readonly Resources unitCost;

        public AlchemyShortfallConversion(AlchemyProductionUnit alchemy)
        {
            unitCost = new Resources(alchemy.Cost);
        }

        /// <summary>Resources per alchemy unit: 100, or 25 with the Mineral Alchemy trait.</summary>
        public int ResourcesPerUnit
        {
            get { return unitCost.Energy; }
        }

        /// <summary>Alchemy units bought by the conversion so far - kT made of each mineral.</summary>
        public int UnitsConverted
        {
            get;
            private set;
        }

        /// <summary>The part-paid unit holding the leftover resources when the conversion could
        /// not cover a shortfall, or null.</summary>
        public AlchemyProductionUnit Leftover
        {
            get;
            private set;
        }

        /// <summary>
        /// Buys min(resources on hand / price, <paramref name="shortfall"/>) whole units, each
        /// adding 1 kT of every mineral, and returns how many were bought.
        /// </summary>
        public int Convert(Star star, int shortfall)
        {
            if (ResourcesPerUnit <= 0 || shortfall <= 0)
            {
                return 0;
            }

            int units = Math.Min(Math.Max(0, star.ResourcesOnHand.Energy) / ResourcesPerUnit, shortfall);
            if (units > 0)
            {
                star.ResourcesOnHand.Energy -= units * ResourcesPerUnit;
                star.ResourcesOnHand.Ironium += units;
                star.ResourcesOnHand.Boranium += units;
                star.ResourcesOnHand.Germanium += units;
                UnitsConverted += units;
            }

            return units;
        }

        /// <summary>
        /// Pays every remaining resource into a part-built alchemy unit (<see cref="Leftover"/>).
        /// Nothing is created when no resources are left.
        /// </summary>
        public void BankLeftover(Star star)
        {
            int leftover = Math.Min(Math.Max(0, star.ResourcesOnHand.Energy), ResourcesPerUnit);
            if (leftover <= 0)
            {
                return;
            }

            star.ResourcesOnHand.Energy -= leftover;
            Resources remaining = new Resources(unitCost);
            remaining.Energy -= leftover;
            Leftover = new AlchemyProductionUnit(unitCost, remaining);
        }
    }
}
