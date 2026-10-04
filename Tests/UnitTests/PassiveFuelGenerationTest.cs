namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;

    // behavior-specs-8/turn-generation-engine.md section 1 step 22 (the fuel pass): a fleet at a
    // friendly starbase that can dock ships is refuelled to full; every other fleet gains 50 mg of
    // fuel per Anti-matter Generator carried and 200 mg per ship of the Fuel Transport and
    // Super-Fuel Transport hulls, up to its capacity. The earlier "ramscoop engines contribute 200
    // fuel" reading was wrong - ramscoops play no part in this pass.
    [TestFixture]
    public class PassiveFuelGenerationTest
    {
        private static ShipDesign MakeDesign(string hullName, int fuelCapacity, int generation)
        {
            Component blueprint = new Component { Name = hullName, Mass = 100 };
            Hull hull = new Hull { FuelCapacity = fuelCapacity };
            hull.Modules = new List<HullModule>();
            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint, Name = hullName };

            // A non-empty Summary stops the lazy Update() from rebuilding it, so the test can state
            // exactly the fuel figures the design's components would have summed to.
            design.Summary.Properties.Add("Fuel", new Fuel(fuelCapacity, generation));
            return design;
        }

        private static Fleet MakeFleet(ShipDesign design, int quantity, double fuel)
        {
            Fleet fleet = new Fleet(1) { Owner = 1, FuelAvailable = fuel };
            fleet.Composition.Add(design.Key, new ShipToken(design, quantity));
            return fleet;
        }

        private static void Regenerate(Fleet fleet)
        {
            SimpleTurnGenerator generator = new SimpleTurnGenerator(new SimpleServerData());
            typeof(TurnGenerator)
                .GetMethod("RegenerateFleet", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(generator, new object[] { fleet, false });
        }

        [Test]
        public void EachAntiMatterGenerator_MakesFiftyMilligramsAYear()
        {
            // The design's summed Fuel generation is 50 per generator, so two generators is 100.
            Fleet fleet = MakeFleet(MakeDesign("Scout", 1000, 100), 1, 100);

            Assert.AreEqual(100, fleet.PassiveFuelGeneration);

            Regenerate(fleet);

            Assert.AreEqual(200, fleet.FuelAvailable);
        }

        [Test]
        public void GenerationScalesWithShipCount()
        {
            Fleet fleet = MakeFleet(MakeDesign("Scout", 1000, 50), 3, 0);

            Regenerate(fleet);

            Assert.AreEqual(150, fleet.FuelAvailable);
        }

        [Test]
        public void FuelTransportHulls_MakeTwoHundredMilligramsPerShipAYear()
        {
            Fleet fleet = MakeFleet(MakeDesign("Fuel Transport", 5000, 0), 2, 0);

            Regenerate(fleet);

            Assert.AreEqual(400, fleet.FuelAvailable);
        }

        [Test]
        public void SuperFuelTransportHulls_DoToo()
        {
            Fleet fleet = MakeFleet(MakeDesign("Super-Fuel Transport", 5000, 0), 1, 0);

            Regenerate(fleet);

            Assert.AreEqual(200, fleet.FuelAvailable);
        }

        [Test]
        public void Generation_NeverExceedsTheFuelCapacity()
        {
            Fleet fleet = MakeFleet(MakeDesign("Fuel Transport", 250, 0), 1, 200);

            Regenerate(fleet);

            Assert.AreEqual(250, fleet.FuelAvailable);
        }

        [Test]
        public void OrdinaryHullWithNoGenerators_GainsNothing()
        {
            Fleet fleet = MakeFleet(MakeDesign("Scout", 1000, 0), 1, 300);

            Regenerate(fleet);

            Assert.AreEqual(300, fleet.FuelAvailable);
        }
    }
}
