namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Packet follow-ups: the design summary's Mass Driver rating (production-queue.md §10b,
    /// launch rating FUN_1048_5138), the special-object pass order (turn-generation-engine.md §1
    /// step 21) and resting-packet (salvage) decay with its grace flag (§1 steps 18 and 21, §3).
    /// </summary>
    [TestFixture]
    public class PacketFollowUpsTest
    {
        // ================================================================ Mass Driver summary

        private static ShipDesign DriverDesign(params (int Warp, int Count)[] slots)
        {
            ShipDesign design = new ShipDesign(1);
            design.Blueprint = new Component();
            Hull hull = new Hull { Modules = new List<HullModule>() };
            foreach ((int warp, int count) in slots)
            {
                Component driver = new Component { Name = "Mass Driver " + warp };
                driver.Properties.Add("Mass Driver", new MassDriver(warp));
                hull.Modules.Add(new HullModule { AllocatedComponent = driver, ComponentCount = count });
            }

            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            return design;
        }

        private static int SummaryRating(ShipDesign design)
        {
            return ((MassDriver)design.Summary.Properties["Mass Driver"]).Value;
        }

        [TestCase(7, 1, 7)]
        [TestCase(7, 3, 7)]
        [TestCase(13, 2, 13)]
        public void OneSlot_IsTheDriversWarp_HoweverManyItHolds(int warp, int count, int expected)
        {
            // The +1 is for the best rating in two DIFFERENT slots, not several in one slot.
            Assert.AreEqual(expected, SummaryRating(DriverDesign((warp, count))));
        }

        [Test]
        public void TwoSlotsAtTheBestRating_AddOne()
        {
            Assert.AreEqual(8, SummaryRating(DriverDesign((7, 1), (7, 1))));
            Assert.AreEqual(8, SummaryRating(DriverDesign((7, 1), (7, 1), (7, 2))), "three slots still add only one");
        }

        [Test]
        public void TheBonusOnlyCountsSlotsAtTheBestRating_InAnyOrder()
        {
            Assert.AreEqual(8, SummaryRating(DriverDesign((7, 1), (7, 1), (8, 1))), "best 8 in one slot");
            Assert.AreEqual(8, SummaryRating(DriverDesign((8, 1), (7, 1), (7, 1))));
            Assert.AreEqual(9, SummaryRating(DriverDesign((8, 1), (7, 1), (8, 1))));
            Assert.AreEqual(10, SummaryRating(DriverDesign((5, 1), (10, 4))));
        }

        [Test]
        public void TheSummary_AgreesWithTheLaunchRating()
        {
            foreach (ShipDesign design in new[] { DriverDesign((7, 1)), DriverDesign((7, 2), (7, 1)), DriverDesign((7, 1), (7, 1), (8, 3)) })
            {
                Fleet starbase = new Fleet(1);
                ShipToken token = new ShipToken(design, 1);
                starbase.Composition.Add(token.Key, token);
                Assert.AreEqual(MineralPacketRules.LaunchRating(starbase), SummaryRating(design));
            }
        }

        // ================================================================ step order

        private static int KeyOf<T>(TurnGenerator generator)
        {
            SortedList<int, ITurnStep> steps = (SortedList<int, ITurnStep>)typeof(TurnGenerator)
                .GetField("turnSteps", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(generator);
            return steps.Single(step => step.Value is T).Key;
        }

        private static int KeyOf(TurnGenerator generator, string stepTypeName)
        {
            SortedList<int, ITurnStep> steps = (SortedList<int, ITurnStep>)typeof(TurnGenerator)
                .GetField("turnSteps", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(generator);
            return steps.Single(step => step.Value.GetType().Name == stepTypeName).Key;
        }

        [Test]
        public void WormholeRelocation_RunsAfterTheProductionHub_AndBeforeTheBattle()
        {
            TurnGenerator generator = new SimpleTurnGenerator(new ServerData());

            // Keys below the bombing step (19) run before the battle; the production hub is 11-15.
            int wormholes = KeyOf<WormholeDriftStep>(generator);
            Assert.Less(wormholes, KeyOf(generator, "BombingStep"), "step 21 precedes the battle (23)");
            Assert.Greater(wormholes, KeyOf<RandomEventsStep>(generator), "step 21 follows the production hub (20)");
            Assert.Greater(wormholes, KeyOf<StarUpdateStep>(generator));
        }

        [Test]
        public void RestingPacketDecay_IsStep18_BeforeTheProductionHubAndTheBattle()
        {
            TurnGenerator generator = new SimpleTurnGenerator(new ServerData());

            int decay = KeyOf<DeepSpaceMineralDecayStep>(generator);
            Assert.Less(decay, KeyOf<ColonistBreedingStep>(generator), "step 18 precedes step 19");
            Assert.Less(decay, KeyOf<RemoteMiningStep>(generator));
            Assert.Less(decay, KeyOf(generator, "BombingStep"));
        }

        // ================================================================ salvage decay

        private static ServerData WithWreckage(Resources minerals)
        {
            ServerData serverState = new ServerData();
            BattleEngine.AddWreckage(serverState, new NovaPoint(500, 500), minerals);
            return serverState;
        }

        [Test]
        public void NewWreckage_SpendsItsGraceYear_ThenDecaysTenPercentRoundedDown()
        {
            ServerData serverState = WithWreckage(new Resources(1095, 50, 200, 0));
            DeepSpaceMinerals wreck = serverState.AllDeepSpaceMinerals.Values.Single();
            Assert.IsTrue(wreck.DecayGrace, "the wreckage routine sets the grace flag");

            new DeepSpaceMineralDecayStep().Process(serverState);
            Assert.AreEqual(1095, wreck.Minerals.Ironium, "the first pass only clears the flag");
            Assert.IsFalse(wreck.DecayGrace);

            new DeepSpaceMineralDecayStep().Process(serverState);
            Assert.AreEqual(1095 - 109, wreck.Minerals.Ironium, "10% of 1,095 rounded down is 109");
            Assert.AreEqual(40, wreck.Minerals.Boranium, "at least 10 kT");
            Assert.AreEqual(180, wreck.Minerals.Germanium);
        }

        [Test]
        public void Wreckage_IsDeletedAtZero()
        {
            ServerData serverState = WithWreckage(new Resources(15, 0, 0, 0));
            DeepSpaceMineralDecayStep step = new DeepSpaceMineralDecayStep();

            step.Process(serverState);
            step.Process(serverState);
            Assert.AreEqual(5, serverState.AllDeepSpaceMinerals.Values.Single().Minerals.Ironium);

            step.Process(serverState);
            Assert.AreEqual(0, serverState.AllDeepSpaceMinerals.Count);
        }

        [Test]
        public void TheGraceFlag_SurvivesSaveAndLoad()
        {
            DeepSpaceMinerals wreck = new DeepSpaceMinerals(new NovaPoint(1, 2)) { Minerals = new Resources(100, 0, 0, 0), DecayGrace = true };
            XmlDocument xml = new XmlDocument();
            DeepSpaceMinerals reloaded = new DeepSpaceMinerals(wreck.ToXml(xml));
            Assert.IsTrue(reloaded.DecayGrace);

            wreck.DecayGrace = false;
            Assert.IsFalse(new DeepSpaceMinerals(wreck.ToXml(xml)).DecayGrace);
        }
    }
}
