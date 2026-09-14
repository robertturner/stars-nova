namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One entry in the in-game manual's topic list - mirrors HelpForm.cs's Topic class, reading the
/// same shared HelpContent/topics.tsv manifest both clients ship.
/// </summary>
public class HelpTopicViewModel
{
    public int Number { get; }

    public string Title { get; }

    public HelpTopicViewModel(int number, string title)
    {
        Number = number;
        Title = title;
    }
}
