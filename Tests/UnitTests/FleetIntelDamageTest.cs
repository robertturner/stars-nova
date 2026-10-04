namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Combat;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// A foreign fleet report carries the ships it saw but no damage; only an owned fleet's report
    /// keeps its armor and damage word (behavior-specs-11/combat-resolution.md section 6.2, "the
    /// damage word outside battle ... foreign reports carry none").
    /// </summary>
    [TestFixture]
    public class FleetIntelDamageTest
    {
        private static (Fleet fleet, ShipDesign design) MakeDamagedFleet()
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 100, ArmorStrength = 100 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Name = "Warship", Blueprint = blueprint };
            design.Update();

            Fleet fleet = new Fleet("Warship", 2, 1, new NovaPoint(10, 10));
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position });
            ShipToken token = new ShipToken(design, 3) { Armor = (design.Armor * 3) - 50, Shields = 0 };
            DamageWord.Store(token, new DamageWord(100, 250));
            fleet.Composition.Add(token.Key, token);
            return (fleet, design);
        }

        [Test]
        public void ForeignReport_CarriesNoDamage()
        {
            (Fleet fleet, ShipDesign design) = MakeDamagedFleet();
            FleetIntel report = new FleetIntel();

            report.Update(fleet, ScanLevel.InScan, 2400);

            ShipToken seen = report.Composition.Values.Single();
            Assert.AreEqual(0, seen.PackedDamage, "a foreign report carries no damage word");
            Assert.AreEqual(design.Armor * 3, seen.Armor, "a foreign report shows full armor");
        }

        [Test]
        public void OwnReport_KeepsTheDamageWord()
        {
            (Fleet fleet, _) = MakeDamagedFleet();
            FleetIntel report = new FleetIntel();

            report.Update(fleet, ScanLevel.Owned, 2400);

            Assert.AreNotEqual(0, report.Composition.Values.Single().PackedDamage,
                "an owned fleet's report keeps its damage word");
        }
    }
}
