using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// One victory-condition row on the New Game Victory page - wraps a single
/// <see cref="EnabledValue"/> field of <see cref="GameSettings"/> (e.g.
/// GameSettings.Data.PlanetsOwned). Used eight times by <see cref="NewGameViewModel"/>, once per
/// condition, but the screen exposes only seven checkboxes: condition 3 (NumberOfFields) is a
/// secondary magnitude paired with condition 2 (TechLevels) and has no checkbox of its own
/// (behavior-specs-11/victory-conditions.md section 1), so its row's <see cref="HasCheckbox"/> is
/// false. Condition 5 is <see cref="GameSettings.SecondPlaceScore"/>.
/// </summary>
public class VictoryConditionRowViewModel : ObservableObject
{
    private readonly EnabledValue enabledValue;

    public VictoryConditionRowViewModel(string title, EnabledValue enabledValue, int minimum, int maximum, bool hasCheckbox = true)
    {
        Title = title;
        this.enabledValue = enabledValue;
        Minimum = minimum;
        Maximum = maximum;
        HasCheckbox = hasCheckbox;
    }

    public string Title { get; }

    /// <summary>False for condition 3 (NumberOfFields): its magnitude matters only while
    /// condition 2 is enabled and the setup screen gives it no toggle of its own
    /// (victory-conditions.md section 1).</summary>
    public bool HasCheckbox { get; }

    public int Minimum { get; }

    public int Maximum { get; }

    public bool IsChecked
    {
        get => enabledValue.IsChecked;
        set
        {
            if (enabledValue.IsChecked != value)
            {
                enabledValue.IsChecked = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Re-reads both values after the underlying EnabledValue changed elsewhere (New
    /// Game's Reset to defaults).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(NumericValue));
    }

    public int NumericValue
    {
        get => enabledValue.NumericValue;
        set
        {
            int clamped = System.Math.Clamp(value, Minimum, Maximum);
            if (enabledValue.NumericValue != clamped)
            {
                enabledValue.NumericValue = clamped;
                OnPropertyChanged();
            }
        }
    }
}

/// <summary>
/// Gives the victory page's spinners the original setup screen's two-step behaviour
/// (behavior-specs-11/victory-conditions.md section 1): an arrow / spinner-button step moves by one
/// (<see cref="NumericUpDown.Increment"/>, set to <see cref="NormalStep"/> on the page), while Page
/// Up / Page Down moves by <see cref="PageStep"/> and repeats while held through the platform's
/// normal key repeat. Avalonia's <see cref="NumericUpDown"/> has no Page Up/Down handling of its
/// own, so this attached property installs a tunnelling KeyDown handler that intercepts those two
/// keys before the inner text box can consume them. It lives here, beside the row view model,
/// because no view code-behind is owned by this change.
/// </summary>
public sealed class VictorySpinner
{
    private VictorySpinner()
    {
    }

    /// <summary>One normal step: an arrow key or a spinner button click.</summary>
    public const int NormalStep = 1;

    /// <summary>One Page Up / Page Down step ("five steps at once", victory-conditions.md section 1).</summary>
    public const int PageStep = 5;

    public static readonly AttachedProperty<bool> PageStepEnabledProperty =
        AvaloniaProperty.RegisterAttached<VictorySpinner, NumericUpDown, bool>("PageStepEnabled");

    static VictorySpinner()
    {
        PageStepEnabledProperty.Changed.AddClassHandler<NumericUpDown>((spinner, args) =>
        {
            spinner.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            if (args.NewValue is true)
            {
                spinner.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            }
        });
    }

    public static bool GetPageStepEnabled(NumericUpDown control) => control.GetValue(PageStepEnabledProperty);

    public static void SetPageStepEnabled(NumericUpDown control, bool value) => control.SetValue(PageStepEnabledProperty, value);

    /// <summary>The value after one step from <paramref name="current"/>: one in the given
    /// <paramref name="direction"/>, or five when <paramref name="page"/> (Page Up/Down).</summary>
    public static decimal Stepped(decimal current, int direction, bool page)
    {
        return current + (direction * (page ? PageStep : NormalStep));
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not NumericUpDown spinner)
        {
            return;
        }

        int direction;
        if (e.Key == Key.PageUp)
        {
            direction = 1;
        }
        else if (e.Key == Key.PageDown)
        {
            direction = -1;
        }
        else
        {
            return;
        }

        decimal next = Stepped(spinner.Value ?? 0m, direction, page: true);
        if (spinner.Minimum is decimal minimum && next < minimum)
        {
            next = minimum;
        }

        if (spinner.Maximum is decimal maximum && next > maximum)
        {
            next = maximum;
        }

        spinner.Value = next;
        e.Handled = true;
    }
}
