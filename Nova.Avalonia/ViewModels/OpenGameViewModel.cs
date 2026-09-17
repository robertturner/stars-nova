using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// The Avalonia startup screen - mirrors NovaLauncher's relevant buttons (Nova\WinForms\
/// Launcher\NovaLauncher.cs): Continue Game, Open Game, New Game, Race Designer.
///
/// Starts on the four-choice screen (<see cref="ShowStartupChoices"/> true). "Open Game" swaps
/// to the existing browse-a-folder-then-pick-a-race panel below, which is the Avalonia equivalent
/// of NovaLauncher's own OpenFileDialog (filtered to *.intel) plus SelectRaceDialog's race list
/// (Nova\Client\SelectRaceDialog.cs / ClientData.SelectRace) folded into one - since the real
/// launcher's file dialog already pins down one specific race's .intel file directly, there's no
/// separate "pick a folder, then pick a race" step in practice. Once a file is picked, every
/// other *.intel file in the same folder is offered as an alternative race to open instead,
/// covering the same "which of the known races do you want to play" choice SelectRaceDialog
/// exists for, just adapted to how save folders are actually laid out (no .race files were found
/// in any real save folder used this session - only .intel/.cstate).
///
/// "Continue" reuses GameSession.Load via GameSession.FindContinuableGame() (backed by the same
/// Global.ClientStateKey conf entry NovaLauncher's own Continue button reads, though each app
/// currently has its own separate nova.conf - see GameSession's own comments) rather than
/// ClientData.Initialize()'s WinForms "continue" branch, which is confirmed broken - it never
/// actually derives an intel file path before reading one, so it always reads null.
///
/// "New Game" and "Race Designer" each swap in a whole separate native screen
/// (NewGameView/RaceDesignerView) rather than launching the old WinForms dialogs as a
/// subprocess (GameSession.TryLaunchNewGameWizard, since removed) - that only ever worked on
/// Windows, and Android has no Nova.exe/subprocess concept at all.
/// </summary>
public class OpenGameViewModel : ViewModelBase
{
    private enum Screen
    {
        Choices,
        OpenGameBrowse,
        RaceDesigner,
        NewGame,
    }

    private string? gameFolder;

    private Screen currentScreen = Screen.Choices;

    public bool ShowStartupChoices => currentScreen == Screen.Choices;

    public bool ShowOpenGameBrowse => currentScreen == Screen.OpenGameBrowse;

    public bool ShowRaceDesigner => currentScreen == Screen.RaceDesigner;

    public bool ShowNewGame => currentScreen == Screen.NewGame;

    /// <summary>Hides the shared "Stars! Nova" title while a full-screen sub-screen (Race
    /// Designer, New Game) is showing its own title instead.</summary>
    public bool ShowStartupHeader => currentScreen == Screen.Choices || currentScreen == Screen.OpenGameBrowse;

    private bool canContinue;

    public bool CanContinue
    {
        get => canContinue;
        private set => SetProperty(ref canContinue, value);
    }

    private string continueStatusMessage = "";

    public string ContinueStatusMessage
    {
        get => continueStatusMessage;
        private set => SetProperty(ref continueStatusMessage, value);
    }

    private (string GameFolder, string RaceName)? continuableGame;

    private IReadOnlyList<string> raceOptions = Array.Empty<string>();

    public IReadOnlyList<string> RaceOptions
    {
        get => raceOptions;
        private set
        {
            if (SetProperty(ref raceOptions, value))
            {
                HasRaceOptions = value.Count > 0;
            }
        }
    }

    private bool hasRaceOptions;

    public bool HasRaceOptions
    {
        get => hasRaceOptions;
        private set => SetProperty(ref hasRaceOptions, value);
    }

    private string? selectedRace;

