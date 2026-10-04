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
    /// behavior-specs-10/production-queue.md 10k item 3 and 10a (coverage rows 32, 33, 34, 37):
    /// terraform headroom by researched reach, the step chooser, Min/Max Terraform, the manual
    /// Terraform Environment clamp (message 303), message 123 per step, and the catalog predicate.
    /// </summary>
    [TestFixture]
    public class TerraformHeadroomTest
    {
        /// <summary>Default race: every axis 20..80, ideal 50; growth 15%.</summary>
        private static Race NewRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            race.GrowthRate = 15;
            race.ColonistsPerResource = 1000;
            race.OperableFactories = 10;
            race.OperableMines = 10;
            race.FactoryProduction = 10;
            return race;
        }

        private static Star NewStar(Race race, int gravity, int temperature, int radiation)
        {
            Star star = new Star();
            star.Name = "Tierra";
            star.Owner = 1;
            star.ThisRace = race;
            star.Gravity = star.OriginalGravity = gravity;
            star.Temperature = star.OriginalTemperature = temperature;
            star.Radiation = star.OriginalRadiation = radiation;
            return star;
        }

        private static Component TerraformComponent(string name)
        {
            return new Component { Name = name, Type = ItemType.Terraforming };
        }

        // ---- Reach ----

        [TestCase("Total ±3", -1, 3)]
        [TestCase("Total ±30", -1, 30)]
        [TestCase("Gravity ±7", 0, 7)]
        [TestCase("Temp ±11", 1, 11)]
        [TestCase("Radiation ±15", 2, 15)]
        [TestCase("Total Terraform ±5", -1, 5)]
        public void Reach_IsReadFromTheComponentName(string name, int axis, int amount)
        {
            Assert.IsTrue(TerraformReach.TryParse(name, out int parsedAxis, out int parsedAmount));
            Assert.AreEqual(axis, parsedAxis);
            Assert.AreEqual(amount, parsedAmount);
        }

        [Test]
        public void Reach_TotalSetsACommonReach_ABetterSingleAxisPartExtendsItsOwnAxis()
        {
            TerraformReach reach = TerraformReach.FromComponents(new[]
            {
                TerraformComponent("Total ±5"),
                TerraformComponent("Gravity ±11"),
                TerraformComponent("Radiation ±3"),
                new Component { Name = "Viewer 50", Type = ItemType.PlanetaryInstallations },
            });

            Assert.AreEqual(11, reach.Gravity);
            Assert.AreEqual(5, reach.Temperature);
            Assert.AreEqual(5, reach.Radiation, "A worse single-axis part does not lower the common reach");
        }

        [Test]
        public void Reach_OfTheRealComponentData_TotalTerraform3()
        {
            Component total3 = new AllComponents().Fetch("Total ±3");
            Assert.IsNotNull(total3);

            TerraformReach reach = TerraformReach.FromComponents(new[] { total3 });
            Assert.AreEqual(3, reach.Gravity);
            Assert.AreEqual(3, reach.Temperature);
            Assert.AreEqual(3, reach.Radiation);
        }

        [Test]
        public void Reach_WithNoTerraformingTechnology_GivesNoHeadroom()
        {
            Race race = NewRace();
            Star star = NewStar(race, 30, 50, 50);

            Assert.AreEqual(0, TerraformProductionUnit.Headroom(star, race, TerraformReach.FromComponents(new Component[0])));
        }

        // ---- Headroom ----

        [Test]
        public void Headroom_IsTheStepsToEachAxisTarget_TheIdealCappedByTheReachFromTheOriginal()
        {
            Race race = NewRace();

            // Gravity 30 (ideal 50, reach 7 -> target 37: 7 steps), temperature at the ideal (0),
            // radiation 47 (target 50: 3 steps).
            Star star = NewStar(race, 30, 50, 47);
            Assert.AreEqual(10, TerraformProductionUnit.Headroom(star, race, new TerraformReach(7, 7, 7)));
        }

        [Test]
        public void Headroom_IsMeasuredFromTheOriginalValue_NotTheCurrentOne()
        {
            Race race = NewRace();
            Star star = NewStar(race, 30, 50, 50);
            star.Gravity = 35; // already terraformed 5 of the 7

            Assert.AreEqual(2, TerraformProductionUnit.Headroom(star, race, new TerraformReach(7, 7, 7)));
        }

        [Test]
        public void Headroom_AnImmuneAxisCountsZero()
        {
            Race race = NewRace();
            race.GravityTolerance.Immune = true;
            Star star = NewStar(race, 30, 50, 50);

            Assert.AreEqual(0, TerraformProductionUnit.Headroom(star, race, new TerraformReach(7, 7, 7)));
        }

        [Test]
        public void Headroom_TargetIsHeldTo1Through99()
        {
            Race race = NewRace();
            race.GravityTolerance.MinimumValue = 0;
            race.GravityTolerance.MaximumValue = 0; // ideal 0
            Star star = NewStar(race, 3, 50, 50);

            // Ideal 0 within reach 10 of 3, but no value goes below 1: 2 steps (3 -> 1).
            Assert.AreEqual(2, TerraformProductionUnit.Headroom(star, race, new TerraformReach(10, 10, 10)));
        }

        // ---- Step chooser ----

        [Test]
        public void StepChooser_FixesAnOutOfRangeAxisFirst()
        {
            Race race = NewRace();

            // Radiation 85 is 5 clicks out of the 20..80 band; gravity 40 is in band but off ideal.
            Star star = NewStar(race, 40, 50, 85);
            TerraformReach reach = new TerraformReach(10, 10, 10);

            Assert.AreEqual(TerraformReach.RadiationAxis, TerraformProductionUnit.ChooseAxis(star, race, reach));
        }

        [Test]
        public void StepChooser_TiesGoToGravityThenTemperature()
        {
            Race race = NewRace();
            Star star = NewStar(race, 50, 40, 60); // temperature and radiation mirror each other
            TerraformReach reach = new TerraformReach(10, 10, 10);

            Assert.AreEqual(TerraformReach.TemperatureAxis, TerraformProductionUnit.ChooseAxis(star, race, reach));
        }

        [Test]
        public void StepChooser_MovesOnePointTowardTheTarget()
        {
            Race race = NewRace();
            Star star = NewStar(race, 50, 50, 60);

            TerraformStep step = TerraformProductionUnit.Step(star, race, new TerraformReach(10, 10, 10));

            Assert.AreEqual(TerraformReach.RadiationAxis, step.Axis);
            Assert.IsFalse(step.Increased);
            Assert.AreEqual(59, step.NewValue);
            Assert.AreEqual(59, star.Radiation);
            Assert.AreEqual(60, star.OriginalRadiation, "The original value is never changed");
        }

        [Test]
        public void StepChooser_NothingToMove_ReturnsNoStep()
        {
            Race race = NewRace();
            Star star = NewStar(race, 50, 50, 50);

            Assert.IsNull(TerraformProductionUnit.Step(star, race, new TerraformReach(10, 10, 10)));
        }

        // ---- Min / Max Terraform ----

        [Test]
        public void MaxTerraform_BuysTheSmallerOfNAndTheHeadroomEachTurn()
        {
            Race race = NewRace();
            Star star = NewStar(race, 47, 50, 50); // 3 steps of headroom
            star.Colonists = 100000;
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);

            TerraformProductionUnit unit = new TerraformProductionUnit(race) { Reach = new TerraformReach(10, 10, 10) };
            ProductionOrder order = new ProductionOrder(5, unit, true);
            star.ManufacturingQueue.Queue.Add(order);

            Assert.AreEqual(3, order.Process(star));
            Assert.AreEqual(50, star.Gravity);
            Assert.AreEqual(700, star.ResourcesOnHand.Energy);
            Assert.AreEqual(5, order.Quantity, "A standing order is never consumed");

            Assert.AreEqual(0, order.Process(star), "No headroom left: skipped, nothing spent");
            Assert.AreEqual(700, star.ResourcesOnHand.Energy);
        }

        [Test]
        public void MaxTerraform_BuysOnlyNPerTurnWhenTheHeadroomIsLarger()
        {
            Race race = NewRace();
            Star star = NewStar(race, 40, 50, 50);
            star.Colonists = 100000;
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);

            ProductionOrder order = new ProductionOrder(2, new TerraformProductionUnit(race) { Reach = new TerraformReach(10, 10, 10) }, true);
            star.ManufacturingQueue.Queue.Add(order);

            Assert.AreEqual(2, order.Process(star));
            Assert.AreEqual(42, star.Gravity);
        }

        [Test]
        public void MinTerraform_OnAPositivePlanetThatIsGrowing_BuysNothing()
        {
            Race race = NewRace();
            Star star = NewStar(race, 40, 50, 50);
            star.Colonists = 10000;
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);
            Assert.Greater(race.HabPercent(star), 0);
            Assert.GreaterOrEqual(star.CalculateGrowth(race), 0);

            ProductionOrder order = new ProductionOrder(3, new TerraformProductionUnit(race, true) { Reach = new TerraformReach(10, 10, 10) }, true);
            star.ManufacturingQueue.Queue.Add(order);

            Assert.AreEqual(0, order.Process(star));
            Assert.AreEqual(1000, star.ResourcesOnHand.Energy);
            Assert.AreEqual(40, star.Gravity);
        }

        [Test]
        public void MinTerraform_OnANegativePlanet_Buys()
        {
            Race race = NewRace();
            Star star = NewStar(race, 85, 50, 50); // 5 out of band: value -5
            star.Colonists = 10000;
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);
            Assert.Less(race.HabPercent(star), 0);

            ProductionOrder order = new ProductionOrder(3, new TerraformProductionUnit(race, true) { Reach = new TerraformReach(10, 10, 10) }, true);
            star.ManufacturingQueue.Queue.Add(order);

            Assert.AreEqual(3, order.Process(star));
            Assert.AreEqual(82, star.Gravity);
        }

        [Test]
        public void MinTerraform_OnAPositivePlanetWhosePopulationIsFalling_Buys()
        {
            Race race = NewRace();
            Star star = NewStar(race, 40, 50, 50);
            star.Colonists = 5000000; // far over capacity: declining
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);
            Assert.Less(star.CalculateGrowth(race), 0);

            ProductionOrder order = new ProductionOrder(1, new TerraformProductionUnit(race, true) { Reach = new TerraformReach(10, 10, 10) }, true);
            star.ManufacturingQueue.Queue.Add(order);

            Assert.AreEqual(1, order.Process(star));
        }

        [Test]
        public void MinTerraform_SurvivesAnXmlRoundTrip_WithItsReach()
        {
            Race race = NewRace();
            TerraformProductionUnit unit = new TerraformProductionUnit(race, true) { Reach = new TerraformReach(3, 7, 11) };

            XmlDocument doc = new XmlDocument();
            TerraformProductionUnit copy = new TerraformProductionUnit(unit.ToXml(doc));

            Assert.IsTrue(copy.MinimumOnly);
            Assert.AreEqual(3, copy.Reach.Gravity);
            Assert.AreEqual(7, copy.Reach.Temperature);
            Assert.AreEqual(11, copy.Reach.Radiation);
            Assert.AreEqual(unit.Cost, copy.Cost);
        }

        // ---- Manual Terraform Environment: clamp to headroom, message 303 ----

        private static ServerData ServerWithEmpire(Race race, params string[] componentNames)
        {
            ServerData server = new ServerData();
            EmpireData empire = new SimpleEmpireData();
            empire.Id = 1;
            empire.Race = race;
            empire.AvailableComponents = new RaceComponents();
            foreach (string name in componentNames)
            {
                empire.AvailableComponents.Add(name, TerraformComponent(name));
            }

            server.AllEmpires.Add(empire.Id, empire);
            return server;
        }

        [Test]
        public void ManualTerraform_OverTheHeadroom_IsCutToIt_WithMessage303()
        {
            Race race = NewRace();
            ServerData server = ServerWithEmpire(race, "Total ±3");
            Star star = NewStar(race, 40, 50, 50); // reach 3: 3 steps
            star.Colonists = 100000;
            star.ResourcesOnHand = new Resources(0, 0, 0, 150);

            ProductionOrder order = new ProductionOrder(10, new TerraformProductionUnit(race), false);
            star.ManufacturingQueue.Queue.Add(order);

            new Manufacture(server).Items(star);

            Assert.AreEqual(41, star.Gravity, "One step bought with 150 resources");
            Assert.AreEqual(2, order.Quantity, "Cut to the 3 steps of headroom, one bought");
            Assert.AreEqual(1, server.AllMessages.Count(m => m.Text.Contains("exceeded the allowed maximum") && m.Text.Contains("reduced to 3")));
            Assert.AreEqual(1, server.AllMessages.Count(m => m.Text.Contains("Gravity to 41")), "Message 123 for the one step");
        }

        [Test]
        public void ManualTerraform_WithNoHeadroom_IsDeleted_AndTheWalkGoesOn()
        {
            Race race = NewRace();
            ServerData server = ServerWithEmpire(race); // no terraform technology
            Star star = NewStar(race, 40, 50, 50);
            star.Colonists = 100000;
            star.ResourcesOnHand = new Resources(0, 0, 10, 150);

            ProductionOrder terraform = new ProductionOrder(2, new TerraformProductionUnit(race), false);
            ProductionOrder factory = new ProductionOrder(1, new FactoryProductionUnit(race), false);
            star.ManufacturingQueue.Queue.Add(terraform);
            star.ManufacturingQueue.Queue.Add(factory);

            new Manufacture(server).Items(star);

            Assert.AreEqual(40, star.Gravity);
            Assert.AreEqual(1, star.Factories, "The entry behind the deleted one was bought");
            Assert.AreEqual(0, star.ManufacturingQueue.Queue.Count);
            Assert.AreEqual(1, server.AllMessages.Count(m => m.Text.Contains("terraforming order") && m.Text.Contains("removed")));
        }

        [Test]
        public void Terraform_PostsMessage123_OncePerStep()
        {
            Race race = NewRace();
            ServerData server = ServerWithEmpire(race, "Total ±5");
            Star star = NewStar(race, 47, 50, 50);
            star.Colonists = 100000;
            star.ResourcesOnHand = new Resources(0, 0, 0, 1000);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(5, new TerraformProductionUnit(race), true));

            new Manufacture(server).Items(star);

            List<string> steps = server.AllMessages.Where(m => m.Text.StartsWith("Your terraforming")).Select(m => m.Text).ToList();
            CollectionAssert.AreEqual(
                new[]
                {
                    "Your terraforming on Tierra has increased the Gravity to 48.",
                    "Your terraforming on Tierra has increased the Gravity to 49.",
                    "Your terraforming on Tierra has increased the Gravity to 50.",
                },
                steps);
        }

        [Test]
        public void Manufacture_StampsTheOwnersCurrentReachOnTerraformEntries()
        {
            Race race = NewRace();
            ServerData server = ServerWithEmpire(race, "Gravity ±7");
            Star star = NewStar(race, 50, 50, 50);
            star.Colonists = 100000;
            TerraformProductionUnit unit = new TerraformProductionUnit(race);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, unit, true));

            new Manufacture(server).Items(star);

            Assert.AreEqual(7, unit.Reach.Gravity);
            Assert.AreEqual(0, unit.Reach.Temperature);
        }

        // ---- Catalog predicate (row 37) ----

        [Test]
        public void Catalog_OffersTerraformEnvironment_OnlyWhenThereIsHeadroom()
        {
            Race race = NewRace();
            EmpireData empire = ServerWithEmpire(race, "Total ±3").AllEmpires[1];

            Assert.IsTrue(TerraformProductionUnit.CatalogOffersTerraformEnvironment(NewStar(race, 40, 50, 50), empire));
            Assert.IsFalse(TerraformProductionUnit.CatalogOffersTerraformEnvironment(NewStar(race, 50, 50, 50), empire), "An ideal planet");

            EmpireData noTech = ServerWithEmpire(race).AllEmpires[1];
            Assert.IsFalse(TerraformProductionUnit.CatalogOffersTerraformEnvironment(NewStar(race, 40, 50, 50), noTech), "No terraforming technology");
        }
    }
}
