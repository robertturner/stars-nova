namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// behavior-specs-11/production-queue.md 10l: the production catalog's available-item list
    /// (<c>FUN_10d0_010c</c>) and the entries the dialog drops when it opens. Every expected figure
    /// is the specification's:
    /// - the auto Mineral Packets entry is offered always, even with no mass driver;
    /// - Factory / Mine / Defenses are never offered for Alternate Reality and only while the
    ///   build room is above 0, pre-filled with the room (at most 1,020);
    /// - the type-27 "best planetary scanner" item is offered, one unit, only while the planet has
    ///   no scanner and the race is not Alternate Reality;
    /// - Terraform Environment is offered only with headroom, pre-filled with it;
    /// - an entry with no matching catalog item is dropped when the dialog opens.
    /// </summary>
    [TestFixture]
    public class ProductionCatalogTest
    {
        private static long nextKey = 40000;

        private static Race DefaultRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.OperableFactories = 10;
            race.MineProductionRate = 10;
            race.MineBuildCost = 5;
            race.OperableMines = 10;
            race.GrowthRate = 0;
            return race;
        }

        private static EmpireData EmpireFor(Race race, params string[] componentNames)
        {
            EmpireData empire = new SimpleEmpireData { Id = 1, Race = race };
            empire.AvailableComponents = new RaceComponents();
            foreach (string name in componentNames)
            {
                empire.AvailableComponents.Add(name, new Component { Name = name, Type = ItemType.Terraforming });
            }

            return empire;
        }

        private static Star Planet(Race race, int gravity, int temperature, int radiation)
        {
            Star star = new Star { Name = "Tierra", Owner = 1, ThisRace = race };
            star.Gravity = star.OriginalGravity = gravity;
            star.Temperature = star.OriginalTemperature = temperature;
            star.Radiation = star.OriginalRadiation = radiation;
            star.Colonists = 100000;
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);
            return star;
        }

        private static Star IdealPlanet(Race race)
        {
            return Planet(race, race.GravityTolerance.OptimumLevel, race.TemperatureTolerance.OptimumLevel, race.RadiationTolerance.OptimumLevel);
        }

        private static Fleet Starbase(ushort owner, params int[] driverWarps)
        {
            Fleet starbase = new Fleet(nextKey++);
            starbase.Owner = owner;
            starbase.Type = ItemType.Starbase;

            ShipDesign design = new ShipDesign(nextKey++);
            design.Blueprint = new Component();
            Hull hull = new Hull { Modules = new List<HullModule>() };
            foreach (int warp in driverWarps)
            {
                Component driver = new Component { Name = "Mass Driver " + warp };
                driver.Properties.Add("Mass Driver", new MassDriver(warp));
                hull.Modules.Add(new HullModule { AllocatedComponent = driver, ComponentCount = 1 });
            }

            design.Blueprint.Properties.Add("Hull", hull);
            ShipToken token = new ShipToken(design, 1);
            starbase.Composition.Add(token.Key, token);
            return starbase;
        }

        // ------------------------------------------------------------------ auto packets

        [Test]
        public void AutoPacket_IsOfferedAlways_EvenWithNoMassDriver()
        {
            Race race = DefaultRace();
            EmpireData empire = EmpireFor(race);
            Star star = IdealPlanet(race);
            Assert.IsNull(star.Starbase);

            IReadOnlyList<ProductionCatalogEntry> catalog = ProductionCatalog.Build(star, empire);

            Assert.That(catalog.Any(entry => entry.Kind == ProductionCatalogKind.AutoPacket), Is.True);
            Assert.That(catalog.Any(entry => entry.Kind == ProductionCatalogKind.ManualPacket), Is.False);

            star.Starbase = Starbase(1, 5);
            catalog = ProductionCatalog.Build(star, empire);
            Assert.That(catalog.Any(entry => entry.Kind == ProductionCatalogKind.AutoPacket), Is.True);
            Assert.That(catalog.Any(entry => entry.Kind == ProductionCatalogKind.ManualPacket), Is.True);
        }

        // ------------------------------------------------------------------ installations

        [Test]
        public void Installations_AreNeverOfferedForAlternateReality()
        {
            Race race = DefaultRace();
            race.Traits.SetPrimary("AR");
            Star star = IdealPlanet(race);

            IReadOnlyList<ProductionCatalogEntry> catalog = ProductionCatalog.Build(star, EmpireFor(race));

            Assert.That(catalog.Any(entry => entry.Kind == ProductionCatalogKind.Factory), Is.False);
            Assert.That(catalog.Any(entry => entry.Kind == ProductionCatalogKind.Mine), Is.False);
            Assert.That(catalog.Any(entry => entry.Kind == ProductionCatalogKind.Defense), Is.False);
            Assert.That(catalog.Any(entry => entry.Kind == ProductionCatalogKind.Alchemy), Is.True, "Alchemy is still offered");
        }

        [Test]
        public void Installations_AreOfferedOnlyWithRoom_AndPrefilledWithTheRoom()
        {
            Race race = DefaultRace();
            EmpireData empire = EmpireFor(race);
            Star star = IdealPlanet(race);

            int cap = star.GetBuildCapFactories();
            Assert.Greater(cap, 0, "test setup: the ideal planet has factory room");

            ProductionCatalogEntry factory = ProductionCatalog.Build(star, empire)
                .Single(entry => entry.Kind == ProductionCatalogKind.Factory);
            int expected = Math.Min(cap, ProductionCatalog.ManualInstallationQuantityCap);
            Assert.AreEqual(expected, factory.PresetQuantity, "pre-filled with the whole room");
            Assert.AreEqual(expected, factory.MaxQuantity, "limited to the room");

            star.Factories = cap; // no room left
            Assert.That(ProductionCatalog.Build(star, empire).Any(entry => entry.Kind == ProductionCatalogKind.Factory), Is.False);
        }

        // ------------------------------------------------------------------ scanner

        [Test]
        public void Scanner_IsOneUnit_OnlyWithNoScanner_AndNotForAlternateReality()
        {
            Race race = DefaultRace();
            Star star = IdealPlanet(race);
            EmpireData empire = EmpireFor(race);

            ProductionCatalogEntry scanner = ProductionCatalog.Build(star, empire)
                .Single(entry => entry.Kind == ProductionCatalogKind.Scanner);
            Assert.AreEqual(1, scanner.PresetQuantity);
            Assert.AreEqual(1, scanner.MaxQuantity, "one unit");
            Assert.IsTrue(scanner.IsScanner);

            star.ScannerType = "Viewer 50";
            Assert.That(ProductionCatalog.Build(star, empire).Any(entry => entry.Kind == ProductionCatalogKind.Scanner), Is.False);

            Race ar = DefaultRace();
            ar.Traits.SetPrimary("AR");
            Star arStar = IdealPlanet(ar);
            Assert.That(ProductionCatalog.Build(arStar, EmpireFor(ar)).Any(entry => entry.Kind == ProductionCatalogKind.Scanner), Is.False);
        }

        // ------------------------------------------------------------------ terraform

        [Test]
        public void TerraformEnvironment_IsPrefilledWithTheHeadroom_AndOnlyHealthyWhenThereIsHeadroom()
        {
            Race race = DefaultRace();
            EmpireData empire = EmpireFor(race, "Total ±3");

            // Ideal planet: no headroom at all.
            Assert.That(ProductionCatalog.Build(IdealPlanet(race), empire)
                .Any(entry => entry.Kind == ProductionCatalogKind.TerraformEnvironment), Is.False);

            // Gravity 40 against the ideal 50 with a reach of 3: three steps.
            ProductionCatalogEntry terraform = ProductionCatalog.Build(Planet(race, 40, 50, 50), empire)
                .Single(entry => entry.Kind == ProductionCatalogKind.TerraformEnvironment);
            Assert.AreEqual(3, terraform.PresetQuantity, "pre-filled with the headroom");
            Assert.AreEqual(3, terraform.MaxQuantity, "limited to the headroom");
        }

        [Test]
        public void AutoTerraform_IsNotOfferedToClaimAdjusters()
        {
            Race race = DefaultRace();
            Assert.That(ProductionCatalog.Build(IdealPlanet(race), EmpireFor(race))
                .Any(entry => entry.Kind == ProductionCatalogKind.AutoTerraform), Is.True);

            Race ca = DefaultRace();
            ca.Traits.SetPrimary("CA");
            Assert.That(ProductionCatalog.Build(IdealPlanet(ca), EmpireFor(ca))
                .Any(entry => entry.Kind == ProductionCatalogKind.AutoTerraform), Is.False);
        }

        // ------------------------------------------------------------------ orphan entries

        [Test]
        public void ManualPacket_OnAPlanetWithNoMassDriver_IsDropped()
        {
            Race race = DefaultRace();
            EmpireData empire = EmpireFor(race);
            Star star = IdealPlanet(race);
            ProductionOrder order = new ProductionOrder(1, new PacketProductionUnit(race, PacketMineral.Mixed, false), false);
            star.ManufacturingQueue.Queue.Add(order);

            ProductionCatalogQueueFilter filter = ProductionCatalog.FilterQueue(star, empire, ProductionCatalog.Build(star, empire));
            CollectionAssert.AreEqual(new[] { order }, filter.Dropped);

            star.Starbase = Starbase(1, 5);
            Assert.IsEmpty(ProductionCatalog.FilterQueue(star, empire, ProductionCatalog.Build(star, empire)).Dropped);
        }

        [Test]
        public void ScannerOrder_OnAPlanetThatHasAScanner_IsDropped()
        {
            Race race = DefaultRace();
            EmpireData empire = EmpireFor(race);
            Star star = IdealPlanet(race);
            star.ScannerType = "Viewer 50";
            ProductionOrder order = new ProductionOrder(1, new ScannerProductionUnit(), false);
            star.ManufacturingQueue.Queue.Add(order);

            CollectionAssert.AreEqual(new[] { order },
                ProductionCatalog.FilterQueue(star, empire, ProductionCatalog.Build(star, empire)).Dropped);

            // And an ordinary queued item stays.
            star.ManufacturingQueue.Queue.Remove(order);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new AlchemyProductionUnit(race), false));
            Assert.IsEmpty(ProductionCatalog.FilterQueue(star, empire, ProductionCatalog.Build(star, empire)).Dropped);
        }

        [Test]
        public void FactoryOrder_WhenTheBuildCapIsFilled_IsDropped()
        {
            Race race = DefaultRace();
            EmpireData empire = EmpireFor(race);
            Star star = IdealPlanet(race);
            star.Factories = star.GetBuildCapFactories();
            ProductionOrder order = new ProductionOrder(1, new FactoryProductionUnit(race), false);
            star.ManufacturingQueue.Queue.Add(order);

            CollectionAssert.AreEqual(new[] { order },
                ProductionCatalog.FilterQueue(star, empire, ProductionCatalog.Build(star, empire)).Dropped);
        }

        [Test]
        public void AutoBuildFactoryOrder_IsNotDropped_EvenWhenTheManualRowIsAtTheCap()
        {
            Race race = DefaultRace();
            EmpireData empire = EmpireFor(race);
            Star star = IdealPlanet(race);
            star.Factories = star.GetBuildCapFactories(); // the manual row disappears
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(5, new FactoryProductionUnit(race), true));

            Assert.IsEmpty(ProductionCatalog.FilterQueue(star, empire, ProductionCatalog.Build(star, empire)).Dropped);

            // Alternate Reality has no auto Factories, so the same order is dropped.
            Race ar = DefaultRace();
            ar.Traits.SetPrimary("AR");
            Star arStar = IdealPlanet(ar);
            arStar.ManufacturingQueue.Queue.Add(new ProductionOrder(5, new FactoryProductionUnit(ar), true));
            Assert.AreEqual(1, ProductionCatalog.FilterQueue(arStar, EmpireFor(ar), ProductionCatalog.Build(arStar, EmpireFor(ar))).Dropped.Count);
        }
    }

    /// <summary>
    /// The type-27 scanner item's server side: installing the best scanner the owner has
    /// (behavior-specs-11/production-queue.md 10d), and the turn-generation research breakthrough
    /// upgrading only planets that already have a scanner (never installing a first one).
    /// </summary>
    [TestFixture]
    public class PlanetaryScannerProductionTest
    {
        private static Race DefaultRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            race.ColonistsPerResource = 1000;
            return race;
        }

        private static Component ScannerComponent(string name, int range)
        {
            Component component = new Component { Name = name, Type = ItemType.PlanetaryInstallations };
            component.Properties.Add("Scanner", new Scanner { NormalScan = range });
            return component;
        }

        [Test]
        public void BestScanner_IsTheHighestNormalRange()
        {
            EmpireData empire = new SimpleEmpireData { Id = 1 };
            empire.AvailableComponents = new RaceComponents();
            empire.AvailableComponents.Add("Viewer 50", ScannerComponent("Viewer 50", 50));
            empire.AvailableComponents.Add("Viewer 90", ScannerComponent("Viewer 90", 90));
            empire.AvailableComponents.Add("Scoper 150", ScannerComponent("Scoper 150", 150));

            Assert.AreEqual("Scoper 150", ScannerProductionUnit.BestScanner(empire).Name);
        }

        [Test]
        public void CompletingTheScannerItem_InstallsTheBestScanner()
        {
            Race race = DefaultRace();
            ServerData server = new ServerData();
            EmpireData empire = new SimpleEmpireData { Id = 1, Race = race };
            empire.AvailableComponents = new RaceComponents();
            empire.AvailableComponents.Add("Viewer 50", ScannerComponent("Viewer 50", 50));
            empire.AvailableComponents.Add("Scoper 150", ScannerComponent("Scoper 150", 150));
            server.AllEmpires.Add(empire.Id, empire);

            Star star = new Star { Name = "Tierra", Owner = 1, ThisRace = race, Colonists = 100000 };
            star.ScannerType = "None";
            star.ResourcesOnHand = new Resources(100, 100, 100, 1000);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ScannerProductionUnit(), false));
            server.AllStars.Add(star.Key, star);
            empire.OwnedStars.Add(star);

            new Manufacture(server).Items(star);

            Assert.AreEqual("Scoper 150", star.ScannerType);
            Assert.AreEqual(150, star.ScanRange);
            Assert.AreEqual(0, star.ManufacturingQueue.Queue.Count, "the one unit is consumed");
        }

        [Test]
        public void ResearchUpgrade_UpgradesOnlyPlanetsThatAlreadyHaveAScanner()
        {
            Race race = DefaultRace();
            ServerData server = new ServerData();
            EmpireData empire = new SimpleEmpireData { Id = 1, Race = race };
            empire.AvailableComponents = new RaceComponents();
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                empire.ResearchLevels[field] = 0;
            }

            empire.ResearchLevels[TechLevel.ResearchField.Electronics] = 2;
            server.AllEmpires.Add(empire.Id, empire);

            Star existing = new Star { Name = "Existing", Owner = 1, ThisRace = race, Colonists = 10000, ScannerType = "Viewer 90", ScanRange = 90 };
            Star none = new Star { Name = "None", Owner = 1, ThisRace = race, Colonists = 10000, ScannerType = "None" };
            server.AllStars.Add(existing.Key, existing);
            server.AllStars.Add(none.Key, none);
            empire.OwnedStars.Add(existing);
            empire.OwnedStars.Add(none);

            StarUpdateStep step = new StarUpdateStep(new Random(1));
            typeof(StarUpdateStep).GetField("serverState", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(step, server);
            typeof(StarUpdateStep).GetMethod("TechLevelUp", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(step, new object[] { TechLevel.ResearchField.Electronics, empire });

            Assert.AreEqual(3, empire.ResearchLevels[TechLevel.ResearchField.Electronics]);
            Assert.AreEqual("Scoper 150", existing.ScannerType, "an installed scanner is upgraded");
            Assert.AreEqual("None", none.ScannerType, "a research breakthrough never installs a first scanner");
            Assert.AreEqual(0, none.ScanRange);
        }
    }
}
