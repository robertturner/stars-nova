using Stars.Core.Population;
using Xunit;

namespace Stars.Core.Tests.Population;

// Worked examples: docs/behavior-specs/population-growth.md
public class MineralMiningTests
{
    [Fact]
    public void Example4_LowYieldMiningDoesNotDropConcentration()
    {
        // 100 standard mines (1.0 efficiency) at Germanium concentration 50.
        double minedThisTurn = MineralMining.MinedThisApplication(mineEquivalents: 100, concentration: 50);
        Assert.Equal(50, minedThisTurn, precision: 6);

        // 12500/50 = 250 kT needed to drop one point; 50 kT falls well short.
        Assert.Equal(250, MineralMining.KtToDropOnePoint(50), precision: 6);
        Assert.Equal(50, MineralMining.DepleteDiscrete(startConcentration: 50, ktMined: minedThisTurn));
    }

    [Fact]
    public void Example5_FiveMaxFleetsSequentially_MatchesDocumentedContinuousApproximation()
    {
        // Five fleets of the 4,000-mine-equivalent cap, mining a Germanium-100 planet in
        // sequence, via the continuous approximation Cend = Cstart * exp(-mined/12500).
        double concentration = 100;
        double[] expectedAfter = { 72.6, 57.6, 47.9, 41.1, 36.0 };

        for (int i = 0; i < 5; i++)
        {
            double mined = MineralMining.MinedThisApplication(MineralMining.RemoteMiningFleetCap, concentration);
            concentration = MineralMining.DepleteContinuous(concentration, mined);
            Assert.Equal(expectedAfter[i], concentration, precision: 1);
        }

        // Documented outcome for this exact scenario is concentration 34 (the true discrete
        // result runs a bit ahead of the continuous approximation); our continuous estimate
        // lands close but slightly above, exactly as the spec describes.
        Assert.True(concentration > 34);
    }

    [Theory]
    [InlineData(100, 125.0)]   // 12500/100
    [InlineData(27, 12500.0 / 27)]
    [InlineData(26, 462)]
    [InlineData(5, 462)]
    [InlineData(4, 1000)]
    [InlineData(3, 1000)]
    [InlineData(2, 2000)]
    public void KtToDropOnePoint_MatchesDocumentedDepletionCurve(int concentration, double expectedKt)
    {
        Assert.Equal(expectedKt, MineralMining.KtToDropOnePoint(concentration), precision: 6);
    }

    [Fact]
    public void ConcentrationNeverDropsBelowOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MineralMining.KtToDropOnePoint(1));
        Assert.Equal(1, MineralMining.DepleteDiscrete(startConcentration: 2, ktMined: 1_000_000));
    }
}
