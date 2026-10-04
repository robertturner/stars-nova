namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // behavior-specs-8/production-queue.md section 3 (segment-24 sweep) corrects the operable-count
    // formula and documents the separate build cap:
    //  - operable = min(buildCap, max(1, floor(setting * population / 10000))) with the population
    //    taken in units of 100 colonists (so 25,000 colonists at the default 10 operate 25 factories,
    //    not 20) and the result never below 1;
    //  - build cap = max(10, setting * MAXIMUM population / 10000) - limited by the planet's maximum
    //    population, not its current one (1,000 at 100% habitability and the default setting);
    //  - defenses: cap = clamp(4 * habitability %, 10, 100), operable = one per 2,500 colonists
    //    (rounded up, at most the cap);
    //  - Alternate Reality gets 0 from every one of these routines.
    [TestFixture]
    public class BuildingCapsTest
    {
        private Race race;
        private Star star;

        [SetUp]
        public void Init()
        {
            race = new Race();
            race.OperableFactories = 10;
            race.OperableMines = 10;

            // Every axis on the default tolerance band's optimum -> 100% habitability.
            star = new Star();
            star.ThisRace = race;
            star.Gravity = race.GravityTolerance.OptimumLevel;
            star.Temperature = race.TemperatureTolerance.OptimumLevel;
            star.Radiation = race.RadiationTolerance.OptimumLevel;
        }

        [Test]
        public void Operable_ProductIsTakenBeforeFlooring_NotInWholeTenThousandBlocks()
        {
            star.Colonists = 25000;

            // floor(25,000 / 10,000) * 10 would be 20 - the spec's own worked example is 25.
            Assert.AreEqual(25, star.GetOperableFactories());
            Assert.AreEqual(25, star.GetOperableMines());
        }

        [Test]
        public void Operable_IsNeverBelowOne_ForAPopulatedPlanet()
        {
            star.Colonists = 300;

            Assert.AreEqual(1, star.GetOperableFactories(), "300 colonists is 0.3 of a block - still operates 1");
            Assert.AreEqual(1, star.GetOperableMines());
        }

        [Test]
        public void Operable_IsClampedToTheBuildCap()
        {
            // A hostile (non-positive habitability) world falls back to the flat 25,000-colonist
            // capacity, so its build cap is 25 at the default setting - far below what a huge
            // colonist count would otherwise operate.
            star.Radiation = race.RadiationTolerance.MaximumValue + 60;
            star.Gravity = race.GravityTolerance.MaximumValue + 60;
            star.Temperature = race.TemperatureTolerance.MaximumValue + 60;
            Assume.That(race.HabValue(star), Is.LessThanOrEqualTo(0));
            star.Colonists = 1000000;

            Assert.AreEqual(25, star.GetBuildCapFactories());
            Assert.AreEqual(25, star.GetOperableFactories());
        }

        [Test]
        public void BuildCap_IsLimitedByMaximumPopulationNotCurrent()
        {
            star.Colonists = 100;

            int expected = race.MaxPopulation * 10 / 10000;
            Assert.AreEqual(expected, star.GetBuildCapFactories(), "maximum population x 10 per 10,000 (1,000 for a 1,000,000 maximum)");
            Assert.AreEqual(expected, star.GetBuildCapMines());
        }

        [Test]
        public void BuildCap_ScalesWithHabitability()
        {
            // Push one axis well outside the band so habitability drops below 100%.
            star.Colonists = 100;
            star.Radiation = race.RadiationTolerance.MaximumValue + 5;
            double habitability = race.HabValue(star);
            Assume.That(habitability, Is.GreaterThan(0).And.LessThan(1), "Test setup: expected a partially habitable world");

            Assert.Less(star.GetBuildCapFactories(), race.MaxPopulation * 10 / 10000);
        }

        [Test]
        public void BuildCap_FloorsAtTen_ForAVerySmallSetting()
        {
            race.OperableMines = 1;
            star.Radiation = race.RadiationTolerance.MaximumValue + 60;
            star.Gravity = race.GravityTolerance.MaximumValue + 60;
            star.Temperature = race.TemperatureTolerance.MaximumValue + 60;
            Assume.That(race.HabValue(star), Is.LessThanOrEqualTo(0));

            // 25,000 fallback capacity * 1 per 10,000 = 2.5 -> floored up to 10.
            Assert.AreEqual(10, star.GetBuildCapMines());
        }

        [Test]
        public void FutureOperable_UsesNextYearsProjectedPopulation()
        {
            star.Colonists = 9900;
            int growth = star.CalculateGrowth(race);
            Assume.That(growth, Is.GreaterThan(0));

            int expected = (9900 + growth) / 100 * 10 / 100;
            Assert.AreEqual(expected, star.GetFutureOperableFactories());
        }

        [Test]
        public void MaxDefenses_IsFourTimesHabitabilityPercent_ClampedToTenAndAHundred()
        {
            // 100% habitable: 4 * 100 = 400 -> clamped to 100.
            Assert.AreEqual(100, star.GetMaxDefenses());
        }

        [Test]
        public void MaxDefenses_OfAPoorWorld_IsHeldToFourTimesItsValue_NeverBelowTen()
        {
            // A barely-positive habitability - well under 25% - gives four times its value.
            star.Radiation = race.RadiationTolerance.MaximumValue + 1;
            double habitability = race.HabValue(star);

            int expected = System.Math.Max(10, System.Math.Min(100, 4 * (int)(habitability * 100)));
            Assert.AreEqual(expected, star.GetMaxDefenses());
            Assert.GreaterOrEqual(star.GetMaxDefenses(), 10);
        }

        [Test]
        public void OperableDefenses_IsOnePer2500Colonists_RoundedUp()
        {
            star.Colonists = 25000;
            Assert.AreEqual(10, star.GetOperableDefenses());

            star.Colonists = 25100;
            Assert.AreEqual(11, star.GetOperableDefenses(), "rounded up");

            star.Colonists = 100;
            Assert.AreEqual(1, star.GetOperableDefenses());
        }

        [Test]
        public void OperableDefenses_NeverExceedTheDefenseCap()
        {
            star.Colonists = 1000000;

            Assert.AreEqual(star.GetMaxDefenses(), star.GetOperableDefenses());
        }

        [Test]
        public void AlternateReality_CannotOperateOrBuildMinesFactoriesOrDefenses()
        {
            race.Traits.SetPrimary("AR");
            star.Colonists = 100000;

            Assert.AreEqual(0, star.GetOperableFactories());
            Assert.AreEqual(0, star.GetOperableMines());
            Assert.AreEqual(0, star.GetBuildCapFactories());
            Assert.AreEqual(0, star.GetBuildCapMines());
            Assert.AreEqual(0, star.GetMaxDefenses());
            Assert.AreEqual(0, star.GetOperableDefenses());
        }

        [Test]
        public void UnownedStar_HasNoCaps()
        {
            Star unowned = new Star();

            Assert.AreEqual(0, unowned.GetOperableFactories());
            Assert.AreEqual(0, unowned.GetBuildCapMines());
            Assert.AreEqual(0, unowned.GetMaxDefenses());
        }
    }
}
