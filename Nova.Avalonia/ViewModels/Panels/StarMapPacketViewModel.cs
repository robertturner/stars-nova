using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One mineral packet in flight that this empire sees this year (EmpireData.MineralPacketReports:
/// its own packets, every packet for a Packet Physics race, and packets inside a scanner's normal
/// range). Selectable like any other Mappable, so it can be measured to.
/// </summary>
public class StarMapPacketViewModel : MapMarkerViewModel
{
    public StarMapPacketViewModel(string name, double x, double y, IBrush color, object selectable, SelectionService selection)
        : base(name, x, y, color, selectable, selection)
    {
    }
}
