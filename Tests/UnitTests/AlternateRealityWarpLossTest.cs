namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    // behavior-specs-9/fleet-movement-scanning-cargo.md §1 "Alternate Reality colonist loss"
    // (message 193): an AR fleet that sets out this turn (at least two waypoints, first task not
    // Transport / Lay Mines, leg warp non-zero and not a Stargate leg) carrying C > 10 units of
    // colonists (1 unit = 1 kT = 100 colonists) loses floor((C + 11) x 3 / 100) units, at any warp,
    // once per turn; a loss of 0 posts no message.
    [TestFixture]
    public class AlternateRealityWarpLossTest
    {
        private ServerData serverState;
        private EmpireData empire;
        private Star home;
        private Star far;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            empire = new SimpleEmpireData { Id = 1, Race = new Race() };
            empire.Race.Traits.SetPrimary("AR");
            serverState.AllEmpires.Add(empire.Id, empire);

            home = new Star { Name = "Home", Owner = empire.Id, Position = new NovaPoint(0, 0) };
            far = new Star { Name = "Far", Owner = Global.Nobody, Position = new NovaPoint(1000, 0) };
            serverState.AllStars.Add(home.Key, home);
            serverState.AllStars.Add(far.Key, far);
        }

        private Fleet MakeColonyFleet(int colonistUnits, int legWarp)
        {
            Component blueprint = new Component { Name = "Colony Ship", Mass = 20 };
            // FuelCapacity > 0: a hull with no fuel tank counts as a starbase here (Hull.IsStarbase).
            Hull hull = new Hull { ArmorStrength = 20, BaseCargo = 1000, FuelCapacity = 100 };
            hull.Modules = new List<HullModule>();
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint, Name = "Colony Ship" };
            design.Update();

            Fleet fleet = new Fleet(1) { Owner = empire.Id, Name = "Colony Fleet" };
            fleet.Composition.Add(design.Key, new ShipToken(design, 1));
            fleet.Position = home.Position;
            fleet.InOrbit = home;
            fleet.Cargo.ColonistsInKilotons = colonistUnits;

            // Waypoint 0 is the fleet's current position; waypoint 1 is the leg it sets out on.
            fleet.Waypoints.Add(new Waypoint { Position = home.Position, Destination = home.Name, Task = new NoTask(), WarpFactor = 0 });
            fleet.Waypoints.Add(new Waypoint { Position = far.Position, Destination = far.Name, Task = new NoTask(), WarpFactor = legWarp });

            empire.AddOrUpdateFleet(fleet);
            return fleet;
        }

        private void RunMovement(Fleet fleet)
        {
            SimpleTurnGenerator generator = new SimpleTurnGenerator(serverState);
            typeof(TurnGenerator)
                .GetMethod("ProcessFleet", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(generator, new object[] { fleet });
        }

        private bool HasLossMessage()
        {
            return serverState.AllMessages.Exists(m => m.Type == "Warp Acceleration");
        }

        [Test]
        public void HundredUnits_LoseThreeUnits_WithMessage()
        {
            Fleet fleet = MakeColonyFleet(100, 5);

            RunMovement(fleet);

            Assert.AreEqual(97, fleet.Cargo.ColonistsInKilotons, "(100 + 11) x 3 / 100 = 3 units");
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Type == "Warp Acceleration" && m.Text.Contains("300 of your colonists")));
        }

        [Test]
        public void FourHundredUnits_LoseTwelve_AtWarpOne()
        {
            Fleet fleet = MakeColonyFleet(400, 1);

            RunMovement(fleet);

            Assert.AreEqual(388, fleet.Cargo.ColonistsInKilotons, "(400 + 11) x 3 / 100 = 12, at any warp");
        }

        [Test]
        public void TwentyThreeUnits_LoseOne()
        {
            Fleet fleet = MakeColonyFleet(23, 5);

            RunMovement(fleet);

            Assert.AreEqual(22, fleet.Cargo.ColonistsInKilotons);
        }

        [Test]
        public void TwentyTwoUnits_LoseNothing_AndPostNoMessage()
        {
            Fleet fleet = MakeColonyFleet(22, 5);

            RunMovement(fleet);

            Assert.AreEqual(22, fleet.Cargo.ColonistsInKilotons, "(22 + 11) x 3 / 100 = 0");
            Assert.IsFalse(HasLossMessage());
        }

        [Test]
        public void StargateLeg_IsExempt()
        {
            Fleet fleet = MakeColonyFleet(100, Global.StargateWarpFactor);

            RunMovement(fleet);

            Assert.AreEqual(100, fleet.Cargo.ColonistsInKilotons);
            Assert.IsFalse(HasLossMessage());
        }

        [Test]
        public void IdleFleet_WithOneWaypoint_LosesNothing()
        {
            Fleet fleet = MakeColonyFleet(100, 5);
            fleet.Waypoints.RemoveAt(1);

            RunMovement(fleet);

            Assert.AreEqual(100, fleet.Cargo.ColonistsInKilotons);
        }

        [Test]
        public void FirstWaypointLayMinesTask_LosesNothing()
        {
            Fleet fleet = MakeColonyFleet(100, 5);
            fleet.Waypoints[0].Task = new LayMinesTask();

            RunMovement(fleet);

            Assert.AreEqual(100, fleet.Cargo.ColonistsInKilotons);
        }

        [Test]
        public void NonAlternateRealityRace_LosesNothing()
        {
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("JOAT");
            Fleet fleet = MakeColonyFleet(100, 5);

            RunMovement(fleet);

            Assert.AreEqual(100, fleet.Cargo.ColonistsInKilotons);
        }
    }
}
