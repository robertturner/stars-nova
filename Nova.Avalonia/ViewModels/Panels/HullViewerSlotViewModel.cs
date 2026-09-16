using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One occupied slot in a read-only hull view (see HullViewerViewModel) - unlike
/// HullSlotRowViewModel (the Ship Designer's editable equivalent), this has no Options list or
/// TryPlace/Clear: a saved design's HullModule already carries its real AllocatedComponent/
/// ComponentCount, so there's nothing left to pick.
/// </summary>
public class HullViewerSlotViewModel
{
    /// <summary>Position in the hull's 5-wide slot grid - see components.xml's per-hull
    /// CellNumber, same convention HullSlotRowViewModel uses.</summary>
    public int Column { get; }

    public int Row { get; }

    public string Name { get; }

    public int Quantity { get; }

    public object? Icon { get; }

    public HullViewerSlotViewModel(HullModule module)
    {
        Column = module.CellNumber % 5;
        Row = module.CellNumber / 5;
        Name = module.AllocatedComponent?.Name ?? module.ComponentType;
        Quantity = module.ComponentCount;
        Icon = module.AllocatedComponent?.ComponentImage;
    }
}
