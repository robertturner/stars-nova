using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// One checkbox in Race Designer's LRT (lesser racial trait) list - wraps a single
/// <see cref="TraitEntry"/> against a <see cref="Race"/>'s <see cref="Race.Traits"/>
/// collection. Used both for the 14 traits shown together on the Traits tab and, separately,
/// for CF ("Cheap Factories") and ExtraTech, which the original WinForms dialog places on the
/// Production and Research tabs respectively rather than with the other LRTs.
/// </summary>
public class SecondaryTraitOptionViewModel : ObservableObject
{
    private readonly Race race;
    private readonly TraitEntry entry;
    private readonly Action onChanged;

    public SecondaryTraitOptionViewModel(Race race, TraitEntry entry, Action onChanged)
    {
        this.race = race;
        this.entry = entry;
        this.onChanged = onChanged;
    }

    public string Title => entry.Name;

    public string Description => entry.Description;

    public bool IsSelected
    {
        get => race.Traits.Contains(entry.Code);
        set
        {
            if (value == race.Traits.Contains(entry.Code))
            {
                return;
            }

            if (value)
            {
                race.Traits.Add(entry);
            }
            else
            {
                race.Traits.Remove(entry);
            }

            OnPropertyChanged();
            onChanged();
        }
    }
}
