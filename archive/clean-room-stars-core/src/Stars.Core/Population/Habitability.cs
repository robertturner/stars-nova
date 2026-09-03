namespace Stars.Core.Population;

/// <summary>
/// Combines a planet's three environment axis scores into an overall habitability percentage.
/// </summary>
/// <remarks>
/// Source: docs/behavior-specs/population-growth.md §2 (credited to Bill Butler / Jason Cawley,
/// starsfaq.com "Guts of Planet Values" §4.11). The source explicitly labels this an
/// approximation that loses accuracy as axis values move away from center, and gives no formula
/// for axis values outside the race's tolerance band (g, t, or r &gt; 1, i.e. "red planet"
/// territory) — that case is an open question in the spec and is deliberately left unhandled
/// here rather than guessed at: the formula below is applied as-is for any input.
/// </remarks>
public static class Habitability
{
    /// <summary>Overall habitability, as a fraction (1.0 = 100%).</summary>
    public static double Calculate(AxisValue gravity, AxisValue temperature, AxisValue radiation)
    {
        double g = gravity.Effective;
        double t = temperature.Effective;
        double r = radiation.Effective;

        double x = Math.Max(0, g - 0.5);
        double y = Math.Max(0, t - 0.5);
        double z = Math.Max(0, r - 0.5);

        double closeness = Math.Sqrt(
            (1 - g) * (1 - g) +
            (1 - t) * (1 - t) +
            (1 - r) * (1 - r)) / Math.Sqrt(3);

        return closeness * (1 - x) * (1 - y) * (1 - z);
    }
}
