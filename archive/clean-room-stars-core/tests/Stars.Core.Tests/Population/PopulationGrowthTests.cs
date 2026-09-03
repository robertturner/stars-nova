using Xunit;
using PopGrowth = Stars.Core.Population.PopulationGrowth;

namespace Stars.Core.Tests.Population;

// Worked examples: docs/behavior-specs/population-growth.md
public class PopulationGrowthTests
{
    [Fact]
    public void Example1_HomeworldUnderCrowdingThreshold_GrowsAtFullRate()
    {
        // "Applying the formula directly to the first step: 25000 * 0.15 * 1.00 = 3750" —
        // the spec notes the documented in-game figure (3,700) differs slightly, attributed to
        // per-turn rounding/truncation this idealized formula doesn't model.
        var growth = PopGrowth.CalculateGrowth(population: 25_000, maxPopulation: 1_000_000, growthRate: 0.15, habitabilityValue: 1.00);

        Assert.Equal(3750, growth, precision: 6);
    }

    [Fact]
    public void Example2_MarginalPlanet_GrowsProportionallyToHabitability()
    {
        var growth = PopGrowth.CalculateGrowth(population: 10_000, maxPopulation: 450_000, growthRate: 0.15, habitabilityValue: 0.45);

        Assert.Equal(675, growth, precision: 6);
    }

    [Fact]
    public void Example2_Contrast_SameStartingPopulationOnHomeworldGrowsFaster()
    {
        var homeworldGrowth = PopGrowth.CalculateGrowth(population: 10_000, maxPopulation: 1_000_000, growthRate: 0.15, habitabilityValue: 1.00);

        Assert.Equal(1500, homeworldGrowth, precision: 6);
    }

    [Fact]
    public void Example3_OvercrowdedHomeworldAt70Percent_AppliesCrowdingFactor()
    {
        // capPct = 0.70 -> crowdingFactor = (16/9)*0.09 = 0.16 -> matches the sourced table's
        // "70% / 700,000 ... 1.6% / 11,200" entry (10% racial rate * 16% crowding = 1.6% effective).
        var growth = PopGrowth.CalculateGrowth(population: 700_000, maxPopulation: 1_000_000, growthRate: 0.10, habitabilityValue: 1.00);

        Assert.Equal(11200, growth, precision: 6);
    }

    [Fact]
    public void Example3_AtFullCapacity_GrowthPlateausToZero()
    {
        var growth = PopGrowth.CalculateGrowth(population: 1_000_000, maxPopulation: 1_000_000, growthRate: 0.10, habitabilityValue: 1.00);

        Assert.Equal(0, growth, precision: 6);
    }

    [Theory]
    [InlineData(0.10, 100.0)]
    [InlineData(0.20, 100.0)]
    [InlineData(0.30, 87.1)]
    [InlineData(0.40, 64.0)]
    [InlineData(0.50, 44.4)]
    [InlineData(0.60, 28.4)]
    [InlineData(0.70, 16.0)]
    [InlineData(0.80, 7.1)]
    [InlineData(0.90, 1.8)]
    [InlineData(1.00, 0.0)]
    public void CrowdingFactorTable_MatchesDocumentedEffectiveRatePercentages(double capPct, double expectedPercentOfMax)
    {
        // §3's documented table of the effective growth-rate multiplier (% of racial max) at
        // each capacity level, for a 100%-habitability world. Below 25% capacity there's no
        // crowding, so the table starts genuinely testing the crowding branch at 30%+.
        double population = capPct * 1_000_000;
        double growth = PopGrowth.CalculateGrowth(population, maxPopulation: 1_000_000, growthRate: 1.0, habitabilityValue: 1.0);

        double actualPercentOfMax = growth / population * 100.0;

        Assert.Equal(expectedPercentOfMax, actualPercentOfMax, precision: 1);
    }
}
