using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Nova.Client;
using Nova.Common;
using Nova.Server.NewGame;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// New Game's main view model - the native Avalonia replacement for
/// Nova/WinForms/NewGameWizard.cs, which this class's Save flow ports faithfully except where
/// noted below. Follows the same pattern as RaceDesignerViewModel/OpenGameViewModel: no
/// TopLevel/StorageProvider access of its own (folder pickers live in NewGameView's
/// code-behind), and raises events instead of navigating directly.
///
/// Deliberately NOT ported, matching the plan's own scope call-outs (both confirmed dead/
/// non-functional in the original, not worth rebuilding):
/// - NumberOfStars: disabled in the original UI itself ("Unavailable in this version"), and
///   write-only in GameSettings (never read anywhere in the codebase).
/// - "Browse for a custom AI executable": PlayerSettings.AiProgram for anything but exactly
///   "Human" is written but discarded downstream (NovaConsole.RunAI() never reads it), so an AI
///   picker beyond "Human"/"Default AI" would be UI with no effect.
/// - An AI personality picker: DefaultAi.cs supports a "-n" personality code, but nothing in
///   this codebase plumbs a chosen value from PlayerSettings through to that launch - a
///   separate, later task.
/// </summary>
public class NewGameViewModel : ViewModelBase
{
    private int selectedTabIndex;

    /// <summary>
    /// Which of the three tabs ("Options"/"Players"/"Victory") is showing. Race Designer's own
    /// ComboBox (see RaceDesignerViewModel.Page's own comment) exists because a default
    /// FluentTheme TabControl's strip wraps onto multiple stacked rows once its header text no
    /// longer fits one line, crushing the page content beneath it - confirmed there live with six
    /// sections. This screen only has three, but even three of the ORIGINAL, longer labels ("Game
    /// Options"/"Players"/"Victory Conditions") still wrapped to three stacked rows at phone width
    /// on the Nova_Test emulator - a default TabItem's fixed padding adds up fast at this width
    /// regardless of tab count. Fixed here by shortening the labels (see NewGameView.axaml) and
    /// giving TabItem a tighter Padding/FontSize in that view's own Styles - confirmed live
    /// afterward to hold to one line.
    /// </summary>
    public int SelectedTabIndex
    {
        get => selectedTabIndex;
        set => SetProperty(ref selectedTabIndex, value);
    }

    private readonly Dictionary<string, Race> knownRaces = new();

    private string gameName;

    private string gameFolder;

    private bool gameFolderManuallySet;

    private string statusMessage = string.Empty;

    private int seed;

    public NewGameViewModel()
    {
        // Commands are created first - AddPlayerRow (called below, while seeding the initial
        // player list) notifies CreateGameCommand of a can-execute change, which would null-ref
        // if that command didn't exist yet.
        AddPlayerCommand = new RelayCommand(() => AddPlayerRow(RaceOptions.FirstOrDefault()), () => RaceOptions.Count > 0);
        RemovePlayerCommand = new RelayCommand<NewGamePlayerRowViewModel>(RemovePlayerRow);
        MoveUpCommand = new RelayCommand<NewGamePlayerRowViewModel>(row => Move(row, -1));
        MoveDownCommand = new RelayCommand<NewGamePlayerRowViewModel>(row => Move(row, 1));
        RandomizeSeedCommand = new RelayCommand(() => Seed = Environment.TickCount);
        CreateGameCommand = new RelayCommand(CreateGame, CanCreateGame);

        foreach (Race race in FileSearcher.GetAvailableRaces())
        {
            knownRaces[race.Name] = race;
        }

        RaceOptions = new ObservableCollection<string>(knownRaces.Keys);

        Players = new ObservableCollection<NewGamePlayerRowViewModel>();

        // Resolve the seed before anything else that draws on randomness (including the default
        // player-race shuffle just below), so the whole screen - not just the galaxy
        // Gameinitializer.Initialize eventually generates - is reproducible from one value. See
        // GameSettings.Seed.
        seed = GameSettings.Data.Seed ?? Environment.TickCount;

        // Seed 2 players with distinct random races, same as the original constructor - but
        // not more than the number of races actually available.
        Random rand = new Random(seed);
        List<string> remainingRaces = new List<string>(RaceOptions);
        int initialPlayers = Math.Min(2, remainingRaces.Count);
        for (int i = 0; i < initialPlayers; i++)
        {
            string raceName = remainingRaces[rand.Next(remainingRaces.Count)];
            remainingRaces.Remove(raceName);
            AddPlayerRow(raceName);
        }

        gameName = GameSettings.Data.GameName;
        gameFolder = ComputeDefaultFolder(gameName);

        PlanetsOwned = new VictoryConditionRowViewModel("Owns the following number of planets (%)", GameSettings.Data.PlanetsOwned, 0, 100);
        TechLevels = new VictoryConditionRowViewModel("Attains the following tech level", GameSettings.Data.TechLevels, 0, 10000);
        NumberOfFields = new VictoryConditionRowViewModel("In the following number of fields", GameSettings.Data.NumberOfFields, 0, 6);
        ProductionCapacity = new VictoryConditionRowViewModel("Has production capacity of (in K resources)", GameSettings.Data.ProductionCapacity, 0, 10000);
        CapitalShips = new VictoryConditionRowViewModel("Number of capital ships", GameSettings.Data.CapitalShips, 0, 10000);
        HighestScore = new VictoryConditionRowViewModel("Has the highest score after (years)", GameSettings.Data.HighestScore, 0, 10000);
        TotalScore = new VictoryConditionRowViewModel("Exceeds a score of", GameSettings.Data.TotalScore, 0, 10000);
        // SecondPlaceScore: VictoryCheck.cs already checks this (a player's score must exceed
        // the runner-up's score times this factor), but the original WinForms wizard never had
        // a control for it at all - not merely unwired, genuinely absent from the dialog.
        SecondPlaceScore = new VictoryConditionRowViewModel("Exceeds second place's score by a factor of", GameSettings.Data.SecondPlaceScore, 0, 100);
    }

