using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the "available to build" catalog for the selected planet - a fixed
/// installation (Factory/Mine/Defenses/Mineral Alchemy/Terraform) or one of this empire's own
/// ship designs, mirroring ProductionDialog.OnLoad's design-list construction.
/// The terraform rows carry their own caption: the manual "Terraform Environment" (offered only
/// with headroom) and the always-offered auto "Min / Max Terraform" entry, which can only be
/// added as an auto-build order (<see cref="AutoOnly"/>). The mineral-packet rows (offered only
/// with a mass driver, Nova.Client.PacketOrders.CatalogItems) are the auto "Mineral Packets"
/// entry (auto only) and four manual items (<see cref="ManualOnly"/>).
/// </summary>
public class ProductionCatalogItemViewModel
{
    private readonly string? displayName;

    public IProductionUnit Unit { get; }

    public string Name => displayName ?? Unit.Name;

    public string CostSummary { get; }

    /// <summary>One of the terraform items (the Min/Max choice applies).</summary>
    public bool IsTerraform { get; }

    /// <summary>Only addable as an auto-build order.</summary>
    public bool AutoOnly { get; }

    /// <summary>Only addable as a manual order (the four manual mineral-packet items: the only
    /// auto-build packet type is the separate "Mineral Packets" entry, production-queue.md
    /// section 10).</summary>
    public bool ManualOnly { get; }

    public ProductionCatalogItemViewModel(IProductionUnit unit, string? displayName = null, bool isTerraform = false, bool autoOnly = false, bool manualOnly = false)
    {
        Unit = unit;
        this.displayName = displayName;
        IsTerraform = isTerraform;
        AutoOnly = autoOnly;
        ManualOnly = manualOnly && !autoOnly;
        CostSummary = ResourceFormat.Cost(unit.Cost);
    }
}
