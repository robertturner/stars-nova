using System;
using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// A single colored range bar in the Inspector's planet detail - the Avalonia equivalent of the
/// WinForms PlanetSummary.cs's Gauge controls (a filled span plus a marker), used for two rather
/// different things: Gravity/Temperature/Radiation (a green band showing this empire's own
/// tolerance range, with a marker at the planet's actual value, colored by whether it falls
/// inside that band) via <see cref="ForEnvironment"/>, and mineral concentration/surface stock
/// (a simple mineral-colored fill with no separate "tolerable range" concept) via
/// <see cref="ForMineral"/>. Unlike the original's fixed per-gauge colors, the environment bar's
/// marker color IS habitability-driven here (green if in range, red if not) - a deliberate
/// improvement over the number-only display this replaces, not a straight port.
/// </summary>
public class RangeBarViewModel : ViewModelBase
{
    /// <summary>Fixed pixel width every bar renders at - simplest way to turn a 0-100 (or
    /// 0-max) value into an absolute position without needing the view to know the data's
    /// scale.</summary>
    public const double BarWidth = 160;

    public string Label { get; }

    public string FormattedValue { get; }

    /// <summary>Left edge of the colored band, in pixels.</summary>
    public double BandLeft { get; }

    /// <summary>Width of the colored band, in pixels.</summary>
    public double BandWidth { get; }

    public IBrush BandColor { get; }

    public bool ShowMarker { get; }

    /// <summary>Left edge of the current-value marker, in pixels. Meaningless when
    /// <see cref="ShowMarker"/> is false.</summary>
    public double MarkerLeft { get; }

    public IBrush MarkerColor { get; }

    private RangeBarViewModel(string label, string formattedValue, double bandLeft, double bandWidth,
        IBrush bandColor, bool showMarker, double markerLeft, IBrush markerColor)
    {
        Label = label;
        FormattedValue = formattedValue;
        BandLeft = bandLeft;
        BandWidth = bandWidth;
        BandColor = bandColor;
        ShowMarker = showMarker;
        MarkerLeft = markerLeft;
        MarkerColor = markerColor;
    }

    /// <summary>Gravity/Temperature/Radiation: a green band spans this empire's own tolerance
    /// range (or the whole bar, if immune), with a marker at the planet's actual 0-100 value -
    /// red if that value falls outside the band, green if inside.</summary>
    public static RangeBarViewModel ForEnvironment(string label, double currentValue, double minTolerance,
        double maxTolerance, bool immune, string formattedValue)
    {
        double clampedValue = Math.Clamp(currentValue, 0, 100);
        bool inRange = immune || (clampedValue >= minTolerance && clampedValue <= maxTolerance);

        double bandLeft = immune ? 0 : Math.Clamp(minTolerance, 0, 100) / 100.0 * BarWidth;
        double bandWidth = immune ? BarWidth : Math.Clamp(maxTolerance - minTolerance, 0, 100) / 100.0 * BarWidth;

        return new RangeBarViewModel(
            label,
            immune ? $"{formattedValue} (Immune)" : formattedValue,
            bandLeft,
            bandWidth,
            Brushes.SeaGreen,
            showMarker: true,
            markerLeft: clampedValue / 100.0 * BarWidth,
            markerColor: inRange ? Brushes.White : Brushes.Red);
    }

    /// <summary>Mineral concentration (0-100%) or surface stockpile (kT, no fixed maximum -
    /// <paramref name="max"/> is a chosen display ceiling, not a real game limit): a simple
    /// mineral-colored fill from 0 to the current value, no separate tolerance band.</summary>
    public static RangeBarViewModel ForMineral(string label, double value, double max, string formattedValue, IBrush color)
    {
        double fillWidth = Math.Clamp(max <= 0 ? 0 : value / max, 0, 1) * BarWidth;

        return new RangeBarViewModel(
            label,
            formattedValue,
            bandLeft: 0,
            bandWidth: fillWidth,
            bandColor: color,
            showMarker: false,
            markerLeft: 0,
            markerColor: color);
    }
}
