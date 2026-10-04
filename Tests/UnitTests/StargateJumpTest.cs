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
    using System;
    using System.Collections.Generic;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    // These exercise TurnGenerator.TryStargateJump indirectly (it's private), either by running a
    // full Generate() and checking where the fleet ends up, or - for the overgating vanish rolls,
    // messages and the no-repair side effect - by invoking the private per-fleet movement step
    // (ProcessFleet) with a scripted Random swapped into the generator. behavior-specs-9/
    // fleet-movement-scanning-cargo.md §5 "Overgating, code-confirmed" replaced the old
    // community-fitted vanish curve with one floor(damage / 3)% roll per ship, so those rolls are
    // now asserted exactly.
    [TestFixture]
    public class StargateJumpTest
    {
        /// <summary>Returns scripted values from Next(...) (then maxValue - 1, i.e. "no event")
        /// and 0 from NextDouble, counting every call.</summary>
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
                return 0.0;
            }
        }

        /// <summary>Runs just the movement/repair step for one fleet with the given Random,
        /// leaving this turn's messages in place. Returns true if the fleet was destroyed.</summary>
        private static bool RunMovement(ServerData serverData, Fleet fleet, Random random)
        {
            SimpleTurnGenerator generator = new SimpleTurnGenerator(serverData);
            typeof(TurnGenerator)
                .GetField("rand", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(generator, random);
            return (bool)typeof(TurnGenerator)
                .GetMethod("ProcessFleet", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(generator, new object[] { fleet });
        }

        /// <summary>A planet with a Stargate starbase. behavior-specs-10 §5: a gate is usable only
        /// by its owner's race or a friend, so by default it belongs to empire 1, the travelling
        /// fleet's owner in every test here.</summary>
        private static Star MakeGateStar(string name, NovaPoint position, double safeRange, double safeHullMass, ushort owner = 1)
        {
            Star star = new Star();
            star.Name = name;
            star.Position = position;
            star.Owner = owner;

            Fleet starbase = new Fleet(1000 + name.GetHashCode() & 0xFFFF);
            starbase.Owner = owner;
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
            waypoint.WarpFactor = Global.StargateWarpFactor; // "use Stargate" - the whole point of these tests
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

        // ---------------------------------------------------------------------------------
        // behavior-specs-10/fleet-movement-scanning-cargo.md §5 "Pre-jump cargo dump" and
        // "Fleet-carried gates", §4 "Cargo via Stargates".
        // ---------------------------------------------------------------------------------

        /// <summary>Rewritten for spec-10 (was "CarryingMineralCargo_RefusesTheJump_ForAnOrdinary
        /// Race", which encoded the superseded "must be off-loaded first" refusal): an ordinary
        /// race's fleet leaving from a planet's gate has all its minerals put onto the departure
        /// planet in full and still jumps; fuel stays aboard (message 236).</summary>
        [Test]
        public void CarryingMineralCargo_IsDumpedOntoTheDeparturePlanet_ForAnOrdinaryRace()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Cargo.Ironium = 10;
            fleet.Cargo.Boranium = 7;
            fleet.Cargo.Germanium = 3;
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position, "The jump still happens");
            Assert.AreEqual(0, fleet.Cargo.Mass, "Cargo is never carried through by a non-IT fleet");
            Assert.AreEqual(10, origin.ResourcesOnHand.Ironium, "No loss: everything lands on the departure planet");
            Assert.AreEqual(7, origin.ResourcesOnHand.Boranium);
            Assert.AreEqual(3, origin.ResourcesOnHand.Germanium);
            Assert.AreEqual(0, destination.ResourcesOnHand.Ironium, "Nothing reaches the destination");
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("20kT of minerals") && m.Text.Contains("Origin")));
        }

        /// <summary>Colonists at the fleet owner's own departure planet join its population in
        /// full, with no cap (message 237).</summary>
        [Test]
        public void CarryingColonists_AtItsOwnPlanet_AreAddedToThePopulation()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0);
            origin.Colonists = 1000;

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Cargo.ColonistsInKilotons = 5;
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position);
            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(1500, origin.Colonists);
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("500 colonists")));
        }

        private static EmpireData AddSecondEmpire(ServerData serverData, EmpireData fleetOwner, PlayerRelation relationTowardFleetOwner)
        {
            EmpireData other = new EmpireData();
            other.Id = 2;
            other.EmpireReports.Add(fleetOwner.Id, new EmpireIntel(fleetOwner) { Relation = relationTowardFleetOwner });
            serverData.AllEmpires.Add(other.Id, other);
            return other;
        }

        private static Star MakeOwnedGateStar(ServerData serverData, string name, NovaPoint position, ushort owner)
        {
            Star star = MakeGateStar(name, position, safeRange: 100, safeHullMass: 200, owner: owner);
            serverData.AllStars.Add(star.Key, star);
            return star;
        }

        /// <summary>Colonists aboard at a planet the fleet's owner does not own - a friend's
        /// included - refuse the jump (message 350); nothing is unloaded, minerals included.</summary>
        [Test]
        public void CarryingColonists_AtAFriendsGate_RefusesTheJump_AndUnloadsNothing()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            AddSecondEmpire(serverData, empire, PlayerRelation.Friend);
            Star origin = MakeOwnedGateStar(serverData, "Origin", new NovaPoint(0, 0), owner: 2);
            origin.Colonists = 1000;
            Star destination = MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Cargo.ColonistsInKilotons = 5;
            fleet.Cargo.Ironium = 10;
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(origin.Position, fleet.Position, "The jump is refused and retried next year");
            Assert.AreEqual(5, fleet.Cargo.ColonistsInKilotons, "Nothing is unloaded");
            Assert.AreEqual(10, fleet.Cargo.Ironium, "Not even the minerals");
            Assert.AreEqual(1000, origin.Colonists);
            Assert.AreEqual(0, origin.ResourcesOnHand.Ironium);
        }

        /// <summary>Minerals at a friend's gate are a gift to that friend, and the friend gets
        /// the same message as the fleet's owner.</summary>
        [Test]
        public void CarryingMinerals_AtAFriendsGate_AreGiftedToTheFriend_AndBothAreTold()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            AddSecondEmpire(serverData, empire, PlayerRelation.Friend);
            Star origin = MakeOwnedGateStar(serverData, "Origin", new NovaPoint(0, 0), owner: 2);
            Star destination = MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Cargo.Ironium = 10;
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position);
            Assert.AreEqual(0, fleet.Cargo.Ironium);
            Assert.AreEqual(10, origin.ResourcesOnHand.Ironium);
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("10kT of minerals")));
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Audience == 2 && m.Text.Contains("10kT of minerals")));
        }

        /// <summary>Interstellar Traveler skips the dump entirely - so not even the 350 refusal
        /// applies: colonists ride through a friend's gate.</summary>
        [Test]
        public void InterstellarTraveler_WithColonists_AtAFriendsGate_JumpsWithThem()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1, Race = new Race() };
            empire.Race.Traits.SetPrimary("IT");
            serverData.AllEmpires.Add(empire.Id, empire);
            AddSecondEmpire(serverData, empire, PlayerRelation.Friend);
            Star origin = MakeOwnedGateStar(serverData, "Origin", new NovaPoint(0, 0), owner: 2);
            Star destination = MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Cargo.ColonistsInKilotons = 5;
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position);
            Assert.AreEqual(5, fleet.Cargo.ColonistsInKilotons);
        }

        /// <summary>A departure gate owned by a race that does not rate the fleet's race as a
        /// friend cannot be used (message 230) - and the refusal unloads nothing.</summary>
        [Test]
        public void DepartureGate_OfANonFriend_RefusesTheJump()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            AddSecondEmpire(serverData, empire, PlayerRelation.Neutral);
            Star origin = MakeOwnedGateStar(serverData, "Origin", new NovaPoint(0, 0), owner: 2);
            MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(origin.Position, fleet.Position);
        }

        /// <summary>The relation that counts is the GATE OWNER's toward the fleet's race: the
        /// fleet's owner rating the gate owner as a friend is not enough.</summary>
        [Test]
        public void DepartureGate_FriendshipMustComeFromTheGateOwner()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            EmpireData other = AddSecondEmpire(serverData, empire, PlayerRelation.Enemy);
            empire.EmpireReports.Add(other.Id, new EmpireIntel(other) { Relation = PlayerRelation.Friend });
            Star origin = MakeOwnedGateStar(serverData, "Origin", new NovaPoint(0, 0), owner: 2);
            MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(origin.Position, fleet.Position);
        }

        /// <summary>An unowned departure gate (no owner can be a friend) is refused too.</summary>
        [Test]
        public void DepartureGate_Unowned_RefusesTheJump()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            Star origin = MakeOwnedGateStar(serverData, "Origin", new NovaPoint(0, 0), owner: Global.Nobody);
            MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(origin.Position, fleet.Position);
        }

        /// <summary>The destination gate must belong to the fleet's race or a friend (229).</summary>
        [Test]
        public void DestinationGate_OfANonFriend_RefusesTheJump()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            AddSecondEmpire(serverData, empire, PlayerRelation.Neutral);
            Star origin = MakeOwnedGateStar(serverData, "Origin", new NovaPoint(0, 0), owner: 1);
            MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 2);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(origin.Position, fleet.Position);
        }

        [Test]
        public void DestinationGate_OfAFriend_CanBeUsed()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            AddSecondEmpire(serverData, empire, PlayerRelation.Friend);
            Star origin = MakeOwnedGateStar(serverData, "Origin", new NovaPoint(0, 0), owner: 1);
            Star destination = MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 2);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position);
        }

        /// <summary>Range/mass refusals (227/228) run BEFORE the dump here (the spec's
        /// reimplementation recommendation; the original dumps first), so a refused overgate
        /// keeps its cargo aboard.</summary>
        [Test]
        public void RefusedOvergate_KeepsItsCargoAboard()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(501, 0);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Cargo.Ironium = 10;
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(origin.Position, fleet.Position);
            Assert.AreEqual(10, fleet.Cargo.Ironium);
            Assert.AreEqual(0, origin.ResourcesOnHand.Ironium);
        }

        private static void FitJumpGate(Fleet fleet)
        {
            foreach (ShipToken token in fleet.Composition.Values)
            {
                HullModule module = new HullModule();
                module.ComponentCount = 1;
                module.AllocatedComponent = new Component { Name = "Jump Gate" };
                token.Design.Hull.Modules.Add(module);
            }
        }

        private static Star MakePlainStar(ServerData serverData, string name, NovaPoint position, ushort owner)
        {
            Star star = new Star { Name = name, Position = position, Owner = owner };
            serverData.AllStars.Add(star.Key, star);
            return star;
        }

        /// <summary>With no gate at the departure planet, a fleet whose every design carries a
        /// Jump Gate still jumps; the cargo dump is skipped, so an ordinary race keeps its cargo.
        /// </summary>
        [Test]
        public void FleetCarriedJumpGates_JumpWithNoDepartureGate_AndKeepTheirCargo()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            Star origin = MakePlainStar(serverData, "Origin", new NovaPoint(0, 0), owner: 1);
            Star destination = MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            FitJumpGate(fleet);
            fleet.Cargo.Ironium = 10;
            fleet.Cargo.ColonistsInKilotons = 4;
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position);
            Assert.AreEqual(10, fleet.Cargo.Ironium, "Fleets on their own Jump Gates arrive with their cargo");
            Assert.AreEqual(4, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(0, origin.ResourcesOnHand.Ironium);
        }

        /// <summary>On its own Jump Gates the destination gate's ratings stand in for both ends:
        /// 300 ly against the destination's 100 ly range overgates (factor 2500 x 200 / 100 =
        /// 5000, damage 50%, 10 of 20 armor per ship; the scripted rolls of 99 vanish nobody),
        /// and 501 ly (more than 5 x 100) is refused.</summary>
        [Test]
        public void FleetCarriedJumpGates_UseTheDestinationRangeForTheSendingEnd()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            Star origin = MakePlainStar(serverData, "Origin", new NovaPoint(0, 0), owner: 1);
            Star destination = MakeOwnedGateStar(serverData, "Destination", new NovaPoint(300, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            FitJumpGate(fleet);
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position);
            Assert.AreEqual(3, OnlyToken(fleet).Quantity);
            Assert.AreEqual(60 - (3 * 10), OnlyToken(fleet).Armor, 1e-9);

            Star farOrigin = MakePlainStar(serverData, "FarOrigin", new NovaPoint(-201, 0), owner: 1);
            Fleet farFleet = MakeTravelingFleet(2, 1, mass: 50, farOrigin, "Destination");
            FitJumpGate(farFleet);
            empire.AddOrUpdateFleet(farFleet);

            RunMovement(serverData, farFleet, new ScriptedRandom());

            Assert.AreEqual(farOrigin.Position, farFleet.Position, "501 ly is beyond 5 x the destination's range");
        }

        /// <summary>Every occupied design must carry a Jump Gate (message 222 otherwise).</summary>
        [Test]
        public void FleetCarriedJumpGates_OneDesignWithoutAGate_RefusesTheJump()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            Star origin = MakePlainStar(serverData, "Origin", new NovaPoint(0, 0), owner: 1);
            MakeOwnedGateStar(serverData, "Destination", new NovaPoint(50, 0), owner: 1);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            FitJumpGate(fleet);
            ShipDesign bare = new ShipDesign(77);
            bare.Blueprint = new Component();
            bare.Blueprint.Mass = 50;
            bare.Blueprint.Properties.Add("Hull", new Hull { ArmorStrength = 20, Modules = new List<HullModule>() });
            ShipToken bareToken = new ShipToken(bare, 1);
            fleet.Composition.Add(bareToken.Key, bareToken);
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(origin.Position, fleet.Position);
        }

        /// <summary>A Jump Gate fleet can also jump from deep space - 500 ly in one turn, which no
        /// ordinary warp leg could cover.</summary>
        [Test]
        public void FleetCarriedJumpGates_JumpFromDeepSpace()
        {
            ServerData serverData = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverData.AllEmpires.Add(empire.Id, empire);
            Star nowhere = new Star { Name = "Nowhere", Position = new NovaPoint(0, 0) };
            Star destination = MakeGateStar("Destination", new NovaPoint(500, 0), safeRange: 1000, safeHullMass: 200);
            serverData.AllStars.Add(destination.Key, destination);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, nowhere, "Destination");
            fleet.InOrbit = null;
            fleet.Waypoints[0].Position = destination.Position;
            FitJumpGate(fleet);
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position);
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

        /// <summary>
        /// Regression test for a real, live-reported bug: a Scout was found stuck at Armor 0,
        /// Quantity 1 - "100% damaged" but never destroyed - after overgating. combinedDamagePercent
        /// (a fraction of the design's own MAX total armor, not whatever's currently left) landing
        /// comfortably under 100% skips the whole-token "all lost" branch entirely, but the
        /// resulting damage amount can still exceed an ALREADY-damaged token's remaining armor
        /// (e.g. from an earlier overgate or battle), driving Math.Max(0, ...)'s input negative -
        /// which clamps to exactly zero. Nothing used to check for that afterward; the token's
        /// survival was governed only by a wholly separate mass/range "vanish chance" dice roll
        /// that has nothing to do with whether its armor was actually exhausted. This test starts
        /// the fleet already partially damaged (Armor 5 of a possible 20) so a deterministic,
        /// comfortably-sub-100% overgate hit (~36%, well under the mass-damage-formula's own
        /// 0-100 range) still drives it to exactly zero.
        /// </summary>
        [Test]
        public void OvergateDamage_ExceedingAlreadyDamagedArmor_DestroysTheTokenInstead()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0); // well within the 100 ly safe range - rangeDamagePercent = 0

            // mass=380 against safeHullMass=200 (both gates, per MakeTwoGatedStars):
            // massDamagePercent = 100*(1-((1000-380)/800)^2) = 100*(1-0.775^2) ~= 40% -
            // comfortably under the whole-token "all lost" (>=100%) threshold, but 20 (this
            // design's own max total armor for 1 ship) * 0.40 ~= 8, which exceeds the 5 armor
            // this token starts with.
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 380, origin, "Destination");
            ShipToken token = OnlyToken(fleet);
            token.Quantity = 1;
            token.Armor = 5; // already damaged (e.g. from an earlier jump or battle), not fully intact
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.IsFalse(empire.OwnedFleets.ContainsKey(fleet.Key), "A token whose armor is driven to zero by overgate damage must be destroyed, not left stuck at 0 armor.");
        }

        /// <summary>
        /// The other half of the original game's "use Stargate" speed selector: choosing an
        /// ORDINARY warp speed must ignore any Stargate that happens to exist at both ends and
        /// travel normally instead - exactly as if no gate were there. Distance 50 at warp 5
        /// (speed 25) takes 2 years, so a single turn's Generate() can only cover half the trip.
        /// </summary>
        [Test]
        public void OrdinaryWarpSpeed_IgnoresAvailableGate_TravelsNormallyInstead()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0); // would gate instantly if ordered to

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            fleet.Waypoints[0].WarpFactor = 5; // an ordinary speed, not Global.StargateWarpFactor
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreNotEqual(destination.Position, fleet.Position, "An ordinary warp speed must not use the Stargate even though one exists at both ends");
            Assert.AreEqual(60, OnlyToken(fleet).Armor, "A plain warp leg must not take any overgating-style damage");
        }

        /// <summary>
        /// Matches the original game's own behavior for this speed selection: explicitly ordering
        /// "use Stargate" when neither end actually has one doesn't fall back to ordinary warp
        /// travel - it simply fails, and the fleet stays exactly where it was.
        /// </summary>
        [Test]
        public void StargateOrdered_ButNeitherStarHasOne_FailsAndStaysPut()
        {
            ServerData serverData = new ServerData();
            Star origin = new Star();
            origin.Name = "PlainOrigin";
            origin.Position = new NovaPoint(0, 0);
            origin.Owner = Global.Nobody;
            Star destination = new Star();
            destination.Name = "PlainDestination";
            destination.Position = new NovaPoint(50, 0);
            destination.Owner = Global.Nobody;
            serverData.AllStars.Add(origin.Key, origin);
            serverData.AllStars.Add(destination.Key, destination);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "PlainDestination");
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            // Note: Generate() itself wipes serverState.AllMessages right before returning (see
            // its own "remove old messages" comment) - even the existing IdleFleetAtItsOwnGate
            // test's own message check above is vacuously true for the same reason - so the
            // failure message TryStargateJump/EmitStargateFailureMessage adds isn't observable
            // here; what matters for this test is that the fleet neither moved nor lost anything.
            Assert.AreEqual(origin.Position, fleet.Position, "With no Stargate at either end, an explicit gate order must fail and leave the fleet exactly where it was");
            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(3, token.Quantity, "A failed gate order must not cost the fleet anything");
        }

        /// <summary>
        /// Real components.xml data includes "Any" mass/range Stargates (e.g. "Gate Any/300",
        /// "Gate 100/Any", "Gate Any/Any"), stored as SafeHullMass/SafeRange &lt;= 0 - the same
        /// convention ShipDesignViewModel's own component description already uses ("Safe for
        /// any hull mass"/"Unlimited range"). Regression test for a real, live-reported bug:
        /// TryStargateJump originally treated that same &lt;= 0 as "this isn't really a gate" (or,
        /// past that check, multiplied distance/mass by a negative "safe" limit), so a fleet at a
        /// perfectly good unlimited-mass gate was told there was no Stargate at all, and a huge
        /// ship that should sail through untouched would instead have been refused outright by
        /// the 5x-cap check (any real mass compared against a negative cap always "exceeds" it).
        /// </summary>
        [Test]
        public void UnlimitedMassGate_LetsAnOversizedShipJumpSafely()
        {
            ServerData serverData = new ServerData();
            Star origin = MakeGateStar("Origin", new NovaPoint(0, 0), safeRange: 100, safeHullMass: -1);
            Star destination = MakeGateStar("Destination", new NovaPoint(50, 0), safeRange: 100, safeHullMass: -1);
            serverData.AllStars.Add(origin.Key, origin);
            serverData.AllStars.Add(destination.Key, destination);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            // Absurdly large relative to any real hull-mass rating - would be refused outright
            // (or take heavy overgating damage) at an ordinary gate, but an unlimited-mass gate
            // must wave it through untouched.
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 100000, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreEqual(destination.Position, fleet.Position, "An unlimited-mass Stargate must let an oversized ship jump this same turn");
            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(3, token.Quantity, "No ships should be lost - mass is unlimited at both ends");
            Assert.AreEqual(60, token.Armor, "No mass-overgating damage should apply when the gate has no mass limit");
        }

        /// <summary>Same bug, the range dimension: an "Any" range gate (SafeRange &lt;= 0) must
        /// let a fleet jump across any map-scale distance with no range-based damage, not refuse
        /// the jump outright the way a negative "5x cap" would if naively multiplied through.
        /// behavior-specs-9 §5: "any" counts as 8,000 ly, so the test distance is kept inside that
        /// (it used to be 1,000,000 ly, which spec-9 says is refused).</summary>
        [Test]
        public void UnlimitedRangeGate_LetsADistantJumpSucceedSafely()
        {
            ServerData serverData = new ServerData();
            Star origin = MakeGateStar("Origin", new NovaPoint(0, 0), safeRange: -1, safeHullMass: 200);
            Star destination = MakeGateStar("Destination", new NovaPoint(7000, 0), safeRange: -1, safeHullMass: 200);
            serverData.AllStars.Add(origin.Key, origin);
            serverData.AllStars.Add(destination.Key, destination);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination"); // well within the 200 mass rating
            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreEqual(destination.Position, fleet.Position, "An unlimited-range Stargate must let a distant jump succeed this same turn");
            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(3, token.Quantity, "No ships should be lost - range is unlimited at the sending gate");
            Assert.AreEqual(60, token.Armor, "No range-overgating damage should apply when the gate has no range limit");
        }

        // ---------------------------------------------------------------------------------
        // behavior-specs-9/fleet-movement-scanning-cargo.md §5 "Overgating, code-confirmed".
        // Gates from MakeTwoGatedStars: range 100 ly, mass 200 kT at both ends. Each design has
        // 20 armor per ship, 3 ships (60 total).
        // ---------------------------------------------------------------------------------

        /// <summary>Mass 600 against 200 at both gates: each factor 2500 x (1000 - 600) / 200 =
        /// 5000; 5000 x 5000 / 10000 = 2500; damage 75%. Interstellar Traveler ships never vanish
        /// (no roll at all) but take the same damage: 15 of 20 armor per ship.</summary>
        [Test]
        public void InterstellarTraveler_NeverVanishes_ButTakesTheFullDamage()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0);
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("IT");
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 600, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);
            ScriptedRandom random = new ScriptedRandom();

            bool destroyed = RunMovement(serverData, fleet, random);

            Assert.IsFalse(destroyed);
            Assert.AreEqual(destination.Position, fleet.Position);
            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(3, token.Quantity, "Interstellar Traveler ships never vanish");
            Assert.AreEqual(60 - (3 * 15), token.Armor, 1e-9, "75% of 20 armor per surviving ship, and no repair after a jump");
            Assert.AreEqual(0, random.Calls, "The vanish roll is skipped entirely for Interstellar Traveler");
        }

        /// <summary>Range 250 ly against 100: factor 2500 x (500 - 250) / 100 = 6250. Mass 300
        /// against 200 at each gate: 2500 x 700 / 200 = 8750. Running: 6250 x 8750 / 10000 = 5468,
        /// x 8750 / 10000 = 4784 (truncated each step); damage (10000 - 4784) / 100 = 52%. The
        /// vanish chance is floor(52 / 3) = 17% - ONE roll per ship covering range and mass
        /// together.</summary>
        [Test]
        public void VanishChance_IsOneThirdOfCombinedDamage_OneRollPerShip()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(250, 0);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 300, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            // 16 < 17 vanishes; 17 and 99 survive.
            ScriptedRandom random = new ScriptedRandom(16, 17, 99);

            bool destroyed = RunMovement(serverData, fleet, random);

            Assert.IsFalse(destroyed);
            Assert.AreEqual(3, random.Calls, "Exactly one roll per ship - not separate mass and range rolls");
            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(2, token.Quantity, "Exactly the ship that rolled below 17 vanished");

            // Each survivor takes floor(20 x 52 / 100) = 10 armor.
            Assert.AreEqual(40 - (2 * 10), token.Armor, 1e-9);

            // 1 lost of 3: not below 3/4 = 0, not above 3/2 = 1 -> the middle message (233).
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Type == "Stargate" && m.Text.Contains("(1 of 3)")),
                "A loss message reports the ships lost against the pre-jump count");
        }

        /// <summary>Just past the range limit (104 ly against 100): factor 2500 x 396 / 100 = 9900,
        /// damage 1%, vanish chance 0. 1% of 20 armor truncates to 0, but survivors take at least
        /// 1 point each. And a completed jump is "saw action": no repair that turn.</summary>
        [Test]
        public void SmallOvergate_DamagesAtLeastOnePointPerShip()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(104, 0);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom(0, 0, 0));

            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(3, token.Quantity, "A 0% vanish chance never loses a ship, whatever the roll");
            Assert.AreEqual(57, token.Armor, 1e-9, "1 point per ship, not 1% of 20 = 0.2");
            Assert.IsFalse(serverData.AllMessages.Exists(m => m.Type == "Stargate" && m.Text.Contains(" lost ")),
                "No loss message when no ship was lost, however damaged the fleet is");
        }

        /// <summary>A completed (safe) jump marks the fleet as having seen action, so the repair
        /// step that follows skips it (fleet-movement-scanning-cargo.md §5 "Side effect").</summary>
        [Test]
        public void CompletedJump_GetsNoRepairThatTurn()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            OnlyToken(fleet).Armor = 30;
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(destination.Position, fleet.Position);
            Assert.AreEqual(30, OnlyToken(fleet).Armor, 1e-9, "No repair in the generation a Stargate jump completed");
        }

        /// <summary>An "any" range gate counts as 8,000 ly: 10,000 ly overgates (factor
        /// 2500 x (40000 - 10000) / 8000 = 9375, damage 6%, 1 point per ship).</summary>
        [Test]
        public void AnyRangeGate_CountsAs8000LightYears()
        {
            ServerData serverData = new ServerData();
            Star origin = MakeGateStar("Origin", new NovaPoint(0, 0), safeRange: -1, safeHullMass: 200);
            Star destination = MakeGateStar("Destination", new NovaPoint(10000, 0), safeRange: -1, safeHullMass: 200);
            serverData.AllStars.Add(origin.Key, origin);
            serverData.AllStars.Add(destination.Key, destination);
            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom(99, 99, 99));

            Assert.AreEqual(destination.Position, fleet.Position);
            ShipToken token = OnlyToken(fleet);
            Assert.AreEqual(3, token.Quantity);
            Assert.AreEqual(57, token.Armor, 1e-9, "6% of 20 = 1 point per ship");
        }

        [Test]
        public void AnyRangeGate_BeyondFiveTimes8000LightYears_RefusesTheJump()
        {
            ServerData serverData = new ServerData();
            Star origin = MakeGateStar("Origin", new NovaPoint(0, 0), safeRange: -1, safeHullMass: 200);
            Star destination = MakeGateStar("Destination", new NovaPoint(40001, 0), safeRange: -1, safeHullMass: 200);
            serverData.AllStars.Add(origin.Key, origin);
            serverData.AllStars.Add(destination.Key, destination);
            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 50, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.AreEqual(origin.Position, fleet.Position, "More than 5 x 8,000 ly is refused");
            Assert.AreEqual(60, OnlyToken(fleet).Armor, 1e-9);
        }

        /// <summary>Exactly 5x a gate's mass rating is not refused, but its survival factor is 0:
        /// certain destruction (message 231, fleet removed).</summary>
        [Test]
        public void MassExactlyFiveTimesTheRating_IsNotRefused_ButIsCertainDestruction()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 1000, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            bool destroyed = RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.IsTrue(destroyed);
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Type == "Stargate" && m.Text.Contains("has been lost")));
        }

        [Test]
        public void MassMoreThanFiveTimesTheRating_RefusesTheJump()
        {
            var (serverData, empire, origin, destination) = MakeTwoGatedStars();
            destination.Position = new NovaPoint(50, 0);
            Fleet fleet = MakeTravelingFleet(1, 1, mass: 1001, origin, "Destination");
            empire.AddOrUpdateFleet(fleet);

            bool destroyed = RunMovement(serverData, fleet, new ScriptedRandom());

            Assert.IsFalse(destroyed);
            Assert.AreEqual(origin.Position, fleet.Position);
            Assert.AreEqual(3, OnlyToken(fleet).Quantity);
        }
    }
}
