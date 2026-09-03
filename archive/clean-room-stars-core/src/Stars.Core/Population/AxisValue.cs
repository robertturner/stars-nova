namespace Stars.Core.Population;

/// <summary>
/// One environment axis (Gravity, Temperature, or Radiation) as scored for a specific race and
/// planet: either immune (pinned to the race's ideal point regardless of the planet's actual
/// reading) or a normalized distance from the race's ideal center toward the edge of its
/// tolerance band, where 0 is the exact center and 1 is the outer edge of what the race can
/// tolerate at all.
/// </summary>
/// <remarks>
/// Source: docs/behavior-specs/population-growth.md §1-2. The mapping from a planet's raw
/// Gravity/Temperature/Radiation reading to this normalized distance is not documented by any
/// source found for that spec (see its Open Questions) and is therefore not modeled here — this
/// type takes the already-normalized distance as an input.
/// </remarks>
public readonly record struct AxisValue
{
    public double NormalizedDistanceFromCenter { get; }
    public bool IsImmune { get; }

    private AxisValue(double normalizedDistanceFromCenter, bool isImmune)
    {
        NormalizedDistanceFromCenter = normalizedDistanceFromCenter;
        IsImmune = isImmune;
    }

    public static AxisValue Immune() => new(0, true);

    public static AxisValue AtDistance(double normalizedDistanceFromCenter) =>
        new(normalizedDistanceFromCenter, false);

    /// <summary>The value actually fed into the habitability formula: 0 when immune.</summary>
    internal double Effective => IsImmune ? 0 : NormalizedDistanceFromCenter;
}
