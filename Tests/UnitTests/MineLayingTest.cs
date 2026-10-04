namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    /// <summary>
    /// Mine laying: behavior-specs-10/turn-generation-engine.md section 3, "Mine laying, exact
    /// rule", and fleet-movement-scanning-cargo.md section 5 (task 0 / task 6 rows).
    /// </summary>
    [TestFixture]
    public class MineLayingTest
    {
        private ServerData serverState;
        private EmpireData empire;
        private long nextDesignKey = 200;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            empire = new SimpleEmpireData { Id = 1, Race = new Race { PluralName = "Layers" } };
            serverState.AllEmpires.Add(empire.Id, empire);
        }

        private static Component MineLayerPart(int rate, double hitChance)
        {
            Component part = new Component();
            part.Name = "Dispenser " + rate;
            part.Properties.Add("Mine Layer", new MineLayer { LayerRate = rate, HitChance = hitChance });
            return part;
        }

        /// <summary>A design with the given (part, quantity) slots, optionally on a doubling hull.</summary>
        private ShipDesign MakeDesign(bool mineLayerHull, params (Component part, int quantity)[] slots)
        {
            ShipDesign design = new ShipDesign(nextDesignKey++);
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.ArmorStrength = 100;
            hull.Modules = new List<HullModule>();
            foreach ((Component part, int quantity) in slots)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = quantity, ComponentMaximum = quantity });
            }

            design.Blueprint.Properties.Add("Hull", hull);
            if (mineLayerHull)
            {
                design.Blueprint.Properties.Add("Mine Layer Efficiency", new DoubleProperty(2));
            }

            design.Update();
            return design;
        }

        private Fleet MakeFleet(ShipDesign design, int quantity, int x = 0, int y = 0)
        {
            Fleet fleet = new Fleet(empire.GetNextFleetKey());
            fleet.Name = "Layer fleet";
            fleet.Position = new NovaPoint(x, y);
            ShipToken token = new ShipToken(design, quantity);
            fleet.Composition.Add(token.Key, token);
            return fleet;
        }

        private static Waypoint At(int x, int y, IWaypointTask task)
        {
            return new Waypoint { Position = new NovaPoint(x, y), Task = task, Destination = "Space", WarpFactor = 0 };
        }

        private Minefield AddField(int x, int y, int mines, MinefieldType type = MinefieldType.Standard)
        {
            Minefield field = new Minefield { NumberOfMines = mines, FieldType = type };
            field.Key = empire.GetNextMinefieldKey();
            field.Position = new NovaPoint(x, y);
            serverState.AllMinefields[field.Key] = field;
            return field;
        }

        private int MinesOfType(MinefieldType type)
        {
            return serverState.AllMinefields.Values.Where(f => f.FieldType == type).Sum(f => f.NumberOfMines);
        }

        [Test]
        public void DesignFigure_IsKeptPerType_AndTheMunitionCountsAsFortyStandardMines()
        {
            Component munition = new Component { Name = Fleet.MultiContainedMunitionName };
            ShipDesign design = MakeDesign(
                false,
                (MineLayerPart(40, MineLayer.StandardHitChance), 2),
                (MineLayerPart(110, MineLayer.HeavyHitChance), 1),
                (MineLayerPart(20, MineLayer.SpeedTrapHitChance), 1),
                (munition, 1));

            Assert.AreEqual(2 * 40 + 40, Fleet.DesignMinesPerYear(design, MinefieldType.Standard));
            Assert.AreEqual(110, Fleet.DesignMinesPerYear(design, MinefieldType.Heavy));
            Assert.AreEqual(20, Fleet.DesignMinesPerYear(design, MinefieldType.SpeedBump));

            Fleet fleet = MakeFleet(design, 3);
            Assert.AreEqual(3 * 120, fleet.MinesPerYear(MinefieldType.Standard));
            Assert.AreEqual(3 * (120 + 110 + 20), fleet.NumberOfMines, "the all-type total");
        }

        [Test]
        public void MineLayerHulls_DoubleTheDesignFigure_WorkedExample()
        {
            // Three Mini Mine Layers, each with four Mine Dispenser 40: 3 x (4 x 40) x 2 = 960.
            ShipDesign miniMineLayer = MakeDesign(true, (MineLayerPart(40, MineLayer.StandardHitChance), 4));
            Fleet fleet = MakeFleet(miniMineLayer, 3);
            fleet.Waypoints.Add(At(0, 0, new LayMinesTask()));

            new LayMines(serverState).Process(fleet, movedThisYear: false);

            Assert.AreEqual(960, MinesOfType(MinefieldType.Standard));
        }

        [Test]
        public void AStationaryFleet_LaysItsFullTotalOfEachType_IntoFieldsOfThatType()
        {
            ShipDesign design = MakeDesign(
                false,
                (MineLayerPart(50, MineLayer.StandardHitChance), 1),
                (MineLayerPart(50, MineLayer.HeavyHitChance), 1),
                (MineLayerPart(30, MineLayer.SpeedTrapHitChance), 1));
            Fleet fleet = MakeFleet(design, 2);
            fleet.Waypoints.Add(At(0, 0, new LayMinesTask()));

            new LayMines(serverState).Process(fleet, movedThisYear: false);

            Assert.AreEqual(3, serverState.AllMinefields.Count, "one field per type");
            Assert.AreEqual(100, MinesOfType(MinefieldType.Standard));
            Assert.AreEqual(100, MinesOfType(MinefieldType.Heavy));
            Assert.AreEqual(60, MinesOfType(MinefieldType.SpeedBump));
            Assert.AreEqual(6, serverState.AllMinefields.Values.Single(f => f.FieldType == MinefieldType.Heavy).SafeSpeed);
        }

        [Test]
        public void AFleetThatMoved_LaysNothing_UnlessItIsSpaceDemolition()
        {
            ShipDesign design = MakeDesign(false, (MineLayerPart(45, MineLayer.StandardHitChance), 1), (MineLayerPart(25, MineLayer.HeavyHitChance), 1));
            Fleet fleet = MakeFleet(design, 1);
            fleet.Waypoints.Add(At(0, 0, new LayMinesTask { Duration = 3 }));

            new LayMines(serverState).Process(fleet, movedThisYear: true);
            Assert.AreEqual(0, serverState.AllMinefields.Count, "the arrival turn: a non-SD fleet lays nothing");
            Assert.AreEqual(3, ((LayMinesTask)fleet.Waypoints[0].Task).Duration, "nor is the duration touched");

            empire.Race.Traits.SetPrimary("SD");
            new LayMines(serverState).Process(fleet, movedThisYear: true);
            Assert.AreEqual(22, MinesOfType(MinefieldType.Standard), "half of 45, rounded down");
            Assert.AreEqual(12, MinesOfType(MinefieldType.Heavy), "halved per type: half of 25, rounded down");
        }

        [Test]
        public void SpaceDemolition_LaysAtHalfRateEnRoute_WhenTheNextWaypointIsLayMineField()
        {
            empire.Race.Traits.SetPrimary("SD");
            ShipDesign design = MakeDesign(false, (MineLayerPart(80, MineLayer.StandardHitChance), 1));
            Fleet fleet = MakeFleet(design, 1, 10, 0);
            fleet.Waypoints.Add(At(10, 0, new NoTask()));
            fleet.Waypoints.Add(At(100, 0, new LayMinesTask()));

            new LayMines(serverState).Process(fleet, movedThisYear: true);

            Assert.AreEqual(40, MinesOfType(MinefieldType.Standard));
        }

        [Test]
        public void OtherRaces_DoNotLayEnRoute()
        {
            ShipDesign design = MakeDesign(false, (MineLayerPart(80, MineLayer.StandardHitChance), 1));
            Fleet fleet = MakeFleet(design, 1, 10, 0);
            fleet.Waypoints.Add(At(10, 0, new NoTask()));
            fleet.Waypoints.Add(At(100, 0, new LayMinesTask()));

            new LayMines(serverState).Process(fleet, movedThisYear: false);

            Assert.AreEqual(0, serverState.AllMinefields.Count);
        }

        [Test]
        public void TheDurationCounter_IsDecremented_ExceptFive_AndZeroEndsTheOrderAfterThisYear()
        {
            ShipDesign design = MakeDesign(false, (MineLayerPart(40, MineLayer.StandardHitChance), 1));
            LayMines layMines = new LayMines(serverState);

            Fleet counted = MakeFleet(design, 1);
            LayMinesTask countedTask = new LayMinesTask { Duration = 2 };
            counted.Waypoints.Add(At(0, 0, countedTask));
            layMines.Process(counted, false);
            Assert.AreEqual(1, countedTask.Duration);

            Fleet indefinite = MakeFleet(design, 1, 500, 500);
            LayMinesTask indefiniteTask = new LayMinesTask();
            indefinite.Waypoints.Add(At(500, 500, indefiniteTask));
            layMines.Process(indefinite, false);
            Assert.AreEqual(LayMinesTask.Indefinitely, indefiniteTask.Duration);
            Assert.IsInstanceOf<LayMinesTask>(indefinite.Waypoints[0].Task);

            Fleet lastYear = MakeFleet(design, 1, 900, 900);
            lastYear.Waypoints.Add(At(900, 900, new LayMinesTask { Duration = 0 }));
            layMines.Process(lastYear, false);
            Assert.IsInstanceOf<NoTask>(lastYear.Waypoints[0].Task, "0: last year, the task is cleared");
            Assert.AreEqual(40, serverState.AllMinefields.Values.Single(f => f.Position.X == 900).NumberOfMines, "but it still lays this year");
        }

        [Test]
        public void AFleetWithNoMineLayingPart_HasTheOrderCancelled()
        {
            ShipDesign design = MakeDesign(false);
            Fleet fleet = MakeFleet(design, 1);
            fleet.Waypoints.Add(At(0, 0, new LayMinesTask()));

            new LayMines(serverState).Process(fleet, false);

            Assert.IsInstanceOf<NoTask>(fleet.Waypoints[0].Task);
            Assert.AreEqual(1, serverState.AllMessages.Count);
            StringAssert.Contains("no ship in the fleet has a mine laying pod", serverState.AllMessages[0].Text);
        }

        [Test]
        public void LaidMines_MergeIntoTheNearestCoveringOwnFieldOfTheSameType_MovingItsCentre()
        {
            Minefield near = AddField(0, 0, 300);
            Minefield far = AddField(25, 0, 400);         // squared distance 225 <= 400, but farther
            Minefield heavy = AddField(10, 0, 1000, MinefieldType.Heavy);
            ShipDesign design = MakeDesign(false, (MineLayerPart(100, MineLayer.StandardHitChance), 1));
            Fleet fleet = MakeFleet(design, 1, 10, 0);    // squared distance 100 <= 300

            new LayMines(serverState).Lay(fleet, MinefieldType.Standard, 100);

            Assert.AreEqual(3, serverState.AllMinefields.Count);
            Assert.AreEqual(400, near.NumberOfMines);
            Assert.AreEqual((0 * 300 + 10 * 100) / 400, near.Position.X, "mine-weighted centre");
            Assert.AreEqual(400, far.NumberOfMines);
            Assert.AreEqual(1000, heavy.NumberOfMines, "a field of another type is never added to");
        }

        [Test]
        public void AFieldThatDoesNotCoverTheFleet_OrHoldsMoreThan999999Mines_IsNotAddedTo()
        {
            Minefield small = AddField(0, 0, 99);   // squared distance 100 > 99
            ShipDesign design = MakeDesign(false, (MineLayerPart(100, MineLayer.StandardHitChance), 1));
            Fleet fleet = MakeFleet(design, 1, 10, 0);
            LayMines layMines = new LayMines(serverState);

            layMines.Lay(fleet, MinefieldType.Standard, 100);
            Assert.AreEqual(99, small.NumberOfMines);
            Assert.AreEqual(2, serverState.AllMinefields.Count);

            serverState.AllMinefields.Clear();
            Minefield huge = AddField(10, 0, 1000000);
            layMines.Lay(fleet, MinefieldType.Standard, 100);
            Assert.AreEqual(1000000, huge.NumberOfMines);
            Assert.AreEqual(2, serverState.AllMinefields.Count, "a field above 999,999 mines forces a new field");
        }

        [Test]
        public void SpeedBumpFields_DecayWithoutTheTenMineFloor()
        {
            Assert.AreEqual(2, Nova.Server.TurnSteps.MinefieldDecayStep.MinesLost(100, 2, MinefieldType.SpeedBump));
            Assert.AreEqual(10, Nova.Server.TurnSteps.MinefieldDecayStep.MinesLost(100, 2, MinefieldType.Standard));
        }
    }
}
