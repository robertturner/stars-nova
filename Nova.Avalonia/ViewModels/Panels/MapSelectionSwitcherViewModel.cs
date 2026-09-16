using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One entry in <see cref="MapSelectionSwitcherViewModel"/>.Options - either the anchor star
/// itself, or one of the fleets sitting in orbit there.
/// </summary>
public class MapSelectionSwitcherOptionViewModel
{
    public string Name { get; }

    public object Selectable { get; }

    public MapSelectionSwitcherOptionViewModel(string name, object selectable)
    {
        Name = name;
        Selectable = selectable;
    }
}

/// <summary>
/// Android's Map screen stacks the Star Map, Inspector, and Production in one column (see
/// MobileMainViewModel's own comment) with no Navigator tab alongside them - this is the "easy
/// way to go between the star and any fleets" that screen asked for instead: a single ComboBox
/// switching the shared SelectionService between whichever star is currently anchoring the
/// selection and any of this empire's own fleets sitting in orbit there (the starbase excluded -
/// it already has its own "View Starbase" button in the Inspector), without needing to go by way
/// of a separate Navigator screen.
///
/// Deliberately its own small reactive object rather than reusing InspectorViewModel's own
/// OrbitingFleets list: that list gets reset to empty the moment the user selects one of the
/// fleets in it (ShowFleet resets everything ShowStar populated), which would make the switcher
/// itself vanish as soon as it's used to switch away from the star. This tracks the "anchor"
/// star independently of exactly which co-located object is momentarily selected, and only
/// rebuilds Options when that anchor itself changes - not on every switch between the star and
/// one of its own fleets.
/// </summary>
public class MapSelectionSwitcherViewModel : ViewModelBase
{
    private readonly ClientData clientState;
    private readonly SelectionService selection;

    private Star? anchorStar;

    private IReadOnlyList<MapSelectionSwitcherOptionViewModel> options = Array.Empty<MapSelectionSwitcherOptionViewModel>();

    public IReadOnlyList<MapSelectionSwitcherOptionViewModel> Options
    {
        get => options;
        private set
        {
            if (SetProperty(ref options, value))
            {
                OnPropertyChanged(nameof(HasOptions));
            }
        }
    }

    /// <summary>Only worth showing once there's an actual choice - a lone star with no fleets
    /// in orbit has nothing to switch to.</summary>
    public bool HasOptions => options.Count > 1;

    private MapSelectionSwitcherOptionViewModel? selectedOption;

    public MapSelectionSwitcherOptionViewModel? SelectedOption
    {
        get => selectedOption;
        set
        {
            if (SetProperty(ref selectedOption, value) && value != null && !ReferenceEquals(value.Selectable, selection.Selected))
            {
                selection.Selected = value.Selectable;
            }
        }
    }

    public MapSelectionSwitcherViewModel(ClientData clientState, SelectionService selection)
    {
        this.clientState = clientState;
        this.selection = selection;

        selection.PropertyChanged += OnSelectionChanged;
        Refresh();
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionService.Selected))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        Star? newAnchor = selection.Selected switch
        {
            Star star => star,
            Fleet { InOrbit: Star fleetStar } => fleetStar,
            _ => null,
        };

        if (!ReferenceEquals(newAnchor, anchorStar))
        {
            anchorStar = newAnchor;
            RebuildOptions();
        }

        // Sync which entry reads as "selected" without going through the property setter above -
        // that setter's job is to PUSH a user pick back into SelectionService; this is the
        // reverse direction (reflecting a selection that came from the map, Navigator, or
        // Inspector), and re-entering it would just reassign selection.Selected to itself.
        selectedOption = options.FirstOrDefault(o => ReferenceEquals(o.Selectable, selection.Selected));
        OnPropertyChanged(nameof(SelectedOption));
    }

    private void RebuildOptions()
    {
        if (anchorStar == null)
        {
            Options = Array.Empty<MapSelectionSwitcherOptionViewModel>();
            return;
        }

        var list = new List<MapSelectionSwitcherOptionViewModel>
        {
            new(anchorStar.Name + " (Planet)", anchorStar),
        };

        foreach (Fleet fleet in clientState.EmpireState.OwnedFleets.Values)
        {
            if (fleet.InOrbit == anchorStar && fleet != anchorStar.Starbase)
            {
                list.Add(new MapSelectionSwitcherOptionViewModel(fleet.Name, fleet));
            }
        }

        Options = list;
    }
}
