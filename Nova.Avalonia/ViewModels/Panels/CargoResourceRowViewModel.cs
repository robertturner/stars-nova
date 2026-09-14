using System;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One resource row in the fleet Cargo Transfer section - a slider from 0 to
/// <see cref="Total"/> (the conserved fleet+planet total for this resource) representing how
/// much the FLEET should end up holding; <see cref="PlanetAmount"/> is just the remainder.
/// Mirrors CargoDialog.cs's per-resource clamp callbacks (CargoIron_ValueChanged etc.), except
/// the cross-resource "total fleet mass can't exceed TotalCargoCapacity" constraint is checked
/// once at Apply time in InspectorViewModel rather than re-clamped live on every keystroke - a
/// simplification, not a behavior change to the actual transfer semantics.
/// </summary>
public class CargoResourceRowViewModel : ViewModelBase
{
    public string Label { get; }

    /// <summary>The conserved total (current fleet amount + current planet stock).</summary>
    public int Total { get; }

    private int fleetAmount;

    public int FleetAmount
    {
        get => fleetAmount;
        set
        {
            int clamped = Math.Clamp(value, 0, Total);
            if (SetProperty(ref fleetAmount, clamped))
            {
                OnPropertyChanged(nameof(PlanetAmount));
            }
        }
    }

    public int PlanetAmount => Total - FleetAmount;

    public CargoResourceRowViewModel(string label, int total, int initialFleetAmount)
    {
        Label = label;
        Total = total;
        fleetAmount = initialFleetAmount;
    }
}
