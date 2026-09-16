using System;
using CommunityToolkit.Mvvm.Input;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// One row in MobileMainViewModel's burger menu - a section to switch to, shown directly as a
/// tappable row rather than an entry in a dropdown (see MobileMainViewModel's own comment on
/// why the ComboBox was replaced). IsSelected highlights whichever section is currently showing.
/// </summary>
public class MobileMenuEntryViewModel
{
    public string Label { get; }

    public bool IsSelected { get; }

    public IRelayCommand SelectCommand { get; }

    public MobileMenuEntryViewModel(string label, bool isSelected, Action onSelect)
    {
        Label = label;
        IsSelected = isSelected;
        SelectCommand = new RelayCommand(onSelect);
    }
}
