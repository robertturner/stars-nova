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

    // These exercise TurnGenerator.TryStargateJump indirectly (it's private) by running a full
    // Generate() and checking where the fleet ends up. Only DETERMINISTIC outcomes are asserted -
    // no test relies on the overgating "vanish chance" rolls, since docs/behavior-specs-4/
    // fleet-movement-scanning-cargo.md's own community-fitted vanish-chance formula produces a
    // near-100% chance right at the mass-overgating threshold (decreasing, counter-intuitively,
    // toward the fitted floor as mass approaches the 5x cap) - not something a test should assert
    // a specific pass/fail count against.
    [TestFixture]
    public class StargateJumpTest
    {
        private static Star MakeGateStar(string name, NovaPoint position, double safeRange, double safeHullMass)
        {
            Star star = new Star();
            star.Name = name;
            star.Position = position;
            star.Owner = Global.Nobody;

            Fleet starbase = new Fleet(1000 + name.GetHashCode() & 0xFFFF);
            ShipDesign gateDesign = new ShipDesign(2000);
            gateDesign.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            HullModule gateModule = new HullModule();
            Component gateComponent = new Component();
            gateComponent.Properties.Add("Gate", new Gate { SafeRange = safeRange, SafeHullMass = safeHullMass });
            gateModule.AllocatedComponent = gateComponent;
            hull.Modules.Add(gateModule);
            gateDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken gateToken = new ShipToken(gateDesign, 1);
            starbase.Composition.Add(gateToken.Key, gateToken);

            star.Starbase = starbase;
            return star;
        }

        private static Fleet MakeTravelingFleet(long key, ushort owner, int mass, Star startingAt, string destinationName)
        {
            Fleet fleet = new Fleet(key);
            fleet.Owner = owner;
            fleet.Position = startingAt.Position;
            fleet.InOrbit = startingAt;

            ShipDesign design = new ShipDesign(key);
            design.Blueprint = new Component();
            design.Blueprint.Mass = mass;
            Hull hull = new Hull();
            hull.ArmorStrength = 20;
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);
            ShipToken token = new ShipToken(design, 3); // 3 identical ships in this token
            token.Armor = 60; // 20 armor/ship x 3 ships, fully intact
            fleet.Composition.Add(token.Key, token);

            Waypoint waypoint = new Waypoint();
            waypoint.Position = startingAt.Position; // ignored by the gate check itself
            waypoint.WarpFactor = 5;
            waypoint.Task = new NoTask();
            waypoint.Destination = destinationName;
            fleet.Waypoints.Add(waypoint);

            return fleet;
        }

        private static ShipToken OnlyToken(Fleet fleet)
        {
            foreach (ShipToken token in fleet.Composition.Values)
            {
                return token;
            }
            return null;
        }

        private static (ServerData serverData, EmpireData empire, Star origin, Star destination) MakeTwoGatedStars()
        {
            ServerData serverData = new ServerData();
            Star origin = MakeGateStar("Origin", new NovaPoint(0, 0), safeRange: 100, safeHullMass: 200);
            Star destination = MakeGateStar("Destination", new NovaPoint(500, 0), safeRange: 100, safeHullMass: 200);
            serverData.AllStars.Add(origin.Key, origin);
            serverData.AllStars.Add(destination.Key, destination);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            return (serverData, empire, origin, destination);
        }

        /// <summary>
        /// A fleet with no real travel order has just one waypoint: the placeholder
        /// representing its own current position (see Fleet's ShipToken constructor), whose
        /// Destination is its own star's name. Regression test for a real bug this exposed the
        /// first time any star ever had a working Stargate (see TurnGenerator.TryStargateJump's
        /// own comment): without a same-star guard, this idle fleet's own placeholder waypoint
        /// was indistinguishable from "jump to my own star", so it took real overgating mass
        /// damage - and could be destroyed outright - purely for sitting still.
        /// </summary>
        [Test]
        public void IdleFleetAtItsOwnGate_IsNotTreatedAsGating()
        {
            var (serverData, empire, origin, _) = MakeTwoGatedStars();

            // Same mass/setup as the other tests here, but with no real travel order - just the
            // placeholder waypoint every fleet always has for "here's where I already am" (see
            // Fleet's ShipToken constructor), whose Destination is this fleet's own current star.
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Origin");
            fleet.Waypoints.Clear();
            Waypoint atRest = new Waypoint();
            atRest.Position = origin.Position;
            atRest.WarpFactor = 0;
            atRest.Task = new NoTask();
            atRest.Destination = "Origin";
            fleet.Waypoints.Add(atRest);
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.IsTrue(empire.OwnedFleets.ContainsKey(fleet.Key), "An idle fleet at its own gate must survive untouched");
            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(3, token.Quantity, "No ships should be lost just for sitting at a gate-equipped star");
            Assert.AreEqual(origin.Position, fleet.Position, "An idle fleet shouldn't move at all");
            Assert.IsFalse(serverData.AllMessages.Exists(m => m.Type == "Stargate"), "No gate-jump attempt should have been logged for a fleet that never ordered one");
        }

        [Test]
        public void SafeJump_ArrivesInstantlyWithNoDamageOrLosses()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();

            // 500 ly apart, but both gates are rated for it (safeRange 100 is irrelevant here -
            // the *distance* between these two stars is what's checked, and 500 > 100 would
            // actually overgate; use stars close enough to be within range instead).
            destination.Position = new NovaPoint(50, 0); // well within the 100 ly safe range
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination"); // well within the 200 mass rating
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreEqual(destination.Position, fleet.Position, "Fleet should have jumped straight to the destination this same turn");
            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(3, token.Quantity, "No ships should be lost on a safe jump");
            Assert.AreEqual(60, token.Armor, "Armor should be untouched by a safe (non-overgating) jump");
        }

        [Test]
        public void BeyondFiveTimesRange_RefusesTheJump_FleetDoesNotArriveThatTurn()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();

            // 100 ly safe range x5 = 500 ly cap - place the destination just beyond it.
            destination.Position = new NovaPoint(501, 0);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreNotEqual(destination.Position, fleet.Position, "Beyond the absolute 5x range cap the gate must refuse the jump outright");
        }

        [Test]
        public void CarryingMineralCargo_RefusesTheJump_ForAnOrdinaryRace()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Cargo.Ironium = 10;
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreNotEqual(destination.Position, fleet.Position, "A fleet carrying mineral cargo must not be allowed to gate unless Interstellar Traveler");
        }

        [Test]
        public void CarryingMineralCargo_StillGates_ForAnInterstellarTravelerRace()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0);
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("IT");

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Cargo.Ironium = 10;
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreEqual(destination.Position, fleet.Position, "Interstellar Traveler races may gate while carrying mineral/colonist cargo");
            Assert.AreEqual(10, fleet.Cargo.Ironium, "Cargo travels with the fleet through the gate");
        }
    }
}
