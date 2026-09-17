using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Nova.Avalonia.ViewModels.Panels;

namespace Nova.Avalonia.Views.Panels;

public partial class StarMapDocumentView : UserControl
{
    public StarMapDocumentView()
    {
        InitializeComponent();

        // Tunnel, not the default Bubble a plain PointerPressed="..." XAML attribute on each
        // marker's own Button would use - a Button marks its own internal press handling
        // Handled during the tunnel phase before bubbling back out (confirmed elsewhere this
        // session, on ShipDesignView's component buttons: an external bubble-phase
        // PointerPressed handler attached directly to a Button never saw the touch at all).
        // Attaching here, on the shared ancestor, ahead of that swallowing, is what lets this
        // resolve a star/fleet tie BEFORE either marker's own Button click can fire - see
        // StarMapDocumentViewModel.FindNearestStarOrFleetMarker for the actual arbitration.
        MapPanel.AddHandler(InputElement.PointerPressedEvent, OnMapPointerPressedTunnel, RoutingStrategies.Tunnel);
    }

    private void OnMapPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not StarMapDocumentViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(MapPanel);
        MapMarkerViewModel? nearest = viewModel.FindNearestStarOrFleetMarker(position.X, position.Y);
        if (nearest != null)
        {
            nearest.SelectCommand.Execute(null);
            e.Handled = true;
        }
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
