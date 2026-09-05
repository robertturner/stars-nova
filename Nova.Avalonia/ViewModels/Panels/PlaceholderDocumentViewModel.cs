using Dock.Model.Mvvm.Controls;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// A stand-in for the Star Map document pane.
/// </summary>
public class PlaceholderDocumentViewModel : Document
{
    public string Description { get; }

    public PlaceholderDocumentViewModel(string id, string title, string description)
    {
        Id = id;
        Title = title;
        Description = description;
    }
}
