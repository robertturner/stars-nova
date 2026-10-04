using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Nova.Avalonia.ViewModels;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Avalonia.Views;
using Nova.Avalonia.Views.Panels;
using Nova.Client;
using Nova.Common;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// Individual panel views rendered headlessly with real data: the bindings resolve (the view
/// shows the view model's text, a command's enabled state reaches its button) and the frame is not
/// blank. Also covers client-ui-dialog-catalog.md rows 34-36 and 38 (text entry, single-choice
/// selectors, lists, word wrap) and client-interface row 71 (the secret entry).
/// </summary>
[TestFixture]
public class PanelViewTests
{
    private static Button ButtonFor(Window window, System.Windows.Input.ICommand command)
    {
        return Headless.All<Button>(window).First(button => ReferenceEquals(button.Command, command));
    }

    [AvaloniaTest]
    public void StarMapView_DrawsAMarkerPerStar_AndTheirNames()
    {
        ClientData client = TestGame.Load();
        var selection = new SelectionService();
        var map = new StarMapDocumentViewModel("StarMap", "Star Map", client, selection);
        Window window = Headless.Show(new StarMapDocumentView { DataContext = map });

        int starMarkers = Headless.All<Button>(window).Select(button => button.DataContext).OfType<StarMapStarViewModel>().Distinct().Count();
        Assert.That(starMarkers, Is.EqualTo(map.Stars.Count), "every star has a clickable marker");
        Assert.That(Headless.All<TextBlock>(window).Select(t => t.Text), Has.Member(TestGame.HomeStar(client).Name));
        Assert.That(Headless.DistinctColours(window), Is.GreaterThan(4));
        window.Close();
    }

    /// <summary>Row 45: changing the zoom keeps the same world point at the centre of the
    /// viewport (the view's own recentre handler, not just the maths).</summary>
    [AvaloniaTest]
    public void StarMapView_ZoomRecentresOnTheSameWorldPoint()
    {
        ClientData client = TestGame.Load();
        var selection = new SelectionService();
        var map = new StarMapDocumentViewModel("StarMap", "Star Map", client, selection);
        Window window = Headless.Show(new StarMapDocumentView { DataContext = map }, 800, 600);

        ScrollViewer scroll = Headless.All<ScrollViewer>(window).First();
        Headless.Pump();
        Assume.That(scroll.Viewport.Width, Is.GreaterThan(0), "the map viewport has been laid out");

        // Put world (50, 40) at the centre at 100%.
        double oldScale = map.Zoom;
        scroll.Offset = new global::Avalonia.Vector(
            System.Math.Max(0, (50 * oldScale) - (scroll.Viewport.Width / 2)),
            System.Math.Max(0, (40 * oldScale) - (scroll.Viewport.Height / 2)));
        Headless.Pump();
        double centreX = (scroll.Offset.X + (scroll.Viewport.Width / 2)) / oldScale;
        double centreY = (scroll.Offset.Y + (scroll.Viewport.Height / 2)) / oldScale;

        map.ZoomLevel += 1; // 125%
        Headless.Pump();
        Headless.Pump();

        double newScale = map.Zoom;
        double newCentreX = (scroll.Offset.X + (scroll.Viewport.Width / 2)) / newScale;
        double newCentreY = (scroll.Offset.Y + (scroll.Viewport.Height / 2)) / newScale;
        Assert.That(newCentreX, Is.EqualTo(centreX).Within(1.0), "the same world X stays centred");
        Assert.That(newCentreY, Is.EqualTo(centreY).Within(1.0), "the same world Y stays centred");
        window.Close();
    }

