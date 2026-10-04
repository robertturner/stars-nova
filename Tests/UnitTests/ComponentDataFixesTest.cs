namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using static Nova.Common.TechLevel;

    // Regression tests for several data-only bugs in components.xml confirmed by the
    // ship-design-and-components.md re-audit: an entire Stargate/Mass-Driver/Starbase-Chassis
    // cost category was stored at exactly half its real value (the "commonly-published halved
    // wiki figures" the spec's own §15c traces and resolves), Mass Driver 7 was misnamed
    // "Super Drvier 7" with a wrong tech level, three of the five Inner-Strength-excluded bomb
    // types had no restriction recorded at all, and all four Snooper planetary scanners had
    // PenetratingScan hard-set to half of NormalScan instead of the full value.
    [TestFixture]
    public class ComponentDataFixesTest
    {
        private static Component Fetch(string name)
        {
            Component component = new AllComponents().Fetch(name);
            Assert.NotNull(component, $"Component '{name}' should exist in components.xml.");
            return component;
        }

        [Test]
        public void MassDriver7_IsCorrectlyNamed_WithDoubledCostAndFixedTechLevel()
        {
            Assert.IsFalse(new AllComponents().Contains("Super Drvier 7"), "The old misspelled/misnamed entry should no longer exist.");

            Component massDriver7 = Fetch("Mass Driver 7");

            Assert.AreEqual(200, massDriver7.Cost.Boranium);
            Assert.AreEqual(200, massDriver7.Cost.Ironium);
            Assert.AreEqual(200, massDriver7.Cost.Germanium);
            Assert.AreEqual(1024, massDriver7.Cost.Energy);
            Assert.AreEqual(9, massDriver7.RequiredTech[ResearchField.Energy], "Mass Driver 7's Energy tech level should be 9, matching every other driver in the family and component-stats.tsv.");
        }

        [TestCase("Stargate 100/250", 40, 100, 40, 400)]
        [TestCase("Mass Driver 5", 40, 48, 40, 140)]
        [TestCase("Space Station", 160, 240, 500, 1200)]
        [TestCase("Death Star", 160, 240, 700, 1500)]
        public void StargateMassDriverAndStarbaseChassisCosts_MatchDoubledGroundTruth(string name, int boranium, int ironium, int germanium, int energy)
        {
            Component component = Fetch(name);

            Assert.AreEqual(boranium, component.Cost.Boranium, $"{name} Boranium cost");
            Assert.AreEqual(ironium, component.Cost.Ironium, $"{name} Ironium cost");
            Assert.AreEqual(germanium, component.Cost.Germanium, $"{name} Germanium cost");
            Assert.AreEqual(energy, component.Cost.Energy, $"{name} Energy cost");
        }

        [TestCase("Smart Bomb")]
        [TestCase("Neutron Bomb")]
        [TestCase("Enriched Neutron Bomb")]
        [TestCase("Peerless Bomb")]
        [TestCase("Annihilator Bomb")]
        public void InnerStrengthExcludedBombs_AreAllMarkedUnavailable(string name)
        {
            Component bomb = Fetch(name);

            Assert.AreEqual(RaceAvailability.not_available, bomb.Restrictions.Availability("IS"),
                $"{name} should be unavailable to Inner Strength races, per the spec's 5-bomb IS exclusion list.");
        }

        [TestCase("Snooper 320X", 320)]
        [TestCase("Snooper 400X", 400)]
        [TestCase("Snooper 500X", 500)]
        [TestCase("Snooper 620X", 620)]
        public void SnooperPenetratingScan_EqualsNormalScan_NotHalf(string name, int expectedRange)
        {
            Component snooper = Fetch(name);
            Scanner scanner = (Scanner)snooper.Properties["Scanner"];

            Assert.AreEqual(expectedRange, scanner.NormalScan);
            Assert.AreEqual(expectedRange, scanner.PenetratingScan,
                $"{name}'s PenetratingScan should equal its NormalScan ({expectedRange}), not half of it.");
        }

        [Test]
        public void RobberBaronScanner_PenetratingScan_IsOneTwenty()
        {
            Scanner scanner = (Scanner)Fetch("Robber Barron Scanner").Properties["Scanner"];

            Assert.AreEqual(220, scanner.NormalScan, "Robber Baron Scanner normal scan");
            Assert.AreEqual(120, scanner.PenetratingScan,
                "Robber Baron Scanner penetrating scan (fleet-movement-scanning-cargo.md section 3)");
        }

        [TestCase("Mega Poly Shell", 80, 40)]
        [TestCase("Multi Contained Munition", 150, 75)]
        [TestCase("Langston Shell", 50, 25)]
        public void NonScannerParts_ContributeTheirScanRangesToADesign(string name, int normal, int penetrating)
        {
            Component part = Fetch(name);
            Component blueprint = new Component { Name = "Test Hull", Mass = 50 };
            Hull hull = new Hull { Modules = new List<HullModule>(), ArmorStrength = 100, FuelCapacity = 100 };
            hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = 1 });
            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update();

            Assert.AreEqual(normal, design.ScanRangeNormal, $"{name} normal scan");
            Assert.AreEqual(penetrating, design.ScanRangePenetrating, $"{name} penetrating scan");
        }
    }
}
