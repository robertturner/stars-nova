namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;

    /// <summary>
    /// behavior-specs-10/production-queue.md sections 8, 10a, 10e and 10i (coverage rows 14, 29
    /// and 35): the queue walk's status codes, the queue messages 62/63/298, the starbase notices
    /// 205-207, the 512-fleet cap (messages 186/313) and the disaster queue cleanup.
    /// </summary>
    [TestFixture]
    public class ProductionQueueMessagesTest
    {
        private static Race NewRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            race.GrowthRate = 0;
            race.ColonistsPerResource = 1000;
            race.OperableFactories = 10;
            race.OperableMines = 10;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.MineBuildCost = 5;
            return race;
        }

        private static Star NewStar(Race race)
        {
            Star star = new Star();
            star.Name = "Tierra";
            star.Owner = 1;
            star.ThisRace = race;
            star.Gravity = star.OriginalGravity = race.GravityTolerance.OptimumLevel;
            star.Temperature = star.OriginalTemperature = race.TemperatureTolerance.OptimumLevel;
            star.Radiation = star.OriginalRadiation = race.RadiationTolerance.OptimumLevel;
            star.Colonists = 100000;
            return star;
        }

        private static ServerData NewServer(Race race, out EmpireData empire)
        {
            ServerData server = new ServerData();
            empire = new SimpleEmpireData();
            empire.Id = 1;
            empire.Race = race;
            empire.AvailableComponents = new RaceComponents();
            server.AllEmpires.Add(empire.Id, empire);
            return server;
        }

        private static List<string> Texts(ServerData server)
        {
            return server.AllMessages.Select(m => m.Text).ToList();
        }

        // ---- 62 / 63 ----

        [Test]
        public void Message63_AnOwnedPlanetWithAnEmptyQueue()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);

            new Manufacture(server).Items(star);

            CollectionAssert.AreEqual(new[] { "The production queue on Tierra is empty." }, Texts(server));
        }

        [Test]
        public void Message62_WhenTheLastEntryCompletes_ThenMessage63NextTurn()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);
            star.ResourcesOnHand = new Resources(0, 0, 0, 100);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new MineProductionUnit(race), false));

            Manufacture manufacture = new Manufacture(server);
            manufacture.Items(star);
            Assert.AreEqual(2, star.Mines);
            CollectionAssert.Contains(Texts(server), "Tierra has completed all of its production orders.");

            server.AllMessages.Clear();
            manufacture.Items(star);
            CollectionAssert.AreEqual(new[] { "The production queue on Tierra is empty." }, Texts(server));
        }

        [Test]
        public void Message62_EveryTurn_ForAQueueOfAutoEntriesThatAllBoughtTheirAllowance()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new MineProductionUnit(race), true));

            new Manufacture(server).Items(star);

            Assert.AreEqual(2, star.Mines);
            CollectionAssert.Contains(Texts(server), "Tierra has completed all of its production orders.");
        }

        [Test]
        public void NoMessage62_WhenAnAutoEntryIsHeldBackByAMineral()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000); // no Germanium for factories
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new FactoryProductionUnit(race), true));
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new MineProductionUnit(race), true));

            new Manufacture(server).Items(star);

            Assert.AreEqual(0, star.Factories);
            Assert.AreEqual(2, star.Mines, "Status 3/4: the walk continues past the mineral-blocked auto entry");
            CollectionAssert.DoesNotContain(Texts(server), "Tierra has completed all of its production orders.");
        }

        // ---- Status 5-7: a manual entry short of a mineral blocks everything behind it ----

        [Test]
        public void ManualEntryShortOfAMineral_StopsTheWalk()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000); // no Germanium
            ProductionOrder factory = new ProductionOrder(1, new FactoryProductionUnit(race), false);
            star.ManufacturingQueue.Queue.Add(factory);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(5, new MineProductionUnit(race), true));

            new Manufacture(server).Items(star);

            Assert.AreEqual(0, star.Mines, "The auto entry behind a blocked manual entry gets nothing");
            Assert.AreEqual(2, star.ManufacturingQueue.Queue.Count);
        }

        [Test]
        public void ManualEntryWithAMineralPartlyShort_PaysItsShare_AndStopsTheWalk()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);
            star.ResourcesOnHand = new Resources(0, 0, 6, 1000); // one factory (4 kT) and half of the next
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(3, new FactoryProductionUnit(race), false));
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(5, new MineProductionUnit(race), true));

            new Manufacture(server).Items(star);

            Assert.AreEqual(1, star.Factories);
            Assert.AreEqual(0, star.Mines);
        }

        // ---- 298 ----

        [Test]
        public void Message298_AManualFactoryOrderOverTheBuildCapIsCutToTheRoom()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);
            star.Factories = star.GetBuildCapFactories() - 2;
            star.ResourcesOnHand = new Resources(0, 0, 0, 0);
            ProductionOrder order = new ProductionOrder(10, new FactoryProductionUnit(race), false);
            star.ManufacturingQueue.Queue.Add(order);

            new Manufacture(server).Items(star);

            Assert.AreEqual(2, order.Quantity);
            CollectionAssert.Contains(Texts(server), "The Factory order on Tierra exceeded the allowed maximum and has been reduced to 2.");
        }

        [Test]
        public void Message298_AtTheCap_TheOrderIsDeleted()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);
            star.Mines = star.GetBuildCapMines();
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(3, new MineProductionUnit(race), false));

            new Manufacture(server).Items(star);

            Assert.AreEqual(0, star.ManufacturingQueue.Queue.Count);
            CollectionAssert.Contains(Texts(server), "The Mine order on Tierra exceeded the allowed maximum and has been removed.");
        }

        [Test]
        public void NoMessage298_WhenTheOrderFits()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out _);
            Star star = NewStar(race);
            star.ResourcesOnHand = new Resources(0, 0, 0, 0);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(3, new MineProductionUnit(race), false));

            new Manufacture(server).Items(star);

            Assert.IsFalse(Texts(server).Any(t => t.Contains("exceeded the allowed maximum")));
        }

        // ---- Disaster queue cleanup (row 29) ----

        [Test]
        public void DisasterCleanup_KeepsAutoBuildEntriesInOrder_AndDeletesEverythingElse()
        {
            Race race = NewRace();
            ShipDesign design = new ShipDesign(1) { Name = "Scout" };

            ProductionQueue queue = new ProductionQueue();
            ProductionOrder autoFactories = new ProductionOrder(10, new FactoryProductionUnit(race), true);
            ProductionOrder manualMines = new ProductionOrder(5, new MineProductionUnit(race), false);
            ProductionOrder autoTerraform = new ProductionOrder(1, new TerraformProductionUnit(race, true), true);
            ProductionOrder manualTerraform = new ProductionOrder(1, new TerraformProductionUnit(race), false);
            ProductionOrder autoShips = new ProductionOrder(2, new ShipProductionUnit(design), true);
            ProductionOrder autoAlchemy = new ProductionOrder(1, new AlchemyProductionUnit(race), true);
            queue.Queue.AddRange(new[] { autoFactories, manualMines, autoTerraform, manualTerraform, autoShips, autoAlchemy });

            queue.CleanupAfterDisaster();

            CollectionAssert.AreEqual(new[] { autoFactories, autoTerraform, autoAlchemy }, queue.Queue);
        }

        // ---- Starbases and ships (205-207, 186, 313) ----

        private static ShipDesign StarbaseDesign(EmpireData empire, string name, int dockCapacity)
        {
            ShipDesign design = new ShipDesign(empire.GetNextDesignKey()) { Type = ItemType.Starbase, Name = name };
            design.Blueprint = new Component();
            design.Blueprint.Properties.Add("Hull", new Hull { Modules = new List<HullModule>(), DockCapacity = dockCapacity });
            empire.Designs.Add(design.Key, design);
            return design;
        }

        [TestCase(0, "Tierra has built a new Fort starbase.")]
        [TestCase(200, "Tierra has built a new Fort starbase. Ships up to 200kT total hull weight can now be built there.")]
        [TestCase(10000, "Tierra has built a new Fort starbase. Ships of any size can now be built there.")]
        public void StarbaseCompletion_PostsMessage205To207ByDockCapacity(int dock, string expected)
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out EmpireData empire);
            Star star = NewStar(race);
            empire.OwnedStars.Add(star);
            ShipDesign design = StarbaseDesign(empire, "Fort", dock);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(design), false));

            new Manufacture(server).Items(star);

            Assert.IsNotNull(star.Starbase);
            CollectionAssert.Contains(Texts(server), expected);
            Assert.IsFalse(Texts(server).Any(t => t.Contains("has produced")), "205-207 replace the generic notice for a starbase");
        }

        private static ShipDesign ShipDesign(EmpireData empire)
        {
            ShipDesign design = new ShipDesign(empire.GetNextDesignKey()) { Type = ItemType.Ship, Name = "Scout" };
            design.Blueprint = new Component();
            design.Blueprint.Properties.Add("Hull", new Hull { Modules = new List<HullModule>(), FuelCapacity = 100 });
            empire.Designs.Add(design.Key, design);
            return design;
        }

        private static void FillFleetTable(EmpireData empire, ShipDesign design, Star elsewhere, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Fleet fleet = new Fleet(new ShipToken(design, 1), elsewhere, empire.GetNextFleetKey());
                fleet.InOrbit = elsewhere;
                empire.AddOrUpdateFleet(fleet);
            }
        }

        [Test]
        public void At512Fleets_NewShipsMergeIntoAFleetOfTheSameDesignAtThePlanet_Message313()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out EmpireData empire);
            Star star = NewStar(race);
            Star elsewhere = new Star { Name = "Elsewhere", Owner = 1 };
            ShipDesign design = ShipDesign(empire);

            FillFleetTable(empire, design, elsewhere, Manufacture.MaxFleetsPerRace - 1);
            Fleet local = new Fleet(new ShipToken(design, 3), star, empire.GetNextFleetKey());
            local.InOrbit = star;
            empire.AddOrUpdateFleet(local);

            star.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new ShipProductionUnit(design), false));
            new Manufacture(server).Items(star);

            Assert.AreEqual(Manufacture.MaxFleetsPerRace, empire.OwnedFleets.Count, "No new fleet");
            Assert.AreEqual(5, local.Composition[design.Key].Quantity);
            Assert.IsTrue(Texts(server).Any(t => t.Contains("have joined")));
        }

        [Test]
        public void At512Fleets_WithNoFleetToJoin_TheShipsAreLost_Message186()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out EmpireData empire);
            Star star = NewStar(race);
            Star elsewhere = new Star { Name = "Elsewhere", Owner = 1 };
            ShipDesign design = ShipDesign(empire);
            FillFleetTable(empire, design, elsewhere, Manufacture.MaxFleetsPerRace);

            ProductionOrder order = new ProductionOrder(2, new ShipProductionUnit(design), false);
            star.ManufacturingQueue.Queue.Add(order);
            new Manufacture(server).Items(star);

            Assert.AreEqual(Manufacture.MaxFleetsPerRace, empire.OwnedFleets.Count);
            Assert.AreEqual(0, star.ManufacturingQueue.Queue.Count, "The order was still consumed: no refund");
            Assert.IsTrue(Texts(server).Any(t => t.Contains("have been lost")));
        }

        [Test]
        public void Under512Fleets_ANewFleetIsCreatedAsBefore()
        {
            Race race = NewRace();
            ServerData server = NewServer(race, out EmpireData empire);
            Star star = NewStar(race);
            ShipDesign design = ShipDesign(empire);

            star.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new ShipProductionUnit(design), false));
            new Manufacture(server).Items(star);

            Assert.AreEqual(1, empire.OwnedFleets.Count);
            Assert.IsTrue(Texts(server).Any(t => t.Contains("has produced 2 new Scout")));
        }
    }
}