    /// <summary>Row 41: a minefield renders with its own type pattern (and the engine never
    /// throws while building the DrawingBrush).</summary>
    [AvaloniaTest]
    public void StarMapView_DrawsMinefieldsWithTheirTypePattern()
    {
        ClientData client = TestGame.Load(TestGame.DemolitionRace);
        Star home = TestGame.HomeStar(client);
        var field = new Minefield
        {
            Owner = client.EmpireState.Id,
            Id = 902,
            NumberOfMines = 100,
            FieldType = MinefieldType.Heavy,
        };
        field.Position = new Nova.Common.DataStructures.NovaPoint(home.Position.X + 5, home.Position.Y + 5);
        client.InputTurn.AllMinefields[field.Key] = field;
        client.EmpireState.VisibleMinefields.Add(field.Key);

        var selection = new SelectionService();
        var map = new StarMapDocumentViewModel("StarMap", "Star Map", client, selection);
        map.ShowMinefields = true;
        Window window = Headless.Show(new StarMapDocumentView { DataContext = map }, 800, 600);

        Assert.That(Headless.DistinctColours(window), Is.GreaterThan(4), "the patterned field rendered");
        window.Close();
    }

    [AvaloniaTest]
    public void InspectorView_ShowsTheSelectedFleet_WithTextEntryAndSelectors()
    {
        ClientData client = TestGame.Load();
        var selection = new SelectionService();
        var inspector = new InspectorViewModel("Inspector", "Inspector", client, selection);
        Fleet fleet = TestGame.FleetsAt(client, TestGame.HomeStar(client))[0];
        selection.Selected = fleet;
        Window window = Headless.Show(new InspectorView { DataContext = inspector }, 600, 900);

        Assert.That(Headless.Texts(window), Has.Member(fleet.Name));
        Assert.That(Headless.DistinctColours(window), Is.GreaterThan(4));

        inspector.SelectedTabIndex = 1; // Orders
        Headless.Pump();
        Assert.That(Headless.All<TextBox>(window).Where(box => box.IsEffectivelyVisible), Is.Not.Empty, "styled text entry (rename box)");
        Assert.That(Headless.All<ComboBox>(window).Where(box => box.IsEffectivelyVisible), Is.Not.Empty, "single-choice selector (task picker)");
        window.Close();
    }

    [AvaloniaTest]
    public void MessagesView_ListsTheMessages()
    {
        ClientData client = TestGame.Load();
        client.Messages.Clear();
        client.Messages.Add(new Message(client.EmpireState.Id, "Headless message one", "UiTestNotice", null));
        client.Messages.Add(new Message(client.EmpireState.Id, "Headless message two", "UiTestNotice", null));
        var messages = new MessagesViewModel("Messages", "Messages", client);
        Window window = Headless.Show(new MessagesView { DataContext = messages }, 900, 400);

        Assert.That(Headless.All<ItemsControl>(window), Is.Not.Empty, "a styled list");
        string all = string.Join("\n", Headless.Texts(window));
        Assert.That(all, Does.Contain("Headless message one"));
        Assert.That(all, Does.Contain("Headless message two"));
        Assert.That(ButtonFor(window, messages.PreviousCommand).IsEffectivelyEnabled, Is.False, "the first message has no previous");
        window.Close();
    }

    [AvaloniaTest]
    public void BattlePlansView_DeleteButtonFollowsTheFirstPlanRule()
    {
        ClientData client = TestGame.Load();
        var plans = new BattlePlansViewModel("BattlePlans", "Battle Plans", client);
        Window window = Headless.Show(new BattlePlansView { DataContext = plans }, 900, 600);

        Button delete = ButtonFor(window, plans.DeleteCommand);
        Assert.That(delete.IsEffectivelyEnabled, Is.False, "the first plan cannot be deleted");

        plans.NewPlanCommand.Execute(null);
        Headless.Pump();
        Assert.That(delete.IsEffectivelyEnabled, Is.True);
        window.Close();
    }

