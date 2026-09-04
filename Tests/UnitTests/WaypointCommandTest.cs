namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Covers WaypointCommand's CommandMode.Insert, added to let a new waypoint be placed in
    /// the middle of an existing route (via Shift+Click on a selected waypoint) rather than
    /// always appending to the end - see StarMap.LeftShiftMouse.
    /// </summary>
    [TestFixture]
    public class WaypointCommandTest
    {
        private EmpireData empire;
        private Fleet fleet;

        [SetUp]
        public void Init()
        {
            empire = new SimpleEmpireData();
            fleet = new Fleet(1);

            fleet.Waypoints.Add(MakeWaypoint("A"));
            fleet.Waypoints.Add(MakeWaypoint("B"));
            fleet.Waypoints.Add(MakeWaypoint("C"));

            empire.OwnedFleets.Add(fleet);
        }

        private static Waypoint MakeWaypoint(string destination)
        {
            Waypoint waypoint = new Waypoint();
            waypoint.Destination = destination;
            return waypoint;
        }

        [Test]
        public void Insert_PlacesNewWaypointInTheMiddle()
        {
            WaypointCommand command = new WaypointCommand(CommandMode.Insert, MakeWaypoint("New"), fleet.Key, 1);

            Assert.IsTrue(command.IsValid(empire));
            command.ApplyToState(empire);

            Assert.AreEqual(4, fleet.Waypoints.Count);
            Assert.AreEqual("A", fleet.Waypoints[0].Destination);
            Assert.AreEqual("New", fleet.Waypoints[1].Destination);
            Assert.AreEqual("B", fleet.Waypoints[2].Destination);
            Assert.AreEqual("C", fleet.Waypoints[3].Destination);
        }

        [Test]
        public void Insert_AtCount_BehavesLikeAppend()
        {
            WaypointCommand command = new WaypointCommand(CommandMode.Insert, MakeWaypoint("New"), fleet.Key, fleet.Waypoints.Count);

            Assert.IsTrue(command.IsValid(empire));
            command.ApplyToState(empire);

            Assert.AreEqual(4, fleet.Waypoints.Count);
            Assert.AreEqual("New", fleet.Waypoints[3].Destination);
        }

        [Test]
        public void Insert_AtZero_PlacesNewWaypointFirst()
        {
            WaypointCommand command = new WaypointCommand(CommandMode.Insert, MakeWaypoint("New"), fleet.Key, 0);

            Assert.IsTrue(command.IsValid(empire));
            command.ApplyToState(empire);

            Assert.AreEqual("New", fleet.Waypoints[0].Destination);
            Assert.AreEqual("A", fleet.Waypoints[1].Destination);
        }

        [Test]
        public void IsValid_RejectsNegativeIndex()
        {
            WaypointCommand command = new WaypointCommand(CommandMode.Insert, MakeWaypoint("New"), fleet.Key, -1);

            Assert.IsFalse(command.IsValid(empire));
        }

        [Test]
        public void IsValid_RejectsIndexPastEndOfList()
        {
            WaypointCommand command = new WaypointCommand(
                CommandMode.Insert, MakeWaypoint("New"), fleet.Key, fleet.Waypoints.Count + 1);

            Assert.IsFalse(command.IsValid(empire));
        }

        [Test]
        public void Insert_DoesNotDisturbExistingAddBehavior()
        {
            // CommandMode.Add must keep ignoring Index and always appending - several existing
            // call sites (AI fleet orders, cargo dialog, etc.) rely on that.
            WaypointCommand command = new WaypointCommand(CommandMode.Add, MakeWaypoint("New"), fleet.Key, 0);

            command.ApplyToState(empire);

            Assert.AreEqual(4, fleet.Waypoints.Count);
            Assert.AreEqual("New", fleet.Waypoints[3].Destination);
        }
    }
}
