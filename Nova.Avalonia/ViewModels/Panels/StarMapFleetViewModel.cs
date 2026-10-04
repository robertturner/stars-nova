using System.Linq;
using System.Text;
using Avalonia.Media;
using Nova.Client.Map;
using Nova.Common;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One fleet on the map (not currently in orbit - see StarMapDocumentViewModel), drawn as a
/// small triangle marker pointing in its direction of travel, or - while it is the tracked
/// (selected) object - as the fixed 11x11 chevron (behavior-specs-10/client-interface.md,
/// "Deep-space fleet marker"): blue for the viewer's own fleet, red otherwise.
/// </summary>
public class StarMapFleetViewModel : MapMarkerViewModel
{
    /// <summary>
    /// Degrees clockwise from due "up" - the same convention as <c>FleetIntel.Bearing</c>
    /// (set by <c>TurnGenerator</c> from atan2(dy,dx)+90) and the WinForms StarMap's own
    /// <c>g.RotateTransform((float)report.Bearing)</c>.
    /// </summary>
    public double Bearing { get; }

    /// <summary>The fleet's first ship type - only available when Selectable is a real, owned
    /// Fleet (a foreign FleetIntel report never reveals exact composition), null otherwise. What
    /// a press-and-hold on the marker shows in the shared HullViewer.</summary>
    public ShipDesign? PrimaryDesign => (Selectable as Fleet)?.Composition.Values.FirstOrDefault()?.Design;

    /// <summary>Total ship count in this stack (FleetIntel.Count, or the live fleet's own count).</summary>
    public int ShipCount { get; }

    /// <summary>Display text for the badge - "999+" past the clamp (kept deliberately: the
    /// coverage report's note on row 33).</summary>
    public string ShipCountDisplay => ShipCount > 999 ? "999+" : ShipCount.ToString();

    public bool IsOwn { get; }

    private bool showBadge = true;

    /// <summary>Ship-count badge toggle (second view-options word bit 0x10, Shift+0).</summary>
    public bool ShowBadge
    {
        get => showBadge;
        set => SetProperty(ref showBadge, value);
    }

    /// <summary>The chevron replaces the heading triangle while this fleet is tracked.</summary>
    public bool ShowChevron => IsSelected;

    public bool ShowTriangle => !IsSelected;

    public IBrush ChevronBrush => IsOwn ? Brushes.Blue : Brushes.Red;

    /// <summary>The 11x11 chevron as unit squares, from TrackedMarkerShape.</summary>
    public static Geometry ChevronGeometry { get; } = Geometry.Parse(BuildPathData(TrackedMarkerShape.Chevron11()));

    public StarMapFleetViewModel(string name, double x, double y, double bearing, IBrush color, object selectable, SelectionService selection, int shipCount, bool isOwn)
        : base(name, x, y, color, selectable, selection)
    {
        Bearing = bearing;
        ShipCount = shipCount;
        IsOwn = isOwn;
    }

    protected override void OnIsSelectedChanged()
    {
        OnPropertyChanged(nameof(ShowChevron));
        OnPropertyChanged(nameof(ShowTriangle));
    }

    private static string BuildPathData(bool[,] mask)
    {
        var data = new StringBuilder();
        foreach ((int column, int row) in TrackedMarkerShape.Pixels(mask))
        {
            data.Append("M").Append(column).Append(',').Append(row).Append(" h1 v1 h-1 z ");
        }

        return data.ToString().TrimEnd();
    }
}
