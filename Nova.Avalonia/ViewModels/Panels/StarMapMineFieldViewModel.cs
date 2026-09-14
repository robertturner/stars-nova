using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One minefield on the map - a big, clickable circle rather than a small marker, so unlike
/// StarMapStarViewModel/StarMapFleetViewModel it stores its own top-left corner (X/Y) already
/// offset by its radius, matching how StarMapScanCircleViewModel centers a circle at a point.
/// Selectable via the same SelectionService as stars/fleets (see MapMarkerViewModel), which is
/// what lets a minefield show up in the Inspector - see InspectorViewModel.ShowMinefield.
/// </summary>
public class StarMapMineFieldViewModel : MapMarkerViewModel
{
    public double Diameter { get; }

    public StarMapMineFieldViewModel(string name, double centerX, double centerY, double radius, IBrush color, object selectable, SelectionService selection)
        : base(name, centerX - radius, centerY - radius, color, selectable, selection)
    {
        Diameter = radius * 2;
    }
}
