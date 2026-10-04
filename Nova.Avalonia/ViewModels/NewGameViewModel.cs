using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Nova.Ai;
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
/// Two setup paths share one draft (GameSettings.Data), as behavior-specs-10/new-game-setup.md
/// section 1-2 describes: <b>Simplified</b> (one screen: your race, galaxy size, difficulty; the
/// player count and the victory year gate are seeded from the galaxy size, see NewGameSetup) and
/// <b>Detailed</b> (the tabbed general / players / victory pages). The general page carries the
/// wizard's discrete galaxy options (Galaxy Size Tiny..Huge, Star Density Sparse..Packed,
/// Starting Distance Close..Distant; GameSettings.UseGalaxyPresets) next to the older free map
/// sliders, the option checkboxes the engine implements, and Reset to defaults (section 8).
///
/// Deliberately NOT ported:
/// - NumberOfStars as an editable number: disabled in the original WinForms UI itself; with the
///   galaxy presets on it is derived (GameSettings.PresetStarCount) and shown read-only.
/// - "Browse for a custom AI executable": PlayerSettings.AiProgram other than "Human" always
///   runs Nova's own DefaultAi (TurnHost / NovaConsole.RunAI).
/// - "Computer Players Form Alliances" and "Public Player Scores": the engine has no setting for
///   either yet, so a checkbox would have no effect.
/// </summary>
public class NewGameViewModel : ViewModelBase
{
    public const string SimplifiedMode = "Simplified";
    public const string DetailedMode = "Detailed";

    /// <summary>The quick dialog's four difficulty radio buttons (ai-opponent-behavior.md
    /// section 1a): Easy tier 0, Standard 1, Harder 2 (the Tough tier), Expert 3.</summary>
    public static readonly IReadOnlyList<string> DifficultyLabels = new[] { "Easy", "Standard", "Harder", "Expert" };

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

    private string selectedSetupMode = DetailedMode;

    private string? simplifiedRaceName;

    private int simplifiedDifficultyIndex = AiRaceTemplates.Standard;

    private int simplifiedPlayerCount;

    private Random playerCountRandom;

    public NewGameViewModel()
    {
        // Commands are created first - AddPlayerRow (called below, while seeding the initial
        // player list) notifies CreateGameCommand of a can-execute change, which would null-ref
        // if that command didn't exist yet.
        AddPlayerCommand = new RelayCommand(() => AddPlayerRow(RaceOptions.FirstOrDefault()));
        RemovePlayerCommand = new RelayCommand<NewGamePlayerRowViewModel>(RemovePlayerRow);
        MoveUpCommand = new RelayCommand<NewGamePlayerRowViewModel>(row => Move(row, -1));
        MoveDownCommand = new RelayCommand<NewGamePlayerRowViewModel>(row => Move(row, 1));
        RandomizeSeedCommand = new RelayCommand(() => Seed = Environment.TickCount);
        CreateGameCommand = new RelayCommand(CreateGame, CanCreateGame);
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);
        RerollPlayerCountCommand = new RelayCommand(RerollPlayerCount);

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
        playerCountRandom = new Random(seed);

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

        simplifiedRaceName = RaceOptions.FirstOrDefault();
        simplifiedPlayerCount = NewGameSetup.ChooseSimplifiedPlayerCount(GameSettings.Data.GalaxySizeSetting, simplifiedDifficultyIndex, playerCountRandom);

        gameName = GameSettings.Data.GameName;
        gameFolder = ComputeDefaultFolder(gameName);

        PlanetsOwned = new VictoryConditionRowViewModel("Owns the following number of planets (%)", GameSettings.Data.PlanetsOwned, 0, 100);
        TechLevels = new VictoryConditionRowViewModel("Attains the following tech level", GameSettings.Data.TechLevels, 0, 10000);
        NumberOfFields = new VictoryConditionRowViewModel("In the following number of fields", GameSettings.Data.NumberOfFields, 0, 6);
        ProductionCapacity = new VictoryConditionRowViewModel("Has production capacity of (in K resources)", GameSettings.Data.ProductionCapacity, 0, 10000);
        CapitalShips = new VictoryConditionRowViewModel("Number of capital ships", GameSettings.Data.CapitalShips, 0, 10000);
        HighestScore = new VictoryConditionRowViewModel("Has the highest score after (years)", GameSettings.Data.HighestScore, 0, 10000);
        // Maximum must be at least the shipped default (11000, GameSettings.cs) - a 10000 cap
        // used to sit below its own default, so the slider could never be moved back up to it
        // once touched. 20000 matches the spec's own confirmed real range for this condition.
        TotalScore = new VictoryConditionRowViewModel("Exceeds a score of", GameSettings.Data.TotalScore, 0, 20000);
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

