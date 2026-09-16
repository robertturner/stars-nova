namespace Nova.Tests.UnitTests
{
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;

    /// <summary>
    /// Covers the ProductionCommand.Swap fix: reordering the production queue via two paired
    /// Edit commands (the original QueueList.SwapProductionOrders/ProductionViewModel.
    /// SwapQueueItems implementation) is unsafe whenever the two adjacent orders have different
    /// costs, because ProductionCommand.IsValid's Edit case rejects any edit that would
    /// *decrease* the remaining/total cost at an index (an anti-cheat guard against quietly
    /// substituting a cheaper order) - which also rejects one half of a cost-differing swap.
    /// CommandMode.Swap exists specifically to avoid that: a real swap changes no order's cost,
    /// so it has its own validity rule instead of going through Edit's.
    /// </summary>
    [TestFixture]
    public class ProductionQueueReorderTest
    {
        private EmpireData empire;
        private Star star;

        /// <summary>Minimal IProductionUnit stub with a fixed, hand-picked cost - avoids any
        /// dependency on race-derived Factory/Mine costs, which aren't guaranteed to differ in
        /// the specific way this test needs (cheaper in at least one Resources component).</summary>
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

        [SetUp]
        public void Init()
        {
            empire = new SimpleEmpireData();
            star = new Star();
            star.Name = "Testworld";
            star.Owner = empire.Id;
            empire.OwnedStars.Add(star);

            // Expensive first, cheap second - the exact adjacency that breaks a paired-Edit swap:
            // moving Cheap into Expensive's slot is a cost *decrease* at that index.
            var expensive = new ProductionOrder(1, new FixedCostUnit("Expensive", new Resources(100, 100, 100, 100)), false);
            var cheap = new ProductionOrder(1, new FixedCostUnit("Cheap", new Resources(10, 10, 10, 10)), false);

            star.ManufacturingQueue.Queue.Add(expensive);
            star.ManufacturingQueue.Queue.Add(cheap);
        }

        [Test]
        public void PairedEditSwap_FailsValidationForACostDecreasingSwap()
        {
            // This reproduces the pre-fix QueueList.SwapProductionOrders/ProductionViewModel.
            // SwapQueueItems implementation exactly, to document the bug it replaced: swapping
            // index 0 (Expensive) and index 1 (Cheap) via two Edit commands.
            var editA = new ProductionCommand(CommandMode.Edit, star.ManufacturingQueue.Queue[1], star.Name, 0);
            Assert.IsFalse(editA.IsValid(empire), "Moving the cheaper order into the pricier order's slot should be rejected by Edit's cost guard - proving the paired-Edit swap is unsafe here.");
        }

        [Test]
        public void Swap_SucceedsForTheSameCostDecreasingCase()
        {
            var swap = new ProductionCommand(CommandMode.Swap, star.Name, 0, 1);

            Assert.IsTrue(swap.IsValid(empire));
            swap.ApplyToState(empire);

            Assert.AreEqual("Cheap", star.ManufacturingQueue.Queue[0].Name);
            Assert.AreEqual("Expensive", star.ManufacturingQueue.Queue[1].Name);
        }

        [Test]
        public void Swap_IsItsOwnInverse()
        {
            var swap = new ProductionCommand(CommandMode.Swap, star.Name, 0, 1);
            swap.ApplyToState(empire);
            swap.ApplyToState(empire);

            Assert.AreEqual("Expensive", star.ManufacturingQueue.Queue[0].Name);
            Assert.AreEqual("Cheap", star.ManufacturingQueue.Queue[1].Name);
        }

        [Test]
        public void Swap_RejectsOutOfRangeIndex()
        {
            var swap = new ProductionCommand(CommandMode.Swap, star.Name, 0, 5);
            Assert.IsFalse(swap.IsValid(empire));
        }

        [Test]
        public void Swap_DoesNotDisturbOtherQueueEntries()
        {
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new FixedCostUnit("Third", new Resources(1, 1, 1, 1)), false));

            var swap = new ProductionCommand(CommandMode.Swap, star.Name, 0, 1);
            swap.ApplyToState(empire);

            Assert.AreEqual("Third", star.ManufacturingQueue.Queue[2].Name);
        }
    }
}
