namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using Nova.Ai;

    using NUnit.Framework;

    /// <summary>
    /// The efficient warp `FUN_1050_69c2` (docs/behavior-specs-10/ai-opponent-behavior.md §12,
    /// "Speeds are set at the end of the pass", step 1). Fuel tables are indexed warp - 1, as
    /// Nova's Engine.FuelConsumption is (components.xml's Warp0..Warp9 are warps 1-10).
    /// </summary>
    [TestFixture]
    public class AiEfficientWarpTest
    {
        // Fuel Mizer (components.xml): free to warp 4, 35% at 5, 120% at 6.
        private static readonly int[] FuelMizer = { 0, 0, 0, 0, 35, 120, 175, 235, 360, 420 };

        // An engine within the 120% limit at every warp.
        private static readonly int[] Cheap = { 10, 10, 10, 10, 10, 10, 10, 10, 10, 10 };

        // An engine that is free at every warp.
        private static readonly int[] AllFree = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        private static int Warp(bool preferFree, params (int[], string)[] stacks)
        {
            return EfficientWarp.Compute(new List<(int[], string)>(stacks), preferFree);
        }

        [Test]
        public void StepsDownUntilTheFuelFigureIsAt120OrLess()
        {
            Assert.AreEqual(6, Warp(false, (FuelMizer, "Fuel Mizer")), "warp 6 costs exactly 120");
        }

        [Test]
        public void FreeSpeedPreference_FuelMizerGivesFourNotSix()
        {
            // The spec's own example: w = 6 costs fuel, 5 is not free, 4 is -> drop by 2.
            Assert.AreEqual(4, Warp(true, (FuelMizer, "Fuel Mizer")));
        }

        [Test]
        public void FreeSpeedPreference_IsSkippedForTheTwoScoops()
        {
            Assert.AreEqual(6, Warp(true, (FuelMizer, "Galaxy Scoop")));
            Assert.AreEqual(6, Warp(true, (FuelMizer, "Trans-Galactic Mizer Scoop")));
        }

        [Test]
        public void ExactlyTenBecomesNine_ExceptForTheWarpTenEngines()
        {
            Assert.AreEqual(9, Warp(false, (Cheap, "Alpha Drive 8")));
            Assert.AreEqual(10, Warp(false, (Cheap, "Interspace-10")));
            Assert.AreEqual(10, Warp(false, (Cheap, "Enigma Pulsar")));
            Assert.AreEqual(10, Warp(false, (Cheap, "Trans-Star 10")));
            Assert.AreEqual(10, Warp(false, (Cheap, "Trans-Galactic Mizer Scoop")));
            Assert.AreEqual(10, Warp(false, (Cheap, "Galaxy Scoop")));
        }

        [Test]
        public void AFreeWarpNeedsNoPreferenceStep()
        {
            Assert.AreEqual(9, Warp(true, (AllFree, "Settler's Delight")), "free at 10, then capped to 9");
        }

        [Test]
        public void TheResultIsTheMinimumOverEveryDesign()
        {
            Assert.AreEqual(6, Warp(false, (Cheap, "Interspace-10"), (FuelMizer, "Fuel Mizer")));
            Assert.AreEqual(6, Warp(false, (FuelMizer, "Fuel Mizer"), (Cheap, "Interspace-10")), "a later design can never raise it");
        }

        [Test]
        public void ADesignWithNoEngineMakesItZero()
        {
            Assert.AreEqual(0, Warp(false, (Cheap, "Alpha Drive 8"), (null, null)));
        }
    }
}
