namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // behavior-specs-9/turn-generation-engine.md §11 "Inner Strength colonist growth" (step 19):
    // growth = floor(R x C / 200) units (1 unit = 1 kT = 100 colonists, R = the race growth-rate
    // setting, no habitability term); when that is 0, a 1-in-3 draw gives 1 unit; clamped to free
    // cargo space (message 251); the excess lands on the orbited planet only when the same race owns
    // it (message 344), otherwise it is lost.
    [TestFixture]
    public class ColonistBreedingStepTest
    {
        private class FixedRandom : Random
        {
            private readonly int value;

            public FixedRandom(int value)
            {
                this.value = value;
            }

            public int Calls { get; private set; }

            public override int Next(int maxValue)
            {
                Calls++;
                return value;
            }
        }

        private ServerData serverState;
        private EmpireData empire;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            empire = new SimpleEmpireData { Id = 1, Race = new Race() };
            empire.Race.Traits.SetPrimary("IS");
            empire.Race.GrowthRate = 10;
            serverState.AllEmpires.Add(empire.Id, empire);
        }

        private Fleet MakeFleet(int colonistUnits, int cargoCapacity)
        {
            Component blueprint = new Component { Name = "Colony Ship", Mass = 20 };
            // FuelCapacity > 0: a hull with no fuel tank counts as a starbase here (Hull.IsStarbase).
            Hull hull = new Hull { ArmorStrength = 20, BaseCargo = cargoCapacity, FuelCapacity = 100 };
            hull.Modules = new List<HullModule>();
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint, Name = "Colony Ship" };
            design.Update();

            Fleet fleet = new Fleet(1) { Owner = empire.Id, Name = "Ark" };
            fleet.Composition.Add(design.Key, new ShipToken(design, 1));
            fleet.Cargo.ColonistsInKilotons = colonistUnits;
            fleet.Waypoints.Add(new Nova.Common.Waypoints.Waypoint
            {
                Position = fleet.Position,
                Destination = "Space at " + fleet.Position,
                Task = new Nova.Common.Waypoints.NoTask(),
            });
            empire.AddOrUpdateFleet(fleet);
            return fleet;
        }

        private void Breed(Random random)
        {
            new ColonistBreedingStep(random).Process(serverState);
        }

        [Test]
        public void GrowthIsHalfTheGrowthRateAsAPercentage_Truncated()
        {
            Fleet fleet = MakeFleet(40, 1000);
            FixedRandom random = new FixedRandom(1);

            Breed(random);

            Assert.AreEqual(42, fleet.Cargo.ColonistsInKilotons, "10 x 40 / 200 = 2 units");
            Assert.AreEqual(0, random.Calls, "No random draw on the non-zero branch");
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Type == "Inner Strength" && m.Text.Contains("200 colonists")));
        }

        [Test]
        public void GrowthRate20_HundredUnits_GainTen()
        {
            empire.Race.GrowthRate = 20;
            Fleet fleet = MakeFleet(100, 1000);

            Breed(new FixedRandom(1));

            Assert.AreEqual(110, fleet.Cargo.ColonistsInKilotons);
        }

        [Test]
        public void ZeroGrowth_DrawOfZero_GivesOneUnit()
        {
            Fleet fleet = MakeFleet(19, 1000);

            Breed(new FixedRandom(0));

            Assert.AreEqual(20, fleet.Cargo.ColonistsInKilotons, "10 x 19 / 200 = 0, then the 1-in-3 draw succeeds");
        }

        [Test]
        public void ZeroGrowth_DrawOfOneOrTwo_GivesNothing()
        {
            Fleet fleet = MakeFleet(19, 1000);
            Breed(new FixedRandom(1));
            Assert.AreEqual(19, fleet.Cargo.ColonistsInKilotons);

            Breed(new FixedRandom(2));
            Assert.AreEqual(19, fleet.Cargo.ColonistsInKilotons);
            Assert.IsFalse(serverState.AllMessages.Exists(m => m.Type == "Inner Strength"));
        }

        [Test]
        public void Overflow_LandsOnAnOwnOrbitedPlanet()
        {
            Star planet = new Star { Name = "Home", Owner = empire.Id, Colonists = 5000 };
            serverState.AllStars.Add(planet.Key, planet);
            Fleet fleet = MakeFleet(40, 41);
            fleet.InOrbit = planet;

            Breed(new FixedRandom(1));

            Assert.AreEqual(41, fleet.Cargo.ColonistsInKilotons, "Clamped to the hold's free space");
            Assert.AreEqual(5100, planet.Colonists, "The 1 unit that did not fit is beamed down");
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Type == "Inner Strength" && m.Text.Contains("beamed down")));
        }

        [Test]
        public void Overflow_IsLost_AtSomeoneElsesPlanet()
        {
            Star planet = new Star { Name = "Theirs", Owner = 2, Colonists = 5000 };
            serverState.AllStars.Add(planet.Key, planet);
            Fleet fleet = MakeFleet(40, 40);
            fleet.InOrbit = planet;

            Breed(new FixedRandom(1));

            Assert.AreEqual(40, fleet.Cargo.ColonistsInKilotons, "Hold already full");
            Assert.AreEqual(5000, planet.Colonists, "Excess is lost, never given to another race");
        }

        [Test]
        public void NonInnerStrengthRace_DoesNotBreed()
        {
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("JOAT");
            empire.Race.GrowthRate = 10;
            Fleet fleet = MakeFleet(40, 1000);

            Breed(new FixedRandom(0));

            Assert.AreEqual(40, fleet.Cargo.ColonistsInKilotons);
        }

        /// <summary>The step is registered in the turn generator (step 19).</summary>
        [Test]
        public void Generate_RunsTheBreedingStep()
        {
            empire.Race.GrowthRate = 20;
            Fleet fleet = MakeFleet(100, 1000);

            new SimpleTurnGenerator(serverState).Generate();

            Assert.AreEqual(110, fleet.Cargo.ColonistsInKilotons);
        }
    }
}
