using Avalonia.Controls;
using Avalonia.Input;
using Nova.Avalonia.ViewModels.Panels;

namespace Nova.Avalonia.Views.Panels;

public partial class StarMapDocumentView : UserControl
{
    public StarMapDocumentView()
    {
        InitializeComponent();
    }

    // Plain wheel zooms (panning is already available via the ScrollViewer's own scrollbars/
    // drag), matching common map-app conventions. Marking the event handled stops the
    // ScrollViewer from also scrolling on the same wheel input.
    private void OnMapPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is StarMapDocumentViewModel viewModel)
        {
            const double step = 1.15;
            viewModel.Zoom *= e.Delta.Y > 0 ? step : 1 / step;
            e.Handled = true;
        }
    }

    // Avalonia's built-in Holding gesture, same pattern ShipDesignView.axaml.cs's
    // OnComponentItemHolding uses (see that method's own comment on why this - not a hand-rolled
    // PointerPressed/DispatcherTimer - is the one that actually works reliably on touch).
    private void OnFleetMarkerHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StarMapFleetViewModel fleet } || DataContext is not StarMapDocumentViewModel viewModel)
        {
            return;
        }

        switch (e.HoldingState)
        {
            case HoldingState.Started:
                viewModel.HullViewer.Show(fleet.PrimaryDesign);
                break;
            case HoldingState.Completed:
            case HoldingState.Canceled:
                viewModel.HullViewer.Hide();
                break;
        }
    }
}
