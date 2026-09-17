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
    // StarMapDocumentView.axaml.cs's OnFleetMarkerHolding - one Border per distinct ship design
    // in the fleet (see NavigatorFleetItemViewModel.ShipTypes), so each shows ITS OWN design
    // rather than always the fleet's first one.
    private void OnFleetShipTypeHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (sender is not Control { DataContext: NavigatorFleetShipTypeViewModel shipType } || DataContext is not NavigatorViewModel viewModel)
        {
            return;
        }

        switch (e.HoldingState)
        {
            case HoldingState.Started:
                viewModel.HullViewer.Show(shipType.Design);
                break;
            case HoldingState.Completed:
            case HoldingState.Canceled:
                viewModel.HullViewer.Hide();
                break;
        }
    }
}
