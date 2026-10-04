using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Nova.Client.Map;
using Nova.Common;

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
/// The map's Find dialog (View > Find / Ctrl+F; behavior-specs-11/client-interface.md "Map
/// canvas" and the command table, ids 4200/4201): one query field, OK / Cancel / Help, matching
/// by the five-step rule (Nova.Client.Map.MapSearch) and centring the result on the map. An empty
/// query finds planet 1; a query that matches nothing leaves the dialog open. Windowed UI only -
/// the Android port has its own menu for this.
/// </summary>
public class StarMapSearchViewModel : ViewModelBase
{
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
            string limited = value ?? string.Empty;
            if (limited.Length > MapSearch.QueryMaxLength)
            {
                limited = limited.Substring(0, MapSearch.QueryMaxLength);
            }

            if (SetProperty(ref query, limited))
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

    /// <summary>Selects and centres the result, then closes.</summary>
    public IRelayCommand GoCommand { get; }

    public StarMapSearchViewModel(Func<IReadOnlyList<MapObjectEntry>> objects, Action<MapObjectEntry> focus)
    {
        this.objects = objects;
        this.focus = focus;
        OpenCommand = new RelayCommand(() => IsOpen = true);
        CloseCommand = new RelayCommand(() => IsOpen = false);
        GoCommand = new RelayCommand(Go, () => selectedResult != null);
    }

    private void Refresh()
    {
        FindResult found = MapSearch.Find(objects(), query, FleetNumber, IsOwnFleet);
        Results = found.Found
            ? new[] { new StarMapSearchResultViewModel(found.Entry) }
            : Array.Empty<StarMapSearchResultViewModel>();
        SelectedResult = results.FirstOrDefault();
        GoCommand.NotifyCanExecuteChanged();
    }

    // SEAM (reported): Nova stores no per-owner fleet number, so the low 32 bits of the fleet's
    // key plus one stands in for the original's displayed "Fleet #N".
    private static int FleetNumber(MapObjectEntry entry)
    {
        return entry.Item is Item item ? (int)item.Key.Id() + 1 : 0;
    }

    // Only the viewer's own (live) fleets can be found by number.
    private static bool IsOwnFleet(MapObjectEntry entry)
    {
        return entry.Item is Fleet;
    }

    private void Go()
    {
        if (selectedResult == null)
        {
            return;
        }

        focus(selectedResult.Entry);
        IsOpen = false;
    }
}
