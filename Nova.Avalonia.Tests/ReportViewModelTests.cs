using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Avalonia.Views.Panels;
using Nova.Client;
using Nova.Common;
using Nova.Common.DataStructures;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The four report windows of behavior-specs-11/client-ui-dialog-catalog.md "Reports"
/// (client-ui-dialog-catalog.md coverage rows 26 and 57): the Planets, Fleets, Others' Fleets and
/// Battles column sets, and the row-count titles (dynamic strings 1177-1180).
///
/// The spec gives the columns by functional name and dynamic-string id (1113-1176) but not the
/// displayed English captions, so the expected lists here are the port's own captions, asserted as
/// an exact sequence. The titles follow the recovered template (ReportTitles).
/// </summary>
[TestFixture]
public class ReportViewModelTests
{
    private static readonly string[] PlanetColumns =
    {
        "Planet Name", "Starbase", "Population", "Cap", "Value", "Production", "Mines",
        "Factories", "Defense", "Minerals", "Mining Rate", "Mineral Concentration", "Resources",
        "Driver Destination", "Routing Destination",
    };

    private static readonly string[] FleetColumns =
    {
        "Fleet Name", "Id", "Location", "Destination", "ETA", "Task", "Fuel", "Cargo",
        "Composition", "Cloak", "Battle Plan", "Mass",
    };

    private static readonly string[] OthersFleetColumns =
    {
        "Fleet Name", "Id", "Location", "Warp", "Mass", "Composition", "Number of Ships",
        "Unarmed", "Scout", "Warship", "Bomber", "Utility",
    };

    private static readonly string[] BattleColumns =
    {
        "Location", "Starbase Present", "Sides", "Units", "Ours", "Theirs", "Unarmed", "Scout",
        "Warship", "Bomber", "Utility", "Our Dead", "Their Dead", "Ours Left", "Theirs Left",
    };

    private static List<string> ColumnHeaders(Control view)
    {
        Window window = Headless.Show(view);
        try
        {
            DataGrid grid = Headless.All<DataGrid>(window).First();
            return grid.Columns.Select(column => column.Header?.ToString() ?? string.Empty).ToList();
        }
        finally
        {
            window.Close();
        }
    }

    private static string ExpectedTitle(string category, int count, string noun)
    {
        return $"{category} Summary Report -- {count} {noun}{(count == 1 ? string.Empty : "s")}";
    }

    [AvaloniaTest]
    public void PlanetsReport_HasTheSpecsFifteenColumns()
    {
        ClientData client = TestGame.Load();
        var viewModel = new PlanetReportViewModel("PlanetReport", "Planet Report", client);

        Assert.That(ColumnHeaders(new PlanetReportView { DataContext = viewModel }), Is.EqualTo(PlanetColumns));
    }

    [AvaloniaTest]
    public void FleetsReport_HasTheSpecsTwelveColumns()
    {
        ClientData client = TestGame.Load();
        var viewModel = new FleetReportViewModel("FleetReport", "Fleet Report", client);

        Assert.That(ColumnHeaders(new FleetReportView { DataContext = viewModel }), Is.EqualTo(FleetColumns));
    }

    [AvaloniaTest]
    public void OthersFleetsReport_HasTheSpecsTwelveColumns()
    {
        ClientData client = TestGame.Load();
        var viewModel = new OthersFleetReportViewModel("OthersFleetReport", "Others' Fleets Report", client);

        Assert.That(ColumnHeaders(new OthersFleetReportView { DataContext = viewModel }), Is.EqualTo(OthersFleetColumns));
    }

    [AvaloniaTest]
    public void BattlesReport_HasTheSpecsFifteenColumns()
    {
        ClientData client = TestGame.Load();
        var viewModel = new BattleReportViewModel("BattleReport", "Battle Report", client);

        Assert.That(ColumnHeaders(new BattleReportView { DataContext = viewModel }), Is.EqualTo(BattleColumns));
    }

