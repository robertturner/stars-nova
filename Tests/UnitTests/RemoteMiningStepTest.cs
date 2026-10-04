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
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    [TestFixture]
    public class RemoteMiningStepTest
    {
        private static Fleet MakeMiningFleet(long key, ushort owner, int mineEquivalents, int cargoCapacity)
        {
            Fleet fleet = new Fleet(key);
            fleet.Owner = owner;

            ShipDesign shipDesign = new ShipDesign(key);
            shipDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.BaseCargo = cargoCapacity;
            hull.Modules = new List<HullModule>();

            HullModule miningModule = new HullModule();
            Component miningComponent = new Component();
            miningComponent.Properties.Add("Mining Robot", new IntegerProperty(mineEquivalents));
            miningModule.AllocatedComponent = miningComponent;
            hull.Modules.Add(miningModule);

            shipDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken shipToken = new ShipToken(shipDesign, 1);
            fleet.Composition.Add(shipToken.Key, shipToken);

            // Fleet.Speed indexes Waypoints[0] unconditionally (via AddOrUpdateFleet's own
            // GenerateReport call) - a holding "stay here" waypoint keeps that from throwing.
            Waypoint waypoint = new Waypoint();
            waypoint.Task = new NoTask();
            fleet.Waypoints.Add(waypoint);

            return fleet;
        }

        [Test]
        public void Process_MinesAtAnUnownedStar_DepositingIntoFleetCargo()
        {
            // Remote mining is the whole reason to send a fleet to a planet nobody has
            // colonized - StarUpdateStep.UpdateMinerals (the planet's own mines) explicitly
            // skips any star with no owner/no colonists, so this needs its own turn step.
            ServerData serverData = new ServerData();

            Star star = new Star();
            star.Name = "Deepspace Rock";
            star.Owner = Global.Nobody;
            star.MineralConcentration = new Resources(50, 50, 50, 0);
            star.MineralMiningProgress = new Resources();
            serverData.AllStars.Add(star.Key, star);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = MakeMiningFleet(1, 1, mineEquivalents: 100, cargoCapacity: 1000);
            fleet.InOrbit = star;
            empire.AddOrUpdateFleet(fleet);

            new RemoteMiningStep().Process(serverData);

            // mineEquivalents(100) * concentration(50) / 100 = 50 kT/mineral, well under both the
            // 250 kT needed for concentration 50 to drop a point and the fleet's 1000 kT hold.
            Assert.AreEqual(50, fleet.Cargo.Ironium);
            Assert.AreEqual(50, fleet.Cargo.Boranium);
            Assert.AreEqual(50, fleet.Cargo.Germanium);
            Assert.AreEqual(50, star.MineralConcentration.Ironium, "50 kT isn't enough to drop concentration 50 by a point yet");
            Assert.AreEqual(50, star.MineralMiningProgress.Ironium, "the partial progress should still be carried");
        }

        [Test]
        public void Process_StopsLoadingCargoOnceTheHoldIsFull_ButStillDepletesConcentration()
        {
            ServerData serverData = new ServerData();

            Star star = new Star();
            star.Name = "Deepspace Rock";
            star.MineralConcentration = new Resources(50, 50, 50, 0);
            star.MineralMiningProgress = new Resources();
            serverData.AllStars.Add(star.Key, star);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            // Only 10 kT of hold space, but the fleet mines 50 kT of Ironium alone this turn.
            Fleet fleet = MakeMiningFleet(1, 1, mineEquivalents: 100, cargoCapacity: 10);
            fleet.InOrbit = star;
            empire.AddOrUpdateFleet(fleet);

            new RemoteMiningStep().Process(serverData);

            Assert.AreEqual(10, fleet.Cargo.Ironium, "should load only as much Ironium as fits");
            Assert.AreEqual(0, fleet.Cargo.Boranium, "no room left for Boranium once Ironium filled the hold");
            Assert.AreEqual(0, fleet.Cargo.Germanium);

            // The planet doesn't care whether the fleet could carry the ore away - it still lost
            // it to the mining operation, matching how a real ship simply can't hold more.
            Assert.AreEqual(50, star.MineralMiningProgress.Ironium);
        }

        [Test]
        public void Process_ANonMiningFleetInOrbit_IsIgnored()
        {
            ServerData serverData = new ServerData();

            Star star = new Star();
            star.Name = "Deepspace Rock";
            star.MineralConcentration = new Resources(50, 50, 50, 0);
            star.MineralMiningProgress = new Resources();
            serverData.AllStars.Add(star.Key, star);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            Fleet scout = new Fleet(1);
            scout.Owner = 1;
            ShipDesign scoutDesign = new ShipDesign(1);
            scoutDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            scoutDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken scoutToken = new ShipToken(scoutDesign, 1);
            scout.Composition.Add(scoutToken.Key, scoutToken);
            scout.InOrbit = star;
            Waypoint scoutWaypoint = new Waypoint();
            scoutWaypoint.Task = new NoTask();
            scout.Waypoints.Add(scoutWaypoint);
            empire.AddOrUpdateFleet(scout);

            new RemoteMiningStep().Process(serverData);

            Assert.AreEqual(0, scout.Cargo.Mass);
            Assert.AreEqual(0, star.MineralMiningProgress.Ironium);
        }

        [Test]
        public void Process_NonArFleetAtAnInhabitedPlanet_DoesNotMine()
        {
            // behavior-specs-11/population-growth.md section 5: the task pass refuses remote
            // mining of any inhabited planet by a non-AR fleet (message 118, order cancelled).
            ServerData serverData = new ServerData();

            Star star = new Star();
            star.Name = "Colony";
            star.Owner = 2;        // inhabited by another empire
            star.Colonists = 1000;
            star.MineralConcentration = new Resources(50, 50, 50, 0);
            star.MineralMiningProgress = new Resources();
            serverData.AllStars.Add(star.Key, star);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = MakeMiningFleet(1, 1, mineEquivalents: 100, cargoCapacity: 1000);
            fleet.InOrbit = star;
            empire.AddOrUpdateFleet(fleet);

            new RemoteMiningStep().Process(serverData);

            Assert.AreEqual(0, fleet.Cargo.Mass, "a non-AR fleet is refused at an inhabited planet");
            Assert.AreEqual(0, star.MineralMiningProgress.Ironium, "the concentration is untouched");
        }

        [Test]
        public void Process_ArFleetAtItsOwnPlanet_Mines()
        {
            ServerData serverData = new ServerData();

            Star star = new Star();
            star.Name = "AR Home";
            star.Owner = 1;
            star.Colonists = 1000;
            star.MineralConcentration = new Resources(50, 50, 50, 0);
            star.MineralMiningProgress = new Resources();
            serverData.AllStars.Add(star.Key, star);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("AR");
            serverData.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = MakeMiningFleet(1, 1, mineEquivalents: 100, cargoCapacity: 1000);
            fleet.InOrbit = star;
            empire.AddOrUpdateFleet(fleet);

            new RemoteMiningStep().Process(serverData);

            Assert.AreEqual(50, fleet.Cargo.Ironium, "an AR fleet may remote-mine its own worlds");
            Assert.AreEqual(50, fleet.Cargo.Boranium);
            Assert.AreEqual(50, fleet.Cargo.Germanium);
        }

        [Test]
        public void Process_ArFleetAtAPlanetItDoesNotOwn_DoesNotMine()
        {
            ServerData serverData = new ServerData();

            Star star = new Star();
            star.Name = "Somebody Else's";
            star.Owner = 2;
            star.Colonists = 1000;
            star.MineralConcentration = new Resources(50, 50, 50, 0);
            star.MineralMiningProgress = new Resources();
            serverData.AllStars.Add(star.Key, star);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("AR");
            serverData.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = MakeMiningFleet(1, 1, mineEquivalents: 100, cargoCapacity: 1000);
            fleet.InOrbit = star;
            empire.AddOrUpdateFleet(fleet);

            new RemoteMiningStep().Process(serverData);

            Assert.AreEqual(0, fleet.Cargo.Mass, "an AR fleet mines only planets it owns");
            Assert.AreEqual(0, star.MineralMiningProgress.Ironium);
        }
    }
}
