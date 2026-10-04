using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Nova.Avalonia.ViewModels;
using Nova.Avalonia.Views.Panels;
using Nova.Client;
using Nova.Client.Shell;
using Nova.Common;

namespace Nova.Avalonia.Views;

/// <summary>
/// Desktop-only Window shell around the portable MainView content. Takes the ViewModel via its
/// constructor (rather than having it assigned to DataContext afterward, as before this split)
/// so it can subscribe to AboutRequested up front - opening a real modal Window here is
/// desktop-specific behavior a single-view host (Android) can't reuse, which is exactly why
/// MainViewModel raises an event instead of showing the About screen itself.
///
/// Also the main frame's controlled shutdown (client-ui-dialog-catalog.md "Main workspace
/// frame": unsaved orders ask Save / Don't Save / Cancel before the game window closes, whether
/// by the close box, File > Exit or File > New/Open/Close), the recent-file items (same load
/// path as Open; a missing file or one without an extension gives an error box), File > Print
/// Map, and the global hotkey relay (Nova.Client.Shell.HotkeyRelay: Escape, [ ], Delete /
/// Backspace and , . work wherever the focus is; the digit keys are relayed by the star map).
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? viewModel;

    // Set once the player has answered the shutdown confirmation (or nothing needed one), so
    // the Close() that follows is not asked about again.
    private bool closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        RestorePlacement();
        Closing += OnClosing;
        AddHandler(KeyDownEvent, OnRelayKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The window's last placement, from the client's config file
    /// (Nova.Client.WindowPlacement; client-interface.md persisted settings).</summary>
    private void RestorePlacement()
    {
        WindowPlacement? placement;
        try
        {
            placement = WindowPlacement.Parse(new Config()[WindowPlacement.PreferenceKey]);
        }
        catch (Exception)
        {
            placement = null;
        }

        if (placement == null)
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(placement.X, placement.Y);
        Width = placement.Width;
        Height = placement.Height;
        if (placement.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        try
        {
            bool maximized = WindowState == WindowState.Maximized;
            using var conf = new Config();
            conf[WindowPlacement.PreferenceKey] = new WindowPlacement(Position.X, Position.Y, (int)Width, (int)Height, maximized).Format();
        }
        catch (Exception)
        {
            // An unsaved placement only means the next session opens at the default size.
        }
    }

    public MainWindow(MainViewModel viewModel)
        : this()
    {
        Attach(viewModel);
    }

    private void Attach(MainViewModel newViewModel)
    {
        // The screen being replaced (a new turn, another game) must stop its own timers.
        viewModel?.Detach();
        viewModel = newViewModel;

        DataContext = newViewModel;
        newViewModel.AboutRequested += () => new AboutWindow().ShowDialog(this);
        newViewModel.RaceViewRequested += ShowRace;
        newViewModel.TurnAdvanced += freshState => Attach(new MainViewModel(freshState));
        newViewModel.StartScreenRequested += choice => _ = ShowStartScreenAsync(choice);
        newViewModel.ExitRequested += Close;
        newViewModel.RecentFileRequested += path => _ = OpenRecentFileAsync(path);
        newViewModel.PrintMapRequested += () => _ = PrintMapAsync();
    }

    // ---------------- shutdown confirmation ----------------

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!closeConfirmed && viewModel != null
            && ShutdownConfirmation.IsNeeded(gameOpen: true, viewModel.HasUnsavedChanges))
        {
            e.Cancel = true;
            _ = ConfirmThenCloseAsync();
            return;
        }

        viewModel?.Detach();
        SavePlacement();
    }

    private async Task ConfirmThenCloseAsync()
    {
        if (await ConfirmLeaveGameAsync())
        {
            Close();
        }
    }

    /// <summary>
    /// The shutdown confirmation: true when the game window may close (after saving, if the
    /// player chose Save). Asks only when the orders have unsaved changes.
    /// </summary>
    private async Task<bool> ConfirmLeaveGameAsync()
    {
        if (closeConfirmed || viewModel == null
            || !ShutdownConfirmation.IsNeeded(gameOpen: true, viewModel.HasUnsavedChanges))
        {
            closeConfirmed = true;
            return true;
        }

        ShutdownChoice choice = await ShellDialogs.AskShutdownAsync(this);
        (bool save, bool close) = ShutdownConfirmation.Resolve(choice);
        if (save)
        {
            try
            {
                viewModel.SaveOrders();
            }
            catch (Exception ex)
            {
                // Never close as if the save had worked.
                await ShellDialogs.ShowErrorAsync(this, $"Your orders could not be saved: {ex.Message}");
                return false;
            }
        }

        closeConfirmed = close;
        return close;
    }

    /// <summary>View > Race (F8): the race wizard on the player's own race, read-only
    /// (RaceDesignerView with isEditable false, working on its own copy), in a dialog window
    /// its Close button closes.
    /// SPEC GAP: the window title is dynamic string 271, whose text the specs do not give; the
    /// race's name is used.</summary>
    private void ShowRace(Race race)
    {
        var raceView = new RaceDesignerView(race, isEditable: false);
        var window = new Window
        {
            Title = race.Name,
            Content = raceView,
            Width = 900,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        raceView.RaceSavedOrCancelled += window.Close;
        window.ShowDialog(this);
    }

    /// <summary>File > New / Open / Custom Race Wizard / Close: back to the startup window (on
    /// the requested sub-screen), which becomes the desktop main window again, and close this
    /// game window - the same swap OpenGameWindow does in the other direction. Unsaved orders
    /// are asked about first; Cancel keeps the game open.</summary>
    private async Task ShowStartScreenAsync(string startupChoice)
    {
        if (!await ConfirmLeaveGameAsync())
        {
            return;
        }

        var startWindow = new OpenGameWindow(startupChoice);

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = startWindow;
        }

        startWindow.Show();
        Close();
    }

    // ---------------- recent files ----------------

    /// <summary>A File menu recent-file item (client-interface.md ids 4300-4308): checked, then
    /// loaded through the same path as Open (GameSession.Load), replacing this game.</summary>
    private async Task OpenRecentFileAsync(string path)
    {
        switch (RecentFiles.Check(path, File.Exists))
        {
            case RecentFileProblem.NoExtension:
                await ShellDialogs.ShowErrorAsync(this, $"\"{path}\" has no file extension, so it cannot be opened.");
                return;
            case RecentFileProblem.Missing:
                await ShellDialogs.ShowErrorAsync(this, $"\"{path}\" could not be found.");
                return;
        }

        if (!await ConfirmLeaveGameAsync())
        {
            return;
        }

        try
        {
            string? folder = Path.GetDirectoryName(path);
            ClientData state = GameSession.Load(folder ?? "", Path.GetFileNameWithoutExtension(path));
            closeConfirmed = false;
            Attach(new MainViewModel(state));
        }
        catch (Exception ex)
        {
            closeConfirmed = false;
            await ShellDialogs.ShowErrorAsync(this, $"Couldn't open \"{path}\": {ex.Message}");
        }
    }

    // ---------------- print map ----------------

    /// <summary>File > Print Map (command 213): the page-count dialog, then the map tiled over
    /// that many pages, each written as an image (MapPrintLayout); Cancel writes nothing.</summary>
    private async Task PrintMapAsync()
    {
        if (viewModel == null)
        {
            return;
        }

        (int Across, int Down)? pages = await ShellDialogs.AskPrintPagesAsync(this);
        if (pages == null)
        {
            return;
        }

        StarMapDocumentView? map = this.GetVisualDescendants().OfType<StarMapDocumentView>().FirstOrDefault();
        try
        {
            if (map == null)
            {
                throw new InvalidOperationException("The star map is not open.");
            }

            var written = map.WriteMapPages(viewModel.GameFolder, viewModel.RaceName, viewModel.TurnYear, pages.Value.Across, pages.Value.Down);
            viewModel.ReportStatus(written.Count == 1 ? $"Wrote {written[0]}" : $"Wrote {written.Count} pages to {Path.GetDirectoryName(written[0])}");
        }
        catch (Exception ex)
        {
            await ShellDialogs.ShowErrorAsync(this, $"{MapPrintLayout.FailureText} {ex.Message}");
        }
    }

    // ---------------- hotkey relay ----------------

    private void OnRelayKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel == null || e.Handled)
        {
            return;
        }

        RelayKey key = e.Key switch
        {
            Key.Escape => RelayKey.Escape,
            Key.Delete => RelayKey.Delete,
            Key.Back => RelayKey.Backspace,
            Key.OemOpenBrackets => RelayKey.LeftBracket,
            Key.OemCloseBrackets => RelayKey.RightBracket,
            Key.OemComma => RelayKey.Comma,
            Key.OemPeriod => RelayKey.Period,
            _ => RelayKey.Other,
        };
        if (key == RelayKey.Other)
        {
            // The digit keys are relayed by StarMapDocumentView; nothing else is intercepted.
            return;
        }

        bool chord = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0;
        bool textEntry = FocusManager?.GetFocusedElement() is TextBox;
        RelayAction action = HotkeyRelay.Classify(key, chord, textEntry, viewModel.IsRouteEditing);
        if (action != RelayAction.None && viewModel.HandleRelayAction(action))
        {
            e.Handled = true;
        }
    }
}
