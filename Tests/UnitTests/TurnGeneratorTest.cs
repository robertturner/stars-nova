using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;

namespace Nova.Server
{
    public class SimpleServerData : ServerData
    {
        public SimpleServerData()
        {
        }
    }

    public class SimpleTurnGenerator : TurnGenerator
    {
        public SimpleTurnGenerator(ServerData serverState) : base(serverState)
        {
        }

        protected override void BackupTurn()
        {
            // base.BackupTurn();
        }

        protected override void ReadOrders()
        {
            // base.ReadOrders();
        }

        protected override void ParseCommands()
        {
            // base.ParseCommands();
        }

        protected override void WriteIntel()
        {
            // base.WriteIntel();
        }

        protected override void CleanupOrders()
        {
            // base.CleanupOrders();
        }
    }
}

namespace Nova.Common
{
    public class SimpleEmpireData : EmpireData
    {
        public SimpleEmpireData()
        {
        }

        protected override void Initialize()
        {
            //base.Initialize();
        }
    }
}

namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;
    using Nova.Common.Waypoints;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    [TestFixture]
    public class TurnGeneratorTest
    {
        private ServerData serverData;
        private List<Fleet> fleets;
        private EmpireData empireData;

        [SetUp]
        public void Init()
        {
            fleets = new List<Fleet>();
            Fleet fleet = new Fleet(1);
            fleet.Owner = 1;
            ShipDesign shipDesign = new ShipDesign(1);
            ShipToken shipToken = new ShipToken(shipDesign, 1);
            fleet.Composition.Add(shipToken.Key, shipToken);
            fleets.Add(fleet);
            Waypoint waypoint = new Waypoint();
            IWaypointTask task = new ScrapTask();
            waypoint.Task = task;
            waypoint.Destination = "Star1";
            fleet.Waypoints.Add(waypoint);
            serverData = new SimpleServerData();
            Star star = new Star();
            star.Name = "Star1";
            serverData.AllStars.Add(star.Key, star);
            empireData = new SimpleEmpireData();
            empireData.Id = 1;
            empireData.OwnedFleets.Add(fleet);
            serverData.AllEmpires.Add(empireData.Id, empireData);
            Console.WriteLine(fleets.First().Composition.Count());
            Assert.AreEqual(fleets.First().Composition.Count(), 1);
        }

        [Test]
        public void Generate_ScrapFleets()
        {
            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            Assert.IsNotEmpty(serverData.IterateAllFleets().ToList());
            turnGenerator.Generate();
            Console.WriteLine(fleets.First().Composition.Count());
            Assert.AreEqual(fleets.First().Composition.Count(), 0);
            Assert.IsEmpty(serverData.IterateAllFleets().ToList());
        }

        /// <summary>
        /// End-to-end confirmation that Generate() actually wires up this turn's empire shuffle
        /// (see EmpireOrderShuffleTest/RemoteMiningOrderFairnessTest for the mechanism's own
        /// focused unit tests) rather than leaving ServerData.ShuffledEmpireOrder unset.
        /// </summary>
        [Test]
        public void Generate_PopulatesShuffledEmpireOrder_WithEveryEmpire()
        {
            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            Assert.IsNull(serverData.ShuffledEmpireOrder, "Sanity check - nothing has run yet.");

            turnGenerator.Generate();

            Assert.IsNotNull(serverData.ShuffledEmpireOrder);
            CollectionAssert.AreEquivalent(serverData.AllEmpires.Values, serverData.ShuffledEmpireOrder);
        }

        [Test]
        public void Generate_Dont_ScrapFleets()
        {
            // ToDo: Fleet-Generation and other things to seperate class/es and/or methods
            Fleet fleet = new Fleet(2);
            fleet.Owner = 1;
            NovaPoint point = new NovaPoint(0, 0);
            fleet.Position = point;
            ShipDesign shipDesign = new ShipDesign(2);
            shipDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            hull.Modules.Add(new HullModule());
            shipDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken shipToken = new ShipToken(shipDesign, 1);
            fleet.Composition.Add(shipToken.Key, shipToken);
            fleets.Add(fleet);
            Waypoint waypoint = new Waypoint();
            NovaPoint waypointpoint = new NovaPoint(1,1);
            waypoint.Position = waypointpoint;
            IWaypointTask task = new NoTask();
            waypoint.Task = task;
            waypoint.Destination = "Star1";
            fleet.Waypoints.Add(waypoint);

            empireData.AddOrUpdateFleet(fleet);
            // empireData.OwnedFleets.Add(fleet); // ToDo: this should not be allowed I think

            Console.WriteLine("2 Fleets: " + fleets.Count());
            Assert.IsNotEmpty(serverData.IterateAllFleets().ToList());
            Console.WriteLine("all Fleets count: " + serverData.IterateAllFleets().ToList().Count());

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();
            // Assert.AreEqual(fleets.First().Composition.Count(), 1);
            Assert.IsNotEmpty(serverData.IterateAllFleets().ToList());
        }

        [Test]
        public void Generate_StopsAtFirstWaypoint_EvenWithLeftoverMovement()
        {
            // Regression test: a fleet with enough speed/fuel to cover several waypoints'
            // worth of distance in one year previously flew through all of them in a single
            // turn - e.g. a scout arriving at a planet and immediately continuing past it
            // before anything (a scan report, an "explored" flag) ever registered the visit.
            // Per docs/behavior-specs/fleet-movement-scanning-cargo.md §5, arrival at a
            // waypoint always uses up the rest of that turn's movement; the fleet only
            // resumes toward its next waypoint the following turn.
            serverData = new SimpleServerData();
            empireData = new SimpleEmpireData();
            empireData.Id = 1;
            serverData.AllEmpires.Add(empireData.Id, empireData);

            Fleet fleet = new Fleet(3);
            fleet.Owner = 1;
            fleet.Position = new NovaPoint(0, 0);

            ShipDesign shipDesign = new ShipDesign(3);
            shipDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            hull.Modules.Add(new HullModule());
            shipDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken shipToken = new ShipToken(shipDesign, 1);
            fleet.Composition.Add(shipToken.Key, shipToken);

            // Warp 9 = 81 ly/year, vastly more than the 1 ly separating each waypoint below -
            // under the bug, the fleet would reach both StarA and StarB in the same turn.
            Waypoint waypointA = new Waypoint();
            waypointA.Position = new NovaPoint(1, 0);
            waypointA.WarpFactor = 9;
            waypointA.Task = new NoTask();
            waypointA.Destination = "StarA";
            fleet.Waypoints.Add(waypointA);

            Waypoint waypointB = new Waypoint();
            waypointB.Position = new NovaPoint(2, 0);
            waypointB.WarpFactor = 9;
            waypointB.Task = new NoTask();
            waypointB.Destination = "StarB";
            fleet.Waypoints.Add(waypointB);

            Star starA = new Star();
            starA.Name = "StarA";
            starA.Position = new NovaPoint(1, 0);
            serverData.AllStars.Add(starA.Key, starA);

            Star starB = new Star();
            starB.Name = "StarB";
            starB.Position = new NovaPoint(2, 0);
            serverData.AllStars.Add(starB.Key, starB);

            empireData.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreEqual(1, fleet.Position.X, "Fleet should have stopped at StarA, not continued to StarB");
            Assert.AreEqual(0, fleet.Position.Y);
            Assert.AreEqual(2, fleet.Waypoints.Count, "StarB should still be a pending waypoint");
            Assert.AreEqual("StarA", fleet.Waypoints[0].Destination, "Fleet should be holding at StarA");
            Assert.AreEqual("StarB", fleet.Waypoints[1].Destination, "StarB should not have been consumed yet");
        }

        [Test]
        public void Generate_LayMines_CreatesAMinefield()
        {
            // Regression test for LayMinesTask.Perform() being a complete no-op (the code that
            // would actually create/update a minefield was commented out with a "TODO: Implement
            // per empire minefields" note, since Common/Waypoints/LayMinesTask.cs has no way to
            // reach ServerData.AllMinefields). See ServerState/LayMines.cs.
            serverData = new SimpleServerData();
            empireData = new SimpleEmpireData();
            empireData.Id = 1;
            serverData.AllEmpires.Add(empireData.Id, empireData);

            Fleet fleet = new Fleet(4);
            fleet.Owner = 1;
            fleet.Position = new NovaPoint(5, 5);

            ShipDesign shipDesign = new ShipDesign(4);
            shipDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();

            HullModule mineLayerModule = new HullModule();
            Component mineLayerComponent = new Component();
            mineLayerComponent.Properties.Add("Mine Layer", new MineLayer { LayerRate = 40 });
            mineLayerModule.AllocatedComponent = mineLayerComponent;
            hull.Modules.Add(mineLayerModule);

            shipDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken shipToken = new ShipToken(shipDesign, 5); // 5 ships x 40/ship = 200 mines/turn
            fleet.Composition.Add(shipToken.Key, shipToken);

            Waypoint waypoint = new Waypoint();
            waypoint.Position = fleet.Position;
            waypoint.WarpFactor = 0;
            waypoint.Task = new LayMinesTask();
            waypoint.Destination = "deep space";
            fleet.Waypoints.Add(waypoint);

            empireData.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreEqual(1, serverData.AllMinefields.Count, "Expected exactly one minefield to have been laid");
            Minefield minefield = serverData.AllMinefields.Values.First();
            Assert.AreEqual(200, minefield.NumberOfMines);
            Assert.AreEqual(1, minefield.Owner);
        }

        [Test]
        public void LayMines_AddsToAnExistingNearbyFieldOfOurs_InsteadOfStartingANewOne()
        {
            // Exercises LayMines.Lay directly rather than through a full TurnGenerator.Generate()
            // turn, so yearly decay does not enter the exact counts. Laid mines join the nearest
            // own field of the same type whose circle covers the fleet (turn-generation-engine.md
            // section 3, "Where the mines go").
            serverData = new SimpleServerData();
            empireData = new SimpleEmpireData();
            empireData.Id = 1;
            serverData.AllEmpires.Add(empireData.Id, empireData);

            Fleet fleet = new Fleet(6);
            fleet.Owner = 1;
            fleet.Position = new NovaPoint(5, 5);

            ShipDesign shipDesign = new ShipDesign(6);
            shipDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            HullModule mineLayerModule = new HullModule();
            Component mineLayerComponent = new Component();
            mineLayerComponent.Properties.Add("Mine Layer", new MineLayer { LayerRate = 40 });
            mineLayerModule.AllocatedComponent = mineLayerComponent;
            hull.Modules.Add(mineLayerModule);
            shipDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken shipToken = new ShipToken(shipDesign, 5); // 200 mines/application
            fleet.Composition.Add(shipToken.Key, shipToken);

            LayMines layMines = new LayMines(serverData);

            layMines.Lay(fleet);
            Assert.AreEqual(1, serverData.AllMinefields.Count);
            Assert.AreEqual(200, serverData.AllMinefields.Values.First().NumberOfMines);

            // Same fleet, same position, laying again - should grow the existing field.
            layMines.Lay(fleet);
            Assert.AreEqual(1, serverData.AllMinefields.Count, "A second Lay Mines application at the same spot should grow the existing field, not create a new one");
            Assert.AreEqual(400, serverData.AllMinefields.Values.First().NumberOfMines);

            // A field belonging to a DIFFERENT empire at the same spot must not be added to.
            EmpireData otherEmpire = new SimpleEmpireData();
            otherEmpire.Id = 2;
            serverData.AllEmpires.Add(otherEmpire.Id, otherEmpire);
            Fleet otherFleet = new Fleet(7);
            otherFleet.Owner = 2;
            otherFleet.Position = fleet.Position;
            otherFleet.Composition.Add(shipToken.Key, shipToken);

            new LayMines(serverData).Lay(otherFleet);
            Assert.AreEqual(2, serverData.AllMinefields.Count, "A different empire's mines at the same spot should start a separate field");
        }

        private static ShipDesign MakeMineLayerDesign(long key, int layerRate)
        {
            ShipDesign shipDesign = new ShipDesign(key);
            shipDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            HullModule mineLayerModule = new HullModule();
            Component mineLayerComponent = new Component();
            mineLayerComponent.Properties.Add("Mine Layer", new MineLayer { LayerRate = layerRate });
            mineLayerModule.AllocatedComponent = mineLayerComponent;
            hull.Modules.Add(mineLayerModule);
            shipDesign.Blueprint.Properties.Add("Hull", hull);
            return shipDesign;
        }

        [Test]
        public void Generate_LayMines_NothingOnTheArrivalTurn_ThenTheFullAmountWhileHolding()
        {
            // behavior-specs-10/turn-generation-engine.md section 3: a fleet that moved lays
            // nothing (unless Space Demolition); a fleet whose current waypoint carries Lay Mine
            // Field does not move on, and lays its full total every year after arriving.
            serverData = new SimpleServerData();
            empireData = new SimpleEmpireData();
            empireData.Id = 1;
            serverData.AllEmpires.Add(empireData.Id, empireData);

            Fleet fleet = new Fleet(8);
            fleet.Owner = 1;
            fleet.Position = new NovaPoint(0, 0);
            ShipToken shipToken = new ShipToken(MakeMineLayerDesign(8, 40), 5);
            fleet.Composition.Add(shipToken.Key, shipToken);

            Waypoint here = new Waypoint { Position = new NovaPoint(0, 0), WarpFactor = 0, Task = new NoTask(), Destination = "Space at (0, 0)" };
            Waypoint layHere = new Waypoint { Position = new NovaPoint(10, 0), WarpFactor = 5, Task = new LayMinesTask(), Destination = "Space at (10, 0)" };
            Waypoint beyond = new Waypoint { Position = new NovaPoint(50, 0), WarpFactor = 5, Task = new NoTask(), Destination = "Space at (50, 0)" };
            fleet.Waypoints.Add(here);
            fleet.Waypoints.Add(layHere);
            fleet.Waypoints.Add(beyond);
            empireData.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreEqual(10, fleet.Position.X, "arrived at the lay waypoint");
            Assert.AreEqual(0, serverData.AllMinefields.Count, "the arrival turn: the fleet moved, so it lays nothing");
            Assert.IsInstanceOf<LayMinesTask>(fleet.Waypoints[0].Task, "the order stays on the current waypoint");

            turnGenerator.Generate();

            Assert.AreEqual(10, fleet.Position.X, "a fleet laying mines does not move on");
            Assert.AreEqual(1, serverData.AllMinefields.Count);
            Assert.AreEqual(200, serverData.AllMinefields.Values.First().NumberOfMines);
        }

        [Test]
        public void Generate_AMinefieldHit_StopsTheFleetShortOfItsWaypoint_AndTheLegResumesNextYear()
        {
            // A speed-bump field covering the whole of a warp-9 leg: c = (9 - 5) x 35 = 140 per
            // mille per light-year over 80 rolls, so a miss is a ~1-in-170,000 event.
            serverData = new SimpleServerData();
            empireData = new SimpleEmpireData();
            empireData.Id = 1;
            serverData.AllEmpires.Add(empireData.Id, empireData);
            EmpireData fieldOwner = new SimpleEmpireData();
            fieldOwner.Id = 2;
            serverData.AllEmpires.Add(fieldOwner.Id, fieldOwner);

            Minefield field = new Minefield { NumberOfMines = 10000, FieldType = MinefieldType.SpeedBump };
            field.Key = fieldOwner.GetNextMinefieldKey();
            field.Position = new NovaPoint(40, 0);
            serverData.AllMinefields[field.Key] = field;

            Fleet fleet = new Fleet(9);
            fleet.Owner = 1;
            fleet.Position = new NovaPoint(0, 0);
            ShipDesign shipDesign = new ShipDesign(9);
            shipDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule> { new HullModule() };
            shipDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken shipToken = new ShipToken(shipDesign, 1);
            fleet.Composition.Add(shipToken.Key, shipToken);

            Waypoint destination = new Waypoint { Position = new NovaPoint(81, 0), WarpFactor = 9, Task = new NoTask(), Destination = "Space at (81, 0)" };
            fleet.Waypoints.Add(destination);
            empireData.AddOrUpdateFleet(fleet);

            new SimpleTurnGenerator(serverData).Generate();

            Assert.Less(fleet.Position.X, 81, "stopped at the hit point");
            Assert.AreEqual(2, fleet.Waypoints.Count, "the destination is still pending");
            Assert.AreEqual(81, fleet.Waypoints[1].Position.X);
            Assert.AreEqual(9, fleet.Waypoints[1].WarpFactor, "the leg keeps its ordered warp");
            Assert.Less(field.NumberOfMines, 10000, "the field lost mines");
        }

        [Test]
        public void SetFleetOrbit()
        {
            Fleet fleet = new Fleet(1);
            fleet.InOrbit = null;

            Star star = new Star();
            star.Name = "Star1";
            NovaPoint starPoint = new NovaPoint(0, 0);

            serverData = new SimpleServerData();
            serverData.AllStars.Add(star.Key, star);

            NovaPoint fleetPoint = new NovaPoint(0, 0);
            fleet.Position = fleetPoint;
            serverData.SetFleetOrbit(fleet);
            Assert.AreEqual(star.Name, fleet.InOrbit.Name);

            fleet.Position.X = 1;
            serverData.SetFleetOrbit(fleet);
            Assert.AreEqual(null, fleet.InOrbit);
        }
    }
}
