using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia; // Application - see IsDarkMode's own comment on why this needs the `using`
                // rather than an inline `Avalonia.Application` (this namespace's own last
                // segment is also literally "Avalonia", which shadows the real one otherwise).
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Input;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Android's own game screen - not the desktop dock layout (MainViewModel/NovaDockFactory),
/// which turned out not to translate to a phone at all: panels shrank to unreadable slivers, and
/// AvaloniaDock's splitters are far too small a drag target for touch. Instead:
///
/// - "Map" groups everything that's about the Star Map and whatever's currently selected on it -
///   the map itself (now draggable/zoomable via touch - see StarMapDocumentViewModel), a
///   MapSelectionSwitcher ComboBox for hopping between a selected star and any of its own fleets
///   without a separate Navigator screen, the Inspector (planet and fleet detail, plus fleet
///   order editing), and Production (the selected planet's build queue, hidden entirely unless
///   that planet is actually colonized - see ProductionViewModel.HasColonizedPlanet) - stacked in
///   one screen instead of separate dock panels, since all of these already react to the exact
///   same shared SelectionService.
/// - Everything else that ISN'T about a specific map selection (Navigator's browse-by-list,
///   Research, Ship Design, Battle Plans, Messages, Summary, Player Relations, the Planet/Fleet/
///   Battle/Score report tables, and the manual) gets its own full-screen page, switched from the
///   burger menu (MenuEntries) - one directly tappable row per section, plus About at the end -
///   rather than a TabControl or a row of on-screen buttons, both of which have their own
///   confirmed-live touch bugs on this app (see RaceDesignerViewModel.Page's own comment). A
///   dropdown was tried first and worked, but buried every option behind an extra tap to open it
///   and hid which sections even existed - a plain list of rows in the menu shows all of them
///   (and About) at a glance instead.
///
/// Turn-submission/About plumbing lives in the shared GameShellViewModelBase, exactly like
/// MainViewModel, so both screens behave identically there despite their very different content.
/// </summary>
public class MobileMainViewModel : GameShellViewModelBase
{
    public enum Page
    {
        Map,
        Navigator,
        Research,
        ShipDesign,
        BattlePlans,
        Messages,
        Summary,
        PlayerRelations,
        PlanetReport,
        FleetReport,
        BattleReport,
        ScoreReport,
        Help,
    }

    private static readonly (string Label, Page Value)[] PageDefinitions =
    {
        ("Map", Page.Map),
        ("Navigator", Page.Navigator),
        ("Research", Page.Research),
        ("Ship Design", Page.ShipDesign),
        ("Battle Plans", Page.BattlePlans),
        ("Messages", Page.Messages),
        ("Summary", Page.Summary),
        ("Player Relations", Page.PlayerRelations),
        ("Planet Report", Page.PlanetReport),
        ("Fleet Report", Page.FleetReport),
        ("Battle Report", Page.BattleReport),
        ("Score Report", Page.ScoreReport),
        ("Help", Page.Help),
    };

    private Page selectedPage = Page.Map;

    private bool isMenuOpen;

    /// <summary>Whether the burger-menu panel (Section picker + About) is currently expanded -
    /// replaces the always-visible title bar/Section combo with a compact header, freeing up
    /// vertical space for the actual game content on a small screen.</summary>
    public bool IsMenuOpen
    {
        get => isMenuOpen;
        set => SetProperty(ref isMenuOpen, value);
    }

    public IRelayCommand ToggleMenuCommand { get; }

    /// <summary>Wraps the shared ShowAboutCommand (GameShellViewModelBase) to also close this
    /// menu first - About opens a separate window rather than switching pages, so nothing else
    /// closes the menu for it the way picking a section already does (SelectedPageLabel's own
    /// setter).</summary>
    public IRelayCommand ShowAboutFromMenuCommand { get; }

    /// <summary>Fires the platform's native Share sheet with whatever Report.Error history has
    /// been persisted (see PlatformHooks.ShareErrorLog's own comment) - a Report.Error only ever
    /// shows as a brief Toast on this host (Application.OnCreate), so without this there was no
    /// way to recover the actual error text once it faded, e.g. to send it on for
    /// diagnosis.</summary>
    public IRelayCommand ShareErrorLogCommand { get; }

    /// <summary>Fires the platform's native Share sheet with the currently-open game's own .intel
    /// file (this empire's full saved state, plain XML) - e.g. for diagnosing a save that's
    /// showing broken behavior. Android's scoped storage otherwise makes this file awkward to
    /// reach even for the player who owns it (no plain file-browser access, and `adb run-as`
    /// needs a debuggable build a real release APK isn't), so this is the only practical way to
    /// get a copy off the device at all.</summary>
    public IRelayCommand ShareGameSaveCommand { get; }

    private bool isDarkMode;

