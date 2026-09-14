using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nova.Avalonia.ViewModels;
using Nova.Common;

namespace Nova.Avalonia.Views;

/// <summary>
/// Race Designer's view - a plain UserControl (same reasoning as OpenGameView: hostable inside
/// either a desktop Window or a single-view Android shell). Owns its own RaceDesignerViewModel
/// (constructible standalone, since it can also be opened from inside New Game via its own
/// "New Race" button) and handles the actual file pickers for Save/Load, since the view model
/// has no TopLevel/StorageProvider access of its own.
/// </summary>
public partial class RaceDesignerView : UserControl
{
    private RaceDesignerViewModel viewModel = null!;

    public RaceDesignerView()
    {
        InitializeComponent();

        SetViewModel(new RaceDesignerViewModel());
    }

    /// <summary>Raised once the race has been saved, or the user cancels out of this screen.
    /// Forwarded from whichever RaceDesignerViewModel is current - a Load can swap it out for a
    /// freshly constructed one wrapping the loaded Race, so this event is re-wired to the new
    /// instance rather than proxied live, keeping any external subscription intact across that
    /// swap.</summary>
    public event Action? RaceSavedOrCancelled;

    /// <summary>The in-progress race's current name, whether or not it was actually saved - a
    /// host that only cares about a genuine save (e.g. New Game's "New Race..." button) should
    /// check <see cref="WasSaved"/> first.</summary>
    public string CurrentRaceName => viewModel.Name;

    /// <summary>The race object itself, for a host that wants to add it directly to its own
    /// known-races list (e.g. New Game) without re-scanning the default race folder on disk -
    /// which would miss a race saved somewhere else via this screen's own Save dialog. Only
    /// meaningful when <see cref="WasSaved"/> is true.</summary>
    public Race CurrentRace => viewModel.Race;

    /// <summary>True once the race was actually saved (not just edited then cancelled) -
    /// RaceSavedOrCancelled fires for both, so a host that only cares about a genuine save
    /// should check this first.</summary>
    public bool WasSaved => viewModel.WasSaved;

    private void SetViewModel(RaceDesignerViewModel newViewModel)
    {
        viewModel = newViewModel;
        DataContext = viewModel;
        viewModel.SaveFileRequested += OnSaveFileRequested;
        viewModel.RaceSavedOrCancelled += () => RaceSavedOrCancelled?.Invoke();
    }

    private async void OnSaveFileRequested(string suggestedFileName)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IStorageFolder? startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(RaceDesignerViewModel.DefaultRaceFolder);

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Race",
            SuggestedFileName = suggestedFileName,
            SuggestedStartLocation = startFolder,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Nova race files") { Patterns = new[] { "*" + Global.RaceExtension } },
            },
        });

        if (file == null)
        {
            return;
        }

        try
        {
            // A stream works regardless of whether the destination is a real local path or only
            // reachable via a content:// handle - see CompleteSave's own comment for why relying
            // on TryGetLocalPath() here previously left a 0-byte file behind on a real device.
            using (System.IO.Stream stream = await file.OpenWriteAsync())
            {
                viewModel.CompleteSave(stream);
            }
        }
        catch (Exception ex)
        {
            viewModel.ReportError("Couldn't save that race: " + ex.Message);
        }
    }

    private async void LoadButton_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IStorageFolder? startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(RaceDesignerViewModel.DefaultRaceFolder);

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Race",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Nova race files") { Patterns = new[] { "*" + Global.RaceExtension } },
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        bool loadedOk;
        RaceDesignerViewModel? loaded;
        string error;
        try
        {
            // A stream works regardless of whether the picked file is a real local path or
            // only reachable via a content:// handle (e.g. a location outside this app's own
            // storage on Android) - TryGetLocalPath()/File APIs can't open the latter at all.
            using (System.IO.Stream stream = await files[0].OpenReadAsync())
            {
                loadedOk = RaceDesignerViewModel.TryLoadFromStream(stream, out loaded, out error);
            }
        }
        catch (Exception ex)
        {
            loadedOk = false;
            loaded = null;
            error = ex.Message;
        }

        if (loadedOk)
        {
            SetViewModel(loaded!);
        }
        else
        {
            // Report.Error is never actually wired to anything visible on this port (see its
            // own comment) - it would otherwise vanish into Console/logcat and the user would
            // see nothing happen at all, exactly the bug this reports through instead.
            viewModel.ReportError("Failed to load race file: " + error);
        }
    }
}
