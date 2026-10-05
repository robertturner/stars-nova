namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Client.Map;
    using Nova.Common;
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

        [Test]
        public void ViewOptions_StartUpWordIsE0AndStoredWordsAreSanitized()
        {
            var options = new MapViewOptions();
            Assert.AreEqual(0, options.Mode, "normal planet view by default");
            Assert.AreEqual(PlanetOverlayKind.None, options.Overlay);
            Assert.AreEqual(0xE0, options.Word1, "start-up word 0x00E0: normal view, scanner, minefields, fleet paths");
            Assert.IsTrue(options.ShowScanCircles);
            Assert.IsTrue(options.ShowMinefields);
            Assert.IsTrue(options.ShowRouteOverlap);

            Assert.AreEqual((0, 0), MapViewOptions.SanitizeStoredWords(6, 0), "a mode nibble above 5 resets the word");
            Assert.AreEqual((0, 0), MapViewOptions.SanitizeStoredWords(0, 0x40), "bit 0x4000 (second word 0x40) resets the word");
            Assert.AreEqual((0, 0), MapViewOptions.SanitizeStoredWords(0, 0x80), "bit 0x8000 (second word 0x80) resets the word");
            Assert.AreEqual((0xE0, 0x14), MapViewOptions.SanitizeStoredWords(0xE0, 0x14), "a valid word passes through");
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
        public void ScanCircle_SecondaryIsHalfThePrimary()
        {
            // behavior-specs-11/client-interface.md: the penetrating-scan circle is drawn at
            // exactly half the primary radius, not from the real penetration range.
            Assert.AreEqual(75, ScanCircleRules.SecondaryRadius(150));
            Assert.AreEqual(3, ScanCircleRules.SecondaryRadius(7), "integer division");
            Assert.AreEqual(0, ScanCircleRules.SecondaryRadius(1));
        }

        [Test]
        public void MassDriverRange_IsTheDriverWarpSquared()
        {
            // behavior-specs-11 SPEC GAP: the spec gives no formula; the port's stand-in is the
            // driver's rated warp squared (see MassDriverRangeRules).
            Assert.AreEqual(25, MassDriverRangeRules.RangeCircleRadius(5), "Mass Driver 5");
            Assert.AreEqual(169, MassDriverRangeRules.RangeCircleRadius(13), "Ultra Driver 13");
            Assert.AreEqual(0, MassDriverRangeRules.RangeCircleRadius(0), "no driver draws nothing");
        }

        [Test]
        public void ScanCircle_NestedCirclesAreCulledAgainstEarlierOnes()
        {            var circles = new List<MapCircle>
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

        // ---------------- minefield overlay ----------------

        [Test]
        public void MinefieldOverlay_ThreePatternsAndTheFourBitMask()
        {
            // behavior-specs-11/client-interface.md: one fill pattern per minefield type.
            Assert.AreEqual(MinefieldPattern.Standard, MinefieldOverlay.PatternOf(MinefieldType.Standard));
            Assert.AreEqual(MinefieldPattern.Heavy, MinefieldOverlay.PatternOf(MinefieldType.Heavy));
            Assert.AreEqual(MinefieldPattern.SpeedBump, MinefieldOverlay.PatternOf(MinefieldType.SpeedBump));

            Assert.AreEqual(MinefieldVisibility.Own, MinefieldOverlay.CategoryOf(true, false, false));
            Assert.AreEqual(MinefieldVisibility.Others, MinefieldOverlay.CategoryOf(false, false, true));
            Assert.AreEqual(MinefieldVisibility.DetectedEnemy, MinefieldOverlay.CategoryOf(false, true, true));
            Assert.AreEqual(MinefieldVisibility.UndetectedEnemy, MinefieldOverlay.CategoryOf(false, true, false));

            Assert.AreEqual(0x0F, (int)MinefieldVisibility.All, "four independent bits");
            Assert.AreEqual(0, (int)MinefieldVisibility.None);
            Assert.IsTrue(MinefieldOverlay.IsVisible(MinefieldVisibility.All, false, true, false));
            Assert.IsFalse(MinefieldOverlay.IsVisible(MinefieldVisibility.None, true, false, true));
            Assert.IsTrue(MinefieldOverlay.IsVisible(MinefieldVisibility.Own, true, false, false));
            Assert.IsFalse(MinefieldOverlay.IsVisible(MinefieldVisibility.Own, false, false, true));
            Assert.IsTrue(MinefieldOverlay.IsVisible(MinefieldVisibility.DetectedEnemy, false, true, true));
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
        public void Identify_PerKindNameBuilders()
        {
            Assert.AreEqual("Deep Space", MapObjectText.Identify(null, null, MapOwnership.Unowned, null), "no object");
            Assert.AreEqual("Alpha", MapObjectText.Identify(MapObjectKind.Planet, "Alpha", MapOwnership.Other, "Hobbits"), "a planet is just its name");
            Assert.AreEqual("Scout #3", MapObjectText.Identify(MapObjectKind.Fleet, "Scout #3", MapOwnership.Own, "Humanoids"), "own fleet: no owner prefix");
            Assert.AreEqual("Hobbits Scout #3", MapObjectText.Identify(MapObjectKind.Fleet, "Scout #3", MapOwnership.Other, "Hobbits"), "other fleet: race-name prefix, never 'owned by'");
            Assert.AreEqual("Wormhole", MapObjectText.Identify(MapObjectKind.Wormhole, "w1", MapOwnership.Unowned, null));
            Assert.AreEqual("space (10, 20)", MapObjectText.DeepSpaceName(10, 20));
            Assert.AreEqual("Deep Space", MapObjectText.DeepSpaceName(null, null));
        }

        [Test]
        public void FleetName_DesignNumberAndGivenName()
        {
            Assert.AreEqual("Scout #3", MapObjectText.FleetName(MapOwnership.Own, null, null, "Scout", 3, false));
            Assert.AreEqual("Hobbits Scout #3", MapObjectText.FleetName(MapOwnership.Other, "Hobbits", null, "Scout", 3, false), "owner prefix, design, #N");
            Assert.AreEqual("Hobbits Scout+ #3", MapObjectText.FleetName(MapOwnership.Other, "Hobbits", null, "Scout", 3, true), "plus sign when more than one design");
            Assert.AreEqual("Hobbits Alpha Strike", MapObjectText.FleetName(MapOwnership.Other, "Hobbits", "Alpha Strike", "Scout", 3, true), "a named fleet has no number");
            Assert.AreEqual("Fleet #5", MapObjectText.FleetName(MapOwnership.Own, null, null, string.Empty, 5, false), "missing design falls back to the fleet noun");

            string longDesign = new string('x', 40);
            Assert.AreEqual(new string('x', 28) + " #1", MapObjectText.FleetName(MapOwnership.Own, null, null, longDesign, 1, false), "design cut to 28 characters");
        }

        [Test]
        public void SpecialObjectNames_TypeAndNoun()
        {
            Assert.AreEqual("Hobbits heavy Minefield", MapObjectText.MinefieldName(MapOwnership.Other, "Hobbits", MinefieldType.Heavy));
            Assert.AreEqual("standard Minefield", MapObjectText.MinefieldName(MapOwnership.Own, null, MinefieldType.Standard));
            Assert.AreEqual("speed bump Minefield", MapObjectText.MinefieldName(MapOwnership.Own, null, MinefieldType.SpeedBump));
            Assert.AreEqual("Hobbits Salvage", MapObjectText.PacketName(MapOwnership.Other, "Hobbits", true));
            Assert.AreEqual("Mineral Packet", MapObjectText.PacketName(MapOwnership.Own, null, false));
            Assert.AreEqual("Mystery Trader", MapObjectText.MysteryTraderName());
        }

        [Test]
        public void PopulationPopup_OwnPlanetThreeSentences()
        {
            var facts = new PlanetPopupFacts
            {
                Name = "Alpha",
                Ownership = MapOwnership.Own,
                ReportLevel = 3,
                Population = 1234,
                Habitability = 55,
                MaxPopulation = 5000,
                Growth = 100,
            };

            List<string> lines = MapObjectText.PopulationPopup(facts);
            Assert.AreEqual(3, lines.Count, "own planet at level 3 with room to grow: three sentences");
            StringAssert.Contains("Alpha", lines[0]);
            StringAssert.Contains("123400", lines[0], "population printed as the stored figure plus two zeros");
            StringAssert.Contains("500000", lines[1], "maximum population likewise formatted");
            StringAssert.Contains("10000", lines[2], "growth formatted");
            StringAssert.Contains("133400", lines[2], "new total is population plus growth");
        }

        [Test]
        public void PopulationPopup_UnownedHasNoThirdSentence()
        {
            var facts = new PlanetPopupFacts
            {
                Name = "Beta",
                Ownership = MapOwnership.Unowned,
                ReportLevel = 3,
                Habitability = 40,
                MaxPopulation = 3000,
            };

            List<string> lines = MapObjectText.PopulationPopup(facts);
            Assert.AreEqual(2, lines.Count, "unowned planet: population and habitability sentences only");
            StringAssert.Contains("Beta", lines[0]);
            StringAssert.Contains("Beta", lines[1]);
        }

        [Test]
        public void PopulationPopup_EstimateIsReportLevelGated()
        {
            var low = new PlanetPopupFacts { Name = "Gamma", Ownership = MapOwnership.Other, ReportLevel = 2, PopulationEstimate = 1000 };
            List<string> lowLines = MapObjectText.PopulationPopup(low);
            Assert.AreEqual(1, lowLines.Count);
            StringAssert.Contains(MapObjectText.PopUnknown583, lowLines[0]);
            Assert.IsFalse(lowLines[0].Contains("100000"), "no estimate below report level 3");

            var high = new PlanetPopupFacts { Name = "Gamma", Ownership = MapOwnership.Other, ReportLevel = 3, PopulationEstimate = 1000 };
            List<string> highLines = MapObjectText.PopulationPopup(high);
            StringAssert.Contains("100000", highLines[0], "the report estimate is printed with the two zeros");
        }

        [Test]
        public void PopulationPopup_HabitabilityLossOneDecimal()
        {
            var facts = new PlanetPopupFacts { Name = "Delta", Ownership = MapOwnership.Other, ReportLevel = 3, Habitability = -35 };
            List<string> lines = MapObjectText.PopulationPopup(facts);
            Assert.AreEqual(2, lines.Count);
            StringAssert.Contains("3.5%", lines[1], "|v|/10 with one decimal and a percent sign");
        }

        [Test]
        public void PopulationPopup_HabitabilitySentenceIsReportLevelGated()
        {
            var facts = new PlanetPopupFacts { Name = "Eta", Ownership = MapOwnership.Other, ReportLevel = 2, Habitability = -35, MaxPopulation = 3000 };
            List<string> lines = MapObjectText.PopulationPopup(facts);
            Assert.AreEqual(1, lines.Count, "no habitability sentence below report level 3");
        }

        [Test]
        public void PopulationPopup_OwnZeroGrowthUsesNoGrowthSentence()
        {
            var facts = new PlanetPopupFacts { Name = "Theta", Ownership = MapOwnership.Own, ReportLevel = 3, Population = 500, Habitability = 0, MaxPopulation = 4000, Growth = 0 };
            List<string> lines = MapObjectText.PopulationPopup(facts);
            Assert.AreEqual(3, lines.Count);
            StringAssert.Contains(MapObjectText.PopGrowNone532, lines[2], "v = 0 means no growth next year");
        }

        [Test]
        public void PopulationPopup_OtherPlanetDefenceCoverage()
        {
            var none = new PlanetPopupFacts { Name = "Eps", Ownership = MapOwnership.Other, ReportLevel = 3, DefenceNibble = 0 };
            List<string> noneLines = MapObjectText.PopulationPopup(none);
            StringAssert.Contains(MapObjectText.PopDefenceNone483, noneLines[noneLines.Count - 1]);

            var some = new PlanetPopupFacts { Name = "Eps", Ownership = MapOwnership.Other, ReportLevel = 3, DefenceNibble = 3 };
            List<string> someLines = MapObjectText.PopulationPopup(some);
            StringAssert.Contains("21%", someLines[someLines.Count - 1], "nibble times 6 plus 3 percent");
        }

        [Test]
        public void PopulationPopup_UnknownDefenceOmitsThirdSentence()
        {
            var facts = new PlanetPopupFacts { Name = "Zeta", Ownership = MapOwnership.Other, ReportLevel = 3, DefenceNibble = -1 };
            List<string> lines = MapObjectText.PopulationPopup(facts);
            Assert.AreEqual(1, lines.Count, "no defence reading means no third sentence");
        }

        [Test]
        public void FormatPopulation_ZeroIsASingleZero()
        {
            Assert.AreEqual("0", MapObjectText.FormatPopulation(0));
            Assert.AreEqual("123400", MapObjectText.FormatPopulation(1234));
        }

        // ---------------- search ----------------

        [Test]
        public void Find_UsesTheFiveStepRule()
        {
            var objects = new List<MapObjectEntry>
            {
                new MapObjectEntry(1, "Sol", MapObjectKind.Planet, 0, 0),
                new MapObjectEntry(2, "Sol Minor", MapObjectKind.Planet, 0, 0),
                new MapObjectEntry(3, "Space Dock", MapObjectKind.Fleet, 0, 0),
            };

            // 1. A whole-name planet match beats a whole-name fleet match.
            FindResult exact = MapSearch.Find(objects, "sol");
            Assert.AreEqual(FindMatch.PlanetExact, exact.Match);
            Assert.AreEqual("Sol", exact.Entry.Name);

            // 4. Only after no exact planet and no fleet does the remembered prefix planet win.
            FindResult prefix = MapSearch.Find(objects, "sol m");
            Assert.AreEqual(FindMatch.PlanetPrefix, prefix.Match);
            Assert.AreEqual("Sol Minor", prefix.Entry.Name);

            // 3. A whole-name fleet match (never by prefix).
            Assert.AreEqual(FindMatch.FleetByName, MapSearch.Find(objects, "space dock").Match);
            Assert.IsFalse(MapSearch.Find(objects, "space").Found, "fleets are never matched by prefix");

            // An empty query finds planet 1; no trimming is done.
            Assert.AreEqual("Sol", MapSearch.Find(objects, string.Empty).Entry.Name);
            Assert.IsFalse(MapSearch.Find(objects, " sol ").Found, "the query is not trimmed");

            Assert.IsFalse(MapSearch.Find(objects, "Zed").Found, "no result");
        }

        [Test]
        public void Find_FleetNumberBeatsFleetName_AndOnlyOwnFleets()
        {
            Fleet own = new Fleet(1);
            var objects = new List<MapObjectEntry>
            {
                new MapObjectEntry(own, "Scout One", MapObjectKind.Fleet, 0, 0),
                new MapObjectEntry("foreign", "Fleet 5 Patrol", MapObjectKind.Fleet, 0, 0),
            };
            System.Func<MapObjectEntry, int> number = entry =>
                ReferenceEquals(entry.Item, own) ? 1 : 5;
            System.Func<MapObjectEntry, bool> isOwn = entry => ReferenceEquals(entry.Item, own);

            // "Fleet 1" is read as a fleet number and finds the viewer's own fleet, even though its
            // displayed name does not contain "1".
            FindResult byNumber = MapSearch.Find(objects, "Fleet 1", number, isOwn);
            Assert.AreEqual(FindMatch.FleetByNumber, byNumber.Match);
            Assert.AreSame(own, byNumber.Entry.Item);

            // "Fleet 5" names number 5, which is a foreign fleet: it cannot be found by number, and
            // "Fleet 5" is not its whole displayed name, so there is no result.
            Assert.IsFalse(MapSearch.Find(objects, "Fleet 5", number, isOwn).Found);

            // A fleet number beats a fleet name only for the viewer's own fleets.
            var named = new List<MapObjectEntry> { new MapObjectEntry(own, "Fleet 1", MapObjectKind.Fleet, 0, 0) };
            Assert.AreEqual(FindMatch.FleetByNumber, MapSearch.Find(named, "Fleet 1", number, isOwn).Match);
        }

        [Test]
        public void Find_FleetNumberParser_MatchesTheSpec()
        {
            Assert.IsTrue(MapSearch.TryParseFleetNumber("Fleet 5", out int a) && a == 5);
            Assert.IsTrue(MapSearch.TryParseFleetNumber("Fleet #7", out int b) && b == 7);
            Assert.IsTrue(MapSearch.TryParseFleetNumber("Fleet#6", out int c) && c == 6);
            Assert.IsTrue(MapSearch.TryParseFleetNumber("12", out int d) && d == 12);
            Assert.IsTrue(MapSearch.TryParseFleetNumber("512", out int e) && e == 512, "numbers below 513 are searchable");
            Assert.IsFalse(MapSearch.TryParseFleetNumber("513", out _), "513 is not");
            Assert.IsFalse(MapSearch.TryParseFleetNumber("0", out _), "the number starts with 1-9");
            Assert.IsFalse(MapSearch.TryParseFleetNumber("Fleet 5x", out _), "the number must end the query");
        }

        [Test]
        public void Find_CaseFoldsOnlyTheLettersAZ()
        {
            Assert.IsTrue(MapSearch.EqualsAz("SMITH", "smith"));
            Assert.IsFalse(MapSearch.EqualsAz("é", "É"), "only A-Z fold");
            Assert.IsTrue(MapSearch.StartsAz("Sol Minor", "sol"));
        }

        // ---------------- planet overlays ----------------

        [Test]
        public void Habitability_ColourAndValue()
        {
            // behavior-specs-11/client-interface.md, mode 3.
            Assert.AreEqual((HabitabilityRingColour.Green, 40), PlanetOverlayRules.HabitabilityReading(40, 40, false));
            Assert.AreEqual((HabitabilityRingColour.Red, -10), PlanetOverlayRules.HabitabilityReading(-10, -10, false));
            Assert.AreEqual((HabitabilityRingColour.Yellow, 5), PlanetOverlayRules.HabitabilityReading(-10, 5, false), "not habitable now, habitable after terraforming");
            Assert.AreEqual((HabitabilityRingColour.Green, 5), PlanetOverlayRules.HabitabilityReading(-10, 5, true), "a Claim Adjuster always uses t");
            Assert.AreEqual((HabitabilityRingColour.Red, -5), PlanetOverlayRules.HabitabilityReading(10, -5, true), "a Claim Adjuster sees only green/red");
            Assert.AreEqual((HabitabilityRingColour.Green, 10), PlanetOverlayRules.HabitabilityReading(10, -5, false));
        }

        [Test]
        public void Habitability_RadiusAndInnerRadius()
        {
            Assert.AreEqual(2, PlanetOverlayRules.HabitabilityRingRadius(0));
            Assert.AreEqual(6, PlanetOverlayRules.HabitabilityRingRadius(50), "50 / 11 + 2");
            Assert.AreEqual(10, PlanetOverlayRules.HabitabilityRingRadius(100), "capped at 10");
            Assert.AreEqual(4, PlanetOverlayRules.HabitabilityRingRadius(-10), "|value| / 5 + 2");
            Assert.AreEqual(10, PlanetOverlayRules.HabitabilityRingRadius(-40), "capped at 10");
            Assert.AreEqual(10, PlanetOverlayRules.HabitabilityRingRadius(-250));

            Assert.AreEqual(1, PlanetOverlayRules.HabitabilityInnerRadius(2), "outer - 1 when outer - 2 would be below 3");
            Assert.AreEqual(2, PlanetOverlayRules.HabitabilityInnerRadius(3));
            Assert.AreEqual(3, PlanetOverlayRules.HabitabilityInnerRadius(4));
            Assert.AreEqual(3, PlanetOverlayRules.HabitabilityInnerRadius(5));
            Assert.AreEqual(8, PlanetOverlayRules.HabitabilityInnerRadius(10));
        }

        [Test]
        public void Population_ExactThresholdsStepsAndRadius()
        {
            int[] expected = { 25, 50, 100, 200, 400, 800, 1000, 1500, 2250, 3000, 4000, 5000, 6000, 7500, 9000, 11000, 14000, 18000, 25000 };
            CollectionAssert.AreEqual(expected, PlanetOverlayRules.PopulationRingThresholds, "19 ascending thresholds in units of 100 colonists");

            Assert.AreEqual(0, PlanetOverlayRules.PopulationSteps(0));
            Assert.AreEqual(0, PlanetOverlayRules.PopulationSteps(24), "under the first threshold");
            Assert.AreEqual(1, PlanetOverlayRules.PopulationSteps(25), "at the first threshold");
            Assert.AreEqual(5, PlanetOverlayRules.PopulationSteps(400), "five thresholds reached");
            Assert.AreEqual(19, PlanetOverlayRules.PopulationSteps(25000));
            Assert.AreEqual(19, PlanetOverlayRules.PopulationSteps(int.MaxValue));

            Assert.AreEqual(4, PlanetOverlayRules.PopulationRingScreenRadius(5, 0), "(n + 3) / 2");
            Assert.AreEqual(4, PlanetOverlayRules.PopulationRingScreenRadius(5, -4));
            Assert.AreEqual(7, PlanetOverlayRules.PopulationRingScreenRadius(5, 3), "n + 2 at 200%");
            Assert.AreEqual(7, PlanetOverlayRules.PopulationRingScreenRadius(5, 4), "n + 2 at 400%");
            Assert.AreEqual(21, PlanetOverlayRules.PopulationRingScreenRadius(19, 4), "up to 21 pixels");

            Assert.AreEqual(PopulationRingColour.Green, PlanetOverlayRules.PopulationColour(MapOwnership.Own, false));
            Assert.AreEqual(PopulationRingColour.Yellow, PlanetOverlayRules.PopulationColour(MapOwnership.Other, true));
            Assert.AreEqual(PopulationRingColour.Red, PlanetOverlayRules.PopulationColour(MapOwnership.Other, false));
        }

        [Test]
        public void MineralBars_ExactFormulas()
        {
            Assert.AreEqual(20, PlanetOverlayRules.ConcentrationBarSteps(100));
            Assert.AreEqual(9, PlanetOverlayRules.ConcentrationBarSteps(49), "divided by 5");
            Assert.AreEqual(20, PlanetOverlayRules.ConcentrationBarSteps(140), "capped at 20");

            Assert.AreEqual(20, PlanetOverlayRules.AmountBarSteps(5000, 5000), "(amount + M/40) / (M/20)");
            Assert.AreEqual(2, PlanetOverlayRules.AmountBarSteps(500, 5000));
            Assert.AreEqual(1, PlanetOverlayRules.AmountBarSteps(125, 5000), "the M/40 rounding half-step");
            Assert.AreEqual(0, PlanetOverlayRules.AmountBarSteps(0, 5000));
            Assert.AreEqual(0, PlanetOverlayRules.AmountBarSteps(500, 0), "no scale, no bar");
        }

        [Test]
        public void ModeGating_Predicates()
        {
            Assert.AreEqual(PlanetOverlayKind.None, MapViewOptions.ModeOverlays[0]);
            Assert.AreEqual(PlanetOverlayKind.MineralAmount, MapViewOptions.ModeOverlays[1]);
            Assert.AreEqual(PlanetOverlayKind.MineralConcentration, MapViewOptions.ModeOverlays[2]);
            Assert.AreEqual(PlanetOverlayKind.Habitability, MapViewOptions.ModeOverlays[3]);
            Assert.AreEqual(PlanetOverlayKind.Population, MapViewOptions.ModeOverlays[4]);
            Assert.AreEqual(PlanetOverlayKind.None, MapViewOptions.ModeOverlays[5]);

            Assert.IsFalse(PlanetOverlayRules.ModeShowsFleets(5), "mode 5 hides fleets");
            Assert.IsTrue(PlanetOverlayRules.ModeShowsFleets(0));

            Assert.IsTrue(PlanetOverlayRules.ModeShowsOrbitRing(0));
            Assert.IsTrue(PlanetOverlayRules.ModeShowsOrbitRing(2));
            Assert.IsFalse(PlanetOverlayRules.ModeShowsOrbitRing(3), "the value/population disc replaces the ring");
            Assert.IsFalse(PlanetOverlayRules.ModeShowsOrbitRing(5));

            Assert.AreEqual(4, PlanetOverlayRules.ModeMinReportLevel(1), "surface minerals need level 4");
            Assert.AreEqual(3, PlanetOverlayRules.ModeMinReportLevel(2));
            Assert.AreEqual(3, PlanetOverlayRules.ModeMinReportLevel(3));
            Assert.AreEqual(3, PlanetOverlayRules.ModeMinReportLevel(4));
            Assert.AreEqual(0, PlanetOverlayRules.ModeMinReportLevel(0));
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
