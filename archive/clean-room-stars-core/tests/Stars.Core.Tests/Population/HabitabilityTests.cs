using Stars.Core.Population;
using Xunit;

namespace Stars.Core.Tests.Population;

// Worked examples: docs/behavior-specs/population-growth.md
public class HabitabilityTests
{
    [Fact]
    public void HomeWorld_AtIdealCenterOnAllAxes_Is100Percent()
    {
        // Example 1: "a 100%-habitability homeworld" — g = t = r = 0 (exact center of tolerance).
        var result = Habitability.Calculate(
            AxisValue.AtDistance(0),
            AxisValue.AtDistance(0),
            AxisValue.AtDistance(0));

        Assert.Equal(1.0, result, precision: 9);
    }

    [Fact]
    public void FullImmunityOnAllAxes_Is100PercentRegardlessOfPlanetReading()
    {
        // race-traits.md Example A: full immunity on every axis => 100% on every planet,
        // regardless of the (here, arbitrary) underlying distance-from-center values.
        var result = Habitability.Calculate(
            AxisValue.Immune(),
            AxisValue.Immune(),
            AxisValue.Immune());

        Assert.Equal(1.0, result, precision: 9);
    }

    [Fact]
    public void SingleAxisImmunity_PinsThatAxisToIdealButOthersStillContribute()
    {
        var noImmunity = Habitability.Calculate(
            AxisValue.AtDistance(0.6),
            AxisValue.AtDistance(0.6),
            AxisValue.AtDistance(0.6));

        var oneImmune = Habitability.Calculate(
            AxisValue.Immune(),
            AxisValue.AtDistance(0.6),
            AxisValue.AtDistance(0.6));

        // race-traits.md §1: one immunity "roughly doubles" overall habitability across the
        // colonizable galaxy versus a no-immunity race with the same tolerances.
        Assert.True(oneImmune > noImmunity);
    }
}
