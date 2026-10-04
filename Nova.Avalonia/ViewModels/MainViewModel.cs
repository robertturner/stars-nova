using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Controls;
using Nova.Avalonia.Docking;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Client.Map;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// The desktop game screen - the AvaloniaDock multi-panel layout (see NovaDockFactory). Only
/// ever used by the desktop head (MainWindow); the single-view Android host uses
/// MobileMainViewModel/MobileMainView instead - a docked, freely-resizable panel layout designed
/// for a mouse and a large screen turned out not to translate to a phone at all (confirmed live:
/// panels shrank to unreadable slivers, and dock splitters are far too small a drag target for
/// touch) - see MobileMainViewModel's own comment for that screen's very different structure.
/// Turn-submission/About plumbing lives in the shared GameShellViewModelBase instead of here, so
/// both screens get the exact same behavior without duplicating it.
///
/// Also backs the desktop menu bar and its accelerators (behavior-specs-10/client-interface.md
/// "Main-window command table"): only the commands this app actually has are wired - see
/// MainView.axaml for the list and for which of the original's commands have no counterpart.
/// </summary>
public partial class MainViewModel : GameShellViewModelBase
{
    /// <summary>Panel Ids (NovaDockFactory.CreateLayout) the menu bar can bring to the front.</summary>
    public const string ShipDesignPanel = "ShipDesign";
    public const string ResearchPanel = "Research";
    public const string BattlePlansPanel = "BattlePlans";
    public const string PlayerRelationsPanel = "PlayerRelations";
    public const string PlanetReportPanel = "PlanetReport";
    public const string FleetReportPanel = "FleetReport";
    public const string BattleReportPanel = "BattleReport";
    public const string ScoreReportPanel = "ScoreReport";
    public const string HelpPanel = "Help";
    public const string MessagesPanel = "Messages";
    public const string ProductionTemplatesPanel = "ProductionTemplates";
    public const string TechnologyBrowserPanel = "TechnologyBrowser";
    public const string VictoryConditionsPanel = "VictoryConditions";

    private readonly NovaDockFactory dockFactory;

    // The report the Report menu last opened - F3 re-issues it (Planets if none yet).
    private string lastReportPanel = PlanetReportPanel;

    public IFactory Factory => dockFactory;

    public IRootDock Layout { get; }

    /// <summary>Brings a panel (by Id) to the front: Commands and Report menu items, F1-F10.</summary>
    public IRelayCommand<string> ShowPanelCommand { get; }

    /// <summary>Help > Technology Browser (F2): shows or closes the browser (client-interface.md
    /// command 256/136).</summary>
    public IRelayCommand ToggleTechnologyBrowserCommand { get; }

    /// <summary>F3: re-issues the last report opened from the Report menu, or Planets.</summary>
    public IRelayCommand ShowLastReportCommand { get; }

    /// <summary>File > Save (Ctrl+S): saves the client state (ClientData.Save) without
    /// submitting the turn (Save And Submit / Ctrl+A is SubmitTurnCommand).</summary>
    public IRelayCommand SaveCommand { get; }

    /// <summary>File > New (Ctrl+N), Open (Ctrl+O), Custom Race Wizard, Close: return to the
    /// startup screen, opening the given sub-screen (see StartScreenRequested).</summary>
    public IRelayCommand NewGameCommand { get; }

    public IRelayCommand OpenGameCommand { get; }

    public IRelayCommand RaceWizardCommand { get; }

    public IRelayCommand CloseGameCommand { get; }

    /// <summary>File > Exit.</summary>
    public IRelayCommand ExitCommand { get; }

    /// <summary>Raised by New/Open/Custom Race Wizard/Close with "new", "open", "race" or "" -
    /// the host (MainWindow) saves nothing itself; it shows the startup window on that screen
    /// and closes the game window.</summary>
    public event Action<string>? StartScreenRequested;

    /// <summary>Raised by File > Exit - the host closes the application window.</summary>
    public event Action? ExitRequested;

    /// <summary>The Star Map document (View > Find, Zoom and Planets act on it).</summary>
    public StarMapDocumentViewModel StarMap { get; }

    /// <summary>View > Zoom (commands 3901-3909): the nine steps -4..+4, as "0".."8".</summary>
    public IRelayCommand<string> SetZoomCommand { get; }

