namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // The Mystery Trader of behavior-specs-10/turn-generation-engine.md §5a: creation (inside the
    // random-event wrapper), movement (special-object pass, mode 0), the fleet encounter (post-
    // movement stage, step 23c) with its technology / part / ships rewards and the three gift
    // templates, persistence, and the salvage exclusion of Mini Morph / Genesis Device. Every draw
    // is scripted; see MysteryTraderStep's and MysteryTraderMovementStep's summaries for the order.
    [TestFixture]
    public class MysteryTraderTest
    {
        /// <summary>Returns scripted Next(int) values in order, failing on an out-of-range value,
        /// an unscripted draw, or any other kind of draw. Records each maxValue asked for.</summary>
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> values;

            public List<int> MaxValues = new List<int>();

            public ScriptedRandom(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public int Remaining => values.Count;

            public override int Next(int maxValue)
            {
                MaxValues.Add(maxValue);
                Assert.IsTrue(values.Count > 0, "Unscripted draw: Next(" + maxValue + ")");
                int value = values.Dequeue();
                Assert.IsTrue(value >= 0 && value < maxValue, "Scripted value " + value + " out of range for Next(" + maxValue + ")");
                return value;
            }

            public override int Next() => throw new AssertionException("Unexpected Next()");

            public override int Next(int minValue, int maxValue) => throw new AssertionException("Unexpected Next(min, max)");

            public override double NextDouble() => throw new AssertionException("Unexpected NextDouble()");
        }

        private int originalMapWidth;
        private int originalMapHeight;
        private bool originalNoRandomEvents;

        private ServerData serverState;
        private EmpireData human;
        private EmpireData other;

        [SetUp]
        public void Init()
        {
            originalMapWidth = GameSettings.Data.MapWidth;
            originalMapHeight = GameSettings.Data.MapHeight;
            originalNoRandomEvents = GameSettings.Data.NoRandomEvents;
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.MapHeight = 400;
            GameSettings.Data.NoRandomEvents = false;

            serverState = new ServerData();
            human = MakeEmpire(1);
            other = MakeEmpire(2);
            serverState.TurnYear = Global.StartingYear + 50;
        }

        [TearDown]
        public void Restore()
        {
            GameSettings.Data.MapWidth = originalMapWidth;
            GameSettings.Data.MapHeight = originalMapHeight;
            GameSettings.Data.NoRandomEvents = originalNoRandomEvents;
        }

        private EmpireData MakeEmpire(ushort id)
        {
            Race race = new Race();
            race.ResearchCosts = new TechLevel(100);
            EmpireData empire = new EmpireData { Id = id, Race = race };
            serverState.AllEmpires.Add(empire.Id, empire);
            return empire;
        }

        private MysteryTrader AddTrader(int x, int y, int item = MysteryTrader.TechnologyItem)
        {
            MysteryTrader trader = new MysteryTrader
            {
                Position = new NovaPoint(x, y),
                Destination = new NovaPoint(380, y),
                Speed = 10,
                Item = item,
            };
            trader.Key = serverState.AllMysteryTraders.Count + 1;
            serverState.AllMysteryTraders.Add(trader.Key, trader);
            return trader;
        }

        private static ShipDesign PlainDesign(EmpireData empire)
        {
            ShipDesign design = new ShipDesign(empire.GetNextDesignKey()) { Blueprint = new Component { Mass = 100 }, Name = "Freighter" };
            // A hull with no fuel capacity counts as a starbase (Hull.IsStarbase), and a token with
            // no armor counts as destroyed in battle.
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 100, ArmorStrength = 50 };
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            return design;
        }

        private Fleet AddFleet(EmpireData empire, int x, int y, int ironium, int boranium = 0, int germanium = 0)
        {
            ShipDesign design = PlainDesign(empire);
            Fleet fleet = new Fleet("Hauler", empire.Id, 0, new NovaPoint(x, y));
            fleet.Key = empire.GetNextFleetKey();
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.Cargo.Ironium = ironium;
            fleet.Cargo.Boranium = boranium;
            fleet.Cargo.Germanium = germanium;
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(x, y), Destination = "Space", WarpFactor = 0 });
            empire.AddOrUpdateFleet(fleet);
            return fleet;
        }

        private List<Message> MessagesTo(EmpireData empire)
        {
            return serverState.AllMessages.Where(m => m.Audience == empire.Id && m.Type == MysteryTraderStep.MessageType).ToList();
        }

        private static void CapAllFields(EmpireData empire)
        {
            foreach (TechLevel.ResearchField field in MysteryTraderStep.FieldOrder)
            {
                empire.ResearchLevels[field] = TechLevel.MaxLevel;
            }
        }

        private static Dictionary<string, int> PartCounts(ShipDesign design)
        {
            return design.Hull.Modules.Where(m => m.AllocatedComponent != null)
                .GroupBy(m => m.AllocatedComponent.Name)
                .ToDictionary(g => g.Key, g => g.Sum(m => m.ComponentCount));
        }

        // ---------------------------------------------------------------- creation

        [TestCase(39, 0)]
        [TestCase(40, 7)]
        [TestCase(49, 4)]  // 2449: 1 in 4
        [TestCase(50, 7)]  // 2450: 1 in 7
        [TestCase(51, 0)]  // 2451: none
        [TestCase(71, 2)]  // 2471: 1 in 2
        [TestCase(133, 3)] // 2533: 1 in 3
        [TestCase(171, 2)]
        [TestCase(177, 4)] // 177 mod 128 = 49
        [TestCase(149, 0)] // odd, none of the special residues
        public void CreationOdds_FollowTheYearRule(int counter, int odds)
        {
            Assert.AreEqual(odds, MysteryTraderStep.CreationOdds(counter));
        }

        [TestCase(39)]
        [TestCase(51)]
        public void TryCreate_NoDrawAtAll_BelowCounter40OrInAnOddYear(int counter)
        {
            var random = new ScriptedRandom();
            Assert.IsNull(MysteryTraderStep.TryCreate(serverState, random, counter));
            Assert.AreEqual(0, random.MaxValues.Count);
        }

        [Test]
        public void TryCreate_FailedRoll_CreatesNothing()
        {
            var random = new ScriptedRandom(3);
            Assert.IsNull(MysteryTraderStep.TryCreate(serverState, random, 50));
            CollectionAssert.AreEqual(new[] { 7 }, random.MaxValues);
            Assert.AreEqual(0, serverState.AllMysteryTraders.Count);
        }

        [Test]
        public void TryCreate_PlacesSpeedEdgesCargo_AndTellsEveryone()
        {
            // chance hit; speed 8 + 2; crosses along x; starts on the far edge; start y 20 + 100,
            // destination y 20 + 0; cargo: k = 5, Next(10) = 5 is a part, Next(13) = 2 Langston.
            var random = new ScriptedRandom(0, 2, 0, 1, 100, 0, 5, 2);
            MysteryTrader trader = MysteryTraderStep.TryCreate(serverState, random, 50);

            Assert.IsNotNull(trader);
            Assert.AreEqual(0, random.Remaining);
            CollectionAssert.AreEqual(new[] { 7, 5, 2, 2, 361, 361, 10, 13 }, random.MaxValues);
            Assert.AreEqual(10, trader.Speed);
            Assert.AreEqual(new NovaPoint(380, 120), trader.Position);
            Assert.AreEqual(new NovaPoint(20, 20), trader.Destination);
            Assert.AreEqual(2, trader.Item);
            Assert.AreSame(trader, serverState.AllMysteryTraders[trader.Key]);
            Assert.AreEqual(1, MessagesTo(human).Count, "Message 299 to every player");
            Assert.AreEqual(1, MessagesTo(other).Count);
        }

        [Test]
        public void TryCreate_NoSingletonCheck_SeveralTradersCanExist()
        {
            AddTrader(100, 100);
            var random = new ScriptedRandom(0, 0, 1, 0, 0, 0, 0, 3);
            MysteryTrader trader = MysteryTraderStep.TryCreate(serverState, random, 50);

            Assert.IsNotNull(trader);
            Assert.AreEqual(2, serverState.AllMysteryTraders.Count);
            Assert.AreEqual(8, trader.Speed);
            // Along y, near edge first: starts at y = 20, heads for y = 380.
            Assert.AreEqual(new NovaPoint(20, 20), trader.Position);
            Assert.AreEqual(new NovaPoint(20, 380), trader.Destination);
        }

        [Test]
        public void ChooseCargo_NoPartBranch_ShipsOneInSixOtherwiseTechnology()
        {
            Assert.AreEqual(MysteryTrader.ShipsItem, MysteryTraderStep.ChooseCargo(new ScriptedRandom(4, 0), 50, 10));
            Assert.AreEqual(MysteryTrader.TechnologyItem, MysteryTraderStep.ChooseCargo(new ScriptedRandom(4, 5), 50, 10));
        }

        [Test]
        public void ChooseCargo_KDependsOnYearAndSpeed()
        {
            // Counter 300: k = 2; speed 8 adds 1 (3), speed 12 subtracts 1 (1).
            Assert.AreEqual(MysteryTrader.TechnologyItem, MysteryTraderStep.ChooseCargo(new ScriptedRandom(2, 1), 300, 8));
            Assert.AreEqual(4, MysteryTraderStep.ChooseCargo(new ScriptedRandom(1, 4), 300, 12));
            // Counter 100: k = 3.
            Assert.AreEqual(5, MysteryTraderStep.ChooseCargo(new ScriptedRandom(3, 5), 100, 10));
        }

        [Test]
        public void ChooseCargo_RestrictedFirstDraw_IsRedrawnOnce_AndYearGateCanTurnItIntoTechnology()
        {
            // Genesis Device first: redrawn; second draw Jump Gate before counter 180: 50/50 roll.
            Assert.AreEqual(MysteryTrader.TechnologyItem, MysteryTraderStep.ChooseCargo(new ScriptedRandom(9, 10, 11, 0), 100, 10));
            Assert.AreEqual(11, MysteryTraderStep.ChooseCargo(new ScriptedRandom(9, 10, 11, 1), 100, 10));
            // After counter 180 no gate roll at all.
            Assert.AreEqual(11, MysteryTraderStep.ChooseCargo(new ScriptedRandom(9, 6, 11), 200, 10));
            // Anti Matter Torpedo is redrawn but never year-gated; the second result is kept even
            // when it is itself restricted.
            Assert.AreEqual(6, MysteryTraderStep.ChooseCargo(new ScriptedRandom(9, 6, 6), 50, 10));
            // An unrestricted first draw is kept with no further draw.
            var random = new ScriptedRandom(9, 8);
            Assert.AreEqual(8, MysteryTraderStep.ChooseCargo(random, 50, 10));
            Assert.AreEqual(0, random.Remaining);
        }

        [Test]
        public void RandomEventsStep_RollsForTheTraderLast()
        {
            Star star = new Star { Name = "Anywhere" };
            serverState.AllStars.Add(star.Key, star);
            serverState.TurnYear = Global.StartingYear + 50;

            // Comet, shift and deposit all miss (planet, chance each), then the Trader's 1 in 7 hits.
            var random = new ScriptedRandom(0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 9, 0);
            new RandomEventsStep(random).Process(serverState);

            CollectionAssert.AreEqual(new[] { 1, 20, 1, 20, 1, 15, 7, 5, 2, 2, 361, 361, 10, 13 }, random.MaxValues);
            Assert.AreEqual(1, serverState.AllMysteryTraders.Count);
        }

        [Test]
        public void RandomEventsStep_NoRandomEvents_CreatesNoTrader()
        {
            GameSettings.Data.NoRandomEvents = true;
            var random = new ScriptedRandom();
            new RandomEventsStep(random).Process(serverState);
            Assert.AreEqual(0, serverState.AllMysteryTraders.Count);
        }

        // ---------------------------------------------------------------- movement

        [Test]
        public void Movement_StepsSpeedSquaredTowardTheDestination_RoundingEachCoordinate()
        {
            MysteryTrader trader = AddTrader(0, 0);
            trader.Destination = new NovaPoint(100, 50);
            trader.Speed = 8;

            // No course change (Next(25) != 0). 64 ly toward (100, 50): 57.24, 28.62 -> 57, 29.
            new MysteryTraderMovementStep(new ScriptedRandom(1)).Process(serverState);

            Assert.AreEqual(new NovaPoint(57, 29), trader.Position);
            Assert.AreEqual(8, trader.Speed);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [Test]
        public void Movement_CourseChange_SpeedsUpAndMayPickANewEdgeDestination()
        {
            MysteryTrader trader = AddTrader(200, 200);
            trader.Destination = new NovaPoint(380, 200);

            // Next(25) = 0: speed 11, message 304; Next(3) = 0: new destination along y (Next(2) = 1)
            // on the near edge (Next(2) = 0), x = 20 + 180 -> (200, 20); then 121 ly toward it.
            new MysteryTraderMovementStep(new ScriptedRandom(0, 0, 1, 0, 180)).Process(serverState);

            Assert.AreEqual(11, trader.Speed);
            Assert.AreEqual(new NovaPoint(200, 20), trader.Destination);
            Assert.AreEqual(new NovaPoint(200, 79), trader.Position);
            Assert.AreEqual(1, MessagesTo(human).Count);
            Assert.AreEqual(1, MessagesTo(other).Count);
        }

        [Test]
        public void Movement_CourseChangeKeepsDestination_TwoTimesInThree()
        {
            MysteryTrader trader = AddTrader(0, 200);
            trader.Destination = new NovaPoint(380, 200);
            trader.Speed = 12;

            new MysteryTraderMovementStep(new ScriptedRandom(0, 2)).Process(serverState);

            Assert.AreEqual(13, trader.Speed);
            Assert.AreEqual(new NovaPoint(380, 200), trader.Destination);
            Assert.AreEqual(new NovaPoint(169, 200), trader.Position);
        }

        [Test]
        public void Movement_AboveSpeed12_NoCourseChangeRoll()
        {
            MysteryTrader trader = AddTrader(0, 200);
            trader.Destination = new NovaPoint(380, 200);
            trader.Speed = 13;

            var random = new ScriptedRandom();
            new MysteryTraderMovementStep(random).Process(serverState);

            Assert.AreEqual(0, random.MaxValues.Count);
            Assert.AreEqual(new NovaPoint(169, 200), trader.Position);
        }

        [Test]
        public void Movement_Arrival_OnlyTrader_HalfTheTimeRemoved()
        {
            MysteryTrader trader = AddTrader(300, 200);
            trader.Destination = new NovaPoint(380, 200); // 80 ly <= 100

            new MysteryTraderMovementStep(new ScriptedRandom(1, 0)).Process(serverState);

            Assert.AreEqual(0, serverState.AllMysteryTraders.Count);
        }

        [Test]
        public void Movement_Arrival_OnlyTrader_OtherwiseAnotherPass()
        {
            MysteryTrader trader = AddTrader(300, 200);
            trader.Destination = new NovaPoint(380, 200);
            trader.ServedRaces.Add(human.Id);

            // no course change, 50/50 -> another pass, new destination along x, near edge, y 20 + 5.
            new MysteryTraderMovementStep(new ScriptedRandom(1, 1, 0, 0, 5)).Process(serverState);

            Assert.AreEqual(1, serverState.AllMysteryTraders.Count);
            Assert.AreEqual(new NovaPoint(380, 200), trader.Position, "It moves to the destination and no further");
            Assert.AreEqual(9, trader.Speed);
            Assert.AreEqual(new NovaPoint(20, 25), trader.Destination);
            Assert.IsTrue(trader.ServedRaces.Contains(human.Id), "The served mask is never reset");
            Assert.AreEqual(1, MessagesTo(human).Count, "Message 192 to every player");
            Assert.AreEqual(1, MessagesTo(other).Count);
        }

        [Test]
        public void Movement_AnotherPass_SpeedNeverBelow7()
        {
            MysteryTrader trader = AddTrader(370, 200);
            trader.Speed = 7;
            new MysteryTraderMovementStep(new ScriptedRandom(1, 1, 0, 0, 5)).Process(serverState);
            Assert.AreEqual(7, trader.Speed);
        }

        [Test]
        public void Movement_Arrival_WithAnotherTrader_RemovedWithoutARoll()
        {
            MysteryTrader arriving = AddTrader(300, 200);
            MysteryTrader cruising = AddTrader(0, 100);

            // arriving: course roll only; cruising: course roll only.
            var random = new ScriptedRandom(1, 1);
            new MysteryTraderMovementStep(random).Process(serverState);

            Assert.IsFalse(serverState.AllMysteryTraders.ContainsKey(arriving.Key));
            Assert.IsTrue(serverState.AllMysteryTraders.ContainsKey(cruising.Key));
            Assert.AreEqual(new NovaPoint(100, 100), cruising.Position);
        }

        // ---------------------------------------------------------------- encounters

        [Test]
        public void Encounter_TooFewMinerals_Message264_FleetKept()
        {
            AddTrader(100, 100);
            Fleet fleet = AddFleet(human, 100, 100, 3000, 1000, 999);

            new MysteryTraderStep(new ScriptedRandom()).Process(serverState);

            Assert.IsTrue(human.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(1, MessagesTo(human).Count);
            StringAssert.Contains("too few minerals", MessagesTo(human)[0].Text);
        }

        [Test]
        public void Encounter_TraderGivenFleet_WithTooFewMinerals_IsNotToldOff()
        {
            AddTrader(100, 100);
            Fleet fleet = AddFleet(human, 100, 100, 0);
            serverState.MysteryTraderGiftFleets.Add(fleet.Key);

            new MysteryTraderStep(new ScriptedRandom()).Process(serverState);

            Assert.IsTrue(human.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(0, MessagesTo(human).Count);
        }

        [Test]
        public void Encounter_RaceAlreadyServed_Message280_FleetKept()
        {
            MysteryTrader trader = AddTrader(100, 100);
            trader.ServedRaces.Add(human.Id);
            Fleet fleet = AddFleet(human, 100, 100, 6000);

            new MysteryTraderStep(new ScriptedRandom()).Process(serverState);

            Assert.IsTrue(human.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(1, MessagesTo(human).Count);
            StringAssert.Contains("still recovering", MessagesTo(human)[0].Text);
        }

        [Test]
        public void Encounter_OnlyExactPosition()
        {
            AddTrader(100, 100);
            Fleet fleet = AddFleet(human, 101, 100, 9000);

            new MysteryTraderStep(new ScriptedRandom()).Process(serverState);

            Assert.IsTrue(human.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [TestCase(5000, 0, 6)]
        [TestCase(6199, 0, 6)]
        [TestCase(6200, 0, 7)]
        [TestCase(99999, 0, 10)]
        [TestCase(5000, 59, 6)]
        [TestCase(5000, 60, 5)]
        [TestCase(5000, 71, 5)]
        [TestCase(5000, 72, 4)]
        [TestCase(10000, 83, 8)]
        [TestCase(5000, 84, 3)]
        [TestCase(5000, 95, 3)]
        [TestCase(99999, 96, 2)]
        [TestCase(99999, 107, 2)]
        [TestCase(99999, 108, 1)]
        public void AdvanceCount_FollowsTheMineralAndTechTotalRule(int minerals, int techTotal, int expected)
        {
            Assert.AreEqual(expected, MysteryTraderStep.AdvanceCount(minerals, techTotal));
        }

        [Test]
        public void Encounter_TechnologyRoute_AbsorbsFleet_AndBuysOneLevelPerAdvance()
        {
            MysteryTrader trader = AddTrader(100, 100);
            Fleet fleet = AddFleet(human, 100, 100, 3000, 2000, 2400); // 7,400 kT -> N = 8

            // Per advance: Next(4) = 0 -> lowest field (first of ties, Energy order); or Next(4) != 0
            // then Next(6) picks Energy/Weapons/Propulsion/Construction/Electronics/Biotechnology.
            var random = new ScriptedRandom(
                0,       // lowest: Energy
                0,       // lowest: Weapons
                3, 5,    // Biotechnology
                1, 5,    // Biotechnology again
                2, 0,    // Energy (level 1, below the cap)
                0,       // lowest: Propulsion
                0,       // lowest: Construction
                0);      // lowest: Electronics
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.Remaining);
            Assert.IsFalse(human.OwnedFleets.ContainsKey(fleet.Key), "The whole fleet is absorbed");
            Assert.IsTrue(trader.ServedRaces.Contains(human.Id));
            Assert.AreEqual(2, human.ResearchLevels[TechLevel.ResearchField.Energy]);
            Assert.AreEqual(1, human.ResearchLevels[TechLevel.ResearchField.Weapons]);
            Assert.AreEqual(1, human.ResearchLevels[TechLevel.ResearchField.Propulsion]);
            Assert.AreEqual(1, human.ResearchLevels[TechLevel.ResearchField.Construction]);
            Assert.AreEqual(1, human.ResearchLevels[TechLevel.ResearchField.Electronics]);
            Assert.AreEqual(2, human.ResearchLevels[TechLevel.ResearchField.Biotechnology]);
            Assert.AreEqual(1, MessagesTo(human).Count);
            StringAssert.Contains("8 technology advances", MessagesTo(human)[0].Text);
        }

        [Test]
        public void Encounter_TechnologyRoute_DoublesTheBankedPoolPlusOneLevel()
        {
            AddTrader(100, 100);
            AddFleet(human, 100, 100, 5000);
            human.ResearchLevels = new TechLevel(10, 10, 10, 10, 10, 10); // T = 60 -> N = 6 - 1 = 5
            int cost11 = Research.Cost(TechLevel.ResearchField.Energy, human.Race, human.ResearchLevels, 11);
            human.ResearchResources[TechLevel.ResearchField.Energy] = cost11 / 2;

            // First advance: Energy (random). Pool = cost11 / 2 x 2 + cost11 >= 2 x cost11 - 1.
            var random = new ScriptedRandom(1, 0, 1, 1, 1, 2, 1, 3, 1, 4);
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.Remaining);
            Assert.That(human.ResearchLevels[TechLevel.ResearchField.Energy], Is.GreaterThanOrEqualTo(11));
            Assert.AreEqual(11, human.ResearchLevels[TechLevel.ResearchField.Weapons]);
        }

        [Test]
        public void Encounter_TechnologyRoute_CappedRandomFieldFallsBackToLowest_AndStopsWhenAllCapped()
        {
            AddTrader(100, 100);
            AddFleet(human, 100, 100, 5000);
            CapAllFields(human);
            human.ResearchLevels[TechLevel.ResearchField.Biotechnology] = 25; // T = 155 -> N = 1

            // Random field Energy is capped -> the lowest (Biotechnology).
            new MysteryTraderStep(new ScriptedRandom(1, 0)).Process(serverState);

            Assert.AreEqual(26, human.ResearchLevels[TechLevel.ResearchField.Biotechnology]);
        }

        [Test]
        public void Encounter_AllFieldsCapped_OneInFiveMessage270()
        {
            AddTrader(100, 100);
            Fleet fleet = AddFleet(human, 100, 100, 5000);
            CapAllFields(human);

            new MysteryTraderStep(new ScriptedRandom(0)).Process(serverState);

            Assert.IsFalse(human.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(0, human.GrantedSpecialComponents.Count);
            Assert.AreEqual(1, MessagesTo(human).Count);
            StringAssert.Contains("nothing new to teach", MessagesTo(human)[0].Text);
        }

        [Test]
        public void Encounter_AllFieldsCapped_OtherwisePartRoute_RandomItem()
        {
            AddTrader(100, 100);
            AddFleet(human, 100, 100, 5000);
            CapAllFields(human);

            // Next(5) != 0 -> part route; the Trader carried technology, so Next(13) = 8: Mini Morph.
            new MysteryTraderStep(new ScriptedRandom(1, 8)).Process(serverState);

            Assert.IsTrue(human.GrantedSpecialComponents.Contains("Mini Morph"));
            Assert.IsTrue(human.AvailableComponents.Contains("Mini Morph"), "Capped tech: buildable at once");
            StringAssert.Contains("Mini Morph hull", MessagesTo(human)[0].Text);
        }

        [Test]
        public void Encounter_PartRoute_GrantsTheCarriedPart_EvenGenesisDevice()
        {
            AddTrader(100, 100, 10);
            Fleet fleet = AddFleet(human, 100, 100, 5000);

            var random = new ScriptedRandom();
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.MaxValues.Count);
            Assert.IsFalse(human.OwnedFleets.ContainsKey(fleet.Key));
            Assert.IsTrue(human.GrantedSpecialComponents.Contains("Genesis Device"));
            Assert.IsFalse(human.AvailableComponents.Contains("Genesis Device"), "Granted, but tech 0 cannot build it yet");
            StringAssert.Contains("Genesis Device", MessagesTo(human)[0].Text);
        }

        [Test]
        public void Encounter_OwnedPart_TakesTheTechnologyRoute()
        {
            AddTrader(100, 100, 2);
            AddFleet(human, 100, 100, 5000);
            human.GrantedSpecialComponents.Add("Langston Shell");

            var random = new ScriptedRandom(0, 0, 0, 0, 0, 0);
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.Remaining);
            Assert.AreEqual(6, MysteryTraderStep.FieldOrder.Sum(f => human.ResearchLevels[f]));
            Assert.AreEqual(1, human.GrantedSpecialComponents.Count);
        }

        [Test]
        public void Encounter_PartRoute_RedrawsOwnedItems_UpTo25Times_ThenShips()
        {
            AddTrader(100, 100);
            AddFleet(human, 100, 100, 5000);
            CapAllFields(human);
            for (int bit = 0; bit < SpecialComponentGrants.GiftBitCount; bit++)
            {
                human.GrantedSpecialComponents.Add(SpecialComponentGrants.GiftBitName(bit));
            }

            // Next(5) = 1 -> part route; first Next(13) and 25 redraws, all owned -> ships item;
            // ships gift: Next(4) = 0 Lifeboat, Next(3) = 1 one ship.
            var script = new List<int> { 1 };
            script.AddRange(Enumerable.Repeat(3, 26));
            script.AddRange(new[] { 0, 1 });
            var random = new ScriptedRandom(script.ToArray());
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.Remaining);
            Assert.AreEqual(26, random.MaxValues.Count(max => max == 13));
            Fleet gift = human.OwnedFleets.Values.Single();
            Assert.AreEqual(MysteryTraderStep.LifeboatName, gift.Composition.Values.Single().Design.Name);
        }

        [Test]
        public void Encounter_ShipsGift_Lifeboat_IsTheNubianTemplate_AtTheAbsorbedFleetsPosition()
        {
            AddTrader(100, 100, MysteryTrader.ShipsItem);
            Fleet fleet = AddFleet(human, 100, 100, 5000);

            // Lifeboat (Next(4) = 0), two ships (Next(3) = 0).
            var random = new ScriptedRandom(0, 0);
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.Remaining);
            Assert.IsFalse(human.OwnedFleets.ContainsKey(fleet.Key));
            Fleet gift = human.OwnedFleets.Values.Single();
            ShipToken token = gift.Composition.Values.Single();
            Assert.AreEqual(2, token.Quantity);
            Assert.AreEqual(new NovaPoint(100, 100), gift.Position);
            Assert.IsTrue(serverState.MysteryTraderGiftFleets.Contains(gift.Key), "Flagged as Trader-given");
            Assert.IsTrue(human.Designs.ContainsKey(token.Design.Key));

            ShipDesign design = token.Design;
            Assert.AreEqual(MysteryTraderStep.LifeboatName, design.Name);
            Assert.AreEqual("Nubian", design.Blueprint.Name);
            Assert.AreEqual(13, design.Hull.Modules.Count(m => m.AllocatedComponent != null && m.ComponentCount == 3));
            CollectionAssert.AreEquivalent(
                new Dictionary<string, int>
                {
                    { "Enigma Pulsar", 3 }, { "Mega Poly Shell", 6 }, { "Anti Matter Torpedo", 6 }, { "Langston Shell", 6 },
                    { "Multi Function Pod", 6 }, { "Multi Cargo Pod", 3 }, { "Multi Contained Munition", 9 },
                },
                PartCounts(design));
            Assert.AreEqual(1, MessagesTo(human).Count);
            StringAssert.Contains("2 M.T. Lifeboat ships", MessagesTo(human)[0].Text);
        }

        [TestCase(0, "M.T. Scout", "Langston Shell")]
        [TestCase(1, "M.T. Probe", "Mega Poly Shell")]
        public void Encounter_ShipsGift_ScoutAndProbe_AreMiniMorphTemplates_WithExtraShips(int which, string name, string shell)
        {
            AddTrader(100, 100, MysteryTrader.ShipsItem);
            AddFleet(human, 100, 100, 5000);

            // Not a Lifeboat (Next(4) = 2), Scout/Probe Next(2), one ship (Next(3) = 2), then a
            // Scout or Probe adds Next(count + 1) = 1.
            var random = new ScriptedRandom(2, which, 2, 1);
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.Remaining);
            CollectionAssert.AreEqual(new[] { 4, 2, 3, 2 }, random.MaxValues);
            ShipToken token = human.OwnedFleets.Values.Single().Composition.Values.Single();
            Assert.AreEqual(2, token.Quantity);
            Assert.AreEqual(name, token.Design.Name);
            Assert.AreEqual("Mini Morph", token.Design.Blueprint.Name);
            CollectionAssert.AreEquivalent(
                new Dictionary<string, int>
                {
                    { "Enigma Pulsar", 2 }, { shell, 3 }, { "Multi Function Pod", 1 }, { "Multi Cargo Pod", 1 },
                    { "Jump Gate", 1 }, { "Anti Matter Torpedo", 4 },
                },
                PartCounts(token.Design));
        }

        [Test]
        public void Encounter_ShipsGift_LateGame_AddsShipsUnlessSingleHuman_CapFive()
        {
            serverState.TurnYear = Global.StartingYear + 250;
            serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = 1, AiProgram = "Human" });
            serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = 2, AiProgram = "Human" });
            AddTrader(100, 100, MysteryTrader.ShipsItem);
            AddFleet(human, 100, 100, 5000);

            // Lifeboat 1 in 3 after counter 100 (Next(3) = 0); two ships; + Next(250 / 100 + 1) = 2 -> 4.
            var random = new ScriptedRandom(0, 0, 2);
            new MysteryTraderStep(random).Process(serverState);

            CollectionAssert.AreEqual(new[] { 3, 3, 3 }, random.MaxValues);
            Assert.AreEqual(4, human.OwnedFleets.Values.Single().Composition.Values.Single().Quantity);
        }

        [Test]
        public void Encounter_ShipsGift_LateGame_SingleHumanGame_NoExtraShips()
        {
            serverState.TurnYear = Global.StartingYear + 250;
            serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = 1, AiProgram = "Human" });
            serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = 2, AiProgram = "Default AI" });
            AddTrader(100, 100, MysteryTrader.ShipsItem);
            AddFleet(human, 100, 100, 5000);

            var random = new ScriptedRandom(0, 0);
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.Remaining);
            Assert.AreEqual(2, human.OwnedFleets.Values.Single().Composition.Values.Single().Quantity);
        }

        [Test]
        public void Encounter_ShipsGift_ComputerPlayerGetsNothing_FleetStillAbsorbed()
        {
            serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = 2, AiProgram = "Default AI" });
            AddTrader(100, 100, MysteryTrader.ShipsItem);
            Fleet fleet = AddFleet(other, 100, 100, 5000);

            var random = new ScriptedRandom();
            new MysteryTraderStep(random).Process(serverState);

            Assert.AreEqual(0, random.MaxValues.Count);
            Assert.AreEqual(0, other.OwnedFleets.Count);
            Assert.IsFalse(other.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(0, MessagesTo(other).Count);
        }

        [Test]
        public void Encounter_ShipsGift_NoFreeDesignSlot_Message336()
        {
            for (int i = 0; i < MysteryTraderStep.MaxShipDesigns; i++)
            {
                ShipDesign design = PlainDesign(human);
                human.Designs.Add(design.Key, design);
            }
            AddTrader(100, 100, MysteryTrader.ShipsItem);
            AddFleet(human, 100, 100, 5000);

            new MysteryTraderStep(new ScriptedRandom(0, 1)).Process(serverState);

            Assert.AreEqual(0, human.OwnedFleets.Count);
            Assert.AreEqual(MysteryTraderStep.MaxShipDesigns, human.Designs.Count);
            StringAssert.Contains("no room", MessagesTo(human).Single().Text);
        }

        [Test]
        public void Encounter_ShipsGift_ReusesAnIdenticalDesign_EvenWithAllSlotsFull()
        {
            ShipDesign existing = MysteryTraderStep.BuildTemplate(MysteryTraderStep.LifeboatName, human);
            existing.Key = human.GetNextDesignKey();
            human.Designs.Add(existing.Key, existing);
            for (int i = 1; i < MysteryTraderStep.MaxShipDesigns; i++)
            {
                ShipDesign design = PlainDesign(human);
                human.Designs.Add(design.Key, design);
            }
            AddTrader(100, 100, MysteryTrader.ShipsItem);
            AddFleet(human, 100, 100, 5000);

            new MysteryTraderStep(new ScriptedRandom(0, 1)).Process(serverState);

            Assert.AreSame(existing, human.OwnedFleets.Values.Single().Composition.Values.Single().Design);
            Assert.AreEqual(MysteryTraderStep.MaxShipDesigns, human.Designs.Count);
        }

        [Test]
        public void Encounter_TwoRacesAtOneTrader_EachServedOnce()
        {
            MysteryTrader trader = AddTrader(100, 100, 3);
            AddFleet(human, 100, 100, 5000);
            AddFleet(other, 100, 100, 5000);
            AddFleet(human, 100, 100, 5000);

            // A carried part the race lacks is granted with no draw at all.
            new MysteryTraderStep(new ScriptedRandom()).Process(serverState);

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, trader.ServedRaces);
            Assert.AreEqual(1, human.OwnedFleets.Count, "The race's second fleet is refused (280)");
            Assert.IsTrue(human.GrantedSpecialComponents.Contains("Mega Poly Shell"));
            Assert.IsTrue(other.GrantedSpecialComponents.Contains("Mega Poly Shell"));
        }

        // ---------------------------------------------------------------- persistence

        [Test]
        public void Trader_And_GiftFlags_RoundTripThroughTheServerStateFile()
        {
            MysteryTrader trader = AddTrader(123, 45, 7);
            trader.Destination = new NovaPoint(380, 77);
            trader.Speed = 11;
            trader.ServedRaces.Add(1);
            trader.ServedRaces.Add(2);
            MysteryTrader technology = AddTrader(10, 20);
            serverState.MysteryTraderGiftFleets.Add(0x100000005L);

            ServerData saved = new ServerData();
            saved.AllMysteryTraders = serverState.AllMysteryTraders;
            saved.MysteryTraderGiftFleets = serverState.MysteryTraderGiftFleets;
            string folder = Path.Combine(Path.GetTempPath(), "nova-trader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                saved.GameFolder = folder;
                saved.StatePathName = Path.Combine(folder, "state.xml");
                saved.ToXml();

                XmlDocument xmldoc = new XmlDocument();
                xmldoc.Load(saved.StatePathName);
                ServerData loaded = new ServerData(xmldoc);

                Assert.AreEqual(2, loaded.AllMysteryTraders.Count);
                MysteryTrader back = loaded.AllMysteryTraders[trader.Key];
                Assert.AreEqual(new NovaPoint(123, 45), back.Position);
                Assert.AreEqual(new NovaPoint(380, 77), back.Destination);
                Assert.AreEqual(11, back.Speed);
                Assert.AreEqual(7, back.Item);
                CollectionAssert.AreEquivalent(new[] { 1, 2 }, back.ServedRaces);
                Assert.IsTrue(loaded.AllMysteryTraders[technology.Key].CarriesTechnology);
                CollectionAssert.AreEquivalent(new[] { 0x100000005L }, loaded.MysteryTraderGiftFleets);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // ---------------------------------------------------------------- salvage exclusion

        [Test]
        public void BattleSalvage_NeverGrantsMiniMorphOrGenesisDevice()
        {
            EmpireData empire = human;
            SalvageTables tables = new SalvageTables();
            foreach (int bit in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 9, 11 })
            {
                tables.RarePercent[bit] = 25;
            }

            Random random = new Random(99);
            for (int i = 0; i < 1000 && empire.GrantedSpecialComponents.Count < 10; i++)
            {
                empire.TechGainedThisTurn = false;
                SalvageDispatcher.TryGain(empire, tables, random);
            }

            CollectionAssert.AreEquivalent(SpecialComponentGrants.SalvageableComponents, empire.GrantedSpecialComponents);
            Assert.IsFalse(empire.GrantedSpecialComponents.Contains("Mini Morph"));
            Assert.IsFalse(empire.GrantedSpecialComponents.Contains("Genesis Device"));
        }
    }
}
