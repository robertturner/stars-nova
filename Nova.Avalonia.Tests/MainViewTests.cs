using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Dock.Model.Core;
using Nova.Avalonia.ViewModels;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Avalonia.Views;
using Nova.Common;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The desktop game screen rendered headlessly (client-interface.md rows 1, 6, 49, 81: the menu
/// bar, its accelerators, panel switching, resize behaviour). Assertions are "contains" checks so
/// menu items added later do not break them.
/// </summary>
[TestFixture]
public class MainViewTests
{
    private static List<MenuItem> AllMenuItems(Menu menu)
    {
        var items = new List<MenuItem>();
        void Walk(IEnumerable<object> children)
        {
            foreach (MenuItem item in children.OfType<MenuItem>())
            {
                items.Add(item);
                Walk(item.Items.Cast<object>());
            }
        }

        Walk(menu.Items.Cast<object>());
        return items;
    }

    private static string Plain(object? header)
    {
        return (header as string ?? string.Empty).Replace("_", string.Empty);
    }

    /// <summary>Rows 1/81: the main menus and the commands behind them exist and are bound.</summary>
    [AvaloniaTest]
    public void MenuBar_HasTheMainMenus_AndEveryLeafItemIsBound()
    {
        var viewModel = new MainViewModel(TestGame.Load());
        Window window = Headless.Show(new MainView { DataContext = viewModel });
        Menu menu = Headless.All<Menu>(window).Single();

        List<string> top = menu.Items.OfType<MenuItem>().Select(item => Plain(item.Header)).ToList();
        Assert.That(top, Is.SupersetOf(new[] { "File", "View", "Turn", "Commands", "Report", "Help" }));

        List<MenuItem> items = AllMenuItems(menu);
        List<MenuItem> leaves = items.Where(item => !item.Items.OfType<MenuItem>().Any()).ToList();
        Assert.That(leaves.Where(item => item.Command == null).Select(item => Plain(item.Header)), Is.Empty, "every leaf item has a command");

        var gestures = items.Where(item => item.InputGesture != null).Select(item => item.InputGesture!.ToString()).ToList();
        Assert.That(gestures, Is.SupersetOf(new[] { "Ctrl+N", "Ctrl+O", "Ctrl+S", "Ctrl+A", "Ctrl+F", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10" }));

        List<string> names = items.Select(item => Plain(item.Header)).ToList();
        Assert.That(names, Has.Some.StartsWith("Technology Browser"));
        Assert.That(names, Has.Some.StartsWith("Race"));
        window.Close();
    }

    /// <summary>Row 81: the accelerators are window key bindings bound to the same commands.</summary>
    [AvaloniaTest]
    public void MainWindow_BindsTheAccelerators()
    {
        var viewModel = new MainViewModel(TestGame.Load());
        var window = new MainWindow(viewModel);

        var bound = window.KeyBindings.Select(binding => binding.Gesture?.ToString()).ToList();
        Assert.That(bound, Is.SupersetOf(new[] { "Ctrl+N", "Ctrl+O", "Ctrl+S", "Ctrl+A", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10" }));
        KeyBinding f5 = window.KeyBindings.Single(binding => binding.Gesture?.ToString() == "F5");
        Assert.That(f5.Command, Is.SameAs(viewModel.ShowPanelCommand));
        Assert.That(f5.CommandParameter, Is.EqualTo(MainViewModel.ResearchPanel));
    }

    /// <summary>The Commands/Report menu bring their panel to the front of its pane.</summary>
    [AvaloniaTest]
    public void ShowPanel_BringsThePanelToTheFront()
    {
        var viewModel = new MainViewModel(TestGame.Load());
        Window window = Headless.Show(new MainView { DataContext = viewModel });

        foreach (string panel in new[] { MainViewModel.ResearchPanel, MainViewModel.BattlePlansPanel, MainViewModel.ScoreReportPanel, MainViewModel.ShipDesignPanel })
        {
            viewModel.ShowPanelCommand.Execute(panel);
            Headless.Pump();
            IDockable shown = FindDockable(viewModel.Layout, panel)!;
            Assert.That(shown, Is.Not.Null, panel);
            Assert.That(((IDock)shown.Owner!).ActiveDockable, Is.SameAs(shown), panel);
        }

        window.Close();
    }

    /// <summary>Row 49: F2 / Help > Technology Browser shows the browser, and closes it when it is
    /// already the front panel.</summary>
    [AvaloniaTest]
    public void TechnologyBrowserToggle_ShowsThenCloses()
    {
        var viewModel = new MainViewModel(TestGame.Load());
        Window window = Headless.Show(new MainView { DataContext = viewModel });

        viewModel.ToggleTechnologyBrowserCommand.Execute(null);
        Headless.Pump();
        IDockable browser = FindDockable(viewModel.Layout, MainViewModel.TechnologyBrowserPanel)!;
        Assert.That(browser, Is.Not.Null);
        Assert.That(((IDock)browser.Owner!).ActiveDockable, Is.SameAs(browser));
        Assert.That(Headless.All<Views.Panels.TechnologyBrowserView>(window), Is.Not.Empty, "the browser view is on screen");

        viewModel.ToggleTechnologyBrowserCommand.Execute(null);
        Headless.Pump();
        Assert.That(FindDockable(viewModel.Layout, MainViewModel.TechnologyBrowserPanel), Is.Null, "a second toggle closes it");

        viewModel.ToggleTechnologyBrowserCommand.Execute(null);
        Assert.That(FindDockable(viewModel.Layout, MainViewModel.TechnologyBrowserPanel), Is.Not.Null, "and a third shows it again");
        window.Close();
    }

    /// <summary>View > Zoom: one item per step, the current one checked.</summary>
    [AvaloniaTest]
    public void ViewZoomMenu_ChecksTheCurrentStep()
    {
        var viewModel = new MainViewModel(TestGame.Load());

        Assert.That(viewModel.ZoomLabels, Has.Count.EqualTo(9));
        viewModel.SetZoomCommand.Execute("8");

        Assert.That(viewModel.StarMap.ZoomLevelIndex, Is.EqualTo(8));
        Assert.That(viewModel.ZoomChecks.Select((check, index) => (check, index)).Where(t => t.check).Select(t => t.index), Is.EqualTo(new[] { 8 }));

        viewModel.StarMap.ZoomOutCommand.Execute(null);
        Assert.That(viewModel.ZoomChecks[7], Is.True, "the checks follow a zoom made on the map itself");
    }

    /// <summary>View > Planets mirrors the map's six-way mode.</summary>
    [AvaloniaTest]
    public void ViewPlanetsMenu_FollowsTheMapMode()
    {
        var viewModel = new MainViewModel(TestGame.Load());

        viewModel.SetPlanetModeCommand.Execute("2");
        Assert.That(viewModel.StarMap.PlanetMode, Is.EqualTo(2));
        Assert.That(viewModel.PlanetModeChecks.Count(check => check), Is.EqualTo(1));
        Assert.That(viewModel.PlanetModeChecks[2], Is.True);
    }

    /// <summary>View > Race (F8) offers the player's own race for read-only viewing.</summary>
    [AvaloniaTest]
    public void ViewRace_RaisesTheOwnRace()
    {
        var viewModel = new MainViewModel(TestGame.Load());
        Race? shown = null;
        viewModel.RaceViewRequested += race => shown = race;

        Assert.That(viewModel.ViewRaceCommand.CanExecute(null), Is.True);
        viewModel.ViewRaceCommand.Execute(null);

        Assert.That(shown?.Name, Is.EqualTo(TestGame.PacketRace));
    }

    /// <summary>Row 6: the docked layout follows the window size.</summary>
    [AvaloniaTest]
    public void Layout_FollowsTheWindowSize()
    {
        var viewModel = new MainViewModel(TestGame.Load());
        var view = new MainView { DataContext = viewModel };
        Window window = Headless.Show(view, 1280, 800);
        double wideWidth = view.Bounds.Width;
        var map = Headless.All<Views.Panels.StarMapDocumentView>(window).Single();
        double wideMap = map.Bounds.Width;

        window.Width = 900;
        window.Height = 600;
        Headless.Pump();

        Assert.That(view.Bounds.Width, Is.LessThan(wideWidth));
        Assert.That(view.Bounds.Width, Is.EqualTo(900).Within(1));
        Assert.That(map.Bounds.Width, Is.LessThan(wideMap), "the map document shrinks with the window");
        Assert.That(Headless.DistinctColours(window), Is.GreaterThan(4));
        window.Close();
    }

    private static IDockable? FindDockable(IDockable root, string id)
    {
        if (root.Id == id)
        {
            return root;
        }

        if (root is IDock dock && dock.VisibleDockables != null)
        {
            foreach (IDockable child in dock.VisibleDockables)
            {
                IDockable? found = FindDockable(child, id);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
    }
}