    public IRelayCommand ResetToDefaultsCommand { get; }

    public IRelayCommand RerollPlayerCountCommand { get; }

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

    // ---- Setup path -------------------------------------------------------------------------

    public IReadOnlyList<string> SetupModeOptions { get; } = new[] { SimplifiedMode, DetailedMode };

    /// <summary>Simplified (one screen) or Detailed (the three-page wizard); both edit the same draft.</summary>
    public string SelectedSetupMode
    {
        get => selectedSetupMode;
        set
        {
            if (value != null && SetProperty(ref selectedSetupMode, value))
            {
                OnPropertyChanged(nameof(IsSimplified));
                OnPropertyChanged(nameof(IsDetailed));
                if (IsSimplified)
                {
                    // The simplified path always uses the discrete wizard galaxy.
                    ApplyPreset();
                    MinimumGameTime = NewGameSetup.SimplifiedYearGate(GameSettings.Data.GalaxySizeSetting);
                    OnPropertyChanged(nameof(SimplifiedYearGate));
                }

                CreateGameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsSimplified => SelectedSetupMode == SimplifiedMode;

    public bool IsDetailed => !IsSimplified;

    /// <summary>The Simplified path's one human player's race.</summary>
    public string? SimplifiedRaceName
    {
        get => simplifiedRaceName;
        set
        {
            if (SetProperty(ref simplifiedRaceName, value))
            {
                CreateGameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> DifficultyOptions => DifficultyLabels;

    /// <summary>0 Easy .. 3 Expert: the tier every computer player of a Simplified game gets.</summary>
    public int SimplifiedDifficultyIndex
    {
        get => simplifiedDifficultyIndex;
        set
        {
            if (SetProperty(ref simplifiedDifficultyIndex, Math.Clamp(value, 0, 3)))
            {
                // new-game-setup.md section 2: the player-count table is by difficulty as well as size.
                SimplifiedPlayerCount = NewGameSetup.ChooseSimplifiedPlayerCount(GameSettings.Data.GalaxySizeSetting, simplifiedDifficultyIndex, playerCountRandom);
                OnPropertyChanged(nameof(SimplifiedPlayerRange));
            }
        }
    }

    /// <summary>Total players of a Simplified game (you plus computer players), drawn from the
    /// galaxy size's range whenever the size changes (new-game-setup.md section 2).</summary>
    public int SimplifiedPlayerCount
    {
        get => simplifiedPlayerCount;
        private set
        {
            if (SetProperty(ref simplifiedPlayerCount, value))
            {
                OnPropertyChanged(nameof(SimplifiedPlayerSummary));
            }
        }
    }

    public string SimplifiedPlayerSummary =>
        $"{SimplifiedPlayerCount} players: you and {SimplifiedPlayerCount - 1} computer player{(SimplifiedPlayerCount - 1 == 1 ? string.Empty : "s")}";

    public string SimplifiedPlayerRange
    {
        get
        {
            (int min, int max) = NewGameSetup.SimplifiedPlayerCountRange(GameSettings.Data.GalaxySizeSetting, SimplifiedDifficultyIndex);
            return min == max ? $"{min} for this galaxy size" : $"{min}-{max} for this galaxy size";
        }
    }

    /// <summary>The year gate the Simplified path seeds from the galaxy size (victory-conditions.md section 1).</summary>
    public int SimplifiedYearGate => NewGameSetup.SimplifiedYearGate(GameSettings.Data.GalaxySizeSetting);

    // ---- General page ----------------------------------------------------------------------

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

    public IReadOnlyList<string> GalaxySizeOptions { get; } = Enum.GetNames(typeof(GalaxySize));

    public IReadOnlyList<string> StarDensityOptions { get; } = Enum.GetNames(typeof(GalaxyDensity));

    public IReadOnlyList<string> StartingDistanceOptions { get; } = Enum.GetNames(typeof(StartingDistance));

    /// <summary>The wizard's discrete galaxy (GameSettings.UseGalaxyPresets): when on, the map is
    /// (size + 1) x 400 ly square and the star count comes from the size and density.</summary>
    public bool UseGalaxyPresets
    {
        get => GameSettings.Data.UseGalaxyPresets;
        set
        {
            if (GameSettings.Data.UseGalaxyPresets == value)
            {
                return;
            }

            if (value)
            {
                ApplyPreset();
            }
            else
            {
                GameSettings.Data.UseGalaxyPresets = false;
            }

            RaiseMapProperties();
        }
    }

    public bool UseFreeMap => !UseGalaxyPresets;

    /// <summary>Galaxy Size, 0 Tiny .. 4 Huge.</summary>
    public int GalaxySizeIndex
    {
        get => (int)GameSettings.Data.GalaxySizeSetting;
        set
        {
            GalaxySize size = (GalaxySize)Math.Clamp(value, 0, 4);
            if (GameSettings.Data.GalaxySizeSetting == size)
            {
                return;
            }

            GameSettings.Data.GalaxySizeSetting = size;
            if (UseGalaxyPresets || IsSimplified)
            {
                ApplyPreset();
            }

            if (IsSimplified)
            {
                // new-game-setup.md section 2 and victory-conditions.md section 1: the player
                // count and the year gate follow the galaxy size on the Simplified path.
                SimplifiedPlayerCount = NewGameSetup.ChooseSimplifiedPlayerCount(size, SimplifiedDifficultyIndex, playerCountRandom);
                MinimumGameTime = NewGameSetup.SimplifiedYearGate(size);
            }

            RaiseMapProperties();
            OnPropertyChanged(nameof(SimplifiedPlayerRange));
            OnPropertyChanged(nameof(SimplifiedYearGate));
        }
    }

    /// <summary>Star Density, 0 Sparse .. 3 Packed.</summary>
    public int StarDensityIndex
    {
        get => (int)GameSettings.Data.StarDensitySetting;
        set
        {
            GalaxyDensity density = (GalaxyDensity)Math.Clamp(value, 0, 3);
            if (GameSettings.Data.StarDensitySetting == density)
            {
                return;
            }

            GameSettings.Data.StarDensitySetting = density;
            if (UseGalaxyPresets || IsSimplified)
            {
                ApplyPreset();
            }

            RaiseMapProperties();
        }
    }

    /// <summary>Starting Distance, 0 Close .. 3 Distant. Stored only: the spec gives it no effect on generation.</summary>
    public int StartingDistanceIndex
    {
        get => (int)GameSettings.Data.StartingDistanceSetting;
        set => SetGameSetting(() => GameSettings.Data.StartingDistanceSetting = (StartingDistance)Math.Clamp(value, 0, 3));
    }

    /// <summary>"N ly across, M stars" for the chosen size and density (new-game-setup.md section 3).</summary>
    public string GalaxySummary
    {
        get
        {
            GalaxySize size = GameSettings.Data.GalaxySizeSetting;
            GalaxyDensity density = GameSettings.Data.StarDensitySetting;
            return $"{GameSettings.GalaxyDiameter(size)} light years across, {GameSettings.PresetStarCount(size, density)} stars";
        }
    }

    /// <summary>"Accelerated BBS Play" (option bit 0x20).</summary>
    public bool AcceleratedStart
    {
        get => GameSettings.Data.AcceleratedStart;
        set => SetGameSetting(() => GameSettings.Data.AcceleratedStart = value);
    }

    /// <summary>The "No Random Events" game option (turn-generation-engine.md §1b) - skips the
    /// yearly comet / environment-shift / mineral-deposit events (RandomEventsStep).</summary>
    public bool NoRandomEvents
    {
        get => GameSettings.Data.NoRandomEvents;
        set => SetGameSetting(() => GameSettings.Data.NoRandomEvents = value);
    }

    /// <summary>"Beginner: Maximum Minerals" (option bit 0x01).</summary>
    public bool MaximumMinerals
    {
        get => GameSettings.Data.MaximumMinerals;
        set => SetGameSetting(() => GameSettings.Data.MaximumMinerals = value);
    }

    /// <summary>"Slower Tech Advances" (option bit 0x02).</summary>
    public bool SlowTechAdvance
    {
        get => GameSettings.Data.SlowTechAdvance;
        set => SetGameSetting(() => GameSettings.Data.SlowTechAdvance = value);
    }

    /// <summary>"Galaxy Clumping" (option bit 0x100).</summary>
    public bool GalaxyClumping
    {
        get => GameSettings.Data.GalaxyClumping;
        set => SetGameSetting(() => GameSettings.Data.GalaxyClumping = value);
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

    /// <summary>The victory year gate (minimum game years before anyone can win).</summary>
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

        if (SimplifiedRaceName == null)
        {
            SimplifiedRaceName = race.Name;
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
        // With no race files at all a row can still be a built-in AI (it needs no race file).
        NewGamePlayerRowViewModel row = new NewGamePlayerRowViewModel(RaceOptions, raceName ?? string.Empty);
        if (raceName == null)
        {
            row.SelectedAiProgram = NewGamePlayerRowViewModel.BuiltInAi;
        }

        row.NewRaceRequested += RequestNewRace;
        row.LoadRaceRequested += RequestLoadRace;
        row.PropertyChanged += (_, _) => CreateGameCommand.NotifyCanExecuteChanged();
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

    private void RerollPlayerCount()
    {
        SimplifiedPlayerCount = NewGameSetup.ChooseSimplifiedPlayerCount(GameSettings.Data.GalaxySizeSetting, SimplifiedDifficultyIndex, playerCountRandom);
    }

    /// <summary>Reset settings to defaults (new-game-setup.md section 8; see NewGameSetup.ResetToDefaults).</summary>
    private void ResetToDefaults()
    {
        NewGameSetup.ResetToDefaults(GameSettings.Data, SimplifiedDifficultyIndex);
        foreach (VictoryConditionRowViewModel row in new[] { PlanetsOwned, TechLevels, NumberOfFields, ProductionCapacity, CapitalShips, HighestScore, TotalScore, SecondPlaceScore })
        {
            row.Refresh();
        }

        if (IsSimplified)
        {
            SimplifiedPlayerCount = NewGameSetup.ChooseSimplifiedPlayerCount(GameSettings.Data.GalaxySizeSetting, SimplifiedDifficultyIndex, playerCountRandom);
            GameSettings.Data.MinimumGameTime = NewGameSetup.SimplifiedYearGate(GameSettings.Data.GalaxySizeSetting);
        }

        RaiseMapProperties();
        foreach (string property in new[]
        {
            nameof(StartingDistanceIndex), nameof(AcceleratedStart), nameof(NoRandomEvents), nameof(MaximumMinerals),
            nameof(SlowTechAdvance), nameof(GalaxyClumping), nameof(StarSeparation), nameof(TargetsToMeet),
            nameof(MinimumGameTime), nameof(SimplifiedPlayerRange), nameof(SimplifiedYearGate),
        })
        {
            OnPropertyChanged(property);
        }

        StatusMessage = "Settings reset to their defaults.";
    }

    private void ApplyPreset()
    {
        GameSettings.Data.ApplyGalaxyPreset(GameSettings.Data.GalaxySizeSetting, GameSettings.Data.StarDensitySetting);
    }

    private void RaiseMapProperties()
    {
        foreach (string property in new[]
        {
            nameof(UseGalaxyPresets), nameof(UseFreeMap), nameof(GalaxySizeIndex), nameof(StarDensityIndex),
            nameof(GalaxySummary), nameof(MapWidth), nameof(MapHeight), nameof(StarDensity), nameof(StarUniformity),
        })
        {
            OnPropertyChanged(property);
        }
    }

    private bool CanCreateGame()
    {
        if (string.IsNullOrWhiteSpace(GameName) || string.IsNullOrWhiteSpace(GameFolder))
        {
            return false;
        }

        if (IsSimplified)
        {
            return !string.IsNullOrEmpty(SimplifiedRaceName) && knownRaces.ContainsKey(SimplifiedRaceName);
        }

        return Players.Count > 0
            && Players.All(row => row.IsBuiltInAi || knownRaces.ContainsKey(row.SelectedRaceName ?? string.Empty));
    }

    /// <summary>
    /// The players of the game being created, with the race of every built-in AI slot made from
    /// its template (ai-opponent-behavior.md section 1a: Random tier then Random archetype
    /// resolved, template copied unvalidated, category = archetype) and added to
    /// <paramref name="races"/> under a unique name. Rows are drawn from the Simplified path's
    /// settings or the Detailed player list.
    /// </summary>
    private List<PlayerSettings> BuildPlayers(Dictionary<string, Race> races)
    {
        Random aiRandom = new Random(Seed);
        List<PlayerSettings> players = new List<PlayerSettings>();

        void AddBuiltInAi(int archetype, int tier)
        {
            AiRaceTemplates.ResolveRandom(ref archetype, ref tier, aiRandom);
            Race race = AiRaceTemplates.CreateRace(archetype, tier);
            race.Name = NewGameSetup.UniqueRaceName(race.Name, races.Keys);
            race.PluralName = race.Name;
            AssignIcon(race, (archetype * 4) + tier);
            races[race.Name] = race;
            players.Add(new PlayerSettings
            {
                PlayerNumber = (ushort)(players.Count + 1),
                RaceName = race.Name,
                AiProgram = NewGamePlayerRowViewModel.DefaultAi,
                AiCategory = archetype,
                AiSkill = tier,
            });
        }

        if (IsSimplified)
        {
            players.Add(new PlayerSettings { PlayerNumber = 1, RaceName = SimplifiedRaceName, AiProgram = NewGamePlayerRowViewModel.Human });

            // SPEC GAP (ai-opponent-behavior.md section 1a, quick New Game dialog): "every
            // difficulty also fills some slots with a random archetype, and in Standard and
            // Harder games the last group of slots has a random tier as well" - which slots, and
            // the fixed archetypes of the others, are not given. Stand-in: every computer player
            // gets a random archetype and the difficulty's tier.
            for (int i = 1; i < SimplifiedPlayerCount; i++)
            {
                AddBuiltInAi(AiRaceTemplates.RandomArchetype, SimplifiedDifficultyIndex);
            }

            return players;
        }

        foreach (NewGamePlayerRowViewModel row in Players)
        {
            if (row.IsBuiltInAi)
            {
                AddBuiltInAi(row.SelectedArchetypeIndex, row.SelectedTierIndex);
            }
            else
            {
                players.Add(new PlayerSettings
                {
                    PlayerNumber = (ushort)(players.Count + 1),
                    RaceName = row.SelectedRaceName,
                    AiProgram = row.AiProgramForSettings,
                });
            }
        }

        return players;
    }

    /// <summary>The templates carry no emblem; give each one a fixed icon from the shipped set
    /// so race lists and reports have something to draw (presentation only).</summary>
    private static void AssignIcon(Race race, int templateIndex)
    {
        try
        {
            AllRaceIcons.Restore();
            List<RaceIcon> icons = AllRaceIcons.Data.IconList;
            if (icons.Count > 0)
            {
                race.Icon = icons[templateIndex % icons.Count];
            }
        }
        catch (Exception)
        {
            // No icon set available on this platform - the race simply has none.
        }
    }

    private void CreateGame()
    {
        try
        {
            Directory.CreateDirectory(GameFolder);

            GameSettings.Data.GameName = GameName;
            GameSettings.Data.Seed = Seed;
            if (IsSimplified)
            {
                NewGameSetup.ApplySimplifiedDefaults(GameSettings.Data, SimplifiedDifficultyIndex);
                GameSettings.Data.MinimumGameTime = NewGameSetup.SimplifiedYearGate(GameSettings.Data.GalaxySizeSetting);
            }

            Dictionary<string, Race> races = new Dictionary<string, Race>(knownRaces);
            List<PlayerSettings> playerSettings = BuildPlayers(races);

            Gameinitializer.Initialize(GameFolder, playerSettings, races);
            GameSettings.Save();

            PlayerSettings? humanPlayer = playerSettings.FirstOrDefault(p => p.AiProgram == NewGamePlayerRowViewModel.Human);
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
