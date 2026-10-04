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
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    using NUnit.Framework;

    /// <summary>
    /// Pursuit of fleet-targeted waypoints, Patrol and Repeat Orders
    /// (behavior-specs-10/fleet-movement-scanning-cargo.md §5: "Pursuit", Patrol "Complete rule",
    /// Repeat Orders).
    /// </summary>
    [TestFixture]
    public class FleetWaypointBehaviourTest
    {
        private ServerData serverData;
        private EmpireData empire1;
        private EmpireData empire2;

        [SetUp]
        public void SetUp()
        {
            serverData = new SimpleServerData();
            empire1 = NewEmpire(1);
            empire2 = NewEmpire(2);
            empire1.EmpireReports.Add(empire2.Id, new EmpireIntel(empire2));
            empire2.EmpireReports.Add(empire1.Id, new EmpireIntel(empire1));
        }

        private EmpireData NewEmpire(ushort id)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Id = id;
            empire.AvailableComponents = new RaceComponents();
            serverData.AllEmpires.Add(empire.Id, empire);
            return empire;
        }

        private static ShipDesign Design(long key, int scanRange = 0, int cargo = 0, Engine engine = null, string engineName = "Test Engine")
        {
            Component blueprint = new Component { Mass = 100 };
            // FuelCapacity > 0: a hull without fuel is a starbase here (Hull.IsStarbase).
            Hull hull = new Hull { Modules = new List<HullModule>(), BaseCargo = cargo, FuelCapacity = 1000 };
            hull.Modules.Add(new HullModule());

            if (scanRange > 0)
            {
                Component scanner = new Component();
                scanner.Properties.Add("Scanner", new Scanner { NormalScan = scanRange });
                hull.Modules.Add(new HullModule { AllocatedComponent = scanner, ComponentCount = 1 });
            }

            if (engine != null)
            {
                Component engineComponent = new Component { Name = engineName };
                engineComponent.Properties.Add("Engine", engine);
                hull.Modules.Add(new HullModule { AllocatedComponent = engineComponent, ComponentCount = 1 });
            }

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Icon = new ShipIcon("hull0000.png", null) };
            design.Update();
            return design;
        }

        private Fleet AddFleet(EmpireData owner, uint id, int x, int y, ShipDesign design)
        {
            Fleet fleet = new Fleet(id);
            fleet.Owner = (ushort)owner.Id;
            fleet.Id = id;
            fleet.Name = "Fleet " + owner.Id + "-" + id;
            fleet.Position = new NovaPoint(x, y);
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position, Destination = "Space at " + fleet.Position, WarpFactor = 0 });
            owner.AddOrUpdateFleet(fleet);
            return fleet;
        }

        private static Waypoint PointAt(int x, int y, int warp, string destination = null)
        {
            return new Waypoint { Position = new NovaPoint(x, y), WarpFactor = warp, Destination = destination ?? ("Space at " + x + "," + y), Task = new NoTask() };
        }

        private Star AddStar(string name, int x, int y)
        {
            Star star = new Star { Name = name, Position = new NovaPoint(x, y) };
            serverData.AllStars.Add(star.Key, star);
            return star;
        }

        private void MarkSeen(EmpireData observer, Fleet target)
        {
            observer.FleetReports[target.Key] = target.GenerateReport(ScanLevel.InScan, serverData.TurnYear);
        }

        private void Generate()
        {
            new SimpleTurnGenerator(serverData).Generate();
        }

        // ------------------------------------------------------------------
        // Pursuit
        // ------------------------------------------------------------------

        [Test]
        public void Pursuit_HeadsForWhereTheTargetStoodLastTurn_AndArrivesOnlyWhenBothEndOnTheSameSpot()
        {
            Fleet target = AddFleet(empire2, 20, 100, 0, Design(2));
            target.Waypoints.Add(PointAt(200, 0, 5)); // 25 ly a year, reaching (200,0) in turn 4

            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1, scanRange: 1000));
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() }; // 81 ly a year
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);

            Generate();
            Assert.AreEqual(new NovaPoint(81, 0), pursuer.Position, "Turn 1: steers toward the stored point (100,0)");
            Assert.AreEqual(2, pursuer.Waypoints.Count);
            Assert.IsTrue(pursuer.Waypoints[1].IsFleetTarget);
            Assert.AreEqual(new NovaPoint(125, 0), pursuer.Waypoints[1].Position, "The refresh copies the target's new position");

            Generate();
            Assert.AreEqual(new NovaPoint(125, 0), pursuer.Position, "Turn 2: reaches the stored point but the target has moved on");
            Assert.AreEqual(2, pursuer.Waypoints.Count, "Not arrived: the target is no longer there");
            Assert.AreEqual(new NovaPoint(150, 0), pursuer.Waypoints[1].Position);
            Assert.AreEqual(9, pursuer.Waypoints[1].WarpFactor, "The leg keeps its warp while the chase goes on");

            Generate(); // pursuer 150, target 175
            Generate(); // pursuer 175, target 200 (stops)
            Assert.AreEqual(new NovaPoint(175, 0), pursuer.Position);

            Generate(); // pursuer 200, target still at 200 -> arrival
            Assert.AreEqual(new NovaPoint(200, 0), pursuer.Position);
            Assert.AreEqual(1, pursuer.Waypoints.Count, "The reached leg became waypoint 0");
            Assert.IsFalse(pursuer.Waypoints[0].IsFleetTarget, "On arrival the waypoint becomes a fixed point");
            Assert.AreEqual(WaypointTargetKind.DeepSpace, pursuer.Waypoints[0].TargetKind);
        }

        [Test]
        public void Pursuit_OnArrivalAtAPlanet_TheWaypointBecomesThatPlanet()
        {
            Star star = AddStar("Haven", 50, 0);
            Fleet target = AddFleet(empire2, 20, 50, 0, Design(2));
            target.InOrbit = star;

            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1, scanRange: 1000));
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);

            Generate();

            Assert.AreEqual(star.Position, pursuer.Position);
            Assert.AreEqual(1, pursuer.Waypoints.Count);
            Assert.AreEqual(WaypointTargetKind.Planet, pursuer.Waypoints[0].TargetKind);
            Assert.AreEqual("Haven", pursuer.Waypoints[0].Destination);
        }

        [Test]
        public void Refresh_TargetGone_TurnsTheWaypointIntoADeepSpacePoint()
        {
            Fleet target = AddFleet(empire2, 20, 100, 0, Design(2));
            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1));
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);

            empire2.RemoveFleet(target);
            new FleetPursuit(serverData).RefreshAndResolveArrivals();

            Assert.IsFalse(pursuer.Waypoints[1].IsFleetTarget);
            Assert.AreEqual(WaypointTargetKind.DeepSpace, pursuer.Waypoints[1].TargetKind);
            Assert.AreEqual(new NovaPoint(100, 0), pursuer.Waypoints[1].Position);
        }

        [Test]
        public void JumpFreeze_KeepsOtherPlayersPursuersAtThePointTheTargetLeft_UntilRevalidation()
        {
            Fleet target = AddFleet(empire2, 20, 100, 0, Design(2));
            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1));
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);

            // The target's own empire also "pursues" it: same-owner waypoints are not frozen.
            Fleet friend = AddFleet(empire2, 21, 0, 50, Design(3));
            Waypoint friendLeg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            friendLeg.AimAtFleet(target);
            friend.Waypoints.Add(friendLeg);

            FleetPursuit pursuit = new FleetPursuit(serverData);
            pursuit.FreezePursuers(target, target.Position);
            target.Position = new NovaPoint(300, 300); // gated away

            pursuit.RefreshAndResolveArrivals();

            Assert.IsTrue(pursuer.Waypoints[1].PursuitFrozen);
            Assert.AreEqual(new NovaPoint(100, 0), pursuer.Waypoints[1].Position, "The refresh leaves a frozen waypoint alone");
            Assert.IsFalse(friend.Waypoints[1].PursuitFrozen);
            Assert.AreEqual(new NovaPoint(300, 300), friend.Waypoints[1].Position);

            MarkSeen(empire1, target);
            pursuit.Revalidate(empire1, pursuer, pursuit.LiveFleetsByKey());
            Assert.IsFalse(pursuer.Waypoints[1].PursuitFrozen, "Step 39 clears the freeze mark");
            Assert.IsTrue(pursuer.Waypoints[1].IsFleetTarget, "A still-seen target keeps the pursuit");
        }

        [Test]
        public void Revalidation_DestroyedTarget_Message40_FixedPoint()
        {
            Fleet target = AddFleet(empire2, 20, 100, 0, Design(2));
            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1));
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);
            empire2.RemoveFleet(target);

            FleetPursuit pursuit = new FleetPursuit(serverData);
            pursuit.Revalidate(empire1, pursuer, pursuit.LiveFleetsByKey());

            Assert.IsFalse(pursuer.Waypoints[1].IsFleetTarget);
            Assert.AreEqual(new NovaPoint(100, 0), pursuer.Waypoints[1].Position);
            Assert.IsTrue(serverData.AllMessages.Any(m => m.Type == "Fleet Target 40" && m.Audience == 1));
        }

        [Test]
        public void Revalidation_UnseenTargetInOrbit_Message41_BecomesThatPlanet()
        {
            Star star = AddStar("Refuge", 100, 0);
            Fleet target = AddFleet(empire2, 20, 100, 0, Design(2));
            target.InOrbit = star;
            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1));
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);

            FleetPursuit pursuit = new FleetPursuit(serverData);
            pursuit.Revalidate(empire1, pursuer, pursuit.LiveFleetsByKey());

            Assert.AreEqual(WaypointTargetKind.Planet, pursuer.Waypoints[1].TargetKind);
            Assert.AreEqual("Refuge", pursuer.Waypoints[1].Destination);
            Assert.IsTrue(serverData.AllMessages.Any(m => m.Type == "Fleet Target 41"));
        }

        [Test]
        public void Revalidation_UnseenTargetInDeepSpace_Message42_FixedPoint()
        {
            Fleet target = AddFleet(empire2, 20, 100, 0, Design(2));
            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1));
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);

            // An old report (last year) does not count as seen.
            empire1.FleetReports[target.Key] = target.GenerateReport(ScanLevel.InScan, serverData.TurnYear - 1);

            FleetPursuit pursuit = new FleetPursuit(serverData);
            pursuit.Revalidate(empire1, pursuer, pursuit.LiveFleetsByKey());

            Assert.AreEqual(WaypointTargetKind.DeepSpace, pursuer.Waypoints[1].TargetKind);
            Assert.IsTrue(serverData.AllMessages.Any(m => m.Type == "Fleet Target 42"));
        }

        [Test]
        public void ReLock_LostTarget_ReAimsAtASameOwnerFleetAtTheStoredPoint_PreferringThePrimaryTargetType()
        {
            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1));
            empire1.BattlePlans["Default"].PrimaryTarget = "Freighters";

            Fleet gone = AddFleet(empire2, 20, 100, 0, Design(2));
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            leg.AimAtFleet(gone);
            pursuer.Waypoints.Add(leg);
            empire2.RemoveFleet(gone); // e.g. merged away

            Fleet warship = AddFleet(empire2, 21, 100, 0, Design(3));
            Fleet freighter = AddFleet(empire2, 22, 100, 0, Design(4, cargo: 100));
            AddFleet(empire1, 11, 100, 0, Design(5)); // another owner's fleet there is never chosen

            for (int seed = 0; seed < 10; seed++)
            {
                leg.TargetFleetKey = gone.Key;
                new FleetPursuit(serverData).ReLockTargets(new Random(seed));
                Assert.AreEqual(freighter.Key, leg.TargetFleetKey, "Seed " + seed);
            }

            // Nothing there: the stale key is kept.
            leg.TargetFleetKey = gone.Key;
            leg.Position = new NovaPoint(5, 5);
            new FleetPursuit(serverData).ReLockTargets(new Random(1));
            Assert.AreEqual(gone.Key, leg.TargetFleetKey);
            Assert.AreNotEqual(warship.Key, leg.TargetFleetKey);
        }

        // ------------------------------------------------------------------
        // Patrol
        // ------------------------------------------------------------------

        private Fleet Patroller(uint id, int x, int y, int speed, int rangeIndex, bool repeat = false, ShipDesign design = null)
        {
            Fleet fleet = AddFleet(empire1, id, x, y, design ?? Design(100 + id));
            fleet.Waypoints[0].Task = new PatrolTask { Speed = speed, RangeIndex = rangeIndex };
            fleet.RepeatOrders = repeat;
            return fleet;
        }

        private void AttackEveryone(string primary = "Any")
        {
            empire1.BattlePlans["Default"].Attack = "Everyone";
            empire1.BattlePlans["Default"].PrimaryTarget = primary;
        }

        [Test]
        public void PatrolRange_IsStoredValuePlusOneTimesFifty_AndTenIsUnlimited()
        {
            Assert.AreEqual(50, new PatrolTask { RangeIndex = 0 }.RangeInLightYears);
            Assert.AreEqual(250, new PatrolTask { RangeIndex = 4 }.RangeInLightYears);
            Assert.AreEqual(500, new PatrolTask { RangeIndex = 9 }.RangeInLightYears);
            Assert.AreEqual(10000, new PatrolTask { RangeIndex = 10 }.RangeInLightYears);
        }

        [Test]
        public void Patrol_InterceptsTheNearestSeenEligibleFleet_InRange_AsWaypointOne()
        {
            AttackEveryone();
            Fleet patroller = Patroller(1, 0, 0, speed: 7, rangeIndex: 1); // 100 ly
            Fleet near = AddFleet(empire2, 20, 60, 0, Design(2));
            Fleet far = AddFleet(empire2, 21, 150, 0, Design(3));
            MarkSeen(empire1, near);
            MarkSeen(empire1, far);

            new PatrolStep().Process(serverData);

            Assert.AreEqual(2, patroller.Waypoints.Count);
            Waypoint leg = patroller.Waypoints[1];
            Assert.IsTrue(leg.IsFleetTarget);
            Assert.AreEqual(near.Key, leg.TargetFleetKey);
            Assert.AreEqual(near.Position, leg.Position);
            Assert.AreEqual(7, leg.WarpFactor, "The Patrol speed setting");
            PatrolTask legTask = leg.Task as PatrolTask;
            Assert.IsNotNull(legTask, "With only its current-position waypoint, the leg carries Patrol");
            Assert.AreEqual(7, legTask.Speed);
            Assert.AreEqual(1, legTask.RangeIndex);
            Assert.IsInstanceOf<PatrolTask>(patroller.Waypoints[0].Task);
            Assert.IsTrue(serverData.AllMessages.Any(m => m.Type == "Patrol" && m.Audience == 1), "Message 255");
        }

        [Test]
        public void Patrol_NoInterceptWhenTheChosenFleetIsOutOfRange_OrUnseen()
        {
            AttackEveryone();
            Fleet patroller = Patroller(1, 0, 0, speed: 7, rangeIndex: 1);
            Fleet far = AddFleet(empire2, 21, 150, 0, Design(3));
            MarkSeen(empire1, far);
            AddFleet(empire2, 22, 30, 0, Design(4)); // in range but not seen

            new PatrolStep().Process(serverData);

            Assert.AreEqual(1, patroller.Waypoints.Count);

            patroller.Waypoints[0].Task = new PatrolTask { Speed = 7, RangeIndex = 10 };
            new PatrolStep().Process(serverData);
            Assert.AreEqual(2, patroller.Waypoints.Count, "Range 10 is effectively unlimited");
            Assert.AreEqual(far.Key, patroller.Waypoints[1].TargetFleetKey);
        }

        [Test]
        public void Patrol_AnUnmarkedFleetBeatsAMarkedOne_SoPatrolsSpreadOverTargets()
        {
            AttackEveryone();
            Fleet first = Patroller(1, 0, 0, speed: 6, rangeIndex: 10);
            Fleet second = Patroller(2, 0, 0, speed: 6, rangeIndex: 10);
            Fleet near = AddFleet(empire2, 20, 10, 0, Design(2));
            Fleet far = AddFleet(empire2, 21, 50, 0, Design(3));
            MarkSeen(empire1, near);
            MarkSeen(empire1, far);

            new PatrolStep().Process(serverData);

            Assert.AreEqual(near.Key, first.Waypoints[1].TargetFleetKey);
            Assert.AreEqual(far.Key, second.Waypoints[1].TargetFleetKey);
        }

        [Test]
        public void Patrol_TargetsFollowTheBattlePlan_PrimaryTargetAndAttackWho()
        {
            Fleet patroller = Patroller(1, 0, 0, speed: 6, rangeIndex: 10);
            Fleet near = AddFleet(empire2, 20, 10, 0, Design(2));
            Fleet freighter = AddFleet(empire2, 21, 50, 0, Design(3, cargo: 100));
            MarkSeen(empire1, near);
            MarkSeen(empire1, freighter);

            // Attack "Enemies" against a Neutral race: no one is a target.
            empire1.BattlePlans["Default"].Attack = "Enemies";
            empire1.BattlePlans["Default"].PrimaryTarget = "Any";
            empire1.EmpireReports[2].Relation = PlayerRelation.Neutral;
            new PatrolStep().Process(serverData);
            Assert.AreEqual(1, patroller.Waypoints.Count);

            // Primary target Freighters: the farther freighter is chosen.
            AttackEveryone("Freighters");
            new PatrolStep().Process(serverData);
            Assert.AreEqual(2, patroller.Waypoints.Count);
            Assert.AreEqual(freighter.Key, patroller.Waypoints[1].TargetFleetKey);
        }

        [Test]
        public void Patrol_LookAheadPromotion_AndNoRescanWhileALegIsPending()
        {
            AttackEveryone();
            Fleet patroller = AddFleet(empire1, 1, 0, 0, Design(101));
            Waypoint post = PointAt(200, 0, 6);
            post.Task = new PatrolTask { Speed = 5, RangeIndex = 10 };
            patroller.Waypoints.Add(post);

            Fleet enemy = AddFleet(empire2, 20, 30, 0, Design(2));
            MarkSeen(empire1, enemy);

            new PatrolStep().Process(serverData);

            Assert.IsInstanceOf<PatrolTask>(patroller.Waypoints[0].Task, "Patrol copied onto waypoint 0");
            Assert.AreEqual(3, patroller.Waypoints.Count);
            Assert.AreEqual(enemy.Key, patroller.Waypoints[1].TargetFleetKey);
            Assert.AreEqual(5, patroller.Waypoints[1].WarpFactor);
            Assert.IsInstanceOf<PatrolTask>(patroller.Waypoints[1].Task, "A copy of the old next waypoint");
            Assert.AreEqual(new NovaPoint(200, 0), patroller.Waypoints[2].Position, "The old next waypoint moved down one place");

            MarkSeen(empire1, enemy);
            new PatrolStep().Process(serverData);
            Assert.AreEqual(3, patroller.Waypoints.Count, "An intercept under way is not re-scanned");
        }

        [Test]
        public void Patrol_RepeatOrdersWithOneWaypoint_AppendsThePatrolPostAtTheEfficientWarp()
        {
            AttackEveryone();
            Engine engine = new Engine();
            engine.FuelConsumption = new[] { 0, 0, 0, 0, 0, 100, 110, 150, 200, 300 };
            Fleet patroller = Patroller(1, 0, 0, speed: 0, rangeIndex: 10, repeat: true, design: Design(101, engine: engine));
            Fleet enemy = AddFleet(empire2, 20, 30, 0, Design(2));
            MarkSeen(empire1, enemy);

            // Highest warp at most 120% fuel is 7 (110); with the free-speed preference Patrol
            // uses, warp 5 burns nothing, so the result drops by 2 to 5
            // (fleet-movement-scanning-cargo.md section 5, Patrol "Efficient warp").
            Assert.AreEqual(5, PatrolStep.EfficientWarp(patroller));

            new PatrolStep().Process(serverData);

            Assert.AreEqual(3, patroller.Waypoints.Count);
            Assert.AreEqual(5, patroller.Waypoints[1].WarpFactor, "Speed setting 0 means the efficient warp");
            Waypoint post = patroller.Waypoints[2];
            Assert.AreEqual(new NovaPoint(0, 0), post.Position);
            Assert.IsInstanceOf<PatrolTask>(post.Task);
            Assert.AreEqual(5, post.WarpFactor);
            Assert.IsFalse(post.IsFleetTarget);
        }

        [Test]
        public void EfficientWarp_WithTheFreeSpeedPreference_DropsToOneWarpBelowAFreeOne()
        {
            // Table where warp 6 burns nothing: the chosen 7 drops by 1 to 6.
            Engine engine = new Engine();
            engine.FuelConsumption = new[] { 0, 0, 0, 0, 0, 0, 110, 150, 200, 300 };
            Fleet fleet = AddFleet(empire1, 1, 0, 0, Design(101, engine: engine));

            Assert.AreEqual(6, PatrolStep.EfficientWarp(fleet));
        }

        [Test]
        public void EfficientWarp_ScoopEnginesSkipTheFreeSpeedDrop()
        {
            // The same table on a scoop engine stays at the highest warp within 120% (7).
            Engine engine = new Engine();
            engine.FuelConsumption = new[] { 0, 0, 0, 0, 0, 0, 110, 150, 200, 300 };
            Fleet fleet = AddFleet(empire1, 1, 0, 0, Design(101, engine: engine, engineName: "Trans-Galactic Mizer Scoop"));

            Assert.AreEqual(7, PatrolStep.EfficientWarp(fleet));
        }

        [Test]
        public void Patrol_ChosenFleetOnTheOriginGivesNoIntercept_AndBlocksOthers()
        {
            AttackEveryone();
            Fleet patroller = Patroller(1, 0, 0, speed: 6, rangeIndex: 10);
            Fleet onTop = AddFleet(empire2, 20, 0, 0, Design(2));
            Fleet other = AddFleet(empire2, 21, 40, 0, Design(3));
            MarkSeen(empire1, onTop);
            MarkSeen(empire1, other);

            new PatrolStep().Process(serverData);

            Assert.AreEqual(1, patroller.Waypoints.Count);
        }

        // ------------------------------------------------------------------
        // Repeat Orders
        // ------------------------------------------------------------------

        [Test]
        public void RepeatOrders_ReachedWaypointsAreReAppendedWithTheirTasks_SoTheRouteCycles()
        {
            Star a = AddStar("Alpha", 0, 0);
            AddStar("Beta", 50, 0);
            Fleet fleet = AddFleet(empire1, 1, 0, 0, Design(1));
            fleet.InOrbit = a;
            fleet.RepeatOrders = true;

            Waypoint toBeta = PointAt(50, 0, 9, "Beta");
            toBeta.Task = new CargoTask { Mode = CargoMode.Unload };
            Waypoint toAlpha = PointAt(0, 0, 8, "Alpha");
            toAlpha.Task = new CargoTask { Mode = CargoMode.Load };
            fleet.Waypoints.Add(toBeta);
            fleet.Waypoints.Add(toAlpha);

            Generate();
            Assert.AreEqual(new NovaPoint(50, 0), fleet.Position);
            CollectionAssert.AreEqual(new[] { "Beta", "Alpha", "Beta" }, fleet.Waypoints.Select(w => w.Destination).ToArray());
            CargoTask recycled = fleet.Waypoints[2].Task as CargoTask;
            Assert.IsNotNull(recycled, "The recycled copy keeps its task");
            Assert.AreEqual(CargoMode.Unload, recycled.Mode);
            Assert.AreNotSame(toBeta.Task, recycled);
            Assert.AreEqual(9, fleet.Waypoints[2].WarpFactor, "and its leg warp");

            Generate();
            Assert.AreEqual(new NovaPoint(0, 0), fleet.Position);
            CollectionAssert.AreEqual(new[] { "Alpha", "Beta", "Alpha" }, fleet.Waypoints.Select(w => w.Destination).ToArray());
            Assert.IsInstanceOf<CargoTask>(fleet.Waypoints[1].Task);
            Assert.IsInstanceOf<CargoTask>(fleet.Waypoints[2].Task);
            Assert.AreEqual(8, fleet.Waypoints[2].WarpFactor);

            Generate();
            Assert.AreEqual(new NovaPoint(50, 0), fleet.Position);
            CollectionAssert.AreEqual(new[] { "Beta", "Alpha", "Beta" }, fleet.Waypoints.Select(w => w.Destination).ToArray());
        }

        [Test]
        public void RepeatOrders_Off_ConsumesTheRoute()
        {
            AddStar("Alpha", 0, 0);
            AddStar("Beta", 50, 0);
            Fleet fleet = AddFleet(empire1, 1, 0, 0, Design(1));
            fleet.Waypoints.Add(PointAt(50, 0, 9, "Beta"));
            fleet.Waypoints.Add(PointAt(0, 0, 9, "Alpha"));

            Generate();
            Generate();

            Assert.AreEqual(1, fleet.Waypoints.Count);
            Assert.AreEqual("Alpha", fleet.Waypoints[0].Destination);
        }

        [Test]
        public void RepeatOrders_TheLastWaypointLeftIsNotRecycled()
        {
            AddStar("Beta", 50, 0);
            Fleet fleet = AddFleet(empire1, 1, 0, 0, Design(1));
            fleet.RepeatOrders = true;
            fleet.Waypoints.Add(PointAt(50, 0, 9, "Beta"));

            Generate();

            Assert.AreEqual(1, fleet.Waypoints.Count);
            Assert.AreEqual("Beta", fleet.Waypoints[0].Destination);
        }

        [Test]
        public void RepeatOrders_AReachedNonPatrolInterceptLeg_IsRecycledWithItsTarget()
        {
            // A fleet-targeted waypoint without Patrol is recycled with its fleet target, so its
            // copy at the end keeps chasing that fleet (fleet-movement-scanning-cargo.md §5).
            Fleet target = AddFleet(empire2, 20, 50, 0, Design(2));
            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1, scanRange: 1000));
            pursuer.RepeatOrders = true;
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);
            pursuer.Waypoints.Add(PointAt(0, 0, 9));

            Generate();

            Assert.AreEqual(new NovaPoint(50, 0), pursuer.Position);
            Assert.AreEqual(3, pursuer.Waypoints.Count, "A non-Patrol intercept leg is re-appended");
            Waypoint tail = pursuer.Waypoints[2];
            Assert.IsTrue(tail.IsFleetTarget, "The recycled copy keeps chasing the fleet");
            Assert.AreEqual(target.Key, tail.TargetFleetKey);
        }

        [Test]
        public void RepeatOrders_AReachedPatrolInterceptLeg_IsNotRecycled()
        {
            Fleet target = AddFleet(empire2, 20, 50, 0, Design(2));
            Fleet pursuer = AddFleet(empire1, 10, 0, 0, Design(1, scanRange: 1000));
            pursuer.RepeatOrders = true;
            Waypoint leg = new Waypoint { WarpFactor = 9, Task = new PatrolTask() };
            leg.AimAtFleet(target);
            pursuer.Waypoints.Add(leg);
            pursuer.Waypoints.Add(PointAt(0, 0, 9));

            Generate();

            Assert.AreEqual(new NovaPoint(50, 0), pursuer.Position);
            Assert.AreEqual(2, pursuer.Waypoints.Count, "A pending Patrol intercept leg is never recycled");
        }

        [Test]
        public void RepeatOrdersCommand_SetsTheFlag_AndRoundTripsThroughXml()
        {
            Fleet fleet = AddFleet(empire1, 1, 0, 0, Design(1));
            RepeatOrdersCommand command = new RepeatOrdersCommand(fleet.Key, true);

            XmlDocument doc = new XmlDocument();
            RepeatOrdersCommand loaded = new RepeatOrdersCommand(command.ToXml(doc));
            Assert.AreEqual(fleet.Key, loaded.FleetKey);
            Assert.IsTrue(loaded.RepeatOrders);

            Assert.IsTrue(loaded.IsValid(empire1));
            Assert.IsFalse(loaded.IsValid(empire2));
            loaded.ApplyToState(empire1);
            Assert.IsTrue(fleet.RepeatOrders);
        }

        [Test]
        public void WaypointTargetFields_PatrolTask_AndRepeatOrders_RoundTripThroughXml()
        {
            Fleet target = AddFleet(empire2, 20, 50, 7, Design(2));
            Waypoint waypoint = new Waypoint { WarpFactor = 6, Task = new PatrolTask { Speed = 4, RangeIndex = 3 } };
            waypoint.AimAtFleet(target);
            waypoint.PursuitFrozen = true;

            XmlDocument doc = new XmlDocument();
            Waypoint loaded = new Waypoint(waypoint.ToXml(doc));

            Assert.AreEqual(WaypointTargetKind.Fleet, loaded.TargetKind);
            Assert.AreEqual(target.Key, loaded.TargetFleetKey);
            Assert.IsTrue(loaded.PursuitFrozen);
            Assert.AreEqual(new NovaPoint(50, 7), loaded.Position);
            PatrolTask patrol = loaded.Task as PatrolTask;
            Assert.IsNotNull(patrol);
            Assert.AreEqual(4, patrol.Speed);
            Assert.AreEqual(3, patrol.RangeIndex);

            Waypoint legacy = new Waypoint(PointAt(1, 2, 3).ToXml(doc));
            Assert.AreEqual(WaypointTargetKind.Unspecified, legacy.TargetKind);
            Assert.IsFalse(legacy.IsFleetTarget);
        }
    }
}
