namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // Regression tests for behavior-specs-9/fleet-movement-scanning-cargo.md §5, "Scrap Fleet
    // recovery, complete decision table": per mineral, recovered = floor(f x S) + the fleet's
    // cargo of it, S = sum of ship count x design cost; f = 1/3 in deep space (left as
    // wreckage) or at a bare planet, 9/20 at a bare planet whose OWNER has Ultimate Recycling,
    // 4/5 at a starbase planet, 9/10 with the planet owner's UR. Both conditions belong to the
    // planet, never to the scrapper. The fleet is always scrapped.
    [TestFixture]
    public class ScrapFleetRecoveryTest
    {
        internal static ShipDesign MakeDesign(Resources cost)
        {
            Component blueprint = new Component { Mass = 100, Cost = cost };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update();
            return design;
        }

        internal static Fleet MakeFleet(int owner, Resources designCost, int quantity)
        {
            Fleet fleet = new Fleet(1) { Owner = (ushort)owner, Name = "Scrapper" };
            ShipToken token = new ShipToken(MakeDesign(designCost), quantity);
            fleet.Composition.Add(token.Key, token);
            return fleet;
        }

        private static EmpireData MakeEmpire(int id, params string[] traits)
        {
            EmpireData empire = new EmpireData { Id = (ushort)id };
            foreach (string trait in traits)
            {
                empire.Race.Traits.Add(trait);
            }

            return empire;
        }

        private static Star MakePlanet(EmpireData owner, bool starbase)
        {
            Star star = new Star { Name = "Planet" };
            if (owner != null)
            {
                star.Owner = owner.Id;
                star.ThisRace = owner.Race;
                star.Colonists = 10000;
            }

            if (starbase)
            {
                star.Starbase = new Fleet(99);
            }

            return star;
        }

        private static Resources ScrapAt(Star star, EmpireData scrapper, EmpireData planetOwner, Fleet fleet)
        {
            fleet.InOrbit = star;
            Assert.IsTrue(new ScrapTask().Perform(fleet, star, scrapper, planetOwner));
            Assert.AreEqual(0, fleet.Composition.Count, "The fleet is scrapped whatever happens");
            return star.ResourcesOnHand;
        }

        [Test]
        public void BarePlanet_RecoversOneThird_MultipliedOutBeforeTruncating()
        {
            // 3 ships of 7 Ironium: S = 21, floor(21/3) = 7 (and 4/5 below gives 16, not 3 x 5).
            EmpireData scrapper = MakeEmpire(1);
            Star star = MakePlanet(null, false);
            Resources onHand = ScrapAt(star, scrapper, null, MakeFleet(1, new Resources(7, 10, 2, 500), 3));

            Assert.AreEqual(7, onHand.Ironium);
            Assert.AreEqual(10, onHand.Boranium);
            Assert.AreEqual(2, onHand.Germanium, "floor(6/3)");
            Assert.AreEqual(0, onHand.Energy, "Resources are never credited as a stockpile");
        }

        [Test]
        public void StarbasePlanet_RecoversFourFifths_OfTheSummedCost()
        {
            EmpireData owner = MakeEmpire(1);
            Star star = MakePlanet(owner, true);
            Resources onHand = ScrapAt(star, owner, owner, MakeFleet(1, new Resources(7, 0, 0, 0), 3));

            Assert.AreEqual(16, onHand.Ironium, "floor(4 x 21 / 5) = 16");
        }

        [Test]
        public void CargoMinerals_AreAddedInFull()
        {
            EmpireData scrapper = MakeEmpire(1);
            Star star = MakePlanet(null, false);
            Fleet fleet = MakeFleet(1, new Resources(100, 0, 0, 0), 1);
            fleet.Cargo.Ironium = 7;
            fleet.Cargo.Germanium = 5;

            Resources onHand = ScrapAt(star, scrapper, null, fleet);

            Assert.AreEqual(33 + 7, onHand.Ironium);
            Assert.AreEqual(5, onHand.Germanium);
        }

        [Test]
        public void PlanetOwnersUltimateRecycling_GivesNineTwentiethsAtABarePlanet()
        {
            EmpireData scrapper = MakeEmpire(1);
            EmpireData urOwner = MakeEmpire(2, "UR");
            Star star = MakePlanet(urOwner, false);

            Resources onHand = ScrapAt(star, scrapper, urOwner, MakeFleet(1, new Resources(100, 0, 0, 0), 1));

            Assert.AreEqual(45, onHand.Ironium, "The planet owner's UR applies even to another race's fleet");
        }

        [Test]
        public void PlanetOwnersUltimateRecycling_GivesNineTenthsAtAStarbase()
        {
            EmpireData urOwner = MakeEmpire(1, "UR");
            Star star = MakePlanet(urOwner, true);

            Resources onHand = ScrapAt(star, urOwner, urOwner, MakeFleet(1, new Resources(100, 0, 0, 0), 1));

            Assert.AreEqual(90, onHand.Ironium);
        }

        [Test]
        public void ScrappersOwnUltimateRecycling_DoesNotApplyOverAnotherRacesPlanet()
        {
            EmpireData urScrapper = MakeEmpire(1, "UR");
            EmpireData plainOwner = MakeEmpire(2);
            Star star = MakePlanet(plainOwner, true);

            Resources onHand = ScrapAt(star, urScrapper, plainOwner, MakeFleet(1, new Resources(100, 0, 0, 0), 1));

            Assert.AreEqual(80, onHand.Ironium, "UR is the planet owner's trait, not the scrapper's");
        }

        [Test]
        public void ScrappersUltimateRecycling_DoesNotApplyAtAnUnownedPlanet()
        {
            EmpireData urScrapper = MakeEmpire(1, "UR");
            Star star = MakePlanet(null, false);

            Resources onHand = ScrapAt(star, urScrapper, null, MakeFleet(1, new Resources(100, 0, 0, 0), 1));

            Assert.AreEqual(33, onHand.Ironium);
        }

        [Test]
        public void OwnPlanet_FleetColonistsJoinThePopulation_ForeignPlanetTheyAreLost()
        {
            EmpireData owner = MakeEmpire(1);
            EmpireData other = MakeEmpire(2);

            Star own = MakePlanet(owner, false);
            Fleet ownFleet = MakeFleet(1, new Resources(10, 0, 0, 0), 1);
            ownFleet.Cargo.ColonistsInKilotons = 5;
            ScrapAt(own, owner, owner, ownFleet);
            Assert.AreEqual(10000 + 500, own.Colonists);

            Star foreign = MakePlanet(other, false);
            Fleet visitor = MakeFleet(1, new Resources(10, 0, 0, 0), 1);
            visitor.Cargo.ColonistsInKilotons = 5;
            ScrapAt(foreign, owner, other, visitor);
            Assert.AreEqual(10000, foreign.Colonists);
        }

        [Test]
        public void DeepSpace_FleetIsScrapped_AndOneThirdIsLeftAsWreckage()
        {
            EmpireData scrapper = MakeEmpire(1);
            Fleet fleet = MakeFleet(1, new Resources(100, 30, 0, 0), 1);
            fleet.Position = new NovaPoint(40, 50);
            fleet.Cargo.Boranium = 4;

            ScrapTask task = new ScrapTask();
            Assert.IsTrue(task.Perform(fleet, null, scrapper));

            Assert.AreEqual(0, fleet.Composition.Count, "A deep-space scrap still scraps the fleet");
            Assert.AreEqual(33, task.Wreckage.Ironium);
            Assert.AreEqual(10 + 4, task.Wreckage.Boranium);
        }

        [Test]
        public void ScrapFleetStep_DeepSpace_CreatesAWreckageObjectAtTheFleetsPosition()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = MakeEmpire(1);
            serverState.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = MakeFleet(1, new Resources(90, 0, 0, 0), 1);
            fleet.Position = new NovaPoint(40, 50);
            Waypoint waypoint = new Waypoint { Destination = "Deep space", Position = new NovaPoint(40, 50), Task = new ScrapTask() };
            fleet.Waypoints.Add(waypoint);
            empire.OwnedFleets.Add(fleet);

            new ScrapFleetStep().Process(serverState);

            Assert.IsTrue(serverState.AllDeepSpaceMinerals.TryGetValue(new NovaPoint(40, 50).ToHashString(), out DeepSpaceMinerals wreckage));
            Assert.AreEqual(30, wreckage.Minerals.Ironium);
            Assert.IsEmpty(serverState.IterateAllFleets());
        }

        /// <summary>
        /// Deep-space scrap goes through the wreckage routine (combat-resolution.md §5/§7): one
        /// object holds at most 30,000 kT, the rest overflows into further objects at the spot.
        /// 120,000 Fe of cost leaves floor(120,000 / 3) = 40,000 kT, plus 5,000 kT of cargo.
        /// </summary>
        [Test]
        public void ScrapFleetStep_DeepSpace_WreckageIsCappedAtThirtyThousandKilotonsPerObject()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = MakeEmpire(1);
            serverState.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = MakeFleet(1, new Resources(120000, 0, 0, 0), 1);
            fleet.Cargo.Germanium = 5000;
            fleet.Position = new NovaPoint(40, 50);
            fleet.Waypoints.Add(new Waypoint { Destination = "Deep space", Position = new NovaPoint(40, 50), Task = new ScrapTask() });
            empire.OwnedFleets.Add(fleet);

            new ScrapFleetStep().Process(serverState);

            string key = new NovaPoint(40, 50).ToHashString();
            Assert.AreEqual(2, serverState.AllDeepSpaceMinerals.Count);
            Assert.AreEqual(30000, serverState.AllDeepSpaceMinerals[key].Minerals.Ironium);
            Assert.AreEqual(0, serverState.AllDeepSpaceMinerals[key].Minerals.Germanium, "Ironium fills the first object");
            DeepSpaceMinerals overflow = serverState.AllDeepSpaceMinerals[key + "#1"];
            Assert.AreEqual(10000, overflow.Minerals.Ironium);
            Assert.AreEqual(5000, overflow.Minerals.Germanium);
        }

        [Test]
        public void StarbaseSalvageRoll_IsRunForThePlanetOwner_NotTheScrapper()
        {
            // A fleet requiring far higher tech than the planet owner has: the tech-trading roll
            // must be made against the planet owner's levels (reading note 5).
            EmpireData scrapper = MakeEmpire(1);
            EmpireData owner = MakeEmpire(2);
            Star star = MakePlanet(owner, true);

            // TechTrading's roll is random (about 49% per attempt here), so make many attempts:
            // the planet owner all but certainly learns something, the scrapper never does.
            for (int i = 0; i < 60; i++)
            {
                owner.TechGainedThisTurn = false;
                scrapper.TechGainedThisTurn = false;
                Fleet fleet = MakeFleet(1, new Resources(10, 0, 0, 0), 1);
                foreach (ShipToken token in fleet.Composition.Values)
                {
                    token.Design.Blueprint.RequiredTech = new TechLevel(26);
                }

                fleet.InOrbit = star;
                new ScrapTask().Perform(fleet, star, scrapper, owner);
            }

            Assert.AreEqual(0, SumLevels(scrapper.ResearchLevels), "The scrapper never gains tech from scrapping at another race's starbase");
            Assert.Greater(SumLevels(owner.ResearchLevels), 0, "The salvage roll is the planet owner's");
        }

        private static int SumLevels(TechLevel levels)
        {
            int sum = 0;
            foreach (TechLevel.ResearchField field in System.Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                sum += levels[field];
            }

            return sum;
        }
    }
}
