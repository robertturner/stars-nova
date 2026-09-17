using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One choice in the Split/Merge "other side" picker - either a brand-new fleet
/// (<see cref="Fleet"/> null, matching FleetDetail's "Split" button passing a null otherFleet)
/// or an existing same-position, non-starbase fleet to merge into (matching FleetDetail's
/// comboOtherFleets, populated only from fleets sharing the selected fleet's position).
/// </summary>
public class SplitMergeTargetOption
{
    public Fleet? Fleet { get; }

    public string DisplayName { get; }

    /// <summary>True for the "New Fleet" option - the only one a suggested split-off fleet name
    /// (<see cref="InspectorViewModel.NewSplitFleetName"/>) makes sense for.</summary>
    public bool IsNewFleet => Fleet == null;

    public SplitMergeTargetOption(Fleet? fleet, string displayName)
    {
        Fleet = fleet;
        DisplayName = displayName;
    }
}
