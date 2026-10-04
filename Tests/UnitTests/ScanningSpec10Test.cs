namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    using NUnit.Framework;

    /// <summary>
    /// behavior-specs-10 scanning/detection rows: Space Demolition minefield detection of
    /// cloaked fleets, the minefield detection radius, wormhole probabilistic detection and the
    /// Jack of All Trades built-in scanner (fleet-movement-scanning-cargo.md §3, race-traits.md §2).
    /// </summary>
    [TestFixture]
    public class ScanningSpec10Test
    {
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> rolls;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = new Queue<int>(rolls);
            }

            public int Calls { get; private set; }

            public override int Next(int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : 0;
            }

            public override int Next(int minValue, int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : minValue;
            }
        }

        private ServerData serverState;
        private EmpireData observer;
        private EmpireData target;
        private long nextFleetKey = 100;

        [SetUp]
        public void SetUp()
        {
            serverState = new ServerData();
            observer = new EmpireData { Id = 1 };
            target = new EmpireData { Id = 2 };
            serverState.AllEmpires.Add(observer.Id, observer);
            serverState.AllEmpires.Add(target.Id, target);
            observer.EmpireReports.Add(target.Id, new EmpireIntel(target));
            target.EmpireReports.Add(observer.Id, new EmpireIntel(observer));
        }

        private static ShipDesign BuildDesign(long key, string hullName, int normalScan = 0, int penScan = 0, double rawCloakUnits = 0)
        {
            Component blueprint = new Component { Mass = 100, Name = hullName };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };

            if (normalScan > 0 || penScan > 0)
            {
                Component scanner = new Component();
                scanner.Properties.Add("Scanner", new Scanner { NormalScan = normalScan, PenetratingScan = penScan });
                hull.Modules.Add(new HullModule { AllocatedComponent = scanner, ComponentCount = 1 });
            }

            if (rawCloakUnits > 0)
            {
                Component cloak = new Component();
                cloak.Properties.Add("Cloak", new ProbabilityProperty(rawCloakUnits));
                hull.Modules.Add(new HullModule { AllocatedComponent = cloak, ComponentCount = 1 });
            }

            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Icon = new ShipIcon("hull0000.png", null) };
            design.Update();
            return design;
        }

        private Fleet AddFleet(EmpireData owner, ShipDesign design, double x, double y)
        {
            Fleet fleet = new Fleet(nextFleetKey++) { Owner = owner.Id, Position = new NovaPoint((int)x, (int)y) };
            fleet.Composition.Add(new ShipToken(design, 1).Key, new ShipToken(design, 1));
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position });
            owner.OwnedFleets.Add(fleet);
            return fleet;
        }

        private Minefield AddMinefield(EmpireData owner, int mines, int x, int y)
        {
            Minefield field = new Minefield { NumberOfMines = mines };
            field.Key = owner.GetNextMinefieldKey();
            field.Owner = owner.Id;
            field.Position = new NovaPoint(x, y);
            serverState.AllMinefields[field.Key] = field;
            return field;
        }

        private Wormhole AddWormhole(long key, int x, int y)
        {
            Wormhole wormhole = new Wormhole { Key = key, Position = new NovaPoint(x, y) };
            serverState.AllWormholes.Add(wormhole.Key, wormhole);
            return wormhole;
        }

        // ------------------------------------------------------------ Space Demolition fields

        // Example 3: an 80%-cloaked scout inside an SD field is spotted 20% of the time per year,
        // one 0-99 roll: 19 spots it, 20 does not. No scanner sees it.
        [TestCase(19, true)]
        [TestCase(20, false)]
        public void SpaceDemolitionField_SpotsAnEightyPercentCloakedFleet_TwentyPercentOfTheTime(int roll, bool expectDetected)
        {
            observer.Race.Traits.SetPrimary("SD");
            AddMinefield(observer, 400, 0, 0);
            Fleet scout = AddFleet(target, BuildDesign(2, "Scout", rawCloakUnits: 420), 5, 0);

            ScriptedRandom random = new ScriptedRandom(roll);
            new ScanStep(random).Process(serverState);

            Assert.AreEqual(80, scout.Cloaked, 1e-9, "precondition: 420 raw cloak units is 80%");
            Assert.AreEqual(1, random.Calls, "one roll for the one fleet inside the field");
            Assert.AreEqual(expectDetected, observer.FleetReports.ContainsKey(scout.Key));
        }

        [Test]
        public void SpaceDemolitionField_AlwaysSpotsAnUncloakedFleet_WithoutARoll()
        {
            observer.Race.Traits.SetPrimary("SD");
            AddMinefield(observer, 400, 0, 0);
            Fleet visitor = AddFleet(target, BuildDesign(2, "Scout"), 19, 0);

            ScriptedRandom random = new ScriptedRandom(99);
            new ScanStep(random).Process(serverState);

            Assert.IsTrue(observer.FleetReports.ContainsKey(visitor.Key));
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void SpaceDemolitionField_DoesNotSeeOutsideTheField_OrForOtherRaces()
        {
            // A fleet just outside an SD field (radius 20: 21 ly away) is not rolled for.
            observer.Race.Traits.SetPrimary("SD");
            AddMinefield(observer, 400, 0, 0);
            Fleet outside = AddFleet(target, BuildDesign(2, "Scout"), 21, 0);

            ScriptedRandom random = new ScriptedRandom(0);
            new ScanStep(random).Process(serverState);

            Assert.IsFalse(observer.FleetReports.ContainsKey(outside.Key));
            Assert.AreEqual(0, random.Calls);

            // A non-SD race's field is no detection layer at all.
            SetUp();
            observer.Race.Traits.SetPrimary("HE");
            AddMinefield(observer, 400, 0, 0);
            Fleet inside = AddFleet(target, BuildDesign(3, "Scout"), 5, 0);

            new ScanStep(new ScriptedRandom(0)).Process(serverState);

            Assert.IsFalse(observer.FleetReports.ContainsKey(inside.Key));
        }

        // ------------------------------------------------------------ minefield visibility

        // A 50 ly scanner sees an unknown field only within r/4 = 12.5 ly.
        [TestCase(12, true)]
        [TestCase(13, false)]
        public void MinefieldDetection_AnUnknownField_NeedsTheQuarterRange(int fieldX, bool expectVisible)
        {
            AddFleet(observer, BuildDesign(1, "Scout", normalScan: 50), 0, 0);
            // 100 mines = radius 10, so the fleet at 12-13 ly is outside the field (no inside test).
            Minefield field = AddMinefield(target, 100, fieldX, 0);

            new ScanStep(new ScriptedRandom()).Process(serverState);

            Assert.AreEqual(expectVisible, observer.VisibleMinefields.Contains(field.Key));
            Assert.AreEqual(expectVisible, observer.CanSeeMinefield(field));
            Assert.AreEqual(expectVisible, IntelWriter.VisibleMinefieldsFor(serverState, observer).ContainsKey(field.Key));
        }

        [Test]
        public void MinefieldDetection_AShipInsideAField_SeesItWithNoScanner()
        {
            AddFleet(observer, BuildDesign(1, "Scout", normalScan: 0, penScan: 0), 0, 0);
            Minefield field = AddMinefield(target, 100, 5, 0); // radius 10: the fleet at 5 ly is inside

            new ScanStep(new ScriptedRandom()).Process(serverState);

            Assert.IsTrue(observer.VisibleMinefields.Contains(field.Key), "a ship inside a field sees it with no scanner");
        }

        [Test]
        public void MinefieldDetection_APenetratingScanner_SeesAnUnknownFieldAtItsPenetratingRange()
        {
            AddFleet(observer, BuildDesign(1, "Scout", normalScan: 10, penScan: 60), 0, 0);
            Minefield field = AddMinefield(target, 100, 50, 0); // beyond r and r/4, within p

            new ScanStep(new ScriptedRandom()).Process(serverState);

            Assert.IsTrue(observer.VisibleMinefields.Contains(field.Key), "a penetrating scanner detects an unknown field");
        }

        [Test]
        public void MinefieldVisibility_AFieldKnownToTheRace_IsSeenAtTheFullNormalRange()
        {
            AddFleet(observer, BuildDesign(1, "Scout", normalScan: 50), 0, 0);
            Minefield known = AddMinefield(target, 256, 40, 0);   // 40 ly: within r, beyond r/4
            known.MarkKnown(observer.Id);
            Minefield unknown = AddMinefield(target, 256, 40, 40); // about 57 ly: beyond r, unknown

            new ScanStep(new ScriptedRandom()).Process(serverState);

            Assert.IsTrue(observer.VisibleMinefields.Contains(known.Key), "a known field within normal range is seen");
            Assert.IsFalse(observer.VisibleMinefields.Contains(unknown.Key), "an unknown field beyond r/4 is not seen");

            Dictionary<long, Minefield> observerTurn = IntelWriter.VisibleMinefieldsFor(serverState, observer);
            Assert.IsTrue(observerTurn.ContainsKey(known.Key));
            Assert.IsFalse(observerTurn.ContainsKey(unknown.Key), "a field the player cannot see stays out of its turn file");

            Dictionary<long, Minefield> ownerTurn = IntelWriter.VisibleMinefieldsFor(serverState, target);
            Assert.AreEqual(2, ownerTurn.Count, "the owner always sees its own fields");
        }

        [Test]
        public void VisibleMinefields_AreRecomputedEveryYear()
        {
            Fleet scout = AddFleet(observer, BuildDesign(1, "Scout", normalScan: 50), 0, 0);
            Minefield field = AddMinefield(target, 100, 40, 0);
            field.MarkKnown(observer.Id); // so the full normal range applies

            new ScanStep(new ScriptedRandom()).Process(serverState);
            Assert.IsTrue(observer.VisibleMinefields.Contains(field.Key));

            scout.Position = new NovaPoint(-100, 0);
            new ScanStep(new ScriptedRandom()).Process(serverState);
            Assert.IsFalse(observer.VisibleMinefields.Contains(field.Key), "a known field out of range is not seen this year");
        }

        // ------------------------------------------------------------ wormholes

        [Test]
        public void UndiscoveredWormhole_NeedsTheQuarterRange_ALocatedOneTheFullRange()
        {
            AddFleet(observer, BuildDesign(1, "Scout", normalScan: 100), 0, 0);
            Wormhole near = AddWormhole(7, 20, 0);  // 20 ly: within r/4 = 25
            Wormhole far = AddWormhole(8, 60, 0);   // 60 ly: beyond r/4, within r

            ScriptedRandom noRolls = new ScriptedRandom();
            new ScanStep(noRolls).Process(serverState);

            Assert.AreEqual(0, noRolls.Calls, "detection is deterministic: no roll");
            Assert.IsTrue(observer.WormholeReports.ContainsKey(near.Key), "an unlocated wormhole is seen within r/4");
            Assert.IsFalse(observer.WormholeReports.ContainsKey(far.Key), "60 ly is beyond r/4 for an unlocated one");

            // Once located, the same end is seen at the full normal range.
            far.Located.Add(observer.Id);
            new ScanStep(new ScriptedRandom()).Process(serverState);
            Assert.IsTrue(observer.WormholeReports.ContainsKey(far.Key), "a located wormhole is seen at the full range");
        }

        [Test]
        public void Wormhole_OutOfRange_IsNotSeen_AndItIsReportedAtItsCurrentPosition()
        {
            Fleet scout = AddFleet(observer, BuildDesign(1, "Scout", normalScan: 100), 0, 0);
            Wormhole wormhole = AddWormhole(7, 101, 0);

            ScriptedRandom random = new ScriptedRandom(99);
            new ScanStep(random).Process(serverState);
            Assert.AreEqual(0, random.Calls, "detection is deterministic: no roll");
            Assert.IsFalse(observer.WormholeReports.ContainsKey(wormhole.Key), "101 ly is beyond r/4 and it is not located");

            scout.Position = new NovaPoint(95, 0); // 6 ly from the end: within r/4
            new ScanStep(new ScriptedRandom()).Process(serverState);
            Assert.IsTrue(observer.WormholeReports.ContainsKey(wormhole.Key));

            // A wormhole is reported at its current (drifted) position.
            wormhole.Position = new NovaPoint(94, 0);
            new ScanStep(new ScriptedRandom()).Process(serverState);
            Assert.AreEqual(94, observer.WormholeReports[wormhole.Key].Position.X);
        }

        [Test]
        public void WormholeReports_RoundTripThroughTheEmpireXml()
        {
            observer.WormholeReports.Add(7, new WormholeIntel(new Wormhole { Key = 7, PairedKey = 8, Position = new NovaPoint(12, 34) }, 2105));
            observer.VisibleMinefields.Add(0x1000002);

            observer.Race = new Race();
            XmlDocument xmldoc = new XmlDocument();
            XmlElement root = xmldoc.CreateElement("Root");
            xmldoc.AppendChild(root);
            root.AppendChild(observer.ToXml(xmldoc));

            EmpireData reloaded = new EmpireData(root.FirstChild);

            Assert.IsTrue(reloaded.WormholeReports.ContainsKey(7));
            Assert.AreEqual(8, reloaded.WormholeReports[7].PairedKey);
            Assert.AreEqual(2105, reloaded.WormholeReports[7].Year);
            Assert.AreEqual(12, reloaded.WormholeReports[7].Position.X);
            Assert.AreEqual(34, reloaded.WormholeReports[7].Position.Y);
            Assert.IsTrue(reloaded.VisibleMinefields.Contains(0x1000002));
        }

        // ------------------------------------------------------------ JOAT built-in scanner

        [Test]
        public void JoatBuiltInScanner_OnScoutFrigateDestroyer_ScalesWithElectronics()
        {
            Race joat = new Race();
            joat.Traits.SetPrimary("JOAT");
            TechLevel tech = new TechLevel();
            tech[TechLevel.ResearchField.Electronics] = 5;

            foreach (string hull in ScannerRules.JoatScannerHulls)
            {
                ScannerRules.DesignScanRanges(BuildDesign(1, hull), joat, tech, out int normal, out int pen);
                Assert.AreEqual(100, normal, hull);
                Assert.AreEqual(50, pen, hull);
            }

            ScannerRules.DesignScanRanges(BuildDesign(2, "Cruiser"), joat, tech, out int cruiserNormal, out int cruiserPen);
            Assert.AreEqual(0, cruiserNormal, "other hulls have no built-in scanner");
            Assert.AreEqual(0, cruiserPen);

            Race other = new Race();
            other.Traits.SetPrimary("HE");
            ScannerRules.DesignScanRanges(BuildDesign(3, "Scout"), other, tech, out int heNormal, out int hePen);
            Assert.AreEqual(0, heNormal, "only JOAT gets it");
            Assert.AreEqual(0, hePen);
        }

        [Test]
        public void JoatBuiltInScanner_CombinesWithAFittedScanner_AsAFourthRootSum()
        {
            Race joat = new Race();
            joat.Traits.SetPrimary("JOAT");
            TechLevel tech = new TechLevel();
            tech[TechLevel.ResearchField.Electronics] = 5;

            // A fitted 100/0 scanner plus the built-in 100/50: fourth root of 2 x 100^4 = 118.
            ScannerRules.DesignScanRanges(BuildDesign(1, "Scout", normalScan: 100), joat, tech, out int normal, out int pen);
            Assert.AreEqual(118, normal);
            Assert.AreEqual(50, pen);
        }

        [Test]
        public void JoatBuiltInPenetratingScanner_SurvivesNoAdvancedScanners_AndItsNormalRangeDoubles()
        {
            Race joat = new Race();
            joat.Traits.SetPrimary("JOAT");
            joat.Traits.Add("NAS");
            TechLevel tech = new TechLevel();
            tech[TechLevel.ResearchField.Electronics] = 5;

            ScannerRules.DesignScanRanges(BuildDesign(1, "Destroyer"), joat, tech, out int normal, out int pen);
            Assert.AreEqual(200, normal);
            Assert.AreEqual(50, pen);
        }

        [Test]
        public void JoatScout_WithNoScannerFitted_DetectsAFleetAndPenetratesAPlanet()
        {
            observer.Race.Traits.SetPrimary("JOAT");
            observer.ResearchLevels[TechLevel.ResearchField.Electronics] = 5;
            AddFleet(observer, BuildDesign(1, "Scout"), 0, 0);
            Fleet distant = AddFleet(target, BuildDesign(2, "Cruiser"), 95, 0);

            Star star = new Star { Name = "Faraway", Position = new NovaPoint(45, 0) };
            serverState.AllStars.Add(star.Name, star);

            new ScanStep(new ScriptedRandom()).Process(serverState);

            Assert.IsTrue(observer.FleetReports.ContainsKey(distant.Key), "100 ly built-in normal range");
            Assert.AreEqual(serverState.TurnYear, observer.StarReports[star.Name].Year, "50 ly built-in penetrating range reads the planet");
        }
    }
}