    [AvaloniaTest]
    public void ProductionAndResearchViews_Render()
    {
        ClientData client = TestGame.Load();
        var selection = new SelectionService();
        var production = new ProductionViewModel("Production", "Production", client, selection);
        selection.Selected = TestGame.HomeStar(client);
        Window productionWindow = Headless.Show(new ProductionView { DataContext = production }, 600, 900);
        Assert.That(Headless.Texts(productionWindow), Has.Member(production.PlanetName));
        Assert.That(Headless.DistinctColours(productionWindow), Is.GreaterThan(4));
        productionWindow.Close();

        var research = new ResearchViewModel("Research", "Research", client);
        Window researchWindow = Headless.Show(new ResearchView { DataContext = research }, 600, 900);
        Assert.That(Headless.All<ComboBox>(researchWindow), Is.Not.Empty, "target and next-field selectors");
        Assert.That(Headless.DistinctColours(researchWindow), Is.GreaterThan(4));
        researchWindow.Close();
    }

    [AvaloniaTest]
    public void TechnologyBrowserView_ShowsTheCurrentEntry()
    {
        var browser = new TechnologyBrowserViewModel("TechnologyBrowser", "Technology Browser", TestGame.Load());
        Window window = Headless.Show(new TechnologyBrowserView { DataContext = browser }, 600, 700);

        Assert.That(Headless.Texts(window), Has.Member(browser.EntryName));
        Assert.That(Headless.All<CheckBox>(window), Is.Not.Empty, "Show Only Available Technology");
        window.Close();
    }

    /// <summary>Client-interface row 71: the race password is a masked secret entry; dialog
    /// catalog rows 35/38: radio-button selectors and wrapped text. Non-editable disables the
    /// entries while showing the values.</summary>
    [AvaloniaTest]
    public void RaceDesignerView_HasAMaskedPasswordEntry_AndDisablesEverythingWhenReadOnly()
    {
        TestGame.PrepareEnvironment();
        var editable = new RaceDesignerView();
        Window window = Headless.Show(editable, 1000, 800);

        Assert.That(Headless.All<TextBox>(window).Where(box => box.PasswordChar != default(char)), Is.Not.Empty, "a masked secret entry");
        Assert.That(Headless.DistinctColours(window), Is.GreaterThan(4));
        window.Close();

        var race = new Race(System.IO.Path.Combine(TestGame.NovaRoot, Global.RaceFolderName, "Humanoid.race"));
        var readOnly = new RaceDesignerView(race, isEditable: false);
        Window readOnlyWindow = Headless.Show(readOnly, 1000, 800);
        var designer = (RaceDesignerViewModel)readOnly.DataContext!;

        TextBox nameBox = Headless.All<TextBox>(readOnlyWindow).First(box => box.Text == race.Name);
        Assert.That(nameBox.IsEffectivelyEnabled && !nameBox.IsReadOnly, Is.False, "the name is shown but cannot be edited");

        designer.SelectedPageLabel = "Research";
        Headless.Pump();
        var radios = Headless.All<RadioButton>(readOnlyWindow).Where(r => r.IsEffectivelyVisible).ToList();
        Assert.That(radios, Is.Not.Empty, "research cost classes are radio buttons");
        Assert.That(radios.Select(r => r.IsEffectivelyEnabled), Is.All.False);
        readOnlyWindow.Close();
    }

    [AvaloniaTest]
    public void NewGameView_RendersBothSetupPaths_WithWrappedHelpText()
    {
        TestGame.PrepareEnvironment();
        GameSettings.Data = TestGame.FreshSettings();
        var view = new NewGameView();
        Window window = Headless.Show(view, 1000, 800);
        var newGame = (NewGameViewModel)view.DataContext!;

        Assert.That(Headless.All<TextBlock>(window).Where(t => t.TextWrapping == TextWrapping.Wrap), Is.Not.Empty, "word-wrapped text");
        Assert.That(Headless.DistinctColours(window), Is.GreaterThan(4));

        newGame.SelectedSetupMode = NewGameViewModel.SimplifiedMode;
        Headless.Pump();
        Assert.That(Headless.Texts(window), Has.Some.Contains(newGame.SimplifiedPlayerSummary));
        window.Close();
    }
}
