namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;

    // Regression tests for two confirmed-but-previously-unimplemented per-trait component-cost
    // discounts from behavior-specs-7/race-traits.md's decompiled trace of the shared
    // component-cost-adjustment routine (the same routine already backing the existing War
    // Monger/Inner Strength weapon-cost and Improved Starbases/Alternate Reality starbase-cost
    // adjustments in ShipDesign.cs): Interstellar Traveler's Stargates cost 25% less, and Cheap
    // Engines' engines cost 50% less.
    [TestFixture]
    public class InterstellarTravelerAndCheapEnginesCostTest
    {
        private static ShipDesign BuildDesignWithOneComponent(Race race, string propertyKey, ComponentProperty property, Resources componentCost)
        {
            Component blueprint = new Component { Mass = 100 };

            Hull hull = new Hull { Modules = new List<HullModule>() };

            Component allocated = new Component { Cost = componentCost };
            allocated.Properties.Add(propertyKey, property);

            HullModule module = new HullModule { AllocatedComponent = allocated, ComponentCount = 1 };
            hull.Modules.Add(module);

            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update(race);
            return design;
        }

        [Test]
        public void InterstellarTraveler_StargateComponent_Costs25PercentLess()
        {
            Race itRace = new Race();
            itRace.Traits.SetPrimary("IT");
            Resources baseCost = new Resources(0, 0, 0, 400);

            ShipDesign design = BuildDesignWithOneComponent(itRace, "Gate", new Gate(), baseCost);

            Assert.AreEqual(300, design.Summary.Cost.Energy);
        }

        [Test]
        public void NonInterstellarTraveler_StargateComponent_PaysFullCost()
        {
            Race race = new Race();
            race.Traits.SetPrimary("SS");
            Resources baseCost = new Resources(0, 0, 0, 400);

            ShipDesign design = BuildDesignWithOneComponent(race, "Gate", new Gate(), baseCost);

            Assert.AreEqual(400, design.Summary.Cost.Energy);
        }

        [Test]
        public void CheapEngines_EngineComponent_Costs50PercentLess()
        {
            Race ceRace = new Race();
            ceRace.Traits.Add("CE");
            Resources baseCost = new Resources(0, 0, 0, 100);

            ShipDesign design = BuildDesignWithOneComponent(ceRace, "Engine", new Engine(), baseCost);

            Assert.AreEqual(50, design.Summary.Cost.Energy);
        }

        [Test]
        public void NonCheapEngines_EngineComponent_PaysFullCost()
        {
            Race race = new Race();
            race.Traits.SetPrimary("SS");
            Resources baseCost = new Resources(0, 0, 0, 100);

            ShipDesign design = BuildDesignWithOneComponent(race, "Engine", new Engine(), baseCost);

            Assert.AreEqual(100, design.Summary.Cost.Energy);
        }

        [Test]
        public void InterstellarTraveler_DoesNotDiscountMassDriverComponents()
        {
            // IT's 25% discount is confirmed scoped to Stargate subtypes specifically, not the
            // whole Orbital/0x0200 category - a Mass Driver (a different component under the same
            // XML <Type>Orbital</Type> bucket, but keyed under a different Properties entry) must
            // not be discounted just because the race is Interstellar Traveler.
            Race itRace = new Race();
            itRace.Traits.SetPrimary("IT");
            Resources baseCost = new Resources(0, 0, 0, 400);

            ShipDesign design = BuildDesignWithOneComponent(itRace, "Mass Driver", new IntegerProperty(5), baseCost);

            Assert.AreEqual(400, design.Summary.Cost.Energy);
        }
    }
}
