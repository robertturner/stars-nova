using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nova.Avalonia.ViewModels;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.Views;

/// <summary>
/// New Game's view - a plain UserControl (same reasoning as OpenGameView/RaceDesignerView:
/// hostable inside either a desktop Window or a single-view Android shell). Owns its own
/// NewGameViewModel and handles the game-folder picker, the "New Race..." flow (hosting a
/// RaceDesignerView), and the "Load Race..." flow (a plain file picker), since the view model
/// has no TopLevel/StorageProvider access of its own.
/// </summary>
public partial class NewGameView : UserControl
{
    private readonly NewGameViewModel viewModel;

    private NewGamePlayerRowViewModel? raceDesignerTargetRow;

    public NewGameView()
    {
        InitializeComponent();

        viewModel = new NewGameViewModel();
        DataContext = viewModel;
        viewModel.RaceDesignerRequested += OnRaceDesignerRequested;
        viewModel.LoadRaceRequested += OnLoadRaceRequested;
    }

    /// <summary>Raised once a game has been successfully created and opened for a human player.</summary>
    public event Action<ClientData>? GameOpened
    {
        add => viewModel.GameOpened += value;
        remove => viewModel.GameOpened -= value;
    }

    /// <summary>Raised when the user cancels out of this screen.</summary>
    public event Action? CancelRequested
    {
        add => viewModel.CancelRequested += value;
        remove => viewModel.CancelRequested -= value;
    }

    private async void BrowseFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IStorageFolder? startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(viewModel.GameFolder);

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose New Game Folder",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
        });

        // A SAF content:// handle (common on Android when the default folder doesn't already
        // exist to seed SuggestedStartLocation) can't be opened via plain File/Directory APIs -
        // leave GameFolder on its existing value rather than writing something unusable.
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is string path)
        {
            viewModel.GameFolder = path;
        }
    }

    /// <summary>Hosts a fresh RaceDesignerView as an overlay-like sibling content, exactly like
    /// OpenGameView does for the startup screen's own "Race Designer" button - see that class's
    /// comment. Remembers which player row asked for this so the result can be routed back to
    /// the right row once the user is done. NewGameViewModel.ShowRaceDesignerOverlay (bound in
    /// the view to the main content's IsVisible) hides everything behind this while it's up -
    /// RaceDesignerView has no opaque background of its own, so without that the two screens
    /// render on top of each other.</summary>
    private void OnRaceDesignerRequested(NewGamePlayerRowViewModel row)
    {
        raceDesignerTargetRow = row;

        var raceDesignerView = new RaceDesignerView();
        raceDesignerView.RaceSavedOrCancelled += () =>
        {
            RaceDesignerHost.Content = null;
            if (raceDesignerTargetRow != null)
            {
                Race? savedRace = raceDesignerView.WasSaved ? raceDesignerView.CurrentRace : null;
                viewModel.CompleteNewRace(raceDesignerTargetRow, savedRace);
            }
        };
        RaceDesignerHost.Content = raceDesignerView;
    }

    private async void OnLoadRaceRequested(NewGamePlayerRowViewModel row)
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

        try
        {
            // A stream works regardless of whether the picked file is a real local path or
            // only reachable via a content:// handle (e.g. a location outside this app's own
            // storage on Android) - TryGetLocalPath()/File APIs can't open the latter at all.
            Race race;
            using (System.IO.Stream stream = await files[0].OpenReadAsync())
            {
                race = Race.LoadFromStream(stream);
            }

            viewModel.CompleteLoadRace(row, race);
        }
        catch (Exception ex)
        {
            // Report.Error is never actually wired to anything visible on this port (see its
            // own comment) - it would otherwise vanish into Console/logcat and the user would
            // see nothing happen at all, exactly the bug this reports through instead.
            viewModel.ReportError("Failed to load race file: " + ex.Message);
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        viewModel.Cancel();
    }
}
