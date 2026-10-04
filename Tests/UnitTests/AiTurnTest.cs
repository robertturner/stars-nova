namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    using NUnit.Framework;

    /// <summary>
    /// Whole-turn checks of the default AI (docs/behavior-specs-10/ai-opponent-behavior.md):
    /// colony fleets actually colonize (the retracted §9 "commitment" gate used to drop every
    /// Colony Ship), the §12/§13 colonist loads, the targetless colony fleet, and the per-planet
    /// production steps of §6 (the queue is kept, the advisors, the bomber pass and the top-up).
    /// </summary>
    [TestFixture]
    public class AiTurnTest
    {
        /// <summary>DefaultAi driven from a hand-built ClientData instead of an intel file.</summary>
        private class TestableAi : DefaultAi
        {
            public TestableAi(ClientData state, int? personality = null)
            {
                clientState = state;
                if (personality.HasValue)
                {
                    commandArguments = new CommandArguments();
                    commandArguments.Add(CommandArguments.Option.AiPersonality, personality.Value);
                }
            }
        }

        private ClientData clientState;
        private Race race;
        private Star home;
        private uint nextFleetId = 1;

        [SetUp]
        public void SetUp()
        {
            clientState = new ClientData();
            clientState.EmpireState.Id = 1;
            race = new Race();
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.OperableFactories = 10;
            race.MineBuildCost = 5;
            race.MineProductionRate = 10;
            race.OperableMines = 10;
            clientState.EmpireState.Race = race;
            clientState.EmpireState.TurnYear = Global.StartingYear + 1;

            home = AddOwnedStar("Home", new NovaPoint(100, 100), 100000);
        }

        private int Ideal
        {
            get { return race.GravityTolerance.OptimumLevel; }
        }

        private Star AddOwnedStar(string name, NovaPoint position, int colonists)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = clientState.EmpireState.Id;
            star.Position = position;
            star.ThisRace = race;
            star.Colonists = colonists;
            star.Gravity = star.OriginalGravity = Ideal;
            star.Temperature = star.OriginalTemperature = Ideal;
            star.Radiation = star.OriginalRadiation = Ideal;
            star.ResourcesOnHand = new Resources(1000, 1000, 1000, 0);
            clientState.EmpireState.OwnedStars.Add(star);

            StarIntel report = new StarIntel();
            report.Name = name;
            report.Position = position;
            report.Owner = clientState.EmpireState.Id;
            clientState.EmpireState.StarReports.Add(name, report);
            return star;
        }

        private StarIntel AddUnownedPlanet(string name, NovaPoint position)
        {
            StarIntel report = new StarIntel();
            report.Name = name;
            report.Position = position;
            report.Owner = Global.Nobody;
            report.Gravity = Ideal;
            report.Temperature = Ideal;
            report.Radiation = Ideal;
            clientState.EmpireState.StarReports.Add(name, report);
            return report;
        }

        private ShipDesign MakeDesign(string name, string hullName, int cargo, bool colonizer, Dictionary<long, ShipDesign> designs)
        {
            ShipDesign design = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            design.Name = name;
            design.Blueprint = new Component();
            design.Blueprint.Name = hullName;
            Hull hull = new Hull();
            hull.BaseCargo = cargo;
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);

            Engine engine = new Engine();
            engine.FuelConsumption = new[] { 0, 0, 0, 0, 35, 120, 175, 235, 360, 420 };
            design.Blueprint.Properties.Add("Engine", engine);

            if (colonizer)
            {
                design.Blueprint.Properties.Add("Colonizer", new DoubleProperty(1.0));
            }

            design.Update();
            designs[design.Key] = design;
            return design;
        }

        private Fleet AddFleet(string name, ShipDesign design, Star at)
        {
            Fleet fleet = new Fleet(name, clientState.EmpireState.Id, nextFleetId++, at.Position);
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.InOrbit = at;
            fleet.Waypoints.Add(new Waypoint { Position = at.Position, Destination = at.Name });
            clientState.EmpireState.OwnedFleets.Add(fleet);
            return fleet;
        }

        private Fleet AddColonyShip(int cargo = 25)
        {
            ShipDesign design = MakeDesign("Santa Maria", "Colony Ship", cargo, true, clientState.EmpireState.Designs);
            return AddFleet("Colony fleet", design, home);
        }

        // ================================================================ colonization

        /// <summary>
        /// The live bug: HandleColonizing dropped every fleet below 5,000 kT of cargo (the Mystery
        /// Trader's trade threshold, misread as an AI "commitment" rule), and a Colony Ship holds
        /// 25 kT, so the AI never colonized.
        /// </summary>
        [Test]
        public void AColonyShipSizedFleet_GetsAColonizeOrder()
        {
            AddUnownedPlanet("Target", new NovaPoint(130, 100));
            Fleet fleet = AddColonyShip(cargo: 25);

            new TestableAi(clientState).DoMove();

            Waypoint last = fleet.Waypoints.Last();
            Assert.IsInstanceOf<ColoniseTask>(last.Task);
            Assert.AreEqual("Target", last.Destination);
        }

        [Test]
        public void TheDefaultAi_LoadsOneHundredFiftyUnits_CappedByTheHold()
        {
            AddUnownedPlanet("Target", new NovaPoint(130, 100));
            Fleet small = AddColonyShip(cargo: 25);

            new TestableAi(clientState).DoMove();

            CargoTask load = small.Waypoints.Select(w => w.Task).OfType<CargoTask>().Single();
            Assert.AreEqual(CargoMode.Load, load.Mode);
            Assert.AreEqual(25, load.Amount.ColonistsInKilotons, "150 units wanted, the 25 kT hold limits it");
            Assert.AreEqual(0, load.Amount.Germanium, "no minerals are loaded");
        }

        [Test]
        public void TheDefaultAi_DoesNotFillABigHold_ItLoadsItsPersonalityFigure()
        {
            AddUnownedPlanet("Target", new NovaPoint(130, 100));
            Fleet big = AddColonyShip(cargo: 1000);

            new TestableAi(clientState).DoMove();

            CargoTask load = big.Waypoints.Select(w => w.Task).OfType<CargoTask>().Single();
            Assert.AreEqual(150, load.Amount.ColonistsInKilotons, "category 2 loads 150 units (15,000 colonists)");
        }

        [Test]
        public void CategoryZero_LoadsTenUnits()
        {
            AddUnownedPlanet("Target", new NovaPoint(130, 100));
            Fleet big = AddColonyShip(cargo: 1000);

            new TestableAi(clientState, personality: DefaultAi.MinStandardPersonality).DoMove();

            CargoTask load = big.Waypoints.Select(w => w.Task).OfType<CargoTask>().Single();
            Assert.AreEqual(10, load.Amount.ColonistsInKilotons);
        }

        /// <summary>
        /// Fleets are walked in plain table order (§8 retracts the fleet shuffle), so the first
        /// colony fleet claims the nearer planet and the second goes on to the next one.
        /// </summary>
        [Test]
        public void TwoColonyFleets_DoNotTargetTheSamePlanet_AndTheFirstInTableOrderChoosesFirst()
        {
            AddUnownedPlanet("Near", new NovaPoint(110, 100));
            AddUnownedPlanet("Far", new NovaPoint(190, 100));
            Fleet first = AddColonyShip();
            Fleet second = AddFleet("Second", first.Composition.Values.First().Design, home);

            for (int run = 0; run < 8; run++)
            {
                first.Waypoints.RemoveRange(1, first.Waypoints.Count - 1);
                second.Waypoints.RemoveRange(1, second.Waypoints.Count - 1);

                new TestableAi(clientState).DoMove();

                Assert.AreEqual("Near", first.Waypoints.Last().Destination);
                Assert.AreEqual("Far", second.Waypoints.Last().Destination);
            }
        }

        /// <summary>Category 0 re-reads the claims on every search; the first fleet's Colonize
        /// order sits behind the load waypoint Nova writes at its own planet, and must still
        /// claim the target.</summary>
        [Test]
        public void TwoColonyFleets_DoNotTargetTheSamePlanet_InCategoryZeroEither()
        {
            AddUnownedPlanet("Near", new NovaPoint(110, 100));
            AddUnownedPlanet("Far", new NovaPoint(190, 100));
            Fleet first = AddColonyShip();
            Fleet second = AddFleet("Second", first.Composition.Values.First().Design, home);

            new TestableAi(clientState, personality: DefaultAi.MinStandardPersonality).DoMove();

            string trace = string.Join(" | ", new[] { first, second }.Select(f => string.Join(",", f.Waypoints.Select(w => w.Destination + ":" + w.Task?.GetType().Name))));
            Assert.AreEqual("Near", first.Waypoints.Last().Destination, trace);
            Assert.AreEqual("Far", second.Waypoints.Last().Destination, trace);
        }

        [Test]
        public void ATargetlessColonyFleet_WaitsForTheDefaultAi_ButIsScrappedByCategoryZero()
        {
            Fleet waiting = AddColonyShip();
            new TestableAi(clientState).DoMove();
            Assert.AreEqual(1, waiting.Waypoints.Count, "category 2 keeps it and tries again next turn");

            SetUp();
            Fleet scrapped = AddColonyShip();
            new TestableAi(clientState, personality: DefaultAi.MinStandardPersonality).DoMove();
            Assert.IsInstanceOf<ScrapTask>(scrapped.Waypoints.Last().Task, "category 0 scraps it where it stands");
        }

        // ================================================================ production

        [Test]
        public void TheProductionQueue_IsNoLongerClearedEachTurn()
        {
            ProductionOrder existing = new ProductionOrder(3, new DefenseProductionUnit(race), false);
            home.ManufacturingQueue.Queue.Add(existing);

            new TestableAi(clientState).DoMove();

            CollectionAssert.Contains(home.ManufacturingQueue.Queue, existing, "an unstarted item survives the AI's turn");
        }

        [Test]
        public void DefensesAdvisor_QueuesFourAtTheBottom_NotTheWholeCap()
        {
            home.Colonists = 200000;
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new MineProductionUnit(race), false));
            DefaultPlanetAI planetAI = new DefaultPlanetAI(home, clientState, new DefaultAIPlanner(clientState), new Random(1), AiCategory.Automitrons);

            planetAI.RunAdvisorChain();

            ProductionOrder last = home.ManufacturingQueue.Queue.Last();
            Assert.IsInstanceOf<DefenseProductionUnit>(last.Unit);
            Assert.AreEqual(4, last.Quantity);
            Assert.IsFalse(last.IsAutoBuild);
        }

        [Test]
        public void AdvisorChain_StopsAtTheFirstAdvisorThatQueues()
        {
            home.Colonists = 200000;
            home.Gravity = Ideal + 5;
            DefaultPlanetAI planetAI = new DefaultPlanetAI(home, clientState, new DefaultAIPlanner(clientState), new Random(1), AiCategory.Automitrons);

            planetAI.RunAdvisorChain();

            Assert.AreEqual(1, home.ManufacturingQueue.Queue.Count);
            Assert.IsInstanceOf<DefenseProductionUnit>(home.ManufacturingQueue.Queue[0].Unit);
        }

        [Test]
        public void TerraformAdvisor_RunsWhenTheDefensesAdvisorHasNothingToDo()
        {
            home.Colonists = 200000;
            home.Defenses = 25; // population 2,000 units / 80
            home.Gravity = Ideal + 5;
            DefaultPlanetAI planetAI = new DefaultPlanetAI(home, clientState, new DefaultAIPlanner(clientState), new Random(1), AiCategory.Automitrons);

            planetAI.RunAdvisorChain();

            Assert.AreEqual(1, home.ManufacturingQueue.Queue.Count);
            Assert.IsInstanceOf<TerraformProductionUnit>(home.ManufacturingQueue.Queue[0].Unit);
            Assert.AreEqual(4, home.ManufacturingQueue.Queue[0].Quantity, "min(headroom 5, 4)");
        }

        [Test]
        public void TerraformHeadroom_CountsTheRemainingStepsOnEachAxis()
        {
            home.Gravity = home.OriginalGravity = Ideal + 5;
            home.Temperature = Ideal - 20;
            home.OriginalTemperature = Ideal - 30; // 10 of the 15 steps already used

            Assert.AreEqual(5 + 5, DefaultPlanetAI.TerraformHeadroom(home, race));

            home.Gravity = home.OriginalGravity = Ideal;
            home.Temperature = home.OriginalTemperature = Ideal;
            Assert.AreEqual(0, DefaultPlanetAI.TerraformHeadroom(home, race));
        }

        [Test]
        public void TopUp_PutsMinesAtTheTopAndFactoriesAtTheBottom()
        {
            home.Colonists = 50000;
            ProductionOrder existing = new ProductionOrder(1, new DefenseProductionUnit(race), false);
            home.ManufacturingQueue.Queue.Add(existing);
            DefaultPlanetAI planetAI = new DefaultPlanetAI(home, clientState, new DefaultAIPlanner(clientState), new Random(1), AiCategory.Automitrons);

            planetAI.RunTopUp(yearCounter: 10);

            List<ProductionOrder> queue = home.ManufacturingQueue.Queue;
            Assume.That(queue.Count, Is.EqualTo(3), "test setup: expected mines, the existing item and factories");
            Assert.IsInstanceOf<MineProductionUnit>(queue[0].Unit);
            Assert.AreSame(existing, queue[1]);
            Assert.IsInstanceOf<FactoryProductionUnit>(queue[2].Unit);
        }

        [Test]
        public void TopUp_SpendsOnlyTheSurplusAfterTheExistingQueue()
        {
            home.Colonists = 50000;
            int resources = home.GetResourceRate();
            Assume.That(resources, Is.GreaterThan(0));

            // A queued item costing every resource of the year leaves nothing for the top-up.
            Resources cost = new DefenseProductionUnit(race).Cost;
            int quantity = (resources / Math.Max(1, cost.Energy)) + 1;
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(quantity, new DefenseProductionUnit(race), false));
            DefaultPlanetAI planetAI = new DefaultPlanetAI(home, clientState, new DefaultAIPlanner(clientState), new Random(1), AiCategory.Automitrons);

            planetAI.RunTopUp(yearCounter: 10);

            Assert.AreEqual(1, home.ManufacturingQueue.Queue.Count, "a negative resource surplus queues nothing");
        }

        [Test]
        public void BomberDefence_QueuesDefencesAtTheTop_WhenAForeignBomberOrbits()
        {
            clientState.EmpireState.TurnYear = Global.StartingYear + 25; // past (0 + 2) x 10
            Dictionary<long, ShipDesign> foreignDesigns = new Dictionary<long, ShipDesign>();
            ShipDesign bomber = MakeDesign("Raider", "B-17 Bomber", 0, false, foreignDesigns);

            FleetIntel report = new FleetIntel();
            report.Name = "Raiders";
            report.Owner = 2;
            report.Position = home.Position;
            report.InOrbit = true;
            report.Composition = new Dictionary<long, ShipToken>();
            ShipToken token = new ShipToken(bomber, 3);
            report.Composition.Add(token.Key, token);
            clientState.EmpireState.FleetReports.Add(report.Key, report);

            ProductionOrder existing = new ProductionOrder(1, new FactoryProductionUnit(race), false);
            home.ManufacturingQueue.Queue.Add(existing);

            new TestableAi(clientState).DoMove();

            List<ProductionOrder> queue = home.ManufacturingQueue.Queue;
            ProductionOrder defenses = queue.FirstOrDefault(order => order.Unit is DefenseProductionUnit);
            Assert.IsNotNull(defenses, "the bomber pass queued defences");
            // Category 2 (Automitrons) at turn 25 takes a 20% research share: R = 100 - 20 = 80,
            // so n = 80 / 25 = 3, within m = 100.
            Assert.AreEqual(3, defenses.Quantity, "R = 80 after the research share: n = 80 / 25 = 3, within m = 100");
            Assert.Less(queue.IndexOf(defenses), queue.IndexOf(existing), "inserted at the top, above the existing item");
        }

        [Test]
        public void BomberDefence_IgnoresAForeignFleetWithoutBomberHulls()
        {
            clientState.EmpireState.TurnYear = Global.StartingYear + 25;
            Dictionary<long, ShipDesign> foreignDesigns = new Dictionary<long, ShipDesign>();
            ShipDesign frigate = MakeDesign("Picket", "Frigate", 0, false, foreignDesigns);

            FleetIntel report = new FleetIntel();
            report.Name = "Pickets";
            report.Owner = 2;
            report.Position = home.Position;
            report.InOrbit = true;
            report.Composition = new Dictionary<long, ShipToken>();
            ShipToken token = new ShipToken(frigate, 3);
            report.Composition.Add(token.Key, token);
            clientState.EmpireState.FleetReports.Add(report.Key, report);

            new TestableAi(clientState).DoMove();

            Assert.IsFalse(home.ManufacturingQueue.Queue.Any(order => order.Unit is DefenseProductionUnit));
        }
    }
}
