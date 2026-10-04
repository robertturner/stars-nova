namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Reflection;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // behavior-specs-10/fleet-movement-scanning-cargo.md §5 Scrap Fleet ("The flag") and
    // ship-design-and-components.md §16 (+0x7c bit 0x80): at the end of a generation every in-use
    // non-starbase ship design whose hull or any part is not fully available to its owner now
    // (trait or tech, or a gift-only part never given) is flagged; the flag is never cleared, and
    // Scrap Fleet / Colonize / the Ultimate Recycling accumulator credit a flagged stack at
    // cost / 4 (integer).
    [TestFixture]
    public class DesignLegalityStepTest
    {
        private ServerData serverState;
        private EmpireData empire;

        [SetUp]
        public void Init()
        {
            serverState = new ServerData();
            empire = new EmpireData { Id = 1, Race = new Race() };
            serverState.AllEmpires.Add(empire.Id, empire);
        }

        private static Component Part(string name, int propulsionNeeded)
        {
            Component part = new Component { Name = name, Mass = 5 };
            part.RequiredTech[TechLevel.ResearchField.Propulsion] = propulsionNeeded;
            return part;
        }

        private ShipDesign AddDesign(Component part, bool starbase = false)
        {
            Component blueprint = new Component { Name = "Hull", Mass = 20, Cost = new Resources(40, 0, 0, 100) };
            // FuelCapacity 0 makes the hull a starbase (Hull.IsStarbase).
            Hull hull = new Hull { ArmorStrength = 20, FuelCapacity = starbase ? 0 : 100, Modules = new List<HullModule>() };
            if (part != null)
            {
                HullModule module = new HullModule { ComponentCount = 1, AllocatedComponent = part };
                hull.Modules.Add(module);
            }

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(empire.GetNextDesignKey()) { Blueprint = blueprint, Name = "Design" };
            design.Update();
            empire.Designs[design.Key] = design;
            return design;
        }

        [Test]
        public void PartAboveTheOwnersTech_FlagsTheDesign()
        {
            ShipDesign design = AddDesign(Part("Fancy Engine", 5));

            new DesignLegalityStep().Process(serverState);

            Assert.IsTrue(design.FailedLegality);
        }

        [Test]
        public void FullyAvailableDesign_IsNotFlagged()
        {
            empire.ResearchLevels[TechLevel.ResearchField.Propulsion] = 5;
            ShipDesign design = AddDesign(Part("Fancy Engine", 5));

            new DesignLegalityStep().Process(serverState);

            Assert.IsFalse(design.FailedLegality);
        }

        [Test]
        public void HullAboveTheOwnersTech_FlagsTheDesign()
        {
            ShipDesign design = AddDesign(null);
            design.Blueprint.RequiredTech[TechLevel.ResearchField.Construction] = 3;

            new DesignLegalityStep().Process(serverState);

            Assert.IsTrue(design.FailedLegality);
        }

        [Test]
        public void PartBarredByTheOwnersTrait_FlagsTheDesign()
        {
            empire.Race.Traits.SetPrimary("JOAT");
            Component part = Part("Exclusive Part", 0);
            part.Restrictions.SetRestriction("JOAT", RaceAvailability.not_available);
            ShipDesign design = AddDesign(part);

            new DesignLegalityStep().Process(serverState);

            Assert.IsTrue(design.FailedLegality);
        }

        [Test]
        public void GiftOnlyPart_NeverGiven_FlagsTheDesign_ButAGivenOneDoesNot()
        {
            ShipDesign notGiven = AddDesign(Part("Jump Gate", 0));
            new DesignLegalityStep().Process(serverState);
            Assert.IsTrue(notGiven.FailedLegality, "A gift-only part the owner was never given");

            Init();
            Component given = Part("Jump Gate", 0);
            empire.AvailableComponents.Add(given);
            ShipDesign givenDesign = AddDesign(given);
            new DesignLegalityStep().Process(serverState);
            Assert.IsFalse(givenDesign.FailedLegality);
        }

        [Test]
        public void StarbaseDesigns_AreNeverFlagged()
        {
            ShipDesign design = AddDesign(Part("Fancy Gate", 9), starbase: true);

            new DesignLegalityStep().Process(serverState);

            Assert.IsFalse(design.FailedLegality);
        }

        [Test]
        public void TheFlag_IsNeverCleared_EvenAfterTheOwnerReachesTheTech()
        {
            ShipDesign design = AddDesign(Part("Fancy Engine", 5));
            new DesignLegalityStep().Process(serverState);
            Assert.IsTrue(design.FailedLegality);

            empire.ResearchLevels[TechLevel.ResearchField.Propulsion] = 26;
            new DesignLegalityStep().Process(serverState);

            Assert.IsTrue(design.FailedLegality);
        }

        [Test]
        public void TheFlag_SurvivesTheDesignXmlRoundTrip()
        {
            ShipDesign design = AddDesign(null);
            design.Icon = new ShipIcon("Dummy0001.png", null);
            design.FailedLegality = true;

            XmlDocument xmldoc = new XmlDocument();
            XmlElement element = design.ToXml(xmldoc);
            ShipDesign reloaded = new ShipDesign(element);

            Assert.IsTrue(reloaded.FailedLegality);

            design.FailedLegality = false;
            Assert.IsFalse(new ShipDesign(design.ToXml(new XmlDocument())).FailedLegality);
        }

        [Test]
        public void TheStep_IsRegisteredBeforeTheScanStep()
        {
            TurnGenerator generator = new SimpleTurnGenerator(serverState);
            SortedList<int, ITurnStep> steps = (SortedList<int, ITurnStep>)typeof(TurnGenerator)
                .GetField("turnSteps", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(generator);

            int legalityKey = -1;
            int scanKey = -1;
            foreach (KeyValuePair<int, ITurnStep> step in steps)
            {
                if (step.Value is DesignLegalityStep)
                {
                    legalityKey = step.Key;
                }

                if (step.Value is ScanStep)
                {
                    scanKey = step.Key;
                }
            }

            Assert.Greater(legalityKey, 0, "DesignLegalityStep is registered");
            Assert.Less(legalityKey, scanKey);
        }

        // ---- the quarter rule in the valuation ----

        private static Fleet FleetOf(ShipDesign design, int quantity)
        {
            Fleet fleet = new Fleet(1) { Owner = 1, Name = "Scrapper" };
            ShipToken token = new ShipToken(design, quantity);
            fleet.Composition.Add(token.Key, token);
            return fleet;
        }

        /// <summary>4 ships x 30 Ironium = 120; flagged: 120 / 4 = 30; at a bare planet
        /// floor(30 / 3) = 10 (40 unflagged).</summary>
        [Test]
        public void ScrapFleet_CreditsAFlaggedStackAtAQuarter()
        {
            ShipDesign design = ScrapFleetRecoveryTest.MakeDesign(new Resources(30, 0, 0, 0));
            design.FailedLegality = true;
            Fleet fleet = FleetOf(design, 4);
            Star star = new Star { Name = "Planet" };
            fleet.InOrbit = star;

            new ScrapTask().Perform(fleet, star, empire, null);

            Assert.AreEqual(10, star.ResourcesOnHand.Ironium);
        }

        /// <summary>Per stack, multiplied out before the quarter: 3 x 7 = 21 / 4 = 5 (not
        /// 3 x (7 / 4) = 3), and only the flagged stack is quartered.</summary>
        [Test]
        public void FleetCost_QuartersEachFlaggedStackSeparately()
        {
            ShipDesign flagged = ScrapFleetRecoveryTest.MakeDesign(new Resources(7, 0, 0, 10));
            flagged.FailedLegality = true;
            ShipDesign plain = ScrapFleetRecoveryTest.MakeDesign(new Resources(7, 0, 0, 10));
            Fleet fleet = new Fleet(1) { Owner = 1 };
            ShipToken a = new ShipToken(flagged, 3);
            ShipToken b = new ShipToken(plain, 3);
            fleet.Composition.Add(1, a);
            fleet.Composition.Add(2, b);

            Resources cost = ScrapTask.FleetCost(fleet);

            Assert.AreEqual(5 + 21, cost.Ironium);
            Assert.AreEqual(7 + 30, cost.Energy, "The resource cost is quartered too (30 / 4 = 7)");
        }

        /// <summary>Colonize shares the valuation: floor(3/4 x 30) = 22 for 4 x 30 flagged.</summary>
        [Test]
        public void Colonize_CreditsAFlaggedStackAtAQuarter()
        {
            ShipDesign design = ScrapFleetRecoveryTest.MakeDesign(new Resources(30, 0, 0, 0));
            design.FailedLegality = true;

            Resources salvage = ScrapTask.SalvageMinerals(FleetOf(design, 4), 3, 4);

            Assert.AreEqual(22, salvage.Ironium);
        }
    }
}
