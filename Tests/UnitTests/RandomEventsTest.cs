namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // The three yearly random events of behavior-specs-9/turn-generation-engine.md §5a (comet,
    // environment shift, mineral deposit), the "No Random Events" option (§1b) and the disaster
    // queue cleanup (production-queue.md §8). Every draw is scripted; see RandomEventsStep's own
    // summary for the draw order.
    [TestFixture]
    public class RandomEventsTest
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

        private bool originalNoRandomEvents;
        private int originalMapWidth;

        private ServerData serverState;
        private EmpireData owner;
        private EmpireData other;
        private Star star;

        [SetUp]
        public void Init()
        {
            originalNoRandomEvents = GameSettings.Data.NoRandomEvents;
            originalMapWidth = GameSettings.Data.MapWidth;
            GameSettings.Data.NoRandomEvents = false;
            GameSettings.Data.MapWidth = 400;

            serverState = new ServerData();
            owner = new EmpireData { Id = 1 };
            other = new EmpireData { Id = 2 };
            serverState.AllEmpires.Add(owner.Id, owner);
            serverState.AllEmpires.Add(other.Id, other);

            star = new Star
            {
                Name = "Target",
                Owner = owner.Id,
                Colonists = 10000,
                Gravity = 50, OriginalGravity = 45,
                Temperature = 50, OriginalTemperature = 40,
                Radiation = 50, OriginalRadiation = 50,
            };
            star.MineralConcentration = new Resources(100, 100, 100, 0);
            star.ResourcesOnHand = new Resources(0, 0, 0, 0);
            serverState.AllStars.Add(star.Key, star);
            owner.OwnedStars.Add(star);
        }

        [TearDown]
        public void Restore()
        {
            GameSettings.Data.NoRandomEvents = originalNoRandomEvents;
            GameSettings.Data.MapWidth = originalMapWidth;
        }

        private static ProductionOrder Order(bool autoBuild)
        {
            return new ProductionOrder(1, new FactoryProductionUnit(new Race()), autoBuild);
        }

        private List<ProductionOrder> FillQueue()
        {
            var orders = new List<ProductionOrder> { Order(false), Order(true), Order(false), Order(true) };
            star.ManufacturingQueue.Queue.AddRange(orders);
            return orders;
        }

        private List<Message> MessagesTo(int empireId)
        {
            return serverState.AllMessages.Where(m => m.Audience == empireId).ToList();
        }

        // ---------------------------------------------------------------- queue cleanup

        [Test]
        public void QueueCleanup_KeepsAutoBuildEntriesInOrder_AndDeletesEverythingElse()
        {
            List<ProductionOrder> orders = FillQueue();

            RandomEventsStep.CleanupQueueAfterDisaster(star);

            CollectionAssert.AreEqual(new[] { orders[1], orders[3] }, star.ManufacturingQueue.Queue);
        }

        // ---------------------------------------------------------------- environment shift

        [Test]
        public void EnvironmentShift_MovesOneAxis_CurrentAndOriginal_MessagesOwnerFirst_AndCleansQueue()
        {
            List<ProductionOrder> orders = FillQueue();
            star.Colonists = 1000; // below the 5,100 protection threshold

            // planet 0, chance 0 (1 in 20), axis 1 (Temperature), 3 + 1 = 4, sign 0 (+).
            var random = new ScriptedRandom(0, 0, 1, 1, 0);
            bool fired = new RandomEventsStep(random).EnvironmentShift(serverState, 0);

            Assert.IsTrue(fired, "No year floor: counter 0 is enough");
            Assert.AreEqual(54, star.Temperature);
            Assert.AreEqual(44, star.OriginalTemperature, "The step is applied to the original value too");
            Assert.AreEqual(50, star.Gravity);
            Assert.AreEqual(50, star.Radiation);
            CollectionAssert.AreEqual(new[] { 1, 20, 3, 3, 2 }, random.MaxValues);
            Assert.AreEqual(1, MessagesTo(owner.Id).Count, "Message 253 goes to the owner");
            StringAssert.Contains("Temperature", MessagesTo(owner.Id)[0].Text);
            Assert.AreEqual(0, MessagesTo(other.Id).Count);
            CollectionAssert.AreEqual(new[] { orders[1], orders[3] }, star.ManufacturingQueue.Queue);
        }

        [Test]
        public void EnvironmentShift_AStepOfThreeIsRedrawnAsSixToEight_AndClampsAtOne()
        {
            star.Colonists = 1000;
            star.Gravity = 5;
            star.OriginalGravity = 9;

            // axis 0 (Gravity), 3 + 0 = 3 -> 6 + 2 = 8, sign 1 (-).
            var random = new ScriptedRandom(0, 0, 0, 0, 2, 1);
            new RandomEventsStep(random).EnvironmentShift(serverState, 0);

            Assert.AreEqual(1, star.Gravity, "5 - 8 clamps to 1");
            Assert.AreEqual(1, star.OriginalGravity, "9 - 8 = 1");
            Assert.AreEqual(0, random.Remaining);
        }

        [Test]
        public void EnvironmentShift_ClampsAt99()
        {
            star.Colonists = 1000;
            star.Radiation = 97;
            star.OriginalRadiation = 90;

            // axis 2 (Radiation), 3 + 2 = 5, sign 0 (+).
            new RandomEventsStep(new ScriptedRandom(0, 0, 2, 2, 0)).EnvironmentShift(serverState, 0);

            Assert.AreEqual(99, star.Radiation);
            Assert.AreEqual(95, star.OriginalRadiation);
        }

        [Test]
        public void EnvironmentShift_ChanceMiss_DrawsThePlanetFirst_AndChangesNothing()
        {
            var random = new ScriptedRandom(0, 1);
            bool fired = new RandomEventsStep(random).EnvironmentShift(serverState, 0);

            Assert.IsFalse(fired);
            CollectionAssert.AreEqual(new[] { 1, 20 }, random.MaxValues, "Planet draw over every star, then the 1-in-20 roll");
            Assert.AreEqual(50, star.Temperature);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [Test]
        public void EnvironmentShift_PopulationProtection_SkipsBigOwnedPlanetsBeforeCounter20()
        {
            star.Colonists = 5100;
            FillQueue();

            bool fired = new RandomEventsStep(new ScriptedRandom(0, 0)).EnvironmentShift(serverState, 19);

            Assert.IsFalse(fired, "Owned, 51+ population units and counter below 20: protected");
            Assert.AreEqual(4, star.ManufacturingQueue.Queue.Count);
            Assert.AreEqual(0, serverState.AllMessages.Count);

            Assert.IsTrue(new RandomEventsStep(new ScriptedRandom(0, 0, 0, 1, 0)).EnvironmentShift(serverState, 20),
                "Counter 20 lifts the protection");
        }

        [Test]
        public void EnvironmentShift_PopulationProtection_NeedsFiftyOneUnits()
        {
            star.Colonists = 5099;

            Assert.IsTrue(new RandomEventsStep(new ScriptedRandom(0, 0, 0, 1, 0)).EnvironmentShift(serverState, 0));
        }

        [Test]
        public void EnvironmentShift_UnownedPlanet_ChangesSilently()
        {
            star.Owner = Global.Nobody;
            star.Colonists = 0;

            Assert.IsTrue(new RandomEventsStep(new ScriptedRandom(0, 0, 0, 1, 0)).EnvironmentShift(serverState, 0));
            Assert.AreEqual(54, star.Gravity);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        // ---------------------------------------------------------------- mineral deposit

        [Test]
        public void MineralDeposit_NeedsCounter10()
        {
            Assert.IsFalse(new RandomEventsStep(new ScriptedRandom(0, 0)).MineralDeposit(serverState, 9, 0));
            Assert.AreEqual(100, star.MineralConcentration.Germanium);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [Test]
        public void MineralDeposit_AddsFiveToNineteen_BelowOneEighty_NoSurfaceNoCleanup()
        {
            FillQueue();

            // planet 0, chance 0, mineral 2 (Germanium), 5 + 14 = 19.
            var random = new ScriptedRandom(0, 0, 2, 14);
            Assert.IsTrue(new RandomEventsStep(random).MineralDeposit(serverState, 10, 0));

            CollectionAssert.AreEqual(new[] { 1, 15, 3, 15 }, random.MaxValues, "Tiny galaxy: 1 in 15");
            Assert.AreEqual(119, star.MineralConcentration.Germanium);
            Assert.AreEqual(100, star.MineralConcentration.Ironium);
            Assert.AreEqual(0, star.ResourcesOnHand.Germanium, "No surface minerals");
            Assert.AreEqual(4, star.ManufacturingQueue.Queue.Count, "The deposit does not run the queue cleanup");
            Assert.AreEqual(1, MessagesTo(owner.Id).Count, "Message 254 to the owner");
            StringAssert.Contains("Germanium", MessagesTo(owner.Id)[0].Text);
        }

        [Test]
        public void MineralDeposit_AtOrAboveOneEighty_ChangesNothingButStillMessagesTheOwner()
        {
            star.MineralConcentration.Boranium = 180;

            var random = new ScriptedRandom(0, 0, 1);
            Assert.IsTrue(new RandomEventsStep(random).MineralDeposit(serverState, 10, 4));

            Assert.AreEqual(11, random.MaxValues[1], "Huge galaxy (s = 4): 1 in 11");
            Assert.AreEqual(180, star.MineralConcentration.Boranium);
            Assert.AreEqual(1, MessagesTo(owner.Id).Count);
        }

        [Test]
        public void MineralDeposit_HasNoPopulationProtection()
        {
            star.Colonists = 1000000;

            Assert.IsTrue(new RandomEventsStep(new ScriptedRandom(0, 0, 0, 0)).MineralDeposit(serverState, 10, 0));
            Assert.AreEqual(105, star.MineralConcentration.Ironium);
        }

        [TestCase(300, 0)]
        [TestCase(400, 0)]
        [TestCase(799, 0)]
        [TestCase(800, 1)]
        [TestCase(1200, 2)]
        [TestCase(1600, 3)]
        [TestCase(2000, 4)]
        [TestCase(9999, 4)]
        public void GalaxySizeIndex_InvertsTheDiameterFormula(int mapWidth, int expected)
        {
            Assert.AreEqual(expected, RandomEventsStep.GalaxySizeIndex(mapWidth));
        }

        // ---------------------------------------------------------------- comet

        [Test]
        public void Comet_NeedsCounter10()
        {
            Assert.IsFalse(new RandomEventsStep(new ScriptedRandom(0, 0)).Comet(serverState, 9));
            Assert.AreEqual(10000, star.Colonists);
        }

        [Test]
        public void Comet_PopulationProtection_AppliesBeforeCounter20()
        {
            star.Colonists = 5100;
            Assert.IsFalse(new RandomEventsStep(new ScriptedRandom(0, 0)).Comet(serverState, 15));
            Assert.AreEqual(5100, star.Colonists);
        }

        [Test]
        public void Comet_Small_HitsOneMineral_ChangesGravityOnly_KillsTwentyFivePercent_TellsEveryone()
        {
            List<ProductionOrder> orders = FillQueue();
            star.MineralConcentration.Boranium = 100;

            var random = new ScriptedRandom(
                0, 0,          // planet, chance
                0,             // size 0 (small)
                2, 1,          // axis-name shuffle: identity
                2, 0,          // mineral permutation: {1, 0, 2} -> Boranium hit
                10, 1000,      // Boranium: +50 + 10 concentration, 3,000 + 1,000 raw
                0, 50, 249,    // base raw: Fe 50, Bo 100, Ge 299
                2, 0);         // Gravity: 3 + 2 = 5, sign +
            Assert.IsTrue(new RandomEventsStep(random).Comet(serverState, 25));
            Assert.AreEqual(0, random.Remaining);

            Assert.AreEqual(7500, star.Colonists, "25% of the colonists killed");
            Assert.AreEqual(160, star.MineralConcentration.Boranium);
            Assert.AreEqual(100, star.MineralConcentration.Ironium, "Only size + 1 = 1 mineral hit");
            Assert.AreEqual(100, star.MineralConcentration.Germanium);
            Assert.AreEqual(50 / 16, star.ResourcesOnHand.Ironium, "Base only: 3 kT");
            Assert.AreEqual(4100 / 16, star.ResourcesOnHand.Boranium, "Hit: (4,000 + 100) / 16 kT");
            Assert.AreEqual(299 / 16, star.ResourcesOnHand.Germanium, "Base only: 18 kT");
            Assert.AreEqual(55, star.Gravity);
            Assert.AreEqual(50, star.OriginalGravity);
            Assert.AreEqual(50, star.Temperature, "A small comet changes Gravity only");
            Assert.AreEqual(40, star.OriginalTemperature);
            Assert.AreEqual(50, star.Radiation);
            CollectionAssert.AreEqual(new[] { orders[1], orders[3] }, star.ManufacturingQueue.Queue);

            Assert.AreEqual(1, MessagesTo(owner.Id).Count);
            StringAssert.Contains("25%", MessagesTo(owner.Id)[0].Text);
            Assert.AreEqual(1, MessagesTo(other.Id).Count, "Every player is told");
            StringAssert.DoesNotContain("%", MessagesTo(other.Id)[0].Text);
        }

        [Test]
        public void Comet_Huge_HitsAllThree_CapsAt200_ShiftsAllAxesSixToTen_KillsEightyFivePercent()
        {
            star.MineralConcentration = new Resources(150, 100, 30, 0);
            star.Gravity = 95;
            star.Temperature = 50;
            star.Radiation = 8;
            star.OriginalRadiation = 8;

            var random = new ScriptedRandom(
                0, 0,                 // planet, chance
                3,                    // size 3 (huge)
                2, 1,                 // axis-name shuffle
                2, 1,                 // mineral permutation: identity (Fe, Bo, Ge)
                49, 14, 16999,        // Fe: +99 +29 = 128 -> 278 capped at 200; 19,999 raw
                0, 0, 0,              // Bo: +50 +15 = 65 -> 165; 3,000 raw
                25, 7, 5000,          // Ge: +75 +22 = 97 -> 127; 8,000 raw
                249, 0, 100,          // base raw: Fe 299, Bo 50, Ge 150
                2, 2, 0,              // Gravity: 5 + 5 = 10, +
                0, 0, 1,              // Temperature: 3 + 3 = 6, -
                1, 2, 1);             // Radiation: 4 + 5 = 9, -
            Assert.IsTrue(new RandomEventsStep(random).Comet(serverState, 25));
            Assert.AreEqual(0, random.Remaining);

            Assert.AreEqual(1500, star.Colonists, "85% killed");
            Assert.AreEqual(200, star.MineralConcentration.Ironium, "Capped at 200");
            Assert.AreEqual(165, star.MineralConcentration.Boranium);
            Assert.AreEqual(127, star.MineralConcentration.Germanium);
            Assert.AreEqual(1268, star.ResourcesOnHand.Ironium, "(19,999 + 299) / 16 = 1,268 kT, the spec's maximum");
            Assert.AreEqual(3050 / 16, star.ResourcesOnHand.Boranium, "(3,000 + 50) / 16 = 190 kT, the spec's minimum for a hit");
            Assert.AreEqual(8150 / 16, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(99, star.Gravity, "95 + 10 clamps to 99");
            Assert.AreEqual(55, star.OriginalGravity);
            Assert.AreEqual(44, star.Temperature);
            Assert.AreEqual(34, star.OriginalTemperature);
            Assert.AreEqual(1, star.Radiation, "8 - 9 clamps to 1");
            Assert.AreEqual(1, star.OriginalRadiation);
        }

        [Test]
        public void Comet_Medium_ChangesGravityAndTemperature_InStorageOrder_WhateverTheMessageNames()
        {
            var random = new ScriptedRandom(
                0, 0,
                1,                    // size 1 (medium): 2 minerals, 2 axes
                0, 0,                 // axis-name shuffle {2,1,0}... the message may name Radiation
                2, 1,                 // minerals: Fe, Bo
                0, 0, 0, 0,           // Fe, Bo: +50, 3,000 raw each
                0, 0, 0,              // base raw
                0, 0,                 // Gravity +3
                0, 0);                // Temperature +3
            Assert.IsTrue(new RandomEventsStep(random).Comet(serverState, 25));
            Assert.AreEqual(0, random.Remaining);

            Assert.AreEqual(5500, star.Colonists, "45% killed");
            Assert.AreEqual(53, star.Gravity);
            Assert.AreEqual(53, star.Temperature);
            Assert.AreEqual(50, star.Radiation, "Radiation is never changed by a medium comet");
            Assert.AreEqual(150, star.MineralConcentration.Ironium);
            Assert.AreEqual(150, star.MineralConcentration.Boranium);
            Assert.AreEqual(100, star.MineralConcentration.Germanium);
            StringAssert.Contains("Radiation", MessagesTo(owner.Id)[0].Text, "Axis names come from the separate shuffle");
        }

        [Test]
        public void Comet_AlternateRealityOwner_LosesNoColonists_AndGetsTheBystanderMessage()
        {
            owner.Race.Traits.SetPrimary("AR");

            // Large comet (size 2): 3 hit minerals x 2 draws, 3 base draws, 3 axes x 2 draws.
            var random = new ScriptedRandom(0, 0, 2, 2, 1, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.IsTrue(new RandomEventsStep(random).Comet(serverState, 25));
            Assert.AreEqual(0, random.Remaining);

            Assert.AreEqual(10000, star.Colonists);
            Assert.AreEqual(1, MessagesTo(owner.Id).Count);
            Assert.AreEqual(MessagesTo(other.Id)[0].Text, MessagesTo(owner.Id)[0].Text);
        }

        [Test]
        public void Comet_UnownedPlanet_EveryPlayerIsToldAndTheQueueIsCleaned()
        {
            star.Owner = Global.Nobody;
            star.Colonists = 0;
            FillQueue();

            var random = new ScriptedRandom(0, 0, 0, 2, 1, 2, 1, 0, 0, 0, 0, 0, 0, 0);
            Assert.IsTrue(new RandomEventsStep(random).Comet(serverState, 10));

            Assert.AreEqual(1, MessagesTo(owner.Id).Count);
            Assert.AreEqual(1, MessagesTo(other.Id).Count);
            Assert.AreEqual(2, star.ManufacturingQueue.Queue.Count);
        }

        // ---------------------------------------------------------------- wrapper

        [Test]
        public void Process_RunsCometThenShiftThenDeposit_EachRollingOnItsOwn()
        {
            GameSettings.Data.MapWidth = 1200; // s = 2: deposit 1 in 13
            serverState.TurnYear = Global.StartingYear + 30;

            var random = new ScriptedRandom(0, 1, 0, 1, 0, 1);
            new RandomEventsStep(random).Process(serverState);

            CollectionAssert.AreEqual(new[] { 1, 20, 1, 20, 1, 13 }, random.MaxValues);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [TestCase(9, false)]
        [TestCase(10, true)]
        public void Process_CounterIsTurnYearMinusStartingYear(int yearsElapsed, bool depositFires)
        {
            serverState.TurnYear = Global.StartingYear + yearsElapsed;

            // Comet misses, shift misses, deposit's roll hits: it fires only from counter 10.
            var script = new List<int> { 0, 1, 0, 1, 0, 0 };
            if (depositFires)
            {
                script.AddRange(new[] { 0, 0 }); // Ironium, +5
            }
            var random = new ScriptedRandom(script.ToArray());
            new RandomEventsStep(random).Process(serverState);

            Assert.AreEqual(0, random.Remaining);
            Assert.AreEqual(depositFires ? 105 : 100, star.MineralConcentration.Ironium);
        }

        [Test]
        public void Process_NoRandomEventsOption_SkipsEverything()
        {
            GameSettings.Data.NoRandomEvents = true;
            serverState.TurnYear = Global.StartingYear + 30;

            var random = new ScriptedRandom();
            new RandomEventsStep(random).Process(serverState);

            Assert.AreEqual(0, random.MaxValues.Count, "No draw at all when the option is set");
        }
    }
}
