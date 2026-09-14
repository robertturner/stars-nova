using CommunityToolkit.Mvvm.Input;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>One row in the Design Manager list: an owned ship/starbase design.</summary>
public class OwnedDesignRowViewModel
{
    public ShipDesign Design { get; }

    public string Name => Design.Name;

    public string CostSummary { get; }

    public int Mass => Design.Mass;

    public int QuantityInUse { get; }

    public IRelayCommand DeleteCommand { get; }

    public OwnedDesignRowViewModel(ShipDesign design, int quantityInUse, System.Action onDelete)
    {
        Design = design;
        CostSummary = ResourceFormat.Cost(design.Cost);
        QuantityInUse = quantityInUse;
        DeleteCommand = new RelayCommand(onDelete);
    }
}
