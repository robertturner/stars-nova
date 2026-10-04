using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Nova.Avalonia.ViewModels;

namespace Nova.Avalonia.Views.Controls;

/// <summary>
/// One economic stepper row for Race Designer (behavior-specs-10/race-designer-ui-and-
/// availability.md, "Stepping behavior"): a decrement and an increment button that step by 1, or
/// by 3 while Shift is held, and auto-repeat while held down (RepeatButton). A RepeatButton's
/// Click carries no modifier keys, so the Shift state is captured when the press starts (pointer
/// or keyboard) and used for that press and all its repeats. Touch has no Shift: it steps by 1.
/// </summary>
public partial class RaceDesignerStepper : UserControl
{
    private bool shiftHeld;

    public RaceDesignerStepper()
    {
        InitializeComponent();

        foreach (RepeatButton button in new[] { DecrementButton, IncrementButton })
        {
            button.AddHandler(PointerPressedEvent, OnPressStarted, RoutingStrategies.Tunnel, handledEventsToo: true);
            button.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    private void OnPressStarted(object? sender, PointerPressedEventArgs e)
    {
        shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
    }

    private void DecrementButton_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as RaceDesignerEconomyRowViewModel)?.Step(-1, shiftHeld);
    }

    private void IncrementButton_Click(object? sender, RoutedEventArgs e)
    {
        (DataContext as RaceDesignerEconomyRowViewModel)?.Step(+1, shiftHeld);
    }
}