    /// <summary>Overrides the app's own default of following the system theme (App.axaml's
    /// "Default" RequestedThemeVariant) with an explicit choice, persisted via
    /// PlatformHooks.SaveThemePreference so it survives a restart - see App.axaml.cs's own
    /// startup read of PlatformHooks.LoadThemePreference. Initialized from the CURRENTLY active
    /// theme (ActualThemeVariant, the resolved one - RequestedThemeVariant itself is often just
    /// "Default" until a choice is actually made) so the checkbox starts in the right state
    /// whether that's from a previously saved choice or today's system setting.</summary>
    public bool IsDarkMode
    {
        get => isDarkMode;
        set
        {
            if (SetProperty(ref isDarkMode, value))
            {
                ThemeVariant variant = value ? ThemeVariant.Dark : ThemeVariant.Light;
                if (Application.Current != null)
                {
                    Application.Current.RequestedThemeVariant = variant;
                }

                PlatformHooks.SaveThemePreference(value ? "Dark" : "Light");
            }
        }
    }

    private bool isConfirmingCloseGame;

    /// <summary>True while the "Close Game" confirmation (Yes/Cancel, in place of the menu's own
    /// entries) is showing - closing discards any commands queued since the last successful End
    /// Turn (they only get written out on Submit), so this isn't a single-tap action the way
    /// picking a section or About is. Same "arm, then a second explicit action" shape as the
    /// map's own "Add Waypoint via Map Tap" - no new modal/dialog infrastructure needed for
    /// it.</summary>
    public bool IsConfirmingCloseGame
    {
        get => isConfirmingCloseGame;
        private set => SetProperty(ref isConfirmingCloseGame, value);
    }

    public IRelayCommand RequestCloseGameCommand { get; }

    public IRelayCommand ConfirmCloseGameCommand { get; }

    public IRelayCommand CancelCloseGameCommand { get; }

    /// <summary>Raised once the user has confirmed closing this game - the host (ShellView) reacts
    /// by swapping back to the startup Open/New Game screen, exactly as if the app had just
    /// launched fresh (see ShellView.ShowOpenGame). Nothing here writes or discards any file on
    /// disk - only in-memory, not-yet-submitted commands are lost, the same as if the app were
    /// killed without hitting End Turn.</summary>
    public event Action? GameCloseRequested;

    public MobileMainViewModel(ClientData clientState) : base(clientState)
    {
        // Set directly on the backing field, not through the IsDarkMode property setter - that
        // setter also re-applies and re-saves the theme, which would be redundant (App.axaml.cs
        // already applied any saved choice before this screen ever exists) and, worse, would
        // overwrite a real saved "Light" choice with whatever the CURRENT system theme happens to
        // be if that saved choice hasn't been applied yet on this exact code path.
        isDarkMode = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

        ToggleMenuCommand = new RelayCommand(() => IsMenuOpen = !IsMenuOpen);
        ShowAboutFromMenuCommand = new RelayCommand(() =>
        {
            IsMenuOpen = false;
            ShowAboutCommand.Execute(null);
        });
        ShareErrorLogCommand = new RelayCommand(() =>
        {
            IsMenuOpen = false;
            if (!PlatformHooks.ShareErrorLog())
            {
                StatusMessage = "No errors have been logged yet.";
            }
        });
        ShareGameSaveCommand = new RelayCommand(() =>
        {
            IsMenuOpen = false;

            string text;
            try
            {
                text = GameSession.BuildShareableSaveText(clientState.GameFolder, clientState.EmpireState.Race.Name);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Couldn't read the save file: {ex.Message}";
                return;
            }

            if (!PlatformHooks.ShareText(text))
            {
                StatusMessage = "Sharing isn't available on this platform.";
            }
        });
        RequestCloseGameCommand = new RelayCommand(() => IsConfirmingCloseGame = true);
        CancelCloseGameCommand = new RelayCommand(() => IsConfirmingCloseGame = false);
        ConfirmCloseGameCommand = new RelayCommand(() =>
        {
            IsConfirmingCloseGame = false;
            IsMenuOpen = false;
            GameCloseRequested?.Invoke();
        });

        SelectionService selection = new SelectionService();

        StarMap = new StarMapDocumentViewModel("StarMap", "Star Map", clientState, selection);
        MapSelectionSwitcher = new MapSelectionSwitcherViewModel(clientState, selection);
        // Production built before Inspector, which embeds it as a tab (see InspectorViewModel's
        // own comment on why) - Mobile no longer gives Production its own separate Grid row.
        Production = new ProductionViewModel("Production", "Production", clientState, selection);
        Inspector = new InspectorViewModel("Inspector", "Inspector", clientState, selection, Production);
        Navigator = new NavigatorViewModel("Navigator", "Navigator", clientState, selection);
        Research = new ResearchViewModel("Research", "Research", clientState);
        ShipDesign = new ShipDesignViewModel("ShipDesign", "Ship Design", clientState, selection);
        BattlePlans = new BattlePlansViewModel("BattlePlans", "Battle Plans", clientState);
        Messages = new MessagesViewModel("Messages", "Messages", clientState);
        Summary = new SummaryViewModel("Summary", "Summary", clientState);
        PlayerRelations = new PlayerRelationsViewModel("PlayerRelations", "Player Relations", clientState);
        PlanetReport = new PlanetReportViewModel("PlanetReport", "Planet Report", clientState);
        FleetReport = new FleetReportViewModel("FleetReport", "Fleet Report", clientState);
        BattleReport = new BattleReportViewModel("BattleReport", "Battle Report", clientState);
        ScoreReport = new ScoreReportViewModel("ScoreReport", "Score Report", clientState);
        Help = new HelpViewModel("Help", "Manual");

        RebuildMenuEntries();
    }

