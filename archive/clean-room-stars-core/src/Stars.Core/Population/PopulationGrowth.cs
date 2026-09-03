namespace Stars.Core.Population;

/// <summary>
/// Per-planet, per-turn population growth: a racial growth rate modified by habitability and,
/// above 25% of capacity, by crowding.
/// </summary>
/// <remarks>
/// Source: docs/behavior-specs/population-growth.md §3
/// (starsfaq.com "Guts of Population Growth" §4.9, credited to Jason Cawley / Bill Butler),
/// cross-checked against §3's effective-growth-rate table.
///
/// Rounding/truncation of the result to whole colonists is left to the caller: the spec's
/// Example 1 shows a small gap between the idealized formula and in-game figures, consistent
/// with per-turn integer truncation, but does not confirm the exact rounding rule (see the
/// spec's Open Questions).
/// </remarks>
public static class PopulationGrowth
{
    private const double CrowdingThreshold = 0.25;

    /// <summary>
    /// The population increase for one turn. Add this to <paramref name="population"/> to get
    /// next turn's population (subject to the caller's own rounding rule).
    /// </summary>
    /// <param name="population">Current population.</param>
    /// <param name="maxPopulation">This planet's population capacity for the race.</param>
    /// <param name="growthRate">Racial base growth rate, as a fraction (e.g. 0.15 for 15%).</param>
    /// <param name="habitabilityValue">This planet's habitability for the race, as a fraction.</param>
    public static double CalculateGrowth(double population, double maxPopulation, double growthRate, double habitabilityValue)
    {
        if (maxPopulation <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPopulation), "A planet must have positive capacity to grow population on.");

        double capPct = population / maxPopulation;
        double baseGrowth = population * growthRate * habitabilityValue;

        if (capPct <= CrowdingThreshold)
            return baseGrowth;

        double crowdingFactor = (16.0 / 9.0) * (1 - capPct) * (1 - capPct);
        return baseGrowth * crowdingFactor;
    }
}
