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

namespace Nova.Common
{
    using System;
    using System.Xml;

    using Nova.Common.Components;

    /// <summary>
    /// One planetary scanner (behavior-specs-11/production-queue.md section 10, item type 27).
    /// The "Planetary Scanner" catalog entry is a one-unit MANUAL item, offered only while the
    /// planet has no scanner and the race is not Alternate Reality (section 10l item 7); it is
    /// priced as the first-tier scanner, subtype 0 (Viewer 50): base 100 resources plus
    /// 10 / 10 / 70 kT of Ironium / Boranium / Germanium (section 10 table, type 27).
    /// Completing it installs the <b>best scanner the owner has</b> (section 10d,
    /// <c>FUN_1008_58de</c>, the best component of category <c>0x8000</c> the owner may build), not
    /// necessarily the subtype-0 price that was paid.
    /// </summary>
    /// <remarks>
    /// The installation itself is applied by the turn's Manufacture step when an order of this unit
    /// completes (<see cref="Install"/>), because it needs the owner's available components. A
    /// research breakthrough that unlocks a better scanner continues to upgrade every already
    /// installed scanner automatically (section 10d, message 343); it never installs a first scanner
    /// on a planet that has none (StarUpdateStep).
    /// </remarks>
    public class ScannerProductionUnit : IProductionUnit
    {
        /// <summary>The type-27 price, the Viewer 50's own cost (section 10 table).</summary>
        public const int IroniumCost = 10;

        /// <inheritdoc cref="IroniumCost"/>
        public const int BoraniumCost = 10;

        /// <inheritdoc cref="IroniumCost"/>
        public const int GermaniumCost = 70;

        /// <inheritdoc cref="IroniumCost"/>
        public const int EnergyCost = 100;

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
            get { return "Planetary Scanner"; }
        }

        public ScannerProductionUnit()
        {
            cost = new Resources(IroniumCost, BoraniumCost, GermaniumCost, EnergyCost);
            remainingCost = new Resources(cost);
        }

        /// <summary>Race is accepted for symmetry with the other units; the price is fixed.</summary>
        public ScannerProductionUnit(Race race) : this()
        {
        }

        /// <summary>
        /// Load: Read in a ProductionUnit from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionUnit</param>
        public ScannerProductionUnit(XmlNode node)
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

        /// <summary>True when the planet has an installed planetary scanner already. The scanner
        /// field's "none" value is stored as the string "None" (Star.ScannerType).</summary>
        public static bool HasScanner(Star star)
        {
            return star != null
                && !string.IsNullOrEmpty(star.ScannerType)
                && !string.Equals(star.ScannerType, "None", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The best planetary scanner the owner may build (section 10d: the search runs from the
        /// top subtype down and takes the first one the owner may build), or null when the owner has
        /// none in its available components.
        /// </summary>
        public static Component BestScanner(EmpireData empire)
        {
            if (empire == null || empire.AvailableComponents == null)
            {
                return null;
            }

            Component best = null;
            int bestRange = -1;
            foreach (Component component in empire.AvailableComponents.Values)
            {
                if (component == null || component.Type != ItemType.PlanetaryInstallations)
                {
                    continue;
                }

                if (!component.Properties.TryGetValue("Scanner", out ComponentProperty property) || property is not Scanner scanner)
                {
                    continue;
                }

                if (scanner.NormalScan > bestRange)
                {
                    bestRange = scanner.NormalScan;
                    best = component;
                }
            }

            return best;
        }

        /// <summary>Installs <paramref name="component"/> as the planet's scanner, applying No
        /// Advanced Scanners' doubling of the normal range (section 10d).</summary>
        public static void Install(Star star, Component component, Race race)
        {
            if (star == null || component == null)
            {
                return;
            }

            star.ScannerType = component.Name;
            if (component.Properties.TryGetValue("Scanner", out ComponentProperty property) && property is Scanner scanner)
            {
                star.ScanRange = race != null && race.HasTrait("NAS") ? scanner.NormalScan * 2 : scanner.NormalScan;
            }
        }

        /// <summary>Skipped (and, for a manual order, deleted by the room clamp) once the planet
        /// already has a scanner, or when there is nothing to pay with.</summary>
        public bool IsSkipped(Star star)
        {
            if (HasScanner(star))
            {
                return true;
            }

            if (star.ResourcesOnHand.Energy <= 0)
            {
                return true;
            }

            return (IroniumCost > 0 && star.ResourcesOnHand.Ironium <= 0)
                || (BoraniumCost > 0 && star.ResourcesOnHand.Boranium <= 0)
                || (GermaniumCost > 0 && star.ResourcesOnHand.Germanium <= 0);
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

        /// <summary>A manual scanner order on a planet that already has a scanner is cancelled
        /// (message 185); otherwise it is not limited.</summary>
        public int? RoomForManualOrder(Star star)
        {
            return HasScanner(star) ? 0 : (int?)null;
        }

        /// <summary>
        /// Pay toward the scanner. A turn that cannot afford the whole unit banks partial progress.
        /// The server's Manufacture step installs the best scanner when the unit completes.
        /// </summary>
        public bool Construct(Star star)
        {
            if (!(star.ResourcesOnHand >= remainingCost))
            {
                Resources lacking = remainingCost - star.ResourcesOnHand;

                double percentBuildable = 1.0;
                if (percentBuildable > (1 - ((double)lacking.Ironium / remainingCost.Ironium)) && lacking.Ironium > 0)
                {
                    percentBuildable = 1 - ((double)lacking.Ironium / remainingCost.Ironium);
                }

                if (percentBuildable > (1 - ((double)lacking.Boranium / remainingCost.Boranium)) && lacking.Boranium > 0)
                {
                    percentBuildable = 1 - ((double)lacking.Boranium / remainingCost.Boranium);
                }

                if (percentBuildable > (1 - ((double)lacking.Germanium / remainingCost.Germanium)) && lacking.Germanium > 0)
                {
                    percentBuildable = 1 - ((double)lacking.Germanium / remainingCost.Germanium);
                }

                if (percentBuildable > (1 - ((double)lacking.Energy / remainingCost.Energy)) && lacking.Energy > 0)
                {
                    percentBuildable = 1 - ((double)lacking.Energy / remainingCost.Energy);
                }

                Resources partialPayment = Resources.PartialPayment(remainingCost, percentBuildable, star.ResourcesOnHand);
                star.ResourcesOnHand -= partialPayment;
                remainingCost -= partialPayment;
                return false;
            }

            star.ResourcesOnHand -= remainingCost;
            remainingCost = new Resources(cost);
            return true;
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelUnit = xmldoc.CreateElement("ScannerUnit");

            xmlelUnit.AppendChild(cost.ToXml(xmldoc, "Cost"));
            xmlelUnit.AppendChild(remainingCost.ToXml(xmldoc, "RemainingCost"));

            return xmlelUnit;
        }
    }
}
