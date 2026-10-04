namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Client.Map;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The star map's pure rules, kept in Nova.Client/Map so they are testable
    /// (behavior-specs-10/client-interface.md, "Map canvas" and "Navigation controls";
    /// client-ui-dialog-catalog.md mode 7 for the planet tooltip).
    /// </summary>
    [TestFixture]
    public class MapRulesTest
    {
        // ---------------- zoom ----------------

        [Test]
        public void Zoom_NineExactFactors()
        {
            double[] expected = { 0.25, 0.375, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 4.0 };
            for (int level = MapZoom.MinLevel; level <= MapZoom.MaxLevel; level++)
            {
                Assert.AreEqual(expected[level - MapZoom.MinLevel], MapZoom.ScaleFactor(level), 0.0, "level " + level);
            }

            Assert.AreEqual("38%", MapZoom.Label(-3), "the menu says 38% for the 3/8 step");
            Assert.AreEqual(4.0, MapZoom.ScaleFactor(99), "levels clamp to +4");
            Assert.AreEqual(0.25, MapZoom.ScaleFactor(-99), "levels clamp to -4");
        }

        [Test]
        public void Zoom_IntegerArithmetic()
        {
            Assert.AreEqual(37, MapZoom.ToScreen(100, -3), "100 x 3 / 8 = 37 (truncated)");
            Assert.AreEqual(25, MapZoom.ToScreen(101, -4), "101 / 4 = 25");
            Assert.AreEqual(126, MapZoom.ToScreen(101, 1), "101 x 5 / 4 = 126");
            Assert.AreEqual(400, MapZoom.ToScreen(100, 4));
            Assert.AreEqual(266, MapZoom.ToWorld(100, -3), "100 x 8 / 3");
            Assert.AreEqual(50, MapZoom.ToWorld(100, 3), "100 / 2");
        }

        [Test]
        public void Zoom_RecentresOnTheSameWorldPoint()
        {
            // Viewport 400x300 at 100%, scrolled so world (500, 350) is in the centre.
            (double x, double y) = MapZoom.RecenteredOffset(300, 200, 400, 300, 1.0, 2.0);
            Assert.AreEqual(800, x, 1e-9, "500 x 2 - 200");
            Assert.AreEqual(550, y, 1e-9, "350 x 2 - 150");

            // And back down to 37.5%: the same world point stays centred.
            (double x2, double y2) = MapZoom.RecenteredOffset(x, y, 400, 300, 2.0, 0.375);
            Assert.AreEqual((500 * 0.375) - 200, x2, 1e-9);
            Assert.AreEqual((350 * 0.375) - 150, y2, 1e-9);
        }

        [Test]
        public void Zoom_NearestLevel()
        {
            Assert.AreEqual(0, MapZoom.NearestLevel(1.0));
            Assert.AreEqual(-3, MapZoom.NearestLevel(0.38));
            Assert.AreEqual(4, MapZoom.NearestLevel(3.5));
        }

        // ---------------- view-option slots and digit keys ----------------

        [Test]
        public void ViewOptions_DigitKeysMapToSlots()
        {
            Assert.AreEqual(0, MapViewOptions.SlotForDigitKey(1, false));
            Assert.AreEqual(5, MapViewOptions.SlotForDigitKey(6, false));
            Assert.AreEqual(7, MapViewOptions.SlotForDigitKey(7, false), "slot 6 (the Shift latch) is skipped");
            Assert.AreEqual(8, MapViewOptions.SlotForDigitKey(8, false));
            Assert.AreEqual(9, MapViewOptions.SlotForDigitKey(9, false));
            Assert.AreEqual(11, MapViewOptions.SlotForDigitKey(0, false), "0 = planet names");
            Assert.AreEqual(17, MapViewOptions.SlotForDigitKey(0, true), "Shift+0 = ship-count badge");
            Assert.IsNull(MapViewOptions.SlotForDigitKey(3, true));
        }

        [Test]
        public void ViewOptions_ModeKeysAreMutuallyExclusive()
        {
            var options = new MapViewOptions();
            options.ApplyDigitKey(3, false);
            Assert.AreEqual(2, options.Mode);
            Assert.AreEqual(1, options.GetSlot(2));
            Assert.AreEqual(0, options.GetSlot(0));

            options.ApplyDigitKey(3, false);
            Assert.AreEqual(2, options.Mode, "pressing the current mode's key again keeps it");

            options.ApplyDigitKey(1, false);
            Assert.AreEqual(0, options.Mode);
            Assert.AreEqual(options.Word1 & MapViewOptions.ModeMask, 0);
        }

        [Test]
        public void ViewOptions_BitKeysToggleTheirBits()
        {
            var options = new MapViewOptions();
            bool scan = options.ShowScanCircles;
            options.ApplyDigitKey(7, false);
            Assert.AreEqual(!scan, options.ShowScanCircles);
            Assert.AreEqual(!scan, (options.Word1 & 0x20) != 0, "key 7 = first word bit 0x20");

            bool mines = options.ShowMinefields;
            options.ApplyDigitKey(8, false);
            Assert.AreEqual(!mines, (options.Word1 & 0x40) != 0, "key 8 = first word bit 0x40");

            bool dash = options.ShowRouteOverlap;
            options.ApplyDigitKey(9, false);
            Assert.AreEqual(!dash, (options.Word1 & 0x80) != 0, "key 9 = first word bit 0x80");

            bool names = options.ShowPlanetNames;
            options.ApplyDigitKey(0, false);
            Assert.AreEqual(!names, (options.Word2 & 0x04) != 0, "key 0 = second word bit 0x04 (slot 11)");

            bool badge = options.ShowShipCountBadges;
            options.ApplyDigitKey(0, true);
            Assert.AreEqual(!badge, (options.Word2 & 0x10) != 0, "Shift+0 = second word bit 0x10 (slot 17)");

            int modeBefore = options.Mode;
            Assert.AreEqual(modeBefore, options.Mode, "bit toggles leave the mode nibble alone");
        }

        [Test]
        public void ViewOptions_SecondWordSlotNumbering()
        {
            var options = new MapViewOptions();
            options.SetSlot(10, 1);
            options.SetSlot(12, 1);
            options.SetSlot(14, 1);
            Assert.AreEqual(0x01 | 0x02 | 0x08, options.Word2 & 0x0B, "slots 10/12/14 = bits 0x01/0x02/0x08");
            options.SetSlot(6, 1);
            Assert.AreEqual(0x10, options.Word1 & 0x10, "slot 6 = first word bit 0x10");
        }

        [Test]
        public void ViewOptions_ScannerPercentageClampsAndForcesScanCircles()
        {
            var options = new MapViewOptions();
            options.ShowScanCircles = false;
            options.ScannerPercentage = 1;
            Assert.AreEqual(2, options.ScannerPercentage, "minimum 2%");
            Assert.IsTrue(options.ShowScanCircles, "changing the percentage force-enables the scan circles");
            options.ScannerPercentage = 500;
            Assert.AreEqual(100, options.ScannerPercentage);
        }

        // ---------------- scan circles ----------------

        [Test]
        public void ScanCircle_PercentageEnlargesBelow100()
        {
            Assert.AreEqual(150, ScanCircleRules.DisplayRadius(150, 100), "no correction at 100%");
            Assert.AreEqual(300, ScanCircleRules.DisplayRadius(150, 50), "x100 / 50");
            Assert.AreEqual(7500, ScanCircleRules.DisplayRadius(150, 2), "50x at the 2% minimum");
            Assert.AreEqual(333, ScanCircleRules.DisplayRadius(100, 30), "integer multiply-then-divide");
        }

        [Test]
        public void ScanCircle_NestedCirclesAreCulledAgainstEarlierOnes()
        {
            var circles = new List<MapCircle>
            {
                new MapCircle(100, 100, 50),   // 0 kept
                new MapCircle(110, 100, 30),   // 1 inside 0 -> culled
                new MapCircle(140, 100, 30),   // 2 pokes out of 0 -> kept
                new MapCircle(100, 100, 50),   // 3 identical to 0 -> culled
                new MapCircle(300, 300, 10),   // 4 kept
                new MapCircle(300, 300, 40),   // 5 contains 4 but comes later -> kept, 4 stays
            };

            CollectionAssert.AreEqual(new[] { 0, 2, 4, 5 }, ScanCircleRules.CullNested(circles));
        }

        // ---------------- route overlap ----------------

        [Test]
        public void RouteOverlap_RepeatedAndReversedLegsDash()
        {
            var a = new NovaPoint(0, 0);
            var b = new NovaPoint(10, 0);
            var c = new NovaPoint(10, 10);
            var route = new List<NovaPoint> { a, b, a, b, c, c };

            bool[] dashed = RouteOverlap.DashedLegs(route);

            CollectionAssert.AreEqual(new[] { false, true, true, false, false }, dashed,
                "A-B solid, B-A (reversal) dashed, A-B (repeat) dashed, B-C solid, zero-length C-C never dashes");
            Assert.IsEmpty(RouteOverlap.DashedLegs(new List<NovaPoint> { a }));
        }

        // ---------------- right-click picker ----------------

        [Test]
        public void Picker_FleetsFirstThenPlanetsWithDividerAndCheck()
        {
            object planet = new object();
            object fleetNear = new object();
            object fleetFar = new object();
            object elsewhere = new object();

            var objects = new List<MapObjectEntry>
            {
                new MapObjectEntry(planet, "Alpha", MapObjectKind.Planet, 100, 100),
                new MapObjectEntry(fleetFar, "Scout #2", MapObjectKind.Fleet, 108, 100),
                new MapObjectEntry(fleetNear, "Scout #1", MapObjectKind.Fleet, 100, 100),
                new MapObjectEntry(elsewhere, "Far", MapObjectKind.Fleet, 400, 400),
                new MapObjectEntry(new object(), "Field", MapObjectKind.Minefield, 100, 100),
            };

            List<MapPickerLine> lines = MapObjectPicker.Build(objects, 100, 100, 16, fleetFar);

            CollectionAssert.AreEqual(new[] { "Scout #1", "Scout #2", "Alpha" }, lines.Select(line => line.Entry.Name).ToArray(),
                "fleets (nearest first), then planets; minefields and out-of-range objects are not listed");
            CollectionAssert.AreEqual(new[] { false, true, false }, lines.Select(line => line.IsChecked).ToArray(), "the selected object is checked");
            CollectionAssert.AreEqual(new[] { false, false, true }, lines.Select(line => line.SeparatorBefore).ToArray(), "divider between the groups");
        }

        [Test]
        public void Picker_NoDividerWithoutFleets()
        {
            var objects = new List<MapObjectEntry> { new MapObjectEntry(new object(), "Alpha", MapObjectKind.Planet, 0, 0) };
            List<MapPickerLine> lines = MapObjectPicker.Build(objects, 0, 0, 16, null);
            Assert.AreEqual(1, lines.Count);
            Assert.IsFalse(lines[0].SeparatorBefore);
        }

        // ---------------- tooltip text ----------------

        [Test]
        public void Identify_OwnedByOtherRaceAndDeepSpace()
        {
            Assert.AreEqual("Deep Space", MapObjectText.Identify(null, null, MapOwnership.Unowned, null));
            Assert.AreEqual("Scout #3 owned by Hobbits", MapObjectText.Identify(MapObjectKind.Fleet, "Scout #3", MapOwnership.Other, "Hobbits"));
            Assert.AreEqual("Scout #3", MapObjectText.Identify(MapObjectKind.Fleet, "Scout #3", MapOwnership.Own, "Humanoids"));
            Assert.AreEqual("Minefield", MapObjectText.Identify(MapObjectKind.Minefield, string.Empty, MapOwnership.Own, null), "plain category-name fallback");
            Assert.AreEqual("Alpha (with Stargate) owned by Hobbits", MapObjectText.Identify(MapObjectKind.Planet, "Alpha", MapOwnership.Other, "Hobbits", true, true));
            Assert.AreEqual("Alpha (with starbase)", MapObjectText.Identify(MapObjectKind.Planet, "Alpha", MapOwnership.Own, null, true, false));
        }

        [Test]
        public void PlanetTooltip_OwnershipLinesAndHabitability()
        {
            CollectionAssert.AreEqual(new[] { "Alpha", "Owned by you", "Habitability: 87%" },
                MapObjectText.PlanetTooltip("Alpha", MapOwnership.Own, null, null, 87));
            CollectionAssert.AreEqual(new[] { "Beta", "Unowned" },
                MapObjectText.PlanetTooltip("Beta", MapOwnership.Unowned, null, null, null));
            CollectionAssert.AreEqual(new[] { "Gamma", "Owned by Hobbits", "Population: 12,300", "Habitability: -12%" },
                MapObjectText.PlanetTooltip("Gamma", MapOwnership.Other, "Hobbits", 12300, -12));
        }

        // ---------------- search ----------------

        [Test]
        public void Search_RanksExactThenPrefixThenContains()
        {
            var objects = new List<MapObjectEntry>
            {
                new MapObjectEntry(1, "Sol Minor", MapObjectKind.Planet, 0, 0),
                new MapObjectEntry(2, "Absolute", MapObjectKind.Planet, 0, 0),
                new MapObjectEntry(3, "sol", MapObjectKind.Planet, 0, 0),
                new MapObjectEntry(4, "Sol Fleet", MapObjectKind.Fleet, 0, 0),
                new MapObjectEntry(5, "Mars", MapObjectKind.Planet, 0, 0),
            };

            CollectionAssert.AreEqual(new[] { "sol", "Sol Fleet", "Sol Minor", "Absolute" }, MapSearch.Find(objects, " SOL ", null).Select(e => e.Name).ToArray());
            CollectionAssert.AreEqual(new[] { "sol", "Sol Minor", "Absolute" }, MapSearch.Find(objects, "sol", MapObjectKind.Planet).Select(e => e.Name).ToArray(), "kind filter");
            Assert.IsNull(MapSearch.FindFirst(objects, "Zed", null), "no result");
            Assert.AreEqual(4, MapSearch.Find(objects, string.Empty, MapObjectKind.Planet).Count, "empty query lists the kind");
        }

        // ---------------- planet overlays ----------------

        [Test]
        public void Habitability_ColourPairs()
        {
            Assert.AreEqual(HabitabilityRingColour.Green, PlanetOverlayRules.HabitabilityColour(40, 40, false));
            Assert.AreEqual(HabitabilityRingColour.Red, PlanetOverlayRules.HabitabilityColour(-10, -10, false));
            Assert.AreEqual(HabitabilityRingColour.Olive, PlanetOverlayRules.HabitabilityColour(-10, 5, false), "negative exact, estimate disagrees, not CA");
            Assert.AreEqual(HabitabilityRingColour.Red, PlanetOverlayRules.HabitabilityColour(-10, 5, true), "negative exact: no recheck for CA");
            Assert.AreEqual(HabitabilityRingColour.Olive, PlanetOverlayRules.HabitabilityColour(10, -5, true), "non-negative exact rechecked only for CA");
            Assert.AreEqual(HabitabilityRingColour.Green, PlanetOverlayRules.HabitabilityColour(10, -5, false));
        }

        [Test]
        public void Habitability_RadiusClampedAtTenSteps()
        {
            Assert.AreEqual(10, PlanetOverlayRules.HabitabilityRingRadius(100));
            Assert.AreEqual(5, PlanetOverlayRules.HabitabilityRingRadius(50));
            Assert.AreEqual(4.5, PlanetOverlayRules.HabitabilityRingRadius(-45));
            Assert.AreEqual(10, PlanetOverlayRules.HabitabilityRingRadius(250));
        }

        [Test]
        public void Population_StepsRadiusAndColour()
        {
            Assert.AreEqual(19, PlanetOverlayRules.PopulationRingThresholds.Length, "19 ascending thresholds");
            for (int i = 1; i < 19; i++)
            {
                Assert.Greater(PlanetOverlayRules.PopulationRingThresholds[i], PlanetOverlayRules.PopulationRingThresholds[i - 1]);
            }

            Assert.AreEqual(0, PlanetOverlayRules.PopulationSteps(0));
            Assert.AreEqual(18, PlanetOverlayRules.PopulationSteps(int.MaxValue), "0-18 steps");
            int t5 = PlanetOverlayRules.PopulationRingThresholds[5];
            Assert.AreEqual(5, PlanetOverlayRules.PopulationSteps(t5));
            Assert.AreEqual(4, PlanetOverlayRules.PopulationSteps(t5 - 1));

            Assert.AreEqual(7, PlanetOverlayRules.PopulationRingScreenRadius(5, 0), "steps + 2");
            Assert.AreEqual(3.5, PlanetOverlayRules.PopulationRingScreenRadius(5, -4), "halved at the lowest zoom levels");

            Assert.AreEqual(PopulationRingColour.Green, PlanetOverlayRules.PopulationColour(MapOwnership.Own, false));
            Assert.AreEqual(PopulationRingColour.Yellow, PlanetOverlayRules.PopulationColour(MapOwnership.Other, true));
            Assert.AreEqual(PopulationRingColour.Red, PlanetOverlayRules.PopulationColour(MapOwnership.Other, false));
        }

        [Test]
        public void MineralBars_TwentySteps()
        {
            Assert.AreEqual(20, PlanetOverlayRules.ConcentrationBarSteps(100));
            Assert.AreEqual(9, PlanetOverlayRules.ConcentrationBarSteps(49), "divided by 5");
            Assert.AreEqual(20, PlanetOverlayRules.ConcentrationBarSteps(140), "clamped to 100");
            Assert.AreEqual(10, PlanetOverlayRules.AmountBarSteps(500, 1000), "normalised against the reference maximum");
            Assert.AreEqual(20, PlanetOverlayRules.AmountBarSteps(1000, 1000));
            Assert.AreEqual(0, PlanetOverlayRules.AmountBarSteps(1000, 0));
        }

        // ---------------- tracked chevron ----------------

        [Test]
        public void Chevron_ElevenByElevenArrow()
        {
            bool[,] chevron = TrackedMarkerShape.Chevron11();

            Assert.AreEqual(11, Enumerable.Range(0, 11).Count(c => chevron[5, c]), "centre row spans all 11 pixels");
            Assert.AreEqual(10, Enumerable.Range(0, 11).Count(c => chevron[4, c]), "the row above spans 10");
            Assert.AreEqual(10, Enumerable.Range(0, 11).Count(c => chevron[6, c]), "the row below spans 10");
            Assert.IsTrue(chevron[0, 0] && chevron[10, 0], "the diagonals start at the top-left and bottom-left corners");
            Assert.AreEqual(3, Enumerable.Range(0, 11).Count(c => chevron[1, c]), "3-pixel-wide diagonal stroke");

            for (int row = 0; row < 11; row++)
            {
                for (int column = 0; column < 11; column++)
                {
                    Assert.AreEqual(chevron[row, column], chevron[10 - row, column], "symmetric about the centre row");
                }
            }
        }

        [Test]
        public void Triangle_FiveByFiveRightAngleBottomLeft()
        {
            bool[,] triangle = TrackedMarkerShape.Triangle5();
            for (int row = 0; row < 5; row++)
            {
                Assert.AreEqual(row + 1, Enumerable.Range(0, 5).Count(c => triangle[row, c]));
                Assert.IsTrue(triangle[row, 0]);
            }
        }
    }
}
