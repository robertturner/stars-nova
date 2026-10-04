namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // Regression test for behavior-specs-7/population-growth.md's decompiled replacement of the
    // mineral-concentration depletion curve: the community-sourced curve this codebase previously
    // used (12500/concentration for concentration >= 27, a flat 462 for 5-26, then an irregular
    // 1000/1000/2000 tail) is superseded by a clean two-breakpoint stair-step traced directly
    // from the exported client: threshold = 12500 / effectiveConcentration, where
    // effectiveConcentration is the raw concentration for concentration >= 25, clamped to 25 for
    // concentration 5-24, and clamped to 10 below 5.
    //
    // Every case here mines an exact amount (chosen so Star.MineForFleet's internal
    // mineEquivalents*concentration/100 division lands on a whole number, avoiding stochastic
    // rounding) that sits ABOVE the old, now-wrong threshold but still BELOW the real, corrected
    // one - proving the old code would have already dropped concentration by this point, and the
    // fixed code correctly hasn't yet - then mines enough further to cross the real threshold and
    // confirms exactly one point drops, landing on the expected leftover progress.
    [TestFixture]
    public class ConcentrationDepletionCurveTest
    {
        [Test]
        public void Concentration26_ThresholdIs480_NotTheOldFlat462()
        {
            int concentration = 26;
            int progress = 0;

            Star.MineForFleet(1800, ref concentration, ref progress, mineProductionRate: 10); // mined = 1800*26/100 = 468
            Assert.AreEqual(26, concentration, "468 kT is above the old flat 462 threshold but below the real 480 (12500/26) one.");
            Assert.AreEqual(468, progress);

            Star.MineForFleet(100, ref concentration, ref progress, mineProductionRate: 10); // mined = 100*26/100 = 26 more -> 494 total
            Assert.AreEqual(25, concentration);
            Assert.AreEqual(14, progress); // 494 - 480
        }

        [Test]
        public void Concentration25_ThresholdIs500_NotTheOldFlat462()
        {
            int concentration = 25;
            int progress = 0;

            Star.MineForFleet(1920, ref concentration, ref progress, mineProductionRate: 10); // mined = 1920*25/100 = 480
            Assert.AreEqual(25, concentration, "480 kT is above the old flat 462 threshold but below the real 500 (12500/25) one.");
            Assert.AreEqual(480, progress);

            Star.MineForFleet(100, ref concentration, ref progress, mineProductionRate: 10); // +25 -> 505 total
            Assert.AreEqual(24, concentration);
            Assert.AreEqual(5, progress); // 505 - 500
        }

        [Test]
        public void Concentration24_ClampsToEffective25_NotTheOldFlat462()
        {
            int concentration = 24;
            int progress = 0;

            Star.MineForFleet(2000, ref concentration, ref progress, mineProductionRate: 10); // mined = 2000*24/100 = 480
            Assert.AreEqual(24, concentration, "480 kT is above the old flat 462 threshold but below the real 500 (12500/25, clamped) one.");
            Assert.AreEqual(480, progress);

            Star.MineForFleet(100, ref concentration, ref progress, mineProductionRate: 10); // +24 -> 504 total
            Assert.AreEqual(23, concentration);
            Assert.AreEqual(4, progress); // 504 - 500
        }

        [Test]
        public void Concentration5_StillClampsToEffective25()
        {
            int concentration = 5;
            int progress = 0;

            Star.MineForFleet(9600, ref concentration, ref progress, mineProductionRate: 10); // mined = 9600*5/100 = 480
            Assert.AreEqual(5, concentration, "480 kT is above the old flat 462 threshold but below the real 500 (12500/25, clamped) one.");
            Assert.AreEqual(480, progress);

            Star.MineForFleet(400, ref concentration, ref progress, mineProductionRate: 10); // mined = 400*5/100 = 20 more -> 500 total
            Assert.AreEqual(4, concentration);
            Assert.AreEqual(0, progress); // 500 - 500
        }

        [Test]
        public void Concentration4_ClampsToEffective10_NotTheOldFlat1000()
        {
            int concentration = 4;
            int progress = 0;

            Star.MineForFleet(27500, ref concentration, ref progress, mineProductionRate: 10); // mined = 27500*4/100 = 1100
            Assert.AreEqual(4, concentration, "1100 kT is above the old flat 1000 threshold but below the real 1250 (12500/10, clamped) one.");
            Assert.AreEqual(1100, progress);

            Star.MineForFleet(3800, ref concentration, ref progress, mineProductionRate: 10); // mined = 3800*4/100 = 152 more -> 1252 total
            Assert.AreEqual(3, concentration);
            Assert.AreEqual(2, progress); // 1252 - 1250
        }
    }
}
