using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One choice in a hull slot's component picker - either a real <see cref="Component"/>, or
/// "Empty" (<see cref="Component"/> null) to leave the slot unallocated.
/// </summary>
public class ComponentOptionViewModel
{
    public Component? Component { get; }

    public string Name => Component?.Name ?? "(Empty)";

    public ComponentOptionViewModel(Component? component)
    {
        Component = component;
    }
}
