namespace Nova.Tests.UnitTests
{
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    /// <summary>
    /// The production-template manager and the default-template copy
    /// (behavior-specs-10/production-queue.md section 9 "a 4-slot manager ... each of the 4 slots
    /// holds up to 12 auto-build entries plus a single stored flag bit"; 10f: on a successful
    /// colonisation or invasion the new owner's default template is copied into the planet's
    /// queue with the leftover flag; Alternate Reality skips types 0-2, Claim Adjuster 4-5).
    /// </summary>
    [TestFixture]
    public class ProductionTemplateTest
    {
        private static EmpireData MakeEmpire(ushort id, string primaryTrait = null)
        {
            EmpireData empire = new EmpireData { Id = id, Race = new Race() };
            if (primaryTrait != null)
            {
                empire.Race.Traits.SetPrimary(primaryTrait);
            }

            return empire;
        }

        private static ProductionTemplate Template(bool onlyLeftover, params (TemplateItemType Type, int Quantity)[] entries)
        {
            ProductionTemplate template = new ProductionTemplate { Name = "Test", OnlyLeftover = onlyLeftover };
            foreach ((TemplateItemType type, int quantity) in entries)
            {
                template.TryAdd(new ProductionTemplateEntry(type, quantity));
            }

            return template;
        }

        private static TemplateItemType?[] QueueTypes(Star star)
        {
            return star.ManufacturingQueue.Queue.Select(ProductionTemplateEntry.TypeOf).ToArray();
        }

        [Test]
        public void TheManager_HasFourSlots_OfTwelveEntries()
        {
            ProductionTemplateSet set = new ProductionTemplateSet();
            Assert.AreEqual(4, set.Slots.Count);

            ProductionTemplate template = new ProductionTemplate();
            for (int i = 0; i < 12; i++)
            {
                Assert.IsTrue(template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Mines, 1)));
            }

