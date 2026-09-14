using System;
using System.Collections.Generic;
using Dock.Model.Core;
using Dock.Model.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Avalonia.ViewModels.Panels;

namespace Nova.Avalonia.Docking;

/// <summary>
/// Builds the default panel layout for the main game window: Navigator/Inspector
/// tabbed on the left, the Star Map as the fixed center document, Production/Research
/// tabbed on the right, and Messages/Summary tabbed along the bottom - matching the
/// desktop layout from the approved "Nova Cockpit" design sketch. Every panel is wired to a
/// real loaded game (see GameSession) - Navigator/Inspector/Production react to the shared
/// SelectionService, Research/Messages/Summary are empire-wide and static per turn, and the
/// Star Map draws every known star. None of them support editing/orders yet.
/// </summary>
public class NovaDockFactory : Factory
{
    private readonly ClientData clientState;

    public NovaDockFactory(ClientData clientState)
    {
        this.clientState = clientState;
    }

    public override IRootDock CreateLayout()
    {
        var selection = new ViewModels.SelectionService();

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

        var rightPane = new ToolDock
        {
            Id = "RightPane",
            ActiveDockable = production,
            VisibleDockables = CreateList<IDockable>(production, research, shipDesign),
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
        var help = new HelpViewModel("Help", "Manual");

        var bottomPane = new ToolDock
        {
            Id = "BottomPane",
            ActiveDockable = messages,
            VisibleDockables = CreateList<IDockable>(
                messages, summary, playerRelations, battlePlans,
                planetReport, fleetReport, battleReport, scoreReport, help),
            Alignment = Alignment.Bottom,
            Proportion = 0.22,
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

        var root = CreateRootDock();
        root.Id = "Root";
        root.VisibleDockables = CreateList<IDockable>(mainLayout);
        root.ActiveDockable = mainLayout;
        root.DefaultDockable = mainLayout;

        return root;
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
