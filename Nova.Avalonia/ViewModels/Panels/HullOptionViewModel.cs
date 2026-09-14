using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>One choice in the hull picker - a master hull Component from AvailableComponents.</summary>
public class HullOptionViewModel
{
    public Component Component { get; }

    public string Name => Component.Name;

    public HullOptionViewModel(Component component)
    {
        Component = component;
    }
}
