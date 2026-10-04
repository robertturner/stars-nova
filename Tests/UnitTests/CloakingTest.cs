#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Regression tests for behavior-specs-7/combat-resolution.md §11's cloaking mechanism: raw
    /// cloak-rating units (summed arithmetically per design, plus race-trait baselines) run
    /// through CloakCalculator's single piecewise curve, combined across a fleet as a
    /// mass-weighted average, and countered by the observer's installed Tachyon Detectors.
    /// </summary>
    [TestFixture]
    public class CloakCalculatorTest
    {
        [TestCase(0, 0)]
        [TestCase(98, 49)]
        [TestCase(99, 49)]
        [TestCase(100, 50)]
        [TestCase(299, 74)]
        [TestCase(300, 75)]
        [TestCase(611, 87)]
        [TestCase(612, 88)]
        [TestCase(1124, 96)]
        [TestCase(1125, 96)]
        [TestCase(1379, 96)]
        [TestCase(1380, 97)]
        [TestCase(1611, 97)]
        [TestCase(1612, 98)]
        [TestCase(5000, 98)]
        [TestCase(25000, 98)]
        // behavior-specs-8 addendum: a zero, negative or over-25,000 raw total counts as NO cloak.
        [TestCase(25001, 0)]
        [TestCase(1000000, 0)]
        [TestCase(-50, 0)]
        public void PercentFromRawUnits_MatchesThePiecewiseCurveAtEachBreakpoint(double rawUnits, int expectedPercent)
        {
            Assert.AreEqual(expectedPercent, CloakCalculator.PercentFromRawUnits(rawUnits));
        }

        [TestCase(0, 100)]
        [TestCase(1, 95)]
        [TestCase(9, 86)]
        [TestCase(17, 81)]
        [TestCase(18, 81)]
        [TestCase(100, 81)]
        public void ApplyTachyonDetectors_UsesTheCounterCloakTable_ClampingBeyondTheLastEntry(int detectorCount, int expectedMultiplierPercent)
        {
            // A target cloak of exactly 100 makes the result read directly as the multiplier.
            double result = CloakCalculator.ApplyTachyonDetectors(100, detectorCount);

            Assert.AreEqual(expectedMultiplierPercent, result);
        }
    }

    [TestFixture]
    public class ShipDesignCloakAggregationTest
    {
        private static ShipDesign BuildDesignWithCloakComponents(params double[] rawUnitsPerComponent)
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };

            foreach (double rawUnits in rawUnitsPerComponent)
            {
                Component cloakComponent = new Component();
                cloakComponent.Properties.Add("Cloak", new ProbabilityProperty(rawUnits));
                hull.Modules.Add(new HullModule { AllocatedComponent = cloakComponent, ComponentCount = 1 });
            }

            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update();
            return design;
        }

        [Test]
        public void SumProperty_MultipleCloakComponents_SumRawUnitsArithmetically_NotAsIndependentProbabilities()
        {
            // Stealth Cloak (70) + Multi Function Pod (60). Independent-probability combination
            // (ProbabilityProperty's own operator+, correct for Jammer/Beam Deflector but wrong
            // here) would give 100 - (100-70)*(100-60)/100 = 88 - a different, wrong number from
            // the correct arithmetic sum of 130.
            ShipDesign design = BuildDesignWithCloakComponents(70, 60);

            ProbabilityProperty cloak = (ProbabilityProperty)design.Summary.Properties["Cloak"];

            Assert.AreEqual(130, cloak.Value);
        }

        [Test]
        public void Update_SuperStealthTrait_AddsA300RawUnitBaseline_YieldingExactly75PercentAlone()
        {
            ShipDesign design = BuildDesignWithCloakComponents(); // no cloak components at all

            Race ssRace = new Race();
            ssRace.Traits.SetPrimary("SS");
            design.Update(ssRace);

            ProbabilityProperty cloak = (ProbabilityProperty)design.Summary.Properties["Cloak"];

            Assert.AreEqual(300, cloak.Value);
            Assert.AreEqual(75, CloakCalculator.PercentFromRawUnits(cloak.Value),
                "300 raw units must land exactly on the curve's 75% breakpoint, matching the community-known inherent SS cloak");
        }

        [Test]
        public void Update_ImprovedStarbasesTrait_AddsA40RawUnitBaseline_OnAStarbaseHull_YieldingExactly20PercentAlone()
        {
            Component blueprint = new Component { Mass = 100 };
            Hull starbaseHull = new Hull { Modules = new List<HullModule>() }; // FuelCapacity 0 -> IsStarbase
            blueprint.Properties.Add("Hull", starbaseHull);

            Race isbRace = new Race();
            isbRace.Traits.Add("ISB");

            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update(isbRace);

            ProbabilityProperty cloak = (ProbabilityProperty)design.Summary.Properties["Cloak"];

            Assert.AreEqual(40, cloak.Value);
            Assert.AreEqual(20, CloakCalculator.PercentFromRawUnits(cloak.Value));
        }

        [Test]
        public void Update_ImprovedStarbasesTrait_AddsNoBaseline_OnANonStarbaseHull()
        {
            ShipDesign design = BuildDesignWithCloakComponents(); // ordinary (non-starbase) hull

            Race isbRace = new Race();
            isbRace.Traits.Add("ISB");
            design.Update(isbRace);

            Assert.IsFalse(design.Summary.Properties.ContainsKey("Cloak"),
                "Improved Starbases' baseline is starbase-hull-specific and must not leak onto ordinary ship designs");
        }
    }

    [TestFixture]
    public class FleetRecalculateCloakTest
    {
        private static ShipDesign BuildDesign(long key, int mass, double rawCloakUnits)
        {
            Component blueprint = new Component { Mass = mass };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };

            if (rawCloakUnits > 0)
            {
                Component cloakComponent = new Component();
                cloakComponent.Properties.Add("Cloak", new ProbabilityProperty(rawCloakUnits));
                hull.Modules.Add(new HullModule { AllocatedComponent = cloakComponent, ComponentCount = 1 });
            }

            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint };
            design.Update();
            return design;
        }

        [Test]
        public void RecalculateCloak_WeightsByDesignMass_NotASimpleSumOrBestDesign()
        {
            // Design A: mass 100, raw 100 (curve alone: 50%). Design B: mass 300, raw 500.
            // Mass-weighted average raw = (100*100 + 500*300) / (100+300) = 400, which the curve
            // maps to 79% - distinct from both a plain sum (600 -> 87%) and either single design's
            // own percentage, so the assertion actually exercises the weighting, not just a sum.
            ShipDesign lightDesign = BuildDesign(1, mass: 100, rawCloakUnits: 100);
            ShipDesign heavyDesign = BuildDesign(2, mass: 300, rawCloakUnits: 500);

            Fleet fleet = new Fleet(1) { Owner = 1 };
            fleet.Composition.Add(new ShipToken(lightDesign, 1).Key, new ShipToken(lightDesign, 1));
            fleet.Composition.Add(new ShipToken(heavyDesign, 1).Key, new ShipToken(heavyDesign, 1));

            fleet.RecalculateCloak(new Race());

            Assert.AreEqual(79, fleet.Cloaked);
        }

        [Test]
        public void RecalculateCloak_WeightsByShipQuantity_NotJustPerDesignMass()
        {
            // Equal mass, but three uncloaked ships against one 400-raw-unit ship: weighted
            // average = (0*100*3 + 400*100*1) / (100*3 + 100*1) = 100, curve -> 50%. A weighting
            // that ignored Quantity (e.g. counted each design once) would instead average the two
            // designs' raw units directly ((0+400)/2=200 -> 62%), a different, wrong number.
            ShipDesign uncloakedDesign = BuildDesign(1, mass: 100, rawCloakUnits: 0);
            ShipDesign cloakedDesign = BuildDesign(2, mass: 100, rawCloakUnits: 400);

            Fleet fleet = new Fleet(1) { Owner = 1 };
            fleet.Composition.Add(new ShipToken(uncloakedDesign, 3).Key, new ShipToken(uncloakedDesign, 3));
            fleet.Composition.Add(new ShipToken(cloakedDesign, 1).Key, new ShipToken(cloakedDesign, 1));

            fleet.RecalculateCloak(new Race());

            Assert.AreEqual(50, fleet.Cloaked);
        }
    }

    [TestFixture]
    public class ScanStepTachyonDetectorTest
    {
        private static ShipDesign BuildScannerDesign(long key, int normalScanRange, int tachyonDetectorCount)
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };

            Component scannerComponent = new Component();
            scannerComponent.Properties.Add("Scanner", new Scanner { NormalScan = normalScanRange });
            hull.Modules.Add(new HullModule { AllocatedComponent = scannerComponent, ComponentCount = 1 });

            if (tachyonDetectorCount > 0)
            {
                Component detectorComponent = new Component();
                detectorComponent.Properties.Add("Tachyon Detector", new ProbabilityProperty(1));
                hull.Modules.Add(new HullModule { AllocatedComponent = detectorComponent, ComponentCount = tachyonDetectorCount });
            }

            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint };
            design.Update();
            return design;
        }

        private static ShipDesign BuildCloakedTargetDesign(long key, double rawCloakUnits)
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };

            Component cloakComponent = new Component();
            cloakComponent.Properties.Add("Cloak", new ProbabilityProperty(rawCloakUnits));
            hull.Modules.Add(new HullModule { AllocatedComponent = cloakComponent, ComponentCount = 1 });

            blueprint.Properties.Add("Hull", hull);

            // A detected target gets copied into the observer's EmpireReports (ShipDesign's own
            // copy constructor), which unconditionally clones Icon - a real design (built via its
            // normal XML-loading path) always has one, in the "<hull><NNNN>.png" source format
            // ShipIcon's constructor parses, so this is just giving the test design that same
            // baseline rather than working around a gap this fix doesn't touch.
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Icon = new ShipIcon("hull0000.png", null) };
            design.Update();
            return design;
        }

        // scanRange 100, target raw cloak 100 (-> 50%). Without a detector the effective scan
        // range is exactly 50, so a target at range 51 goes undetected; the observer's own
        // Tachyon Detector reduces the target's effective cloak (50 * 95% = 47.5), extending the
        // effective range to 52.5 and bringing that same target within detection.
        [TestCase(0, false)]
        [TestCase(1, true)]
        public void TachyonDetector_ExtendsEffectiveDetectionRange_AgainstACloakedTarget(int observerDetectorCount, bool expectDetected)
        {
            ServerData serverState = new ServerData();

            EmpireData observerEmpire = new EmpireData { Id = 1 };
            EmpireData targetEmpire = new EmpireData { Id = 2 };
            serverState.AllEmpires.Add(observerEmpire.Id, observerEmpire);
            serverState.AllEmpires.Add(targetEmpire.Id, targetEmpire);
            observerEmpire.EmpireReports.Add(targetEmpire.Id, new EmpireIntel(targetEmpire));
            targetEmpire.EmpireReports.Add(observerEmpire.Id, new EmpireIntel(observerEmpire));

            ShipDesign observerDesign = BuildScannerDesign(1, normalScanRange: 100, tachyonDetectorCount: observerDetectorCount);
            Fleet observerFleet = new Fleet(10) { Owner = observerEmpire.Id, Position = new NovaPoint(0, 0) };
            observerFleet.Composition.Add(new ShipToken(observerDesign, 1).Key, new ShipToken(observerDesign, 1));
            observerFleet.Waypoints.Add(new Waypoint { Position = observerFleet.Position });
            observerEmpire.OwnedFleets.Add(observerFleet);

            ShipDesign targetDesign = BuildCloakedTargetDesign(2, rawCloakUnits: 100);
            Fleet targetFleet = new Fleet(20) { Owner = targetEmpire.Id, Position = new NovaPoint(51, 0) };
            targetFleet.Composition.Add(new ShipToken(targetDesign, 1).Key, new ShipToken(targetDesign, 1));
            targetFleet.Waypoints.Add(new Waypoint { Position = targetFleet.Position });
            targetEmpire.OwnedFleets.Add(targetFleet);

            new ScanStep().Process(serverState);

            Assert.AreEqual(expectDetected, observerEmpire.FleetReports.ContainsKey(targetFleet.Key));
        }
    }
}
