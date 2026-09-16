using System.Linq;
using Avalonia.Media;
using Nova.Common;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One fleet on the map (not currently in orbit - see StarMapDocumentViewModel), drawn as a
/// small triangle marker pointing in its direction of travel.
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
    /// a press-and-hold on the marker shows in the shared HullViewer - no separate on-map icon
    /// (the heading triangle alone is enough at map scale, per explicit feedback).</summary>
    public ShipDesign? PrimaryDesign => (Selectable as Fleet)?.Composition.Values.FirstOrDefault()?.Design;

    /// <summary>Total ship count in this stack - docs/behavior-specs-5/client-interface.md
    /// documents a small numeric badge next to a fleet's map icon showing this (clamped to 999),
    /// missing from this port until now. Comes from FleetIntel.Count directly (a basic scan
    /// reveals a fleet's ship count even when full composition isn't known), not
    /// PrimaryDesign/Fleet.Composition, which is only available for the viewer's own fleets.</summary>
    public int ShipCount { get; }

    /// <summary>Display text for the badge - clamped at "999+" per the spec's own documented
    /// clamp, rather than an ever-widening number for a very large stack.</summary>
    public string ShipCountDisplay => ShipCount > 999 ? "999+" : ShipCount.ToString();

    public StarMapFleetViewModel(string name, double x, double y, double bearing, IBrush color, object selectable, SelectionService selection, int shipCount)
        : base(name, x, y, color, selectable, selection)
    {
        Bearing = bearing;
        ShipCount = shipCount;
    }
}
