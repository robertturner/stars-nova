using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Nova.Avalonia.ViewModels;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Client.Map;
using Nova.Common;
using Nova.Common.DataStructures;
using Nova.Common.Waypoints;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The Star Map document's view model against a real generated game (client-interface.md, "Map
/// canvas" rows 17-46). Where a rule's values are spec-gap stand-ins (the planet-mode order, the
/// initial view options), the tests read them through MapViewOptions' own seams instead of
/// pinning them.
/// </summary>
[TestFixture]
public class StarMapViewModelTests
{
    private static (ClientData Client, SelectionService Selection, StarMapDocumentViewModel Map) Open(string race = TestGame.PacketRace)
    {
        ClientData client = TestGame.Load(race);
        var selection = new SelectionService();
        var map = new StarMapDocumentViewModel("StarMap", "Star Map", client, selection);
        return (client, selection, map);
    }

    private static StarMapStarViewModel MarkerOf(StarMapDocumentViewModel map, string starName)
    {
        return map.Stars.Single(star => star.Name == starName);
    }

    /// <summary>Row 17: selecting an object on the map makes it the subject every panel follows.</summary>
    [AvaloniaTest]
    public void SelectingAStarMarker_PublishesTheOwnedStar_AndHighlightsIt()
    {
        (ClientData client, SelectionService selection, StarMapDocumentViewModel map) = Open();
        Star home = TestGame.HomeStar(client);
        var inspector = new InspectorViewModel("Inspector", "Inspector", client, selection);
        var production = new ProductionViewModel("Production", "Production", client, selection);

        StarMapStarViewModel marker = MarkerOf(map, home.Name);
        marker.SelectCommand.Execute(null);

        Assert.That(selection.Selected, Is.SameAs(home), "an owned star publishes the live Star, not its report");
        Assert.That(marker.IsSelected, Is.True);
        Assert.That(map.Stars.Count(star => star.IsSelected), Is.EqualTo(1));
        Assert.That(inspector.Name, Is.EqualTo(home.Name));
        Assert.That(production.PlanetName, Is.EqualTo(home.Name));
    }

    /// <summary>Row 17: an unowned star publishes only its report.</summary>
    [AvaloniaTest]
    public void SelectingAForeignStarMarker_PublishesItsReport()
    {
        (ClientData client, SelectionService selection, StarMapDocumentViewModel map) = Open();
        StarIntel report = client.EmpireState.StarReports.Values.First(r => r.Owner != client.EmpireState.Id);

        MarkerOf(map, report.Name).SelectCommand.Execute(null);

        Assert.That(selection.Selected, Is.SameAs(report));
    }

    /// <summary>Rows 18-20: the selected fleet's route is drawn as ordered legs with a distinct
    /// first leg and final marker; selecting something else replaces it.</summary>
    [AvaloniaTest]
    public void SelectedFleetRoute_IsDrawnAsOrderedLegs_AndFollowsTheSelection()
    {
        (ClientData client, SelectionService selection, StarMapDocumentViewModel map) = Open();
        Star home = TestGame.HomeStar(client);
        Fleet fleet = TestGame.FleetsAt(client, home)[0];
        List<StarIntel> targets = client.EmpireState.StarReports.Values.Where(r => r.Name != home.Name).Take(2).ToList();
        foreach (StarIntel target in targets)
        {
            fleet.Waypoints.Add(new Waypoint { Position = target.Position, Destination = target.Name, WarpFactor = 6 });
        }

        selection.Selected = fleet;

        IReadOnlyList<StarMapRouteLegViewModel> legs = map.RouteLegs;
        Assert.That(legs, Has.Count.EqualTo(2), "one leg per pending waypoint");
        bool firstLegDistinct = legs[0].LineThickness != legs[1].LineThickness || !Equals(legs[0].LineColor, legs[1].LineColor);
        Assert.That(firstLegDistinct, Is.True, "the first leg (from the fleet's current position) is drawn distinctly");
        Assert.That(legs[1].MarkerDiameter, Is.GreaterThan(legs[0].MarkerDiameter), "the final destination is marked distinctly");
        Assert.That(legs[0].End, Is.EqualTo(legs[1].Start), "the legs join in order");

        selection.Selected = home;
        Assert.That(map.RouteLegs, Is.Empty, "selecting a planet removes the route overlay");

        Fleet other = TestGame.FleetsAt(client, home)[1];
        other.Waypoints.Add(new Waypoint { Position = targets[0].Position, Destination = targets[0].Name, WarpFactor = 5 });
        selection.Selected = other;
        Assert.That(map.RouteLegs, Has.Count.EqualTo(1), "a different fleet's own route replaces the first one");
    }

