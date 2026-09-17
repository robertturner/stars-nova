namespace Nova.Tests.UnitTests
{
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;

    /// <summary>
    /// Covers a real, live-reproduced ANR/infinite loop: reordering a production queue that had
    /// a permanently-unaffordable item in it (a stardock, "0% done - 100+ yrs") froze the whole
    /// app at ~100% CPU indefinitely - confirmed via a live `adb` thread dump showing the main
    /// thread stuck inside this exact assembly. Root cause: ProductionOrder.Process's while loop
    /// assumes IsSkipped()/Construct() jointly guarantee forward progress every iteration, but a
    /// genuine rounding edge case in a partial build (Resources' int-rounded `* double` operator)
    /// can leave both false forever, with nothing left to make either one flip. Process now bails
    /// out for the year if a Construct() call spends nothing at all, rather than trusting the
    /// unit to eventually stop asking.
    /// </summary>
    [TestFixture]
    public class ProductionOrderProcessTest
    {
        /// <summary>
        /// A minimal IProductionUnit that reproduces the exact stuck state found live:
        /// IsSkipped() never returns true ("there's still something to spend") and Construct()
        /// never returns true either ("not a whole unit yet"), and - critically, matching a
        /// genuine rounding edge case in Resources' own int-rounded `* double` operator
        /// (FactoryProductionUnit.Construct's partial-build branch) - it doesn't actually spend
        /// anything on the star either. Neither condition that ProductionOrder.Process's while
        /// loop relies on to eventually stop ever fires on its own.
        /// </summary>
        private class StuckProductionUnit : IProductionUnit
        {
            public Resources Cost { get; } = new Resources(0, 0, 100, 100);

            public Resources RemainingCost { get; } = new Resources(0, 0, 100, 100);

            public string Name => "Stuck Unit";

            public bool IsSkipped(Star star) => false;

            public int? CurrentCount(Star star) => null;

            public bool Construct(Star star) => false;

            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("StuckUnit");
        }

        [Test]
        public void Process_UnitThatNeverCompletesAndNeverSkipsAndNeverSpends_TerminatesInsteadOfHanging()
        {
            Star star = new Star();
            star.ResourcesOnHand = new Resources(1000, 1000, 1000, 1000);

            var order = new ProductionOrder(1000000, new StuckProductionUnit(), isAutoBuild: false);

            int done = 0;
            Assert.DoesNotThrow(() => done = order.Process(star));

            Assert.That(done, Is.EqualTo(0), "Nothing ever actually completed.");
            Assert.That(order.Quantity, Is.EqualTo(1000000), "Quantity must be untouched - Construct() never signaled a completion.");
        }

        [Test]
        public void Process_AutoBuildUnitThatNeverCompletesAndNeverSkipsAndNeverSpends_TerminatesInsteadOfHanging()
        {
            // Same stuck condition, but through the auto-build ("maintain up to N") branch -
            // a different while loop in Process with an identical risk.
            Star star = new Star();
            star.ResourcesOnHand = new Resources(1000, 1000, 1000, 1000);

            var order = new ProductionOrder(1000000, new AutoBuildStuckProductionUnit(), isAutoBuild: true);

            int done = 0;
            Assert.DoesNotThrow(() => done = order.Process(star));

            Assert.That(done, Is.EqualTo(0));
        }

        /// <summary>Same as StuckProductionUnit, but with a non-null CurrentCount so
        /// ProductionOrder.Process takes its auto-build branch instead.</summary>
        private class AutoBuildStuckProductionUnit : IProductionUnit
        {
            public Resources Cost { get; } = new Resources(0, 0, 100, 100);

            public Resources RemainingCost { get; } = new Resources(0, 0, 100, 100);

            public string Name => "Stuck Auto-Build Unit";

            public bool IsSkipped(Star star) => false;

            public int? CurrentCount(Star star) => 0;

            public bool Construct(Star star) => false;

            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("StuckUnit");
        }
    }
}
