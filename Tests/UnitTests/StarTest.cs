#region Copyright Notice
// ============================================================================
// Copyright (C) 2011 The Stars-Nova Project
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
    using Nova.Common;
    using NUnit.Framework;

    // TODO Clean up unit test. Using magic numbers is not recommended.
    [TestFixture]
    public class StarTest
    {
        private Star star = new Star();
        private Race race = new Race();
        private double habitalValue;

        [SetUp]
        public void Init()
        {
            // Recreate fresh each test rather than mutating shared instances — several tests
            // (e.g. the *PopGrowth ones) set overlapping fields (Colonists, GrowthRate, PRT,
            // environment axes) and previously relied on running in a specific alphabetical
            // order to avoid leaking state between them, which modern NUnit doesn't guarantee.
            star = new Star();
            race = new Race();
        }

        /// <summary>
        /// Test population growth for a negative hab planet.
        /// </summary>
        [Test]
        public void NegativeHabPopGrowth()
        {
            // setup the star
            star.Colonists = 100000;
            star.Gravity = 10;
            star.Radiation = 10;
            star.Temperature = 10;

            // setup the race
            race.GrowthRate = 10; // 10% growth
            race.Traits.SetPrimary("SS"); // avoid the JoAT and HE complications

            // run the growth calculation
            int growth = star.CalculateGrowth(race);

            // Relies on the race's default (unset) environment tolerance, which
            // docs/behavior-specs-4/race-designer-ui-and-availability.md confirms should default
            // to a 20-80 band, not the previous (incorrect) 15-85 - a narrower default band means
            // more malus at the same out-of-range star value (10), hence larger population decline.
            Assert.AreEqual(-3000, growth);
        }

        // Tests for population growth
        [Test]
        public void LowPopGrowth()
        {
            // setup the star
            star.Colonists = 100000;
            star.Gravity = 50;
            star.Radiation = 50;
            star.Temperature = 50;

            // set a growth rate
            race.GrowthRate = 10; // 10% growth

            // run the growth calculation
            int growth = star.CalculateGrowth(race);

            // check the growth
            Assert.AreEqual(10000, growth);
        }

        [Test]
        public void CrowdingPopGrowth()
        {
            // setup the star
            star.Colonists = 500000;
            star.Gravity = 50;
            star.Radiation = 50;
            star.Temperature = 50;

            // setup the race
            race.GrowthRate = 10; // 10% growth
            race.Traits.SetPrimary("SS"); // avoid the JoAT and HE complications

            // run the growth calculation
            int growth = star.CalculateGrowth(race);

            // check the growth
            Assert.AreEqual(22200, growth);
        }

        [Test]
        public void MaxPopGrowth()
        {
            // setup the star
            star.Colonists = 1000000;
            star.Gravity = 50;
            star.Radiation = 50;
            star.Temperature = 50;

            // setup the race
            race.GrowthRate = 10; // 10% growth
            race.Traits.SetPrimary("SS"); // avoid the JoAT and HE complications

            // run the growth calculation
            int growth = star.CalculateGrowth(race);

            // check the growth
            Assert.AreEqual(0, growth);
        }

        [Test]
        public void OvercrowdedPopGrowth()
        {
            // setup the star
            star.Colonists = 1500000;
            star.Gravity = 50;
            star.Radiation = 50;
            star.Temperature = 50;

            // setup the race
            race.GrowthRate = 10; // 10% growth
            race.Traits.SetPrimary("SS"); // avoid the JoAT and HE complications

            // run the growth calculation
            int growth = star.CalculateGrowth(race);

            // docs/behavior-specs-4/population-growth.md Example 3: population plateaus at
            // capacity rather than declining further past it - no source was found for a
            // population-loss mechanic purely from exceeding capacity on an otherwise-positive
            // -habitability world, unlike the previously-asserted -30000 here.
            Assert.AreEqual(0, growth);
        }

        [Test]
        public void VeryOvercrowdedPopGrowth()
        {
            // setup the star
            star.Colonists = 5000000;
            star.Gravity = 50;
            star.Radiation = 50;
            star.Temperature = 50;

            // setup the race
            race.GrowthRate = 10; // 10% growth
            race.Traits.SetPrimary("SS"); // avoid the JoAT and HE complications

            // run the growth calculation
            int growth = star.CalculateGrowth(race);

            // docs/behavior-specs-4/population-growth.md Example 3: population plateaus at
            // capacity rather than declining further past it - see OvercrowdedPopGrowth above.
            Assert.AreEqual(0, growth);
        }

        [Test]
        public void HabitalValue_AllImmune()
        {
            star.Radiation = 100;
            star.Gravity = 1;
            star.Temperature = 17;
            race.RadiationTolerance.Immune = true;
            race.TemperatureTolerance.Immune = true;
            race.GravityTolerance.Immune = true;
            habitalValue = race.HabValue(star);
            Assert.AreEqual(1.0, habitalValue);
        }

        [Test]
        public void HabitalValue_100()
        {
            star.Radiation = 90;
            star.Gravity = 1;
            star.Temperature = 17;
            race.RadiationTolerance.MaximumValue = 91;
            race.RadiationTolerance.MinimumValue = 89;
            race.TemperatureTolerance.MaximumValue = 18;
            race.TemperatureTolerance.MinimumValue = 16;
            race.GravityTolerance.MaximumValue = 2;
            race.GravityTolerance.MinimumValue = 0;
            habitalValue = race.HabValue(star);
            Assert.AreEqual(1.0, habitalValue);
        }

        [Test]
        public void HabitalValue_n15()
        {
            star.Radiation = 100;
            star.Gravity = 1;
            star.Temperature = 17;
            race.RadiationTolerance.MaximumValue = 3;
            race.RadiationTolerance.MinimumValue = 1;
            race.TemperatureTolerance.MaximumValue = 18;
            race.TemperatureTolerance.MinimumValue = 16;
            race.GravityTolerance.MaximumValue = 2;
            race.GravityTolerance.MinimumValue = 0;
            habitalValue = race.HabValue(star);
            Assert.AreEqual(-0.15, habitalValue);
        }

        [Test]
        public void HabitalValue_n45()
        {
            star.Radiation = 100;
            star.Gravity = 1;
            star.Temperature = 17;
            race.RadiationTolerance.MaximumValue = 53;
            race.RadiationTolerance.MinimumValue = 51;
            race.TemperatureTolerance.MaximumValue = 58;
            race.TemperatureTolerance.MinimumValue = 56;
            race.GravityTolerance.MaximumValue = 60;
            race.GravityTolerance.MinimumValue = 50;
            habitalValue = race.HabValue(star);
            Assert.AreEqual(-0.45, habitalValue);
        }

        [Test]
        public void HabitalValue_n90()
        {
            star.Radiation = 1;
            star.Gravity = 1;
            star.Temperature = 1;
            race.Traits.Add("TT");
            race.RadiationTolerance.MaximumValue = 70;
            race.RadiationTolerance.MinimumValue = 60;
            race.TemperatureTolerance.MaximumValue = 70;
            race.TemperatureTolerance.MinimumValue = 60;
            race.GravityTolerance.MaximumValue = 70;
            race.GravityTolerance.MinimumValue = 60;
            habitalValue = race.HabValue(star);
            // docs/behavior-specs-4/population-growth.md confirms the single-axis habitability
            // malus is capped at a hard, unconditional 15, not doubled to 30 for Total
            // Terraforming (that doubling is a real, separate mechanic - TT's max TERRAFORM
            // STEPS - that Race.GetMaxMalus previously, incorrectly, also applied here). Halving
            // the malus cap halves this deeply-out-of-range result.
            Assert.AreEqual(-0.45, habitalValue);
        }

        [Test]
        public void HabitalValue_n10()
        {
            star.Radiation = 50;
            star.Gravity = 10;
            star.Temperature = 17;
            race.RadiationTolerance.MaximumValue = 45;
            race.RadiationTolerance.MinimumValue = 43;
            race.TemperatureTolerance.MaximumValue = 18;
            race.TemperatureTolerance.MinimumValue = 16;
            race.GravityTolerance.MaximumValue = 5;
            race.GravityTolerance.MinimumValue = 0;
            habitalValue = race.HabValue(star);
            Assert.AreEqual(-0.10, habitalValue);
        }
    }
}