    /// <summary>The View > Zoom labels (25% .. 400%).</summary>
    public IReadOnlyList<string> ZoomLabels { get; } =
        Enumerable.Range(MapZoom.MinLevel, MapZoom.MaxLevel - MapZoom.MinLevel + 1).Select(MapZoom.Label).ToList();

    private IReadOnlyList<bool> zoomChecks = Array.Empty<bool>();

    /// <summary>The check mark of each View > Zoom item ("the current zoom step" is checked,
    /// client-interface.md "Runtime menu state").</summary>
    public IReadOnlyList<bool> ZoomChecks
    {
        get => zoomChecks;
        private set => SetProperty(ref zoomChecks, value);
    }

    /// <summary>View > Planets: the six-way "Planets:" view mode (the digit keys 1-6; the map's
    /// ring/bar overlay), as "0".."5". An addition to the original's View menu, which reaches the
    /// mode only through the keys and the on-map selector.</summary>
    public IRelayCommand<string> SetPlanetModeCommand { get; }

    public IReadOnlyList<string> PlanetModeLabels => MapViewOptions.ModeLabels;

    private IReadOnlyList<bool> planetModeChecks = Array.Empty<bool>();

    public IReadOnlyList<bool> PlanetModeChecks
    {
        get => planetModeChecks;
        private set => SetProperty(ref planetModeChecks, value);
    }

    public MainViewModel(ClientData clientState) : base(clientState)
    {
        dockFactory = new NovaDockFactory(clientState);
        Layout = dockFactory.CreateLayout();
        dockFactory.InitLayout(Layout);
        StarMap = dockFactory.StarMap!;

        SetZoomCommand = new RelayCommand<string>(index =>
        {
            if (int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                StarMap.ZoomLevelIndex = value;
                RefreshViewChecks();
            }
        });
        SetPlanetModeCommand = new RelayCommand<string>(index =>
        {
            if (int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                StarMap.PlanetMode = value;
                RefreshViewChecks();
            }
        });
        StarMap.PropertyChanged += OnStarMapPropertyChanged;
        RefreshViewChecks();

        ShowPanelCommand = new RelayCommand<string>(ShowPanel);
        ShowLastReportCommand = new RelayCommand(() => ShowPanel(lastReportPanel));
        ToggleTechnologyBrowserCommand = new RelayCommand(() => dockFactory.TogglePanel(TechnologyBrowserPanel));
        SaveCommand = new RelayCommand(Save);
        NewGameCommand = new RelayCommand(() => StartScreenRequested?.Invoke("new"));
        OpenGameCommand = new RelayCommand(() => StartScreenRequested?.Invoke("open"));
        RaceWizardCommand = new RelayCommand(() => StartScreenRequested?.Invoke("race"));
        CloseGameCommand = new RelayCommand(() => StartScreenRequested?.Invoke(""));
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke());

        // Title, recent files, toolbar, window layout, sound items, Print Map, autosave and the
        // hotkey relay (MainViewModel.Shell.cs).
        InitializeShell();
    }

    private void OnStarMapPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StarMapDocumentViewModel.ZoomLevel) || e.PropertyName == nameof(StarMapDocumentViewModel.PlanetMode))
        {
            RefreshViewChecks();
        }
    }

    /// <summary>Re-publishes the View menu's check marks (zoom step and planet mode) - also
    /// after a click, since a radio menu item toggles its own check before the command runs.</summary>
    private void RefreshViewChecks()
    {
        int zoomIndex = StarMap.ZoomLevelIndex;
        ZoomChecks = Enumerable.Range(0, ZoomLabels.Count).Select(index => index == zoomIndex).ToList();
        int mode = StarMap.PlanetMode;
        PlanetModeChecks = Enumerable.Range(0, PlanetModeLabels.Count).Select(index => index == mode).ToList();
    }

    private void ShowPanel(string? panelId)
    {
        if (string.IsNullOrEmpty(panelId))
        {
            return;
        }

        if (panelId == PlanetReportPanel || panelId == FleetReportPanel || panelId == BattleReportPanel)
        {
            lastReportPanel = panelId;
        }

        dockFactory.ShowPanel(panelId);
    }

    private void Save()
    {
        try
        {
            SaveOrders();
            StatusMessage = "Saved (turn not submitted).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
}
