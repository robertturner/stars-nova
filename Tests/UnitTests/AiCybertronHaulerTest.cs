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
    using Nova.Common.Waypoints;

    using NUnit.Framework;

    /// <summary>
    /// Personality 4's colonist router `FUN_10a8_23d0` (docs/behavior-specs-10/
    /// ai-opponent-behavior.md §12, personality 4 "Haulers"), with the effective behaviour of the
    /// indexing defect (no hauler is ever seen inbound).
    /// </summary>
    [TestFixture]
    public class AiCybertronHaulerTest
    {
        private ClientData clientState;
        private Race race;
        private uint nextFleetId = 1;
        private ShipDesign privateer;

        [SetUp]
        public void SetUp()
        {
            clientState = new ClientData();
            clientState.EmpireState.Id = 1;
            race = new Race();
            clientState.EmpireState.Race = race;
            clientState.EmpireState.TurnYear = Global.StartingYear + 30;
            nextFleetId = 1;

            privateer = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            privateer.Name = AiDesignRoleTag.Name("Privateer", "hauler-a", Global.StartingYear + 21);
            privateer.Blueprint = new Component { Name = "Privateer" };
            privateer.Blueprint.Properties.Add("Hull", new Hull { BaseCargo = 2000, FuelCapacity = 500, Modules = new List<HullModule>() });
            privateer.Update();
            clientState.EmpireState.Designs[privateer.Key] = privateer;
        }

        private Star AddOwnedStar(string name, int x, int colonists, bool starbase = false)
        {
            Star star = new Star { Name = name, Owner = 1, Position = new NovaPoint(x, 100), ThisRace = race, Colonists = colonists };
            if (starbase)
            {
                star.Starbase = new Fleet(name + " base", 1, 5000 + nextFleetId++, star.Position);
            }

            clientState.EmpireState.OwnedStars.Add(star);
            clientState.EmpireState.StarReports.Add(name, new StarIntel { Name = name, Position = star.Position, Owner = 1 });
            return star;
        }

        private Star AddForeignPlanet(string name, int x, ushort owner, bool starbase = false)
        {
            StarIntel report = new StarIntel { Name = name, Position = new NovaPoint(x, 100), Owner = owner, Colonists = 50000 };
            if (starbase)
            {
                report.Starbase = new Fleet(name + " base", owner, 7000 + nextFleetId++, report.Position);
            }

            clientState.EmpireState.StarReports.Add(name, report);
            return new Star { Name = name, Owner = owner, Position = report.Position };
        }

        private Fleet Hauler(Star at, int colonistsAboard = 0)
        {
            Fleet fleet = new Fleet("Hauler", 1, nextFleetId++, at.Position);
            ShipToken token = new ShipToken(privateer, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.InOrbit = at;
            fleet.Cargo.ColonistsInKilotons = colonistsAboard;
            fleet.Waypoints.Add(new Waypoint { Position = at.Position, Destination = at.Name });
            clientState.EmpireState.OwnedFleets.Add(fleet);
            return fleet;
        }

        private FleetOrder Route(Fleet fleet)
        {
            AiFleetContext context = new AiFleetContext(clientState, AiCategory.Cybertrons, AiCategory.StandardSkill, new Random(1), clientState.EmpireState.OwnedFleets.Values.ToList());
            return CybertronHaulerRouter.Route(fleet, context);
        }

        [Test]
        public void AtALargeOwnWorld_LoadsAThousand_AndPrefersAColonyUnderTwoHundredUnits()
        {
            Star big = AddOwnedStar("Big", 100, 300000, starbase: true);
            AddOwnedStar("Medium", 150, 50000);   // 500 units, nearer
            AddOwnedStar("Tiny", 300, 10000);     // 100 units, under 200
            AddOwnedStar("FarTiny", 600, 10000);  // beyond 400 ly

            FleetOrder order = Route(Hauler(big));

            Assert.AreEqual(1000, order.LoadColonistsKt, "1,000 units (100,000 colonists)");
            Assert.AreEqual("Tiny", order.Destination.Name);
        }

        [Test]
        public void Loaded_FallsBackToAnOwnPlanetUnderAThousandUnits_WithinFourHundredLightYears()
        {
            Star big = AddOwnedStar("Big", 100, 300000, starbase: true);
            AddOwnedStar("Medium", 450, 90000);   // 900 units
            AddOwnedStar("Large", 150, 150000);   // 1,500 units: not a target

            FleetOrder order = Route(Hauler(big));

            Assert.AreEqual("Medium", order.Destination.Name);
        }

        [Test]
        public void Loaded_WithNowhereToGo_UnloadsAtItsOwnPlanet()
        {
            Star big = AddOwnedStar("Big", 100, 300000, starbase: true);

            FleetOrder order = Route(Hauler(big, colonistsAboard: 1500));

            Assert.IsNull(order.Destination);
            Assert.AreEqual(1000, order.UnloadColonistsKt, "up to 1,000 units");
            Assert.AreEqual(0, order.LoadColonistsKt);
        }

        [Test]
        public void AtASmallOwnColony_UnloadsAndHeadsForALargeStarbaseWorld()
        {
            Star small = AddOwnedStar("Small", 100, 20000);
            AddOwnedStar("NoBase", 150, 300000);
            AddOwnedStar("Base", 400, 230000, starbase: true);   // 2,300 units
            AddOwnedStar("SmallBase", 120, 210000, starbase: true); // 2,100: not over 2,200

            FleetOrder order = Route(Hauler(small, colonistsAboard: 800));

            Assert.AreEqual(800, order.UnloadColonistsKt);
            Assert.AreEqual("Base", order.Destination.Name);
        }

        [Test]
        public void AtAForeignPlanetWithoutAStarbase_MakesAGroundAssault_ThenGoesHomeEmpty()
        {
            AddOwnedStar("Base", 400, 230000, starbase: true);
            Star enemy = AddForeignPlanet("Enemy", 100, 2);

            FleetOrder order = Route(Hauler(enemy, colonistsAboard: 600));

            Assert.AreEqual(600, order.UnloadColonistsKt, "Unload All colonists there");
            Assert.AreEqual("Base", order.Destination.Name);
        }

        [Test]
        public void AtAForeignStarbasePlanet_OrAnUnownedOne_ItCountsAsLoadedWhenCarrying()
        {
            AddOwnedStar("Base", 400, 230000, starbase: true);
            AddOwnedStar("Colony", 200, 5000);
            Star enemy = AddForeignPlanet("Fortress", 100, 2, starbase: true);

            FleetOrder order = Route(Hauler(enemy, colonistsAboard: 600));
            Assert.AreEqual(0, order.UnloadColonistsKt, "no assault on a starbase planet");
            Assert.AreEqual("Colony", order.Destination.Name, "loaded: to the small colony");

            Star unowned = AddForeignPlanet("Empty", 120, Global.Nobody);
            order = Route(Hauler(unowned));
            Assert.AreEqual("Base", order.Destination.Name, "empty: to the large starbase world");
        }

        [Test]
        public void InDeepSpace_HeadsForTheNearestPlanet()
        {
            AddOwnedStar("Base", 400, 230000, starbase: true);
            AddForeignPlanet("Near", 160, 2);

            Fleet fleet = new Fleet("Hauler", 1, nextFleetId++, new NovaPoint(150, 100));
            ShipToken token = new ShipToken(privateer, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position, Destination = "deep space" });
            clientState.EmpireState.OwnedFleets.Add(fleet);

            Assert.AreEqual("Near", Route(fleet).Destination.Name);
        }

        [Test]
        public void HasSmallOwnPlanetWithin400_LeavesOutThePlanetItself()
        {
            Star big = AddOwnedStar("Big", 100, 50000);
            Assert.IsFalse(CybertronHaulerRouter.HasSmallOwnPlanetWithin400(big.Position, big.Name, clientState.EmpireState));

            AddOwnedStar("Colony", 499, 99900);
            Assert.IsTrue(CybertronHaulerRouter.HasSmallOwnPlanetWithin400(big.Position, big.Name, clientState.EmpireState));
        }
    }
}
