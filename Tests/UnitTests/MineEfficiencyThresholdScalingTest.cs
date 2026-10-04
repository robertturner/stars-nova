namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // Regression test for behavior-specs-7/population-growth.md row 24: "Mine efficiency above 1.0
    // (a racial customization) scales these thresholds up proportionally... extracting more kT per
    // point of concentration drop, not more points per kT." Star.KtToDropOnePoint previously ignored
    // Race.MineProductionRate entirely, so a higher-efficiency race's mines (which already produce
    // more kT per turn via GetMiningRate) also depleted concentration FASTER - the opposite of the
    // documented principle, which says efficiency should buy more minerals per point lost, not a
    // faster-draining planet. MineProductionRate 10 is the baseline 1.0x ("10 mines produce 10 kT
    // at 100% concentration" - see DefaultRaces/*.race, all of which use 9-10).
    [TestFixture]
    public class MineEfficiencyThresholdScalingTest
    {
        [Test]
        public void DoubleEfficiency_ScalesTheDepletionThresholdProportionally_NotJustTheOutput()
        {
            // At concentration 25, the baseline (10, i.e. 1.0x) threshold is 1250*10/25 = 500 kT
            // per point. A 2.0x-efficient race (MineProductionRate 20) must need exactly 2x that
            // (1000 kT) to drop the same point.
            int concentration = 25;
            int progress = 0;

            Star.MineForFleet(mineEquivalents: 3996, ref concentration, ref progress, mineProductionRate: 20); // 3996*25/100 = 999
            Assert.AreEqual(25, concentration, "999 kT is below the 2.0x-scaled 1000 kT threshold");
            Assert.AreEqual(999, progress);

            Star.MineForFleet(mineEquivalents: 4, ref concentration, ref progress, mineProductionRate: 20); // +1 -> 1000 total
            Assert.AreEqual(24, concentration);
            Assert.AreEqual(0, progress);
        }

        [Test]
        public void BaselineMineProductionRate_ThresholdIsUnaffectedByThisFix()
        {
            int concentration = 25;
            int progress = 0;

            Star.MineForFleet(mineEquivalents: 1996, ref concentration, ref progress, mineProductionRate: 10); // 1996*25/100 = 499
            Assert.AreEqual(25, concentration);
            Assert.AreEqual(499, progress);

            Star.MineForFleet(mineEquivalents: 4, ref concentration, ref progress, mineProductionRate: 10); // +1 -> 500 total
            Assert.AreEqual(24, concentration);
            Assert.AreEqual(0, progress);
        }

        [Test]
        public void DoublingEfficiency_HalvesPointsDroppedForTheSameRawKtMined_NetSameDepletionRateAsBaseline()
        {
            // The flip side of "more kT per point, not more points per kT": mining the exact same
            // raw kT (not the proportionally-higher output a 2x race would actually produce) against
            // a 2x-efficient threshold must drop half as many concentration points as against the
            // 1.0x baseline threshold - the depletion RATE (points per unit of real mining effort)
            // stays put; only the kT-per-point cost changes.
            int concentration1x = 25;
            int progress1x = 0;
            int concentration2x = 25;
            int progress2x = 0;

            Star.MineForFleet(mineEquivalents: 4000, ref concentration1x, ref progress1x, mineProductionRate: 10); // 1000 kT
            Star.MineForFleet(mineEquivalents: 4000, ref concentration2x, ref progress2x, mineProductionRate: 20); // 1000 kT

            Assert.AreEqual(23, concentration1x, "1000 kT at the 1.0x/500-per-point threshold drops 2 points");
            Assert.AreEqual(24, concentration2x, "the same 1000 kT at the 2.0x/1000-per-point threshold drops only 1 point");
        }
    }
}