    public StarMapDocumentViewModel StarMap { get; }

    public MapSelectionSwitcherViewModel MapSelectionSwitcher { get; }

    public InspectorViewModel Inspector { get; }

    public ProductionViewModel Production { get; }

    public NavigatorViewModel Navigator { get; }

    public ResearchViewModel Research { get; }

    public ShipDesignViewModel ShipDesign { get; }

    public BattlePlansViewModel BattlePlans { get; }

    public MessagesViewModel Messages { get; }

    public SummaryViewModel Summary { get; }

    public PlayerRelationsViewModel PlayerRelations { get; }

    public PlanetReportViewModel PlanetReport { get; }

    public FleetReportViewModel FleetReport { get; }

    public BattleReportViewModel BattleReport { get; }

    public ScoreReportViewModel ScoreReport { get; }

    public HelpViewModel Help { get; }

    private IReadOnlyList<MobileMenuEntryViewModel> menuEntries = Array.Empty<MobileMenuEntryViewModel>();

    /// <summary>One directly-tappable row per section (see this class's own top comment for why
    /// this replaced a dropdown) - rebuilt whenever the selected section changes so exactly one
    /// row's IsSelected highlight stays in sync.</summary>
    public IReadOnlyList<MobileMenuEntryViewModel> MenuEntries
    {
        get => menuEntries;
        private set => SetProperty(ref menuEntries, value);
    }

    public string SelectedPageLabel
    {
        get => PageDefinitions.First(p => p.Value == selectedPage).Label;
        set
        {
            (string Label, Page Value) match = PageDefinitions.FirstOrDefault(p => p.Label == value);
            if (match.Label != null && selectedPage != match.Value)
            {
                selectedPage = match.Value;
                IsMenuOpen = false;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowMapPage));
                OnPropertyChanged(nameof(ShowNavigatorPage));
                OnPropertyChanged(nameof(ShowResearchPage));
                OnPropertyChanged(nameof(ShowShipDesignPage));
                OnPropertyChanged(nameof(ShowBattlePlansPage));
                OnPropertyChanged(nameof(ShowMessagesPage));
                OnPropertyChanged(nameof(ShowSummaryPage));
                OnPropertyChanged(nameof(ShowPlayerRelationsPage));
                OnPropertyChanged(nameof(ShowPlanetReportPage));
                OnPropertyChanged(nameof(ShowFleetReportPage));
                OnPropertyChanged(nameof(ShowBattleReportPage));
                OnPropertyChanged(nameof(ShowScoreReportPage));
                OnPropertyChanged(nameof(ShowHelpPage));
                RebuildMenuEntries();
            }
        }
    }

    private void RebuildMenuEntries()
    {
        var entries = new List<MobileMenuEntryViewModel>();
        foreach ((string label, Page value) in PageDefinitions)
        {
            entries.Add(new MobileMenuEntryViewModel(label, value == selectedPage, () => SelectedPageLabel = label));
        }

        MenuEntries = entries;
    }

    public bool ShowMapPage => selectedPage == Page.Map;

    public bool ShowNavigatorPage => selectedPage == Page.Navigator;

    public bool ShowResearchPage => selectedPage == Page.Research;

    public bool ShowShipDesignPage => selectedPage == Page.ShipDesign;

    public bool ShowBattlePlansPage => selectedPage == Page.BattlePlans;

    public bool ShowMessagesPage => selectedPage == Page.Messages;

    public bool ShowSummaryPage => selectedPage == Page.Summary;

    public bool ShowPlayerRelationsPage => selectedPage == Page.PlayerRelations;

    public bool ShowPlanetReportPage => selectedPage == Page.PlanetReport;

    public bool ShowFleetReportPage => selectedPage == Page.FleetReport;

    public bool ShowBattleReportPage => selectedPage == Page.BattleReport;

    public bool ShowScoreReportPage => selectedPage == Page.ScoreReport;

    public bool ShowHelpPage => selectedPage == Page.Help;
}
