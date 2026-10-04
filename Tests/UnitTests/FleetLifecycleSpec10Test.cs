#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    using NUnit.Framework;

    /// <summary>
    /// Fleet lifecycle (behavior-specs-10/fleet-movement-scanning-cargo.md §4): a new fleet takes
    /// the smallest id its owner is not using; Merge Fleets recombines scanning as the maximum.
    /// </summary>
    [TestFixture]
    public class FleetIdAllocationTest
    {
        private static EmpireData EmpireWithFleets(params uint[] ids)
        {
            EmpireData empire = CargoTestKit.Empire(3);
            ShipDesign design = CargoTestKit.Design(1, 10, 10);
            foreach (uint id in ids)
            {
                empire.AddOrUpdateFleet(CargoTestKit.Fleet(3, id, design));
            }

            return empire;
        }

        [Test]
        public void NewFleet_TakesTheSmallestUnusedId()
        {
            EmpireData empire = EmpireWithFleets(1, 2, 3, 5);

            Assert.AreEqual(4u, empire.GetNextFleetKey().Id());
        }

        [Test]
        public void AFreedId_IsReused()
        {
            EmpireData empire = EmpireWithFleets(1, 2, 3);
            empire.RemoveFleet(empire.OwnedFleets.Values.First(f => f.Id == 2));

            long key = empire.GetNextFleetKey();
            Assert.AreEqual(2u, key.Id());
            Assert.AreEqual((ushort)3, key.Owner());
        }

        [Test]
        public void TwoKeysIssuedBeforeEitherFleetIsAdded_NeverCollide()
        {
            EmpireData empire = EmpireWithFleets(1, 3);

            long first = empire.GetNextFleetKey();
            long second = empire.GetNextFleetKey();

            Assert.AreEqual(2u, first.Id());
            Assert.AreEqual(4u, second.Id());
        }

        [Test]
        public void AnEmptyEmpire_StartsAtOne()
        {
            Assert.AreEqual(1u, CargoTestKit.Empire(4).GetNextFleetKey().Id());
        }

        [Test]
        public void MergedFleet_ScanRangesAreTheMaximumOfTheMergedShips()
        {
            Component rhino = Spec10CombatKit.Real("Rhino Scanner");   // 50 ly
            Component possum = Spec10CombatKit.Real("Possum Scanner"); // 150 ly
            Fleet left = CargoTestKit.Fleet(1, 1, CargoTestKit.Design(1, 0, 100, rhino));
            Fleet right = CargoTestKit.Fleet(1, 2, CargoTestKit.Design(2, 0, 100, possum));
            left.FuelAvailable = left.TotalFuelCapacity;
            right.FuelAvailable = right.TotalFuelCapacity;

            EmpireData empire = CargoTestKit.Empire(1);
            empire.AddOrUpdateFleet(left);
            empire.AddOrUpdateFleet(right);

            SplitMergeTask merge = new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), right.Key);
            merge.Perform(left, right, empire, empire);

            Assert.AreEqual(2, left.Composition.Values.Sum(t => t.Quantity));
            Assert.AreEqual(150, left.ScanRange);
        }
    }

    /// <summary>
    /// The Multi Cargo Pod is the third cargo-pod tier: +250 kT per pod (§4, "Cargo-pod and
    /// fuel-pod bonuses": Mechanical subtypes 2/3/4 give +50/+100/+250, times quantity).
    /// </summary>
    [TestFixture]
    public class CargoPodTierTest
    {
        [TestCase("Cargo Pod", 50)]
        [TestCase("Super Cargo Pod", 100)]
        [TestCase("Multi Cargo Pod", 250)]
        public void EachPodAddsItsBonusTimesQuantity(string pod, int bonus)
        {
            Component part = Spec10CombatKit.Real(pod);
            Component blueprint = new Component { Mass = 50 };
            Hull hull = new Hull { Modules = new List<HullModule>(), BaseCargo = 70, FuelCapacity = 100, ArmorStrength = 10 };
            hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = 2 });
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update();

            Assert.AreEqual(70 + (2 * bonus), design.CargoCapacity);
        }
    }

    /// <summary>
    /// Transfer Fleet (task 9; §5 task table row 9): after movement and the battle, the fleet is
    /// given to the recipient - refused while it carries cargo or when the recipient already has
    /// 512 fleets - as a new fleet of the recipient's at the same place, reusing an identical
    /// design of the recipient's or copying the design across; the source fleet is removed.
    /// </summary>
    [TestFixture]
    public class TransferFleetTest
    {
        private EmpireData giver;
        private EmpireData taker;

        [SetUp]
        public void SetUp()
        {
            giver = CargoTestKit.Empire(CargoTestKit.Us);
            taker = CargoTestKit.Empire(CargoTestKit.Them);
        }

        private Fleet GiftFleet(int quantity = 3)
        {
            ShipDesign design = CargoTestKit.Design(((long)CargoTestKit.Us << 32) | 5, 100, 400, CargoTestKit.Named("Thing"));
            giver.Designs[design.Key] = design;
            Fleet fleet = CargoTestKit.Fleet(CargoTestKit.Us, 1, design, 40, 60, quantity);
            fleet.FuelAvailable = 250;
            giver.AddOrUpdateFleet(fleet);
            return fleet;
        }

        [Test]
        public void Transfer_CreatesTheRecipientsFleet_AndRemovesTheSource()
        {
            Fleet fleet = GiftFleet();
            TransferFleetTask task = new TransferFleetTask(CargoTestKit.Them);

            Fleet gift = task.Transfer(fleet, giver, taker);

            Assert.IsNotNull(gift);
            Assert.AreEqual(CargoTestKit.Them, gift.Owner);
            Assert.IsTrue(taker.OwnedFleets.ContainsKey(gift.Key));
            Assert.IsFalse(giver.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(new NovaPoint(40, 60), gift.Position);
            Assert.AreEqual(3, gift.Composition.Values.Sum(t => t.Quantity));
            Assert.AreEqual(250, gift.FuelAvailable, 0.001);

            ShipDesign copied = gift.Composition.Values.Single().Design;
            Assert.AreEqual(CargoTestKit.Them, copied.Owner, "The design is copied to the recipient");
            Assert.IsTrue(taker.Designs.ContainsKey(copied.Key));
            Assert.AreEqual(1, task.Messages.Count(m => m.Audience == CargoTestKit.Us));
            Assert.AreEqual(1, task.Messages.Count(m => m.Audience == CargoTestKit.Them));
        }

        [Test]
        public void Transfer_ReusesTheRecipientsIdenticalDesign()
        {
            Fleet fleet = GiftFleet();
            ShipDesign theirs = CargoTestKit.Design(((long)CargoTestKit.Them << 32) | 9, 100, 400, CargoTestKit.Named("Thing"));
            taker.Designs[theirs.Key] = theirs;

            Fleet gift = new TransferFleetTask(CargoTestKit.Them).Transfer(fleet, giver, taker);

            Assert.AreSame(theirs, gift.Composition.Values.Single().Design);
            Assert.AreEqual(1, taker.Designs.Count);
        }

        [Test]
        public void Transfer_IsRefused_WhileTheFleetCarriesColonists()
        {
            // Only colonists block (fleet-movement-scanning-cargo.md §5, "Transfer Fleet (9)").
            Fleet fleet = GiftFleet();
            fleet.Cargo.ColonistsInKilotons = 1;

            Assert.IsNull(new TransferFleetTask(CargoTestKit.Them).Transfer(fleet, giver, taker));
            Assert.IsTrue(giver.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(0, taker.OwnedFleets.Count);
        }

        [Test]
        public void Transfer_CarriesMineralsAndFuel()
        {
            // Minerals and fuel do not block; they go with the ships.
            Fleet fleet = GiftFleet();
            fleet.Cargo.Ironium = 10;
            fleet.Cargo.Boranium = 20;
            fleet.Cargo.Germanium = 30;

            Fleet gift = new TransferFleetTask(CargoTestKit.Them).Transfer(fleet, giver, taker);

            Assert.IsNotNull(gift);
            Assert.AreEqual(10, gift.Cargo.Ironium);
            Assert.AreEqual(20, gift.Cargo.Boranium);
            Assert.AreEqual(30, gift.Cargo.Germanium);
        }

        [Test]
        public void Transfer_IsRefused_ToAComputerPlayer()
        {
            Fleet fleet = GiftFleet();

            Assert.IsNull(new TransferFleetTask(CargoTestKit.Them).Transfer(fleet, giver, taker, recipientIsComputer: true));
            Assert.IsTrue(giver.OwnedFleets.ContainsKey(fleet.Key));
        }

        [Test]
        public void Transfer_IsRefused_WhenTheRecipientRatesTheGiverEnemy()
        {
            Fleet fleet = GiftFleet();
            taker.EmpireReports.Add(giver.Id, new EmpireIntel(giver) { Relation = PlayerRelation.Enemy });

            Assert.IsNull(new TransferFleetTask(CargoTestKit.Them).Transfer(fleet, giver, taker));
            Assert.IsTrue(giver.OwnedFleets.ContainsKey(fleet.Key));
        }

        [Test]
        public void Transfer_IsRefused_WhenTheRecipientHasNoFreeDesignSlot()
        {
            Fleet fleet = GiftFleet();
            for (int i = 0; i < Global.MaxDesignsAmount; i++)
            {
                ShipDesign filler = CargoTestKit.Design(100 + i, 10, 10, CargoTestKit.Named("Filler" + i));
                taker.Designs[filler.Key] = filler;
            }

            TransferFleetTask task = new TransferFleetTask(CargoTestKit.Them);
            Assert.IsNull(task.Transfer(fleet, giver, taker));
            Assert.AreEqual(Global.MaxDesignsAmount, taker.Designs.Count);
            Assert.IsTrue(task.Messages.Any(m => m.Audience == CargoTestKit.Us && m.Text.Contains("design slot")));
            Assert.IsTrue(task.Messages.Any(m => m.Audience == CargoTestKit.Them && m.Text.Contains("design slot")));
        }

        [Test]
        public void Transfer_FlagsACopiedDesignAsFailedLegality()
        {
            Fleet fleet = GiftFleet();

            Fleet gift = new TransferFleetTask(CargoTestKit.Them).Transfer(fleet, giver, taker);

            Assert.IsNotNull(gift);
            Assert.IsTrue(gift.Composition.Values.Single().Design.FailedLegality,
                "a design copied across gets the +0x7c bit 0x80 failed-legality flag");
        }

        [Test]
        public void Transfer_IsRefused_WhenTheRecipientHas512Fleets()
        {
            ShipDesign design = CargoTestKit.Design(2, 10, 10);
            for (uint id = 1; id <= Global.MaxFleetAmount; id++)
            {
                taker.AddOrUpdateFleet(CargoTestKit.Fleet(CargoTestKit.Them, id, design));
            }

            Fleet fleet = GiftFleet();
            TransferFleetTask task = new TransferFleetTask(CargoTestKit.Them);

            Assert.IsNull(task.Transfer(fleet, giver, taker));
            Assert.AreEqual(Global.MaxFleetAmount, taker.OwnedFleets.Count);
            Assert.IsTrue(giver.OwnedFleets.ContainsKey(fleet.Key));
            Assert.IsTrue(task.Messages.Any(m => m.Text.Contains("512")));

            taker.RemoveFleet(taker.OwnedFleets.Values.First());
            Assert.IsNotNull(new TransferFleetTask(CargoTestKit.Them).Transfer(fleet, giver, taker), "511 fleets leave room");
        }

        [Test]
        public void Transfer_IsRefused_ToOneselfOrAnUnknownRace()
        {
            Fleet fleet = GiftFleet();

            Assert.IsNull(new TransferFleetTask(CargoTestKit.Us).Transfer(fleet, giver, giver));
            Assert.IsNull(new TransferFleetTask(77).Transfer(fleet, giver, null));
            Assert.IsTrue(giver.OwnedFleets.ContainsKey(fleet.Key));
        }

        [Test]
        public void Task_RoundTripsThroughTheWaypointXml()
        {
            Waypoint waypoint = new Waypoint { Position = new NovaPoint(0, 0), Destination = "X", Task = new TransferFleetTask(2) };
            Waypoint loaded = new Waypoint(waypoint.ToXml(new XmlDocument()));

            Assert.IsInstanceOf<TransferFleetTask>(loaded.Task);
            Assert.AreEqual((ushort)2, ((TransferFleetTask)loaded.Task).RecipientId);
        }
    }

    /// <summary>
    /// The Transport and Transfer Fleet tasks inside a whole turn: an unfinished Transport task
    /// holds the fleet on its waypoint and tries again next year; Transfer Fleet runs after the
    /// battle and settles its task.
    /// </summary>
    [TestFixture]
    public class CargoTurnIntegrationTest
    {
        private ServerData serverData;
        private EmpireData us;
        private EmpireData them;

        [SetUp]
        public void SetUp()
        {
            serverData = new SimpleServerData();
            us = CargoTestKit.Empire(CargoTestKit.Us);
            them = CargoTestKit.Empire(CargoTestKit.Them);
            serverData.AllEmpires.Add(us.Id, us);
            serverData.AllEmpires.Add(them.Id, them);
            us.EmpireReports.Add(them.Id, new EmpireIntel(them));
            them.EmpireReports.Add(us.Id, new EmpireIntel(us));
        }

        private Star AddStar(string name, ushort owner, int x, int y)
        {
            Star star = new Star { Name = name, Position = new NovaPoint(x, y) };
            star.Owner = owner;
            star.ResourcesOnHand = new Resources();
            serverData.AllStars.Add(star.Key, star);
            if (owner == CargoTestKit.Us)
            {
                us.OwnedStars.Add(star);
            }

            return star;
        }

        private Fleet AddFleet(Star at)
        {
            ShipDesign design = CargoTestKit.Design(((long)CargoTestKit.Us << 32) | 1, 100, 1000);
            design.Icon = new ShipIcon("hull0000.png", null);
            us.Designs[design.Key] = design;
            Fleet fleet = CargoTestKit.Fleet(CargoTestKit.Us, 1, design, at.Position.X, at.Position.Y);
            fleet.Waypoints[0].Destination = at.Name;
            fleet.InOrbit = at;
            fleet.FuelAvailable = 1000;
            us.AddOrUpdateFleet(fleet);
            return fleet;
        }

        private void Generate()
        {
            new SimpleTurnGenerator(serverData).Generate();
        }

        [Test]
        public void WaitForPercent_HoldsTheFleetUntilTheLevelIsReached_ThenItMovesOn()
        {
            Star home = AddStar("Home", Global.Nobody, 0, 0);
            AddStar("Away", Global.Nobody, 30, 0);
            Fleet fleet = AddFleet(home);

            fleet.Waypoints[0].Task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.WaitForPercent, 100));
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(30, 0), Destination = "Away", WarpFactor = 6 });

            Generate();
            Assert.AreEqual(new NovaPoint(0, 0), fleet.Position, "Waiting for a full hold: the fleet stays");
            Assert.IsInstanceOf<CargoTask>(fleet.Waypoints[0].Task);

            home.ResourcesOnHand.Ironium = 1000;
            Generate();
            Assert.AreEqual(100, fleet.Cargo.Ironium);
            Assert.AreEqual(new NovaPoint(30, 0), fleet.Position, "Loaded, so it goes on the same year");
        }

        [Test]
        public void TransferFleet_HappensInTheTurn_AndSettlesTheTask()
        {
            Star home = AddStar("Home", Global.Nobody, 0, 0);
            Fleet fleet = AddFleet(home);
            fleet.Waypoints[0].Task = new TransferFleetTask(CargoTestKit.Them);

            Generate();

            Assert.IsFalse(us.OwnedFleets.ContainsKey(fleet.Key));
            Fleet gift = them.OwnedFleets.Values.Single();
            Assert.AreEqual(new NovaPoint(0, 0), gift.Position);
            Assert.IsInstanceOf<NoTask>(gift.Waypoints[0].Task);
        }

        [Test]
        public void TransferFleet_AfterArriving_HappensTheSameYear()
        {
            Star home = AddStar("Home", Global.Nobody, 0, 0);
            AddStar("Away", Global.Nobody, 30, 0);
            Fleet fleet = AddFleet(home);
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(30, 0), Destination = "Away", WarpFactor = 6, Task = new TransferFleetTask(CargoTestKit.Them) });

            Generate();

            Assert.IsFalse(us.OwnedFleets.ContainsKey(fleet.Key));
            Assert.AreEqual(new NovaPoint(30, 0), them.OwnedFleets.Values.Single().Position);
        }
    }
}
