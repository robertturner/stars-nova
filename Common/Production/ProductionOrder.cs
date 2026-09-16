#region Copyright Notice
// ============================================================================
// Copyright (C) 2010, 2011 stars-nova
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
    using System.Diagnostics;
    using System.Globalization;
    using System.Xml;

    using Nova.Common.Components;

    /// <summary>
    /// Details of a design in the queue.
    /// </summary>
    [Serializable]
    public class ProductionOrder 
    {        
        // Number to build
        public int Quantity
        {
            set;
            get;
        }
        
        public bool IsAutoBuild
        {
            private set;
            get;
        }
        
        public IProductionUnit Unit
        {
            private set;
            get;
        }
        
        public string Name
        {
            get {return Unit.Name;}
        }

        
        /// <summary>
        /// initializing constructor.
        /// </summary>
        /// <param name="quantity">The number of items to produce.</param>
        /// <param name="design">The <see cref="Design"/> to build.</param>
        public ProductionOrder(int quantity, IProductionUnit productionUnit, bool isAutoBuild)
        {
            Quantity = quantity;
            Unit = productionUnit;
            IsAutoBuild = isAutoBuild;            
        }

        
        /// <summary>
        /// Return the resources needed to complete this whole order.
        /// </summary>
        /// <returns></returns>
        public Resources NeededResources()
        {
            Resources neededResources = new Resources();
            
            if (Unit.RemainingCost != Unit.Cost)
            {
                neededResources = Unit.RemainingCost + (Unit.Cost * (Quantity - 1));
            }
            else
            {
                neededResources = Unit.Cost * Quantity;
            }
            
            return neededResources;
        }
        
        
        public bool IsBlocking(Star star)
        {
            return (Unit.IsSkipped(star) && !IsAutoBuild);
        }
        
        
        /// <summary>
        /// Processes this order.
        /// </summary>
        /// <param name="star">The star where this unit is processed</param>
        /// <returns>The number of units completed</returns>
        /// <remarks>
        /// For an auto-build order whose Unit has a persistent, countable planetary stat
        /// (Factories/Mines/Defenses - see IProductionUnit.CurrentCount), Quantity is treated
        /// as a standing target to maintain rather than a one-off batch to consume: this year's
        /// shortfall is computed fresh from the star's live count each time, and Quantity itself
        /// is never decremented - so the order is never exhausted/removed (Manufacture.Items
        /// only removes an order once Quantity reaches 0) and automatically resumes building if
        /// the count later drops (e.g. bombing destroys some factories). This matches the
        /// manual's "Factories (Auto Build) Up to 10" template phrasing (docs/behavior-specs-4/
        /// production-queue.md §9) - before this fix, ANY order (auto-build or not) simply
        /// decremented Quantity to 0 and was deleted from the queue once its batch completed,
        /// silently discarding the "maintain this many" intent auto-build is meant to express.
        /// Every other order (a manual order of any kind, or an auto-build order for a Ship/
        /// Alchemy/Terraform unit, none of which have such a count to check against) keeps the
        /// original, unchanged consume-Quantity-to-zero behavior.
        /// </remarks>
        public int Process(Star star)
        {
            int done = 0;

            int? currentCount = IsAutoBuild ? Unit.CurrentCount(star) : null;
            if (currentCount.HasValue)
            {
                int remaining = Math.Max(0, Quantity - currentCount.Value);
                while (remaining > 0)
                {
                    if (Unit.IsSkipped(star))
                    {
                        break;
                    }

                    if (Unit.Construct(star))
                    {
                        remaining--;
                        done++;
                    }
                }

                return done;
            }

            while (Quantity > 0)
            {
                if (Unit.IsSkipped(star))
                {
                    break;
                }

                if (Unit.Construct(star))
                {
                    Quantity--;
                    done++;
                }
            }

            return done;
        }
        
        /// <summary>
        /// Load: Read in a ProductionQueue.Item from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionQueue.Item.</param>
        public ProductionOrder(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLower())
                    {
                        case "quantity":
                            Quantity = int.Parse(subnode.FirstChild.Value, CultureInfo.InvariantCulture);
                        break;
                            
                        case "isautobuild":
                            IsAutoBuild = bool.Parse(subnode.FirstChild.Value);
                        break;
                        
                        case "factoryunit":
                            Unit = new FactoryProductionUnit(subnode);
                        break;

                        case "mineunit":
                        Unit = new MineProductionUnit(subnode);
                        break;

                        case "defenseunit":
                        Unit = new DefenseProductionUnit(subnode);
                        break;

                        case "shipunit":
                            Unit = new ShipProductionUnit(subnode);
                        break;

                        case "alchemyunit":
                            Unit = new AlchemyProductionUnit(subnode);
                        break;

                        case "terraformunit":
                            Unit = new TerraformProductionUnit(subnode);
                        break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error(e.Message);
                }
                subnode = subnode.NextSibling;
            }
        }

        
        /// <summary>
        /// Save: Generate an XmlElement representation of the ProductionQueue.Item for saving.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representation of the ProductionQueue.Item.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelProductionOrder = xmldoc.CreateElement("ProductionOrder");
            
            Global.SaveData(xmldoc, xmlelProductionOrder, "Quantity", Quantity.ToString(CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelProductionOrder, "IsAutoBuild", IsAutoBuild.ToString(CultureInfo.InvariantCulture));
            xmlelProductionOrder.AppendChild(Unit.ToXml(xmldoc));

            return xmlelProductionOrder;
        }
    }
}