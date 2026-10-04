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
    /// The AI integration follow-ups of docs/behavior-specs-10/ai-opponent-behavior.md: the
    /// fallback transport design is not re-added every turn; detected wormholes reach the §12
    /// colony-ship wormhole diversion; the planet pass builds the §17 role designs the design
    /// builder made (scout, colonizer, transport, with personality 5's colony phase); and the
    /// §14 slot-0 scout / slot-1 colonizer are created when an empire lacks them.
    /// </summary>
    [TestFixture]
    public class AiIntegrationTest
    {
        private static readonly AllComponents Components = new AllComponents();

        private class ZeroRandom : Random
        {
            public override int Next(int maxValue)
            {
                return 0;
            }

            public override int Next(int minValue, int maxValue)
            {
                return minValue;
            }

            public override double NextDouble()
            {
                return 0;
            }
        }

        private class TestableAi : DefaultAi
        {
            public TestableAi(ClientData state, int category, Random random = null)
            {
                clientState = state;
                commandArguments = new CommandArguments();
                commandArguments.Add(CommandArguments.Option.AiPersonality, category + DefaultAi.MinStandardPersonality);
                AiRandom = random ?? new Random(11);
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

            home = new Star();
            home.Name = "Home";
            home.Owner = 1;
            home.Position = new NovaPoint(100, 100);
            home.ThisRace = race;
            home.Colonists = 300000;
            home.Gravity = home.OriginalGravity = race.GravityTolerance.OptimumLevel;
            home.Temperature = home.OriginalTemperature = race.TemperatureTolerance.OptimumLevel;
            home.Radiation = home.OriginalRadiation = race.RadiationTolerance.OptimumLevel;
            home.ResourcesOnHand = new Resources(1000, 1000, 1000, 0);
            home.Starbase = new Fleet("Home base", 1, 9000, home.Position);
            clientState.EmpireState.OwnedStars.Add(home);
            clientState.EmpireState.StarReports.Add("Home", new StarIntel { Name = "Home", Position = home.Position, Owner = 1 });
        }

        private void MakeEverythingAvailable(Func<Component, bool> except = null)
        {
            foreach (Component component in Components.GetAll.Values)
            {
                if (except == null || !except(component))
                {
                    clientState.EmpireState.AvailableComponents[component.Name] = component;
                }
            }
        }

        private ShipDesign Add(ShipDesign design)
        {
            clientState.EmpireState.Designs[design.Key] = design;
            return design;
        }

        private ShipDesign Build(string hull, string template, string name, Dictionary<string, Component> available = null)
        {
            Dictionary<string, Component> parts = available ?? Components.GetAll.ToDictionary(pair => pair.Key, pair => pair.Value);
            return new DesignBuilder(parts, new Random(1)).Build(hull, DesignBuilder.ParseTemplate(template), clientState.EmpireState.GetNextDesignKey(), name);
        }

        private int TransportDesignCount()
        {
            return clientState.EmpireState.Designs.Values.Count(design => AiDesignRoles.BaseName(design) == DefaultAIPlanner.TransportBaseName);
        }

        private void AddFleet(ShipDesign design, int ships)
        {
            Fleet fleet = new Fleet(design.Name + " fleet", 1, nextFleetId++, home.Position);
            ShipToken token = new ShipToken(design, ships);
            fleet.Composition.Add(token.Key, token);
            fleet.InOrbit = home;
            fleet.Waypoints.Add(new Waypoint { Position = home.Position, Destination = home.Name });
            clientState.EmpireState.OwnedFleets.Add(fleet);
        }

        // ================================================================ (1) fallback transport

        /// <summary>The planner is rebuilt every turn, so its cached design was always null and
        /// a new "AI Transport T&lt;year&gt;" was added on every turn once tech passed the gate.</summary>
        [Test]
        public void TheFallbackTransport_IsAddedOnce_NotEveryTurn()
        {
            MakeEverythingAvailable();
            clientState.EmpireState.ResearchLevels = new TechLevel(26);

            ShipDesign first = new DefaultAIPlanner(clientState, AiCategory.Rototills).TransportDesign;
            Assert.IsNotNull(first);
            Assert.AreEqual(1, TransportDesignCount());

            for (int turn = 0; turn < 5; turn++)
            {
                clientState.EmpireState.TurnYear++;
                Assert.AreSame(first, new DefaultAIPlanner(clientState, AiCategory.Rototills).TransportDesign, "turn " + turn);
            }

            Assert.AreEqual(1, TransportDesignCount());

            // Due for refresh after 35 years, but the same parts would build the same design.
            clientState.EmpireState.TurnYear += ShipDesignRefresher.ModerateAgeThreshold;
            Assert.AreSame(first, new DefaultAIPlanner(clientState, AiCategory.Rototills).TransportDesign);
            Assert.AreEqual(1, TransportDesignCount(), "an identical design is never added again");
        }

        [Test]
        public void TheFallbackTransport_IsReplacedOnce_WhenARefreshBuildsSomethingNew()
        {
            MakeEverythingAvailable();
            clientState.EmpireState.ResearchLevels = new TechLevel(26);
            Component best = ShipDesignRefresher.BestAvailableEngine(clientState);
            clientState.EmpireState.AvailableComponents.Remove(best.Name);
            ShipDesign first = new DefaultAIPlanner(clientState, AiCategory.Rototills).TransportDesign;

            clientState.EmpireState.AvailableComponents[best.Name] = best;
            clientState.EmpireState.TurnYear += ShipDesignRefresher.ModerateAgeThreshold;
            ShipDesign refreshed = new DefaultAIPlanner(clientState, AiCategory.Rototills).TransportDesign;
            Assert.AreNotSame(first, refreshed);
            Assert.AreEqual(2, TransportDesignCount());

            clientState.EmpireState.TurnYear++;
            Assert.AreSame(refreshed, new DefaultAIPlanner(clientState, AiCategory.Rototills).TransportDesign);
            Assert.AreEqual(2, TransportDesignCount());
        }

        [Test]
        public void SeveralWholeTurns_KeepTheDesignCountStable()
        {
            MakeEverythingAvailable();
            clientState.EmpireState.ResearchLevels = new TechLevel(26);

            new TestableAi(clientState, AiCategory.Rototills).DoMove();
            int designs = clientState.EmpireState.Designs.Count;

            // §12 personality 3: its planet pass builds only slot-0 ships (year 0) and colony
            // ships, so no transport is asked for and none is designed.
            Assert.AreEqual(0, TransportDesignCount());
            Assert.IsFalse(home.ManufacturingQueue.Queue.Any(order => order.Unit is ShipProductionUnit unit
                && clientState.EmpireState.Designs[unit.DesignKey].Name.StartsWith(DefaultAIPlanner.TransportBaseName)));

            for (int turn = 0; turn < 5; turn++)
            {
                clientState.EmpireState.TurnYear++;
                new TestableAi(clientState, AiCategory.Rototills).DoMove();
                Assert.AreEqual(designs, clientState.EmpireState.Designs.Count, "turn " + turn);
            }
        }

        // ================================================================ (2) wormhole intel

        [Test]
        public void Sightings_AreTheEmpiresWormholeReports_WithTheirUsedBitAndTier()
        {
            clientState.EmpireState.WormholeReports.Add(7, new WormholeIntel(new Wormhole { Key = 7, PairedKey = 8, Position = new NovaPoint(130, 100) }, 2101));
            clientState.EmpireState.WormholeReports.Add(9, new WormholeIntel(new Wormhole { Key = 9, PairedKey = 10, Position = new NovaPoint(50, 60), StabilityTier = 3 }, 2101) { UsedByUs = true });

            WormholeSighting sighting = WormholeDiversion.Sightings(clientState.EmpireState).Single(s => s.Key == 7);

            Assert.AreEqual(130, sighting.Position.X);
            Assert.AreEqual(100, sighting.Position.Y);
            Assert.IsFalse(sighting.Known, "a report without this race's used bit is not 'known'");
            Assert.AreEqual(0, sighting.StabilityTier);
            Assert.IsFalse(string.IsNullOrEmpty(sighting.Name), "a waypoint needs a destination name");

            WormholeSighting used = WormholeDiversion.Sightings(clientState.EmpireState).Single(s => s.Key == 9);
            Assert.IsTrue(used.Known, "WormholeIntel.UsedByUs is the per-race bit");
            Assert.AreEqual(3, used.StabilityTier, "the tier the scan step recorded");
            Assert.AreEqual(40, WormholeDiversion.Score(used, 1, 1), "known: 70 - 10 x tier 3");

            Assert.IsEmpty(WormholeDiversion.Sightings(new EmpireData()));
        }

        /// <summary>DefaultAi.KnownWormholes used to return nothing; a detected wormhole now
        /// diverts a Robotoid colony fleet (§12 personality 0 step 3).</summary>
        [Test]
        public void ADetectedWormhole_DivertsARobotoidColonyFleet()
        {
            clientState.EmpireState.StarReports.Add("Target", new StarIntel
            {
                Name = "Target",
                Position = new NovaPoint(160, 100),
                Owner = Global.Nobody,
                Year = Global.StartingYear,
                Gravity = race.GravityTolerance.OptimumLevel,
                Temperature = race.TemperatureTolerance.OptimumLevel,
                Radiation = race.RadiationTolerance.OptimumLevel,
            });
            clientState.EmpireState.WormholeReports.Add(7, new WormholeIntel(new Wormhole { Key = 7, PairedKey = 8, Position = new NovaPoint(130, 100) }, 2101));

            ShipDesign colony = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            colony.Name = "Colony";
            colony.Blueprint = new Component { Name = "Colony Ship" };
            Hull hull = new Hull { BaseCargo = 25, FuelCapacity = 100, Modules = new List<HullModule>() };
            Component engineComponent = new Component { Name = "Long Hump 6" };
            engineComponent.Properties.Add("Engine", new Engine { FuelConsumption = new[] { 0, 0, 0, 0, 35, 120, 175, 235, 360, 420 } });
            hull.Modules.Add(new HullModule { ComponentType = "Engine", AllocatedComponent = engineComponent, ComponentCount = 1 });
            colony.Blueprint.Properties.Add("Hull", hull);
            colony.Blueprint.Properties.Add("Colonizer", new DoubleProperty(1.0));
            colony.Update();
            Add(colony);

            Fleet fleet = new Fleet("Colony fleet", 1, nextFleetId++, home.Position);
            ShipToken token = new ShipToken(colony, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.InOrbit = home;
            fleet.FuelAvailable = fleet.TotalFuelCapacity;
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(home.Position), Destination = home.Name });
            clientState.EmpireState.OwnedFleets.Add(fleet);

            // An unused wormhole no farther than the planet scores 90; every roll is 0.
            new TestableAi(clientState, AiCategory.Robotoids, new ZeroRandom()).DoMove();

            Waypoint last = fleet.Waypoints.Last();
            Assert.AreEqual(WormholeDiversion.Sightings(clientState.EmpireState).Single().Name, last.Destination);
            Assert.AreEqual(130, last.Position.X);
            Assert.IsInstanceOf<NoTask>(last.Task, "a wormhole leg has no task");
        }

        // ================================================================ (3) role designs are built

        [Test]
        public void ThePlanetPass_BuildsTheRoleDesigns_OfTheDesignBuilder()
        {
            MakeEverythingAvailable();
            clientState.EmpireState.ResearchLevels = new TechLevel(26);

            new TestableAi(clientState, AiCategory.Automitrons).DoMove();

            Dictionary<string, ShipDesign> current = AiDesignPlanner.CurrentDesigns(AiCategory.Automitrons, clientState.EmpireState.Designs.Values);
            DefaultAIPlanner planner = new DefaultAIPlanner(clientState, AiCategory.Automitrons);
            Assert.AreEqual("explorer", AiDesignRoleTag.TagOf(planner.ScoutDesign));
            Assert.AreSame(current["explorer"], planner.ScoutDesign);
            Assert.AreEqual("colonizer", AiDesignRoleTag.TagOf(planner.ColonizerDesign));
            Assert.AreEqual("Medium Freighter", planner.ColonizerDesign.Blueprint.Name, "personality 2's slot-1 colonizer");
            CollectionAssert.Contains(new[] { "freighter-a", "freighter-b" }, AiDesignRoleTag.TagOf(planner.TransportDesign));
            Assert.AreEqual(0, TransportDesignCount(), "no fallback transport while a freighter role has a design");

            // §12 personality 2's planet pass queues only freighters, colony ships, minelayers,
            // bombers and slot-9/10 (garrison) ships; it never queues its slot-0 explorer, and
            // in year 1 (no hubs, one planet) its freighter quota is 0.
            List<ShipDesign> queued = home.ManufacturingQueue.Queue
                .Select(order => order.Unit).OfType<ShipProductionUnit>()
                .Select(unit => clientState.EmpireState.Designs[unit.DesignKey])
                .ToList();
            Assert.IsFalse(queued.Contains(current["explorer"]), "the explorer role design is never queued");
            Assert.IsFalse(queued.Contains(planner.TransportDesign), "freighter quota max(1 / 10, 2 x 0 hubs) = 0");
            foreach (ShipDesign design in queued)
            {
                CollectionAssert.Contains(new[] { "minelayer", "bomber-a", "bomber-b", "garrison" }, AiDesignRoleTag.TagOf(design));
            }
        }

        [Test]
        public void WithoutARoleDesign_ThePlannerFallsBackToTheNameSearch()
        {
            ShipDesign scout = Add(Build("Scout", "30.26.26", "Scout"));
            ShipDesign colony = Add(Build("Colony Ship", "8.31", "Santa Maria"));

            // Personality 0's slot 0 is a minelayer, not a scout: the name search still decides.
            DefaultAIPlanner planner = new DefaultAIPlanner(clientState, AiCategory.Robotoids);
            Assert.AreSame(scout, planner.ScoutDesign);
            Assert.AreSame(colony, planner.ColonizerDesign, "the starting colonizer is the slot-1 role's design");
            Assert.IsNull(planner.TransportDesign, "tech below the fallback transport's gate");
        }

        [Test]
        public void Personality5_BuildsColonyShipsFromSlot7_DuringItsColonyPhase()
        {
            Dictionary<string, Component> noScoop = Components.GetAll.ToDictionary(pair => pair.Key, pair => pair.Value);
            noScoop.Remove("Galaxy Scoop");
            ShipDesign slotOne = Add(Build("Colony Ship", "8.40", "Pinta", noScoop));
            ShipDesign slotSeven = Add(Build("Colony Ship", "8.40", AiDesignRoleTag.Name("Colony Ship", "colonizer-early", Global.StartingYear + 1), noScoop));
            AddFleet(slotSeven, 1);
            MakeEverythingAvailable(component => component.Name == "Galaxy Scoop");

            Assert.AreSame(slotSeven, new DefaultAIPlanner(clientState, AiCategory.Macinti).ColonizerDesign, "slot 1 has no Galaxy Scoop");
            Assert.AreSame(slotOne, new DefaultAIPlanner(clientState, AiCategory.Automitrons).ColonizerDesign, "other personalities have no slot-7 colonizer");

            MakeEverythingAvailable();
            Assert.AreSame(slotSeven, new DefaultAIPlanner(clientState, AiCategory.Macinti).ColonizerDesign, "a slot-7 ship still exists");

            clientState.EmpireState.OwnedFleets.Clear();
            Assert.AreSame(slotOne, new DefaultAIPlanner(clientState, AiCategory.Macinti).ColonizerDesign, "the phase ends: Galaxy Scoop available, no slot-7 ship");

            ShipDesign scooped = Add(Build("Colony Ship", "8.40", AiDesignRoleTag.Name("Colony Ship", "colonizer", Global.StartingYear + 50)));
            AddFleet(slotSeven, 1);
            Assert.AreSame(scooped, new DefaultAIPlanner(clientState, AiCategory.Macinti).ColonizerDesign, "slot 1 with the Galaxy Scoop always wins");
        }

        // ================================================================ (4) starting designs

        [Test]
        public void BuildMissing_CreatesTheSlot0ScoutAndSlot1Colonizer_OnlyWhenLacking()
        {
            MakeEverythingAvailable();
            List<ShipDesign> built = AiStartingDesigns.BuildMissing(
                clientState.EmpireState.Designs.Values, clientState.EmpireState.AvailableComponents, AiCategory.Robotoids, AiRaceTemplates.Standard, 0, clientState.EmpireState.GetNextDesignKey);

            CollectionAssert.AreEqual(new[] { "Smaugarian Peeping Tom", "Spore Cloud" }, built.Select(design => design.Name).ToArray());
            Assert.AreEqual(AiStartingRole.Scout, AiStartingDesigns.Classify(built[0]));
            Assert.AreEqual(AiStartingRole.Colonizer, AiStartingDesigns.Classify(built[1]));
            Assert.AreNotEqual(built[0].Key, built[1].Key);

            foreach (ShipDesign design in built)
            {
                Add(design);
            }

            Assert.IsEmpty(AiStartingDesigns.BuildMissing(
                clientState.EmpireState.Designs.Values, clientState.EmpireState.AvailableComponents, AiCategory.Robotoids, AiRaceTemplates.Standard, 0, clientState.EmpireState.GetNextDesignKey));
        }

        [Test]
        public void BuildMissing_LeavesNovasOwnStartingDesignsAlone()
        {
            MakeEverythingAvailable();
            Add(Build("Scout", "30.26.26", "Scout"));
            Add(Build("Colony Ship", "8.31", "Santa Maria"));

            Assert.IsEmpty(AiStartingDesigns.BuildMissing(
                clientState.EmpireState.Designs.Values, clientState.EmpireState.AvailableComponents, AiCategory.Cybertrons, AiRaceTemplates.Standard, 0, clientState.EmpireState.GetNextDesignKey));

            clientState.EmpireState.Designs.Clear();
            Add(Build("Colony Ship", "8.31", "Santa Maria"));
            ShipDesign scout = AiStartingDesigns.BuildMissing(
                clientState.EmpireState.Designs.Values, clientState.EmpireState.AvailableComponents, AiCategory.Cybertrons, AiRaceTemplates.Standard, 0, clientState.EmpireState.GetNextDesignKey).Single();
            Assert.AreEqual("Long Range Scout", scout.Name, "only the missing scout is created");
        }

        [Test]
        public void TheAiTurn_CreatesItsStartingDesignsThroughDesignCommands_Once()
        {
            MakeEverythingAvailable();

            new TestableAi(clientState, AiCategory.Rototills).DoMove();

            Dictionary<AiStartingRole, ShipDesign> roles = AiStartingDesigns.AssignRoles(clientState.EmpireState.Designs.Values);
            Assert.AreEqual("Smaugarian Peeping Tom", roles[AiStartingRole.Scout].Name);
            Assert.AreEqual("Santa Maria", roles[AiStartingRole.Colonizer].Name);
            Assert.AreEqual(2, clientState.Commands.OfType<Nova.Common.Commands.DesignCommand>().Count(), "one DesignCommand each");

            int designs = clientState.EmpireState.Designs.Count;
            new TestableAi(clientState, AiCategory.Rototills).DoMove();
            Assert.AreEqual(designs, clientState.EmpireState.Designs.Count, "no duplicates on the next turn");
        }
    }
}
