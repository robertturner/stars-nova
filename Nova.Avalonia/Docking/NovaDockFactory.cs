using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Core;
using Dock.Model.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;
using Nova.Avalonia.ViewModels.Panels;

namespace Nova.Avalonia.Docking;

/// <summary>
/// Builds the default panel layout for the main game window: Navigator/Inspector
/// tabbed on the left, the Star Map as the fixed center document, Production/Research
/// tabbed on the right, and Messages/Summary tabbed along the bottom - matching the
/// desktop layout from the approved "Nova Cockpit" design sketch. Every panel is wired to a
/// real loaded game (see GameSession) - Navigator/Inspector/Production react to the shared
/// SelectionService, Research/Messages/Summary are empire-wide and static per turn, and the
/// Star Map draws every known star, fleet, minefield, wormhole and mineral packet. Orders are
/// issued from the Inspector (fleet waypoints, cargo, split/merge, packet destination),
/// Production, Research, Ship Design, Battle Plans and the map itself (waypoint targeting,
/// Shift+click packet destination), each pushing an ICommand onto ClientData.Commands.
/// </summary>
public class NovaDockFactory : Factory
{
    private readonly ClientData clientState;

    // Every panel CreateLayout built, by Id - lets the desktop menu bar and its accelerators
    // (behavior-specs-10/client-interface.md command table: F4 Ship Design, F5 Research, ...)
    // bring a panel to the front; see ShowPanel.
    private readonly Dictionary<string, IDockable> panels = new Dictionary<string, IDockable>();

    // The pane each panel was created in, so a closed panel can be re-added to it (Dock clears
    // a closed dockable's Owner).
    private readonly Dictionary<string, IDock> homes = new Dictionary<string, IDock>();

    private ViewModels.SelectionService? selection;

    /// <summary>The Star Map document CreateLayout built - the View menu's Find, Zoom and
    /// "Planets:" mode items act on it.</summary>
    public StarMapDocumentViewModel? StarMap { get; private set; }

    public NovaDockFactory(ClientData clientState)
    {
        this.clientState = clientState;
    }

