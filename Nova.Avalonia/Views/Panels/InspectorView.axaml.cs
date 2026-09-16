using Avalonia.Controls;
using Avalonia.Input;
using Nova.Avalonia.ViewModels.Panels;

namespace Nova.Avalonia.Views.Panels;

public partial class InspectorView : UserControl
{
    public InspectorView()
    {
        InitializeComponent();
    }

    // Same Holding-gesture pattern as ShipDesignView.axaml.cs's OnComponentItemHolding,
    // StarMapDocumentView.axaml.cs's OnFleetMarkerHolding and NavigatorView.axaml.cs's
    // OnFleetIconHolding - press-and-hold a ship-type row in the "Ships" composition list to
    // see that design's full component layout via the shared HullViewer.
    private void OnCompositionRowHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (sender is not Control { DataContext: SplitMergeRowViewModel row } || DataContext is not InspectorViewModel viewModel)
        {
            return;
        }

        switch (e.HoldingState)
        {
            case HoldingState.Started:
                viewModel.HullViewer.Show(row.Design);
                break;
            case HoldingState.Completed:
            case HoldingState.Canceled:
                viewModel.HullViewer.Hide();
                break;
        }
    }
}
