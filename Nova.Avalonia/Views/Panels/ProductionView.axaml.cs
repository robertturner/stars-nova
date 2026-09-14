using Avalonia.Controls;
using Avalonia.Input;
using Nova.Avalonia.ViewModels.Panels;

namespace Nova.Avalonia.Views.Panels;

public partial class ProductionView : UserControl
{
    public ProductionView()
    {
        InitializeComponent();
    }

    // Starts the hold-repeat timer for a row's "+"/"−" button. A plain quick click still just
    // runs its own Command (+1/-1, untouched) - this only matters if the pointer stays down
    // long enough for BeginHold's first tick to fire. Handled on the individual Button itself
    // (not a stable ancestor) because that's the only moment we can reliably read which row -
    // via its DataContext - was actually pressed; ProductionViewModel takes it from here since
    // the row and its Button may not survive the first repeat tick (see BeginHold's own
    // comment for why).
    private void OnIncrementPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginHold(sender, +1);
    }

    private void OnDecrementPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginHold(sender, -1);
    }

    private void BeginHold(object? sender, int direction)
    {
        if (sender is Button { DataContext: ProductionItemViewModel row } &&
            DataContext is ProductionViewModel viewModel)
        {
            viewModel.BeginHold(row.Index, direction);
        }
    }

    // Attached on this UserControl (not the individual buttons) since by the time a held press
    // is actually released, the button that started the hold has likely already been replaced
    // by a rebuilt row - see ProductionViewModel.BeginHold's comment. A stable ancestor still
    // receives the release via normal hit-testing regardless of which row is currently under
    // the pointer, and this handler is a no-op whenever no hold is in progress.
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        (DataContext as ProductionViewModel)?.EndHold();
    }
}
