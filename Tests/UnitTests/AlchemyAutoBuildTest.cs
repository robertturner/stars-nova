namespace Nova.Tests.UnitTests
{
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;

    // behavior-specs-10/production-queue.md section 7 ("How the code does it") and 10a: where an
    // AUTO Mineral Alchemy entry sits decides what it does. In last position (or alone) it buys as
    // many units as the resources pay for (quantity forced to 1,000), each 100 resources (25 with
    // the Mineral Alchemy trait) adding 1 kT of each mineral, and the remainder becomes a one-unit
    // manual Alchemy entry at the top of the queue. Anywhere else it buys nothing itself: it arms a
    // shortfall conversion for the NEXT entry only, which converts the remaining resources into
    // exactly the scarcest mineral's shortfall when that entry is short of a mineral. The auto entry
    // leaves the queue with the item it served. A manual Alchemy entry is an ordinary item.
    // (This reverses the spec-9 pass, under which an auto entry bought up to 1,000 wherever it sat.)
    [TestFixture]
    public class AlchemyAutoBuildTest
    {
        /// <summary>A part with a fixed cost that pays proportionally when short, like Factory /
        /// Defenses / Ship units do (the scarcest component's affordable fraction of every
        /// component), and is skipped when a needed component is entirely absent.</summary>
        private class PartUnit : IProductionUnit
        {
            private readonly Resources cost;
            private Resources remainingCost;

            public int Built;

            public PartUnit(Resources cost)
            {
                this.cost = cost;
                remainingCost = new Resources(cost);
            }

            public Resources Cost => cost;
            public Resources RemainingCost => remainingCost;
            public string Name => "Part";
            public int? CurrentCount(Star star) => null;
            public int? SupportableCount(Star star) => null;
            public XmlElement ToXml(XmlDocument xmldoc) => xmldoc.CreateElement("PartUnit");

            public bool IsSkipped(Star star)
            {
                Resources onHand = star.ResourcesOnHand;
                return (cost.Ironium > 0 && onHand.Ironium <= 0)
                    || (cost.Boranium > 0 && onHand.Boranium <= 0)
                    || (cost.Germanium > 0 && onHand.Germanium <= 0)
                    || (cost.Energy > 0 && onHand.Energy <= 0);
            }

            public bool Construct(Star star)
            {
                if (star.ResourcesOnHand >= remainingCost)
                {
                    star.ResourcesOnHand -= remainingCost;
                    remainingCost = new Resources(cost);
                    Built++;
                    return true;
                }

                double fraction = 1.0;
                fraction = Fraction(fraction, remainingCost.Ironium, star.ResourcesOnHand.Ironium);
                fraction = Fraction(fraction, remainingCost.Boranium, star.ResourcesOnHand.Boranium);
                fraction = Fraction(fraction, remainingCost.Germanium, star.ResourcesOnHand.Germanium);
                fraction = Fraction(fraction, remainingCost.Energy, star.ResourcesOnHand.Energy);

                Resources paid = remainingCost * fraction;
                star.ResourcesOnHand -= paid;
                remainingCost -= paid;
                return false;
            }

            private static double Fraction(double current, int needed, int onHand)
            {
                return needed > onHand ? System.Math.Min(current, (double)onHand / needed) : current;
            }
        }

        private ServerData serverState;
        private Star star;

        [SetUp]
        public void Init()
        {
            serverState = new ServerData();
            star = new Star();
            star.Name = "Alembic";
            star.Owner = 1;
        }

        private void Produce()
        {
            new Manufacture(serverState).Items(star);
        }

        private ProductionOrder Queue(int quantity, IProductionUnit unit, bool isAutoBuild)
        {
            ProductionOrder order = new ProductionOrder(quantity, unit, isAutoBuild);
            star.ManufacturingQueue.Queue.Add(order);
            return order;
        }

        private int AlchemyMessages()
        {
            return serverState.AllMessages.Count(m => m.Text != null && m.Text.Contains("transmuted"));
        }

        // ---- Last position (or alone) ----

        [Test]
        public void AutoAlchemy_Alone_SpendsEverythingItCanAfford_IgnoringItsOwnQuantity()
        {
            star.ResourcesOnHand = new Resources(0, 0, 0, 5000);
            ProductionOrder order = Queue(1, new AlchemyProductionUnit(new Race()), true);

            Produce();

            Assert.AreEqual(0, star.ResourcesOnHand.Energy);
            Assert.AreEqual(50, star.ResourcesOnHand.Ironium, "5,000 resources at 100 per unit");
            Assert.AreEqual(50, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(50, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(1, order.Quantity, "The auto entry is never edited");
            CollectionAssert.AreEqual(new[] { order }, star.ManufacturingQueue.Queue, "...nor removed");
            Assert.AreEqual(1, AlchemyMessages(), "Message 140 once for the purchase");
        }

        [Test]
        public void AutoAlchemy_Alone_IsCappedAtOneThousandUnitsAYear()
        {
            star.ResourcesOnHand = new Resources(0, 0, 0, 200000);
            ProductionOrder order = new ProductionOrder(1, new AlchemyProductionUnit(new Race()), true);

            int done = order.Process(star);

            Assert.AreEqual(1000, done);
            Assert.AreEqual(100000, star.ResourcesOnHand.Energy, "200,000 less 1,000 units at 100 each");
        }

        [Test]
        public void AutoAlchemy_InLastPosition_RemainderBecomesAOneUnitManualEntryAtTheTop()
        {
            star.ResourcesOnHand = new Resources(0, 0, 0, 5060);
            ProductionOrder auto = Queue(1, new AlchemyProductionUnit(new Race()), true);

            Produce();

            Assert.AreEqual(50, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(0, star.ResourcesOnHand.Energy);
            Assert.AreEqual(2, star.ManufacturingQueue.Queue.Count);
            ProductionOrder top = star.ManufacturingQueue.Queue[0];
            Assert.IsFalse(top.IsAutoBuild, "The leftover is peeled off into a MANUAL entry");
            Assert.IsInstanceOf<AlchemyProductionUnit>(top.Unit);
            Assert.AreEqual(1, top.Quantity);
            Assert.AreEqual(40, top.Unit.RemainingCost.Energy, "60 of the unit's 100 already paid");
            Assert.AreSame(auto, star.ManufacturingQueue.Queue[1]);
            Assert.AreEqual(100, auto.Unit.RemainingCost.Energy, "The auto entry itself holds no progress");
        }

        [Test]
        public void AutoAlchemy_MineralAlchemyTrait_CostsTwentyFivePerUnit()
        {
            Race race = new Race();
            race.Traits.Add("MA");
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);
            Queue(1, new AlchemyProductionUnit(race), true);

            Produce();

            Assert.AreEqual(40, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(0, star.ResourcesOnHand.Energy);
        }

        // ---- Not last: skipped, arms the next entry's shortfall conversion ----

        [Test]
        public void AutoAlchemy_NotLast_BuysNothingItself()
        {
            star.ResourcesOnHand = new Resources(10, 0, 0, 1000);
            Queue(1, new AlchemyProductionUnit(new Race()), true);
            PartUnit part = new PartUnit(new Resources(5, 0, 0, 10));
            Queue(1, part, false);

            Produce();

            Assert.AreEqual(1, part.Built);
            Assert.AreEqual(990, star.ResourcesOnHand.Energy, "Only the part was paid for");
            Assert.AreEqual(0, star.ResourcesOnHand.Boranium, "No alchemy - the part was never short");
            Assert.AreEqual(0, AlchemyMessages());
        }

        [Test]
        public void AutoAlchemy_NotLast_ProcessCalledDirectly_BuysNothing()
        {
            // The same rule seen through ProductionOrder.Process itself (as the completion
            // estimator calls it): an auto Alchemy entry with another entry behind it buys nothing.
            star.ResourcesOnHand = new Resources(0, 0, 0, 5000);
            ProductionOrder auto = Queue(1, new AlchemyProductionUnit(new Race()), true);
            Queue(1, new PartUnit(new Resources(0, 0, 0, 10)), false);

            Assert.AreEqual(0, auto.Process(star));
            Assert.AreEqual(5000, star.ResourcesOnHand.Energy);
        }

        [Test]
        public void ServedItemLeavingTheQueue_TakesTheAutoAlchemyEntryWithIt()
        {
            star.ResourcesOnHand = new Resources(10, 0, 0, 1000);
            Queue(1, new AlchemyProductionUnit(new Race()), true);
            Queue(1, new PartUnit(new Resources(5, 0, 0, 10)), false);
            ProductionOrder after = Queue(1, new PartUnit(new Resources(0, 0, 0, 10)), true);

            Produce();

            CollectionAssert.AreEqual(new[] { after }, star.ManufacturingQueue.Queue, "Both the served entry and its auto Alchemy entry are gone");
        }

        [Test]
        public void ShortfallConversion_ManualEntry_PaysProportionallyThenConvertsExactlyTheShortfall()
        {
            // Part costs 10 Ironium + 20 resources; 4 Ironium on hand (40%, the scarcest). The manual
            // entry first pays 40% (4 Ir, 8 res), then the shortfall of 6 Ironium is converted at 100
            // resources a unit, each unit adding 1 kT of every mineral, and the purchase is retried.
            star.ResourcesOnHand = new Resources(4, 0, 0, 1000);
            Queue(1, new AlchemyProductionUnit(new Race()), true);
            PartUnit part = new PartUnit(new Resources(10, 0, 0, 20));
            Queue(1, part, false);

            Produce();

            Assert.AreEqual(1, part.Built, "Retried and completed after the conversion");
            Assert.AreEqual(1000 - 8 - 600 - 12, star.ResourcesOnHand.Energy);
            Assert.AreEqual(0, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(6, star.ResourcesOnHand.Boranium, "6 units: 6 kT of each mineral");
            Assert.AreEqual(6, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(0, star.ManufacturingQueue.Queue.Count, "The served item and its Alchemy entry were the whole queue");
            Assert.AreEqual(1, AlchemyMessages());
            StringAssert.Contains("6", serverState.AllMessages.Single(m => m.Text.Contains("transmuted")).Text);
        }

        [Test]
        public void ShortfallConversion_CanConvertAgainForTheNextScarceMineral_WithOneMessage()
        {
            // 5 Ir + 10 Bo + 10 res, none of either mineral: both at 0%, so Ironium (first, and a
            // later component only replaces it when strictly lower) is converted first: 5 units. The
            // retry is then short of Boranium (5 of 10) and converts 5 more.
            star.ResourcesOnHand = new Resources(0, 0, 0, 2000);
            Queue(1, new AlchemyProductionUnit(new Race()), true);
            PartUnit part = new PartUnit(new Resources(5, 10, 0, 10));
            Queue(1, part, false);

            Produce();

            Assert.AreEqual(1, part.Built);
            Assert.AreEqual(2000 - 1000 - 10, star.ResourcesOnHand.Energy);
            Assert.AreEqual(5, star.ResourcesOnHand.Ironium, "10 converted, 5 used");
            Assert.AreEqual(0, star.ResourcesOnHand.Boranium, "10 converted, 10 used");
            Assert.AreEqual(10, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(1, AlchemyMessages(), "Message 140 once per purchase call");
        }

        [Test]
        public void ShortfallConversion_ThatCannotCoverTheShortfall_LeavesAPartPaidManualAlchemyAtTheTop()
        {
            // No Ironium at all (the part would normally block the queue) and 350 resources: 3 units
            // convert (not the 10 needed), the 50 left over become a part-paid manual Alchemy unit.
            star.ResourcesOnHand = new Resources(0, 0, 0, 350);
            ProductionOrder auto = Queue(1, new AlchemyProductionUnit(new Race()), true);
            PartUnit part = new PartUnit(new Resources(10, 0, 0, 20));
            ProductionOrder partOrder = Queue(1, part, false);

            Produce();

            Assert.AreEqual(0, part.Built);
            Assert.AreEqual(3, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(3, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(0, star.ResourcesOnHand.Energy);
            Assert.AreEqual(3, star.ManufacturingQueue.Queue.Count);
            ProductionOrder top = star.ManufacturingQueue.Queue[0];
            Assert.IsInstanceOf<AlchemyProductionUnit>(top.Unit);
            Assert.IsFalse(top.IsAutoBuild);
            Assert.AreEqual(1, top.Quantity);
            Assert.AreEqual(50, top.Unit.RemainingCost.Energy, "50 of 100 paid");
            Assert.AreSame(auto, star.ManufacturingQueue.Queue[1]);
            Assert.AreSame(partOrder, star.ManufacturingQueue.Queue[2]);
        }

        [Test]
        public void ShortfallConversion_DoesNothingWhenResourcesAreTheScarcestComponent()
        {
            // 8 of 10 Ironium (80%) but 100 of 200 resources (50%): resources are scarcest.
            star.ResourcesOnHand = new Resources(8, 0, 0, 100);
            Queue(1, new AlchemyProductionUnit(new Race()), true);
            PartUnit part = new PartUnit(new Resources(10, 0, 0, 200));
            Queue(1, part, false);

            Produce();

            Assert.AreEqual(0, part.Built);
            Assert.AreEqual(0, star.ResourcesOnHand.Boranium, "Nothing converted");
            Assert.AreEqual(0, AlchemyMessages());
        }

        [Test]
        public void ShortfallConversion_ServingAnAutoEntry_KeepsTheAlchemyEntry()
        {
            // An auto part short of Ironium: 10 units convert (1,000 of 1,100 resources), one part
            // is built (20 resources), the next is short again with only 80 resources left - those
            // become the part-paid manual Alchemy unit. An auto entry never leaves the queue, so its
            // Alchemy entry stays too.
            star.ResourcesOnHand = new Resources(0, 0, 0, 1100);
            ProductionOrder auto = Queue(1, new AlchemyProductionUnit(new Race()), true);
            PartUnit part = new PartUnit(new Resources(10, 0, 0, 20));
            ProductionOrder partOrder = Queue(2, part, true);

            Produce();

            Assert.AreEqual(1, part.Built);
            Assert.AreEqual(10, part.RemainingCost.Ironium, "The auto entry made no partial payment of its own");
            Assert.AreEqual(3, star.ManufacturingQueue.Queue.Count);
            Assert.AreEqual(20, star.ManufacturingQueue.Queue[0].Unit.RemainingCost.Energy, "80 of 100 paid");
            Assert.AreSame(auto, star.ManufacturingQueue.Queue[1]);
            Assert.AreSame(partOrder, star.ManufacturingQueue.Queue[2]);
        }

        [Test]
        public void WithoutTheFlag_AnEntryShortOfAMineralConvertsNothing()
        {
            // The Alchemy entry is BEHIND the short part, not in front of it: the part blocks and
            // nothing is converted.
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);
            PartUnit part = new PartUnit(new Resources(10, 0, 0, 20));
            Queue(1, part, false);
            Queue(1, new AlchemyProductionUnit(new Race()), true);

            Produce();

            Assert.AreEqual(0, part.Built);
            Assert.AreEqual(0, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(1000, star.ResourcesOnHand.Energy);
        }

        [Test]
        public void TheFlagServesTheNextEntryOnly()
        {
            star.ResourcesOnHand = new Resources(10, 0, 0, 1000);
            Queue(1, new AlchemyProductionUnit(new Race()), true);
            Queue(1, new PartUnit(new Resources(5, 0, 0, 10)), false);
            PartUnit second = new PartUnit(new Resources(10, 0, 0, 10));
            Queue(1, second, false);

            Produce();

            Assert.AreEqual(0, second.Built, "Short of Ironium with no Alchemy entry directly in front of it");
            Assert.AreEqual(0, star.ResourcesOnHand.Boranium, "Nothing converted for it");
        }

        // ---- Manual Alchemy ----

        [Test]
        public void ManualAlchemy_IsAnOrdinaryItemWhereverItStands_AndNeverArmsTheFlag()
        {
            star.ResourcesOnHand = new Resources(0, 0, 0, 5000);
            ProductionOrder manual = Queue(3, new AlchemyProductionUnit(new Race()), false);
            PartUnit part = new PartUnit(new Resources(10, 0, 0, 20));
            Queue(1, part, false);

            Produce();

            Assert.AreEqual(3, star.ResourcesOnHand.Boranium, "Exactly the 3 ordered units, and no conversion for the part");
            Assert.AreEqual(3, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(0, part.Built, "Short of Ironium (3 of 10) and not served by a conversion");
            Assert.IsFalse(star.ManufacturingQueue.Queue.Contains(manual), "The manual order is consumed");
            Assert.AreEqual(1, AlchemyMessages());
        }

        [Test]
        public void ManualAlchemy_StillBuysOnlyWhatWasOrdered()
        {
            star.ResourcesOnHand = new Resources(0, 0, 0, 5000);
            ProductionOrder order = new ProductionOrder(3, new AlchemyProductionUnit(new Race()), false);

            Assert.AreEqual(3, order.Process(star));
            Assert.AreEqual(4700, star.ResourcesOnHand.Energy);
            Assert.AreEqual(0, order.Quantity);
        }
    }
}