            Assert.IsFalse(template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Mines, 1)), "a 13th entry is refused");
            Assert.AreEqual(12, template.Entries.Count);
        }

        [Test]
        public void EntryQuantity_IsTheTenBitField()
        {
            Assert.AreEqual(1023, new ProductionTemplateEntry(TemplateItemType.Factories, 5000).Quantity);
            Assert.AreEqual(1, new ProductionTemplateEntry(TemplateItemType.Factories, 0).Quantity);
        }

        [Test]
        public void ManualExample_IsTheSection9Table()
        {
            ProductionTemplate example = ProductionTemplate.ManualExample();

            CollectionAssert.AreEqual(
                new[]
                {
                    TemplateItemType.MinTerraform, TemplateItemType.Factories, TemplateItemType.Mines, TemplateItemType.Defenses,
                    TemplateItemType.Factories, TemplateItemType.Mines, TemplateItemType.MaxTerraform, TemplateItemType.Defenses,
                },
                example.Entries.Select(entry => entry.Type).ToArray());
            CollectionAssert.AreEqual(new[] { 10, 10, 10, 2, 25, 25, 10, 5 }, example.Entries.Select(entry => entry.Quantity).ToArray());
        }

        [Test]
        public void Colonisation_CopiesTheDefaultTemplate_InOrder_WithTheLeftoverFlag()
        {
            ServerData serverState = new ServerData();
            Star star = new Star { Name = "New World" };
            serverState.AllStars.Add(star.Name, star);

            EmpireData empire = MakeEmpire(1);
            empire.ProductionTemplates.SetSlot(2, Template(true, (TemplateItemType.Factories, 10), (TemplateItemType.Mines, 5), (TemplateItemType.MinTerraform, 3)));
            empire.ProductionTemplates.DefaultSlot = 2;

            Fleet fleet = new Fleet("Colony", 1, 1, new NovaPoint(0, 0));
            fleet.Cargo.ColonistsInKilotons = 5;
            star.PendingColonizations.Add(new ColonizationAttempt(fleet, empire));

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(1, star.Owner);
            CollectionAssert.AreEqual(
                new TemplateItemType?[] { TemplateItemType.Factories, TemplateItemType.Mines, TemplateItemType.MinTerraform },
                QueueTypes(star));
            Assert.IsTrue(star.ManufacturingQueue.Queue.All(order => order.IsAutoBuild), "template entries are auto-build (category 1)");
            CollectionAssert.AreEqual(new[] { 10, 5, 3 }, star.ManufacturingQueue.Queue.Select(order => order.Quantity).ToArray());
            Assert.IsTrue(star.OnlyLeftover, "the leftover flag is copied from the template");
            Assert.IsTrue(((TerraformProductionUnit)star.ManufacturingQueue.Queue[2].Unit).MinimumOnly);
        }

        [Test]
        public void AlternateReality_SkipsMinesFactoriesAndDefenses()
        {
            Star star = new Star { Name = "AR world" };
            EmpireData empire = MakeEmpire(1, "AR");
            empire.ProductionTemplates.SetSlot(0, ProductionTemplate.ManualExample());

            ProductionTemplateSet.ApplyDefault(star, empire);

            CollectionAssert.AreEqual(
                new TemplateItemType?[] { TemplateItemType.MinTerraform, TemplateItemType.MaxTerraform },
                QueueTypes(star));
        }

        [Test]
        public void ClaimAdjuster_SkipsMinAndMaxTerraform()
        {
            Star star = new Star { Name = "CA world" };
            EmpireData empire = MakeEmpire(1, "CA");
            empire.ProductionTemplates.SetSlot(0, Template(false, (TemplateItemType.MinTerraform, 1), (TemplateItemType.Alchemy, 2), (TemplateItemType.MaxTerraform, 3), (TemplateItemType.Defenses, 4)));

            ProductionTemplateSet.ApplyDefault(star, empire);

            CollectionAssert.AreEqual(new TemplateItemType?[] { TemplateItemType.Alchemy, TemplateItemType.Defenses }, QueueTypes(star));
        }

        [Test]
        public void NoDefaultTemplate_LeavesThePlanetAlone()
        {
            Star star = new Star { Name = "Untouched", OnlyLeftover = true };
            EmpireData empire = MakeEmpire(1);
            empire.ProductionTemplates.SetSlot(0, ProductionTemplate.ManualExample());
            empire.ProductionTemplates.DefaultSlot = ProductionTemplateSet.NoDefault;

            ProductionTemplateSet.ApplyDefault(star, empire);

            Assert.AreEqual(0, star.ManufacturingQueue.Queue.Count);
            Assert.IsTrue(star.OnlyLeftover);
        }

        [Test]
        public void Invasion_CopiesTheCaptorsDefaultTemplate()
        {
            EmpireData attacker = MakeEmpire(1);
            EmpireData defender = MakeEmpire(2);
            attacker.EmpireReports.Add(defender.Id, new EmpireIntel(defender) { Relation = PlayerRelation.Enemy });
            attacker.ProductionTemplates.SetSlot(0, Template(false, (TemplateItemType.Defenses, 7)));

            Star star = new Star { Name = "Target", Owner = defender.Id, Colonists = 1000 };
            defender.OwnedStars.Add(star);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(3, new FactoryProductionUnit(defender.Race), false));

            Fleet fleet = new Fleet(1) { Owner = attacker.Id };
            fleet.InOrbit = star;
            fleet.Cargo.ColonistsInKilotons = 500;

            new InvadeTask().Perform(fleet, star, attacker, defender);

            Assert.AreEqual(attacker.Id, star.Owner);
            CollectionAssert.AreEqual(new TemplateItemType?[] { TemplateItemType.Defenses }, QueueTypes(star), "the defender's orders are gone, the captor's default template is in");
            Assert.AreEqual(7, star.ManufacturingQueue.Queue[0].Quantity);
        }

        [Test]
        public void FromQueue_KeepsOnlyAutoBuildEntries()
        {
            Race race = new Race();
            Star star = new Star { Name = "Source", OnlyLeftover = true };
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(4, new MineProductionUnit(race), true));
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(9, new FactoryProductionUnit(race), false));
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new TerraformProductionUnit(race, false), true));

            ProductionTemplate template = ProductionTemplate.FromQueue("Copied", star);

            CollectionAssert.AreEqual(new[] { TemplateItemType.Mines, TemplateItemType.MaxTerraform }, template.Entries.Select(entry => entry.Type).ToArray());
            Assert.IsTrue(template.OnlyLeftover);
        }

        [Test]
        public void TheCommand_StoresASlotAndTheDefault_AndSurvivesTheOrdersFile()
        {
            EmpireData empire = MakeEmpire(1);
            ProductionTemplateCommand store = new ProductionTemplateCommand(3, Template(true, (TemplateItemType.Alchemy, 1000)));
            ProductionTemplateCommand chooseDefault = new ProductionTemplateCommand(3);

            XmlDocument xmldoc = new XmlDocument();
            ProductionTemplateCommand storeRead = new ProductionTemplateCommand(store.ToXml(xmldoc));
            ProductionTemplateCommand defaultRead = new ProductionTemplateCommand(chooseDefault.ToXml(xmldoc));

            Assert.IsTrue(storeRead.IsValid(empire));
            storeRead.ApplyToState(empire);
            Assert.IsTrue(defaultRead.IsValid(empire));
            defaultRead.ApplyToState(empire);

            Assert.AreEqual(3, empire.ProductionTemplates.DefaultSlot);
            Assert.AreEqual(TemplateItemType.Alchemy, empire.ProductionTemplates[3].Entries.Single().Type);
            Assert.AreEqual(1000, empire.ProductionTemplates[3].Entries.Single().Quantity);
            Assert.IsTrue(empire.ProductionTemplates[3].OnlyLeftover);
            Assert.IsFalse(new ProductionTemplateCommand(4, new ProductionTemplate()).IsValid(empire), "only slots 0-3 exist");
        }

        [Test]
        public void Templates_SurviveTheEmpireFile()
        {
            EmpireData empire = MakeEmpire(1);
            empire.ProductionTemplates.SetSlot(1, ProductionTemplate.ManualExample());
            empire.ProductionTemplates.DefaultSlot = 1;

            XmlDocument xmldoc = new XmlDocument();
            XmlElement element = empire.ProductionTemplates.ToXml(xmldoc);
            ProductionTemplateSet reloaded = ProductionTemplateSet.FromXml(element);

            Assert.AreEqual(1, reloaded.DefaultSlot);
            Assert.AreEqual("Player's Guide example", reloaded[1].Name);
            CollectionAssert.AreEqual(
                ProductionTemplate.ManualExample().Entries.Select(entry => (entry.Type, entry.Quantity)).ToArray(),
                reloaded[1].Entries.Select(entry => (entry.Type, entry.Quantity)).ToArray());
            Assert.IsTrue(reloaded[0].IsEmpty);
        }
    }
}
