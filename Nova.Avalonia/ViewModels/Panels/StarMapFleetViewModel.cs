using Avalonia.Media;

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

    public StarMapFleetViewModel(string name, double x, double y, double bearing, IBrush color, object selectable, SelectionService selection)
        : base(name, x, y, color, selectable, selection)
    {
        Bearing = bearing;
    }
}
