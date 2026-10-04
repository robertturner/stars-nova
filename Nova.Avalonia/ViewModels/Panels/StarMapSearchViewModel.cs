using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Nova.Client.Map;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>One Find result row.</summary>
public class StarMapSearchResultViewModel
{
    public StarMapSearchResultViewModel(MapObjectEntry entry)
    {
        Entry = entry;
        Display = entry.Name + " (" + MapObjectText.CategoryName(entry.Kind) + ")";
    }

    public MapObjectEntry Entry { get; }

    public string Display { get; }
}

/// <summary>
/// The map's Find dialog (View > Find / Ctrl+F; behavior-specs-10/client-interface.md "Map
/// canvas" and the command table, ids 4200/4201): a query over the objects this empire knows
/// about, optionally filtered by kind, whose chosen result becomes the selection and is centred
/// on the map. The matching itself is Nova.Client.Map.MapSearch (see its SPEC GAP note).
/// Windowed UI only - the Android port has its own menu for this.
/// </summary>
public class StarMapSearchViewModel : ViewModelBase
{
    /// <summary>The kind filter's choices, index 0 = any kind.</summary>
    public static IReadOnlyList<string> KindLabels { get; } = new[] { "Anything", "Planets", "Fleets", "Minefields", "Wormholes" };

    private static readonly MapObjectKind?[] KindValues = { null, MapObjectKind.Planet, MapObjectKind.Fleet, MapObjectKind.Minefield, MapObjectKind.Wormhole };

    private readonly Func<IReadOnlyList<MapObjectEntry>> objects;
    private readonly Action<MapObjectEntry> focus;

    private bool isOpen;

    public bool IsOpen
    {
        get => isOpen;
        set
        {
            if (SetProperty(ref isOpen, value) && value)
            {
                Refresh();
            }
        }
    }

    private string query = string.Empty;

    public string Query
    {
        get => query;
        set
        {
            if (SetProperty(ref query, value ?? string.Empty))
            {
                Refresh();
            }
        }
    }

    private int kindIndex;

    public int KindIndex
    {
        get => kindIndex;
        set
        {
            if (SetProperty(ref kindIndex, Math.Clamp(value, 0, KindValues.Length - 1)))
            {
                Refresh();
            }
        }
    }

    private IReadOnlyList<StarMapSearchResultViewModel> results = Array.Empty<StarMapSearchResultViewModel>();

    public IReadOnlyList<StarMapSearchResultViewModel> Results
    {
        get => results;
        private set
        {
            if (SetProperty(ref results, value))
            {
                OnPropertyChanged(nameof(NoResult));
            }
        }
    }

    /// <summary>"Returns a selected result or no result" - shown when the query matches nothing.</summary>
    public bool NoResult => results.Count == 0;

    private StarMapSearchResultViewModel? selectedResult;

    public StarMapSearchResultViewModel? SelectedResult
    {
        get => selectedResult;
        set
        {
            if (SetProperty(ref selectedResult, value))
            {
                GoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand CloseCommand { get; }

    /// <summary>Selects and centres the chosen result (or the best match when none is
    /// highlighted), then closes.</summary>
    public IRelayCommand GoCommand { get; }

    public StarMapSearchViewModel(Func<IReadOnlyList<MapObjectEntry>> objects, Action<MapObjectEntry> focus)
    {
        this.objects = objects;
        this.focus = focus;
        OpenCommand = new RelayCommand(() => IsOpen = true);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        GoCommand = new RelayCommand(Go, () => selectedResult != null || results.Count > 0);
    }

    private void Refresh()
    {
        Results = MapSearch.Find(objects(), query, KindValues[kindIndex])
            .Select(entry => new StarMapSearchResultViewModel(entry))
            .ToList();
        SelectedResult = results.FirstOrDefault();
        GoCommand.NotifyCanExecuteChanged();
    }

    private void Go()
    {
        StarMapSearchResultViewModel? chosen = selectedResult ?? results.FirstOrDefault();
        if (chosen == null)
        {
            return;
        }

        focus(chosen.Entry);
        IsOpen = false;
    }
}