    public static string ComputeDefaultFolder(string gameName)
    {
        string root = PlatformHooks.GamesRootOverride?.Invoke()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Stars! Nova");
        return Path.Combine(root, gameName);
    }

    /// <summary>Shared by every player row's race dropdown - adding to this collection (e.g.
    /// after a "New Race..." save) updates every row's dropdown live.</summary>
    public ObservableCollection<string> RaceOptions { get; }

    public ObservableCollection<NewGamePlayerRowViewModel> Players { get; }

    public VictoryConditionRowViewModel PlanetsOwned { get; }

    public VictoryConditionRowViewModel TechLevels { get; }

    public VictoryConditionRowViewModel NumberOfFields { get; }

    public VictoryConditionRowViewModel ProductionCapacity { get; }

    public VictoryConditionRowViewModel CapitalShips { get; }

    public VictoryConditionRowViewModel HighestScore { get; }

    public VictoryConditionRowViewModel TotalScore { get; }

    public VictoryConditionRowViewModel SecondPlaceScore { get; }

    public IRelayCommand AddPlayerCommand { get; }

    public IRelayCommand<NewGamePlayerRowViewModel> RemovePlayerCommand { get; }

    public IRelayCommand<NewGamePlayerRowViewModel> MoveUpCommand { get; }

    public IRelayCommand<NewGamePlayerRowViewModel> MoveDownCommand { get; }

    public IRelayCommand RandomizeSeedCommand { get; }

    public IRelayCommand CreateGameCommand { get; }

    /// <summary>Raised when any player row's "New Race..." button is clicked, carrying that
    /// row - the view hosts a RaceDesignerView and, once it reports back, calls
    /// <see cref="CompleteNewRace"/> with the same row.</summary>
    public event Action<NewGamePlayerRowViewModel>? RaceDesignerRequested;

    /// <summary>Raised when any player row's "Load Race..." button is clicked, carrying that
    /// row - the view opens a file picker for an existing .race file and, once it resolves,
    /// calls <see cref="CompleteLoadRace"/> with the same row.</summary>
    public event Action<NewGamePlayerRowViewModel>? LoadRaceRequested;

    private bool showRaceDesignerOverlay;

    /// <summary>True while a RaceDesignerView is hosted on top of this screen (see
    /// RequestNewRace/CompleteNewRace) - the view binds its own main content's IsVisible to the
    /// negation of this, since without it the Race Designer overlay has no opaque background of
    /// its own and both screens render on top of each other (confirmed live: a "graphics
    /// quirk" opening Race Designer from within New Game).</summary>
    public bool ShowRaceDesignerOverlay
    {
        get => showRaceDesignerOverlay;
        private set => SetProperty(ref showRaceDesignerOverlay, value);
    }

    /// <summary>Raised once a game has been successfully created and opened for a human player,
    /// carrying the ready ClientData - same contract as OpenGameViewModel.GameOpened.</summary>
    public event Action<ClientData>? GameOpened;

    public event Action? CancelRequested;

