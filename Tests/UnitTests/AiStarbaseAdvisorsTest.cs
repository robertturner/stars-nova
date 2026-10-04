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

    using NUnit.Framework;

    /// <summary>
    /// The AI's starbase production rules (docs/behavior-specs-10/ai-opponent-behavior.md): the
    /// starbase-building advisor `FUN_1090_510a` (§3), the starbase-upgrade advisor
    /// `FUN_1090_52da` (§6) on the ten-slot stand-in, and the planet-queue side of replacing a
    /// design (§7, §17).
    /// </summary>
    [TestFixture]
    public class AiStarbaseAdvisorsTest
    {
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> rolls;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = new Queue<int>(rolls);
            }

            public int Calls { get; private set; }

            public override int Next(int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
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
            star.MineralConcentration = new Resources(50, 50, 50, 0);
            clientState.EmpireState.OwnedStars.Add(star);

            StarIntel report = new StarIntel();
            report.Name = name;
            report.Position = position;
            report.Owner = clientState.EmpireState.Id;
            clientState.EmpireState.StarReports.Add(name, report);
            return star;
        }

        private ShipDesign MakeDesign(string name, ItemType type)
        {
            ShipDesign design = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            design.Name = name;
            design.Type = type;
            design.Blueprint = new Component();
            design.Blueprint.Name = type == ItemType.Starbase ? "Space Station" : "Medium Freighter";
            design.Blueprint.Cost = new Resources(10, 10, 10, 10);
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            clientState.EmpireState.Designs[design.Key] = design;
            return design;
        }

        /// <summary>Starbase designs filling slots 0 .. count - 1, in creation order; the names
        /// carry the given creation years (TurnYear units, 0 for a starting design).</summary>
        private List<ShipDesign> MakeStarbaseSlots(params int[] creationYears)
        {
            List<ShipDesign> designs = new List<ShipDesign>();
            for (int slot = 0; slot < creationYears.Length; slot++)
            {
                string name = creationYears[slot] == 0 ? "Base " + slot : "Base " + slot + " T" + creationYears[slot];
                designs.Add(MakeDesign(name, ItemType.Starbase));
            }

            return designs;
        }

        private void GiveStarbase(Star star, ShipDesign design)
        {
            Fleet starbase = new Fleet(star.Name + " Starbase", clientState.EmpireState.Id, nextFleetId++, star.Position);
            ShipToken token = new ShipToken(design, 1);
            starbase.Composition.Add(token.Key, token);
            starbase.Type = ItemType.Starbase;
            starbase.InOrbit = star;
            star.Starbase = starbase;
        }

        private DefaultPlanetAI PlanetAI(Star star, int category, Random random = null)
        {
            return new DefaultPlanetAI(star, clientState, new DefaultAIPlanner(clientState), random ?? new Random(1), category);
        }

        private List<ShipDesign> QueuedDesigns(Star star)
        {
            return star.ManufacturingQueue.Queue
                .Select(order => order.Unit)
                .OfType<ShipProductionUnit>()
                .Select(unit => clientState.EmpireState.Designs[unit.DesignKey])
                .ToList();
        }

        /// <summary>A colony that meets the §5 hub thresholds: over 79 units, 20 mines and 20
        /// factories, and minerals plus 4 × concentration² of at least 7,000.</summary>
        private Star AddHubColony(string name, NovaPoint position)
        {
            Star colony = AddOwnedStar(name, position, 10000);
            colony.Mines = 20;
            colony.Factories = 20;
            colony.ResourcesOnHand = new Resources(3000, 3000, 3000, 0);
            return colony;
        }

        // ================================================================ the slot stand-in

        [Test]
        public void Slots_FollowCreationOrder_AndStopAtTen()
        {
            List<ShipDesign> designs = MakeStarbaseSlots(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            MakeDesign("A ship", ItemType.Ship);

            StarbaseSlots slots = StarbaseSlots.ForEmpire(clientState.EmpireState);

            for (int slot = 0; slot < 10; slot++)
            {
                Assert.AreSame(designs[slot], slots[slot]);
            }

            Assert.AreEqual(StarbaseAdvisors.NoSlot, slots.SlotOf(designs[10]), "the eleventh starbase design is 'above index 9'");
            Assert.AreEqual(3, slots.SlotOf(designs[3]));
        }

        [Test]
        public void DefaultSlot_IsFiveOnlyWhenSlotFiveIsNewerThanSlotZero()
        {
            int start = Global.StartingYear;
            Assert.AreEqual(0, StarbaseSlots.ForEmpire(clientState.EmpireState).DefaultSlot(), "no designs at all");

            SetUp();
            MakeStarbaseSlots(0, 0, 0, 0, 0);
            Assert.AreEqual(0, StarbaseSlots.ForEmpire(clientState.EmpireState).DefaultSlot(), "slot 5 empty");

            SetUp();
            MakeStarbaseSlots(start + 10, 0, 0, 0, 0, start + 10);
            Assert.AreEqual(0, StarbaseSlots.ForEmpire(clientState.EmpireState).DefaultSlot(), "same year is not newer");

            SetUp();
            MakeStarbaseSlots(start + 10, 0, 0, 0, 0, start + 11);
            Assert.AreEqual(5, StarbaseSlots.ForEmpire(clientState.EmpireState).DefaultSlot());
        }

        [Test]
        public void HubBase_IsSixOnlyWhenSlotSixIsNewerThanSlotOne()
        {
            int start = Global.StartingYear;
            MakeStarbaseSlots(0, start + 5, 0, 0, 0, 0, start + 5);
            Assert.AreEqual(1, StarbaseSlots.ForEmpire(clientState.EmpireState).HubBase());

            SetUp();
            MakeStarbaseSlots(0, start + 5, 0, 0, 0, 0, start + 6);
            Assert.AreEqual(6, StarbaseSlots.ForEmpire(clientState.EmpireState).HubBase());
        }

        // ================================================================ §3 building advisor

        [Test]
        public void HubAdvisor_QueuesTheDefaultSlot_OnAHubWithoutAStarbase()
        {
            Assert.AreEqual(0, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Automitrons, true, false, 80, false, false, 0));
            Assert.AreEqual(5, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Robotoids, true, false, 80, false, false, 5));
            Assert.AreEqual(0, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Rototills, true, false, 80, false, false, 0));
        }

        [Test]
        public void HubAdvisor_Gates()
        {
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Automitrons, true, false, 79, false, false, 0), "population must be above 79 units");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Automitrons, false, false, 80, false, false, 0), "only a hub");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Automitrons, true, true, 80, false, false, 0), "already has a starbase in the slots");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Turindrones, true, false, 80, true, false, 0), "urgent-supply flag");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Automitrons, true, false, 80, false, true, 0), "a starbase already queued");
        }

        [Test]
        public void HubAdvisor_DoesNothingForCategoriesFourFiveAndSeven()
        {
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Cybertrons, true, false, 80, false, false, 0), "category 4 uses the planet test");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.HubStarbaseSlot(AiCategory.Macinti, true, false, 80, false, false, 0));
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.HubStarbaseSlot(AiCategory.EconomyOnly, true, false, 80, false, false, 0));
        }

        [Test]
        public void PlanetTest_PicksSlotFiveOrSixByConcentration_FallingBackToZeroOrOne()
        {
            int start = Global.StartingYear;
            MakeStarbaseSlots(0, 0, 0, 0, 0, start + 20, start + 20);
            StarbaseSlots slots = StarbaseSlots.ForEmpire(clientState.EmpireState);

            Assert.AreEqual(5, StarbaseAdvisors.PlanetTestSlot(false, 15, 500, true, false, slots));
            Assert.AreEqual(6, StarbaseAdvisors.PlanetTestSlot(false, 15, 500, false, false, slots));

            SetUp();
            MakeStarbaseSlots(start + 20, start + 20, 0, 0, 0, start + 20);
            slots = StarbaseSlots.ForEmpire(clientState.EmpireState);
            Assert.AreEqual(0, StarbaseAdvisors.PlanetTestSlot(false, 15, 500, true, false, slots), "slot 5 not newer than slot 0");
            Assert.AreEqual(1, StarbaseAdvisors.PlanetTestSlot(false, 15, 500, false, false, slots), "slot 6 empty");
        }

        [Test]
        public void PlanetTest_Gates()
        {
            StarbaseSlots slots = new StarbaseSlots(Enumerable.Empty<ShipDesign>());
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.PlanetTestSlot(false, 14, 500, true, false, slots), "habitability must exceed 14");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.PlanetTestSlot(false, 15, 499, true, false, slots), "population must exceed 499 units");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.PlanetTestSlot(true, 15, 500, true, false, slots), "no starbase yet");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.PlanetTestSlot(false, 15, 500, true, true, slots), "a starbase already queued");
        }

        [Test]
        public void BuildingAdvisor_QueuesTheStartingStarbase_OnAHubColony_FromTurnTwenty()
        {
            List<ShipDesign> designs = MakeStarbaseSlots(0);
            GiveStarbase(home, designs[0]);
            Star colony = AddHubColony("Colony", new NovaPoint(300, 100));

            clientState.EmpireState.TurnYear = Global.StartingYear + 19;
            PlanetAI(colony, AiCategory.Automitrons).RunAdvisorChain();
            Assert.IsEmpty(QueuedDesigns(colony), "no hubs before turn 20");

            clientState.EmpireState.TurnYear = Global.StartingYear + 20;
            PlanetAI(colony, AiCategory.Automitrons).RunAdvisorChain();
            CollectionAssert.AreEqual(new[] { designs[0] }, QueuedDesigns(colony));
            Assert.AreEqual(1, colony.ManufacturingQueue.Queue.Single(order => order.Unit is ShipProductionUnit).Quantity);

            PlanetAI(colony, AiCategory.Automitrons).RunAdvisorChain();
            Assert.AreEqual(1, QueuedDesigns(colony).Count, "not again while one is queued");
        }

        [Test]
        public void BuildingAdvisor_SkipsAColonyThatIsNotAHub()
        {
            List<ShipDesign> designs = MakeStarbaseSlots(0);
            GiveStarbase(home, designs[0]);
            Star colony = AddHubColony("Colony", new NovaPoint(300, 100));
            colony.Mines = 19;
            clientState.EmpireState.TurnYear = Global.StartingYear + 30;

            PlanetAI(colony, AiCategory.Automitrons).RunAdvisorChain();

            Assert.IsEmpty(QueuedDesigns(colony));
        }

        [Test]
        public void BuildingAdvisor_CategoryZeroSkipsAHubWithinFiftyLightYearsOfAnother()
        {
            List<ShipDesign> designs = MakeStarbaseSlots(0);
            GiveStarbase(home, designs[0]);
            Star near = AddHubColony("Near", new NovaPoint(140, 100));
            Star far = AddHubColony("Far", new NovaPoint(300, 100));
            clientState.EmpireState.TurnYear = Global.StartingYear + 30;

            PlanetAI(near, AiCategory.Robotoids).RunAdvisorChain();
            PlanetAI(far, AiCategory.Robotoids).RunAdvisorChain();

            Assert.IsEmpty(QueuedDesigns(near), "40 ly from the home starbase hub: not a category-0 hub");
            Assert.AreEqual(1, QueuedDesigns(far).Count);

            PlanetAI(near, AiCategory.Automitrons).RunAdvisorChain();
            Assert.AreEqual(1, QueuedDesigns(near).Count, "the 50 ly exclusion is category 0's only");
        }

        [Test]
        public void BuildingAdvisor_CategoryFour_UsesThePlanetTest_WithoutAHub()
        {
            int start = Global.StartingYear;
            List<ShipDesign> designs = MakeStarbaseSlots(0, 0, 0, 0, 0, start + 1);
            Star colony = AddOwnedStar("Colony", new NovaPoint(300, 100), 50000);
            Assume.That(race.HabPercent(colony), Is.GreaterThan(14));

            PlanetAI(colony, AiCategory.Cybertrons).RunStarbaseBuilding();

            CollectionAssert.AreEqual(new[] { designs[5] }, QueuedDesigns(colony), "all concentrations above 15: slot 5");

            Star poor = AddOwnedStar("Poor", new NovaPoint(500, 100), 50000);
            poor.MineralConcentration = new Resources(50, 15, 50, 0);
            PlanetAI(poor, AiCategory.Cybertrons).RunStarbaseBuilding();
            CollectionAssert.AreEqual(new[] { designs[1] }, QueuedDesigns(poor), "slot 6 is empty, so slot 1");
        }

        [Test]
        public void BuildingAdvisor_DoesNothingForCategoryFive()
        {
            List<ShipDesign> designs = MakeStarbaseSlots(0);
            GiveStarbase(home, designs[0]);
            Star colony = AddHubColony("Colony", new NovaPoint(300, 100));
            clientState.EmpireState.TurnYear = Global.StartingYear + 30;

            PlanetAI(colony, AiCategory.Macinti).RunStarbaseBuilding();

            Assert.IsEmpty(QueuedDesigns(colony));
        }

        // ================================================================ §6 upgrade advisor

        [Test]
        public void OtherGenerationChance_MatchesTheSpecCurve()
        {
            Assert.AreEqual(5, StarbaseAdvisors.OtherGenerationChance(0));
            Assert.AreEqual(5, StarbaseAdvisors.OtherGenerationChance(9));
            Assert.AreEqual(5, StarbaseAdvisors.OtherGenerationChance(11), "a = 1, halved to 0");
            Assert.AreEqual(6, StarbaseAdvisors.OtherGenerationChance(12));
            Assert.AreEqual(29, StarbaseAdvisors.OtherGenerationChance(59));
            Assert.AreEqual(55, StarbaseAdvisors.OtherGenerationChance(60), "from 50 on a is used as is");
        }

        private StarbaseSlots FullSlots(params int[] creationYears)
        {
            MakeStarbaseSlots(creationYears);
            return StarbaseSlots.ForEmpire(clientState.EmpireState);
        }

        [Test]
        public void Upgrade_SameGeneration_ClimbsTwoRungs_OnASixPercentRoll()
        {
            StarbaseSlots slots = FullSlots(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            Assert.AreEqual(2, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 0, false, slots, 0, 0, true, new ScriptedRandom(5)));
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 0, false, slots, 0, 0, true, new ScriptedRandom(6)), "roll 6 is not below 6");
            Assert.AreEqual(4, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 2, false, slots, 0, 0, true, new ScriptedRandom(0)));
            Assert.AreEqual(3, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 1, false, slots, 0, 0, true, new ScriptedRandom(0)), "hub ladder 1 -> 3");
        }

        [Test]
        public void Upgrade_SameGeneration_NeedsTwoHundredOfEachMineral_AndALiveTarget()
        {
            StarbaseSlots slots = FullSlots(0, 0, 0);

            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 0, false, slots, 0, 0, false, new ScriptedRandom(0)));

            ScriptedRandom random = new ScriptedRandom(0);
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 1, false, slots, 0, 0, true, random), "slot 3 is empty");
        }

        [Test]
        public void Upgrade_TopRung_DoesNothing()
        {
            StarbaseSlots slots = FullSlots(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            ScriptedRandom random = new ScriptedRandom(0, 0);

            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 4, false, slots, 0, 0, true, random));
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 3, false, slots, 0, 0, true, random));
            Assert.AreEqual(0, random.Calls, "no roll at the top rung");
        }

        [Test]
        public void Upgrade_OtherGeneration_MovesToTheSameRungOfTheCurrentGeneration()
        {
            // D = 0, H = 1 (slots 5 and 6 are not newer): starbases in slots 5-9 belong to the
            // other generation.
            StarbaseSlots slots = FullSlots(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            Assert.AreEqual(2, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 7, false, slots, 30, 0, false, new ScriptedRandom(14)), "age 30: a = 10, chance 15; minerals not checked");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 7, false, slots, 30, 0, false, new ScriptedRandom(15)));
            Assert.AreEqual(1, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 6, false, slots, 0, 0, false, new ScriptedRandom(4)), "hub slot 6 -> (6 mod 5) + H - 1 = 1");
        }

        [Test]
        public void Upgrade_OtherGeneration_FromTheNewGeneration_IntoSlotsFiveAndUp()
        {
            int start = Global.StartingYear;
            StarbaseSlots slots = FullSlots(start, start, 0, 0, 0, start + 1, start + 1, 0, 0, 0);
            Assume.That(slots.DefaultSlot(), Is.EqualTo(5));
            Assume.That(slots.HubBase(), Is.EqualTo(6));

            Assert.AreEqual(7, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 2, false, slots, 0, 0, true, new ScriptedRandom(0)));
            Assert.AreEqual(8, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 3, false, slots, 0, 0, true, new ScriptedRandom(0)), "hub 3 -> 3 + 6 - 1");
        }

        [Test]
        public void Upgrade_Gates()
        {
            StarbaseSlots slots = FullSlots(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, 0, true, slots, 0, 0, true, new ScriptedRandom(0)), "a starbase already queued");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Automitrons, 50, StarbaseAdvisors.NoSlot, false, slots, 0, 0, true, new ScriptedRandom(0)), "no starbase in the slots");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Cybertrons, 39, 0, false, slots, 0, 0, true, new ScriptedRandom(0)), "category 4 before year 40");
            Assert.AreEqual(2, StarbaseAdvisors.UpgradeSlot(AiCategory.Cybertrons, 40, 0, false, slots, 0, 0, true, new ScriptedRandom(0)));
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.EconomyOnly, 50, 0, false, slots, 0, 0, true, new ScriptedRandom(0)), "category 7 passes no slot");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.UpgradeSlot(AiCategory.Macinti, 50, 0, false, slots, 0, 0, true, new ScriptedRandom(0)), "category 5 has its own branch (MacintiUpgradeSlot)");
        }

        [Test]
        public void UpgradeAdvisor_InTheChain_QueuesTheNextRung_AndEndsTheChain()
        {
            List<ShipDesign> designs = MakeStarbaseSlots(0, 0, 0);
            GiveStarbase(home, designs[0]);
            home.Colonists = 200000; // the Defenses advisor would also fire
            clientState.EmpireState.TurnYear = Global.StartingYear + 10;

            PlanetAI(home, AiCategory.Automitrons, new ScriptedRandom(0)).RunAdvisorChain();

            CollectionAssert.AreEqual(new[] { designs[2] }, QueuedDesigns(home));
            Assert.AreEqual(1, home.ManufacturingQueue.Queue.Count, "the first advisor that queues ends the chain");
        }

        [Test]
        public void UpgradeAdvisor_FailedRoll_LetsTheDefensesAdvisorRun()
        {
            List<ShipDesign> designs = MakeStarbaseSlots(0, 0, 0);
            GiveStarbase(home, designs[0]);
            home.Colonists = 200000;
            clientState.EmpireState.TurnYear = Global.StartingYear + 10;

            PlanetAI(home, AiCategory.Automitrons, new ScriptedRandom(50)).RunAdvisorChain();

            Assert.IsEmpty(QueuedDesigns(home));
            Assert.IsInstanceOf<DefenseProductionUnit>(home.ManufacturingQueue.Queue.Single().Unit);
        }

        // ================================================================ §7/§17 replacement

        [Test]
        public void AQueuedOrderForASupersededDesign_IsSwitchedToItsSuccessor()
        {
            ShipDesign scout = MakeDesign("Scout", ItemType.Ship);
            ShipDesign oldTransport = MakeDesign("AI Transport T2101", ItemType.Ship);
            ShipDesign newTransport = MakeDesign("AI Transport T2140", ItemType.Ship);

            home.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(scout), false));
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(3, new ShipProductionUnit(oldTransport), false));
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new DefenseProductionUnit(race), false));

            PlanetAI(home, AiCategory.Automitrons).ReplaceSupersededOrders(newTransport);

            List<ProductionOrder> queue = home.ManufacturingQueue.Queue;
            Assert.AreEqual(3, queue.Count);
            Assert.AreEqual(scout.Key, ((ShipProductionUnit)queue[0].Unit).DesignKey, "another role is untouched");
            Assert.AreEqual(newTransport.Key, ((ShipProductionUnit)queue[1].Unit).DesignKey, "replaced in place");
            Assert.AreEqual(3, queue[1].Quantity);
            Assert.IsInstanceOf<DefenseProductionUnit>(queue[2].Unit);
        }

        [Test]
        public void AnOlderDesign_NeverReplacesANewerOne()
        {
            ShipDesign oldTransport = MakeDesign("AI Transport T2101", ItemType.Ship);
            ShipDesign newTransport = MakeDesign("AI Transport T2140", ItemType.Ship);
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new ShipProductionUnit(newTransport), false));

            PlanetAI(home, AiCategory.Automitrons).ReplaceSupersededOrders(oldTransport);

            Assert.AreEqual(newTransport.Key, ((ShipProductionUnit)home.ManufacturingQueue.Queue.Single().Unit).DesignKey);
        }

        [Test]
        public void RoleTags_DecideTheRole_WhateverTheHull()
        {
            ShipDesign privateer = MakeDesign("Privateer [freighter11] T2101", ItemType.Ship);
            ShipDesign metaMorph = MakeDesign("Meta Morph [freighter11] T2140", ItemType.Ship);
            ShipDesign otherRole = MakeDesign("Meta Morph [freighter12] T2141", ItemType.Ship);

            Assert.IsTrue(AiDesignRoles.Supersedes(metaMorph, privateer));
            Assert.IsFalse(AiDesignRoles.Supersedes(otherRole, privateer), "another tag is another role");
            Assert.IsFalse(AiDesignRoles.Supersedes(privateer, metaMorph), "never backwards");
        }

        [Test]
        public void CreationYear_ReadsTheTurnSuffix_ElseTheStartingYear()
        {
            Assert.AreEqual(2142, AiDesignRoles.CreationYear(new ShipDesign(1) { Name = "AI Transport T2142" }));
            Assert.AreEqual(Global.StartingYear, AiDesignRoles.CreationYear(new ShipDesign(1) { Name = "Starbase" }));
        }
    }
}
