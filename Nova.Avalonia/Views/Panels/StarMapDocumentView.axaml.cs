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
}
