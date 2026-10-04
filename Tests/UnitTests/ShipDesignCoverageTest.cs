namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// behavior-specs-10/ship-design-and-components.md coverage rows 19 and 20 (and race-traits.md
    /// row 32): a design's scanners combine as sqrt(sqrt(sum of range^4)) over all of them
    /// (section 15c, FUN_1038_337e), No Advanced Scanners doubles the conventional range, and the
    /// resolver's scanner gates (section 14 table: NAS blocks ship scanner subtypes 7, 8, 12 and
    /// the penetrating planetary scanners 5-8; Super Stealth alone gets subtypes 5, 6, 14).
    /// </summary>
    [TestFixture]
    public class ShipDesignCoverageTest
    {
        /// <summary>A test hull with one Scanner-type slot per entry, each holding
        /// <c>count</c> scanners of the given normal/penetrating range.</summary>
        private static ShipDesign DesignWithScanners(Race race, params (int normal, int penetrating, int count)[] slots)
        {
            Component hullBlueprint = new Component { Name = "Test Hull", Mass = 10, Cost = new Resources(1, 1, 1, 1) };
            Hull hull = new Hull { FuelCapacity = 100, Modules = new List<HullModule>() };
            int cell = 0;
            foreach ((int normal, int penetrating, int count) in slots)
            {
                Component part = new Component { Name = "Scanner " + cell, Mass = 1, Cost = new Resources(0, 0, 0, 1), Type = ItemType.Scanner };
                part.Properties.Add("Scanner", new Scanner { NormalScan = normal, PenetratingScan = penetrating });
                hull.Modules.Add(new HullModule { CellNumber = cell++, ComponentType = "Scanner", ComponentMaximum = count, ComponentCount = count, AllocatedComponent = part });
            }

            hullBlueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = hullBlueprint, Name = "Scanners" };
            design.Update(race);
            return design;
        }

        /// <summary>The spec formula, written independently: the integer part of the fourth root
        /// of the sum over every scanner of range^4. The test cases are chosen with a fractional
        /// part below one half, so truncating and rounding agree (the spec does not say which).</summary>
        private static int SpecCombined(params (int range, int count)[] scanners)
        {
            double sum = scanners.Sum(s => s.count * Math.Pow(s.range, 4));
            double root = Math.Sqrt(Math.Sqrt(sum));
            Assert.Less(root - Math.Floor(root), 0.5, "test case must not depend on the unspecified final rounding");
            return (int)Math.Floor(root);
        }

        private static Race PlainRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("SS");
            return race;
        }

        [Test]
        public void ThreeScannersCombineOnce_AsTheFourthRootOfTheSumOfFourthPowers()
        {
            // 100, 100, 150: (2 x 10^8 + 5.0625 x 10^8)^(1/4) = 163.02 -> 163. Combining pairwise
            // with truncation in between gives 118 then 162.
            ShipDesign design = DesignWithScanners(PlainRace(), (100, 0, 1), (100, 0, 1), (150, 0, 1));
            Assert.AreEqual(SpecCombined((100, 1), (100, 1), (150, 1)), design.ScanRangeNormal);
            Assert.AreEqual(163, design.ScanRangeNormal);
        }

        [TestCase(225, 225, 225, 296)]
        [TestCase(150, 150, 185, 216)]
        [TestCase(100, 185, 300, 311)]
        [TestCase(220, 300, 300, 369)]
        public void ThreeSlotsOfTsvScanners(int a, int b, int c, int expected)
        {
            ShipDesign design = DesignWithScanners(PlainRace(), (a, 0, 1), (b, 0, 1), (c, 0, 1));
            Assert.AreEqual(SpecCombined((a, 1), (b, 1), (c, 1)), design.ScanRangeNormal);
            Assert.AreEqual(expected, design.ScanRangeNormal);
        }

        [Test]
        public void PenetratingRangesCombineTheSameWay()
        {
            ShipDesign design = DesignWithScanners(PlainRace(), (220, 100, 1), (300, 100, 1), (230, 150, 1));
            Assert.AreEqual(SpecCombined((100, 1), (100, 1), (150, 1)), design.ScanRangePenetrating);
        }

        [Test]
        public void SeveralScannersInOneSlot_EachCountsInTheSum()
        {
            // A slot of 2 x 100 plus one 150: (2 x 100^4 + 150^4)^(1/4) = 163.02 -> 163.
            ShipDesign design = DesignWithScanners(PlainRace(), (100, 0, 2), (150, 0, 1));
            Assert.AreEqual(SpecCombined((100, 2), (150, 1)), design.ScanRangeNormal);
            Assert.AreEqual(163, design.ScanRangeNormal);
        }

        [Test]
        public void ASingleScannerIsItsOwnRange()
        {
            Assert.AreEqual(150, DesignWithScanners(PlainRace(), (150, 0, 1)).ScanRangeNormal);
        }

        // ---- No Advanced Scanners (ship-design row 20, race-traits row 32) ----

        [TestCase(50)]
        [TestCase(150)]
        [TestCase(335)]
        public void NoAdvancedScanners_DoublesAConventionalScannersRange(int range)
        {
            Race nas = PlainRace();
            nas.Traits.Add("NAS");

            Assert.AreEqual(range, DesignWithScanners(PlainRace(), (range, 0, 1)).ScanRangeNormal);
            Assert.AreEqual(2 * range, DesignWithScanners(nas, (range, 0, 1)).ScanRangeNormal);
        }

        private static Component Fetch(string name)
        {
            Component component = new AllComponents().Fetch(name);
            Assert.NotNull(component, name);
            return component;
        }

        /// <summary>Section 14 table, row 0x0002: bit 10 (NAS) blocks ship scanner subtypes 7, 8
        /// and 12 (Ferret, Dolphin, Elephant) and no others; row 0x8000: it blocks the four
        /// penetrating planetary scanners (subtypes 5-8, the Snoopers) and no other scanner.</summary>
        [TestCase("Bat Scanner", false)]
        [TestCase("Rhino Scanner", false)]
        [TestCase("Mole Scanner", false)]
        [TestCase("DNA Scanner", false)]
        [TestCase("Possum Scanner", false)]
        [TestCase("Pick Pocket Scanner", false)]
        [TestCase("Chameleon Scanner", false)]
        [TestCase("Ferret Scanner", true)]
        [TestCase("Dolphin Scanner", true)]
        [TestCase("Gazelle Scanner", false)]
        [TestCase("RNA Scanner", false)]
        [TestCase("Cheetah Scanner", false)]
        [TestCase("Elephant Scanner", true)]
        [TestCase("Eagle Eye Scanner", false)]
        [TestCase("Robber Barron Scanner", false)]
        [TestCase("Peerless Scanner", false)]
        [TestCase("Viewer 50", false)]
        [TestCase("Viewer 90", false)]
        [TestCase("Scoper 150", false)]
        [TestCase("Scoper 220", false)]
        [TestCase("Scoper 280", false)]
        [TestCase("Snooper 320X", true)]
        [TestCase("Snooper 400X", true)]
        [TestCase("Snooper 500X", true)]
        [TestCase("Snooper 620X", true)]
        public void NoAdvancedScanners_BlocksExactlyTheListedScanners(string name, bool blocked)
        {
            Assert.AreEqual(blocked, Fetch(name).Restrictions.Availability("NAS") == RaceAvailability.not_available);
        }

        /// <summary>Section 14 table, row 0x0002: PRT 1 (Super Stealth) for subtypes 5, 6 and 14.</summary>
        [TestCase("Pick Pocket Scanner")]
        [TestCase("Chameleon Scanner")]
        [TestCase("Robber Barron Scanner")]
        public void SuperStealthAloneGetsTheThreeStealthScanners(string name)
        {
            Assert.AreEqual(RaceAvailability.required, Fetch(name).Restrictions.Availability("SS"));
        }

        /// <summary>Section 14 table, row 0x8000: War Monger is blocked from subtypes 11-13
        /// (Laser Battery, Planetary Shield, Neutron Shield) - race-traits.md: "restricted to
        /// SDI/Missile-Battery planetary defenses only".</summary>
        [TestCase("SDI", false)]
        [TestCase("Missile Battery", false)]
        [TestCase("Laser Battery", true)]
        [TestCase("Planetary Shield", true)]
        [TestCase("Neutron Shield", true)]
        public void WarMonger_IsBlockedFromTheThreeBestDefences(string name, bool blocked)
        {
            Assert.AreEqual(blocked, Fetch(name).Restrictions.Availability("WM") == RaceAvailability.not_available);
        }
    }
}
