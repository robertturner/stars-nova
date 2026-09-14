using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Wraps one field of a race's <see cref="TechLevel"/>-typed ResearchCosts for
/// ResearchCostSelector - the Avalonia-native replacement for the WinForms
/// ControlLibrary.ResearchCost 3-way radio group (50% less / standard / 75% extra - the only
/// three values the original UI can ever set; 150 is load-only backward-compat, never
/// UI-settable, so it's not offered here either). Three plain bool properties instead of an
/// int-to-radio-button converter - simpler to bind, and there are only ever three of them.
/// </summary>
public class ResearchCostViewModel : ObservableObject
{
    private readonly TechLevel researchCosts;
    private readonly TechLevel.ResearchField field;

    public ResearchCostViewModel(string title, TechLevel researchCosts, TechLevel.ResearchField field)
    {
        Title = title;
        this.researchCosts = researchCosts;
        this.field = field;

        // A fresh Race's ResearchCosts defaults to 0 for every field (Race.cs: `new TechLevel(0)`)
        // - not one of the three selectable values - so nothing would show as selected until the
        // user touched it. Default to Standard (100), matching the original WinForms control's
        // own default selection.
        if (researchCosts[field] != 50 && researchCosts[field] != 100 && researchCosts[field] != 175)
        {
            researchCosts[field] = 100;
        }
    }

    public string Title { get; }

    private int Cost
    {
        get => researchCosts[field];
        set
        {
            if (researchCosts[field] != value)
            {
                researchCosts[field] = value;
                OnPropertyChanged(nameof(IsCheap));
                OnPropertyChanged(nameof(IsStandard));
                OnPropertyChanged(nameof(IsExpensive));
            }
        }
    }

    public bool IsCheap
    {
        get => Cost == 50;
        set { if (value) { Cost = 50; } }
    }

    public bool IsStandard
    {
        get => Cost == 100;
        set { if (value) { Cost = 100; } }
    }

    public bool IsExpensive
    {
        get => Cost == 175;
        set { if (value) { Cost = 175; } }
    }
}