    [AvaloniaTest]
    public void ReportTitles_CarryTheRowCountAndPluralMarker()
    {
        ClientData client = TestGame.Load();

        var planets = new PlanetReportViewModel("PlanetReport", "Planet Report", client);
        var fleets = new FleetReportViewModel("FleetReport", "Fleet Report", client);
        var others = new OthersFleetReportViewModel("OthersFleetReport", "Others' Fleets Report", client);
        var battles = new BattleReportViewModel("BattleReport", "Battle Report", client);

        Assert.That(planets.Title, Is.EqualTo(ExpectedTitle("Planet", planets.Planets.Count, "Planet")));
        Assert.That(fleets.Title, Is.EqualTo(ExpectedTitle("Fleet", fleets.Fleets.Count, "Fleet")));
        Assert.That(others.Title, Is.EqualTo(ExpectedTitle("Others' Fleets", others.Fleets.Count, "Fleet")));
        Assert.That(battles.Title, Is.EqualTo(ExpectedTitle("Battle", battles.Battles.Count, "Battle")));
    }

    [AvaloniaTest]
    public void PlanetRow_CarriesDriverDestination_AndTheRoutingSeam()
    {
        ClientData client = TestGame.Load();
        Star home = TestGame.HomeStar(client);
        home.PacketDestination = "Rigel";

        var row = new PlanetReportRowViewModel(home, client.EmpireState.Race);

        Assert.That(row.DriverDestination, Is.EqualTo("Rigel"));
        Assert.That(row.Cap, Is.EqualTo(((int)home.CapacityColonists(client.EmpireState.Race)).ToString()),
            "the spec's cap is the population capacity, not the utilisation percentage");
        Assert.That(row.RoutingDestination, Is.Empty, "the port models no route target (named seam)");
    }

    [AvaloniaTest]
    public void FleetRow_CarriesIdAndComposition()
    {
        ClientData client = TestGame.Load();
        Fleet fleet = client.EmpireState.OwnedFleets.Values.First(f => !f.IsStarbase);

        var row = new FleetReportRowViewModel(fleet);

        Assert.That(row.Id, Is.EqualTo(fleet.Id.ToString()));
        ShipToken token = fleet.Composition.Values.First();
        Assert.That(row.Composition, Does.Contain(token.Design.Name));
        Assert.That(row.Composition, Does.Contain("x" + token.Quantity));
    }

    [AvaloniaTest]
    public void OthersFleetRow_CarriesShipsAndTheClassCountSeam()
    {
        ClientData client = TestGame.Load();
        Fleet fleet = client.EmpireState.OwnedFleets.Values.First(f => !f.IsStarbase);
        FleetIntel intel = fleet.GenerateReport(ScanLevel.Owned, 0);

        var row = new OthersFleetReportRowViewModel(intel);

        Assert.That(row.Id, Is.EqualTo(intel.Id.ToString()));
        Assert.That(row.NumberOfShips, Is.EqualTo(intel.Count.ToString()));
        Assert.That(row.Unarmed, Is.EqualTo(ShipHullClass.Unrecovered),
            "the design hull class is not modelled (named seam)");
    }

    [AvaloniaTest]
    public void BattleRow_CountsShipsPerSide_AndReadsLosses()
    {
        ClientData client = TestGame.Load();
        Fleet fleet = client.EmpireState.OwnedFleets.Values.First(f => !f.IsStarbase);
        ShipToken token = fleet.Composition.Values.First();

        var stack = new Stack(fleet, 1, token);
        var report = new BattleReport { Location = "Test" };
        report.Stacks[stack.Key] = stack;
        report.Losses[client.EmpireState.Id] = 1;

        var row = new BattleReportRowViewModel(report, client.EmpireState.Id);

        Assert.That(row.Ours, Is.EqualTo(token.Quantity.ToString()), "one stack counts its ships, not one unit");
        Assert.That(row.Theirs, Is.EqualTo("0"));
        Assert.That(row.Units, Is.EqualTo(token.Quantity.ToString()));
        Assert.That(row.OurDead, Is.EqualTo("1"));
        Assert.That(row.OursLeft, Is.EqualTo((token.Quantity - 1).ToString()));
        Assert.That(row.TheirsLeft, Is.EqualTo("0"));
    }
}
