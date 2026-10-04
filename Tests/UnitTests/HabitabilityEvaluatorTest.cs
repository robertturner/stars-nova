namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // The original's integer habitability evaluator FUN_1048_490e (behavior-specs-10/population-
    // growth.md section 2, race-traits.md section 1b): closeness = sum over axes of
    // (100 - floor(100d/h))^2 (10,000 for an immune axis); ideality starts at 10,000 and is scaled
    // by (3h - 2d)/(2h) for each in-band axis with 2d > h, h being the centre-to-edge distance on
    // the PLANET'S side; result floor(sqrt(closeness/3) + 0.9) x ideality / 10,000, or minus the
    // summed out-of-band penalties (each capped at 15). HabValue is that percent / 100.
    [TestFixture]
    public class HabitabilityEvaluatorTest
    {
        private Race race;
        private Star star;

        [SetUp]
        public void Init()
        {
            race = new Race();
            SetBand(race.GravityTolerance, 20, 80);
            SetBand(race.TemperatureTolerance, 20, 80);
            SetBand(race.RadiationTolerance, 20, 80);

            star = new Star();
            star.Gravity = 50;
            star.Temperature = 50;
            star.Radiation = 50;
        }

        private static void SetBand(EnvironmentTolerance tolerance, int min, int max)
        {
            tolerance.MinimumValue = min;
            tolerance.MaximumValue = max;
        }

        [Test]
        public void AtTheCentreOnEveryAxis_Is100()
        {
            Assert.AreEqual(100, race.HabPercent(star));
            Assert.AreEqual(1.0, race.HabValue(star));
        }

        [Test]
        public void HalfwayOnEveryAxis_Is50_WithNoEdgeFactorAtExactlyHalf()
        {
            // d = 15, h = 30: (100 - 50)^2 x 3 = 7,500; sqrt(2,500) + 0.9 -> 50; 2d = h, no factor.
            star.Gravity = 65;
            star.Temperature = 35;
            star.Radiation = 65;

            Assert.AreEqual(50, race.HabPercent(star));
            Assert.AreEqual(0.5, race.HabValue(star));
        }

        [Test]
        public void OneAxisAtTheEdge_Is41()
        {
            // closeness 0 + 10,000 + 10,000: floor(sqrt(6,666.7) + 0.9) = floor(82.55) = 82;
            // ideality 10,000 x (90 - 60) / 60 = 5,000; 82 x 5,000 / 10,000 = 41.
            star.Gravity = 80;

            Assert.AreEqual(41, race.HabPercent(star));
        }

        [Test]
        public void TheNormalizedDistanceIsTruncatedToAWholePercent_AndThePointNineOffsetApplies()
        {
            // d = 11, h = 30: floor(36.67) = 36 -> 64^2 = 4,096; total 24,096; sqrt(8,032) = 89.62,
            // + 0.9 -> 90. (The community floating-point formula gives 89.46.)
            star.Gravity = 61;

            Assert.AreEqual(90, race.HabPercent(star));
        }

        [Test]
        public void TheCentreToEdgeDistance_IsTakenOnThePlanetsSideOfTheCentre()
        {
            // Band 15-86: centre 15 + 71/2 = 50, so h is 35 below the centre and 36 above it. A
            // planet on the top edge (86) is in band at d = h = 36 - the same 41 as the bottom edge.
            SetBand(race.GravityTolerance, 15, 86);

            star.Gravity = 86;
            Assert.AreEqual(41, race.HabPercent(star), "Top edge, h = 36");

            star.Gravity = 15;
            Assert.AreEqual(41, race.HabPercent(star), "Bottom edge, h = 35");
        }

        [Test]
        public void AnImmuneAxis_CountsAsPerfectlyClose_AndDoesNotScaleIdeality()
        {
            // Gravity immune, the other two at their edge: closeness 10,000 -> floor(57.74 + 0.9)
            // = 58; ideality 10,000 x 1/2 x 1/2 = 2,500; 58 x 2,500 / 10,000 = 14.
            race.GravityTolerance.Immune = true;
            star.Gravity = 1;
            star.Temperature = 80;
            star.Radiation = 20;

            Assert.AreEqual(14, race.HabPercent(star));
        }

        [Test]
        public void EdgeFactorsCompoundInIntegerArithmetic()
        {
            // Two axes at d = 29 of h = 30: (100 - 96)^2 = 16 each, closeness 10,032 ->
            // floor(57.83 + 0.9) = 58; ideality 10,000 x 32 / 60 = 5,333, x 32 / 60 = 2,844;
            // 58 x 2,844 / 10,000 = 16.
            star.Gravity = 79;
            star.Temperature = 21;

            Assert.AreEqual(16, race.HabPercent(star));
        }

        [Test]
        public void OutOfBandPenalties_AddUpAcrossAxes()
        {
            star.Gravity = 90;     // 10 outside
            star.Temperature = 2;  // 18 outside, capped at 15

            Assert.AreEqual(-25, race.HabPercent(star));
            Assert.AreEqual(-0.25, race.HabValue(star));
        }

        [Test]
        public void DefenseCap_IsFourTimesTheIntegerPercent()
        {
            // 16% (see EdgeFactorsCompoundInIntegerArithmetic) -> 64 defenses.
            star.Gravity = 79;
            star.Temperature = 21;
            star.ThisRace = race;

            Assert.AreEqual(64, star.GetMaxDefenses());
        }
    }
}