    public string GameName
    {
        get => gameName;
        set
        {
            if (SetProperty(ref gameName, value))
            {
                GameSettings.Data.GameName = value;

                // Keep GameFolder following GameName until the user explicitly overrides it
                // (Browse, or editing the folder field directly) - otherwise a renamed game
                // silently keeps writing into a folder named after whatever GameName happened
                // to be at construction time, and a second game created afterward collides
                // with the first one's files in that same stale folder (confirmed live: a
                // renamed game's .sstate ended up sitting alongside an earlier game's own
                // files, and turn advancement picked up the wrong one).
                if (!gameFolderManuallySet)
                {
                    SetProperty(ref gameFolder, ComputeDefaultFolder(value), nameof(GameFolder));
                }

                CreateGameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string GameFolder
    {
        get => gameFolder;
        set
        {
            if (SetProperty(ref gameFolder, value))
            {
                gameFolderManuallySet = true;
                CreateGameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int Seed
    {
        get => seed;
        set => SetProperty(ref seed, value);
    }

    public bool AcceleratedStart
    {
        get => GameSettings.Data.AcceleratedStart;
        set
        {
            if (GameSettings.Data.AcceleratedStart != value)
            {
                GameSettings.Data.AcceleratedStart = value;
                OnPropertyChanged();
            }
        }
    }

    public int MapWidth
    {
        get => GameSettings.Data.MapWidth;
        set => SetGameSetting(() => GameSettings.Data.MapWidth = Math.Clamp(value, 300, 9999));
    }

    public int MapHeight
    {
        get => GameSettings.Data.MapHeight;
        set => SetGameSetting(() => GameSettings.Data.MapHeight = Math.Clamp(value, 300, 9999));
    }

    public int StarSeparation
    {
        get => GameSettings.Data.StarSeparation;
        set => SetGameSetting(() => GameSettings.Data.StarSeparation = Math.Clamp(value, 5, 50));
    }

    public int StarDensity
    {
        get => GameSettings.Data.StarDensity;
        set => SetGameSetting(() => GameSettings.Data.StarDensity = Math.Clamp(value, 1, 100));
    }

    public int StarUniformity
    {
        get => GameSettings.Data.StarUniformity;
        set => SetGameSetting(() => GameSettings.Data.StarUniformity = Math.Clamp(value, 1, 100));
    }

    /// <summary>How many of the enabled victory conditions must be simultaneously true - capped at
    /// how many actually ARE enabled (docs/behavior-specs-4/victory-conditions.md's derived
    /// meta-setting: "min(raw, count of currently-enabled conditions among 1-7 excluding condition
    /// 3)"), not a fixed 1-8 range - otherwise a player could require more conditions than are
    /// even turned on, making victory unreachable. Condition 3 (NumberOfFields) is excluded because
    /// it's paired with condition 2 (TechLevels) rather than independently toggleable; condition 8
    /// (HighestScore) is excluded because the spec's own range for this formula is "1-7", not "1-8".</summary>
    public int TargetsToMeet
    {
        get => GameSettings.Data.TargetsToMeet;
        set => SetGameSetting(() => GameSettings.Data.TargetsToMeet = Math.Clamp(value, 1, Math.Max(1, EnabledVictoryConditionCount)));
    }

    private static int EnabledVictoryConditionCount
    {
        get
        {
            EnabledValue[] conditions =
            {
                GameSettings.Data.PlanetsOwned,
                GameSettings.Data.TechLevels,
                GameSettings.Data.TotalScore,
                GameSettings.Data.SecondPlaceScore,
                GameSettings.Data.ProductionCapacity,
                GameSettings.Data.CapitalShips,
            };
            return conditions.Count(c => c.IsChecked);
        }
    }

    public int MinimumGameTime
    {
        get => GameSettings.Data.MinimumGameTime;
        set => SetGameSetting(() => GameSettings.Data.MinimumGameTime = Math.Clamp(value, 10, 10000));
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    /// <summary>
    /// Lets the view's code-behind surface a problem it hit (e.g. a Load Race file picker that
    /// failed) through this screen's own visible status line, rather than through
    /// Report.Error - which is never actually wired to anything visible on this port (neither
    /// desktop nor Android registers PlatformHooks.ShowError), so it silently vanishes into
    /// Console/logcat and the user sees nothing happen at all.
    /// </summary>
    public void ReportError(string message)
    {
        StatusMessage = message;
    }

    /// <summary>
    /// Adds (or replaces) a race in this screen's own known-races list and race-picker options -
    /// shared by both the "New Race..." and "Load Race..." flows below, and by extension usable
    /// regardless of where the race file actually lives on disk (unlike re-scanning the default
    /// race folder, which would miss one saved or loaded from elsewhere).
    /// </summary>
    public void AddKnownRace(Race race)
    {
        knownRaces[race.Name] = race;
        if (!RaceOptions.Contains(race.Name))
        {
            RaceOptions.Add(race.Name);
        }
    }

    /// <summary>
    /// Called by the view's code-behind once a RaceDesignerView it hosted (for the given row's
    /// "New Race..." button) reports back. <paramref name="savedRace"/> is null on a Cancel
    /// (RaceDesignerView.WasSaved was false) - nothing to add in that case.
    /// </summary>
    public void CompleteNewRace(NewGamePlayerRowViewModel row, Race? savedRace)
    {
        ShowRaceDesignerOverlay = false;

        if (savedRace != null)
        {
            AddKnownRace(savedRace);
            row.SelectedRaceName = savedRace.Name;
        }
    }

    /// <summary>
    /// Called by the view's code-behind once a row's "Load Race..." file picker resolves to a
    /// real race file.
    /// </summary>
    public void CompleteLoadRace(NewGamePlayerRowViewModel row, Race loadedRace)
    {
        // A race with no name can't be added to knownRaces (Dictionary<string, Race> keyed by
        // it) or usefully selected in a row's ComboBox - reject it here with a visible reason
        // rather than letting a null-key dictionary write throw, which the file picker's own
        // try/catch would otherwise turn into the exact silent "nothing happens" this method
        // exists to avoid.
        if (string.IsNullOrEmpty(loadedRace.Name))
        {
            StatusMessage = "That file doesn't look like a valid race (it has no name) - couldn't load it.";
            return;
        }

        AddKnownRace(loadedRace);
        row.SelectedRaceName = loadedRace.Name;
        StatusMessage = $"Loaded race \"{loadedRace.Name}\".";
    }

    public void RequestNewRace(NewGamePlayerRowViewModel row)
    {
        ShowRaceDesignerOverlay = true;
        RaceDesignerRequested?.Invoke(row);
    }

    public void RequestLoadRace(NewGamePlayerRowViewModel row)
    {
        LoadRaceRequested?.Invoke(row);
    }

    public void Cancel()
    {
        CancelRequested?.Invoke();
    }

    private void AddPlayerRow(string? raceName)
    {
        if (raceName == null)
        {
            return;
        }

        NewGamePlayerRowViewModel row = new NewGamePlayerRowViewModel(RaceOptions, raceName);
        row.NewRaceRequested += RequestNewRace;
        row.LoadRaceRequested += RequestLoadRace;
        Players.Add(row);
        RenumberPlayers();
        CreateGameCommand.NotifyCanExecuteChanged();
    }

    private void RemovePlayerRow(NewGamePlayerRowViewModel? row)
    {
        if (row == null)
        {
            return;
        }

        row.NewRaceRequested -= RequestNewRace;
        row.LoadRaceRequested -= RequestLoadRace;
        Players.Remove(row);
        RenumberPlayers();
        CreateGameCommand.NotifyCanExecuteChanged();
    }

    private void Move(NewGamePlayerRowViewModel? row, int direction)
    {
        if (row == null)
        {
            return;
        }

        int index = Players.IndexOf(row);
        int newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= Players.Count)
        {
            return;
        }

        Players.Move(index, newIndex);
        RenumberPlayers();
    }

    private void RenumberPlayers()
    {
        for (int i = 0; i < Players.Count; i++)
        {
            Players[i].DisplayNumber = i + 1;
        }
    }

    private bool CanCreateGame()
    {
        return Players.Count > 0
            && !string.IsNullOrWhiteSpace(GameName)
            && !string.IsNullOrWhiteSpace(GameFolder);
    }

    private void CreateGame()
    {
        try
        {
            Directory.CreateDirectory(GameFolder);

            GameSettings.Data.GameName = GameName;
            GameSettings.Data.Seed = Seed;

            List<PlayerSettings> playerSettings = Players
                .Select((row, index) => new PlayerSettings
                {
                    PlayerNumber = (ushort)(index + 1),
                    RaceName = row.SelectedRaceName,
                    AiProgram = row.SelectedAiProgram,
                })
                .ToList();

            Gameinitializer.Initialize(GameFolder, playerSettings, knownRaces);
            GameSettings.Save();

            PlayerSettings? humanPlayer = playerSettings.FirstOrDefault(p => p.AiProgram == "Human");
            if (humanPlayer != null)
            {
                ClientData clientState = GameSession.Load(GameFolder, humanPlayer.RaceName);
                GameOpened?.Invoke(clientState);
            }
            else
            {
                StatusMessage = $"Game \"{GameName}\" created with no human players to open here.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't create the game: {ex.Message}";
        }
    }

    private void SetGameSetting(Action apply, [CallerMemberName] string? propertyName = null)
    {
        apply();
        OnPropertyChanged(propertyName);
    }
}
