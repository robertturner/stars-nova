#region Copyright Notice
// ============================================================================
// Copyright (C) 2010 stars-nova
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
    /// "Constructs" a 1% terraform, automatically picking whichever environment factor is
    /// furthest from the race's ideal, up to the race's maximum total terraform per factor
    /// (15%, or 30% with Total Terraforming). See docs/behavior-specs/production-queue.md §6.
    /// </summary>
    /// <remarks>
    /// This is a simplified model: the real game's maximum terraform amount is gated by
    /// researched terraforming tech level (each tech level raising the cap), which is not
    /// modeled here — only the flat 15%/30% ceiling described in the spec.
    /// </remarks>
    public class TerraformProductionUnit : IProductionUnit
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
            get { return "Terraform"; }
        }

        /// <summary>
        /// initializing constructor.
        /// </summary>
        /// <param name="race">Race performing the terraforming (Total Terraforming lowers the cost).</param>
        public TerraformProductionUnit(Race race)
        {
            int resourceCost = race.HasTrait("TT") ? 70 : 100;
            cost = new Resources(0, 0, 0, resourceCost);
            remainingCost = cost;
        }

        /// <summary>
        /// Load: Read in a ProductionUnit from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionUnit</param>
        public TerraformProductionUnit(XmlNode node)
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

        private static int MaxTerraformPercent(Race race)
        {
            return race.HasTrait("TT") ? 30 : 15;
        }

        /// <summary>
        /// The environment axis furthest from the race's ideal that still has terraforming
        /// headroom left, or null if every axis is already ideal or fully terraformed.
        /// </summary>
        private static string SelectAxisToImprove(Star star, Race race)
        {
            int maxPercent = MaxTerraformPercent(race);

            string best = null;
            int bestDistance = 0;

            CheckAxis("Gravity", star.Gravity, star.OriginalGravity, race.GravityTolerance.OptimumLevel, maxPercent, ref best, ref bestDistance);
            CheckAxis("Temperature", star.Temperature, star.OriginalTemperature, race.TemperatureTolerance.OptimumLevel, maxPercent, ref best, ref bestDistance);
            CheckAxis("Radiation", star.Radiation, star.OriginalRadiation, race.RadiationTolerance.OptimumLevel, maxPercent, ref best, ref bestDistance);

            return best;
        }

        private static void CheckAxis(string axisName, int current, int original, int ideal, int maxPercent, ref string best, ref int bestDistance)
        {
            int distanceToIdeal = Math.Abs(current - ideal);
            int alreadyUsed = Math.Abs(current - original);

            if (distanceToIdeal > 0 && alreadyUsed < maxPercent && distanceToIdeal > bestDistance)
            {
                best = axisName;
                bestDistance = distanceToIdeal;
            }
        }

        private static void ImproveAxis(Star star, Race race, string axis)
        {
            switch (axis)
            {
                case "Gravity":
                    star.Gravity += Math.Sign(race.GravityTolerance.OptimumLevel - star.Gravity);
                    break;
                case "Temperature":
                    star.Temperature += Math.Sign(race.TemperatureTolerance.OptimumLevel - star.Temperature);
                    break;
                case "Radiation":
                    star.Radiation += Math.Sign(race.RadiationTolerance.OptimumLevel - star.Radiation);
                    break;
            }
        }

        /// <summary>
        /// Returns true if this production item is to be skipped this year (no resources, or
        /// every environment factor is already ideal or fully terraformed).
        /// </summary>
        public bool IsSkipped(Star star)
        {
            if (star.ResourcesOnHand.Energy <= 0)
            {
                return true;
            }

            return SelectAxisToImprove(star, star.ThisRace) == null;
        }

        // No single stable count to compare a target against - each 1% nudges whichever of
        // three separate environment axes needs it most (SelectAxisToImprove), unlike a single
        // running total the way Factories/Mines/Defenses have. An auto-build order for this
        // keeps its original consume-to-zero-then-remove behavior (ProductionOrder.Process).
        public int? CurrentCount(Star star)
        {
            return null;
        }

        /// <summary>
        /// Construct a 1% terraform on whichever axis needs it most.
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
                string axis = SelectAxisToImprove(star, star.ThisRace);
                if (axis != null)
                {
                    ImproveAxis(star, star.ThisRace, axis);
                }

                remainingCost = cost;
                return true;
            }
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelUnit = xmldoc.CreateElement("TerraformUnit");

            xmlelUnit.AppendChild(cost.ToXml(xmldoc, "Cost"));

            xmlelUnit.AppendChild(remainingCost.ToXml(xmldoc, "RemainingCost"));

            return xmlelUnit;
        }
    }
}
