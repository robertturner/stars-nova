namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;

    // behavior-specs-9/population-growth.md section 3: Alternate Reality's maximum population is not
    // derived from habitability at all - it is fixed by the starbase chassis orbiting the planet, from
    // a table in units of 100 colonists: Orbital Fort 250,000, Space Dock 500,000, Space Station
    // 1,000,000, Ultra Station 2,000,000, Death Star 3,000,000 colonists (live-confirmed: an AR
    // homeworld on a Space Station grew 25,000 -> 57,700). A planet with no starbase (status bit 0x2)
    // has capacity 0. The +10% of lesser-trait bit 9 (Only Basic Remote Mining) applies last.
    // (spec-8's 2,500...30,000 figures were 100x too low and its "Orbital Fort fallback" for a planet
    // without a starbase is replaced by the Starter Colony starbase installed on colonisation.)
    [TestFixture]
    public class AlternateRealityCapacityTest
    {
        private Race race;
        private Star star;

        [SetUp]
        public void Init()
        {
            race = new Race();
            race.Traits.SetPrimary("AR");

            star = new Star { Name = "Orbit", Owner = 1, ThisRace = race };
            star.Gravity = race.GravityTolerance.OptimumLevel;
            star.Temperature = race.TemperatureTolerance.OptimumLevel;
            star.Radiation = race.RadiationTolerance.OptimumLevel;
        }

        private void GiveStarbase(string chassisName)
        {
            Component blueprint = new Component { Name = chassisName, Mass = 100 };
            Hull hull = new Hull { FuelCapacity = 0, DockCapacity = 100 };
            hull.Modules = new List<HullModule>();
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint, Name = chassisName };
            design.Update();

            Fleet starbase = new Fleet(1) { Owner = 1 };
            starbase.Composition.Add(design.Key, new ShipToken(design, 1));
            star.Starbase = starbase;
        }

        /// <summary>Capacity as a percentage of the colonists: colonists / capacity * 100, rounded up.</summary>
        private int CapacityPercent(int colonists)
        {
            star.Colonists = colonists;
            return star.Capacity(race);
        }

        [TestCase("Orbital Fort", 250000)]
        [TestCase("Space Dock", 500000)]
        [TestCase("Space Station", 1000000)]
        [TestCase("Ultra Station", 2000000)]
        [TestCase("Death Star", 3000000)]
        public void MaximumPopulation_IsTheChassisTableValue_InUnitsOf100Colonists(string chassis, int expectedMaximum)
        {
            GiveStarbase(chassis);

            Assert.AreEqual(50, CapacityPercent(expectedMaximum / 2), "half the chassis maximum is 50% full");
            Assert.AreEqual(100, CapacityPercent(expectedMaximum));
        }

        [Test]
        public void AStartingAlternateRealityHomeworld_OnASpaceStation_HasRoomToGrow()
        {
            // Regression: with the table at 10,000 a 25,000-colonist homeworld sat at 250% and shrank
            // from turn 1; the live game grew it 25,000 -> 57,700.
            GiveStarbase("Space Station");
            race.GrowthRate = 15;
            star.Colonists = 25000;

            Assert.Less(star.Capacity(race), 100);
            Assert.Greater(star.CalculateGrowth(race), 0);
        }

        [Test]
        public void MaximumPopulation_IgnoresHabitability()
        {
            GiveStarbase("Space Station");
            star.Radiation = 99;
            star.Gravity = 99;
            star.Temperature = 99;

            Assert.AreEqual(50, CapacityPercent(500000), "Still 1,000,000 regardless of the environment");
        }

        [Test]
        public void ABiggerStarbase_RaisesTheCeiling()
        {
            GiveStarbase("Space Dock");
            int withDock = CapacityPercent(500000);

            GiveStarbase("Death Star");
            int withDeathStar = CapacityPercent(500000);

            Assert.AreEqual(100, withDock);
            Assert.Less(withDeathStar, withDock);
        }

        [Test]
        public void NoStarbase_MeansCapacityZero_NotAFallback()
        {
            Assert.AreEqual(100, CapacityPercent(2500), "a populated planet with no starbase supports nobody");
            Assert.AreEqual(0, CapacityPercent(0));
        }

        [Test]
        public void AnAlternateRealityPlanetWithNoStarbase_DeclinesAtTheHardClampedRate()
        {
            star.Colonists = 50000;

            // Capacity 0 is infinitely over-full: growth is the hard-clamped -12% a year.
            Assert.AreEqual(-6000, star.CalculateGrowth(race));
        }

        [Test]
        public void OnlyBasicRemoteMining_AddsTenPercentToTheTableValue()
        {
            race.Traits.Add("OBRM");
            GiveStarbase("Space Station");

            Assert.AreEqual(100, CapacityPercent(1100000));
            Assert.AreEqual(50, CapacityPercent(550000));
        }

        [Test]
        public void NonAlternateRealityRaces_AreUnaffected()
        {
            Race other = new Race();
            star.ThisRace = other;
            GiveStarbase("Orbital Fort");
            star.Gravity = other.GravityTolerance.OptimumLevel;
            star.Temperature = other.TemperatureTolerance.OptimumLevel;
            star.Radiation = other.RadiationTolerance.OptimumLevel;
            star.Colonists = 250000;

            Assert.Less(star.Capacity(other), 100, "An ordinary race on a good world is not capped by its starbase chassis");
        }

        [Test]
        public void AnAlternateRealityPlanet_MinesInnately_SquareRootOfPopulationInHundreds()
        {
            // Live test (spec-9): 24 effective mines at 57,700 colonists - sqrt(577) = 24.02.
            // AR cannot build or operate mines, but its planets still mine.
            GiveStarbase("Space Station");
            star.Colonists = 57700;

            Assert.AreEqual(24, star.GetMinesInUse());
            Assert.AreEqual(0, star.GetOperableMines(), "AR still operates none of the mines it cannot build");
        }

        [Test]
        public void InnateMining_HasNoMinimum_UnderOneHundredColonistsMineNothing()
        {
            // behavior-specs-10/race-traits.md, "Alternate Reality innate mining, exact":
            // floor(sqrt(population / 100)) with no minimum (rewritten from the superseded
            // spec-9 "minimum 1 for a populated planet" reading).
            GiveStarbase("Space Station");
            star.Colonists = 100;
            Assert.AreEqual(1, star.GetMinesInUse());

            star.Colonists = 99;
            Assert.AreEqual(0, star.GetMinesInUse());
        }
    }
}
