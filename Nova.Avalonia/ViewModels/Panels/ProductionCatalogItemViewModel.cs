using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the "available to build" catalog for the selected planet - a fixed
/// installation (Factory/Mine/Defenses/Mineral Alchemy/Terraform) or one of this empire's own
/// ship designs, mirroring ProductionDialog.OnLoad's design-list construction.
/// </summary>
public class ProductionCatalogItemViewModel
{
    public IProductionUnit Unit { get; }

    public string Name => Unit.Name;

    public string CostSummary { get; }

    public ProductionCatalogItemViewModel(IProductionUnit unit)
    {
        Unit = unit;
        CostSummary = ResourceFormat.Cost(unit.Cost);
    }
}
