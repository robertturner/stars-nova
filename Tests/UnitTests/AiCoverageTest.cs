namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Spec-driven coverage of behavior-specs-10/ai-opponent-behavior.md, coverage rows 17
    /// (freighter travel time T = (d + 24) / 25 with d the TRUNCATED distance, at least 1; §5
    /// step 3), 24 (the 200-entry production-queue insertion cap, §6), 30 (the per-turn
    /// Fisher-Yates shuffle of the owned-planet order, §8) and 53 (the Terraform advisor
    /// FUN_1090_55b0, §6). The terraform headroom is a stand-in (TerraformProductionUnit's
    /// rule); these tests read it through DefaultPlanetAI.TerraformHeadroom and pin only the
    /// advisor's gates and its min(C, 4).
    /// </summary>
    [TestFixture]
    public class AiCoverageTest
    {
        /// <summary>Queued draws; records each bound. Next(n) returns the queued value (clamped
        /// into range), 0 when the queue is empty.</summary>
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> rolls;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = new Queue<int>(rolls);
            }

            public List<int> Bounds { get; } = new List<int>();

            public override int Next(int maxValue)
            {
                Bounds.Add(maxValue);
                return rolls.Count > 0 ? Math.Min(rolls.Dequeue(), Math.Max(0, maxValue - 1)) : 0;
            }

            public override int Next(int minValue, int maxValue)
            {
                return minValue + Next(maxValue - minValue);
            }
        }

        private class TestableAi : DefaultAi
        {
            public TestableAi(ClientData state, int personality, Random random)
            {
                clientState = state;
                commandArguments = new CommandArguments();
                commandArguments.Add(CommandArguments.Option.AiPersonality, personality);
                AiRandom = random;
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
            nextFleetId = 1;

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

        private DefaultPlanetAI PlanetAi(Star star, int category)
        {
            return new DefaultPlanetAI(star, clientState, new DefaultAIPlanner(clientState), new ScriptedRandom(), category);
        }

        // ================================================================ row 17: travel time

        [Test]
        public void Row17_TravelTime_TruncatesTheDistanceFirst_ThenRoundsUpToLegsOf25_AtLeastOne()
        {
            Assert.AreEqual(1, FreighterRoutingSelector.TravelTime(0), "(0 + 24) / 25 = 0, raised to 1");
            Assert.AreEqual(1, FreighterRoutingSelector.TravelTime(1), "d = 1");
            Assert.AreEqual(1, FreighterRoutingSelector.TravelTime((26 * 26) - 1), "25.98 ly truncates to 25: one leg, not two");
            Assert.AreEqual(2, FreighterRoutingSelector.TravelTime(26 * 26));
            Assert.AreEqual(2, FreighterRoutingSelector.TravelTime(50 * 50));
            Assert.AreEqual(2, FreighterRoutingSelector.TravelTime((51 * 51) - 1), "50.99 ly truncates to 50");
            Assert.AreEqual(3, FreighterRoutingSelector.TravelTime(51 * 51));
            Assert.AreEqual(40, FreighterRoutingSelector.TravelTime(1000 * 1000));
        }

        private Star AddSource(string name, NovaPoint position, int ironium, int boranium, int germanium, bool starbase = false)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = clientState.EmpireState.Id;
            star.Position = position;
            star.ResourcesOnHand = new Resources(ironium, boranium, germanium, 0);
            if (starbase)
            {
                star.Starbase = new Fleet("Starbase " + name, clientState.EmpireState.Id, 900 + nextFleetId++, position);
            }

            clientState.EmpireState.OwnedStars.Add(star);
            return star;
        }

        private Fleet AddFreighter(Star at, int capacity)
        {
            ShipDesign design = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            design.Blueprint = new Component();
            Hull hull = new Hull { BaseCargo = capacity, Modules = new List<HullModule>() };
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();

            Fleet fleet = new Fleet("Freighter", clientState.EmpireState.Id, nextFleetId++, at.Position);
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.Waypoints.Add(new Waypoint { Position = at.Position, Destination = at.Name });
            fleet.InOrbit = at;
            clientState.EmpireState.OwnedFleets.Add(fleet);
            return fleet;
        }

        [Test]
        public void Row17_TheScoreIsValueOverTheIntegerT_FromTheTruncatedDistance()
        {
            // A fresh empire with a hub only (no "Home" planet in the table).
            clientState.EmpireState.OwnedStars.Clear();
            clientState.EmpireState.StarReports.Clear();
            Star hub = AddSource("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true); // scarcity level 0
            // Oblique: sqrt(25^2 + 5^2) = 25.5 ly -> d = 25 -> T = 1; M = 900 -> value 9,000.
            AddSource("Oblique", new NovaPoint(25, 5), 300, 300, 300);
            // Close: 10 ly -> T = 1; M = 890 -> value 8,900. With an un-truncated, rounded-up
            // d / 25 the oblique planet would score 9,000 / 2 = 4,500 and lose.
            AddSource("Close", new NovaPoint(10, 0), 297, 297, 296);
            Fleet freighter = AddFreighter(hub, 1000);

            FreighterRun run = new FreighterRoutingSelector(clientState, AiCategory.Automitrons, 50).SelectRun(freighter);

            Assert.IsNotNull(run);
            Assert.AreEqual("Oblique", run.Destination.Name);
        }

        // ================================================================ row 24: the 200-entry cap

        private void FillQueue(Star star, int entries)
        {
            for (int i = 0; i < entries; i++)
            {
                star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new MineProductionUnit(race), false));
            }
        }

        [Test]
        public void Row24_AnInsertIsAcceptedUpTo200Entries_ThenRefusedAtTheTopAndTheBottom()
        {
            DefaultPlanetAI planetAi = PlanetAi(home, AiCategory.Automitrons);
            FillQueue(home, 199);

            Assert.IsTrue(planetAi.QueueItem(new ProductionOrder(1, new FactoryProductionUnit(race), false), atTop: false), "the 200th entry");
            Assert.AreEqual(200, home.ManufacturingQueue.Queue.Count);
            int commands = clientState.Commands.Count;

            Assert.IsFalse(planetAi.QueueItem(new ProductionOrder(1, new FactoryProductionUnit(race), false), atTop: false), "a 201st at the bottom");
            Assert.IsFalse(planetAi.QueueItem(new ProductionOrder(1, new FactoryProductionUnit(race), false), atTop: true), "a 201st at the top");
            Assert.AreEqual(200, home.ManufacturingQueue.Queue.Count);
            Assert.AreEqual(commands, clientState.Commands.Count, "a refused insert sends no order");
        }

        [Test]
        public void Row24_TheCapIsOnEntries_NotOnTheQuantityOfOneEntry()
        {
            DefaultPlanetAI planetAi = PlanetAi(home, AiCategory.Automitrons);

            Assert.IsTrue(planetAi.QueueItem(new ProductionOrder(5000, new FactoryProductionUnit(race), false), atTop: false));
            Assert.AreEqual(1, home.ManufacturingQueue.Queue.Count);
            Assert.AreEqual(1023, home.ManufacturingQueue.Queue[0].Quantity, "the separate per-item quantity cap of production-queue.md");
        }

        [Test]
        public void Row24_AnAdvisorFindsAFullQueueClosed()
        {
            home.Colonists = 200000;
            home.Gravity = Ideal + 5;
            FillQueue(home, 200);
            foreach (ProductionOrder order in home.ManufacturingQueue.Queue)
            {
                order.Quantity = 0; // cost nothing, so the queue-affordability gate stays open
            }

            PlanetAi(home, AiCategory.Automitrons).RunAdvisorChain();

            Assert.AreEqual(200, home.ManufacturingQueue.Queue.Count);
            Assert.IsFalse(home.ManufacturingQueue.Queue.Any(o => o.Unit is TerraformProductionUnit || o.Unit is DefenseProductionUnit));
        }

        // ================================================================ row 30: the planet shuffle

        private static List<string> ShuffledPlanetOrder(DefaultAi ai)
        {
            FieldInfo field = typeof(DefaultAi).GetField("shuffledPlanetAIs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "DefaultAi keeps the run's shuffled planet list");
            return ((List<DefaultPlanetAI>)field.GetValue(ai)).Select(p => p.Planet.Name).ToList();
        }

        [Test]
        public void Row30_ThePlanetOrderIsAForwardFisherYatesShuffle_OfTheOwnedPlanetTable()
        {
            // §8 (FUN_1090_60de): "for each index from 0 up to count-2, it draws a bounded random
            // offset into the remaining unshuffled suffix and swaps that element to the front".
            // Table order Home, B, C, D; offsets 2, 0, 1:
            //   i = 0: swap with 0 + 2 -> C B Home D
            //   i = 1: swap with 1 + 0 -> C B Home D
            //   i = 2: swap with 2 + 1 -> C B D Home
            AddOwnedStar("B", new NovaPoint(200, 100), 1000);
            AddOwnedStar("C", new NovaPoint(300, 100), 1000);
            AddOwnedStar("D", new NovaPoint(400, 100), 1000);
            List<string> table = clientState.EmpireState.OwnedStars.Values.Select(s => s.Name).ToList();
            CollectionAssert.AreEqual(new[] { "Home", "B", "C", "D" }, table);

            ScriptedRandom random = new ScriptedRandom(2, 0, 1);
            TestableAi ai = new TestableAi(clientState, DefaultAi.PassivePersonality, random);
            ai.DoMove();

            CollectionAssert.AreEqual(new[] { 4, 3, 2 }, random.Bounds.Take(3).ToList(), "bounded by the size of the unshuffled suffix");
            CollectionAssert.AreEqual(new[] { "C", "B", "D", "Home" }, ShuffledPlanetOrder(ai));
        }

        [Test]
        public void Row30_TheShuffleIsRedoneEachRun_AndEveryPlanetIsVisitedOnce()
        {
            AddOwnedStar("B", new NovaPoint(200, 100), 1000);
            AddOwnedStar("C", new NovaPoint(300, 100), 1000);

            TestableAi first = new TestableAi(clientState, DefaultAi.PassivePersonality, new ScriptedRandom(0, 0));
            first.DoMove();
            CollectionAssert.AreEqual(new[] { "Home", "B", "C" }, ShuffledPlanetOrder(first), "offsets 0, 0 keep the table order");

            TestableAi second = new TestableAi(clientState, DefaultAi.PassivePersonality, new ScriptedRandom(1, 1));
            second.DoMove();
            List<string> order = ShuffledPlanetOrder(second);
            CollectionAssert.AreEqual(new[] { "B", "C", "Home" }, order, "offsets 1, 1: B Home C, then B C Home");
            CollectionAssert.AreEquivalent(new[] { "Home", "B", "C" }, order);
        }

        // ================================================================ row 53: the Terraform advisor

        private void MakeTerraformable(Star star, int colonists)
        {
            star.Colonists = colonists;           // under 1,600 units: the Defenses advisor stays out
            star.Gravity = star.OriginalGravity = Ideal + 10;
        }

        private static List<ProductionOrder> Terraform(Star star)
        {
            return star.ManufacturingQueue.Queue.Where(o => o.Unit is TerraformProductionUnit).ToList();
        }

        private int ExpectedQuantity(int queued)
        {
            return Math.Min(DefaultPlanetAI.TerraformHeadroom(home, race) - queued, 4);
        }

        [TestCase(AiCategory.Robotoids)]
        [TestCase(AiCategory.Cybertrons)]
        public void Row53_NeverForCategoriesZeroAndFour(int category)
        {
            MakeTerraformable(home, 50000);

            PlanetAi(home, category).RunAdvisorChain();

            Assert.IsEmpty(Terraform(home));
        }

        [TestCase(AiCategory.Turindrones)]
        [TestCase(AiCategory.Automitrons)]
        [TestCase(AiCategory.Rototills)]
        [TestCase(AiCategory.Macinti)]
        [TestCase(AiCategory.EconomyOnly)]
        public void Row53_TheOtherCategories_QueueMinOfTheCapAndFour_AtTheBottom(int category)
        {
            MakeTerraformable(home, 50000);
            ProductionOrder existing = new ProductionOrder(0, new MineProductionUnit(race), false);
            home.ManufacturingQueue.Queue.Add(existing);
            Assert.Greater(DefaultPlanetAI.TerraformHeadroom(home, race), 4, "fixture: a cap above 4");

            PlanetAi(home, category).RunAdvisorChain();

            List<ProductionOrder> queued = Terraform(home);
            Assert.AreEqual(1, queued.Count);
            Assert.AreEqual(4, queued[0].Quantity, "min(C, 4)");
            Assert.IsFalse(queued[0].IsAutoBuild, "a manual Terraform Environment item");
            Assert.AreSame(existing, home.ManufacturingQueue.Queue[0]);
            Assert.AreSame(queued[0], home.ManufacturingQueue.Queue.Last(), "at the bottom of the queue");
        }

        [Test]
        public void Row53_NeedsTwoHundredUnits()
        {
            MakeTerraformable(home, 19999); // 199 units
            PlanetAi(home, AiCategory.Automitrons).RunAdvisorChain();
            Assert.IsEmpty(Terraform(home), "199 units");

            home.Colonists = 20000;          // 200 units
            PlanetAi(home, AiCategory.Automitrons).RunAdvisorChain();
            Assert.AreEqual(1, Terraform(home).Count, "200 units");
        }

        [Test]
        public void Row53_NotWhileAManualTerraformItemIsQueued()
        {
            MakeTerraformable(home, 50000);
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new TerraformProductionUnit(race), false));

            PlanetAi(home, AiCategory.Automitrons).RunAdvisorChain();

            Assert.AreEqual(1, Terraform(home).Count, "nothing added");
            Assert.AreEqual(1, Terraform(home)[0].Quantity);
        }

        [Test]
        public void Row53_AnAutoBuildTerraformItemDoesNotBlock_ButCountsAgainstTheCap()
        {
            MakeTerraformable(home, 50000);
            int headroom = DefaultPlanetAI.TerraformHeadroom(home, race);
            int queued = headroom - 2; // leaves C = 2
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(queued, new TerraformProductionUnit(race), true));

            PlanetAi(home, AiCategory.Automitrons).RunAdvisorChain();

            List<ProductionOrder> manual = Terraform(home).Where(o => !o.IsAutoBuild).ToList();
            Assert.AreEqual(1, manual.Count);
            Assert.AreEqual(ExpectedQuantity(queued), manual[0].Quantity);
            Assert.AreEqual(2, manual[0].Quantity, "C = headroom - queued = 2");
        }

        [Test]
        public void Row53_NothingWhenTheQueuedAmountUsesUpTheHeadroom()
        {
            MakeTerraformable(home, 50000);
            int headroom = DefaultPlanetAI.TerraformHeadroom(home, race);
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(headroom, new TerraformProductionUnit(race), true));

            PlanetAi(home, AiCategory.Automitrons).RunAdvisorChain();

            Assert.IsFalse(Terraform(home).Any(o => !o.IsAutoBuild), "C = 0");
        }

        [Test]
        public void Row53_NothingWhenEveryAxisIsAtTheIdeal()
        {
            home.Colonists = 50000;

            Assert.IsFalse(DefaultPlanetAI.AnyAxisOffIdeal(home, race));
            PlanetAi(home, AiCategory.Automitrons).RunAdvisorChain();
            Assert.IsEmpty(Terraform(home));
        }

        [Test]
        public void Row53_AnImmuneAxisAlwaysCountsAsOffTheIdeal()
        {
            Race immune = new Race();
            immune.GravityTolerance.Immune = true;
            Star star = new Star
            {
                Gravity = 50,
                Temperature = immune.TemperatureTolerance.OptimumLevel,
                Radiation = immune.RadiationTolerance.OptimumLevel,
            };

            Assert.IsTrue(DefaultPlanetAI.AnyAxisOffIdeal(star, immune));
        }
    }
}
