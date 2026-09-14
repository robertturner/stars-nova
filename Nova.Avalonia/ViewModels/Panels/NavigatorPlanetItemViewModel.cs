using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Navigator's Planets list: a planet this empire owns.
/// </summary>
public class NavigatorPlanetItemViewModel
{
    public Star Star { get; }

    public string Name => Star.Name;

    public string Status => Star.Colonists > 0
        ? $"{Star.Colonists:N0} colonists"
        : "uncolonized";

    public NavigatorPlanetItemViewModel(Star star)
    {
        Star = star;
    }
}
