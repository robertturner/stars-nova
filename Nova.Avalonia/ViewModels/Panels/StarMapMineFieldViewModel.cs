using Avalonia;
using Avalonia.Media;
using Nova.Client.Map;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One minefield on the map - a big, clickable circle rather than a small marker, so unlike
/// StarMapStarViewModel/StarMapFleetViewModel it stores its own top-left corner (X/Y) already
/// offset by its radius, matching how StarMapScanCircleViewModel centers a circle at a point.
/// Selectable via the same SelectionService as stars/fleets (see MapMarkerViewModel), which is
/// what lets a minefield show up in the Inspector - see InspectorViewModel.ShowMinefield.
///
/// behavior-specs-11/client-interface.md: the field is filled with one of three per-type patterns
/// (Standard / Heavy / Speed Bump) and its visibility is gated by the minefield owner mask; the
/// owner colour is kept as the disc's outline so ownership still reads.
/// </summary>
public class StarMapMineFieldViewModel : MapMarkerViewModel
{
    public double Diameter { get; }

    /// <summary>Which of the three per-type fill patterns this field uses.</summary>
    public MinefieldPattern Pattern { get; }

    /// <summary>The pattern brush (the type's motif over the owner-colour wash).</summary>
    public IBrush PatternFill { get; }

    /// <summary>This field's single visibility category (own / other / detected or undetected
    /// enemy) - see <see cref="MinefieldOverlay"/>.</summary>
    public MinefieldVisibility Category { get; }

    private bool isVisible = true;

    /// <summary>Whether the current owner mask shows this field.</summary>
    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }

    public StarMapMineFieldViewModel(
        string name,
        double centerX,
        double centerY,
        double radius,
        IBrush color,
        MinefieldPattern pattern,
        MinefieldVisibility category,
        object selectable,
        SelectionService selection)
        : base(name, centerX - radius, centerY - radius, color, selectable, selection)
    {
        Diameter = radius * 2;
        Pattern = pattern;
        PatternFill = PatternFillFor(pattern, color);
        Category = category;
    }

    /// <summary>
    /// A tiled brush whose motif distinguishes the three minefield types, over a translucent wash
    /// of the owner colour. Standard uses diagonal lines, Heavy a cross-hatch and Speed Bump dots.
    /// </summary>
    private static IBrush PatternFillFor(MinefieldPattern pattern, IBrush ownerColor)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)),
            Brush = ownerColor,
        });

        var stroke = new Pen(new SolidColorBrush(global::Avalonia.Media.Color.FromArgb(190, 255, 255, 255)), 1.0);
        string motif = pattern switch
        {
            MinefieldPattern.Heavy => "M0,0 L8,8 M8,0 L0,8",
            MinefieldPattern.SpeedBump => "M1,1 L1,1 M5,5 L5,5",
            _ => "M0,0 L8,8",
        };
        if (pattern == MinefieldPattern.SpeedBump)
        {
            group.Children.Add(new GeometryDrawing
            {
                Geometry = Geometry.Parse("M1,1 L1.01,1 M5,5 L5.01,5"),
                Pen = new Pen(new SolidColorBrush(global::Avalonia.Media.Color.FromArgb(210, 255, 255, 255)), 2.2),
            });
        }
        else
        {
            group.Children.Add(new GeometryDrawing
            {
                Geometry = Geometry.Parse(motif),
                Pen = stroke,
            });
        }

        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            Stretch = Stretch.None,
        };
    }
}
