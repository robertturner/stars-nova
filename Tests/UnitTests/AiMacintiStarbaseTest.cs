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
    /// Category 5's branch of the starbase-upgrade advisor (docs/behavior-specs-10/
    /// ai-opponent-behavior.md §6 "Category 5"): the urgency marks of `FUN_1090_492e` and the
    /// upgrade rules of `FUN_1090_52da` (`:65246`-`65288`).
    /// </summary>
    [TestFixture]
    public class AiMacintiStarbaseTest
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

        private const int Fort = 0;
        private const int Dock = 1;
        private const int Station = 2;
        private const int Ultra = 3;
        private const int DeathStar = 4;

        private static bool[] Live(params int[] slots)
        {
            bool[] live = new bool[10];
            foreach (int slot in slots)
            {
                live[slot] = true;
            }

            return live;
        }

        private static int[] Marks(int year, bool[] live, int[] ages, bool[] inUse, int[] chassis = null)
        {
            return StarbaseAdvisors.MacintiMarks(year, live, ages, inUse, chassis ?? new[] { Fort, Dock, Station, Ultra, DeathStar, DeathStar, DeathStar, DeathStar, DeathStar, DeathStar });
        }

        // ================================================================ marks

        [Test]
        public void SlotsOneToThree_MarkedByAge_OnlyTheMostUrgentKeepsAboveOne()
        {
            bool[] live = Live(0, 1, 2, 3);
            bool[] inUse = Live(0, 1, 2, 3);

            int[] marks = Marks(10, live, new[] { 99, 34, 35, 10, 0, 0, 0, 0, 0, 0 }, inUse);
            Assert.AreEqual(0, marks[0], "slot 0 is never marked");
            Assert.AreEqual(0, marks[1], "34 years: 0");
            Assert.AreEqual(2, marks[2], "35-49 years: 2");
            Assert.AreEqual(0, marks[3]);

            marks = Marks(10, live, new[] { 0, 50, 40, 10, 0, 0, 0, 0, 0, 0 }, inUse);
            Assert.AreEqual(3, marks[1], "50 years or more: 3");
            Assert.AreEqual(0, marks[2], "only the most urgent of slots 1-3 keeps a mark above 1");
        }

        [Test]
        public void SlotsOneToThree_EmptyIsOne_UnusedIsZero()
        {
            int[] marks = Marks(10, Live(0, 2), new[] { 0, 0, 60, 0, 0, 0, 0, 0, 0, 0 }, Live(0));
            Assert.AreEqual(1, marks[1], "empty: no design could be made");
            Assert.AreEqual(0, marks[2], "a design no starbase uses is rebuilt (mark 0), whatever its age");
            Assert.AreEqual(1, marks[3]);
        }

        [Test]
        public void SlotOne_FromYearTwentySix_IsThreeUnlessASpaceDock()
        {
            int[] chassis = { Fort, Station, Station, Ultra, DeathStar, DeathStar, DeathStar, DeathStar, DeathStar, DeathStar };
            Assert.AreEqual(0, Marks(25, Live(0, 1), new int[10], Live(0, 1), chassis)[1]);
            Assert.AreEqual(3, Marks(26, Live(0, 1), new int[10], Live(0, 1), chassis)[1], "the AR home world's Space Station is pushed out of slot 1");

            chassis[1] = Dock;
            Assert.AreEqual(0, Marks(26, Live(0, 1), new int[10], Live(0, 1), chassis)[1]);
        }

        [Test]
        public void Groups_TheOlderGroupIsMarkedAsAWhole()
        {
            bool[] live = Live(0, 1, 2, 3, 4, 5, 6, 7, 8);
            int[] ages = { 0, 0, 0, 0, 29, 29, 29, 5, 5, 0 };
            int[] marks = Marks(50, live, ages, Live(0));
            CollectionAssert.AreEqual(new[] { 2, 2, 2 }, marks.Skip(4).Take(3).ToArray(), "older base under 30 years: 2");
            CollectionAssert.AreEqual(new[] { 0, 0, 1 }, marks.Skip(7).Take(3).ToArray(), "the newer group: 0, or 1 when empty");

            ages[4] = 30;
            marks = Marks(50, live, ages, Live(0));
            CollectionAssert.AreEqual(new[] { 3, 3, 3 }, marks.Skip(4).Take(3).ToArray(), "from 30 years: 3");

            ages = new[] { 0, 0, 0, 0, 5, 5, 5, 40, 40, 0 };
            marks = Marks(50, live, ages, Live(0));
            CollectionAssert.AreEqual(new[] { 3, 3, 3 }, marks.Skip(7).Take(3).ToArray(), "the 7-9 group can be the older one");
        }

        [Test]
        public void SlotsTwoAndThree_RaisedByTheNewerGroupsChassis()
        {
            bool[] live = Live(0, 1, 2, 3, 4, 7);
            int[] ages = { 0, 0, 0, 0, 40, 0, 0, 5, 0, 0 };
            int[] chassis = { Fort, Dock, Station, Ultra, DeathStar, DeathStar, DeathStar, Ultra, Ultra, Ultra };

            int[] marks = Marks(50, live, ages, Live(0), chassis);
            Assert.AreEqual(2, marks[3], "newer base below Death Star: slot 3 is 2");
            Assert.AreEqual(0, marks[2], "not below Ultra Station");

            chassis[7] = Station;
            marks = Marks(50, live, ages, Live(0), chassis);
            Assert.AreEqual(2, marks[3]);
            Assert.AreEqual(2, marks[2], "below Ultra Station: slot 2 too");

            chassis[7] = DeathStar;
            marks = Marks(50, live, ages, Live(0), chassis);
            Assert.AreEqual(0, marks[2]);
            Assert.AreEqual(0, marks[3]);
        }

        // ================================================================ upgrade rules

        [Test]
        public void UpgradeTarget_FirstHealthySlotAbove_ElseNine_OrThreeAcross()
        {
            int[] marks = { 0, 1, 2, 0, 0, 0, 0, 0, 0, 0 };
            Assert.AreEqual(3, StarbaseAdvisors.MacintiUpgradeTarget(0, marks), "slots 1 and 2 are marked");
            marks = new[] { 0, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
            Assert.AreEqual(9, StarbaseAdvisors.MacintiUpgradeTarget(0, marks), "none healthy up to 8: slot 9");
            Assert.AreEqual(7, StarbaseAdvisors.MacintiUpgradeTarget(4, marks));
            Assert.AreEqual(9, StarbaseAdvisors.MacintiUpgradeTarget(6, marks));
            Assert.AreEqual(4, StarbaseAdvisors.MacintiUpgradeTarget(7, marks), "s + 3 would pass 9: s - 3");
        }

        [Test]
        public void Upgrade_MarkThreeAlways_MarkTwoOneTimeInTen()
        {
            int[] marks = { 0, 0, 3, 0, 2, 0, 0, 0, 0, 0 };
            Assert.AreEqual(3, StarbaseAdvisors.MacintiUpgradeSlot(2, marks, 0, new ScriptedRandom()));
            Assert.AreEqual(7, StarbaseAdvisors.MacintiUpgradeSlot(4, marks, 0, new ScriptedRandom(0)));

            // A failed 1-in-10 roll falls through to the 8% rule for slot 4.
            Assert.AreEqual(5, StarbaseAdvisors.MacintiUpgradeSlot(4, marks, 0, new ScriptedRandom(1, 7)));
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.MacintiUpgradeSlot(4, marks, 0, new ScriptedRandom(1, 8)));
        }

        [Test]
        public void Upgrade_EightPercentStepWithinAGroup()
        {
            int[] marks = new int[10];
            foreach (int slot in new[] { 4, 5, 7, 8 })
            {
                Assert.AreEqual(slot + 1, StarbaseAdvisors.MacintiUpgradeSlot(slot, marks, 0, new ScriptedRandom(7)));
                Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.MacintiUpgradeSlot(slot, marks, 0, new ScriptedRandom(8)));
            }

            ScriptedRandom random = new ScriptedRandom(0);
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.MacintiUpgradeSlot(6, marks, 100, random), "slots 6 and 9 have no step");
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void Upgrade_PopulationRule_SixPercentPerPointAboveFifteen()
        {
            int[] marks = new int[10];
            ScriptedRandom noRoll = new ScriptedRandom(0);
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.MacintiUpgradeSlot(0, marks, 15, noRoll), "below 16%");
            Assert.AreEqual(0, noRoll.Calls);

            Assert.AreEqual(1, StarbaseAdvisors.MacintiUpgradeSlot(0, marks, 16, new ScriptedRandom(5)), "16%: chance 6");
            Assert.AreEqual(StarbaseAdvisors.NoSlot, StarbaseAdvisors.MacintiUpgradeSlot(0, marks, 16, new ScriptedRandom(6)));
            Assert.AreEqual(1, StarbaseAdvisors.MacintiUpgradeSlot(0, marks, 32, new ScriptedRandom(99)), "certain from 32%");
            Assert.AreEqual(3, StarbaseAdvisors.MacintiUpgradeSlot(2, marks, 50, new ScriptedRandom(0)));
        }

        [Test]
        public void PopulationPercent_RoundsToNearest_CappedAt999()
        {
            Assert.AreEqual(16, StarbaseAdvisors.PopulationPercent(15500, 100000));
            Assert.AreEqual(15, StarbaseAdvisors.PopulationPercent(15499, 100000));
            Assert.AreEqual(999, StarbaseAdvisors.PopulationPercent(100000, 1000));
            Assert.AreEqual(0, StarbaseAdvisors.PopulationPercent(0, 1000));
        }

        // ================================================================ in the chain

        private ClientData clientState;
        private Race race;
        private Star home;

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
            clientState.EmpireState.TurnYear = Global.StartingYear + 10;

            home = new Star { Name = "Home", Owner = 1, Position = new NovaPoint(100, 100), ThisRace = race, Colonists = 500000 };
            home.Gravity = home.OriginalGravity = race.GravityTolerance.OptimumLevel;
            home.Temperature = home.OriginalTemperature = race.TemperatureTolerance.OptimumLevel;
            home.Radiation = home.OriginalRadiation = race.RadiationTolerance.OptimumLevel;
            home.ResourcesOnHand = new Resources(1000, 1000, 1000, 0);
            home.MineralConcentration = new Resources(50, 50, 50, 0);
            clientState.EmpireState.OwnedStars.Add(home);
            clientState.EmpireState.StarReports.Add("Home", new StarIntel { Name = "Home", Position = home.Position, Owner = 1 });
        }

        private ShipDesign StarbaseDesign(string hull)
        {
            ShipDesign design = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            design.Name = hull + " design";
            design.Type = ItemType.Starbase;
            design.Blueprint = new Component { Name = hull, Cost = new Resources(10, 10, 10, 10) };
            design.Blueprint.Properties.Add("Hull", new Hull { Modules = new List<HullModule>() });
            design.Update();
            clientState.EmpireState.Designs[design.Key] = design;
            return design;
        }

        [Test]
        public void Chain_AStarterColonyFillingItsCapacity_MovesUpToSlotOne()
        {
            ShipDesign fort = StarbaseDesign("Orbital Fort");
            ShipDesign station = StarbaseDesign("Space Station");
            Fleet starbase = new Fleet("Home Starbase", 1, 50, home.Position);
            ShipToken token = new ShipToken(fort, 1);
            starbase.Composition.Add(token.Key, token);
            starbase.Type = ItemType.Starbase;
            home.Starbase = starbase;
            Assume.That(StarbaseAdvisors.PopulationPercent(home.Colonists, home.CapacityColonists(race)), Is.GreaterThanOrEqualTo(32));

            DefaultPlanetAI planetAI = new DefaultPlanetAI(home, clientState, new DefaultAIPlanner(clientState), new ScriptedRandom(), AiCategory.Macinti);
            planetAI.RunAdvisorChain();

            ProductionOrder order = home.ManufacturingQueue.Queue.First();
            Assert.AreEqual(station.Key, ((ShipProductionUnit)order.Unit).DesignKey, "slot 0 -> the first slot above with mark 0 (slot 1, unused)");

            planetAI.RunAdvisorChain();
            Assert.AreEqual(1, home.ManufacturingQueue.Queue.Count(o => o.Unit is ShipProductionUnit), "the queue test applies to a planet with a starbase");
        }
    }
}
