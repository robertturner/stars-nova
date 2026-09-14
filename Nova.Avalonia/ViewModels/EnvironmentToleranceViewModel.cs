using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Wraps one <see cref="EnvironmentTolerance"/> (Gravity/Temperature/Radiation) for
/// EnvironmentToleranceEditor - the Avalonia-native replacement for the WinForms
/// ControlLibrary.Range dual-handle drag bar (a System.Windows.Forms.UserControl, not reusable
/// here). Two Sliders (0-100 internal scale, matching the model's own scale) replace the
/// original's drag handles/expand/contract buttons - a deliberate simplification given neither
/// mouse-drag precision nor those buttons' only reason for existing (WinForms having no built-in
/// range slider) matter here, and two sliders are far more touch-friendly for Android besides.
/// </summary>
public class EnvironmentToleranceViewModel : ObservableObject
{
    private readonly EnvironmentTolerance tolerance;
    private readonly Func<int, string> formatValue;

    public EnvironmentToleranceViewModel(string title, EnvironmentTolerance tolerance, Func<int, string> formatValue)
    {
        Title = title;
        this.tolerance = tolerance;
        this.formatValue = formatValue;
    }

    public string Title { get; }

    /// <summary>docs/behavior-specs-4/race-designer-ui-and-availability.md's confirmed constraint:
    /// adjusting a bound enforces a minimum band width of 20 (out of the 0-100 scale) - narrowing
    /// past that re-centers the band to force exactly 20 wide, rather than merely clamping the
    /// moved bound against the other one (which would allow an arbitrarily narrow band).</summary>
    private const int MinimumBandWidth = 20;

    public int MinValue
    {
        get => tolerance.MinimumValue;
        set => SetBound(isMin: true, value);
    }

    public int MaxValue
    {
        get => tolerance.MaximumValue;
        set => SetBound(isMin: false, value);
    }

    private void SetBound(bool isMin, int requestedValue)
    {
        int clamped = Math.Clamp(requestedValue, 0, 100);
        int newMin = isMin ? clamped : tolerance.MinimumValue;
        int newMax = isMin ? tolerance.MaximumValue : clamped;

        if (newMax - newMin < MinimumBandWidth)
        {
            // Re-center a MinimumBandWidth-wide band on the endpoint just adjusted.
            newMin = Math.Clamp(clamped - MinimumBandWidth / 2, 0, 100 - MinimumBandWidth);
            newMax = newMin + MinimumBandWidth;
        }

        if (tolerance.MinimumValue != newMin || tolerance.MaximumValue != newMax)
        {
            tolerance.MinimumValue = newMin;
            tolerance.MaximumValue = newMax;
            OnPropertyChanged(nameof(MinValue));
            OnPropertyChanged(nameof(MaxValue));
            OnPropertyChanged(nameof(FormattedMin));
            OnPropertyChanged(nameof(FormattedMax));
        }
    }

    public bool Immune
    {
        get => tolerance.Immune;
        set
        {
            if (tolerance.Immune != value)
            {
                tolerance.Immune = value;
                if (!value)
                {
                    // "Turning immunity off restores the ordinary editable interval defined by
                    // the draft (confirmed default reset values: a 20-80 band centered on 50)" -
                    // docs/behavior-specs-4/race-designer-ui-and-availability.md. Immunity itself
                    // needs no corresponding change here when turned ON - it pins the axis to its
                    // ideal value at the calculation level (see Race.NormalizeHabitalityDistance)
                    // regardless of whatever Min/Max are currently set to.
                    tolerance.MinimumValue = 20;
                    tolerance.MaximumValue = 80;
                    OnPropertyChanged(nameof(MinValue));
                    OnPropertyChanged(nameof(MaxValue));
                    OnPropertyChanged(nameof(FormattedMin));
                    OnPropertyChanged(nameof(FormattedMax));
                }
                OnPropertyChanged();
            }
        }
    }

    public string FormattedMin => formatValue(MinValue);

    public string FormattedMax => formatValue(MaxValue);
}
