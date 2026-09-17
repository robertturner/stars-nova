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

    // Either a real (owned) Star or an unowned/neutral StarIntel report - a scout can easily be
    // sitting in orbit at a star this empire doesn't own, which only ever has a report, never a
    // real Star object (see InspectorViewModel.ShowStarReport's own fix for the same gap).
    private Mappable? anchorStar;

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
        Mappable? newAnchor = selection.Selected switch
        {
            Star star => star,
            StarIntel report => report,
            Fleet { InOrbit: not null } fleet => fleet.InOrbit,
            _ => null,
        };

        anchorStar = newAnchor;
        RebuildOptions();

        // Sync which entry reads as "selected" without going through the property setter above -
        // that setter's job is to PUSH a user pick back into SelectionService; this is the
        // reverse direction (reflecting a selection that came from the map, Navigator, or
        // Inspector), and re-entering it would just reassign selection.Selected to itself.
        selectedOption = options.FirstOrDefault(o => ReferenceEquals(o.Selectable, selection.Selected));
        OnPropertyChanged(nameof(SelectedOption));
    }

    private void RebuildOptions()
    {
        List<MapSelectionSwitcherOptionViewModel> newOptions = BuildOptions();

        // Only actually reassign (and so rebind/flicker the ComboBox) when the options genuinely
        // changed - both WHICH objects are listed and what each one is currently labeled.
        // Without this check, this would need to run unconditionally on every Refresh() - e.g.
        // merely switching selection between the star and one of its own already-listed fleets
        // fires this same Refresh() but shouldn't visibly rebuild the list. It DOES need to
        // actually run every time (not just when the anchor reference changes, as before) - an
        // in-place mutation that leaves the anchor unchanged but changes who's in orbit (e.g. a
        // fleet Split creating a new fleet at the same star) previously left Options stale until
        // the user navigated away and back, since that was the only way to force the anchor
        // reference itself to change. Comparing Name alongside Selectable catches the same shape
        // of staleness for a rename (Split/Merge's own "Rename this fleet", or Orders' own rename
        // box) - the object stays the same, only its label changes, which a Selectable-only
        // comparison would never notice.
        bool unchanged = newOptions.Count == options.Count
            && newOptions.Zip(options, (a, b) => ReferenceEquals(a.Selectable, b.Selectable) && a.Name == b.Name).All(same => same);

        if (!unchanged)
        {
            Options = newOptions;
        }
    }

    private List<MapSelectionSwitcherOptionViewModel> BuildOptions()
    {
        if (anchorStar == null)
        {
            return new List<MapSelectionSwitcherOptionViewModel>();
        }

        var list = new List<MapSelectionSwitcherOptionViewModel>
        {
            new(anchorStar.Name + " (Planet)", anchorStar),
        };

        // anchorStar may be a real (owned) Star or an unowned StarIntel report - both carry their
        // own Starbase field (see StarIntel's own comment), but neither is on their common
        // Mappable base, so it's read per-type here rather than via anchorStar directly.
        Fleet? anchorStarbase = anchorStar switch
        {
            Star star => star.Starbase,
            StarIntel report => report.Starbase,
            _ => null,
        };

        foreach (Fleet fleet in clientState.EmpireState.OwnedFleets.Values)
        {
            if (fleet.InOrbit != null && fleet.InOrbit.Name == anchorStar.Name && fleet != anchorStarbase)
            {
                list.Add(new MapSelectionSwitcherOptionViewModel(fleet.Name, fleet));
            }
        }

        return list;
    }
}
