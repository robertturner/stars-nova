using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>Which of the scan-overlay's circles this is: the ordinary scanner wash, the
/// half-radius penetrating-scan circle, or the Packet Physics mass-driver range circle (they share
/// the same view-option toggle bit).</summary>
public enum ScanCircleKind
{
    Primary,
    Penetrating,
    MassDriver,
}

/// <summary>
/// A translucent scan-range wash drawn under the stars/fleets layer, matching the WinForms
/// StarMap's own long-range/penetrating scanner circles (see StarMap.cs's "(1a)/(1b)/(2)"
/// comments) - only ever drawn for this empire's own stars and fleets, since that's the only
/// scan coverage the player actually knows.
/// </summary>
public class StarMapScanCircleViewModel
{
    public double Diameter { get; }

    /// <summary>Canvas.Left - the circle's own X/Y is its center, not its top-left corner.</summary>
    public double Left { get; }

    /// <summary>Canvas.Top - see <see cref="Left"/>.</summary>
    public double Top { get; }

    public IBrush Fill { get; }

    /// <summary>Which circle family this is (behavior-specs-11/client-interface.md).</summary>
    public ScanCircleKind Kind { get; }

    public StarMapScanCircleViewModel(double centerX, double centerY, double radius, IBrush fill, ScanCircleKind kind = ScanCircleKind.Primary)
    {
        Diameter = radius * 2;
        Left = centerX - radius;
        Top = centerY - radius;
        Fill = fill;
        Kind = kind;
    }
}
