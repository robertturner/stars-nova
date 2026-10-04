#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Shared set-up for the spec-driven coverage of behavior-specs-10/
    /// fleet-movement-scanning-cargo.md (coverage-table rows 1, 3, 4, 7, 11, 14, 15, 24, 25, 27,
    /// 28, 47 and 62). Every expected figure is the specification's.
    /// </summary>
    internal static class MovementCoverageKit
    {
        /// <summary>Returns queued values from Next(...) (then maxValue - 1, i.e. "no event") and
        /// 0.5 from NextDouble, counting every call.</summary>
        public class ScriptedRandom : Random
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
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
            }

            public override int Next(int minValue, int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
            }

            public override double NextDouble()
            {
                Calls++;
                return 0.5;
            }
        }

        public static Component Real(string name)
        {
            Component component = new AllComponents().Fetch(name);
            Assert.IsNotNull(component, name + " must exist in components.xml");
            return component;
        }

        /// <summary>A design on a test hull of the given mass, fuel tank and hold, plus parts.</summary>
        public static ShipDesign Design(long key, int hullMass, int hullFuel, int hullCargo, params (Component part, int count)[] parts)
        {
            Component blueprint = new Component { Name = "Test Hull", Mass = hullMass };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = hullFuel, BaseCargo = hullCargo, ArmorStrength = 10 };
            foreach ((Component part, int count) in parts)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = count });
            }

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Name = "Design " + key, Blueprint = blueprint };
            design.Update();
            return design;
        }

        /// <summary>A design on a real hull from components.xml, plus parts.</summary>
        public static ShipDesign RealHullDesign(long key, string hullName, Race race, params (Component part, int count)[] parts)
        {
            Component blueprint = Real(hullName);
            Hull hull = new Hull((Hull)blueprint.Properties["Hull"]);
            blueprint.Properties["Hull"] = hull;
            foreach ((Component part, int count) in parts)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = count });
            }

            ShipDesign design = new ShipDesign(key) { Name = hullName + " " + key, Blueprint = blueprint };
            design.Update(race);
            return design;
        }

        /// <summary>A fleet of <paramref name="quantity"/> ships of one design at (0, 0), fuel full.</summary>
        public static Fleet Fleet(ShipDesign design, int quantity, ushort owner = 1, uint id = 1)
        {
            Fleet fleet = new Fleet(((long)owner << 32) | id);
            fleet.Owner = owner;
            fleet.Name = "Fleet " + id;
            fleet.Position = new NovaPoint(0, 0);
            ShipToken token = new ShipToken(design, quantity);
            fleet.Composition.Add(token.Key, token);
            fleet.FuelAvailable = fleet.TotalFuelCapacity;
            return fleet;
        }

        /// <summary>The usual two waypoints: the fleet's own position, then a deep-space point
        /// <paramref name="x"/> ly east at <paramref name="warp"/>.</summary>
        public static void OrderEast(Fleet fleet, int x, int warp)
        {
            fleet.Waypoints.Clear();
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position, Destination = "Space at start", WarpFactor = 0, Task = new NoTask() });
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(x, 0), Destination = "Space at target", WarpFactor = warp, Task = new NoTask() });
        }

        public static ServerData Game(Race race, Fleet fleet, out EmpireData empire)
        {
            ServerData serverData = new ServerData();
            empire = new SimpleEmpireData();
            empire.Id = fleet.Owner;
            empire.AvailableComponents = new RaceComponents();
            empire.Race = race;
            serverData.AllEmpires.Add(empire.Id, empire);
            empire.AddOrUpdateFleet(fleet);
            return serverData;
        }

        /// <summary>Runs just the per-fleet movement step (TurnGenerator.ProcessFleet) with the
        /// given Random swapped into the generator. Returns true if the fleet was destroyed.</summary>
        public static bool RunMovement(ServerData serverData, Fleet fleet, Random random)
        {
            SimpleTurnGenerator generator = new SimpleTurnGenerator(serverData);
            typeof(TurnGenerator)
                .GetField("rand", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(generator, random);
            return (bool)typeof(TurnGenerator)
                .GetMethod("ProcessFleet", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(generator, new object[] { fleet });
        }

        public static Race PlainRace(params string[] lesserTraits)
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            foreach (string trait in lesserTraits)
            {
                race.Traits.Add(trait);
            }

            return race;
        }

        public static int ShipCount(Fleet fleet)
        {
            return fleet.Composition.Values.Sum(token => token.Quantity);
        }
    }

    /// <summary>
    /// Row 1, §1: "The maximum distance a fleet can cover in a single year at warp factor N is N²
    /// light-years"; a nearer waypoint is reached the same year.
    /// </summary>
    [TestFixture]
    public class WarpDistanceCoverageTest
    {
        private static Fleet FastFleet()
        {
            ShipDesign design = MovementCoverageKit.Design(1, 50, 1000000, 0, (MovementCoverageKit.Real("Trans-Star 10"), 1));
            return MovementCoverageKit.Fleet(design, 1);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(9)]
        [TestCase(10)]
        public void AYearAtWarpN_CoversNSquaredLightYears(int warp)
        {
            Fleet fleet = FastFleet();
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(5000, 0), WarpFactor = warp, Task = new NoTask() });
            double availableTime = 1.0;

            Fleet.TravelStatus status = fleet.Move(ref availableTime, MovementCoverageKit.PlainRace());

            Assert.AreEqual(Fleet.TravelStatus.InTransit, status);
            Assert.AreEqual(warp * warp, fleet.Position.X);
            Assert.AreEqual(0, fleet.Position.Y);
        }

        [Test]
        public void ANearerWaypoint_IsReachedTheSameYear()
        {
            Fleet fleet = FastFleet();
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(50, 0), WarpFactor = 9, Task = new NoTask() });
            double availableTime = 1.0;

            Fleet.TravelStatus status = fleet.Move(ref availableTime, MovementCoverageKit.PlainRace());

            Assert.AreEqual(Fleet.TravelStatus.Arrived, status);
            Assert.AreEqual(50, fleet.Position.X);
        }

        /// <summary>Through the turn's movement step: 81 ly at warp 9 each year, continuing from
        /// where it stopped the following year.</summary>
        [Test]
        public void TheMovementStep_CoversWarpSquaredEachYear_AndContinuesNextYear()
        {
            Fleet fleet = FastFleet();
            MovementCoverageKit.OrderEast(fleet, 1000, 9);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);

            MovementCoverageKit.RunMovement(serverData, fleet, new MovementCoverageKit.ScriptedRandom());
            Assert.AreEqual(81, fleet.Position.X);

            MovementCoverageKit.RunMovement(serverData, fleet, new MovementCoverageKit.ScriptedRandom());
            Assert.AreEqual(162, fleet.Position.X);
        }
    }

    /// <summary>
    /// Row 3, §1: at warp 10, unless the engine is rated safe at warp 10 (Interspace-10, Trans-Star
    /// 10), "every individual ship attempting warp 10 risks outright destruction: a 10% chance per
    /// year, rolled independently per ship (not per fleet, and not scaled by how many engines that
    /// ship's hull mounts)".
    /// </summary>
    [TestFixture]
    public class Warp10DestructionCoverageTest
    {
        private static Fleet LongHumpFleet(int ships, int enginesPerShip = 1)
        {
            ShipDesign design = MovementCoverageKit.Design(1, 50, 1000000, 0, (MovementCoverageKit.Real("Long Hump 6"), enginesPerShip));
            return MovementCoverageKit.Fleet(design, ships);
        }

        [Test]
        public void OneShip_IsLostOnExactlyOneRollInTen()
        {
            int lost = 0;
            for (int roll = 0; roll < 10; roll++)
            {
                Fleet fleet = LongHumpFleet(1);
                MovementCoverageKit.OrderEast(fleet, 1000, 10);
                ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);

                if (MovementCoverageKit.RunMovement(serverData, fleet, new MovementCoverageKit.ScriptedRandom(roll)))
                {
                    lost++;
                    Assert.AreEqual(0, MovementCoverageKit.ShipCount(fleet));
                }
            }

            Assert.AreEqual(1, lost, "10% a year");
        }

        [Test]
        public void EachShip_RollsForItself()
        {
            Fleet fleet = LongHumpFleet(3);
            MovementCoverageKit.OrderEast(fleet, 1000, 10);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);

            // Find the losing roll value first (one value in ten).
            int losing = LosingRoll();
            int safe = (losing + 1) % 10;
            MovementCoverageKit.ScriptedRandom random = new MovementCoverageKit.ScriptedRandom(losing, safe, losing);

            bool destroyed = MovementCoverageKit.RunMovement(serverData, fleet, random);

            Assert.IsFalse(destroyed);
            Assert.AreEqual(3, random.Calls, "One roll per ship, not one per fleet");
            Assert.AreEqual(1, MovementCoverageKit.ShipCount(fleet), "Two of the three ships were lost");
            Assert.AreEqual(100, fleet.Position.X, "The survivor travels on at warp 10 (100 ly)");
        }

        [Test]
        public void TheRisk_IsNotScaledByTheNumberOfEngines()
        {
            Fleet fleet = LongHumpFleet(2, enginesPerShip: 4);
            MovementCoverageKit.OrderEast(fleet, 1000, 10);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);
            MovementCoverageKit.ScriptedRandom random = new MovementCoverageKit.ScriptedRandom();

            MovementCoverageKit.RunMovement(serverData, fleet, random);

            Assert.AreEqual(2, random.Calls, "Two ships with four engines each: still one roll per ship");
        }

        [TestCase("Interspace 10")]
        [TestCase("Trans-Star 10")]
        public void ASafeWarp10Engine_IsFullyExempt(string engine)
        {
            ShipDesign design = MovementCoverageKit.Design(1, 50, 1000000, 0, (MovementCoverageKit.Real(engine), 1));
            Fleet fleet = MovementCoverageKit.Fleet(design, 3);
            MovementCoverageKit.OrderEast(fleet, 1000, 10);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);
            int losing = LosingRoll();
            MovementCoverageKit.ScriptedRandom random = new MovementCoverageKit.ScriptedRandom(losing, losing, losing);

            MovementCoverageKit.RunMovement(serverData, fleet, random);

            Assert.AreEqual(3, MovementCoverageKit.ShipCount(fleet));
            Assert.AreEqual(0, random.Calls, "No roll at all");
            Assert.AreEqual(100, fleet.Position.X);
        }

        [Test]
        public void Warp9_CarriesNoRisk()
        {
            Fleet fleet = LongHumpFleet(3);
            MovementCoverageKit.OrderEast(fleet, 1000, 9);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);
            int losing = LosingRoll();
            MovementCoverageKit.ScriptedRandom random = new MovementCoverageKit.ScriptedRandom(losing, losing, losing);

            MovementCoverageKit.RunMovement(serverData, fleet, random);

            Assert.AreEqual(3, MovementCoverageKit.ShipCount(fleet));
            Assert.AreEqual(0, random.Calls);
        }

        /// <summary>The single roll value (of 0-9) that destroys one ship.</summary>
        private static int LosingRoll()
        {
            for (int roll = 0; roll < 10; roll++)
            {
                Fleet fleet = LongHumpFleet(1);
                MovementCoverageKit.OrderEast(fleet, 1000, 10);
                ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);
                if (MovementCoverageKit.RunMovement(serverData, fleet, new MovementCoverageKit.ScriptedRandom(roll)))
                {
                    return roll;
                }
            }

            Assert.Fail("No roll value destroys a ship at warp 10");
            return -1;
        }
    }

    /// <summary>
    /// Row 4, §1: with the Cheap Engines lesser trait, "any time such a fleet is ordered above
    /// warp 6, there is a 10% chance per year that the engines simply fail to engage at all (the
    /// fleet does not move that turn, and is not damaged or destroyed".
    /// </summary>
    [TestFixture]
    public class CheapEnginesCoverageTest
    {
        private static Fleet Fleet(int ships = 3)
        {
            ShipDesign design = MovementCoverageKit.Design(1, 50, 1000000, 0, (MovementCoverageKit.Real("Long Hump 6"), 1));
            return MovementCoverageKit.Fleet(design, ships);
        }

        [Test]
        public void AboveWarp6_TheEnginesFailOnExactlyOneRollInTen_WithoutDamage()
        {
            int failed = 0;
            for (int roll = 0; roll < 10; roll++)
            {
                Fleet fleet = Fleet();
                double armorBefore = fleet.TotalArmorStrength;
                MovementCoverageKit.OrderEast(fleet, 1000, 7);
                ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace("CE"), fleet, out _);

                bool destroyed = MovementCoverageKit.RunMovement(serverData, fleet, new MovementCoverageKit.ScriptedRandom(roll));

                Assert.IsFalse(destroyed);
                Assert.AreEqual(3, MovementCoverageKit.ShipCount(fleet), "Never damaged or destroyed");
                Assert.AreEqual(armorBefore, fleet.TotalArmorStrength);
                if (fleet.Position.X == 0)
                {
                    failed++;
                }
                else
                {
                    Assert.AreEqual(49, fleet.Position.X, "Otherwise it moves at warp 7");
                }
            }

            Assert.AreEqual(1, failed, "10% a year");
        }

        [Test]
        public void OneRollPerFleet()
        {
            Fleet fleet = Fleet(3);
            MovementCoverageKit.OrderEast(fleet, 1000, 7);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace("CE"), fleet, out _);
            MovementCoverageKit.ScriptedRandom random = new MovementCoverageKit.ScriptedRandom();

            MovementCoverageKit.RunMovement(serverData, fleet, random);

            Assert.AreEqual(1, random.Calls, "The fleet's engines engage or not, as one");
        }

        [Test]
        public void AtWarp6_ThereIsNoRisk()
        {
            Fleet fleet = Fleet();
            MovementCoverageKit.OrderEast(fleet, 1000, 6);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace("CE"), fleet, out _);
            MovementCoverageKit.ScriptedRandom random = new MovementCoverageKit.ScriptedRandom(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);

            MovementCoverageKit.RunMovement(serverData, fleet, random);

            Assert.AreEqual(0, random.Calls);
            Assert.AreEqual(36, fleet.Position.X);
        }

        [Test]
        public void WithoutTheTrait_ThereIsNoRisk()
        {
            Fleet fleet = Fleet();
            MovementCoverageKit.OrderEast(fleet, 1000, 9);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);
            MovementCoverageKit.ScriptedRandom random = new MovementCoverageKit.ScriptedRandom(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);

            MovementCoverageKit.RunMovement(serverData, fleet, random);

            Assert.AreEqual(0, random.Calls);
            Assert.AreEqual(81, fleet.Position.X);
        }
    }

    /// <summary>
    /// Row 7, §2 "Confirmed formula": fuel used = (per-warp table value x distance in ly x mass in
    /// kT) / 20,000, rounded up ("adds 9 and then divides by 10"); with Improved Fuel Efficiency
    /// the per-warp table value itself "is reduced by 15% of itself, rounded down". Long Hump 6's
    /// table (component-stats.tsv): warp 5 = 100, warp 6 = 105, warp 9 = 900. The test design
    /// weighs 200 kT (191 hull + the 9 kT engine) and carries no cargo.
    /// </summary>
    [TestFixture]
    public class FuelConsumptionCoverageTest
    {
        private static ShipDesign TwoHundredKiloton()
        {
            ShipDesign design = MovementCoverageKit.Design(1, 191, 100000, 0, (MovementCoverageKit.Real("Long Hump 6"), 1));
            Assert.AreEqual(200, design.Mass, "test setup: 191 kT hull + 9 kT Long Hump 6");
            return design;
        }

        /// <summary>A full year's fuel at warp N is value x N² x mass / 20,000.</summary>
        [TestCase(5, 25.0)]   // 100 x 25 x 200 / 20,000
        [TestCase(6, 37.8)]   // 105 x 36 x 200 / 20,000
        [TestCase(9, 729.0)]  // 900 x 81 x 200 / 20,000
        public void PerYear_IsTableValueTimesDistanceTimesMassOver20000(int warp, double expected)
        {
            ShipDesign design = TwoHundredKiloton();

            Assert.AreEqual(expected, design.FuelConsumption(warp, MovementCoverageKit.PlainRace(), 0), 1e-9);
        }

        /// <summary>IFE: warp 5's 100 becomes 85; warp 6's 105 becomes 105 - floor(15.75) = 90
        /// (not 105 x 0.85 = 89.25).</summary>
        [TestCase(5, 21.25)]  // 85 x 25 x 200 / 20,000
        [TestCase(6, 32.4)]   // 90 x 36 x 200 / 20,000
        public void ImprovedFuelEfficiency_CutsTheTableValueBy15PercentRoundedDown(int warp, double expected)
        {
            ShipDesign design = TwoHundredKiloton();

            Assert.AreEqual(expected, design.FuelConsumption(warp, MovementCoverageKit.PlainRace("IFE"), 0), 1e-9);
        }

        /// <summary>Mass is each design's mass times its ship count: ten 200 kT ships at warp 5
        /// over 25 ly use 10 x 25 = 250 mg.</summary>
        [Test]
        public void AMove_ChargesTheWholeFleetsMass()
        {
            Fleet fleet = MovementCoverageKit.Fleet(TwoHundredKiloton(), 10);
            fleet.FuelAvailable = 1000;
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(25, 0), WarpFactor = 5, Task = new NoTask() });
            double availableTime = 1.0;

            fleet.Move(ref availableTime, MovementCoverageKit.PlainRace());

            Assert.AreEqual(25, fleet.Position.X);
            Assert.AreEqual(750, fleet.FuelAvailable, 1e-9);
        }

        /// <summary>The usage figure is rounded up: 37.8 mg is charged as 38, and with IFE 32.4 as 33.</summary>
        [TestCase(false, 38)]
        [TestCase(true, 33)]
        public void TheFuelUsed_IsRoundedUp(bool improvedFuelEfficiency, int charged)
        {
            Fleet fleet = MovementCoverageKit.Fleet(TwoHundredKiloton(), 1);
            fleet.FuelAvailable = 1000;
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(1000, 0), WarpFactor = 6, Task = new NoTask() });
            double availableTime = 1.0;
            Race race = improvedFuelEfficiency ? MovementCoverageKit.PlainRace("IFE") : MovementCoverageKit.PlainRace();

            fleet.Move(ref availableTime, race);

            Assert.AreEqual(36, fleet.Position.X);
            Assert.AreEqual(1000 - charged, fleet.FuelAvailable, 1e-9);
        }
    }

    /// <summary>
    /// Row 11, §2 "Caller-side confirmation": "an underfueled fleet's travel this turn is capped to
    /// the distance its remaining fuel can actually pay for, rather than completing the full
    /// ordered-speed hop and only afterward clamping fuel to zero"; fuel never goes negative.
    /// </summary>
    [TestFixture]
    public class FuelExhaustionCoverageTest
    {
        /// <summary>One 200 kT ship at warp 9 burns 9 mg a light-year (900 x 200 / 20,000); 90 mg
        /// pays for 10 ly, not the 81 ly hop.</summary>
        private static Fleet UnderfueledFleet()
        {
            ShipDesign design = MovementCoverageKit.Design(1, 191, 100000, 0, (MovementCoverageKit.Real("Long Hump 6"), 1));
            Fleet fleet = MovementCoverageKit.Fleet(design, 1);
            fleet.FuelAvailable = 90;
            return fleet;
        }

        [Test]
        public void Move_TravelsOnlyAsFarAsTheFuelPaysFor()
        {
            Fleet fleet = UnderfueledFleet();
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(1000, 0), WarpFactor = 9, Task = new NoTask() });
            double availableTime = 1.0;

            Fleet.TravelStatus status = fleet.Move(ref availableTime, MovementCoverageKit.PlainRace());

            Assert.AreEqual(Fleet.TravelStatus.InTransit, status);
            Assert.AreEqual(10, fleet.Position.X, "A partial hop of 10 ly, not 81");
            Assert.AreEqual(0, fleet.FuelAvailable, 1e-9, "The tank is emptied, never below zero");
        }

        [Test]
        public void TheMovementStep_StopsTheFleetWhereItsFuelRunsOut()
        {
            Fleet fleet = UnderfueledFleet();
            MovementCoverageKit.OrderEast(fleet, 1000, 9);
            ServerData serverData = MovementCoverageKit.Game(MovementCoverageKit.PlainRace(), fleet, out _);

            MovementCoverageKit.RunMovement(serverData, fleet, new MovementCoverageKit.ScriptedRandom());

            Assert.AreEqual(10, fleet.Position.X);
            Assert.AreEqual(0, fleet.FuelAvailable, 1e-9);
        }
    }

    /// <summary>
    /// Rows 14 and 15, §3: No Advanced Scanners doubles the range of every conventional scanner
    /// (an Electronics-3 homeworld scanner reaches 300 ly instead of 150); several scanners on
    /// one design combine as the fourth root of the sum of their ranges to the fourth power
    /// (100 + 100 + 60 ly gives about 120 ly).
    /// </summary>
    [TestFixture]
    public class ScannerRangeCoverageTest
    {
        private static Component ScannerPart(int normal, int penetrating = 0)
        {
            Component part = new Component { Name = "Scanner " + normal + "/" + penetrating, Mass = 1 };
            part.Properties.Add("Scanner", new Scanner { NormalScan = normal, PenetratingScan = penetrating });
            return part;
        }

        [Test]
        public void NoAdvancedScanners_DoublesAShipScanner()
        {
            Component possum = MovementCoverageKit.Real("Possum Scanner");
            ShipDesign plain = MovementCoverageKit.Design(1, 20, 100, 0, (possum, 1));
            plain.Update(MovementCoverageKit.PlainRace());
            ShipDesign nas = MovementCoverageKit.Design(2, 20, 100, 0, (MovementCoverageKit.Real("Possum Scanner"), 1));
            nas.Update(MovementCoverageKit.PlainRace("NAS"));

            Assert.AreEqual(150, plain.ScanRangeNormal);
            Assert.AreEqual(300, nas.ScanRangeNormal);
        }

        /// <summary>The spec's own example: the Electronics-3 planetary scanner (Scoper 150) gives
        /// 150 ly, or 300 ly under No Advanced Scanners.</summary>
        [TestCase(false, 150)]
        [TestCase(true, 300)]
        public void NoAdvancedScanners_DoublesTheElectronics3PlanetaryScanner(bool noAdvancedScanners, int expected)
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new SimpleEmpireData();
            empire.Id = 1;
            empire.AvailableComponents = new RaceComponents();
            empire.Race = noAdvancedScanners ? MovementCoverageKit.PlainRace("NAS") : MovementCoverageKit.PlainRace();
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                empire.ResearchLevels[field] = 0;
            }

            empire.ResearchLevels[TechLevel.ResearchField.Electronics] = 2;
            serverData.AllEmpires.Add(empire.Id, empire);

            Star home = new Star { Name = "Home", Owner = empire.Id, ThisRace = empire.Race, Colonists = 10000 };
            home.ScannerType = "Viewer 90";
            home.ScanRange = 90;
            serverData.AllStars.Add(home.Key, home);
            empire.OwnedStars.Add(home);

            StarUpdateStep step = new StarUpdateStep(new Random(1));
            typeof(StarUpdateStep).GetField("serverState", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(step, serverData);
            typeof(StarUpdateStep).GetMethod("TechLevelUp", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(step, new object[] { TechLevel.ResearchField.Electronics, empire });

            Assert.AreEqual(3, empire.ResearchLevels[TechLevel.ResearchField.Electronics]);
            Assert.AreEqual("Scoper 150", home.ScannerType);
            Assert.AreEqual(expected, home.ScanRange);
        }

        /// <summary>Fourth root of (100^4 + 100^4 + 60^4) = 120.8: "about 120", more than any one
        /// scanner, far short of the 260 sum. The final rounding is not specified, so 120 or 121.</summary>
        [Test]
        public void ThreeScanners_CombineAsTheFourthRootOfTheSumOfFourthPowers()
        {
            ShipDesign design = MovementCoverageKit.Design(1, 20, 100, 0, (ScannerPart(100), 1), (ScannerPart(100), 1), (ScannerPart(60), 1));

            Assert.That(design.ScanRangeNormal, Is.InRange(120, 121));
        }

        [Test]
        public void TwoInOneSlotPlusAThird_CombineTheSameWay()
        {
            ShipDesign design = MovementCoverageKit.Design(1, 20, 100, 0, (ScannerPart(100), 2), (ScannerPart(60), 1));

            Assert.That(design.ScanRangeNormal, Is.InRange(120, 121));
        }

        [Test]
        public void PenetratingScanners_CombineTheSameWay()
        {
            ShipDesign design = MovementCoverageKit.Design(1, 20, 100, 0, (ScannerPart(100, 100), 1), (ScannerPart(100, 100), 1), (ScannerPart(60, 60), 1));

            Assert.That(design.ScanRangePenetrating, Is.InRange(120, 121));
        }

        /// <summary>Two equal scanners: 2^(1/4) x 100 = 118.9.</summary>
        [Test]
        public void TwoEqualScanners_GiveTheFourthRootOfTwoTimesTheRange()
        {
            ShipDesign design = MovementCoverageKit.Design(1, 20, 100, 0, (ScannerPart(100), 1), (ScannerPart(100), 1));

            Assert.That(design.ScanRangeNormal, Is.InRange(118, 119));
        }
    }

    /// <summary>
    /// Rows 24, 25, 27 and 28, §4: each hull's base cargo (kT), with fuel a separate pool; cargo
    /// pods +50 / +100 kT, fuel tanks +250 / +500 mg and the Anti-matter Generator +200 mg of fuel
    /// capacity, each times the quantity mounted, on top of the hull's own figures.
    /// </summary>
    [TestFixture]
    public class CargoAndFuelCapacityCoverageTest
    {
        [TestCase("Small Freighter", 70)]
        [TestCase("Medium Freighter", 210)]
        [TestCase("Large Freighter", 1200)]
        [TestCase("Super Freighter", 3000)]
        [TestCase("Privateer", 250)]
        [TestCase("Rogue", 500)]
        [TestCase("Galleon", 1000)]
        [TestCase("Meta Morph", 300)]
        [TestCase("Mini-Colony Ship", 10)]
        public void EachHull_HasItsBaseCargo(string hull, int cargo)
        {
            ShipDesign design = MovementCoverageKit.RealHullDesign(1, hull, null);

            Assert.AreEqual(cargo, design.CargoCapacity);
        }

        /// <summary>§4 cross-check of the hull table's fuel column, and the two fuel-tanker hulls
        /// (Fuel Transport 750 mg, Super-Fuel Xport 2250 mg).</summary>
        [TestCase("Small Freighter", 130)]
        [TestCase("Medium Freighter", 450)]
        [TestCase("Large Freighter", 2600)]
        [TestCase("Privateer", 650)]
        [TestCase("Rogue", 2250)]
        [TestCase("Fuel Transport", 750)]
        [TestCase("Super-Fuel Transport", 2250)]
        public void EachHull_HasItsOwnFuelTank(string hull, int fuel)
        {
            ShipDesign design = MovementCoverageKit.RealHullDesign(1, hull, null);

            Assert.AreEqual(fuel, design.FuelCapacity);
        }

        /// <summary>Fuel does not count against the hold: a Small Freighter with a full tank still
        /// loads its whole 70 kT, and loading cargo leaves the fuel alone.</summary>
        [Test]
        public void Fuel_IsASeparatePoolFromCargo()
        {
            ShipDesign design = MovementCoverageKit.RealHullDesign(1, "Small Freighter", null);
            Fleet fleet = MovementCoverageKit.Fleet(design, 1);
            Assert.AreEqual(130, fleet.FuelAvailable, "test setup: a full tank");

            EmpireData us = new SimpleEmpireData { Id = 1 };
            us.AvailableComponents = new RaceComponents();
            Star star = new Star { Name = "Home", Owner = us.Id };
            star.ResourcesOnHand.Ironium = 500;
            us.OwnedStars.Add(star);
            fleet.InOrbit = star;

            CargoTask load = new CargoTask { Mode = CargoMode.Load };
            load.Amount.Ironium = 500;
            Assert.IsTrue(load.IsValid(fleet, star, us, us));
            load.Perform(fleet, star, us, us);

            Assert.AreEqual(70, fleet.Cargo.Ironium, "The whole hold, despite the full tank");
            Assert.AreEqual(70, fleet.Cargo.Mass, "Cargo mass does not include fuel");
            Assert.AreEqual(130, fleet.FuelAvailable);
        }

        [TestCase("Cargo Pod", 1, 210 + 50)]
        [TestCase("Cargo Pod", 2, 210 + 100)]
        [TestCase("Super Cargo Pod", 1, 210 + 100)]
        [TestCase("Super Cargo Pod", 2, 210 + 200)]
        public void CargoPods_AddTheirBonusTimesQuantity_ToTheHullsCargo(string pod, int quantity, int expected)
        {
            ShipDesign design = MovementCoverageKit.RealHullDesign(1, "Medium Freighter", null, (MovementCoverageKit.Real(pod), quantity));

            Assert.AreEqual(expected, design.CargoCapacity);
            Assert.AreEqual(450, design.FuelCapacity, "A cargo pod adds no fuel");
        }

        [TestCase("Fuel Tank", 1, 130 + 250)]
        [TestCase("Fuel Tank", 2, 130 + 500)]
        [TestCase("Super Fuel Tank", 1, 130 + 500)]
        [TestCase("Super Fuel Tank", 2, 130 + 1000)]
        public void FuelTanks_AddTheirBonusTimesQuantity_ToTheHullsFuel(string tank, int quantity, int expected)
        {
            ShipDesign design = MovementCoverageKit.RealHullDesign(1, "Small Freighter", null, (MovementCoverageKit.Real(tank), quantity));

            Assert.AreEqual(expected, design.FuelCapacity);
            Assert.AreEqual(70, design.CargoCapacity, "A fuel tank adds no cargo");
        }

        /// <summary>The fuel-transport hulls' own fuel property must not displace their tank, nor
        /// double their yearly 200 mg per ship (turn-generation-engine.md step 22, row 52).</summary>
        [TestCase("Fuel Transport", 750)]
        [TestCase("Super-Fuel Transport", 2250)]
        public void FuelTransportHulls_KeepTheirTank_AndMake200mgAShipAYear(string hull, int tank)
        {
            ShipDesign design = MovementCoverageKit.RealHullDesign(1, hull, null);
            Fleet fleet = MovementCoverageKit.Fleet(design, 2);

            Assert.AreEqual(2 * tank, fleet.TotalFuelCapacity);
            Assert.AreEqual(2 * 200, fleet.PassiveFuelGeneration, 1e-9);
        }

        [TestCase(1, 130 + 200)]
        [TestCase(2, 130 + 400)]
        public void AntiMatterGenerator_Adds200mgOfFuelCapacityEach(int quantity, int expected)
        {
            ShipDesign design = MovementCoverageKit.RealHullDesign(1, "Small Freighter", null, (MovementCoverageKit.Real("Anti-matter Generator"), quantity));

            Assert.AreEqual(expected, design.FuelCapacity);
            Assert.AreEqual(70, design.CargoCapacity);
        }
    }

    /// <summary>
    /// Row 47, §4 "Fleet lifecycle": "Merge Fleets sums ship counts, cargo, and mass into the
    /// primary fleet and deletes the absorbed fleets"; Split Fleet moves ships off into a new
    /// fleet of the same owner at the same place. (Fuel-stranding below full fuel is row 48 and
    /// the post-merge scanner maximum row 49, both tested elsewhere; every merge here is at full
    /// fuel.)
    /// </summary>
    [TestFixture]
    public class SplitAndMergeCoverageTest
    {
        private ServerData serverData;
        private EmpireData empire;
        private ShipDesign smallHold;
        private ShipDesign bigHold;

        [SetUp]
        public void SetUp()
        {
            serverData = new ServerData();
            empire = new SimpleEmpireData();
            empire.Id = 1;
            empire.AvailableComponents = new RaceComponents();
            serverData.AllEmpires.Add(empire.Id, empire);

            smallHold = MovementCoverageKit.Design(((long)1 << 32) | 1, 40, 100, 10);
            bigHold = MovementCoverageKit.Design(((long)1 << 32) | 2, 60, 200, 100);
        }

        private Fleet AddFleet(uint id, params (ShipDesign design, int quantity)[] stacks)
        {
            Fleet fleet = new Fleet(((long)empire.Id << 32) | id) { Owner = empire.Id, Name = "Fleet " + id, Position = new NovaPoint(10, 10) };
            foreach ((ShipDesign design, int quantity) in stacks)
            {
                ShipToken token = new ShipToken(design, quantity);
                fleet.Composition.Add(token.Key, token);
            }

            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position, Destination = "Space", WarpFactor = 0, Task = new NoTask() });
            fleet.FuelAvailable = fleet.TotalFuelCapacity;
            empire.AddOrUpdateFleet(fleet);
            return fleet;
        }

        [Test]
        public void Merge_SumsShipsCargoFuelAndMass_AndDeletesTheAbsorbedFleet()
        {
            Fleet primary = AddFleet(1, (smallHold, 2));
            primary.Cargo.Ironium = 10;
            Fleet absorbed = AddFleet(2, (bigHold, 3));
            absorbed.Cargo.Boranium = 20;
            absorbed.Cargo.ColonistsInKilotons = 5;
            int massBefore = primary.Mass + absorbed.Mass;
            double fuelBefore = primary.FuelAvailable + absorbed.FuelAvailable;

            SplitMergeTask merge = new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), absorbed.Key);
            merge.Perform(primary, absorbed, empire, empire);
            serverData.CleanupFleets();

            Assert.AreEqual(5, MovementCoverageKit.ShipCount(primary));
            Assert.AreEqual(2, primary.Composition[smallHold.Key].Quantity);
            Assert.AreEqual(3, primary.Composition[bigHold.Key].Quantity);
            Assert.AreEqual(10, primary.Cargo.Ironium);
            Assert.AreEqual(20, primary.Cargo.Boranium);
            Assert.AreEqual(5, primary.Cargo.ColonistsInKilotons);
            Assert.AreEqual(fuelBefore, primary.FuelAvailable, 1e-9);
            Assert.AreEqual(massBefore, primary.Mass);
            Assert.IsFalse(empire.OwnedFleets.ContainsKey(absorbed.Key), "The absorbed fleet is deleted");
            Assert.IsTrue(empire.OwnedFleets.ContainsKey(primary.Key));
        }

        [Test]
        public void Merge_OfTheSameDesign_SumsTheShipCountIntoOneStack()
        {
            Fleet primary = AddFleet(1, (smallHold, 2));
            Fleet absorbed = AddFleet(2, (smallHold, 3));

            new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), absorbed.Key)
                .Perform(primary, absorbed, empire, empire);
            serverData.CleanupFleets();

            Assert.AreEqual(1, primary.Composition.Count);
            Assert.AreEqual(5, primary.Composition[smallHold.Key].Quantity);
            Assert.IsFalse(empire.OwnedFleets.ContainsKey(absorbed.Key));
        }

        /// <summary>A split moves the chosen ships into a new fleet of the same owner at the same
        /// place; ships, cargo and fuel are conserved and each fleet's cargo fits its hold.</summary>
        [Test]
        public void Split_MovesTheChosenShipsIntoANewFleet_ConservingCargoAndFuel()
        {
            Fleet source = AddFleet(1, (smallHold, 2), (bigHold, 3));
            source.Cargo.Ironium = 200;
            double fuelBefore = source.FuelAvailable;

            // The split dialog's two sides: everything that stays (the big holds at 0) and what goes.
            Dictionary<long, ShipToken> left = new Dictionary<long, ShipToken>
            {
                { smallHold.Key, new ShipToken(smallHold, 2) },
                { bigHold.Key, new ShipToken(bigHold, 0) },
            };
            Dictionary<long, ShipToken> right = new Dictionary<long, ShipToken> { { bigHold.Key, new ShipToken(bigHold, 3) } };
            new SplitMergeTask(left, right).Perform(source, null, empire, empire);
            serverData.CleanupFleets();

            Fleet split = empire.OwnedFleets.Values.Single(f => f.Key != source.Key);
            Assert.AreEqual(empire.Id, split.Owner);
            Assert.AreEqual(source.Position, split.Position);
            Assert.AreEqual(2, MovementCoverageKit.ShipCount(source));
            Assert.AreEqual(3, split.Composition[bigHold.Key].Quantity);
            Assert.AreEqual(200, source.Cargo.Ironium + split.Cargo.Ironium, "Cargo is conserved");
            Assert.LessOrEqual(source.Cargo.Mass, source.TotalCargoCapacity);
            Assert.LessOrEqual(split.Cargo.Mass, split.TotalCargoCapacity);
            Assert.AreEqual(fuelBefore, source.FuelAvailable + split.FuelAvailable, 1e-9, "Fuel is conserved");
            Assert.LessOrEqual(source.FuelAvailable, source.TotalFuelCapacity);
        }
    }

    /// <summary>
    /// Row 62, §4 "Colonist unload outcomes, complete table" (Unload task column) plus
    /// turn-generation-engine.md §11's landing rule for an owned planet: the attackers' strength is
    /// 110% of the landed colonists (165% War Monger) against the defenders' population, doubled
    /// for Inner Strength; the attack wins when its strength is at least the defence. Relations are
    /// never consulted. Ties (strength exactly equal) are not exercised: see the report.
    /// </summary>
    [TestFixture]
    public class InvasionStrengthCoverageTest
    {
        private EmpireData us;
        private EmpireData them;
        private Fleet fleet;

        [SetUp]
        public void Init()
        {
            us = new EmpireData { Id = 1, Race = new Race() };
            them = new EmpireData { Id = 2, Race = new Race() };
            us.Race.Traits.SetPrimary("JOAT");
            them.Race.Traits.SetPrimary("JOAT");

            fleet = new Fleet(1) { Owner = us.Id, Name = "Troopship" };
            ShipToken freighter = new ShipToken(CargoTestKit.Design(1, 200, 0), 1);
            fleet.Composition.Add(freighter.Key, freighter);
            fleet.Cargo.ColonistsInKilotons = 50; // 5,000 colonists
        }

        private void SetRelations(PlayerRelation relation)
        {
            us.EmpireReports.Add(them.Id, new EmpireIntel(them) { Relation = relation });
            them.EmpireReports.Add(us.Id, new EmpireIntel(us) { Relation = relation });
        }

        private Star TheirPlanet(int colonists)
        {
            Star star = new Star { Name = "Target", Owner = them.Id, Colonists = colonists };
            them.OwnedStars.Add(star);
            fleet.InOrbit = star;
            return star;
        }

        private void UnloadAllColonists(Star star)
        {
            CargoTask task = new CargoTask { Mode = CargoMode.Unload };
            task.Amount.ColonistsInKilotons = 50;
            if (task.IsValid(fleet, star, us, them))
            {
                task.Perform(fleet, star, us, them);
            }
        }

        /// <summary>5,000 troops attack at 5,500.</summary>
        [TestCase(5499, true)]
        [TestCase(5501, false)]
        public void OrdinaryAttackers_StrikeAt110Percent(int defenders, bool captured)
        {
            SetRelations(PlayerRelation.Enemy);
            Star star = TheirPlanet(defenders);

            UnloadAllColonists(star);

            Assert.AreEqual(captured ? us.Id : them.Id, star.Owner);
            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons, "The troops landed either way");
        }

        /// <summary>A War Monger's 5,000 troops attack at 8,250.</summary>
        [TestCase(8249, true)]
        [TestCase(8251, false)]
        public void WarMongerAttackers_StrikeAt165Percent(int defenders, bool captured)
        {
            us.Race.Traits.SetPrimary("WM");
            SetRelations(PlayerRelation.Enemy);
            Star star = TheirPlanet(defenders);

            UnloadAllColonists(star);

            Assert.AreEqual(captured ? us.Id : them.Id, star.Owner);
        }

        /// <summary>Inner Strength defenders count double: 2,749 defend at 5,498 (falls to 5,500),
        /// 2,751 at 5,502 (holds).</summary>
        [TestCase(2749, true)]
        [TestCase(2751, false)]
        public void InnerStrengthDefenders_CountDouble(int defenders, bool captured)
        {
            them.Race.Traits.SetPrimary("IS");
            SetRelations(PlayerRelation.Enemy);
            Star star = TheirPlanet(defenders);

            UnloadAllColonists(star);

            Assert.AreEqual(captured ? us.Id : them.Id, star.Owner);
        }

        [TestCase(PlayerRelation.Enemy)]
        [TestCase(PlayerRelation.Neutral)]
        [TestCase(PlayerRelation.Friend)]
        public void Relations_AreNeverConsulted(PlayerRelation relation)
        {
            SetRelations(relation);
            Star star = TheirPlanet(1000);

            UnloadAllColonists(star);

            Assert.AreEqual(us.Id, star.Owner, "Unloading onto any other race's planet is an invasion");
        }

        /// <summary>Alternate Reality, own planet: "Same" as the other races, an ordinary transfer.</summary>
        [Test]
        public void AlternateReality_OntoItsOwnPlanet_IsAnOrdinaryTransfer()
        {
            us.Race.Traits.SetPrimary("AR");
            Star star = new Star { Name = "Ours", Owner = us.Id, Colonists = 1000 };
            us.OwnedStars.Add(star);
            fleet.InOrbit = star;

            CargoTask task = new CargoTask { Mode = CargoMode.Unload };
            task.Amount.ColonistsInKilotons = 50;
            Assert.IsTrue(task.IsValid(fleet, star, us, us));
            task.Perform(fleet, star, us, us);

            Assert.AreEqual(6000, star.Colonists);
            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons);
        }

        /// <summary>Alternate Reality onto an unowned planet: refused (message 85), colonists stay aboard.</summary>
        [Test]
        public void AlternateReality_OntoAnUnownedPlanet_IsRefused_ColonistsStayAboard()
        {
            us.Race.Traits.SetPrimary("AR");
            Star star = new Star { Name = "Empty", Owner = Global.Nobody };
            fleet.InOrbit = star;

            CargoTask task = new CargoTask { Mode = CargoMode.Unload };
            task.Amount.ColonistsInKilotons = 50;
            if (task.IsValid(fleet, star, us, null))
            {
                task.Perform(fleet, star, us, null);
            }

            Assert.AreEqual(Global.Nobody, star.Owner);
            Assert.AreEqual(0, star.Colonists);
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons);
        }

        /// <summary>Every refusal tells the fleet's owner (messages 85, 86, 309).</summary>
        [TestCase("unowned")]
        [TestCase("AR")]
        [TestCase("starbase")]
        public void ARefusal_IsReportedToTheFleetsOwner(string situation)
        {
            Star star;
            EmpireData receiver = them;
            if (situation == "unowned")
            {
                star = new Star { Name = "Empty", Owner = Global.Nobody };
                fleet.InOrbit = star;
                receiver = null;
            }
            else
            {
                star = TheirPlanet(1000);
                if (situation == "AR")
                {
                    us.Race.Traits.SetPrimary("AR");
                }
                else
                {
                    star.Starbase = new Fleet(99);
                }
            }

            CargoTask task = new CargoTask { Mode = CargoMode.Unload };
            task.Amount.ColonistsInKilotons = 50;
            if (task.IsValid(fleet, star, us, receiver))
            {
                task.Perform(fleet, star, us, receiver);
            }

            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons, "The colonists stay aboard");
            Assert.IsTrue(task.Messages.Any(m => m.Audience == us.Id && m.Text.Contains(star.Name)), "The owner is told");
        }
    }
}
