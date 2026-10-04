namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // behavior-specs-8/new-game-setup.md's option-flag table: Accelerated BBS Play multiplies each
    // home planet's starting population by (growth rate % + 5) x 2 / 10 - 4x at the default 15%
    // growth (which is where the previous flat 100,000 came from), 3x at 10%, 5x at 20%.
    [TestFixture]
    public class AcceleratedStartPopulationTest
    {
        private bool originalAcceleratedStart;

        [SetUp]
        public void Init()
        {
            originalAcceleratedStart = GameSettings.Data.AcceleratedStart;
        }

        [TearDown]
        public void Cleanup()
        {
            GameSettings.Data.AcceleratedStart = originalAcceleratedStart;
        }

        [Test]
        public void NormalStart_IsTheFlatStartingColonists_RegardlessOfGrowthRate()
        {
            GameSettings.Data.AcceleratedStart = false;
            Race race = new Race { GrowthRate = 20 };

            Assert.AreEqual(Global.StartingColonists, race.GetStartingPopulation());
        }

        [Test]
        public void AcceleratedStart_AtFifteenPercentGrowth_IsFourTimesTheNormalStart()
        {
            GameSettings.Data.AcceleratedStart = true;
            Race race = new Race { GrowthRate = 15 };

            Assert.AreEqual(100000, race.GetStartingPopulation());
        }

        [Test]
        public void AcceleratedStart_ScalesWithTheRacesGrowthRate()
        {
            GameSettings.Data.AcceleratedStart = true;

            Assert.AreEqual(75000, new Race { GrowthRate = 10 }.GetStartingPopulation(), "(10 + 5) x 2 / 10 = 3x");
            Assert.AreEqual(125000, new Race { GrowthRate = 20 }.GetStartingPopulation(), "(20 + 5) x 2 / 10 = 5x");
        }

        [Test]
        public void AcceleratedStart_StillAppliesLowStartingPopulationAfterwards()
        {
            GameSettings.Data.AcceleratedStart = true;
            Race race = new Race { GrowthRate = 15 };
            race.Traits.Add("LSP");

            Assert.AreEqual((int)(100000 * Global.LowStartingPopulationFactor), race.GetStartingPopulation());
        }
    }
}
