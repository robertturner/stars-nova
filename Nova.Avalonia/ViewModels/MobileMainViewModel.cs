using System.Collections.Generic;
using System.Linq;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Android's own game screen - not the desktop dock layout (MainViewModel/NovaDockFactory),
/// which turned out not to translate to a phone at all: panels shrank to unreadable slivers, and
/// AvaloniaDock's splitters are far too small a drag target for touch. Instead:
///
/// - "Map" groups everything that's about the Star Map and whatever's currently selected on it -
///   the map itself (now draggable/zoomable via touch - see StarMapDocumentViewModel), the
///   Inspector (planet and fleet detail, plus fleet order editing), and Production (the selected
///   planet's build queue) - stacked in one screen instead of separate dock panels, since all
///   three already react to the exact same shared SelectionService.
/// - Everything else that ISN'T about a specific map selection (Navigator's browse-by-list,
///   Research, Ship Design, Battle Plans, Messages, Summary, Player Relations, the Planet/Fleet/
///   Battle/Score report tables, and the manual) gets its own full-screen page, switched via a
///   ComboBox rather than a TabControl or a row of buttons - both have their own confirmed-live
///   touch bugs on this app (see RaceDesignerViewModel.Page's own comment) - a ComboBox is
///   already proven reliable via touch elsewhere in this app.
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

    public MobileMainViewModel(ClientData clientState) : base(clientState)
    {
        SelectionService selection = new SelectionService();

        StarMap = new StarMapDocumentViewModel("StarMap", "Star Map", clientState, selection);
        Inspector = new InspectorViewModel("Inspector", "Inspector", clientState, selection);
        Production = new ProductionViewModel("Production", "Production", clientState, selection);
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
    }

    public StarMapDocumentViewModel StarMap { get; }

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

    public IReadOnlyList<string> PageLabels { get; } = PageDefinitions.Select(p => p.Label).ToList();

    public string SelectedPageLabel
    {
        get => PageDefinitions.First(p => p.Value == selectedPage).Label;
        set
        {
            (string Label, Page Value) match = PageDefinitions.FirstOrDefault(p => p.Label == value);
            if (match.Label != null && selectedPage != match.Value)
            {
                selectedPage = match.Value;
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
            }
        }
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
