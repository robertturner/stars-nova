using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Nova.Avalonia.ViewModels;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.Views;

/// <summary>
/// The "startup choices" / "pick a race's .intel file" screen, as a plain UserControl so it can
/// be hosted either inside a desktop Window (OpenGameWindow) or as content in a single-view shell
/// (Android, once that exists). Owns the OpenGameViewModel itself (unlike MainView, which takes
/// its ViewModel from whoever hosts it) since nothing else needs to construct one first - and
/// forwards GameOpened rather than reacting to it itself, since "what happens next" is
/// host-specific: a desktop Window swap here, a content swap there.
/// </summary>
public partial class OpenGameView : UserControl
{
    private readonly OpenGameViewModel viewModel;

    public OpenGameView()
    {
        InitializeComponent();

        viewModel = new OpenGameViewModel();
        DataContext = viewModel;
        viewModel.GameOpened += clientState => GameOpened?.Invoke(clientState);
        viewModel.RaceDesignerRequested += ShowRaceDesigner;
        viewModel.NewGameRequested += ShowNewGame;
    }

    /// <summary>Builds a fresh RaceDesignerView each time (rather than reusing one instance) so
    /// picking "Race Designer" from the startup choices always starts from a brand new race, and
    /// hosts it in RaceDesignerHost - a plain ContentControl, since RaceDesignerView is a whole
    /// separate portable UserControl with its own Save/Load file-picker logic.</summary>
    private void ShowRaceDesigner()
    {
        var raceDesignerView = new RaceDesignerView();
        raceDesignerView.RaceSavedOrCancelled += () =>
        {
            RaceDesignerHost.Content = null;
            viewModel.BackToStartCommand.Execute(null);
        };
        RaceDesignerHost.Content = raceDesignerView;
    }

    /// <summary>Same pattern as ShowRaceDesigner - a fresh NewGameView each time, hosted in
    /// NewGameHost, forwarding GameOpened up through this view's own event.</summary>
    private void ShowNewGame()
    {
        var newGameView = new NewGameView();
        newGameView.GameOpened += clientState =>
        {
            NewGameHost.Content = null;
            GameOpened?.Invoke(clientState);
        };
        newGameView.CancelRequested += () =>
        {
            NewGameHost.Content = null;
            viewModel.BackToStartCommand.Execute(null);
        };
        NewGameHost.Content = newGameView;
    }

    public event Action<ClientData>? GameOpened;

    /// <summary>Steps back from the "browse for a race's .intel file" sub-screen to the initial
    /// Continue/Open/New Game choices, same as the on-screen "&lt; Back" button - see
    /// PlatformHooks.TryHandleBackRequest for why a host needs to be able to trigger this itself
    /// (Android's OS back button/gesture) rather than only ever firing from that button's own
    /// Click handler. Returns false (nothing to go back to) when already on the choices screen.</summary>
    public bool TryGoBack()
    {
        if (viewModel.ShowStartupChoices)
        {
            return false;
        }

        viewModel.BackToStartCommand.Execute(null);
        return true;
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a race's .intel file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Nova intel files") { Patterns = new[] { "*" + Global.IntelExtension } },
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        IStorageFile file = files[0];
        if (file.TryGetLocalPath() is string path)
        {
            viewModel.OnFileSelected(path);
            return;
        }

        // No real local path (e.g. Android's SAF picker hands back a content:// URI) - same
        // situation RaceDesignerView's Load/Save handlers work around via streams, but the rest
        // of this screen's pipeline (RaceOptions' sibling-file scan, GameSession.Load's later
        // .settings/.cstate paths) is all path-based, so there's no stream-only route through it.
        // Copy the picked file into a fresh temp folder under its own original file name instead,
        // so OnFileSelected gets a real local path to a real (if solitary) "game folder" to scan.
        try
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), "NovaOpenGame_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);
            string tempFilePath = Path.Combine(tempFolder, file.Name);

            using (Stream source = await file.OpenReadAsync())
            using (FileStream dest = File.Create(tempFilePath))
            {
                await source.CopyToAsync(dest);
            }

            viewModel.OnFileSelected(tempFilePath);
        }
        catch (Exception ex)
        {
            viewModel.ReportError("Couldn't open that file: " + ex.Message);
        }
    }
}
