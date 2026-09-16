using System;
using System.Collections.Generic;
using System.Linq;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One resource row in a fleet Cargo Transfer section (fleet-vs-planet or fleet-vs-fleet) - a
/// slider from 0 to <see cref="Total"/> (the conserved fleet+other total for this resource)
/// representing how much the FLEET should end up holding; <see cref="PlanetAmount"/> is just the
/// remainder ("PlanetAmount" is a historical name - for a fleet-vs-fleet transfer it means "the
/// other fleet's amount").
///
/// Docs/original-game parity fix: the original client's slider range was bounded by the actual
/// cargo capacity of both sides, not just this one resource's own conserved total - previously
/// this class clamped only to [0, Total] per resource independently, with the real "the fleet's
/// combined cargo can't exceed TotalCargoCapacity" constraint checked once at Apply time
/// (rejecting the whole transfer with an error message instead of ever preventing the invalid
/// drag in the first place). Now every row in the same transfer is wired to its siblings (see
/// <see cref="AttachSiblings"/>) so each one's own live <see cref="EffectiveMaximum"/>/
/// <see cref="EffectiveMinimum"/> account for what's already committed to the OTHER resources -
/// dragging one slider up correspondingly shrinks the room left for the others, exactly like a
/// real, shared cargo hold.
/// </summary>
public class CargoResourceRowViewModel : ViewModelBase
{
    public string Label { get; }

    /// <summary>The conserved total (current fleet amount + current other-side amount).</summary>
    public int Total { get; }

    private readonly int sourceCapacity;
    private readonly int? targetCapacity;

    private IReadOnlyList<CargoResourceRowViewModel> siblings = Array.Empty<CargoResourceRowViewModel>();

    private int fleetAmount;

    /// <param name="sourceCapacity">This fleet's own total capacity for whatever unit Total is
    /// measured in (kT for cargo, mg for fuel) - shared across every sibling row wired via
    /// <see cref="AttachSiblings"/>, since a real cargo hold has one combined size, not one per
    /// resource.</param>
    /// <param name="targetCapacity">The other side's own capacity, or null when the other side is
    /// a planet (no cargo-hold limit to speak of - a planet's stockpile isn't bounded by anything
    /// this transfer needs to enforce).</param>
    public CargoResourceRowViewModel(string label, int total, int initialFleetAmount, int sourceCapacity, int? targetCapacity = null)
    {
        Label = label;
        Total = total;
        fleetAmount = Math.Clamp(initialFleetAmount, 0, total);
        this.sourceCapacity = sourceCapacity;
        this.targetCapacity = targetCapacity;
    }

    /// <summary>Wires this row to every row (including itself) in the same transfer, so its own
    /// capacity clamp can see what's already committed to its siblings. Called once, right after
    /// every row in a transfer is constructed - a row with no siblings attached behaves as if it
    /// were the only resource being transferred (its own capacity, nothing shared).</summary>
    public void AttachSiblings(IReadOnlyList<CargoResourceRowViewModel> allRows)
    {
        siblings = allRows;
    }

    /// <summary>The highest value this row's FleetAmount can currently take without either
    /// exceeding this resource's own conserved Total or pushing the fleet's COMBINED cargo mass
    /// (this row plus every sibling's current FleetAmount) over sourceCapacity.</summary>
    public int EffectiveMaximum => Math.Min(Total, Math.Max(0, sourceCapacity - OthersFleetMass()));

    /// <summary>The lowest value this row's FleetAmount can currently take without pushing the
    /// OTHER side's combined amount (this row's PlanetAmount plus every sibling's current
    /// PlanetAmount) over targetCapacity - always 0 when the other side is an uncapped planet
    /// (targetCapacity is null).</summary>
    public int EffectiveMinimum
    {
        get
        {
            if (!targetCapacity.HasValue)
            {
                return 0;
            }

            int minByTarget = Total - Math.Max(0, targetCapacity.Value - OthersPlanetMass());
            return Math.Max(0, Math.Min(minByTarget, EffectiveMaximum));
        }
    }

    public int FleetAmount
    {
        get => fleetAmount;
        set
        {
            int min = EffectiveMinimum;
            int max = EffectiveMaximum;
            int clamped = Math.Clamp(value, min, Math.Max(min, max));
            if (SetProperty(ref fleetAmount, clamped))
            {
                OnPropertyChanged(nameof(PlanetAmount));

                // This row's own committed amount just changed, which shifts how much capacity
                // is left for every sibling - tell them so their own EffectiveMaximum/
                // EffectiveMinimum (and, if now out of range, their own FleetAmount) refresh too.
                foreach (CargoResourceRowViewModel sibling in siblings)
                {
                    if (!ReferenceEquals(sibling, this))
                    {
                        sibling.NotifyCapacityChanged();
                    }
                }
            }
        }
    }

    public int PlanetAmount => Total - FleetAmount;

    private int OthersFleetMass()
    {
        return siblings.Where(row => !ReferenceEquals(row, this)).Sum(row => row.fleetAmount);
    }

    private int OthersPlanetMass()
    {
        return siblings.Where(row => !ReferenceEquals(row, this)).Sum(row => row.PlanetAmount);
    }

    /// <summary>Called by a sibling after ITS OWN FleetAmount changed - refreshes this row's
    /// bounds and, if its current value no longer fits them, re-clamps it (freeing capacity back
    /// up for the sibling that changed, rather than leaving this row silently over-committed).</summary>
    internal void NotifyCapacityChanged()
    {
        OnPropertyChanged(nameof(EffectiveMaximum));
        OnPropertyChanged(nameof(EffectiveMinimum));

        int min = EffectiveMinimum;
        int max = EffectiveMaximum;
        if (fleetAmount > max || fleetAmount < min)
        {
            FleetAmount = Math.Clamp(fleetAmount, min, Math.Max(min, max));
        }
    }
}
