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
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    using NUnit.Framework;

    /// <summary>
    /// Shared fixtures for the Transport-instruction, theft and Transfer Fleet tests.
    /// </summary>
    public static class CargoTestKit
    {
        public const ushort Us = 1;
        public const ushort Them = 2;

        public static EmpireData Empire(ushort id)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Id = id;
            empire.AvailableComponents = new RaceComponents();
            empire.Race.PluralName = "Race" + id;
            return empire;
        }

        /// <summary>A design on a hull with the given cargo hold and fuel tank, plus parts.</summary>
        public static ShipDesign Design(long key, int cargo, int fuel, params Component[] parts)
        {
            Component blueprint = new Component { Name = "Test Hull", Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), BaseCargo = cargo, FuelCapacity = fuel, ArmorStrength = 20 };
            foreach (Component part in parts)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = 1 });
            }

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Name = "Design" + key, Blueprint = blueprint };
            design.Update();
            return design;
        }

        public static Component Named(string name)
        {
            return new Component { Name = name };
        }

        public static Fleet Fleet(ushort owner, uint id, ShipDesign design, int x = 0, int y = 0, int quantity = 1)
        {
            Fleet fleet = new Fleet(((long)owner << 32) | id);
            fleet.Name = "Fleet " + owner + "-" + id;
            fleet.Position = new NovaPoint(x, y);
            ShipToken token = new ShipToken(design, quantity);
            fleet.Composition.Add(token.Key, token);
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position, Destination = "Space at " + fleet.Position, WarpFactor = 0 });
            return fleet;
        }

        public static Star Star(ushort owner, int ironium = 0, int boranium = 0, int germanium = 0, int colonists = 0)
        {
            Star star = new Star { Name = "Planet" + owner, Position = new NovaPoint(0, 0) };
            star.Owner = owner;
            star.ResourcesOnHand = new Resources(ironium, boranium, germanium, 0);
            star.Colonists = colonists;
            return star;
        }

        public static CargoTask Transport(params (CargoSlot slot, CargoAction action, int amount)[] orders)
        {
            CargoInstruction[] instructions = new CargoInstruction[TransportHandler.SlotCount];
            foreach ((CargoSlot slot, CargoAction action, int amount) in orders)
            {
                instructions[(int)slot] = new CargoInstruction(action, amount);
            }

            return new CargoTask(instructions);
        }

        /// <summary>Runs the task exactly as an arrival does: IsValid, then Perform.</summary>
        public static bool Run(CargoTask task, Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver = null)
        {
            if (target is Star && fleet.InOrbit == null)
            {
                fleet.InOrbit = target;
            }

            bool valid = task.IsValid(fleet, target, sender, receiver);
            if (valid)
            {
                task.Perform(fleet, target, sender, receiver);
            }

            return valid;
        }
    }

    /// <summary>
    /// The Transport task's per-slot instructions (behavior-specs-10/fleet-movement-scanning-cargo.md
    /// §4, "Conditional transfers, traced in full"): Set waypoint to N = source - N, Set amount to N
    /// = N - carried, Fill/Wait to N% = floor(N% x capacity) - carried, loads cut to free space and
    /// source holdings, Load Dunnage and Load Optimal (code 7), completion and strike-off.
    /// </summary>
    [TestFixture]
    public class TransportInstructionTest
    {
        private EmpireData us;

        [SetUp]
        public void SetUp()
        {
            us = CargoTestKit.Empire(CargoTestKit.Us);
        }

        private Fleet Freighter(int cargo = 100, int fuel = 500)
        {
            return CargoTestKit.Fleet(CargoTestKit.Us, 1, CargoTestKit.Design(1, cargo, fuel));
        }

        [Test]
        public void SetWaypointTo_LoadsTheSurplusAboveN_LeavingNAtTheSource()
        {
            Fleet fleet = Freighter();
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 150);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.SetWaypointTo, 100));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(50, fleet.Cargo.Ironium);
            Assert.AreEqual(100, star.ResourcesOnHand.Ironium);
            Assert.IsFalse(task.Pending);
        }

        [Test]
        public void SetWaypointTo_UnloadsTheDeficit_ButNeverMoreThanTheFleetCarries()
        {
            Fleet fleet = Freighter();
            fleet.Cargo.Ironium = 30;
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 40);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.SetWaypointTo, 100));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(0, fleet.Cargo.Ironium, "Deficit 60, but only 30 aboard");
            Assert.AreEqual(70, star.ResourcesOnHand.Ironium);
        }

        [Test]
        public void SetWaypointTo_PartialDeficit_UnloadsExactlyTheDeficit()
        {
            Fleet fleet = Freighter();
            fleet.Cargo.Boranium = 80;
            Star star = CargoTestKit.Star(CargoTestKit.Us, boranium: 70);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Boranium, CargoAction.SetWaypointTo, 100));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(50, fleet.Cargo.Boranium);
            Assert.AreEqual(100, star.ResourcesOnHand.Boranium);
        }

        [Test]
        public void SetAmountTo_LoadsTheShortfall_AndUnloadsAnExcess()
        {
            Fleet loading = Freighter();
            loading.Cargo.Germanium = 10;
            Star star = CargoTestKit.Star(CargoTestKit.Us, germanium: 100);
            CargoTask load = CargoTestKit.Transport((CargoSlot.Germanium, CargoAction.SetAmountTo, 60));
            CargoTestKit.Run(load, loading, star, us);
            Assert.AreEqual(60, loading.Cargo.Germanium);
            Assert.AreEqual(50, star.ResourcesOnHand.Germanium);
            Assert.IsFalse(load.Pending);

            Fleet unloading = Freighter();
            unloading.Cargo.Germanium = 80;
            CargoTask unload = CargoTestKit.Transport((CargoSlot.Germanium, CargoAction.SetAmountTo, 60));
            CargoTestKit.Run(unload, unloading, star, us);
            Assert.AreEqual(60, unloading.Cargo.Germanium);
            Assert.AreEqual(70, star.ResourcesOnHand.Germanium);
        }

        [Test]
        public void SetAmountTo_WhenTheSourceIsShort_LoadsWhatThereIsAndStaysPending()
        {
            Fleet fleet = Freighter();
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 20);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.SetAmountTo, 60));

            CargoTestKit.Run(task, fleet, star, us);
            Assert.AreEqual(20, fleet.Cargo.Ironium);
            Assert.IsTrue(task.Pending, "A Set amount to N that did not find enough at the source is not complete");

            star.ResourcesOnHand.Ironium = 100;
            CargoTestKit.Run(task, fleet, star, us);
            Assert.AreEqual(60, fleet.Cargo.Ironium);
            Assert.IsFalse(task.Pending);
        }

        [Test]
        public void FillToPercent_IsFloorOfNPercentOfTheWholeHold_MinusWhatIsCarried()
        {
            // 70 kT hold (a Small Freighter), 33%: floor(23.1) = 23; 5 already aboard -> load 18.
            Fleet fleet = Freighter(cargo: 70);
            fleet.Cargo.Ironium = 5;
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 100);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.FillToPercent, 33));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(23, fleet.Cargo.Ironium);
            Assert.AreEqual(82, star.ResourcesOnHand.Ironium);
        }

        [Test]
        public void FillToPercent_CompletesEvenWhenShort_WaitForPercent_StaysUntilReached()
        {
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 30, boranium: 30);

            Fleet filler = Freighter();
            CargoTask fill = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.FillToPercent, 50));
            CargoTestKit.Run(fill, filler, star, us);
            Assert.AreEqual(30, filler.Cargo.Ironium);
            Assert.IsFalse(fill.Pending);

            Fleet waiter = Freighter();
            CargoTask wait = CargoTestKit.Transport((CargoSlot.Boranium, CargoAction.WaitForPercent, 50));
            CargoTestKit.Run(wait, waiter, star, us);
            Assert.AreEqual(30, waiter.Cargo.Boranium);
            Assert.IsTrue(wait.Pending, "Wait for 50% (50 kT) has only 30");

            star.ResourcesOnHand.Boranium = 40;
            CargoTestKit.Run(wait, waiter, star, us);
            Assert.AreEqual(50, waiter.Cargo.Boranium);
            Assert.AreEqual(20, star.ResourcesOnHand.Boranium);
            Assert.IsFalse(wait.Pending);
        }

        [Test]
        public void WaitForPercent_IsSatisfiedByAFullHold()
        {
            Fleet fleet = Freighter();
            fleet.Cargo.Ironium = 100;
            Star star = CargoTestKit.Star(CargoTestKit.Us);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Boranium, CargoAction.WaitForPercent, 50));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.IsFalse(task.Pending, "Wait for N% also ends when the hold is full");
        }

        [Test]
        public void Loads_ShareTheHold_InSlotOrder_AndNeverTakeTheSourceBelowZero()
        {
            Fleet fleet = Freighter();
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 80, boranium: 80, germanium: 5);
            CargoTask task = CargoTestKit.Transport(
                (CargoSlot.Ironium, CargoAction.LoadAll, 0),
                (CargoSlot.Boranium, CargoAction.LoadAll, 0));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(80, fleet.Cargo.Ironium, "Ironium first");
            Assert.AreEqual(20, fleet.Cargo.Boranium, "Boranium gets what space is left");
            Assert.AreEqual(60, star.ResourcesOnHand.Boranium);

            Fleet second = Freighter();
            CargoTask exactly = CargoTestKit.Transport((CargoSlot.Germanium, CargoAction.LoadExactly, 50));
            CargoTestKit.Run(exactly, second, star, us);
            Assert.AreEqual(5, second.Cargo.Germanium, "Load exactly N that finds less loads what there is");
            Assert.AreEqual(0, star.ResourcesOnHand.Germanium);
            Assert.IsFalse(exactly.Pending, "and counts as done");
        }

        [Test]
        public void UnloadInstructions_ActFirst_AndAreStruckOffWhileTheTaskWaits()
        {
            Fleet fleet = Freighter();
            fleet.Cargo.Ironium = 40;
            Star star = CargoTestKit.Star(CargoTestKit.Us, boranium: 10);
            CargoTask task = CargoTestKit.Transport(
                (CargoSlot.Ironium, CargoAction.UnloadAll, 0),
                (CargoSlot.Boranium, CargoAction.WaitForPercent, 50));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(0, fleet.Cargo.Ironium);
            Assert.AreEqual(40, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(10, fleet.Cargo.Boranium);
            Assert.IsTrue(task.Pending);
            Assert.AreEqual(CargoAction.None, task.Instructions[(int)CargoSlot.Ironium].Action, "The carried-out unload is struck off");
            Assert.AreEqual(CargoAction.WaitForPercent, task.Instructions[(int)CargoSlot.Boranium].Action);
        }

        [Test]
        public void LoadDunnage_FillsTheRemainingSpace_OnlyAfterEverythingElse()
        {
            Fleet fleet = Freighter();
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 200, boranium: 200);
            CargoTask task = CargoTestKit.Transport(
                (CargoSlot.Ironium, CargoAction.LoadDunnageOrOptimal, 0),
                (CargoSlot.Boranium, CargoAction.LoadExactly, 30));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(30, fleet.Cargo.Boranium, "The ordinary load is served first");
            Assert.AreEqual(70, fleet.Cargo.Ironium, "Dunnage fills the space left");
        }

        [Test]
        public void LoadDunnage_DoesNothingWhileAnotherConditionIsUnmet()
        {
            Fleet fleet = Freighter();
            Star star = CargoTestKit.Star(CargoTestKit.Us, ironium: 200, boranium: 10);
            CargoTask task = CargoTestKit.Transport(
                (CargoSlot.Ironium, CargoAction.LoadDunnageOrOptimal, 0),
                (CargoSlot.Boranium, CargoAction.WaitForPercent, 50));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(0, fleet.Cargo.Ironium);
            Assert.IsTrue(task.Pending);
        }

        [Test]
        public void Colonists_MoveOneToOne_InHundreds()
        {
            Fleet fleet = Freighter();
            Star star = CargoTestKit.Star(CargoTestKit.Us, colonists: 10000);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Colonists, CargoAction.LoadExactly, 25));

            CargoTestKit.Run(task, fleet, star, us);

            Assert.AreEqual(25, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(7500, star.Colonists);
        }

        [Test]
        public void Fuel_UsesTheTank_AndPlanetsHaveNoFuelSlot()
        {
            Fleet fleet = Freighter(cargo: 100, fuel: 500);
            fleet.FuelAvailable = 100;
            Star star = CargoTestKit.Star(CargoTestKit.Us);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Fuel, CargoAction.UnloadAll, 0));

            CargoTestKit.Run(task, fleet, star, us);
            Assert.AreEqual(100, fleet.FuelAvailable, "Fuel cannot go onto a planet");

            Fleet tanker = CargoTestKit.Fleet(CargoTestKit.Us, 2, CargoTestKit.Design(2, 0, 1000));
            tanker.FuelAvailable = 900;
            CargoTask fill = CargoTestKit.Transport((CargoSlot.Fuel, CargoAction.FillToPercent, 50));
            CargoTestKit.Run(fill, fleet, tanker, us);
            Assert.AreEqual(250, fleet.FuelAvailable, "Fill to 50% of a 500 mg tank");
            Assert.AreEqual(750, tanker.FuelAvailable);
        }

        [Test]
        public void UnloadIntoAnotherFleet_IsCutToItsFreeSpace()
        {
            Fleet fleet = Freighter();
            fleet.Cargo.Ironium = 90;
            Fleet small = CargoTestKit.Fleet(CargoTestKit.Us, 2, CargoTestKit.Design(2, 50, 100));
            small.Cargo.Boranium = 20;
            CargoTask task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.UnloadAll, 0));

            CargoTestKit.Run(task, fleet, small, us);

            Assert.AreEqual(30, small.Cargo.Ironium);
            Assert.AreEqual(60, fleet.Cargo.Ironium);
        }

        [Test]
        public void LoadOptimal_KeepsTheRouteFuel_AndHandsTheExcessToTheTargetFleet()
        {
            Engine engine = new Engine();
            for (int i = 0; i < engine.FuelConsumption.Length; i++)
            {
                engine.FuelConsumption[i] = 100;
            }

            Component engineComponent = new Component { Name = "Test Engine" };
            engineComponent.Properties.Add("Engine", engine);
            ShipDesign design = CargoTestKit.Design(1, 0, 1000, engineComponent);
            Fleet fleet = CargoTestKit.Fleet(CargoTestKit.Us, 1, design);
            fleet.FuelAvailable = 300;
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(100, 0), WarpFactor = 5, Destination = "Space at 100,0" });

            Fleet receiving = CargoTestKit.Fleet(CargoTestKit.Us, 2, CargoTestKit.Design(2, 0, 1000));
            receiving.FuelAvailable = 0;

            CargoTask task = CargoTestKit.Transport((CargoSlot.Fuel, CargoAction.LoadDunnageOrOptimal, 0));
            CargoTestKit.Run(task, fleet, receiving, us);

            // 1 mg moves 200 kT 1 ly at a fuel-table value of 100: mass x 100 ly / 200.
            int need = (int)System.Math.Ceiling(design.Mass * 100 / 200.0);
            Assert.AreEqual(need, fleet.FuelAvailable, 0.001);
            Assert.AreEqual(300 - need, receiving.FuelAvailable, 0.001);
        }

        [Test]
        public void LoadOptimal_WithNoFurtherWaypoint_HandsOverAllFuel_AndNeverTakesFuelIn()
        {
            Fleet fleet = CargoTestKit.Fleet(CargoTestKit.Us, 1, CargoTestKit.Design(1, 0, 1000));
            fleet.FuelAvailable = 120;
            Fleet receiving = CargoTestKit.Fleet(CargoTestKit.Us, 2, CargoTestKit.Design(2, 0, 1000));
            receiving.FuelAvailable = 500;

            CargoTask task = CargoTestKit.Transport((CargoSlot.Fuel, CargoAction.LoadDunnageOrOptimal, 0));
            CargoTestKit.Run(task, fleet, receiving, us);

            Assert.AreEqual(0, fleet.FuelAvailable, 0.001);
            Assert.AreEqual(620, receiving.FuelAvailable, 0.001);
        }

        [Test]
        public void ForeignPlanetLoad_IsBlocked_TheOwnerIsTold_AndTheTaskEnds()
        {
            Fleet fleet = Freighter();
            Star theirs = CargoTestKit.Star(CargoTestKit.Them, ironium: 100);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.LoadAll, 0));

            CargoTestKit.Run(task, fleet, theirs, us);

            Assert.AreEqual(0, fleet.Cargo.Ironium);
            Assert.AreEqual(100, theirs.ResourcesOnHand.Ironium);
            Assert.IsFalse(task.Pending);
            Assert.IsTrue(task.Messages.Any(m => m.Text.Contains("another race")));
        }

        [Test]
        public void Instructions_RoundTripThroughTheWaypointXml()
        {
            Waypoint waypoint = new Waypoint { Position = new NovaPoint(1, 2), Destination = "X", WarpFactor = 5 };
            waypoint.Task = CargoTestKit.Transport(
                (CargoSlot.Ironium, CargoAction.SetWaypointTo, 300),
                (CargoSlot.Fuel, CargoAction.LoadDunnageOrOptimal, 0),
                (CargoSlot.Colonists, CargoAction.WaitForPercent, 75));

            XmlDocument xmldoc = new XmlDocument();
            Waypoint loaded = new Waypoint(waypoint.ToXml(xmldoc));

            CargoTask task = loaded.Task as CargoTask;
            Assert.IsNotNull(task);
            Assert.IsNotNull(task.Instructions);
            Assert.AreEqual(CargoAction.SetWaypointTo, task.Instructions[(int)CargoSlot.Ironium].Action);
            Assert.AreEqual(300, task.Instructions[(int)CargoSlot.Ironium].Amount);
            Assert.AreEqual(CargoAction.WaitForPercent, task.Instructions[(int)CargoSlot.Colonists].Action);
            Assert.AreEqual(75, task.Instructions[(int)CargoSlot.Colonists].Amount);
            Assert.AreEqual(CargoAction.LoadDunnageOrOptimal, task.Instructions[(int)CargoSlot.Fuel].Action);
            Assert.AreEqual(CargoAction.None, task.Instructions[(int)CargoSlot.Boranium].Action);

            CargoTask clone = Waypoint.CloneTask(task) as CargoTask;
            Assert.AreNotSame(task.Instructions[0], clone.Instructions[0]);
            Assert.AreEqual(300, clone.Instructions[0].Amount);
        }

        [Test]
        public void Amounts_AreTwelveBit()
        {
            Assert.AreEqual(4095, new CargoInstruction(CargoAction.LoadExactly, 10000).Amount);
        }
    }

    /// <summary>
    /// Cargo theft (behavior-specs-10/fleet-movement-scanning-cargo.md §4, "Caps on a load" and
    /// "Theft"): taking cargo from another race's planet or fleet works only with the theft scanner
    /// ability. Per the in-game help, the Pick Pocket Scanner steals fleet cargo and the Robber
    /// Baron Scanner also a planet's surface minerals; colonists are never taken.
    /// </summary>
    [TestFixture]
    public class CargoTheftTest
    {
        private EmpireData us;
        private EmpireData them;

        [SetUp]
        public void SetUp()
        {
            us = CargoTestKit.Empire(CargoTestKit.Us);
            them = CargoTestKit.Empire(CargoTestKit.Them);
        }

        private static Fleet Thief(string scanner)
        {
            Component[] parts = scanner == null ? new Component[0] : new[] { CargoTestKit.Named(scanner) };
            return CargoTestKit.Fleet(CargoTestKit.Us, 1, CargoTestKit.Design(1, 100, 500, parts));
        }

        private static Fleet Victim()
        {
            Fleet victim = CargoTestKit.Fleet(CargoTestKit.Them, 7, CargoTestKit.Design(7, 100, 500));
            victim.Cargo.Ironium = 60;
            victim.Cargo.ColonistsInKilotons = 20;
            victim.FuelAvailable = 300;
            return victim;
        }

        [Test]
        public void RobberBaron_StealsAForeignPlanetsSurfaceMinerals_ButNotItsColonists()
        {
            Fleet fleet = Thief("Robber Barron Scanner"); // components.xml's spelling
            Star theirs = CargoTestKit.Star(CargoTestKit.Them, ironium: 70, colonists: 5000);
            CargoTask task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.LoadAll, 0));

            CargoTestKit.Run(task, fleet, theirs, us, them);
            Assert.AreEqual(70, fleet.Cargo.Ironium);
            Assert.AreEqual(0, theirs.ResourcesOnHand.Ironium);

            CargoTask colonists = CargoTestKit.Transport((CargoSlot.Colonists, CargoAction.LoadAll, 0));
            CargoTestKit.Run(colonists, fleet, theirs, us, them);
            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(5000, theirs.Colonists);
        }

        [Test]
        public void PickPocket_StealsFromAForeignFleet_ButNotFromAForeignPlanet()
        {
            Fleet fleet = Thief(Fleet.PickPocketScannerName);
            Fleet victim = Victim();

            CargoTask task = CargoTestKit.Transport(
                (CargoSlot.Ironium, CargoAction.LoadAll, 0),
                (CargoSlot.Fuel, CargoAction.LoadExactly, 100));
            CargoTestKit.Run(task, fleet, victim, us, them);
            Assert.AreEqual(60, fleet.Cargo.Ironium);
            Assert.AreEqual(0, victim.Cargo.Ironium);
            Assert.AreEqual(200, victim.FuelAvailable, 0.001);

            Star theirs = CargoTestKit.Star(CargoTestKit.Them, boranium: 50);
            CargoTask planet = CargoTestKit.Transport((CargoSlot.Boranium, CargoAction.LoadAll, 0));
            CargoTestKit.Run(planet, fleet, theirs, us, them);
            Assert.AreEqual(0, fleet.Cargo.Boranium);
            Assert.AreEqual(50, theirs.ResourcesOnHand.Boranium);
        }

        [Test]
        public void WithoutATheftScanner_AForeignFleetLoadIsBlocked()
        {
            Fleet fleet = Thief(null);
            Fleet victim = Victim();

            CargoTask task = CargoTestKit.Transport((CargoSlot.Ironium, CargoAction.LoadAll, 0));
            CargoTestKit.Run(task, fleet, victim, us, them);

            Assert.AreEqual(0, fleet.Cargo.Ironium);
            Assert.AreEqual(60, victim.Cargo.Ironium);
        }

        [Test]
        public void TheAbilityIsAnyShipInTheFleet()
        {
            Fleet fleet = Thief(null);
            ShipDesign scout = CargoTestKit.Design(9, 0, 100, CargoTestKit.Named(Fleet.PickPocketScannerName));
            ShipToken token = new ShipToken(scout, 1);
            fleet.Composition.Add(token.Key, token);

            Assert.IsTrue(fleet.CanStealFromFleets);
            Assert.IsFalse(fleet.CanStealFromPlanets);
        }

        [Test]
        public void LegacyLoadOrder_AtAForeignPlanet_IsAllowedWithARobberBaron()
        {
            Fleet fleet = Thief("Robber Barron Scanner");
            fleet.InOrbit = CargoTestKit.Star(CargoTestKit.Them, ironium: 10);
            CargoTask task = new CargoTask { Mode = CargoMode.Load };
            task.Amount.Ironium = 10;

            Assert.IsTrue(task.IsValid(fleet, fleet.InOrbit, us, them));

            Fleet honest = Thief(null);
            honest.InOrbit = fleet.InOrbit;
            Assert.IsFalse(new CargoTask { Mode = CargoMode.Load }.IsValid(honest, honest.InOrbit, us, them));
        }
    }
}