    public string? SelectedRace
    {
        get => selectedRace;
        set
        {
            if (SetProperty(ref selectedRace, value))
            {
                OpenCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string statusMessage = "Pick a race's .intel file from an existing game folder to open it.";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public IRelayCommand OpenCommand { get; }

    public IRelayCommand ContinueCommand { get; }

    public IRelayCommand NewGameCommand { get; }

    public IRelayCommand ShowOpenGameCommand { get; }

    public IRelayCommand RaceDesignerCommand { get; }

    public IRelayCommand BackToStartCommand { get; }

    /// <summary>
    /// Lets the user retrieve diagnostics without adb, from the one screen guaranteed reachable
    /// even when "Continue"/"Open" itself is what crashes (see PlatformHooks.ShareCrashLog's own
    /// comment) - the in-game burger menu's equivalent entries never get a chance to render in
    /// that scenario. "Crash Log" shares whatever nova-avalonia-crash.log/its desktop equivalent
    /// holds; "Save File" shares the raw .intel XML of whatever game Continue would have loaded,
    /// using the same GameFolder/RaceName FindContinuableGame already resolved - it reads that
    /// file directly rather than routing through GameSession.Load, so it works even when loading
    /// itself is what's crashing.
    /// </summary>
    public IRelayCommand ShareCrashLogCommand { get; }

    public IRelayCommand ShareContinuableSaveCommand { get; }

    /// <summary>Raised once a game has been successfully loaded, carrying the ready ClientData.</summary>
    public event Action<ClientData>? GameOpened;

    /// <summary>Raised when the user picks "Race Designer" from the startup choices - the view's
    /// code-behind handles actually constructing/hosting a RaceDesignerView, since that's a
    /// whole separate portable UserControl with its own file-picker concerns, not something this
    /// class should own directly.</summary>
    public event Action? RaceDesignerRequested;

    /// <summary>Raised when the user picks "New Game..." - same reasoning as
    /// RaceDesignerRequested, the view hosts a whole separate NewGameView.</summary>
    public event Action? NewGameRequested;

    public OpenGameViewModel()
    {
        OpenCommand = new RelayCommand(Open, () => gameFolder != null && SelectedRace != null);
        ContinueCommand = new RelayCommand(Continue, () => CanContinue);
        NewGameCommand = new RelayCommand(() =>
        {
            SwitchTo(Screen.NewGame);
            NewGameRequested?.Invoke();
        });
        ShowOpenGameCommand = new RelayCommand(() => SwitchTo(Screen.OpenGameBrowse));
        RaceDesignerCommand = new RelayCommand(() =>
        {
            SwitchTo(Screen.RaceDesigner);
            RaceDesignerRequested?.Invoke();
        });
        BackToStartCommand = new RelayCommand(() => SwitchTo(Screen.Choices));
        ShareCrashLogCommand = new RelayCommand(() =>
        {
            ContinueStatusMessage = PlatformHooks.ShareCrashLog()
                ? ContinueStatusMessage
                : "No crash log found - either nothing has crashed since this fix shipped, or this platform hasn't wired sharing.";
        });
        ShareContinuableSaveCommand = new RelayCommand(
            ShareContinuableSave,
            () => continuableGame != null);

        continuableGame = GameSession.FindContinuableGame();
        CanContinue = continuableGame != null;
        ContinueStatusMessage = continuableGame != null
            ? $"Continue \"{continuableGame.Value.RaceName}\" in \"{continuableGame.Value.GameFolder}\"."
            : "No previous game found to continue.";
    }

    private void SwitchTo(Screen screen)
    {
        if (currentScreen == screen)
        {
            return;
        }

        currentScreen = screen;
        OnPropertyChanged(nameof(ShowStartupChoices));
        OnPropertyChanged(nameof(ShowOpenGameBrowse));
        OnPropertyChanged(nameof(ShowRaceDesigner));
        OnPropertyChanged(nameof(ShowNewGame));
        OnPropertyChanged(nameof(ShowStartupHeader));
    }

    private void ShareContinuableSave()
    {
        if (continuableGame == null)
        {
            return;
        }

        string text;
        try
        {
            text = GameSession.BuildShareableSaveText(continuableGame.Value.GameFolder, continuableGame.Value.RaceName);
        }
        catch (Exception ex)
        {
            ContinueStatusMessage = $"Couldn't read the save file: {ex.Message}";
            return;
        }

        ContinueStatusMessage = PlatformHooks.ShareText(text)
            ? ContinueStatusMessage
            : "Sharing isn't available on this platform.";
    }

    private void Continue()
    {
        if (continuableGame == null)
        {
            return;
        }

        try
        {
            ClientData clientState = GameSession.Load(continuableGame.Value.GameFolder, continuableGame.Value.RaceName);
            GameOpened?.Invoke(clientState);
        }
        catch (Exception ex)
        {
            ContinueStatusMessage = $"Couldn't continue that game: {ex.Message}";
        }
    }

    /// <summary>
    /// Called by the view's code-behind once the platform file picker resolves - this class has
    /// no TopLevel/StorageProvider access of its own, matching how the other panels in this app
    /// keep platform-dialog concerns in the view layer.
    /// </summary>
    public void OnFileSelected(string intelFilePath)
    {
        try
        {
            gameFolder = Path.GetDirectoryName(intelFilePath);
            if (gameFolder == null)
            {
                StatusMessage = "Couldn't determine the game folder from that file.";
                return;
            }

            string pickedRace = Path.GetFileNameWithoutExtension(intelFilePath);

            RaceOptions = new DirectoryInfo(gameFolder).GetFiles("*" + Global.IntelExtension)
                .Select(file => Path.GetFileNameWithoutExtension(file.Name))
                .OrderBy(name => name)
                .ToList();

            SelectedRace = RaceOptions.Contains(pickedRace) ? pickedRace : RaceOptions.FirstOrDefault();
            StatusMessage = $"Found {RaceOptions.Count} race(s) in \"{gameFolder}\".";
            OpenCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't read that folder: {ex.Message}";
        }
    }

    /// <summary>
    /// Lets the view's code-behind surface a problem it hit (e.g. a Browse file picker that
    /// failed to copy a content:// file to a temp local path on Android) through this screen's
    /// own visible status line - see RaceDesignerViewModel.ReportError's own comment for why
    /// Report.Error itself is never actually visible on this port.
    /// </summary>
    public void ReportError(string message)
    {
        StatusMessage = message;
    }

    private void Open()
    {
        if (gameFolder == null || SelectedRace == null)
        {
            return;
        }

        try
        {
            ClientData clientState = GameSession.Load(gameFolder, SelectedRace);
            GameOpened?.Invoke(clientState);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open that game: {ex.Message}";
        }
    }
}
