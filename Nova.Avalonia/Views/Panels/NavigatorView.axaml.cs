using Avalonia.Controls;
using Avalonia.Input;
using Nova.Avalonia.ViewModels.Panels;

namespace Nova.Avalonia.Views.Panels;

public partial class NavigatorView : UserControl
{
    public NavigatorView()
    {
        InitializeComponent();
    }

    // Same Holding-gesture pattern as ShipDesignView.axaml.cs's OnComponentItemHolding and
    // StarMapDocumentView.axaml.cs's OnFleetMarkerHolding.
    private void OnFleetIconHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (sender is not Control { DataContext: NavigatorFleetItemViewModel row } || DataContext is not NavigatorViewModel viewModel)
        {
            return;
        }

        switch (e.HoldingState)
        {
            case HoldingState.Started:
                viewModel.HullViewer.Show(row.PrimaryDesign);
                break;
            case HoldingState.Completed:
            case HoldingState.Canceled:
                viewModel.HullViewer.Hide();
                break;
        }
    }
}
