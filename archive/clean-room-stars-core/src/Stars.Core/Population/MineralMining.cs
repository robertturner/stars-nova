namespace Stars.Core.Population;

/// <summary>
/// Mineral concentration depletion and per-turn mining yield.
/// </summary>
/// <remarks>
/// Source: docs/behavior-specs/population-growth.md §5 (starsfaq.com "Mineral Concentration And
/// Mining" by Jason Cawley; Stars!wiki "Remote Mining"). The per-turn yield formula and the
/// discrete depletion curve are both flagged in the spec as high-confidence syntheses rather
/// than verbatim-sourced formulas — see the spec's Open Questions.
/// </remarks>
public static class MineralMining
{
    public const int MaxConcentration = 100;
    public const int MinConcentration = 1;

    /// <summary>A single fleet's remote-mining contribution is capped at this many mine-equivalents.</summary>
    public const int RemoteMiningFleetCap = 4000;

    /// <summary>
    /// kT of a mineral that must be extracted before concentration drops from
    /// <paramref name="concentration"/> to concentration-1, at 1.0 mine efficiency. Higher mine
    /// efficiency scales this threshold up proportionally (more kT per point of concentration
    /// drop), which callers can apply by dividing this result by their efficiency multiplier.
    /// </summary>
    public static double KtToDropOnePoint(int concentration)
    {
        if (concentration <= MinConcentration)
            throw new ArgumentOutOfRangeException(nameof(concentration), "Concentration never drops below 1.");
        if (concentration > MaxConcentration)
            throw new ArgumentOutOfRangeException(nameof(concentration));

        return concentration switch
        {
            2 => 2000,
            3 => 1000,
            4 => 1000,
            >= 5 and <= 26 => 462,
            _ => 12500.0 / concentration, // concentration >= 27
        };
    }

    /// <summary>kT mined by one mining source's application this turn.</summary>
    /// <param name="mineEquivalents">Mine-equivalents operating (already folds in mine-efficiency/value setting).</param>
    /// <param name="concentration">Concentration at the moment this source mines, 0-100.</param>
    public static double MinedThisApplication(double mineEquivalents, double concentration) =>
        mineEquivalents * concentration / 100.0;

    /// <summary>
    /// Steps concentration down one whole point at a time, consuming <see cref="KtToDropOnePoint"/>
    /// kT per point, until the mined kT is exhausted or concentration bottoms out at 1. Partial
    /// progress toward the next point is discarded rather than carried to the next mining
    /// application — the spec does not document a carryover rule for this (see Example 4).
    /// </summary>
    public static int DepleteDiscrete(int startConcentration, double ktMined)
    {
        int concentration = startConcentration;
        double remaining = ktMined;
        while (remaining > 0 && concentration > MinConcentration)
        {
            double cost = KtToDropOnePoint(concentration);
            if (remaining < cost)
                break;
            remaining -= cost;
            concentration--;
        }
        return concentration;
    }

    /// <summary>
    /// Continuous approximation of depletion, valid for concentration &gt;= 27:
    /// <c>Cend = Cstart * exp(-ktMined / 12500)</c>. The spec notes the true discrete result runs
    /// slightly higher than this estimate, more so at high mining rates.
    /// </summary>
    public static double DepleteContinuous(double startConcentration, double ktMined) =>
        startConcentration * Math.Exp(-ktMined / 12500.0);
}
