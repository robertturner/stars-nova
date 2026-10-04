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

    /// <summary>A single tap on the row shows this design's details (TODO-FEATURES #1).</summary>
    public IRelayCommand SelectCommand { get; }

    public OwnedDesignRowViewModel(ShipDesign design, int quantityInUse, System.Action onDelete, System.Action onSelect)
    {
        Design = design;
        CostSummary = ResourceFormat.Cost(design.Cost);
        QuantityInUse = quantityInUse;
        DeleteCommand = new RelayCommand(onDelete);
        SelectCommand = new RelayCommand(onSelect);
    }
}
