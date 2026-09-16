using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Navigator panel: every planet and fleet this empire owns, tabbed. Selecting a row
/// publishes it to the shared <see cref="SelectionService"/> so the Inspector panel (and
/// eventually the Star Map) can show details for it.
/// </summary>
public class NavigatorViewModel : Tool
{
    private readonly ClientData clientState;
    private readonly SelectionService selection;

    private IReadOnlyList<NavigatorPlanetItemViewModel> planets = Array.Empty<NavigatorPlanetItemViewModel>();

    public IReadOnlyList<NavigatorPlanetItemViewModel> Planets
    {
        get => planets;
        private set => SetProperty(ref planets, value);
    }

    private IReadOnlyList<NavigatorFleetItemViewModel> fleets = Array.Empty<NavigatorFleetItemViewModel>();

    public IReadOnlyList<NavigatorFleetItemViewModel> Fleets
    {
        get => fleets;
        private set => SetProperty(ref fleets, value);
    }

    private NavigatorPlanetItemViewModel? selectedPlanet;

    public NavigatorPlanetItemViewModel? SelectedPlanet
    {
        get => selectedPlanet;
        set
        {
            if (SetProperty(ref selectedPlanet, value) && value != null)
            {
                selection.Selected = value.Star;
            }
        }
    }

    public HullViewerViewModel HullViewer { get; } = new HullViewerViewModel();

    private NavigatorFleetItemViewModel? selectedFleet;

    public NavigatorFleetItemViewModel? SelectedFleet
    {
        get => selectedFleet;
        set
        {
            if (SetProperty(ref selectedFleet, value) && value != null)
            {
                selection.Selected = value.Fleet;
            }
        }
    }

    public NavigatorViewModel(string id, string title, ClientData clientState, SelectionService selection)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        this.selection = selection;

        RebuildLists();

        // A command applied elsewhere (Inspector's fleet-orders/cargo/split-merge sections)
        // mutates EmpireData and calls SelectionService.NotifyMutated() rather than
        // reassigning Selected - rebuild both lists from scratch in response, since a plain
        // per-item refresh (as this used to do) only catches an existing fleet/planet's own
        // properties changing, not one being created or removed entirely (e.g. by a split
        // that spins off a new fleet, or leaves the original with zero ships).
        selection.PropertyChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionService.Selected))
        {
            RebuildLists();
        }
    }

    private void RebuildLists()
    {
        Planets = clientState.EmpireState.OwnedStars.Values
            .Select(star => new NavigatorPlanetItemViewModel(star))
            .OrderBy(p => p.Name)
            .ToList();

        Fleets = clientState.EmpireState.OwnedFleets.Values
            .Select(fleet => new NavigatorFleetItemViewModel(fleet))
            .OrderBy(f => f.Name)
            .ToList();
    }
}
