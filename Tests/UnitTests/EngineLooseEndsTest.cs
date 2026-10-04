namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    using NUnit.Framework;

    /// <summary>
    /// The fixed-amount Load/Unload cargo task (CargoTask Mode/Amount, the form the AI writes)
    /// obeys the same caps as the Transport handler (behavior-specs-10/
    /// fleet-movement-scanning-cargo.md §4, "Amounts" and "Caps on a load"): a load is cut to
    /// the hold's free space, recomputed after every slot in the order ironium, boranium,
    /// germanium, colonists, then to what the planet holds; an unload never exceeds what the
    /// fleet carries.
    /// </summary>
    [TestFixture]
    public class FixedAmountCargoCapsTest
    {
        private EmpireData us;

        [SetUp]
        public void SetUp()
        {
            us = CargoTestKit.Empire(CargoTestKit.Us);
        }

        private static Fleet Freighter(int cargo)
        {
            return CargoTestKit.Fleet(CargoTestKit.Us, 1, CargoTestKit.Design(1, cargo, 0));
        }

        private static CargoTask Load(int ironium = 0, int boranium = 0, int germanium = 0, int colonists = 0)
        {
            CargoTask task = new CargoTask { Mode = CargoMode.Load };
            task.Amount.Ironium = ironium;
            task.Amount.Boranium = boranium;
            task.Amount.Germanium = germanium;
            task.Amount.ColonistsInKilotons = colonists;
            return task;
        }

        private static CargoTask Unload(int ironium = 0, int colonists = 0)
        {
            CargoTask task = new CargoTask { Mode = CargoMode.Unload };
            task.Amount.Ironium = ironium;
            task.Amount.ColonistsInKilotons = colonists;
            return task;
        }

        [Test]
        public void Load_IsCutToTheHoldsFreeSpace()
        {
            Fleet fleet = Freighter(100);
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 500);

            Assert.IsTrue(CargoTestKit.Run(Load(ironium: 300), fleet, star, us));

            Assert.AreEqual(100, fleet.Cargo.Ironium, "The hold holds 100 kT");
            Assert.AreEqual(400, star.ResourcesOnHand.Ironium);
        }

        [Test]
        public void Load_IsCutToWhatThePlanetHolds_NeverTakingItBelowZero()
        {
            Fleet fleet = Freighter(100);
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 30);

            CargoTestKit.Run(Load(ironium: 80), fleet, star, us);

            Assert.AreEqual(30, fleet.Cargo.Ironium);
            Assert.AreEqual(0, star.ResourcesOnHand.Ironium);
        }

        [Test]
        public void Load_EarlierSlotsGetFirstClaimOnTheSharedHold()
        {
            Fleet fleet = Freighter(100);
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 100, boranium: 100);

            CargoTestKit.Run(Load(ironium: 70, boranium: 70), fleet, star, us);

            Assert.AreEqual(70, fleet.Cargo.Ironium);
            Assert.AreEqual(30, fleet.Cargo.Boranium, "Free space is recomputed after the ironium slot");
            Assert.AreEqual(70, star.ResourcesOnHand.Boranium);
        }

        [Test]
        public void Load_ColonistsAreCutToThePlanetsPopulation()
        {
            Fleet fleet = Freighter(100);
            Star star = CargoTestKit.Star(CargoTestKit.Us, colonists: 2500);

            CargoTestKit.Run(Load(colonists: 50), fleet, star, us);

            Assert.AreEqual(25, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(0, star.Colonists);
        }

        [Test]
        public void Unload_NeverExceedsWhatTheFleetCarries()
        {
            Fleet fleet = Freighter(100);
            fleet.Cargo.Ironium = 30;
            fleet.Cargo.ColonistsInKilotons = 10;
            Star star = CargoTestKit.Star(CargoTestKit.Us, colonists: 1000);

            CargoTestKit.Run(Unload(ironium: 100, colonists: 50), fleet, star, us);

            Assert.AreEqual(0, fleet.Cargo.Ironium, "Not -70");
            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(30, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(2000, star.Colonists, "10 kT = 1,000 colonists landed, not 5,000");
        }
    }

    /// <summary>
    /// EmpireData.TemporaryFleets holds fleets a split created until ServerData.CleanupFleets
    /// promotes them; once promoted they must leave the list, or every later cleanup (several
    /// a turn, every turn) re-adds them - resurrecting a fleet that has since merged away, been
    /// destroyed or been scrapped.
    /// </summary>
    [TestFixture]
    public class TemporaryFleetsTest
    {
        [Test]
        public void CleanupFleets_PromotesTemporaryFleets_ThenEmptiesTheList()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = CargoTestKit.Empire(CargoTestKit.Us);
            serverState.AllEmpires.Add(empire.Id, empire);
            Fleet split = CargoTestKit.Fleet(CargoTestKit.Us, 5, CargoTestKit.Design(1, 0, 100));
            empire.TemporaryFleets.Add(split);

            serverState.CleanupFleets();

            Assert.IsTrue(empire.OwnedFleets.ContainsKey(split.Key));
            Assert.IsEmpty(empire.TemporaryFleets);
        }

        [Test]
        public void APromotedFleetThatLaterEmpties_IsNotResurrectedByTheNextCleanup()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = CargoTestKit.Empire(CargoTestKit.Us);
            serverState.AllEmpires.Add(empire.Id, empire);
            Fleet split = CargoTestKit.Fleet(CargoTestKit.Us, 5, CargoTestKit.Design(1, 0, 100));
            empire.TemporaryFleets.Add(split);
            serverState.CleanupFleets();

            // Merged away (or destroyed) later in the same turn.
            split.Composition.Clear();
            serverState.CleanupFleets();

            Assert.IsFalse(empire.OwnedFleets.ContainsKey(split.Key));
        }
    }

    /// <summary>
    /// What an empire's wormhole report carries for the AI's diversion scoring
    /// (behavior-specs-10/ai-opponent-behavior.md §12: "A wormhole this race has not used scores
    /// 90 ... else 50; a known one scores 70 - 10 x its stability tier", "known" being this
    /// race's bit in the wormhole's per-race bitmask): the stability tier seen at the last
    /// detection, and whether this race has used it.
    /// </summary>
    [TestFixture]
    public class WormholeIntelUseAndStabilityTest
    {
        private class FixedRandom : Random
        {
            private readonly int roll;

            public FixedRandom(int roll)
            {
                this.roll = roll;
            }

            public override int Next(int maxValue)
            {
                return roll;
            }

            public override int Next(int minValue, int maxValue)
            {
                return minValue;
            }
        }

        private ServerData serverState;
        private EmpireData observer;
        private EmpireData other;

        [SetUp]
        public void SetUp()
        {
            serverState = new ServerData();
            observer = new EmpireData { Id = 1 };
            other = new EmpireData { Id = 2 };
            serverState.AllEmpires.Add(observer.Id, observer);
            serverState.AllEmpires.Add(other.Id, other);
            observer.EmpireReports.Add(other.Id, new EmpireIntel(other));
            other.EmpireReports.Add(observer.Id, new EmpireIntel(observer));
        }

        private Fleet AddScout(EmpireData owner, long key, int x)
        {
            Component scanner = new Component();
            scanner.Properties.Add("Scanner", new Scanner { NormalScan = 100 });
            Hull hull = new Hull { Modules = new List<HullModule> { new HullModule { AllocatedComponent = scanner, ComponentCount = 1 } }, FuelCapacity = 1000 };
            Component blueprint = new Component { Mass = 100, Name = "Scout" };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Icon = new ShipIcon("hull0000.png", null) };
            design.Update();

            Fleet fleet = new Fleet(key) { Owner = owner.Id, Position = new NovaPoint(x, 0) };
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position });
            owner.OwnedFleets.Add(fleet);
            return fleet;
        }

        private Wormhole AddWormhole(int tier)
        {
            Wormhole wormhole = new Wormhole { Key = 7, PairedKey = 8, Position = new NovaPoint(10, 0), StabilityTier = tier };
            serverState.AllWormholes.Add(wormhole.Key, wormhole);
            return wormhole;
        }

        [Test]
        public void Detection_RecordsTheStabilityTier_AndWhetherThisRaceHasUsedIt()
        {
            AddScout(observer, 100, 0);
            AddScout(other, 101, 0);
            Wormhole wormhole = AddWormhole(4);
            wormhole.UsedBy.Add(observer.Id);

            new ScanStep(new FixedRandom(99)).Process(serverState);

            WormholeIntel seen = observer.WormholeReports[wormhole.Key];
            Assert.AreEqual(4, seen.StabilityTier);
            Assert.IsTrue(seen.UsedByUs);
            Assert.AreEqual(4, other.WormholeReports[wormhole.Key].StabilityTier);
            Assert.IsFalse(other.WormholeReports[wormhole.Key].UsedByUs, "Another race's use is not ours");
        }

        [Test]
        public void UseIsTheRacesOwnKnowledge_RefreshedEvenWhenTheEndIsOutOfRange()
        {
            Fleet scout = AddScout(observer, 100, 0);
            Wormhole wormhole = AddWormhole(2);
            new ScanStep(new FixedRandom(99)).Process(serverState);
            int yearSeen = observer.WormholeReports[wormhole.Key].Year;
            Assert.IsFalse(observer.WormholeReports[wormhole.Key].UsedByUs);

            // The race goes through it, ending far from this end; the tier changes unseen.
            scout.Position = new NovaPoint(5000, 0);
            wormhole.UsedBy.Add(observer.Id);
            wormhole.StabilityTier = 5;
            serverState.TurnYear++;
            new ScanStep(new FixedRandom(99)).Process(serverState);

            WormholeIntel report = observer.WormholeReports[wormhole.Key];
            Assert.IsTrue(report.UsedByUs);
            Assert.AreEqual(2, report.StabilityTier, "The tier is as last seen");
            Assert.AreEqual(yearSeen, report.Year);
        }

        [Test]
        public void UsedByAndTier_SurviveSaveAndReload()
        {
            Wormhole wormhole = new Wormhole { Key = 7, PairedKey = 8, Position = new NovaPoint(10, 0), StabilityTier = 3 };
            wormhole.UsedBy.Add(1);
            wormhole.UsedBy.Add(3);
            XmlDocument xmldoc = new XmlDocument();
            Wormhole reloaded = new Wormhole(wormhole.ToXml(xmldoc));
            Assert.IsTrue(reloaded.IsUsedBy(1));
            Assert.IsTrue(reloaded.IsUsedBy(3));
            Assert.IsFalse(reloaded.IsUsedBy(2));
            Assert.AreEqual(3, reloaded.StabilityTier);

            WormholeIntel intel = new WormholeIntel(wormhole, 2105) { UsedByUs = true };
            WormholeIntel reloadedIntel = new WormholeIntel(intel.ToXml(xmldoc));
            Assert.AreEqual(3, reloadedIntel.StabilityTier);
            Assert.IsTrue(reloadedIntel.UsedByUs);
            Assert.AreEqual(2105, reloadedIntel.Year);

            WormholeIntel unused = new WormholeIntel(new WormholeIntel(wormhole, 2105).ToXml(xmldoc));
            Assert.IsFalse(unused.UsedByUs);
        }
    }
}
