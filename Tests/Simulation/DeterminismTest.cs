namespace Nova.Tests.Simulation
{
    using Nova.Sim;

    using NUnit.Framework;

    /// <summary>
    /// Two simulations with the same config must produce the same saved state, turn by turn:
    /// the game (map generation, every turn step, every AI) is meant to be a pure function of
    /// the game seed. A failure names the first turn whose state hash differs; the saved states
    /// of that turn can then be diffed (Nova.Sim run --out a / --out b, then compare the game
    /// folders, or the metrics with Nova.Sim compare). Reuses the golden seed 101's two runs.
    /// </summary>
    [TestFixture]
    [Category("Simulation")]
    [Category("Determinism")]
    public class DeterminismTest
    {
        [Test]
        public void TwoIdenticalRuns_ProduceIdenticalPerTurnStateHashes()
        {
            (SimulationResult first, SimulationResult second) = GoldenSnapshotTest.TwoRuns(101);
            Assert.IsNull(first.FatalError, first.FatalError);
            Assert.IsNull(second.FatalError, second.FatalError);
            Assert.AreEqual(first.Turns.Count, second.Turns.Count);

            string divergence = SimulationTestSupport.FirstHashDivergence(first, second);
            Assert.IsNull(divergence, "DETERMINISM INCOMPLETE: two runs of the same seed diverged at " + divergence);
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentGames()
        {
            SimulationConfig a = GoldenSnapshotTest.Config(101);
            a.Turns = 1;
            SimulationConfig b = GoldenSnapshotTest.Config(101);
            b.Seed = 102;
            b.Turns = 1;
            Assert.AreNotEqual(SimulationRunner.RunOnce(a).Turns[0].StateHash, SimulationRunner.RunOnce(b).Turns[0].StateHash);
        }
    }
}
