using CommunityToolkit.Mvvm.Input;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One entry in the selected star's "Fleets here" list (see InspectorViewModel.ShowStar) - lets
/// a player jump straight to one of the fleets sitting at this planet (to edit its waypoints,
/// etc.) without going by way of the Navigator's Fleets tab, the same shortcut "View Starbase"
/// already offers for the planet's own starbase.
/// </summary>
public class OrbitingFleetRowViewModel
{
    public string Name { get; }

    public IRelayCommand SelectCommand { get; }

    public OrbitingFleetRowViewModel(string name, System.Action onSelect)
    {
        Name = name;
        SelectCommand = new RelayCommand(onSelect);
    }
}
