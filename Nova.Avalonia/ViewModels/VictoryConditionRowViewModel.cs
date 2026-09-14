using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// One checkbox+numeric victory-condition row - wraps a single <see cref="EnabledValue"/> field
/// of <see cref="GameSettings"/> (e.g. GameSettings.Data.PlanetsOwned). Reused 8 times by
/// NewGameViewModel, one per condition the original NewGameWizard's Victory Conditions tab
/// exposed - plus SecondPlaceScore, which VictoryCheck.cs already checks but the original
/// WinForms UI never actually exposed a control for at all.
/// </summary>
public class VictoryConditionRowViewModel : ObservableObject
{
    private readonly EnabledValue enabledValue;

    public VictoryConditionRowViewModel(string title, EnabledValue enabledValue, int minimum, int maximum)
    {
        Title = title;
        this.enabledValue = enabledValue;
        Minimum = minimum;
        Maximum = maximum;
    }

    public string Title { get; }

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
