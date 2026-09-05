using System;
using System.Collections.Generic;
using Dock.Model.Core;
using Dock.Model.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Nova.Avalonia.ViewModels.Panels;

namespace Nova.Avalonia.Docking;

/// <summary>
/// Builds the default panel layout for the main game window: Navigator/Inspector
/// tabbed on the left, the Star Map as the fixed center document, Production/Research
/// tabbed on the right, and Messages/Summary tabbed along the bottom - matching the
/// desktop layout from the approved "Nova Cockpit" design sketch. Every panel here is
/// a placeholder (see ViewModels/Panels) until wired to real game data.
/// </summary>
public class NovaDockFactory : Factory
{
    public override IRootDock CreateLayout()
    {
        var navigator = new PlaceholderToolViewModel(
            "Navigator", "Navigator", "Empire tree: planets, fleets, designs.");
        var inspector = new PlaceholderToolViewModel(
            "Inspector", "Inspector", "Details for whatever is selected on the map.");

        var leftPane = new ToolDock
        {
            Id = "LeftPane",
            ActiveDockable = navigator,
            VisibleDockables = CreateList<IDockable>(navigator, inspector),
            Alignment = Alignment.Left,
            Proportion = 0.22,
        };

        var starMap = new PlaceholderDocumentViewModel(
            "StarMap", "Star Map", "The main map view - always open, never closed.");

        var centerDocuments = new DocumentDock
        {
            Id = "CenterDocuments",
            ActiveDockable = starMap,
            VisibleDockables = CreateList<IDockable>(starMap),
            CanCreateDocument = false,
        };

        var production = new PlaceholderToolViewModel(
            "Production", "Production", "Production queue for the selected planet.");
        var research = new PlaceholderToolViewModel(
            "Research", "Research", "Tech levels and the research budget slider.");

        var rightPane = new ToolDock
        {
            Id = "RightPane",
            ActiveDockable = production,
            VisibleDockables = CreateList<IDockable>(production, research),
            Alignment = Alignment.Right,
            Proportion = 0.22,
        };

        var messages = new PlaceholderToolViewModel(
            "Messages", "Messages", "This turn's events, oldest first.");
        var summary = new PlaceholderToolViewModel(
            "Summary", "Summary", "Empire-wide totals: planets, fleets, resources.");

        var bottomPane = new ToolDock
        {
            Id = "BottomPane",
            ActiveDockable = messages,
            VisibleDockables = CreateList<IDockable>(messages, summary),
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