    public override IRootDock CreateLayout()
    {
        var selection = new ViewModels.SelectionService();
        this.selection = selection;

        var navigator = new NavigatorViewModel("Navigator", "Navigator", clientState, selection);
        var inspector = new InspectorViewModel("Inspector", "Inspector", clientState, selection);

        var leftPane = new ToolDock
        {
            Id = "LeftPane",
            ActiveDockable = navigator,
            VisibleDockables = CreateList<IDockable>(navigator, inspector),
            Alignment = Alignment.Left,
            Proportion = 0.22,
        };

        var starMap = new StarMapDocumentViewModel("StarMap", "Star Map", clientState, selection);
        StarMap = starMap;

        // The Inspector's scanner-display % control edits the map's own value (desktop only;
        // the Android screen builds its Inspector without it).
        inspector.AttachScannerDisplay(() => starMap.ScannerPercentage, value => starMap.ScannerPercentage = value);
        starMap.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StarMapDocumentViewModel.ScannerPercentage))
            {
                inspector.RefreshScannerDisplay();
            }
        };

        var centerDocuments = new DocumentDock
        {
            Id = "CenterDocuments",
            ActiveDockable = starMap,
            VisibleDockables = CreateList<IDockable>(starMap),
            CanCreateDocument = false,
        };

        var production = new ProductionViewModel("Production", "Production", clientState, selection);
        var research = new ResearchViewModel("Research", "Research", clientState);
        var shipDesign = new ShipDesignViewModel("ShipDesign", "Ship Design", clientState, selection);
        var productionTemplates = new ProductionTemplatesViewModel("ProductionTemplates", "Production Templates", clientState, selection);
        var technologyBrowser = new TechnologyBrowserViewModel("TechnologyBrowser", "Technology Browser", clientState);

        // The Production panel's template list follows edits made in the template manager.
        productionTemplates.TemplatesChanged += production.RefreshTemplateSlots;

        var rightPane = new ToolDock
        {
            Id = "RightPane",
            ActiveDockable = production,
            VisibleDockables = CreateList<IDockable>(production, research, shipDesign, productionTemplates, technologyBrowser),
            Alignment = Alignment.Right,
            Proportion = 0.22,
        };

        var messages = new MessagesViewModel("Messages", "Messages", clientState);
        var summary = new SummaryViewModel("Summary", "Summary", clientState);
        var playerRelations = new PlayerRelationsViewModel("PlayerRelations", "Player Relations", clientState);
        var battlePlans = new BattlePlansViewModel("BattlePlans", "Battle Plans", clientState);
        var planetReport = new PlanetReportViewModel("PlanetReport", "Planet Report", clientState);
        var fleetReport = new FleetReportViewModel("FleetReport", "Fleet Report", clientState);
        var battleReport = new BattleReportViewModel("BattleReport", "Battle Report", clientState);
        var scoreReport = new ScoreReportViewModel("ScoreReport", "Score Report", clientState);
        var victoryConditions = new VictoryConditionsViewModel("VictoryConditions", "Victory Conditions");
        var help = new HelpViewModel("Help", "Manual");

        var bottomPane = new ToolDock
        {
            Id = "BottomPane",
            ActiveDockable = messages,
            VisibleDockables = CreateList<IDockable>(
                messages, summary, playerRelations, battlePlans,
                planetReport, fleetReport, battleReport, scoreReport, victoryConditions, help),
            Alignment = Alignment.Bottom,
            Proportion = 0.22,
        };

        // Tapping a battle message (see MessageItemViewModel.ReplayCommand) jumps straight to
        // that battle's step log: select it in the Battle Report panel, then bring that panel's
        // tab to the front via IFactory.SetActiveDockable (this class - NovaDockFactory - IS the
        // IFactory). Wired here rather than in either ViewModel since this is the one place that
        // already holds direct references to both panels.
        messages.BattleReplayRequested += report =>
        {
            battleReport.SelectBattle(report);
            SetActiveDockable(battleReport);
        };

        // Message-click routing (Nova.Client.MessageRouting): 62/63 open the named planet's
        // production queue, other planet notices select the planet, tech notices open Research
        // or the Technology Browser.
        messages.DestinationRequested += destination =>
        {
            switch (destination.Kind)
            {
                case MessageDestinationKind.ProductionQueue:
                    SelectPlanet(destination.PlanetName);
                    ShowPanel("Production");
                    break;
                case MessageDestinationKind.Planet:
                    SelectPlanet(destination.PlanetName);
                    ShowPanel("Inspector");
                    break;
                case MessageDestinationKind.Research:
                    ShowPanel("Research");
                    break;
                case MessageDestinationKind.TechnologyBrowser:
                    ShowPanel("TechnologyBrowser");
                    break;
            }
        };

        var centerColumn = new ProportionalDock
        {
            Id = "CenterColumn",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                centerDocuments,
                new ProportionalDockSplitter(),
                bottomPane),
        };

        var mainLayout = new ProportionalDock
        {
            Id = "MainLayout",
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftPane,
                new ProportionalDockSplitter(),
                centerColumn,
                new ProportionalDockSplitter(),
                rightPane),
        };

        foreach (IDockable panel in new IDockable[]
        {
            navigator, inspector, starMap, production, research, shipDesign, productionTemplates, technologyBrowser,
            messages, summary, playerRelations, battlePlans, planetReport, fleetReport, battleReport, scoreReport,
            victoryConditions, help,
        })
        {
            panels[panel.Id] = panel;
        }

        foreach (IDock pane in new IDock[] { leftPane, centerDocuments, rightPane, bottomPane })
        {
            foreach (IDockable member in pane.VisibleDockables ?? new List<IDockable>())
            {
                homes[member.Id] = pane;
            }
        }

        sidePanes = new[] { leftPane, rightPane, bottomPane };

        var root = CreateRootDock();
        root.Id = "Root";
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;

        return root;
    }

    /// <summary>
    /// Brings the panel with this Id (see CreateLayout) to the front of its pane, re-adding it
    /// to its pane first if the player had closed it. False for an unknown Id.
    /// </summary>
    public bool ShowPanel(string id)
    {
        if (!panels.TryGetValue(id, out IDockable? panel))
        {
            return false;
        }

        if (panel is VictoryConditionsViewModel victory)
        {
            victory.Refresh();
        }

        IDock? dock = panel.Owner as IDock;
        if (dock == null && homes.TryGetValue(id, out IDock? home))
        {
            dock = home;
        }

        if (dock is IDock owner)
        {
            if (owner.VisibleDockables != null && !owner.VisibleDockables.Contains(panel))
            {
                AddDockable(owner, panel);
            }

            SetActiveDockable(panel);
            SetFocusedDockable(owner, panel);
        }
        else
        {
            SetActiveDockable(panel);
        }

        return true;
    }

    /// <summary>
    /// Shows the panel, or closes it when it is already the front panel of its pane - the
    /// Technology Browser's F2 "shows or closes" (client-interface.md command 256/136).
    /// </summary>
    public bool TogglePanel(string id)
    {
        if (!panels.TryGetValue(id, out IDockable? panel))
        {
            return false;
        }

        if (panel.Owner is IDock owner && owner.ActiveDockable == panel
            && owner.VisibleDockables != null && owner.VisibleDockables.Contains(panel))
        {
            CloseDockable(panel);
            return true;
        }

        return ShowPanel(id);
    }

    // The left, right and bottom panel groups (the window-layout preset sizes them).
    private ToolDock[] sidePanes = Array.Empty<ToolDock>();

    /// <summary>The panel with this Id (see CreateLayout), or null.</summary>
    public T? GetPanel<T>(string id) where T : class
    {
        return panels.TryGetValue(id, out IDockable? panel) ? panel as T : null;
    }

    /// <summary>
    /// View > Window Layout (client-interface.md commands 130-132): recomputes the workspace
    /// layout for the preset - each side/bottom panel group gets
    /// Nova.Client.Shell.WindowLayout.SidePaneProportion of the window (a SPEC GAP stand-in:
    /// the spec does not say what each preset changes).
    /// </summary>
    public void ApplyWindowLayout(Nova.Client.Shell.WindowLayoutPreset preset)
    {
        double proportion = Nova.Client.Shell.WindowLayout.SidePaneProportion(preset);
        foreach (ToolDock pane in sidePanes)
        {
            pane.Proportion = proportion;
        }
    }

    /// <summary>Selects one of this empire's planets (or its report of another) by name.</summary>
    public void SelectPlanet(string? name)
    {
        if (selection == null || string.IsNullOrEmpty(name))
        {
            return;
        }

        object? target = clientState.EmpireState.OwnedStars.Values.FirstOrDefault(star => star.Name == name)
            ?? (object?)(clientState.EmpireState.StarReports.TryGetValue(name, out StarIntel? report) ? report : null);
        if (target != null)
        {
            selection.Selected = target;
        }
    }

    public override void InitLayout(IDockable layout)
    {
        DockableLocator = new Dictionary<string, Func<IDockable?>>();

        HostWindowLocator = new Dictionary<string, Func<IHostWindow>>
        {
            [nameof(IDockWindow)] = () => new Dock.Avalonia.Controls.HostWindow(),
        };

        base.InitLayout(layout);
    }
}
