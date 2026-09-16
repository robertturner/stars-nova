namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Covers SplitMergeTask.MergeFleets' fuel-shortfall stranding - docs/behavior-specs-5/
    /// client-ui-dialog-catalog.md: "a design already at full fuel merges without penalty; a
    /// design below full fuel undergoes a probabilistic check that can leave some ships behind."
    /// Only the DETERMINISTIC full-fuel path and the bounds of the probabilistic one are
    /// asserted - the exact roll isn't seeded (see MergeFleets' own comment on why), matching
    /// this project's established pattern for other unseeded gameplay randomness (e.g.
    /// StargateJumpTest's own "no test relies on the overgating vanish chance" note).
    /// </summary>
    [TestFixture]
    public class FleetMergeFuelShortfallTest
    {
        private static Fleet MakeFleet(long key, int shipCount, int fuelCapacityPerShip, double fuelAvailable)
        {
            Fleet fleet = new Fleet(key);
            fleet.Owner = 1;

            ShipDesign design = new ShipDesign(key);
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.FuelCapacity = fuelCapacityPerShip;
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);

            ShipToken token = new ShipToken(design, shipCount);
            fleet.Composition.Add(token.Key, token);
            fleet.FuelAvailable = fuelAvailable;

            return fleet;
        }

        private static int TotalShips(Fleet fleet)
        {
            return fleet.Composition.Values.Sum(t => t.Quantity);
        }

        [Test]
        public void MergeAtFullFuel_MovesEveryShipAndAllFuel_NoMessage()
        {
            Fleet left = MakeFleet(1, shipCount: 2, fuelCapacityPerShip: 100, fuelAvailable: 100);
            Fleet right = MakeFleet(2, shipCount: 3, fuelCapacityPerShip: 100, fuelAvailable: 300); // at capacity

            EmpireData empire = new SimpleEmpireData();
            empire.OwnedFleets.Add(right);

            var task = new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), right.Key);
            task.Perform(left, right, empire, empire);

            Assert.That(TotalShips(left), Is.EqualTo(5), "Every ship should have merged at full fuel.");
            Assert.That(TotalShips(right), Is.EqualTo(0), "Nothing should be left behind at full fuel.");
            Assert.That(left.FuelAvailable, Is.EqualTo(400), "Fuel pools should combine unchanged at full fuel.");
            Assert.That(task.Messages, Is.Empty, "No graduated message when nothing was stranded.");
        }

        [Test]
        public void MergeAtZeroFuel_NeverStrandsMoreShipsThanExist_AndReportsAMessageWheneverAnyAreStranded()
        {
            for (int trial = 0; trial < 25; trial++)
            {
                Fleet left = MakeFleet(1, shipCount: 2, fuelCapacityPerShip: 100, fuelAvailable: 100);
                Fleet right = MakeFleet(2, shipCount: 10, fuelCapacityPerShip: 100, fuelAvailable: 0); // empty tank

                EmpireData empire = new SimpleEmpireData();
                empire.OwnedFleets.Add(right);

                var task = new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), right.Key);
                task.Perform(left, right, empire, empire);

                int strandedShips = TotalShips(right);
                Assert.That(strandedShips, Is.InRange(0, 10), "Stranded count must never exceed the fleet's own ship count.");
                Assert.That(TotalShips(left) + strandedShips, Is.EqualTo(12), "No ship should be created or destroyed by a merge.");

                if (strandedShips > 0)
                {
                    Assert.That(task.Messages, Is.Not.Empty, "A graduated message should report any stranding.");
                }
            }
        }
    }
}
