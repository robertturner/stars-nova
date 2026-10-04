namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    // behavior-specs-10/fleet-movement-scanning-cargo.md §4, "Colonist unload outcomes, complete
    // table", Unload task column, driven through CargoTask exactly as TurnGenerator.UpdateFleet
    // does (IsValid, then Perform):
    //   own planet                        - ordinary transfer
    //   another race's planet, no starbase - invasion (relations never consulted)
    //   another race's planet, starbase    - refused (309), colonists stay aboard
    //   unowned planet                     - refused (85), colonists stay aboard
    //   Alternate Reality, not its own     - refused (86), colonists stay aboard
    // Only a colonist unload reaches the invasion code: minerals may be unloaded onto any planet,
    // and a load from another race's planet is blocked (no cargo-theft ability in this port).
    [TestFixture]
    public class ColonistUnloadOutcomeTest
    {
        private EmpireData us;
        private EmpireData them;
        private Fleet fleet;

        [SetUp]
        public void Init()
        {
            us = new EmpireData { Id = 1, Race = new Race() };
            them = new EmpireData { Id = 2, Race = new Race() };
            us.EmpireReports.Add(them.Id, new EmpireIntel(them) { Relation = PlayerRelation.Friend });
            them.EmpireReports.Add(us.Id, new EmpireIntel(us) { Relation = PlayerRelation.Friend });

            fleet = new Fleet(1) { Owner = us.Id, Name = "Transport" };

            // A 200 kT hold: loads are cut to free space (§4, "Caps on a load").
            ShipToken freighter = new ShipToken(CargoTestKit.Design(1, 200, 0), 1);
            fleet.Composition.Add(freighter.Key, freighter);
            fleet.Cargo.ColonistsInKilotons = 50;
            fleet.Cargo.Ironium = 30;
        }

        private Star Planet(EmpireData owner, int colonists, bool starbase = false)
        {
            Star star = new Star { Name = "Target", Owner = owner == null ? (ushort)Global.Nobody : owner.Id,Colonists = colonists };
            if (owner != null)
            {
                owner.OwnedStars.Add(star);
            }

            if (starbase)
            {
                star.Starbase = new Fleet(99);
            }

            fleet.InOrbit = star;
            return star;
        }

        private static CargoTask Unload(int ironium, int colonistKilotons)
        {
            CargoTask task = new CargoTask { Mode = CargoMode.Unload };
            task.Amount.Ironium = ironium;
            task.Amount.ColonistsInKilotons = colonistKilotons;
            return task;
        }

        private bool Run(CargoTask task, Star star, EmpireData receiver)
        {
            if (!task.IsValid(fleet, star, us, receiver))
            {
                return false;
            }

            return task.Perform(fleet, star, us, receiver);
        }

        [Test]
        public void OwnPlanet_IsAnOrdinaryTransfer()
        {
            Star star = Planet(us, 1000);

            Run(Unload(30, 50), star, us);

            Assert.AreEqual(6000, star.Colonists);
            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(30, star.ResourcesOnHand.Ironium);
        }

        /// <summary>Relations are never consulted: unloading onto a FRIEND's planet without a
        /// starbase is an invasion like any other (110%: 5,000 troops x 1.1 = 5,500 against 1,000
        /// defenders).</summary>
        [Test]
        public void FriendsPlanetWithoutAStarbase_IsInvaded()
        {
            Star star = Planet(them, 1000);

            Run(Unload(0, 50), star, them);

            Assert.AreEqual(us.Id, star.Owner, "The invasion succeeds; friendship does not cancel it");
            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(30, fleet.Cargo.Ironium, "Only the ordered colonists landed");
        }

        [Test]
        public void ForeignPlanetWithAStarbase_IsRefused_ColonistsStayAboard_MineralsStillUnload()
        {
            Star star = Planet(them, 1000, starbase: true);

            Run(Unload(30, 50), star, them);

            Assert.AreEqual(them.Id, star.Owner);
            Assert.AreEqual(1000, star.Colonists);
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons, "Message 309: the colonists stay aboard");
            Assert.AreEqual(30, star.ResourcesOnHand.Ironium, "Minerals precede colonists in slot order and are not refused");
            Assert.AreEqual(0, fleet.Cargo.Ironium);
        }

        [Test]
        public void UnownedPlanet_IsRefused_ColonistsStayAboard()
        {
            Star star = Planet(null, 0);

            Run(Unload(30, 50), star, null);

            Assert.AreEqual(Global.Nobody, star.Owner, "Nobody colonises by unloading");
            Assert.AreEqual(0, star.Colonists);
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons, "Message 85: the colonists stay aboard");
            Assert.AreEqual(30, star.ResourcesOnHand.Ironium, "Minerals may be dropped on an unowned planet");
        }

        [Test]
        public void AlternateReality_OnAnotherRacesPlanet_IsRefused_ColonistsStayAboard()
        {
            us.Race.Traits.SetPrimary("AR");
            Star star = Planet(them, 1000);

            Run(Unload(0, 50), star, them);

            Assert.AreEqual(them.Id, star.Owner);
            Assert.AreEqual(1000, star.Colonists);
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons, "Message 86: refused, not destroyed");
        }

        /// <summary>A mineral-only unload at another race's planet never reaches the invasion
        /// code, even with colonists aboard.</summary>
        [Test]
        public void MineralOnlyUnload_AtAForeignPlanet_DoesNotInvade()
        {
            Star star = Planet(them, 1000);

            Run(Unload(30, 0), star, them);

            Assert.AreEqual(them.Id, star.Owner);
            Assert.AreEqual(1000, star.Colonists);
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(30, star.ResourcesOnHand.Ironium, "The minerals are a gift");
        }

        [Test]
        public void LoadAtAForeignPlanet_IsBlocked_AndDoesNotInvade()
        {
            Star star = Planet(them, 1000);
            star.ResourcesOnHand.Ironium = 100;
            CargoTask task = new CargoTask { Mode = CargoMode.Load };
            task.Amount.Ironium = 50;

            bool done = Run(task, star, them);

            Assert.IsFalse(done);
            Assert.AreEqual(them.Id, star.Owner);
            Assert.AreEqual(1000, star.Colonists);
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(30, fleet.Cargo.Ironium);
            Assert.AreEqual(100, star.ResourcesOnHand.Ironium);
        }

        [Test]
        public void LoadAtAnUnownedPlanet_IsAllowed()
        {
            Star star = Planet(null, 0);
            star.ResourcesOnHand.Ironium = 100;
            CargoTask task = new CargoTask { Mode = CargoMode.Load };
            task.Amount.Ironium = 50;

            Assert.IsTrue(Run(task, star, null));
            Assert.AreEqual(80, fleet.Cargo.Ironium);
            Assert.AreEqual(50, star.ResourcesOnHand.Ironium);
        }
    }
}
