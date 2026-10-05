using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Avalonia.Views.Panels;
using Nova.Client;
using Nova.Common;
using Nova.Common.DataStructures;
using Nova.Common.Waypoints;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The four report windows of behavior-specs-11/client-ui-dialog-catalog.md "Reports"
/// (client-ui-dialog-catalog.md coverage rows 26 and 57): the Planets, Fleets, Others' Fleets and
/// Battles column sets, the row-count titles (dynamic strings 1177-1180), the shared per-column
/// show/hide and header sort/reverse (dynamic strings 1133-1137), and the Fleets report's idle/
/// status glyph and ETA column.
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
        "Composition", "Cloak", "Battle Plan", "Mass", "Status",
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
    public void FleetsReport_HasTheSpecsTwelveColumns_AndTheStatusGlyph()
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

    // -- Header sort / reverse (spec strings 1133-1137) -----------------------------------------

    [AvaloniaTest]
    public void PlanetsReport_SortsByName_AndReverses()
    {
        ClientData client = TestGame.Load();
        var viewModel = new PlanetReportViewModel("PlanetReport", "Planet Report", client);
        List<string> names = viewModel.Planets.Select(row => row.Name).ToList();

        viewModel.Sort("Name");

        Assert.That(viewModel.SortColumnKey, Is.EqualTo("Name"));
        Assert.That(viewModel.SortDirection, Is.EqualTo(ReportSortDirection.Ascending));
        Assert.That(
            viewModel.Planets.Select(row => row.Name),
            Is.EqualTo(names.OrderBy(name => name, StringComparer.Ordinal)),
            "the ascending header sort orders the rows by the column");

        viewModel.ReverseSort();

        Assert.That(viewModel.SortDirection, Is.EqualTo(ReportSortDirection.Descending));
        Assert.That(
            viewModel.Planets.Select(row => row.Name),
            Is.EqualTo(names.OrderByDescending(name => name, StringComparer.Ordinal)),
            "reverse sort flips the current column's direction");
    }

    [AvaloniaTest]
    public void PlanetsReport_ReselectingTheSortedColumn_Reverses()
    {
        ClientData client = TestGame.Load();
        var viewModel = new PlanetReportViewModel("PlanetReport", "Planet Report", client);

        viewModel.Sort("Name");
        viewModel.Sort("Name");

        Assert.That(viewModel.SortColumnKey, Is.EqualTo("Name"));
        Assert.That(viewModel.SortDirection, Is.EqualTo(ReportSortDirection.Descending));
    }

    [AvaloniaTest]
    public void FleetsReport_SortsNumericColumn_Numerically()
    {
        ClientData client = TestGame.Load();
        var viewModel = new FleetReportViewModel("FleetReport", "Fleet Report", client);
        List<int> masses = viewModel.Fleets.Select(row => int.Parse(row.Mass)).OrderBy(mass => mass).ToList();

        viewModel.Sort("Mass");

        Assert.That(
            viewModel.Fleets.Select(row => int.Parse(row.Mass)),
            Is.EqualTo(masses),
            "numeric cells compare by value, not as text (10 after 9)");
    }

    // -- Header hide / show (spec strings 1133-1137) ---------------------------------------------

    [AvaloniaTest]
    public void ReportColumns_ToggleHideAndShow()
    {
        ClientData client = TestGame.Load();
        var viewModel = new PlanetReportViewModel("PlanetReport", "Planet Report", client);

        Assert.That(viewModel.IsColumnVisible("Starbase"), Is.True);

        viewModel.ToggleColumn("Starbase");
        Assert.That(viewModel.IsColumnVisible("Starbase"), Is.False, "the column can be hidden");

        viewModel.ToggleColumn("Starbase");
        Assert.That(viewModel.IsColumnVisible("Starbase"), Is.True, "the same command shows it again");
    }

    [AvaloniaTest]
    public void ReportGrid_ColumnVisibility_FollowsTheViewModel()
    {
        ClientData client = TestGame.Load();
        var viewModel = new PlanetReportViewModel("PlanetReport", "Planet Report", client);

        Window window = Headless.Show(new PlanetReportView { DataContext = viewModel });
        try
        {
            DataGrid grid = Headless.All<DataGrid>(window).First();
            Assert.That(grid.Columns[1].IsVisible, Is.True);

            viewModel.ToggleColumn("Starbase");

            Assert.That(grid.Columns[1].IsVisible, Is.False,
                "the grid column's IsVisible binding follows the view model's column state");
        }
        finally
        {
            window.Close();
        }
    }

    // -- Fleets idle/status glyph and ETA (spec "Reports" line 355/357) ---------------------------

    [AvaloniaTest]
    public void FleetRow_IdleFleet_UsesIdleGlyphAndTheNeverEta()
    {
        var idle = new Fleet("Idle", 1, 700, new NovaPoint(0, 0));

        var row = new FleetReportRowViewModel(idle);

        Assert.That(row.IsIdle, Is.True, "a fleet with no next waypoint is the distinguished idle state");
        Assert.That(row.Status, Is.EqualTo(FleetReportStatus.IdleGlyph));
        Assert.That(row.Eta, Is.EqualTo(FleetReportRowViewModel.EtaNever),
            "no valid estimate applies, so the ETA cell holds the large 'never' sentinel");
    }

    [AvaloniaTest]
    public void FleetRow_MovingFleet_ComputesEtaAndFlagsTheFuelWarning()
    {
        ClientData client = TestGame.Load();
        Fleet fleet = client.EmpireState.OwnedFleets.Values.First(f => !f.IsStarbase);
        fleet.Waypoints.Clear();
        fleet.Waypoints.Add(new Waypoint { Position = fleet.Position, Task = new NoTask() });
        fleet.Waypoints.Add(new Waypoint
        {
            Position = new NovaPoint(fleet.Position.X, fleet.Position.Y + 100),
            WarpFactor = 9,
            Destination = "Rigel",
            Task = new NoTask(),
        });
        fleet.FuelAvailable = 0;

        var row = new FleetReportRowViewModel(fleet, client.EmpireState.Race);

        Assert.That(row.IsIdle, Is.False);
        Assert.That(row.Status, Is.EqualTo(FleetReportStatus.ActiveGlyph));
        Assert.That(row.Destination, Is.EqualTo("Rigel"));
        Assert.That(row.Eta, Is.Not.EqualTo(FleetReportRowViewModel.EtaNever));
        Assert.That(row.EtaWarning, Is.True, "no fuel for the next leg -> warning colour");
    }
}
