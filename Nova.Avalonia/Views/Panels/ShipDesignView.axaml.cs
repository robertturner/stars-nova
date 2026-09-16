using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Common.Components;

namespace Nova.Avalonia.Views.Panels;

public partial class ShipDesignView : UserControl
{
    private static readonly DataFormat<Component> ComponentFormat = DataFormat.CreateInProcessFormat<Component>("Nova.Component");

    public ShipDesignView()
    {
        InitializeComponent();
    }

    // Avalonia's built-in press-and-hold gesture (InputElement.Holding, enabled per-Button via
    // IsHoldingEnabled="True" in the DataTemplate) - NOT a hand-rolled PointerPressed/DispatcherTimer
    // combo, which was tried first and confirmed dead on arrival live on the emulator: Button
    // swallows PointerPressed internally before it ever reaches an external instance handler
    // attached to that same Button (confirmed via a tunnel-phase probe that saw the touch while
    // the Button's own bubble-phase PointerPressed handler never ran at all), so a timer started
    // from that handler would never even survive to be checked. The Holding gesture recognizer
    // hooks in at the same pipeline level as ScrollGestureRecognizer, ahead of that swallowing.
    private void OnComponentItemHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ComponentListItemViewModel item } || DataContext is not ShipDesignViewModel viewModel)
        {
            return;
        }

        switch (e.HoldingState)
        {
            case HoldingState.Started:
                viewModel.ShowInfo(item.Component);
                break;
            case HoldingState.Completed:
            case HoldingState.Canceled:
                viewModel.HideInfo();
                break;
        }
    }

    // Same wheel-zoom convention as StarMapDocumentView's own OnMapPointerWheelChanged (panning
    // is already available via the ScrollViewer's own scrollbars/touch drag).
    private void OnHullPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is ShipDesignViewModel viewModel)
        {
            const double step = 1.15;
            viewModel.Zoom *= e.Delta.Y > 0 ? step : 1 / step;
            e.Handled = true;
        }
    }

    // Real OS drag-and-drop, a desktop-only bonus alongside the tap-to-arm-then-tap path every
    // platform gets (see ComponentListItemViewModel's own comment on why touch doesn't get this
    // too) - both funnel into the exact same HullSlotRowViewModel.TryPlace logic, just reached a
    // different way, mirroring HullGrid's own Grid_DragBegin/Grid_DragEnter/Grid_DragDrop trio.
    //
    // Only for a real mouse: starting DragDrop.DoDragDropAsync unconditionally (including for a
    // plain touch tap with no actual drag) was confirmed live to swallow the gesture outright -
    // the Button's own Click/Command never fired at all afterward, since the drag machinery had
    // already taken over the pointer sequence from PointerPressed onward. Gating on
    // PointerType.Mouse leaves a touch tap alone entirely, so it reaches the Button normally and
    // SelectCommand fires as usual.
    private async void OnComponentItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Mouse)
        {
            return;
        }

        if (sender is not Button { DataContext: ComponentListItemViewModel item })
        {
            return;
        }

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(ComponentFormat, item.Component));
        await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy);
    }

    private void OnHullSlotDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = CanAccept(sender, e) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnHullSlotDrop(object? sender, DragEventArgs e)
    {
        if (sender is Button { DataContext: HullSlotRowViewModel slot } && TryGetComponent(e, out Component? component))
        {
            slot.TryPlace(component!, 1);
        }
    }

    private static bool CanAccept(object? sender, DragEventArgs e)
    {
        return sender is Button { DataContext: HullSlotRowViewModel slot }
            && TryGetComponent(e, out Component? component)
            && slot.Options.Any(o => o.Component == component);
    }

    private static bool TryGetComponent(DragEventArgs e, out Component? component)
    {
        component = e.DataTransfer.TryGetValue(ComponentFormat);
        return component != null;
    }
}
