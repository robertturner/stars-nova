namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Message 78, the "the named fleet has completed its orders" notice of
    /// behavior-specs-11/turn-generation-engine.md §5a: posted once per fleet per generation when a
    /// fleet whose only remaining waypoint carries a task finishes it, and withdrawn (with nothing
    /// posted) when the fleet leaves play - colonising, being scrapped, being merged into another
    /// fleet, or being absorbed by a Mystery Trader.
    /// </summary>
    [TestFixture]
    public class FleetOrdersNoticeTest
    {
        private static Fleet MakeFleet(long id, ushort owner = 1, bool withShip = true)
        {
            Fleet fleet = new Fleet("Fleet " + id, owner, 0, new NovaPoint(10, 10));
            fleet.Key = id;
            fleet.Owner = owner; // Owner is encoded in the key's bits 25-32.
            if (withShip)
            {
                ShipDesign design = new ShipDesign(id) { Blueprint = new Component { Mass = 100 }, Name = "Scout" };
                Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 100, ArmorStrength = 50 };
                design.Blueprint.Properties.Add("Hull", hull);
                design.Update();
                ShipToken token = new ShipToken(design, 1);
                fleet.Composition.Add(token.Key, token);
            }

            return fleet;
        }

        private static List<Message> Notices(ServerData server)
        {
            return server.AllMessages.Where(m => m.Type == FleetOrdersNotice.MessageType).ToList();
        }

        [Test]
        public void Post_KeepsAtMostOneNoticePerFleetPerGeneration()
        {
            ServerData server = new ServerData();
            Fleet fleet = MakeFleet(1);

            FleetOrdersNotice.Post(server.AllMessages, fleet);
            FleetOrdersNotice.Post(server.AllMessages, fleet);

            Assert.AreEqual(1, Notices(server).Count, "An earlier 78 for the same fleet is withdrawn before the new one.");
            Assert.AreEqual(fleet.Key, Notices(server)[0].Event, "The notice is keyed by the fleet for the withdrawal.");
            Assert.AreEqual(fleet.Owner, Notices(server)[0].Audience);
        }

        [Test]
        public void Post_DoesNotWithdrawAnotherFleetsNotice()
        {
            ServerData server = new ServerData();
            Fleet first = MakeFleet(1);
            Fleet second = MakeFleet(2, owner: 2);

            FleetOrdersNotice.Post(server.AllMessages, first);
            FleetOrdersNotice.Post(server.AllMessages, second);

            Assert.AreEqual(2, Notices(server).Count);
            CollectionAssert.AreEquivalent(new[] { first.Key, second.Key }, Notices(server).Select(m => (long)m.Event).ToList());
        }

        [Test]
        public void Withdraw_RemovesOnlyThatFleetsNotice()
        {
            ServerData server = new ServerData();
            Fleet first = MakeFleet(1);
            Fleet second = MakeFleet(2);
            FleetOrdersNotice.Post(server.AllMessages, first);
            FleetOrdersNotice.Post(server.AllMessages, second);

            FleetOrdersNotice.Withdraw(server.AllMessages, first.Key);

            Assert.AreEqual(1, Notices(server).Count);
            Assert.AreEqual(second.Key, Notices(server)[0].Event);
        }

        [Test]
        public void OnTaskFinished_TaskOnTheOnlyRemainingWaypoint_Posts()
        {
            ServerData server = new ServerData();
            Fleet fleet = MakeFleet(1);

            FleetOrdersNotice.OnTaskFinished(server.AllMessages, new ColoniseTask(), fleet, true, new EmpireData(), null);

            Assert.AreEqual(1, Notices(server).Count);
        }

        [Test]
        public void OnTaskFinished_MoreWaypointsRemain_DoesNotPost()
        {
            ServerData server = new ServerData();
            Fleet fleet = MakeFleet(1);

            FleetOrdersNotice.OnTaskFinished(server.AllMessages, new ColoniseTask(), fleet, false, new EmpireData(), null);

            Assert.AreEqual(0, Notices(server).Count, "A fleet with further orders has not finished them.");
        }

        [Test]
        public void OnTaskFinished_NoTaskOnTheOnlyRemainingWaypoint_DoesNotPost()
        {
            ServerData server = new ServerData();
            Fleet fleet = MakeFleet(1);

            // An ordinary move to a destination carrying no task is not "finished its orders".
            FleetOrdersNotice.OnTaskFinished(server.AllMessages, new NoTask(), fleet, true, new EmpireData(), null);

            Assert.AreEqual(0, Notices(server).Count);
        }

        [Test]
        public void OnTaskFinished_FleetThatLeftPlay_WithdrawsAndPostsNothing()
        {
            ServerData server = new ServerData();
            Fleet fleet = MakeFleet(1);
            FleetOrdersNotice.Post(server.AllMessages, fleet);

            // Scrapping / colonising empties the composition.
            fleet.Composition.Clear();

            FleetOrdersNotice.OnTaskFinished(server.AllMessages, new NoTask(), fleet, true, new EmpireData(), null);

            Assert.AreEqual(0, Notices(server).Count, "A fleet that left play must not be reported as having finished its orders.");
        }

        [Test]
        public void OnTaskFinished_Merge_WithdrawsTheAbsorbedFleetsNotice()
        {
            ServerData server = new ServerData();
            EmpireData empire = new EmpireData { Id = 1, Race = new Race() };
            Fleet survivor = MakeFleet(1);
            Fleet absorbed = MakeFleet(2);
            empire.OwnedFleets.Add(absorbed);

            FleetOrdersNotice.Post(server.AllMessages, absorbed);

            // The merge empties the other fleet's ships; the performing fleet keeps its own.
            absorbed.Composition.Clear();
            SplitMergeTask merge = new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), absorbed.Key);

            FleetOrdersNotice.OnTaskFinished(server.AllMessages, merge, survivor, true, empire, empire);

            Assert.AreEqual(1, Notices(server).Count, "The absorbed fleet's 78 is withdrawn; the survivor may post its own.");
            Assert.AreEqual(survivor.Key, Notices(server)[0].Event);
        }

        // ---------------------------------------------------------------- integration

        /// <summary>Always returns 0, so a capped race's Trader encounter picks the "nothing to
        /// teach" 1-in-5 branch and needs no further reward draws.</summary>
        private sealed class ZeroRandom : Random
        {
            public override int Next(int maxValue) => 0;
            public override int Next() => 0;
            public override int Next(int minValue, int maxValue) => minValue;
            public override double NextDouble() => 0.0;
        }

        [Test]
        public void ScrappingAFleet_WithdrawsItsCompletedOrdersNotice()
        {
            ServerData server = new ServerData();
            EmpireData empire = new EmpireData { Id = 1, Race = new Race() };
            server.AllEmpires.Add(1, empire);

            Fleet fleet = MakeFleet(1);
            fleet.Waypoints.Add(new Waypoint
            {
                Position = fleet.Position,
                Destination = "Space",
                WarpFactor = 0,
                Task = new ScrapTask(),
            });
            empire.AddOrUpdateFleet(fleet);

            FleetOrdersNotice.Post(server.AllMessages, fleet);
            Assert.AreEqual(1, Notices(server).Count);

            new ScrapFleetStep().Process(server);

            Assert.AreEqual(0, Notices(server).Count, "A scrapped fleet must not be reported as having finished its orders.");
        }

        [Test]
        public void MysteryTraderAbsorbingAFleet_WithdrawsItsCompletedOrdersNotice()
        {
            ServerData server = new ServerData { TurnYear = Global.StartingYear + 50 };
            EmpireData human = new EmpireData { Id = 1, Race = new Race { ResearchCosts = new TechLevel(100) } };
            server.AllEmpires.Add(1, human);
            foreach (TechLevel.ResearchField field in MysteryTraderStep.FieldOrder)
            {
                human.ResearchLevels[field] = TechLevel.MaxLevel;
            }

            MysteryTrader trader = new MysteryTrader
            {
                Position = new NovaPoint(100, 100),
                Destination = new NovaPoint(380, 100),
                Speed = 10,
                Item = MysteryTrader.TechnologyItem,
            };
            trader.Key = 1;
            server.AllMysteryTraders.Add(trader.Key, trader);

            Fleet fleet = MakeFleet(1);
            fleet.Position = new NovaPoint(100, 100);
            fleet.Cargo.Ironium = MysteryTraderStep.MinimumMinerals;
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position, Destination = "Space", WarpFactor = 0 });
            human.AddOrUpdateFleet(fleet);

            FleetOrdersNotice.Post(server.AllMessages, fleet);
            Assert.AreEqual(1, Notices(server).Count);

            new MysteryTraderStep(new ZeroRandom()).Process(server);

            Assert.IsFalse(human.OwnedFleets.ContainsKey(fleet.Key), "The trade absorbs the fleet.");
            Assert.AreEqual(0, Notices(server).Count, "An absorbed fleet must not be reported as having finished its orders.");
        }
    }
}
