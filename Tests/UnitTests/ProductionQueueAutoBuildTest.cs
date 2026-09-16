namespace Nova.Tests.UnitTests
{
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;

    /// <summary>
    /// Covers the Production panel's Auto Build toggle (ProductionViewModel.ToggleAutoBuild /
    /// AutoBuildOnAdd - see docs/behavior-specs-4/production-queue.md §9): flipping an
    /// already-queued order's IsAutoBuild flag in place is done via the same CommandMode.Edit
    /// path as AdjustQuantity's own edits, replacing the order with a new one that carries the
    /// same Unit and Quantity - so it must not trip ProductionCommand.IsValid's Edit anti-cheat
    /// guard (which rejects any edit that would change the order's cost), even though nothing
    /// about the actual mechanic being tested here (auto-build vs. manual) touches cost at all.
    /// </summary>
    [TestFixture]
    public class ProductionQueueAutoBuildTest
    {
        private class FixedCostUnit : IProductionUnit
        {
            public Resources Cost { get; }
            public Resources RemainingCost => Cost;
            public string Name { get; }

            public FixedCostUnit(string name, Resources cost)
            {
                Name = name;
                Cost = cost;
            }

            public bool IsSkipped(Star star) => false;
            public bool Construct(Star star) => true;
            public int? CurrentCount(Star star) => null;
            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("FixedCostUnit");
        }

        private EmpireData empire;
        private Star star;

        [SetUp]
        public void Init()
        {
            empire = new SimpleEmpireData();
            star = new Star();
            star.Name = "Testworld";
            star.Owner = empire.Id;
            empire.OwnedStars.Add(star);

            var manualOrder = new ProductionOrder(5, new FixedCostUnit("Factory", new Resources(10, 0, 4, 0)), false);
            star.ManufacturingQueue.Queue.Add(manualOrder);
        }

        [Test]
        public void TogglingToAutoBuild_KeepsSameUnitAndQuantity_AndIsAccepted()
        {
            ProductionOrder existing = star.ManufacturingQueue.Queue[0];
            var edited = new ProductionOrder(existing.Quantity, existing.Unit, !existing.IsAutoBuild);
            var command = new ProductionCommand(CommandMode.Edit, edited, star.Name, 0);

            Assert.IsTrue(command.IsValid(empire), "Toggling only IsAutoBuild must not trip the Edit cost-decrease guard");

            command.ApplyToState(empire);

            Assert.IsTrue(star.ManufacturingQueue.Queue[0].IsAutoBuild, "The order should now be flagged as auto-build");
            Assert.AreEqual(5, star.ManufacturingQueue.Queue[0].Quantity, "Quantity must be unchanged by the toggle");
        }

        [Test]
        public void AutoBuildOrder_NeverBlocksTheQueue_EvenWhenItCannotBeAfforded()
        {
            var blocked = new BlockedUnit();
            var autoOrder = new ProductionOrder(1, blocked, true);
            var manualOrder = new ProductionOrder(1, blocked, false);

            Assert.IsFalse(autoOrder.IsBlocking(star), "An auto-build order must never block the queue");
            Assert.IsTrue(manualOrder.IsBlocking(star), "An ordinary order that can't be afforded must still block the queue");
        }

        /// <summary>A unit that's always "skipped" (can't be afforded this year) - the exact
        /// condition docs/behavior-specs-4/production-queue.md §9 says an auto-build order is
        /// silently passed over for, rather than halting everything queued after it.</summary>
        private class BlockedUnit : IProductionUnit
        {
            public Resources Cost => new Resources(100, 100, 100, 100);
            public Resources RemainingCost => Cost;
            public string Name => "Blocked";
            public bool IsSkipped(Star star) => true;
            public bool Construct(Star star) => false;
            public int? CurrentCount(Star star) => null;
            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("BlockedUnit");
        }

        /// <summary>Stands in for Factory/Mine/Defense's real CurrentCount-backed units without
        /// depending on Race-derived costs: Built plays the role of star.Factories, incremented
        /// by Construct and read back by CurrentCount, so a test can simulate a later loss (e.g.
        /// bombing) just by decrementing it directly.</summary>
        private class CountedUnit : IProductionUnit
        {
            public int Built;
            public Resources Cost => new Resources(1, 0, 0, 0);
            public Resources RemainingCost => Cost;
            public string Name => "Counted";
            public bool IsSkipped(Star star) => false;

            public bool Construct(Star star)
            {
                Built++;
                return true;
            }

            public int? CurrentCount(Star star) => Built;
            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("CountedUnit");
        }

        [Test]
        public void AutoBuildTarget_BuildsOnlyTheShortfall_AndNeverExhaustsQuantity()
        {
            var unit = new CountedUnit { Built = 7 };
            var order = new ProductionOrder(10, unit, true); // "Up to 10", 7 already built

            int done = order.Process(star);

            Assert.AreEqual(3, done, "Should build exactly the shortfall (10 - 7), not the full target");
            Assert.AreEqual(10, unit.Built);
            Assert.AreEqual(10, order.Quantity, "Quantity is the standing target, never decremented by Process");
        }

        [Test]
        public void AutoBuildTarget_StaysQueuedAndIdle_OnceSatisfied()
        {
            var unit = new CountedUnit { Built = 10 };
            var order = new ProductionOrder(10, unit, true);

            int done = order.Process(star);

            Assert.AreEqual(0, done, "Nothing left to build - target already met");
            Assert.AreEqual(10, order.Quantity, "Still non-zero, so Manufacture.Items never removes it from the queue");
        }

        [Test]
        public void AutoBuildTarget_ResumesBuilding_AfterTheCountLaterDrops()
        {
            var unit = new CountedUnit { Built = 10 };
            var order = new ProductionOrder(10, unit, true);
            order.Process(star); // satisfied, goes idle

            unit.Built = 6; // e.g. bombing destroyed 4 factories
            int done = order.Process(star);

            Assert.AreEqual(4, done, "The now-idle order should automatically resume to close the new gap");
            Assert.AreEqual(10, unit.Built);
            Assert.AreEqual(10, order.Quantity);
        }

        [Test]
        public void ManualOrder_ForTheSameUnitType_StillConsumesQuantityAndCanBeExhausted()
        {
            var unit = new CountedUnit { Built = 0 };
            var order = new ProductionOrder(3, unit, false); // manual, NOT auto-build

            int done = order.Process(star);

            Assert.AreEqual(3, done);
            Assert.AreEqual(0, order.Quantity, "A manual order still consumes down to 0 exactly as before this fix");
        }
    }
}
