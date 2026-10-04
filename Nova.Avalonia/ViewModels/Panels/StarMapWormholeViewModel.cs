using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One detected wormhole end (EmpireData.WormholeReports - the last position and year this
/// empire saw it; behavior-specs-10/fleet-movement-scanning-cargo.md section 3). Selectable like
/// any other Mappable, so it can be measured to or targeted.
/// </summary>
public class StarMapWormholeViewModel : MapMarkerViewModel
{
    public StarMapWormholeViewModel(string name, double x, double y, IBrush color, object selectable, SelectionService selection)
        : base(name, x, y, color, selectable, selection)
    {
    }
}
