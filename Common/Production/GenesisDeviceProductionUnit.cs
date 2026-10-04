#region Copyright Notice
// ============================================================================
// Copyright (C) 2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
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

    using Nova.Common.Components;

    /// <summary>
    /// One Genesis Device: a one-shot manual production item costing 5,000 resources that resets
    /// the planet it completes on (<see cref="Star.ApplyGenesisDevice"/>). This is what the earlier
    /// specs called the "Planet Rebirth" event; behavior-specs-8/production-queue.md section 10c
    /// settles it as an ordinary catalog item (item type 13), not a random disaster.
    /// </summary>
    /// <remarks>
    /// The planetary reset itself is applied by the turn's Manufacture step when an order of this
    /// unit completes (it needs the game's random source and the message list, neither of which a
    /// Star carries). If several Genesis Devices complete in the same turn the effect runs once per
    /// completed order, not once per device.
    /// </remarks>
    public class GenesisDeviceProductionUnit : IProductionUnit
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
            get { return "Genesis Device"; }
        }

        /// <summary>
        /// A device at its unadjusted catalog price (5,000 resources) - for callers with no race
        /// or tech levels to hand. Prefer <see cref="GenesisDeviceProductionUnit(EmpireData)"/>.
        /// </summary>
        public GenesisDeviceProductionUnit()
        {
            cost = new Resources(0, 0, 0, Global.GenesisDeviceResourceCost);
            remainingCost = new Resources(cost);
        }

        /// <summary>
        /// A device priced for <paramref name="race"/> at <paramref name="currentTechLevels"/>: the
        /// Genesis Device component's own cost, miniaturized and (for Bleeding Edge Technology)
        /// doubled by the shared item-cost routine like any other part. behavior-specs-10/
        /// race-traits.md section 7 (FUN_1050_7d44): the miniaturization exclusion covers
        /// planetary category 0x8000 subtypes 0-13 only, so the Genesis Device (subtype 14) IS
        /// discounted; production-queue.md section 10 (type 13): "Component-cost lookup, category
        /// 0x8000 subtype 14". A null component falls back to 5,000 resources with no tech
        /// requirement (no miniaturization).
        /// </summary>
        public GenesisDeviceProductionUnit(Component genesisDevice, Race race, TechLevel currentTechLevels)
        {
            Resources baseCost = genesisDevice != null && genesisDevice.Cost != null
                ? genesisDevice.Cost
                : new Resources(0, 0, 0, Global.GenesisDeviceResourceCost);

            cost = genesisDevice != null && genesisDevice.RequiredTech != null && race != null && currentTechLevels != null
                ? ShipDesign.ApplyMiniaturizationAndBleedingEdge(baseCost, genesisDevice.RequiredTech, race, currentTechLevels)
                : new Resources(baseCost);
            remainingCost = new Resources(cost);
        }

        /// <summary>
        /// A device priced for <paramref name="empire"/>'s race and current research levels, using
        /// its own "Genesis Device" component record when it has one.
        /// </summary>
        public GenesisDeviceProductionUnit(EmpireData empire)
            : this(FindGenesisDevice(empire), empire == null ? null : empire.Race, empire == null ? null : empire.ResearchLevels)
        {
        }

        private static Component FindGenesisDevice(EmpireData empire)
        {
            if (empire == null || empire.AvailableComponents == null)
            {
                return null;
            }

            return empire.AvailableComponents.TryGetValue("Genesis Device", out Component component) ? component : null;
        }

        /// <summary>
        /// Load: Read in a ProductionUnit from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionUnit</param>
        public GenesisDeviceProductionUnit(XmlNode node)
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

        /// <summary>Skipped (and, for a manual order, blocking) when there are no resources.</summary>
        public bool IsSkipped(Star star)
        {
            return star.ResourcesOnHand.Energy <= 0;
        }

        // A one-shot item with no persistent count to compare against.
        public int? CurrentCount(Star star)
        {
            return null;
        }

        public int? SupportableCount(Star star)
        {
            return null;
        }

        /// <summary>There is no auto-build Genesis Device in the original.</summary>
        public bool AutoBuildIsStandingOrder
        {
            get { return false; }
        }

        /// <summary>
        /// Pay toward the device. Like every other unit, a turn that cannot afford the whole
        /// 5,000 resources banks partial progress instead of wasting them.
        /// </summary>
        public bool Construct(Star star)
        {
            if (star.ResourcesOnHand.Energy < remainingCost.Energy)
            {
                remainingCost.Energy -= star.ResourcesOnHand.Energy;
                star.ResourcesOnHand.Energy = 0;
                return false;
            }

            star.ResourcesOnHand.Energy -= remainingCost.Energy;

            // A copy: Construct's partial payment mutates remainingCost in place, which must
            // never reach the unit's Cost.
            remainingCost = new Resources(cost);
            return true;
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelUnit = xmldoc.CreateElement("GenesisDeviceUnit");

            xmlelUnit.AppendChild(cost.ToXml(xmldoc, "Cost"));

            xmlelUnit.AppendChild(remainingCost.ToXml(xmldoc, "RemainingCost"));

            return xmlelUnit;
        }
    }
}
