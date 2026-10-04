namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;

    /// <summary>
    /// Economy/production items from the behavior-specs-10 audit: Alternate Reality innate mining
    /// and the home-world mining floor (race-traits.md "Alternate Reality innate mining, exact";
    /// population-growth.md section 5; new-game-setup.md section 3), the Genesis Device /
    /// Terraform Cost-vs-RemainingCost aliasing, the Genesis Device's miniaturized price
    /// (race-traits.md section 7), at-cap manual installation orders (production-queue.md section
    /// 10a), the completion estimator's Alchemy shortfall conversion (section 7), and War Monger /
    /// Inner Strength bomb pricing (race-traits.md section 7 step 4).
    /// </summary>
    [TestFixture]
    public class EconomySpec10Test
    {
        private static Race OrdinaryRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("SS");
            race.MineProductionRate = 10;
            race.OperableMines = 10;
            race.OperableFactories = 10;
            race.FactoryBuildCost = 10;
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.GrowthRate = 0;
            return race;
        }

        private static Star IdealStar(Race race)
        {
            Star star = new Star();
            star.Name = "Tierra";
            star.Owner = 1;
            star.ThisRace = race;
            star.Gravity = race.GravityTolerance.OptimumLevel;
            star.Temperature = race.TemperatureTolerance.OptimumLevel;
            star.Radiation = race.RadiationTolerance.OptimumLevel;
            return star;
        }

        // ---- Mining ----

        [TestCase(2400, 4)]
        [TestCase(57600, 24)]
        [TestCase(57700, 24)]
        [TestCase(100, 1)]
        [TestCase(99, 0)]
        [TestCase(0, 0)]
        public void AlternateReality_InnateMines_AreFloorOfSqrtOfPopulationInHundreds(int colonists, int expectedMines)
        {
            Race ar = new Race();
            ar.Traits.SetPrimary("AR");
            Star star = new Star { ThisRace = ar, Colonists = colonists };

            Assert.AreEqual(expectedMines, star.GetMinesInUse());
        }

        [Test]
        public void AlternateReality_InnateMining_UsesTheFixedSettingTen_NotTheRaceMineSetting()
        {
            Race ar = new Race();
            ar.Traits.SetPrimary("AR");
            ar.MineProductionRate = 20;
            Star star = new Star { ThisRace = ar, Colonists = 57600 };

            // 24 mines x 10 / 10 x 100% = exactly 24 kT (no stochastic remainder).
            Assert.AreEqual(24, star.GetMiningRate(100));
        }

        [Test]
        public void AlternateReality_LiveTest_24InnateMinesAtConcentration115_Yield27Point6()
        {
            // turn-generation-engine.md section 6 live test: 24 innate mines x 115% = 27.6 kT,
            // stochastically rounded to 27 or 28.
            Race ar = new Race();
            ar.Traits.SetPrimary("AR");
            Star star = new Star { ThisRace = ar, Colonists = 57600 };

            int mined = star.GetMiningRate(115);
            Assert.That(mined, Is.InRange(27, 28));
        }

        [Test]
        public void OwnMines_AreNotTruncatedToWholeTensOfMines()
        {
            // mine-equivalents x concentration x setting / 1,000: 15 mines at 100% give 15 kT,
            // not (15 / 10) x 10 = 10.
            Race race = OrdinaryRace();
            Star star = IdealStar(race);
            star.Colonists = 100000;
            star.Mines = 15;

            Assert.AreEqual(15, star.GetMinesInUse());
            Assert.AreEqual(15, star.GetMiningRate(100));
        }

        [Test]
        public void HomeWorld_OwnMinesYieldAsIfConcentrationWereAtLeast30()
        {
            Race race = OrdinaryRace();
            Star star = IdealStar(race);
            star.Colonists = 100000;
            star.Mines = 10;

            Assert.AreEqual(2, star.GetMiningRate(20), "An ordinary planet: 10 mines at 20%");

            star.IsHomeWorld = true;
            Assert.AreEqual(3, star.GetMiningRate(20), "A home world yields at 30%");
            Assert.AreEqual(5, star.GetMiningRate(50), "Above 30 the stored value is used");
        }

        [Test]
        public void HomeWorld_Floor_DoesNotStopTheStoredConcentrationFalling()
        {
            Race race = OrdinaryRace();
            Star star = IdealStar(race);
            star.IsHomeWorld = true;
            star.Colonists = 100000;
            star.Mines = 100;
            star.MineralConcentration = new Resources(20, 20, 20, 0);
            // One point drops per 12500 / 25 / 10 x 10 = 500 kT at a stored 20 (effective 25);
            // 100 mines at the floored 30% yield 30 kT, so seed the progress just short of a drop.
            star.MineralMiningProgress = new Resources(490, 490, 490, 0);

            star.UpdateMinerals();

            Assert.AreEqual(30, star.ResourcesOnHand.Ironium, "Yield uses the floor of 30");
            Assert.AreEqual(19, star.MineralConcentration.Ironium, "The stored value still falls below 30");
        }

        [Test]
        public void RemoteMining_OfAnAlternateRealityHomeWorld_UsesTheFloorOf30()
        {
            Race ar = new Race();
            ar.Traits.SetPrimary("AR");
            Star star = new Star { ThisRace = ar, IsHomeWorld = true };
            Assert.AreEqual(30, star.RemoteMiningYieldConcentrationFloor());

            star.IsHomeWorld = false;
            Assert.AreEqual(0, star.RemoteMiningYieldConcentrationFloor(), "Not a home world");

            Star ordinaryHome = new Star { ThisRace = OrdinaryRace(), IsHomeWorld = true };
            Assert.AreEqual(0, ordinaryHome.RemoteMiningYieldConcentrationFloor(), "Only an AR owner's home world");

            int concentration = 10;
            int progress = 0;
            int mined = Star.MineForFleet(100, ref concentration, ref progress, 10, 30);
            Assert.AreEqual(30, mined, "100 mine-equivalents at the floored 30%");
            Assert.AreEqual(10, concentration, "The stored concentration is not raised");
        }

        [Test]
        public void IsHomeWorld_SurvivesAnXmlRoundTrip()
        {
            Star star = new Star { Name = "Home", IsHomeWorld = true };
            Star loaded = new Star(star.ToXml(new XmlDocument()));
            Assert.IsTrue(loaded.IsHomeWorld);

            Star other = new Star(new Star { Name = "Other" }.ToXml(new XmlDocument()));
            Assert.IsFalse(other.IsHomeWorld);
        }

        // ---- Cost vs RemainingCost aliasing ----

        [Test]
        public void GenesisDevice_PartialPayment_DoesNotReduceItsCost()
        {
            GenesisDeviceProductionUnit unit = new GenesisDeviceProductionUnit();
            Star star = new Star();
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);

            Assert.IsFalse(unit.Construct(star));
            Assert.AreEqual(5000, unit.Cost.Energy, "The price is unchanged by a partial payment");
            Assert.AreEqual(4000, unit.RemainingCost.Energy);

            star.ResourcesOnHand = new Resources(0, 0, 0, 4000);
            Assert.IsTrue(unit.Construct(star));
            Assert.AreEqual(0, star.ResourcesOnHand.Energy);
            Assert.AreEqual(5000, unit.RemainingCost.Energy, "The next device costs the full price again");
            Assert.AreNotSame(unit.Cost, unit.RemainingCost);
        }

        [Test]
        public void Terraform_PartialPayment_DoesNotReduceItsCost()
        {
            Race race = OrdinaryRace();
            TerraformProductionUnit unit = new TerraformProductionUnit(race);
            Star star = IdealStar(race);
            star.ResourcesOnHand = new Resources(0, 0, 0, 30);

            Assert.IsFalse(unit.Construct(star));
            Assert.AreEqual(100, unit.Cost.Energy);
            Assert.AreEqual(70, unit.RemainingCost.Energy);

            star.ResourcesOnHand = new Resources(0, 0, 0, 70);
            Assert.IsTrue(unit.Construct(star));
            Assert.AreEqual(100, unit.RemainingCost.Energy);
            Assert.AreNotSame(unit.Cost, unit.RemainingCost);
        }

        // ---- Genesis Device price ----

        private static Component GenesisComponent()
        {
            return new Component
            {
                Name = "Genesis Device",
                Type = ItemType.PlanetaryInstallations,
                Cost = new Resources(0, 0, 0, 5000),
                RequiredTech = new TechLevel(20, 10, 20, 10, 10, 20),
            };
        }

        [Test]
        public void GenesisDevice_IsMiniaturizedLikeAnyOtherPart()
        {
            // Six levels above every 20 requirement (margin 6): 4% x 6 = 24% off,
            // 5000 - MulDiv(5000, 24, 100) = 3800.
            Race race = OrdinaryRace();
            GenesisDeviceProductionUnit unit = new GenesisDeviceProductionUnit(GenesisComponent(), race, new TechLevel(26));

            Assert.AreEqual(3800, unit.Cost.Energy);
            Assert.AreEqual(3800, unit.RemainingCost.Energy);
        }

        [Test]
        public void GenesisDevice_AtItsRequirement_BleedingEdgeTechnologyDoublesIt()
        {
            Race race = OrdinaryRace();
            race.Traits.Add("BET");
            GenesisDeviceProductionUnit unit = new GenesisDeviceProductionUnit(GenesisComponent(), race, new TechLevel(20, 10, 20, 10, 10, 20));

            Assert.AreEqual(10000, unit.Cost.Energy);
        }

        [Test]
        public void GenesisDevice_ForAnEmpire_UsesItsOwnComponentRecordAndResearchLevels()
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Race = OrdinaryRace();
            empire.AvailableComponents = new RaceComponents();
            empire.AvailableComponents.Add("Genesis Device", GenesisComponent());
            empire.ResearchLevels = new TechLevel(26);

            Assert.AreEqual(3800, new GenesisDeviceProductionUnit(empire).Cost.Energy);
        }

        // ---- At-cap manual installation orders ----

        [Test]
        public void ManualDefensesAtTheCap_IsDeleted_AndDoesNotBlockTheRestOfTheQueue()
        {
            Race race = OrdinaryRace();
            Star star = IdealStar(race);
            star.Colonists = 100000;
            star.Defenses = 100; // the most any planet may hold
            star.ResourcesOnHand = new Resources(0, 0, 100, 1000);

            ProductionOrder defenses = new ProductionOrder(5, new DefenseProductionUnit(race), false);
            ProductionOrder factory = new ProductionOrder(1, new FactoryProductionUnit(race), false);
            star.ManufacturingQueue.Queue.Add(defenses);
            star.ManufacturingQueue.Queue.Add(factory);

            Assert.IsFalse(defenses.IsBlocking(star), "No room under the cap: deleted, not blocking");

            new Manufacture(new ServerData()).Items(star);

            Assert.AreEqual(100, star.Defenses);
            Assert.AreEqual(1, star.Factories, "The entry behind it was still bought");
            Assert.AreEqual(0, star.ManufacturingQueue.Queue.Count, "The at-cap entry was deleted");
        }

        [Test]
        public void ManualDefensesUnderTheCap_WithNoResources_StillBlocks()
        {
            Race race = OrdinaryRace();
            Star star = IdealStar(race);
            star.Colonists = 100000;
            star.ResourcesOnHand = new Resources(0, 0, 0, 0);

            ProductionOrder defenses = new ProductionOrder(5, new DefenseProductionUnit(race), false);
            star.ManufacturingQueue.Queue.Add(defenses);

            Assert.IsTrue(defenses.IsBlocking(star));
        }

        // ---- Completion estimator: Alchemy shortfall conversion ----

        [Test]
        public void Estimator_SimulatesTheAutoAlchemyShortfallConversion()
        {
            // A manual Factory with no Germanium on hand, behind an auto Mineral Alchemy entry:
            // the real turn converts 4 units of resources (1 kT each mineral) and buys it in year
            // one (production-queue.md section 7). The estimator used to treat the factory as
            // blocked forever.
            Race race = OrdinaryRace();
            Star star = IdealStar(race);
            star.Colonists = 2000000;

            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new AlchemyProductionUnit(race), true));
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new FactoryProductionUnit(race), false));

            IReadOnlyList<ProductionCompletionEstimate> estimates = ProductionCompletionEstimator.EstimateAll(star, race, researchBudget: 0);

            Assert.AreEqual(1, estimates[1].YearsToFinish);
            Assert.AreEqual(ProductionQueueColor.Green, estimates[1].Color);
        }

        [Test]
        public void ProcessYear_MatchesManufactureItems()
        {
            Race race = OrdinaryRace();

            Star viaManufacture = IdealStar(race);
            Star viaQueue = IdealStar(race);
            foreach (Star star in new[] { viaManufacture, viaQueue })
            {
                star.Colonists = 100000;
                star.ResourcesOnHand = new Resources(0, 0, 1, 1060);
                star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new AlchemyProductionUnit(race), true));
                star.ManufacturingQueue.Queue.Add(new ProductionOrder(3, new FactoryProductionUnit(race), false));
                star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new AlchemyProductionUnit(race), true));
            }

            new Manufacture(new ServerData()).Items(viaManufacture);
            viaQueue.ManufacturingQueue.ProcessYear(viaQueue, null);

            Assert.AreEqual(viaManufacture.ResourcesOnHand, viaQueue.ResourcesOnHand);
            Assert.AreEqual(viaManufacture.Factories, viaQueue.Factories);
            CollectionAssert.AreEqual(
                viaManufacture.ManufacturingQueue.Queue.Select(o => o.Name + o.Quantity + o.IsAutoBuild + o.Unit.RemainingCost.Energy),
                viaQueue.ManufacturingQueue.Queue.Select(o => o.Name + o.Quantity + o.IsAutoBuild + o.Unit.RemainingCost.Energy));
        }

        // ---- War Monger / Inner Strength bombs ----

        private static ShipDesign DesignWithOneBomb(Race race)
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>() };
            Component bomb = new Component { Cost = new Resources(0, 0, 0, 100), Type = ItemType.Bomb };
            hull.Modules.Add(new HullModule { AllocatedComponent = bomb, ComponentCount = 1 });
            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update(race);
            return design;
        }

        [TestCase("WM", 75)]
        [TestCase("IS", 125)]
        [TestCase("SS", 100)]
        public void Bombs_CostLessForWarMonger_AndMoreForInnerStrength(string primaryTrait, int expectedCost)
        {
            Race race = new Race();
            race.Traits.SetPrimary(primaryTrait);

            Assert.AreEqual(expectedCost, DesignWithOneBomb(race).Summary.Cost.Energy);
        }
    }
}
