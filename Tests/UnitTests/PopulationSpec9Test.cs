namespace Nova.Tests.UnitTests
{
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;

    // behavior-specs-9/population-growth.md corrections: the over-capacity dead band is 10 population
    // UNITS (1,000 colonists), not 10 colonists; and concentrations above 100 (comets, 100-299
    // homeworlds) mine at their raw value but deplete as if they were 100.
    [TestFixture]
    public class PopulationSpec9Test
    {
        private static Star FullyHabitableStar(out Race race)
        {
            race = new Race();
            race.Traits.SetPrimary("SS");
            race.GrowthRate = 15;
            Star star = new Star { Name = "Home", Owner = 1, ThisRace = race };
            star.Gravity = race.GravityTolerance.OptimumLevel;
            star.Temperature = race.TemperatureTolerance.OptimumLevel;
            star.Radiation = race.RadiationTolerance.OptimumLevel;
            return star;
        }

        [Test]
        public void OverCapacity_UpTo999Colonists_StillProducesZeroGrowth()
        {
            Star star = FullyHabitableStar(out Race race);
            int capacity = race.MaxPopulation;

            star.Colonists = capacity + 999;
            Assert.AreEqual(0, star.CalculateGrowth(race));

            star.Colonists = capacity + 500;
            Assert.AreEqual(0, star.CalculateGrowth(race));
        }

        [Test]
        public void OverCapacity_FromAThousandColonistsOver_Declines()
        {
            Star star = FullyHabitableStar(out Race race);

            star.Colonists = race.MaxPopulation + 1001;

            Assert.Less(star.CalculateGrowth(race), 0);
        }

        private static int KtToDropOnePoint(int concentration, int rate)
        {
            return (int)typeof(Star).GetMethod("KtToDropOnePoint", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { concentration, rate });
        }

        [TestCase(100, 125)]
        [TestCase(101, 125)]
        [TestCase(200, 125)]
        [TestCase(299, 125)]
        [TestCase(50, 250)]
        [TestCase(25, 500)]
        public void Depletion_TreatsConcentrationsAbove100AsOneHundred(int concentration, int expectedKtPerPoint)
        {
            Assert.AreEqual(expectedKtPerPoint, KtToDropOnePoint(concentration, 10));
        }
    }
}
