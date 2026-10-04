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

    // Regression tests for War Monger (WM)'s three previously-unimplemented secondary effects
    // (behavior-specs-7/race-traits.md row 6): a flat "half-square" battle-movement bonus,
    // instantly learning an enemy design's exact fit-out once scanned (instead of the usual
    // bare-hull-only record), and being unable to build minelayers or anything but SDI/Missile
    // Battery planetary defenses.
    [TestFixture]
    public class WarMongerBattleMovementTest
    {
        private static ShipDesign BuildDesignWithBattleMovementComponent(Race race, double componentBonus)
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };

            if (componentBonus != 0)
            {
                Component overthruster = new Component();
                overthruster.Properties.Add("Battle Movement", new DoubleProperty(componentBonus));
                hull.Modules.Add(new HullModule { AllocatedComponent = overthruster, ComponentCount = 1 });
            }

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update(race);
            return design;
        }

        [Test]
        public void SumProperty_BattleMovementComponents_AreActuallySummed()
        {
            // A real, adjacent pre-existing bug this fix's own plumbing exposed: "Battle Movement"
            // was a recognised XML property type (Overthruster/Maneuvering Jet components carry
            // it) but SumProperty's switch had no case for it at all, so it was silently dropped
            // every time - BattleSpeed's own "if (Summary.Properties.ContainsKey("Battle
            // Movement"))" check could never fire. Confirms the underlying aggregate actually
            // populates now, which War Monger's own bonus (below) is added on top of.
            ShipDesign design = BuildDesignWithBattleMovementComponent(null, 0.75);

            Assert.IsTrue(design.Summary.Properties.ContainsKey("Battle Movement"));
            Assert.AreEqual(0.75, ((DoubleProperty)design.Summary.Properties["Battle Movement"]).Value);
        }

        [Test]
        public void Update_WarMongerTrait_AddsAFlatHalfSquareBattleMovementBonus()
        {
            Race wmRace = new Race();
            wmRace.Traits.SetPrimary("WM");

            ShipDesign design = BuildDesignWithBattleMovementComponent(wmRace, 0);

            Assert.IsTrue(design.Summary.Properties.ContainsKey("Battle Movement"));
            Assert.AreEqual(0.5, ((DoubleProperty)design.Summary.Properties["Battle Movement"]).Value);
        }

        [Test]
        public void Update_WarMongerTrait_StacksWithAnInstalledOverthruster()
        {
            Race wmRace = new Race();
            wmRace.Traits.SetPrimary("WM");

            ShipDesign design = BuildDesignWithBattleMovementComponent(wmRace, 0.25);

            Assert.AreEqual(0.75, ((DoubleProperty)design.Summary.Properties["Battle Movement"]).Value,
                "WM's own +0.5 must add to (not replace) whatever an installed Overthruster/Maneuvering Jet already contributes");
        }

        [Test]
        public void Update_NonWarMongerRace_GetsNoBattleMovementBonus()
        {
            Race race = new Race();
            race.Traits.SetPrimary("SS");

            ShipDesign design = BuildDesignWithBattleMovementComponent(race, 0);

            Assert.IsFalse(design.Summary.Properties.ContainsKey("Battle Movement"));
        }

        [Test]
        public void Update_WarMongerTrait_SkipsTheBonusOnStarbases()
        {
            Race wmRace = new Race();
            wmRace.Traits.SetPrimary("WM");

            Component blueprint = new Component { Mass = 100 };
            Hull starbaseHull = new Hull { Modules = new List<HullModule>() }; // FuelCapacity 0 -> IsStarbase
            blueprint.Properties.Add("Hull", starbaseHull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update(wmRace);

            Assert.IsFalse(design.Summary.Properties.ContainsKey("Battle Movement"), "Starbases never move in battle, so WM's bonus doesn't apply to them");
        }
    }

    [TestFixture]
    public class WarMongerComponentRestrictionTest
    {
        [Test]
        public void RaceComponents_ExcludesAllMineLayers_ForWarMongerRaces()
        {
            Race wmRace = new Race();
            wmRace.Traits.SetPrimary("WM");
            TechLevel maxTech = new TechLevel(26);

            RaceComponents components = new RaceComponents(wmRace, maxTech);

            Assert.IsFalse(components.Contains("Mine Dispenser 50"), "Standard mine layer");
            Assert.IsFalse(components.Contains("Speed Trap 20"), "Speed-trap mine layer");
        }

        [Test]
        public void RaceComponents_AllowsMineLayers_ForNonWarMongerRaces()
        {
            Race race = new Race();
            race.Traits.SetPrimary("SS");
            TechLevel maxTech = new TechLevel(26);

            RaceComponents components = new RaceComponents(race, maxTech);

            Assert.IsTrue(components.Contains("Mine Dispenser 50"));
        }

        [Test]
        public void RaceComponents_ExcludesNonSdiDefenses_ForWarMongerRaces()
        {
            Race wmRace = new Race();
            wmRace.Traits.SetPrimary("WM");
            TechLevel maxTech = new TechLevel(26);

            RaceComponents components = new RaceComponents(wmRace, maxTech);

            Assert.IsFalse(components.Contains("Planetary Shield"));
            Assert.IsFalse(components.Contains("Laser Battery"));
            Assert.IsFalse(components.Contains("Neutron Shield"));
            Assert.IsTrue(components.Contains("SDI"), "War Monger is restricted TO SDI, not away from it");
            Assert.IsTrue(components.Contains("Missile Battery"), "War Monger is restricted TO Missile Battery, not away from it");
        }
    }

    [TestFixture]
    public class WarMongerInstantDesignRecognitionTest
    {
        private static ShipDesign BuildFullyEquippedDesign(long key)
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };

            Component engineComponent = new Component();
            Engine engine = new Engine();
            engine.FuelConsumption[0] = 100;
            engineComponent.Properties.Add("Engine", engine);
            hull.Modules.Add(new HullModule { AllocatedComponent = engineComponent, ComponentCount = 1 });

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Icon = new ShipIcon("hull0000.png", null) };
            design.Update();
            return design;
        }

        private static (ServerData serverState, EmpireData observerEmpire, Fleet targetFleet, ShipDesign targetDesign) BuildScanScenario(bool observerIsWarMonger)
        {
            ServerData serverState = new ServerData();

            EmpireData observerEmpire = new EmpireData { Id = 1 };
            if (observerIsWarMonger)
            {
                observerEmpire.Race.Traits.SetPrimary("WM");
            }
            EmpireData targetEmpire = new EmpireData { Id = 2 };
            serverState.AllEmpires.Add(observerEmpire.Id, observerEmpire);
            serverState.AllEmpires.Add(targetEmpire.Id, targetEmpire);
            observerEmpire.EmpireReports.Add(targetEmpire.Id, new EmpireIntel(targetEmpire));
            targetEmpire.EmpireReports.Add(observerEmpire.Id, new EmpireIntel(observerEmpire));

            ShipDesign observerDesign = new ShipDesign(1) { Blueprint = new Component { Mass = 100 } };
            Hull observerHull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };
            Component scannerComponent = new Component();
            scannerComponent.Properties.Add("Scanner", new Scanner { NormalScan = 1000 });
            observerHull.Modules.Add(new HullModule { AllocatedComponent = scannerComponent, ComponentCount = 1 });
            observerDesign.Blueprint.Properties.Add("Hull", observerHull);
            observerDesign.Update();

            Fleet observerFleet = new Fleet(10) { Owner = observerEmpire.Id, Position = new NovaPoint(0, 0) };
            observerFleet.Composition.Add(new ShipToken(observerDesign, 1).Key, new ShipToken(observerDesign, 1));
            observerFleet.Waypoints.Add(new Waypoint { Position = observerFleet.Position });
            observerEmpire.OwnedFleets.Add(observerFleet);

            ShipDesign targetDesign = BuildFullyEquippedDesign(2);
            Fleet targetFleet = new Fleet(20) { Owner = targetEmpire.Id, Position = new NovaPoint(1, 0) };
            targetFleet.Composition.Add(new ShipToken(targetDesign, 1).Key, new ShipToken(targetDesign, 1));
            targetFleet.Waypoints.Add(new Waypoint { Position = targetFleet.Position });
            targetEmpire.OwnedFleets.Add(targetFleet);

            return (serverState, observerEmpire, targetFleet, targetDesign);
        }

        [Test]
        public void Scan_WarMongerEmpire_RecordsTheFullEnemyDesign_NotJustTheBareHull()
        {
            var scenario = BuildScanScenario(observerIsWarMonger: true);

            new ScanStep().Process(scenario.serverState);

            ShipDesign recorded = scenario.observerEmpire.EmpireReports[2].Designs[scenario.targetDesign.Key];
            Assert.IsNotNull(recorded.Hull.Modules[0].AllocatedComponent, "War Monger must record the enemy's actual fitted component, not a stripped bare hull");
            Assert.IsTrue(recorded.Hull.Modules[0].AllocatedComponent.Properties.ContainsKey("Engine"));
        }

        [Test]
        public void Scan_NonWarMongerEmpire_StillOnlyRecordsTheBareHull()
        {
            var scenario = BuildScanScenario(observerIsWarMonger: false);

            new ScanStep().Process(scenario.serverState);

            ShipDesign recorded = scenario.observerEmpire.EmpireReports[2].Designs[scenario.targetDesign.Key];
            Assert.IsNull(recorded.Hull.Modules[0].AllocatedComponent, "A non-WM empire only ever sees the bare hull from a scan, same as before this fix");
        }
    }
}
