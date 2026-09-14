namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Shared mediator between the Navigator and Inspector panels: Navigator sets
/// <see cref="Selected"/> to whichever planet or fleet the user picked (a <c>Star</c> or
/// <c>Fleet</c> from the loaded <c>ClientData</c>), and Inspector observes it to refresh its
/// own display. Kept as a small standalone object (rather than a direct reference between
/// the two panel view models) since Dock panels are constructed independently by the
/// factory and shouldn't need to know about each other.
/// </summary>
public class SelectionService : ViewModelBase
{
    private object? selected;

    public object? Selected
    {
        get => selected;
        set => SetProperty(ref selected, value);
    }

    /// <summary>
    /// Announces that the currently-selected object's own state changed in place (e.g. a
    /// command was applied to it) without the selection itself changing - <see cref="Selected"/>
    /// only raises a change notification when the reference actually changes, so an in-place
    /// mutation needs this instead. Other panels (e.g. Navigator, whose list items aren't
    /// individually observable) can subscribe to <see cref="ViewModelBase.PropertyChanged"/> and
    /// treat a "Selected" notification with an unchanged value as "something about it changed -
    /// refresh your own display of it".
    /// </summary>
    public void NotifyMutated()
    {
        OnPropertyChanged(nameof(Selected));
    }
}
