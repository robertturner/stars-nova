namespace Nova.Tests.UnitTests
{
    using System;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;

    // behavior-specs-8/production-queue.md section 10c: what earlier specs called the "Planet
    // Rebirth" random disaster is really the build-completion effect of the Genesis Device, an
    // ordinary manual catalog item (item type 13, 5,000 resources, no minerals). On completion it
    // zeroes the mine/factory/defense counts and the planetary scanner (except for Alternate Reality
    // owners), zeroes the three mineral stockpiles, re-rolls the three mineral concentrations
    // (25 + two 0-39 draws) and the three environment values (1 + two 0-49 draws), and tells every
    // player (message 283).
    [TestFixture]
    public class GenesisDeviceTest
    {
        /// <summary>Every bounded draw returns either the lowest or the highest legal value, so a
        /// test can assert the exact edges of the documented ranges.</summary>
        private class ExtremeRandom : Random
        {
            private readonly bool high;

            public ExtremeRandom(bool high)
            {
                this.high = high;
            }

            public override int Next(int minValue, int maxValue)
            {
                return high ? maxValue - 1 : minValue;
            }
        }

        private static Star BuildFullyDevelopedStar(Race race)
        {
            Star star = new Star();
            star.Name = "Old World";
            star.Owner = 1;
            star.ThisRace = race;
            star.Colonists = 120000;
            star.Factories = 40;
            star.Mines = 30;
            star.Defenses = 12;
            star.ScannerType = "Scoper 150";
            star.ResourcesOnHand = new Resources(500, 400, 300, 777);
            star.MineralConcentration = new Resources(90, 80, 70, 0);
            star.Gravity = 10;
            star.Temperature = 20;
            star.Radiation = 30;
            star.OriginalGravity = 11;
            star.OriginalTemperature = 21;
            star.OriginalRadiation = 31;
            return star;
        }

        [Test]
        public void Effect_ZeroesInfrastructureScannerAndMineralStockpiles()
        {
            Star star = BuildFullyDevelopedStar(new Race());

            star.ApplyGenesisDevice(new ExtremeRandom(false));

            Assert.AreEqual(0, star.Factories);
            Assert.AreEqual(0, star.Mines);
            Assert.AreEqual(0, star.Defenses);
            Assert.AreEqual("None", star.ScannerType);
            Assert.AreEqual(0, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(0, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(0, star.ResourcesOnHand.Germanium);
        }

        [Test]
        public void Effect_LeavesPopulationOwnerAndResourcesAlone()
        {
            Star star = BuildFullyDevelopedStar(new Race());

            star.ApplyGenesisDevice(new ExtremeRandom(false));

            Assert.AreEqual(120000, star.Colonists);
            Assert.AreEqual(1, star.Owner);
            Assert.AreEqual(777, star.ResourcesOnHand.Energy, "Only the mineral stockpiles are zeroed, not the resources");
        }

        [Test]
        public void Effect_RerollsMineralConcentrations_Between25And103()
        {
            Star low = BuildFullyDevelopedStar(new Race());
            low.ApplyGenesisDevice(new ExtremeRandom(false));
            Assert.AreEqual(25, low.MineralConcentration.Ironium);
            Assert.AreEqual(25, low.MineralConcentration.Boranium);
            Assert.AreEqual(25, low.MineralConcentration.Germanium);

            Star high = BuildFullyDevelopedStar(new Race());
            high.ApplyGenesisDevice(new ExtremeRandom(true));
            Assert.AreEqual(103, high.MineralConcentration.Ironium, "25 + 39 + 39");
            Assert.AreEqual(103, high.MineralConcentration.Boranium);
            Assert.AreEqual(103, high.MineralConcentration.Germanium);
        }

        [Test]
        public void Effect_RerollsEnvironmentBetween1And99_ResettingTheTerraformHistory()
        {
            Star low = BuildFullyDevelopedStar(new Race());
            low.ApplyGenesisDevice(new ExtremeRandom(false));
            Assert.AreEqual(1, low.Gravity);
            Assert.AreEqual(1, low.Temperature);
            Assert.AreEqual(1, low.Radiation);

            Star high = BuildFullyDevelopedStar(new Race());
            high.ApplyGenesisDevice(new ExtremeRandom(true));
            Assert.AreEqual(99, high.Gravity, "1 + 49 + 49");
            Assert.AreEqual(99, high.Temperature);
            Assert.AreEqual(99, high.Radiation);

            Assert.AreEqual(high.Gravity, high.OriginalGravity);
            Assert.AreEqual(high.Temperature, high.OriginalTemperature);
            Assert.AreEqual(high.Radiation, high.OriginalRadiation);
        }

        [Test]
        public void Effect_OnAnAlternateRealityPlanet_SkipsTheInfrastructureReset_ButStillRerollsTheLand()
        {
            Race race = new Race();
            race.Traits.SetPrimary("AR");
            Star star = BuildFullyDevelopedStar(race);

            star.ApplyGenesisDevice(new ExtremeRandom(true));

            Assert.AreEqual(40, star.Factories, "Alternate Reality owners keep their counts (they have none to lose)");
            Assert.AreEqual(12, star.Defenses);
            Assert.AreEqual("Scoper 150", star.ScannerType);
            Assert.AreEqual(0, star.ResourcesOnHand.Germanium, "Stockpiles are zeroed for every owner");
            Assert.AreEqual(103, star.MineralConcentration.Ironium, "Concentrations are re-rolled for every owner");
            Assert.AreEqual(99, star.Gravity);
        }

        [Test]
        public void Unit_CostsFiveThousandResourcesAndNoMinerals()
        {
            GenesisDeviceProductionUnit unit = new GenesisDeviceProductionUnit();

            Assert.AreEqual(5000, unit.Cost.Energy);
            Assert.AreEqual(0, unit.Cost.Ironium + unit.Cost.Boranium + unit.Cost.Germanium);
        }

        [Test]
        public void Unit_BanksPartialProgress_WhenTheWholeCostCannotBePaid()
        {
            Star star = BuildFullyDevelopedStar(new Race());
            star.ResourcesOnHand = new Resources(0, 0, 0, 1200);
            GenesisDeviceProductionUnit unit = new GenesisDeviceProductionUnit();

            Assert.IsFalse(unit.Construct(star));
            Assert.AreEqual(0, star.ResourcesOnHand.Energy, "Everything available was spent");
            Assert.AreEqual(3800, unit.RemainingCost.Energy, "...and banked toward the remaining 5,000");
        }

        [Test]
        public void Unit_SurvivesAnXmlRoundTripThroughTheProductionOrder()
        {
            ProductionOrder order = new ProductionOrder(1, new GenesisDeviceProductionUnit(), false);
            XmlDocument doc = new XmlDocument();
            XmlElement element = order.ToXml(doc);

            ProductionOrder loaded = new ProductionOrder(element);

            Assert.IsInstanceOf<GenesisDeviceProductionUnit>(loaded.Unit);
            Assert.AreEqual(5000, loaded.Unit.Cost.Energy);
        }

        [Test]
        public void Manufacture_CompletingTheDevice_ResetsThePlanetAndTellsEveryPlayer()
        {
            ServerData serverState = new ServerData();
            Race race = new Race();
            Star star = BuildFullyDevelopedStar(race);
            star.ResourcesOnHand = new Resources(10, 10, 10, 5000);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new GenesisDeviceProductionUnit(), false));

            new Manufacture(serverState, new ExtremeRandom(false)).Items(star);

            Assert.AreEqual(0, star.Factories);
            Assert.AreEqual(25, star.MineralConcentration.Ironium);
            Assert.AreEqual(0, star.ManufacturingQueue.Queue.Count, "The one-shot order is consumed");
            Assert.AreEqual(0, star.ResourcesOnHand.Energy, "5,000 resources were paid");

            // Message 283 to everyone; the owner also gets message 62 (the queue is now empty,
            // production-queue.md 10i), which this test used to rule out.
            var rebirth = serverState.AllMessages.FindAll(m => m.Text.Contains("rebirthed"));
            Assert.AreEqual(1, rebirth.Count);
            Assert.AreEqual(0, rebirth[0].Audience, "Audience 0 is everyone, not just the owner");
            StringAssert.Contains("Old World", rebirth[0].Text);
        }

        [Test]
        public void Manufacture_AnUnaffordableDevice_DoesNothingToThePlanetYet()
        {
            ServerData serverState = new ServerData();
            Star star = BuildFullyDevelopedStar(new Race());
            star.ResourcesOnHand = new Resources(10, 10, 10, 4000);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new GenesisDeviceProductionUnit(), false));

            new Manufacture(serverState, new ExtremeRandom(false)).Items(star);

            Assert.AreEqual(40, star.Factories, "Only partial progress was banked - the planet is untouched");
            Assert.AreEqual(1, star.ManufacturingQueue.Queue.Count);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }
    }
}
