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
    using System.Xml;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The client rules behind the fleet-orders editor (Nova.Client.WaypointOrders):
    /// - a map tap on a fleet or fleet report makes the waypoint pursue it
    ///   (behavior-specs-10/fleet-movement-scanning-cargo.md section 5, "Pursuit");
    /// - the Patrol task option, its speed (0 = efficient warp) and range ((index + 1) x 50 ly,
    ///   10 = 10,000 ly) labels and the PatrolTask it builds (section 5, Patrol "Complete rule").
    /// </summary>
    [TestFixture]
    public class WaypointOrdersTest
    {
        private const ushort Me = 1;
        private const ushort Them = 2;

        private static Fleet NewFleet(string name, ushort owner, uint id, int x, int y)
        {
            Fleet fleet = new Fleet(name, owner, id, new NovaPoint(x, y));
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position, Destination = "Space at " + fleet.Position, WarpFactor = 0 });
            return fleet;
        }

        private static Waypoint TapWaypoint(Mappable target)
        {
            return new Waypoint { Position = target.Position, Destination = "Space at " + target.Position, WarpFactor = 6 };
        }

        // ---------------- pursuit targeting ----------------

        [Test]
        public void TappingAnotherOwnFleet_AimsTheWaypointAtIt()
        {
            Fleet me = NewFleet("Scout", Me, 1, 0, 0);
            Fleet other = NewFleet("Hauler", Me, 2, 100, 50);
            Waypoint waypoint = TapWaypoint(other);

            Assert.IsTrue(WaypointOrders.AimAtTappedFleet(waypoint, other, me.Key));
            Assert.AreEqual(WaypointTargetKind.Fleet, waypoint.TargetKind);
            Assert.AreEqual(other.Key, waypoint.TargetFleetKey);
            Assert.IsTrue(waypoint.IsFleetTarget);
            Assert.AreEqual(other.Position, waypoint.Position);
            Assert.AreEqual("Hauler", waypoint.Destination);
        }

        [Test]
        public void TappingAForeignFleetReport_AimsTheWaypointAtIt()
        {
            Fleet me = NewFleet("Scout", Me, 1, 0, 0);
            Fleet enemy = NewFleet("Raider", Them, 7, 30, 40);
            FleetIntel report = new FleetIntel(enemy, ScanLevel.InScan, 2101);
            Waypoint waypoint = TapWaypoint(report);

            Assert.IsTrue(WaypointOrders.AimAtTappedFleet(waypoint, report, me.Key));
            Assert.AreEqual(WaypointTargetKind.Fleet, waypoint.TargetKind);
            Assert.AreEqual(enemy.Key, waypoint.TargetFleetKey);
            Assert.AreEqual(new NovaPoint(30, 40), waypoint.Position);
        }

        [Test]
        public void TappingAStarOrTheFleetItself_LeavesAFixedPoint()
        {
            Fleet me = NewFleet("Scout", Me, 1, 0, 0);
            Star star = new Star { Name = "Rigel", Position = new NovaPoint(10, 10) };

            Waypoint atStar = TapWaypoint(star);
            Assert.IsFalse(WaypointOrders.AimAtTappedFleet(atStar, star, me.Key));
            Assert.AreEqual(WaypointTargetKind.Unspecified, atStar.TargetKind);
            Assert.AreEqual(Global.None, atStar.TargetFleetKey);

            Waypoint atSelf = TapWaypoint(me);
            Assert.IsFalse(WaypointOrders.AimAtTappedFleet(atSelf, me, me.Key), "a fleet never pursues itself");
            Assert.IsFalse(atSelf.IsFleetTarget);
        }

        [Test]
        public void TappingAStarbaseReport_IsNotAPursuit()
        {
            Fleet me = NewFleet("Scout", Me, 1, 0, 0);
            FleetIntel starbase = new FleetIntel(NewFleet("Fort", Them, 9, 5, 5), ScanLevel.InScan, 2101);
            starbase.IsStarbase = true;

            Assert.IsNull(WaypointOrders.PursuitTargetOf(starbase, me.Key));
            Assert.IsFalse(WaypointOrders.AimAtTappedFleet(TapWaypoint(starbase), starbase, me.Key));
        }

        [Test]
        public void APursuitOrder_SurvivesTheOrdersFileRoundTrip()
        {
            Fleet me = NewFleet("Scout", Me, 1, 0, 0);
            Fleet enemy = NewFleet("Raider", Them, 7, 30, 40);
            Waypoint waypoint = TapWaypoint(enemy);
            WaypointOrders.AimAtTappedFleet(waypoint, enemy, me.Key);

            XmlDocument xmldoc = new XmlDocument();
            XmlElement element = new WaypointCommand(CommandMode.Add, waypoint, me.Key).ToXml(xmldoc);
            WaypointCommand loaded = new WaypointCommand(element);

            Assert.AreEqual(WaypointTargetKind.Fleet, loaded.Waypoint.TargetKind);
            Assert.AreEqual(enemy.Key, loaded.Waypoint.TargetFleetKey);
        }

        [Test]
        public void TargetNote_NamesTheChasedFleet_OnlyForPursuits()
        {
            Fleet me = NewFleet("Scout", Me, 1, 0, 0);
            Fleet enemy = NewFleet("Raider", Them, 7, 30, 40);
            Waypoint waypoint = TapWaypoint(enemy);

            Assert.AreEqual(string.Empty, WaypointOrders.TargetNote(waypoint));
            WaypointOrders.AimAtTappedFleet(waypoint, enemy, me.Key);
            Assert.AreEqual("chasing Raider", WaypointOrders.TargetNote(waypoint));
        }

        // ---------------- Patrol ----------------

        [Test]
        public void TaskOptions_IncludePatrol_AndBuildAPatrolTask()
        {
            CollectionAssert.Contains(WaypointOrders.TaskOptions, "Patrol");

            IWaypointTask task = WaypointOrders.BuildTask("Patrol", 7, 3);
            Assert.IsInstanceOf<PatrolTask>(task);
            Assert.AreEqual(7, ((PatrolTask)task).Speed);
            Assert.AreEqual(3, ((PatrolTask)task).RangeIndex);
            Assert.AreEqual(200, ((PatrolTask)task).RangeInLightYears);
            Assert.AreEqual("Patrol", task.Name, "the picker shows the task's own name back for a selected waypoint");
        }

        [Test]
        public void TaskOptions_BuildTheExistingTasks()
        {
            Assert.IsInstanceOf<ColoniseTask>(WaypointOrders.BuildTask("Colonise"));
            Assert.IsInstanceOf<ScrapTask>(WaypointOrders.BuildTask("Scrap"));
            Assert.IsInstanceOf<LayMinesTask>(WaypointOrders.BuildTask("Lay Mines"));
            Assert.IsInstanceOf<InvadeTask>(WaypointOrders.BuildTask("Invade"));
            Assert.IsInstanceOf<NoTask>(WaypointOrders.BuildTask("None"));
            Assert.IsInstanceOf<NoTask>(WaypointOrders.BuildTask("Merge With Fleet"), "merge is built by the caller");
        }

        [Test]
        public void PatrolTask_ChosenWithoutSettings_GetsSettingsZero()
        {
            // "Patrol and Transfer Fleet get settings 0" when the task is changed.
            PatrolTask task = (PatrolTask)WaypointOrders.BuildTask("Patrol");
            Assert.AreEqual(0, task.Speed);
            Assert.AreEqual(0, task.RangeIndex);
        }

        [Test]
        public void PatrolRangeLabels_AreFiftyLightYearSteps_ThenTenThousand()
        {
            Assert.AreEqual(11, WaypointOrders.PatrolRangeLabels.Count);
            Assert.AreEqual("50 ly", WaypointOrders.PatrolRangeLabels[0]);
            Assert.AreEqual("100 ly", WaypointOrders.PatrolRangeLabels[1]);
            Assert.AreEqual("500 ly", WaypointOrders.PatrolRangeLabels[9]);
            Assert.AreEqual("10,000 ly", WaypointOrders.PatrolRangeLabels[10]);
            Assert.AreEqual("10,000 ly", WaypointOrders.PatrolRangeLabel(42), "out-of-range indices clamp");
            Assert.AreEqual("50 ly", WaypointOrders.PatrolRangeLabel(-3));
        }

        [Test]
        public void PatrolSpeedLabels_ZeroIsAutomatic()
        {
            Assert.AreEqual(11, WaypointOrders.PatrolSpeedLabels.Count);
            Assert.AreEqual("Automatic", WaypointOrders.PatrolSpeedLabels[0]);
            Assert.AreEqual("Warp 1", WaypointOrders.PatrolSpeedLabels[1]);
            Assert.AreEqual("Warp 10", WaypointOrders.PatrolSpeedLabels[10]);
        }

        [Test]
        public void BuildPatrol_ClampsItsSettings()
        {
            PatrolTask task = WaypointOrders.BuildPatrol(12, 15);
            Assert.AreEqual(10, task.Speed);
            Assert.AreEqual(10, task.RangeIndex);
            Assert.AreEqual(10000, task.RangeInLightYears);
        }

        [Test]
        public void TaskDisplay_ShowsPatrolSettings()
        {
            Assert.AreEqual("Patrol (Automatic, 50 ly)", WaypointOrders.TaskDisplay(WaypointOrders.BuildPatrol(0, 0)));
            Assert.AreEqual("Patrol (Warp 6, 10,000 ly)", WaypointOrders.TaskDisplay(WaypointOrders.BuildPatrol(6, 10)));
            Assert.AreEqual("Scrap", WaypointOrders.TaskDisplay(new ScrapTask()));
            Assert.AreEqual("None", WaypointOrders.TaskDisplay(null));
        }
    }
}