    /// <summary>Row 22: repeated clicks on the same spot cycle through the co-located objects.</summary>
    [AvaloniaTest]
    public void RepeatedClickOnTheSameSpot_CyclesThroughCoLocatedMarkers()
    {
        ClientData client = TestGame.Load();
        Star home = TestGame.HomeStar(client);

        // Take one fleet out of orbit (still on the star's position) so it gets its own marker
        // sitting on the star's.
        Fleet scout = TestGame.FleetsAt(client, home)[0];
        scout.InOrbit = null;

        var selection = new SelectionService();
        var map = new StarMapDocumentViewModel("StarMap", "Star Map", client, selection);
        StarMapStarViewModel star = MarkerOf(map, home.Name);
        Assume.That(map.Fleets.Any(f => ReferenceEquals(f.Selectable, scout)), "the out-of-orbit fleet has a map marker");

        MapMarkerViewModel? first = map.FindNearestStarOrFleetMarker(star.X, star.Y);
        MapMarkerViewModel? second = map.FindNearestStarOrFleetMarker(star.X, star.Y);
        MapMarkerViewModel? third = map.FindNearestStarOrFleetMarker(star.X, star.Y);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.SameAs(first), "a second click on the same spot moves to the next object there");
        Assert.That(new[] { first, second }, Has.Some.SameAs(star));
        Assert.That(third, Is.SameAs(first), "the cycle wraps round");
    }

    /// <summary>Rows 27 and 32: the home planet's starbase/mass-driver dots and the fleet-in-orbit
    /// ring, which grows from the small to the large size class when the planet is tracked.</summary>
    [AvaloniaTest]
    public void HomePlanetMarker_ShowsStarbaseCapabilities_AndTheOrbitRing()
    {
        (ClientData client, SelectionService selection, StarMapDocumentViewModel map) = Open();
        Star home = TestGame.HomeStar(client);
        StarMapStarViewModel marker = MarkerOf(map, home.Name);

        Assert.That(marker.HasStarbase, Is.True);
        Assert.That(map.Stars.Any(s => s.HasMassDriver), Is.True, "a Packet Physics empire starts with a mass-driver starbase");
        Assert.That(marker.HasOwnFleetInOrbit, Is.True, "the starting fleets orbit the home world");
        Assert.That(marker.HasFleetsInOrbit, Is.True);

        double untracked = marker.OrbitRingDiameter;
        selection.Selected = home;
        Assert.That(marker.OrbitRingDiameter, Is.GreaterThan(untracked), "the tracked planet uses the large ring");
        Assert.That(untracked, Is.EqualTo(11), "client-interface.md: 11x11 ring normally");
        Assert.That(marker.OrbitRingDiameter, Is.EqualTo(19), "19x19 when the planet is the tracked object");
    }

    /// <summary>Rows 33 and 34: a deep-space fleet marker carries its ship count, the badge
    /// follows Shift+0, and the tracked fleet shows the chevron instead of the heading
    /// triangle.</summary>
    [AvaloniaTest]
    public void DeepSpaceFleetMarker_BadgeAndChevron()
    {
        ClientData client = TestGame.Load();
        Star home = TestGame.HomeStar(client);
        Fleet scout = TestGame.FleetsAt(client, home)[0];
        scout.InOrbit = null;

        var selection = new SelectionService();
        var map = new StarMapDocumentViewModel("StarMap", "Star Map", client, selection);
        StarMapFleetViewModel? marker = map.Fleets.FirstOrDefault(f => ReferenceEquals(f.Selectable, scout));
        Assume.That(marker, Is.Not.Null, "the out-of-orbit fleet has a map marker");

        Assert.That(marker!.ShipCount, Is.EqualTo(scout.Composition.Values.Sum(token => token.Quantity)));
        Assert.That(marker.ShipCountDisplay, Is.EqualTo(marker.ShipCount.ToString()));

        bool badge = marker.ShowBadge;
        Assert.That(map.HandleDigitKey(0, shift: true), Is.True, "Shift+0 is the badge key");
        Assert.That(marker.ShowBadge, Is.EqualTo(!badge));

        Assert.That(marker.ShowChevron, Is.False);
        selection.Selected = scout;
        Assert.That(marker.ShowChevron, Is.True, "the tracked fleet is drawn as the chevron");
        Assert.That(marker.ShowTriangle, Is.False);
    }

    /// <summary>Row 36: the empire's own scanners draw scan-range circles; key 7 turns them off.</summary>
    [AvaloniaTest]
    public void ScanCircles_DrawnForOwnScanners_AndFollowKeySeven()
    {
        (ClientData client, _, StarMapDocumentViewModel map) = Open();
        map.ShowScanCircles = true;
        Assert.That(map.ScanCircles, Is.Not.Empty, "the home planet's scanner draws a circle");

        Assert.That(map.HandleDigitKey(7, shift: false), Is.True);
        Assert.That(map.ShowScanCircles, Is.False);
        Assert.That(map.ScanCircles, Is.Empty);

        map.HandleDigitKey(7, shift: false);
        Assert.That(map.ScanCircles, Is.Not.Empty);
    }

    /// <summary>Row 38 wiring: lowering the scanner percentage enlarges the drawn circles by the
    /// rule in ScanCircleRules, and force-enables them.</summary>
    [AvaloniaTest]
    public void ScannerPercentage_RescalesTheDrawnCircles()
    {
        (ClientData client, _, StarMapDocumentViewModel map) = Open();
        Star home = TestGame.HomeStar(client);
        map.ShowScanCircles = true;
        double fullSize = map.ScanCircles.Max(circle => circle.Diameter / 2);

        map.ShowScanCircles = false;
        map.ScannerPercentage = 50;

        Assert.That(map.ShowScanCircles, Is.True, "changing the percentage turns the circles back on");
        Assert.That(map.ScanCircles.Max(circle => circle.Diameter / 2), Is.GreaterThan(fullSize));
        Assert.That(map.ScanCircles.Select(c => c.Diameter / 2), Has.Some.EqualTo(ScanCircleRules.DisplayRadius(home.ScanRange, 50)).Within(0.001));
    }

    /// <summary>Row 40: a Packet Physics race gets a Mass-Driver range circle, bundled with the
    /// scan-circle toggle; no other race does.</summary>
    [AvaloniaTest]
    public void MassDriverOverlay_IsDrawnForPacketPhysicsOnly()
    {
        (ClientData ppClient, _, StarMapDocumentViewModel ppMap) = Open(TestGame.PacketRace);
        ppMap.ShowScanCircles = true;
        Star ppHome = TestGame.HomeStar(ppClient);
        int driver = MineralPacketRules.BestDriverWarp(ppHome.Starbase);
        Assume.That(driver, Is.GreaterThan(0), "a Packet Physics empire starts with a mass-driver starbase");

        Assert.That(ppMap.ScanCircles.Any(circle => circle.Kind == ScanCircleKind.MassDriver), Is.True, "the PP home world gets the overlay");
        Assert.That(
            ppMap.ScanCircles.Any(circle => circle.Kind == ScanCircleKind.MassDriver
                && System.Math.Abs((circle.Diameter / 2) - MassDriverRangeRules.RangeCircleRadius(driver)) < 0.001),
            Is.True,
            "the radius follows the driver level");

        ppMap.ShowScanCircles = false;
        Assert.That(ppMap.ScanCircles, Is.Empty, "the Mass-Driver circle shares the scan-circle toggle");

        (_, _, StarMapDocumentViewModel sdMap) = Open(TestGame.DemolitionRace);
        sdMap.ShowScanCircles = true;
        Assert.That(sdMap.ScanCircles.Any(circle => circle.Kind == ScanCircleKind.MassDriver), Is.False, "no overlay for another PRT (which in any case has no driver)");
    }

    /// <summary>Row 44: the nine fixed zoom steps; the zoom commands clamp at both ends.</summary>
    [AvaloniaTest]
    public void Zoom_HasNineSteps_AndClampsAtBothEnds()
    {
        (_, _, StarMapDocumentViewModel map) = Open();

        Assert.That(map.ZoomLevelLabels, Has.Count.EqualTo(9));
        Assert.That(map.Zoom, Is.EqualTo(1.0), "the default step is 100%");

        for (int i = 0; i < 12; i++)
        {
            map.ZoomInCommand.Execute(null);
        }

        Assert.That(map.ZoomLevelIndex, Is.EqualTo(8));
        Assert.That(map.Zoom, Is.EqualTo(4.0), "the largest step is x4");

        for (int i = 0; i < 12; i++)
        {
            map.ZoomOutCommand.Execute(null);
        }

        Assert.That(map.ZoomLevelIndex, Is.EqualTo(0));
        Assert.That(map.Zoom, Is.EqualTo(0.25), "the smallest step is 1/4");

        map.ResetZoomCommand.Execute(null);
        Assert.That(map.Zoom, Is.EqualTo(1.0));
    }

    /// <summary>Row 41: each field type gets its own pattern, and the owner mask hides a field
    /// whose category is cleared.</summary>
    [AvaloniaTest]
    public void MinefieldOverlay_UsesTheTypePattern_AndTheOwnerMask()
    {
        ClientData client = TestGame.Load(TestGame.DemolitionRace);
        Star home = TestGame.HomeStar(client);
        var field = new Minefield { Owner = client.EmpireState.Id, Id = 901, NumberOfMines = 100, FieldType = MinefieldType.Heavy };
        field.Position = new NovaPoint(home.Position.X, home.Position.Y);
        client.InputTurn.AllMinefields[field.Key] = field;
        client.EmpireState.VisibleMinefields.Add(field.Key);

        var selection = new SelectionService();
        var map = new StarMapDocumentViewModel("StarMap", "Star Map", client, selection);
        map.ShowMinefields = true;

        StarMapMineFieldViewModel marker = map.Minefields.First(m => ReferenceEquals(m.Selectable, field));
        Assert.That(marker.Pattern, Is.EqualTo(MinefieldPattern.Heavy), "the type selects the fill pattern");
        Assert.That(marker.Category, Is.EqualTo(MinefieldVisibility.Own));
        Assert.That(marker.IsVisible, Is.True);

        map.ShowOwnMinefields = false;
        Assert.That(marker.IsVisible, Is.False, "clearing the own bit hides the field");
        map.MinefieldsNoneCommand.Execute(null);
        Assert.That(marker.IsVisible, Is.False);
        map.MinefieldsAllCommand.Execute(null);
        Assert.That(marker.IsVisible, Is.True);
        Assert.That(map.MinefieldMaskIsAll, Is.True);
    }

    /// <summary>Rows 31 and 46: the six-way "Planets:" mode drives every star's overlay (read
    /// through the MapViewOptions seam, not pinned), and the digit keys 1-6 select it.</summary>
    [AvaloniaTest]
    public void PlanetMode_DrivesEveryStarOverlay_AndTheDigitKeysSelectIt()
    {
        (_, _, StarMapDocumentViewModel map) = Open();
        Assert.That(map.PlanetModeLabels, Has.Count.EqualTo(MapViewOptions.ModeCount));
        Assert.That(MapViewOptions.ModeCount, Is.EqualTo(6));

        for (int mode = 0; mode < MapViewOptions.ModeCount; mode++)
        {
            Assert.That(map.HandleDigitKey(mode + 1, shift: false), Is.True);
            Assert.That(map.PlanetMode, Is.EqualTo(mode));
            Assert.That(map.Stars.Select(star => star.Overlay), Is.All.EqualTo(MapViewOptions.ModeOverlays[mode]));
        }
    }

    /// <summary>Row 46: key 0 toggles the planet-name labels on every star.</summary>
    [AvaloniaTest]
    public void KeyZero_TogglesPlanetNames()
    {
        (_, _, StarMapDocumentViewModel map) = Open();
        bool before = map.ShowPlanetNames;

        map.HandleDigitKey(0, shift: false);

        Assert.That(map.ShowPlanetNames, Is.EqualTo(!before));
        Assert.That(map.Stars.Select(star => star.ShowName), Is.All.EqualTo(!before));
        Assert.That(map.HandleDigitKey(5, shift: true), Is.False, "Shift with 1-9 is not bound");
    }

    /// <summary>Row 11: commands are gated on context - measuring needs a map object selected.</summary>
    [AvaloniaTest]
    public void MeasureDistance_IsEnabledOnlyWithAMapObjectSelected()
    {
        (ClientData client, SelectionService selection, StarMapDocumentViewModel map) = Open();
        Star home = TestGame.HomeStar(client);
        StarIntel other = client.EmpireState.StarReports.Values.First(r => r.Name != home.Name);

        Assert.That(map.MeasureDistanceCommand.CanExecute(null), Is.False);

        selection.Selected = home;
        Assert.That(map.MeasureDistanceCommand.CanExecute(null), Is.True);

        map.MeasureDistanceCommand.Execute(null);
        Assert.That(map.IsMeasuringDistance, Is.True);
        MarkerOf(map, other.Name).SelectCommand.Execute(null);

        Assert.That(map.IsMeasuringDistance, Is.False);
        Assert.That(selection.Selected, Is.SameAs(home), "the measured-to tap does not change the selection");
        Assert.That(map.MeasureDistanceResult, Does.Contain(home.Name).And.Contain(other.Name));
    }

    /// <summary>Row 83: detected mineral packets and wormholes get selectable markers with an
    /// identification tooltip.</summary>
    [AvaloniaTest]
    public void PacketAndWormholeReports_GetSelectableMarkers()
    {
        ClientData client = TestGame.Load();
        Star home = TestGame.HomeStar(client);
        var packet = new MineralPacket { Owner = client.EmpireState.Id, Id = 77, Warp = 8, TargetName = home.Name };
        packet.Position = new NovaPoint(home.Position.X + 30, home.Position.Y + 30);
        packet.Minerals.Ironium = 100;
        client.EmpireState.MineralPacketReports[packet.Key] = packet;

        var wormhole = new WormholeIntel { Id = 5, Year = client.EmpireState.TurnYear };
        wormhole.Position = new NovaPoint(home.Position.X - 30, home.Position.Y - 30);
        client.EmpireState.WormholeReports[wormhole.Key] = wormhole;

        var selection = new SelectionService();
        var map = new StarMapDocumentViewModel("StarMap", "Star Map", client, selection);
        var inspector = new InspectorViewModel("Inspector", "Inspector", client, selection);

        StarMapPacketViewModel packetMarker = map.Packets.Single();
        StarMapWormholeViewModel wormholeMarker = map.Wormholes.Single();
        Assert.That(packetMarker.ToolTipText, Is.Not.Empty);
        Assert.That(wormholeMarker.ToolTipText, Is.Not.Empty);

        packetMarker.SelectCommand.Execute(null);
        Assert.That(selection.Selected, Is.SameAs(packet));
        Assert.That(inspector.Kind, Is.EqualTo("Mineral Packet"));
        Assert.That(inspector.Rows, Is.Not.Empty);

        wormholeMarker.SelectCommand.Execute(null);
        Assert.That(selection.Selected, Is.SameAs(wormhole));
    }

    /// <summary>Rows 9/93: the map toolbar's scanner-percentage drop-down is pre-filled 100% down
    /// to 10% in steps of 10.</summary>
    [AvaloniaTest]
    public void ScannerPercentageDropDown_IsPreFilled100DownTo10By10()
    {
        (_, _, StarMapDocumentViewModel map) = Open();

        Assert.That(map.ScannerPercentagePresets, Has.Count.EqualTo(10));
        Assert.That(
            map.ScannerPercentagePresets.Select(preset => int.Parse(preset.TrimEnd('%'))),
            Is.EqualTo(new[] { 100, 90, 80, 70, 60, 50, 40, 30, 20, 10 }));
    }

    /// <summary>Rows 9/93: Enter commits a typed in-range value and force-enables the scan
    /// circles.</summary>
    [AvaloniaTest]
    public void CommitScannerPercentage_TypedValue_ForceEnablesScanCircles()
    {
        (_, _, StarMapDocumentViewModel map) = Open();
        map.ShowScanCircles = false;

        map.ScannerPercentageText = "45";
        map.CommitScannerPercentageCommand.Execute(null);

        Assert.That(map.ScannerPercentage, Is.EqualTo(45));
        Assert.That(map.ShowScanCircles, Is.True, "Enter unconditionally turns the overlay on");
        Assert.That(map.ScannerPercentageText, Is.EqualTo("45%"), "the committed value is shown formatted");
    }

    /// <summary>Rows 9/93: a typed value outside 2-100 is clamped to the nearest limit.</summary>
    [AvaloniaTest]
    public void CommitScannerPercentage_OutOfRange_ClampsToTheRange()
    {
        (_, _, StarMapDocumentViewModel map) = Open();

        map.ScannerPercentageText = "1";
        map.CommitScannerPercentageCommand.Execute(null);
        Assert.That(map.ScannerPercentage, Is.EqualTo(2), "below the minimum clamps to 2%");

        map.ScannerPercentageText = "500";
        map.CommitScannerPercentageCommand.Execute(null);
        Assert.That(map.ScannerPercentage, Is.EqualTo(100), "above the maximum clamps to 100%");
    }

    /// <summary>Rows 9/93: Escape reverts to the last committed value (a typed, uncommitted value
    /// is discarded).</summary>
    [AvaloniaTest]
    public void EscapeScannerPercentage_RevertsToTheLastCommittedValue()
    {
        (_, _, StarMapDocumentViewModel map) = Open();

        map.ScannerPercentageText = "60";
        map.CommitScannerPercentageCommand.Execute(null);
        Assume.That(map.ScannerPercentage, Is.EqualTo(60));

        map.ScannerPercentageText = "20";
        map.RevertScannerPercentageCommand.Execute(null);

        Assert.That(map.ScannerPercentage, Is.EqualTo(60), "Escape does not commit the typed value");
        Assert.That(map.ScannerPercentageText, Is.EqualTo("60%"));
        Assert.That(map.ShowScanCircles, Is.True);
    }

    /// <summary>Row 93: the View > Zoom menu drives slot 16 of the shared view-option word, so the
    /// step survives a map rebuild and is addressable through MapViewOptions.GetSlot/SetSlot(16).</summary>
    [AvaloniaTest]
    public void ZoomLevel_IsTheSharedViewOptionSlot16()
    {
        (_, _, StarMapDocumentViewModel map) = Open();
        try
        {
            map.ZoomLevel = MapZoom.MaxLevel;
            Assert.That(StarMapDocumentViewModel.ViewOptions.GetSlot(16), Is.EqualTo(MapZoom.MaxLevel));
            Assert.That(map.Zoom, Is.EqualTo(4.0));

            StarMapDocumentViewModel.ViewOptions.SetSlot(16, MapZoom.MinLevel);
            Assert.That(map.ZoomLevel, Is.EqualTo(MapZoom.MinLevel), "the slot reads back through the view model");
            Assert.That(map.Zoom, Is.EqualTo(0.25));
        }
        finally
        {
            StarMapDocumentViewModel.ViewOptions.ZoomStep = MapZoom.DefaultLevel;
        }
    }

    /// <summary>Row 93: View > Player Colors (command 2445) toggles second-word bit 0x20 and is
    /// exposed to the map so the name/badge colour converter can follow it.</summary>
    [AvaloniaTest]
    public void PlayerColors_TogglesTheSharedViewOptionBit()
    {
        (_, _, StarMapDocumentViewModel map) = Open();
        StarMapDocumentViewModel.ViewOptions.ShowPlayerColors = false;
        try
        {
            map.ShowPlayerColors = true;
            Assert.That(map.ShowPlayerColors, Is.True);
            Assert.That(StarMapDocumentViewModel.ViewOptions.Word2 & MapViewOptions.PlayerColorsBit,
                Is.EqualTo(MapViewOptions.PlayerColorsBit), "second word bit 0x20");

            map.ShowPlayerColors = false;
            Assert.That(StarMapDocumentViewModel.ViewOptions.Word2 & MapViewOptions.PlayerColorsBit, Is.Zero);
        }
        finally
        {
            StarMapDocumentViewModel.ViewOptions.ShowPlayerColors = false;
        }
    }

    /// <summary>Row 93: the name/badge colour converter returns the owner colour only while Player
    /// Colors is on, otherwise the spec's plain default.</summary>
    [AvaloniaTest]
    public void PlayerColorConverter_UsesOwnerColourOnlyWhenEnabled()
    {
        IBrush owner = Brushes.Red;
        object? on = PlayerColorConverter.Instance.Convert(new object?[] { owner, true }, typeof(IBrush), null, CultureInfo.InvariantCulture);
        object? off = PlayerColorConverter.Instance.Convert(new object?[] { owner, false }, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.That(on, Is.SameAs(owner));
        Assert.That(off, Is.SameAs(Brushes.White), "Player Colors off falls back to the plain default");
    }
}
