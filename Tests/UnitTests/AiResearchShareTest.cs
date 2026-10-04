namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Ai;
    using Nova.Common;

    /// <summary>
    /// The AI's research share p (ai-opponent-behavior.md §6, FUN_1090_2900): the per-personality
    /// and per-turn percentage of resource output dedicated to research before production.
    /// </summary>
    [TestFixture]
    public class AiResearchShareTest
    {
        private static TechLevel Levels(int level)
        {
            return new TechLevel(level);
        }

        [Test]
        public void PercentFor_PerPersonalityAndTurn_AndZeroAtMaxTech()
        {
            Assert.AreEqual(0, AiResearchShare.PercentFor(AiCategory.Robotoids, 9, Levels(0)));
            Assert.AreEqual(15, AiResearchShare.PercentFor(AiCategory.Robotoids, 10, Levels(0)));
            Assert.AreEqual(15, AiResearchShare.PercentFor(AiCategory.Turindrones, 0, Levels(0)));
            Assert.AreEqual(0, AiResearchShare.PercentFor(AiCategory.Automitrons, 9, Levels(0)));
            Assert.AreEqual(20, AiResearchShare.PercentFor(AiCategory.Automitrons, 10, Levels(0)));
            Assert.AreEqual(0, AiResearchShare.PercentFor(AiCategory.Rototills, 19, Levels(0)));
            Assert.AreEqual(15, AiResearchShare.PercentFor(AiCategory.Rototills, 20, Levels(0)));
            Assert.AreEqual(0, AiResearchShare.PercentFor(AiCategory.EconomyOnly, 19, Levels(0)));
            Assert.AreEqual(15, AiResearchShare.PercentFor(AiCategory.EconomyOnly, 20, Levels(0)));
            Assert.AreEqual(17, AiResearchShare.PercentFor(AiCategory.Cybertrons, 0, Levels(0)));
            Assert.AreEqual(15, AiResearchShare.PercentFor(AiCategory.Macinti, 0, Levels(0)));
            Assert.AreEqual(0, AiResearchShare.PercentFor(AiCategory.NoDriver, 50, Levels(0)));

            // p becomes 0 once every one of the six tech levels is 24 or more.
            Assert.AreEqual(0, AiResearchShare.PercentFor(AiCategory.Automitrons, 100, Levels(24)));
            Assert.AreEqual(20, AiResearchShare.PercentFor(AiCategory.Automitrons, 100, new TechLevel(23, 26, 26, 26, 26, 26)));
        }

        [Test]
        public void AfterShare_SubtractsTheTruncatedPercentage()
        {
            Assert.AreEqual(100, AiResearchShare.AfterShare(100, 0));
            Assert.AreEqual(85, AiResearchShare.AfterShare(100, 15));
            Assert.AreEqual(810, AiResearchShare.AfterShare(1000, 19));
            Assert.AreEqual(85, AiResearchShare.AfterShare(99, 15), "15 x 99 / 100 truncates to 14");
        }
    }
}
