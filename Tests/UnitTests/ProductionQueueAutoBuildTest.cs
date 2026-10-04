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
            public int? SupportableCount(Star star) => null;
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
            public int? SupportableCount(Star star) => null;
            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("BlockedUnit");
        }

        /// <summary>Stands in for Factory/Mine/Defense's real CurrentCount-backed units without
        /// depending on Race-derived costs: Built plays the role of star.Factories, incremented
        /// by Construct and read back by CurrentCount, so a test can simulate a later loss (e.g.
        /// bombing) just by decrementing it directly.</summary>
        private class CountedUnit : IProductionUnit
        {
            public int Built;

            /// <summary>Null means "no population-scaled cap" (matches Alchemy/Defense/Ship/
            /// Terraform's real SupportableCount); set to simulate Factory/Mine's real
            /// star.GetOperableFactories()/GetOperableMines() throttle.</summary>
            public int? Supportable;

            /// <summary>Null means "no build cap" (matches Ship/Alchemy/Terraform); set to
            /// simulate Factory/Mine/Defense's real maximum-population-based BuildCap.</summary>
            public int? Cap;

            public int? BuildCap(Star star) => Cap;

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
            public int? SupportableCount(Star star) => Supportable;
            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("CountedUnit");
        }

        // behavior-specs-8/production-queue.md section 10h (live test, Stars! v2.70j): the "up to N"
        // figure on an auto-build Mines/Factories/Defenses order is the most it may buy EACH TURN,
        // not a total the planet is topped up to. The built count is never subtracted from N.

        [Test]
        public void AutoBuild_BuysItsFullPerTurnQuantity_EvenWhenThatMayAlreadyBeBuilt()
        {
            var unit = new CountedUnit { Built = 10 };
            var order = new ProductionOrder(2, unit, true); // "Up to 2" with 10 already built

            int done = order.Process(star);

            Assert.AreEqual(2, done, "A total-target reading would buy nothing (10 >= 2); the live test bought N every year");
            Assert.AreEqual(12, unit.Built);
            Assert.AreEqual(2, order.Quantity, "The auto entry itself is never edited");
        }

        [Test]
        public void AutoBuild_KeepsBuyingTheSameQuantityEveryTurn_AndNeverLeavesTheQueue()
        {
            var unit = new CountedUnit { Built = 10 };
            var order = new ProductionOrder(3, unit, true);

            Assert.AreEqual(3, order.Process(star));
            Assert.AreEqual(3, order.Process(star));
            Assert.AreEqual(3, order.Process(star));

            Assert.AreEqual(19, unit.Built);
            Assert.AreEqual(3, order.Quantity, "Still non-zero, so Manufacture.Items never removes it from the queue");
        }

        [Test]
        public void AutoBuild_IsClampedToTheOperableRoom_SupportableMinusBuilt()
        {
            // Population can operate 15 and 10 are built: room for 5, even though N is 20.
            var unit = new CountedUnit { Built = 10, Supportable = 15 };
            var order = new ProductionOrder(20, unit, true);

            int done = order.Process(star);

            Assert.AreEqual(5, done, "min(N=20, operable 15 - built 10)");
            Assert.AreEqual(15, unit.Built);
            Assert.AreEqual(20, order.Quantity);
        }

        [Test]
        public void AutoBuild_WithNoRoomUnderTheOperableCap_GoesIdleButStaysQueued()
        {
            var unit = new CountedUnit { Built = 15, Supportable = 15 };
            var order = new ProductionOrder(5, unit, true);

            Assert.AreEqual(0, order.Process(star));
            Assert.AreEqual(5, order.Quantity);

            unit.Supportable = 18; // population grew
            Assert.AreEqual(3, order.Process(star), "Resumes as soon as the operable cap leaves room again");
        }

        [Test]
        public void AutoBuild_WhenNThrottlesBeforeTheCap_BuysExactlyN()
        {
            var unit = new CountedUnit { Built = 0, Supportable = 50 };
            var order = new ProductionOrder(4, unit, true);

            Assert.AreEqual(4, order.Process(star), "Run 2 of the live test: exactly N a turn although nowhere near the cap");
        }

        [Test]
        public void AutoBuild_ForAUnitWithNoPopulationScaledCap_BuysNEveryTurnToo()
        {
            var unit = new CountedUnit { Built = 0 };
            var order = new ProductionOrder(10, unit, true);

            Assert.AreEqual(10, order.Process(star));
            Assert.AreEqual(10, order.Process(star));
            Assert.AreEqual(10, order.Quantity);
        }

        [Test]
        public void AutoBuildShipOrder_KeepsTheLegacyOneOffBehaviour()
        {
            // The original has no auto-build ship item; flipping a ship order to auto-build must
            // not turn it into an endless standing order.
            var unit = new OneOffUnit();
            var order = new ProductionOrder(3, unit, true);

            Assert.AreEqual(3, order.Process(star));
            Assert.AreEqual(0, order.Quantity, "Consumed and eligible for removal like a manual order");
        }

        private class OneOffUnit : IProductionUnit
        {
            public Resources Cost => new Resources(1, 0, 0, 0);
            public Resources RemainingCost => Cost;
            public string Name => "OneOff";
            public bool IsSkipped(Star star) => false;
            public bool Construct(Star star) => true;
            public int? CurrentCount(Star star) => null;
            public int? SupportableCount(Star star) => null;
            public bool AutoBuildIsStandingOrder => false;
            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("OneOffUnit");
        }

        [Test]
        public void AutoBuildPerTurnLimitOverride_ReplacesTheOrdersOwnQuantity()
        {
            var unit = new LimitedUnit();
            var order = new ProductionOrder(1, unit, true);

            // Mirrors auto Mineral Alchemy: the entry's own quantity is ignored and it buys as many
            // as it can afford (limit 1,000) every turn.
            int done = order.Process(star);

            Assert.AreEqual(1000, done);
            Assert.AreEqual(1, order.Quantity);
        }

        private class LimitedUnit : IProductionUnit
        {
            public Resources Cost => new Resources(1, 0, 0, 0);
            public Resources RemainingCost => Cost;
            public string Name => "Limited";
            public bool IsSkipped(Star star) => false;
            public bool Construct(Star star) => true;
            public int? CurrentCount(Star star) => null;
            public int? SupportableCount(Star star) => null;
            public int? AutoBuildPerTurnLimit => 1000;
            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("LimitedUnit");
        }

        [Test]
        public void ManualOrder_IsCutToTheBuildCapMinusWhatIsBuilt()
        {
            // production-queue.md section 10a: a manual Factory/Mine/Defenses quantity is cut to
            // build cap - built.
            var unit = new CountedUnit { Built = 8, Cap = 10 };
            var order = new ProductionOrder(5, unit, false);

            int done = order.Process(star);

            Assert.AreEqual(2, done);
            Assert.AreEqual(10, unit.Built);
            Assert.AreEqual(0, order.Quantity);
        }

        [Test]
        public void ManualOrder_WithNoRoomUnderTheBuildCap_IsZeroedSoTheQueueDropsIt()
        {
            var unit = new CountedUnit { Built = 12, Cap = 10 };
            var order = new ProductionOrder(5, unit, false);

            int done = order.Process(star);

            Assert.AreEqual(0, done);
            Assert.AreEqual(0, order.Quantity, "Deleted in the original when the room is zero or negative");
            Assert.AreEqual(12, unit.Built);
        }

        [Test]
        public void AutoBuildOrder_IsNotCutByTheManualBuildCap()
        {
            // The build cap applies to manual orders; an auto-build entry is clamped by the operable
            // count instead (and the operable count never exceeds the cap).
            var unit = new CountedUnit { Built = 8, Cap = 10, Supportable = 100 };
            var order = new ProductionOrder(5, unit, true);

            Assert.AreEqual(5, order.Process(star));
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
