using Dock.Model.Mvvm.Controls;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// A stand-in for a not-yet-built dock panel. Shows just its own title and a short
/// description of what will eventually live here, so the docking shell can be built
/// and exercised (moved, floated, collapsed, resized) before any panel is wired to
/// real game data.
/// </summary>
public class PlaceholderToolViewModel : Tool
{
    public string Description { get; }

    public PlaceholderToolViewModel(string id, string title, string description)
    {
        Id = id;
        Title = title;
        Description = description;
    }
}
