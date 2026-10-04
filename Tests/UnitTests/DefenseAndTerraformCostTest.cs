#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
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

namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // behavior-specs-8/production-queue.md sections 5-6 (segment-24 sweep) CORRECTS the previous
    // revision: the "25/44/48 resources" Defenses and "70/110/120" Terraforming figures were Mineral
    // Packet kilotonnages read off the wrong case group of the cost calculator. The real items:
    //  - Defenses cost the SDI component's own record: 15 resources + 5 kT each of Ironium,
    //    Boranium and Germanium, fixed whatever defense technology the race has; Inner Strength
    //    multiplies all four fields by 3 and integer-divides by 5 (9 resources + 3 kT each).
    //  - Terraforming costs 100 resources per 1% step (70 with Total Terraforming), no minerals,
    //    halved for Claim Adjuster by a one-bit right shift.
    [TestFixture]
    public class DefenseAndTerraformCostTest
    {
        [Test]
        public void Defense_Default_Costs15ResourcesPlus5OfEachMineral()
        {
            DefenseProductionUnit unit = new DefenseProductionUnit(new Race());

            Assert.AreEqual(15, unit.Cost.Energy);
            Assert.AreEqual(5, unit.Cost.Ironium);
            Assert.AreEqual(5, unit.Cost.Boranium);
            Assert.AreEqual(5, unit.Cost.Germanium);
        }

        [Test]
        public void Defense_PacketPhysicsAndInterstellarTraveler_PayTheSamePriceAsEveryoneElse()
        {
            // The old 25/48 figures were the mixed Mineral Packet's; Defenses have no PRT-keyed
            // branches at all.
            foreach (string prt in new[] { "PP", "IT" })
            {
                Race race = new Race();
                race.Traits.SetPrimary(prt);
                DefenseProductionUnit unit = new DefenseProductionUnit(race);

                Assert.AreEqual(15, unit.Cost.Energy, prt);
                Assert.AreEqual(5, unit.Cost.Germanium, prt);
            }
        }

        [Test]
        public void Defense_InnerStrength_PaysThreeFifthsOfEveryField_WithIntegerDivision()
        {
            Race race = new Race();
            race.Traits.SetPrimary("IS");
            DefenseProductionUnit unit = new DefenseProductionUnit(race);

            Assert.AreEqual(9, unit.Cost.Energy, "15 * 3 / 5");
            Assert.AreEqual(3, unit.Cost.Ironium, "5 * 3 / 5");
            Assert.AreEqual(3, unit.Cost.Boranium);
            Assert.AreEqual(3, unit.Cost.Germanium);
        }

        [Test]
        public void Defense_IsSkippedWhenAMineralIsMissing_AsItNeedsAllThree()
        {
            Star star = new Star();
            star.ThisRace = new Race();
            star.ResourcesOnHand = new Resources(10, 10, 0, 100);
            DefenseProductionUnit unit = new DefenseProductionUnit(star.ThisRace);

            Assert.IsTrue(unit.IsSkipped(star), "No Germanium on hand - the old resources-only assumption no longer holds");

            star.ResourcesOnHand = new Resources(10, 10, 10, 100);
            Assert.IsFalse(unit.IsSkipped(star));
        }

        [Test]
        public void Terraform_Default_Costs100ResourcesPerPercent()
        {
            TerraformProductionUnit unit = new TerraformProductionUnit(new Race());

            Assert.AreEqual(100, unit.Cost.Energy);
            Assert.AreEqual(0, unit.Cost.Ironium + unit.Cost.Boranium + unit.Cost.Germanium, "Terraforming costs no minerals");
        }

        [Test]
        public void Terraform_PacketPhysicsAndInterstellarTraveler_PayTheSamePriceAsEveryoneElse()
        {
            foreach (string prt in new[] { "PP", "IT" })
            {
                Race race = new Race();
                race.Traits.SetPrimary(prt);

                Assert.AreEqual(100, new TerraformProductionUnit(race).Cost.Energy, prt);
            }
        }

        [Test]
        public void Terraform_TotalTerraforming_Costs70()
        {
            Race race = new Race();
            race.Traits.Add("TT");

            Assert.AreEqual(70, new TerraformProductionUnit(race).Cost.Energy);
        }

        [Test]
        public void Terraform_ClaimAdjuster_PaysHalf()
        {
            Race race = new Race();
            race.Traits.SetPrimary("CA");

            Assert.AreEqual(50, new TerraformProductionUnit(race).Cost.Energy, "100 >> 1");
        }

        [Test]
        public void Terraform_ClaimAdjusterWithTotalTerraforming_HalvesTheDiscountedPrice()
        {
            Race race = new Race();
            race.Traits.SetPrimary("CA");
            race.Traits.Add("TT");

            Assert.AreEqual(35, new TerraformProductionUnit(race).Cost.Energy, "70 >> 1");
        }
    }
}
