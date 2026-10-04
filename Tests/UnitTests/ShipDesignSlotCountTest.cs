namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;

    // A hull slot holding N of a part contributes N times the part's mass and cost. Armor, weapons,
    // fuel and the other summed properties were already scaled by the slot's count; mass and cost
    // were added once per slot, so a fully loaded warship cost and weighed the same as one carrying a
    // single of each part (found while auditing the combat/battle-movement work).
    [TestFixture]
    public class ShipDesignSlotCountTest
    {
        private static ShipDesign Build(int count)
        {
            Component hullBlueprint = new Component { Name = "Test Hull", Mass = 100, Cost = new Resources(10, 10, 10, 10) };
            Hull hull = new Hull { FuelCapacity = 100 };
            hull.Modules = new List<HullModule>();
            HullModule module = new HullModule { ComponentType = "Mechanical" };
            module.AllocatedComponent = new Component { Name = "Test Part", Mass = 7, Cost = new Resources(1, 2, 3, 4) };
            module.ComponentCount = count;
            hull.Modules.Add(module);
            hullBlueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(1) { Blueprint = hullBlueprint, Name = "Loaded" };
            design.Update();
            return design;
        }

        [Test]
        public void MassAndCost_ScaleWithTheSlotsComponentCount()
        {
            ShipDesign one = Build(1);
            ShipDesign three = Build(3);

            Assert.AreEqual(100 + 7, one.Mass);
            Assert.AreEqual(100 + 7 * 3, three.Mass);
            Assert.AreEqual(10 + 1, one.Cost.Ironium);
            Assert.AreEqual(10 + 1 * 3, three.Cost.Ironium);
            Assert.AreEqual(10 + 4 * 3, three.Cost.Energy);
        }
    }
}
