namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Covers the "swap adjacent waypoints via two Edit commands" mechanism behind
    /// FleetDetail's new Move Up/Down buttons - mirrors ProductionDialog.QueueUp_Click's own
    /// "swap payloads, don't move list items" idiom, generalized to waypoints instead of adding
    /// a new CommandMode. Exercises exactly the sequence FleetDetail.SwapWaypoints/
    /// CloneWaypointFully produce, without needing the full WinForms control.
    /// </summary>
    [TestFixture]
    public class FleetWaypointReorderTest
    {
        private EmpireData empire;
        private Fleet fleet;

        [SetUp]
        public void Init()
        {
            empire = new SimpleEmpireData();
            fleet = new Fleet(1);

            fleet.Waypoints.Add(MakeWaypoint("Home", 6, new NoTask()));
            fleet.Waypoints.Add(MakeWaypoint("A", 5, new ScrapTask()));
            fleet.Waypoints.Add(MakeWaypoint("B", 7, new ColoniseTask()));
            fleet.Waypoints.Add(MakeWaypoint("C", 4, new NoTask()));

            empire.OwnedFleets.Add(fleet);
        }

        private static Waypoint MakeWaypoint(string destination, int warp, IWaypointTask task)
        {
            Waypoint waypoint = new Waypoint();
            waypoint.Destination = destination;
            waypoint.WarpFactor = warp;
            waypoint.Task = task;
            return waypoint;
        }

        /// <summary>
        /// Reproduces FleetDetail.CloneWaypointFully exactly - Waypoint's own copy constructor
        /// deliberately drops Task, so it must be restored explicitly or a reorder would
        /// silently clear every waypoint's task assignment.
        /// </summary>
        private static Waypoint CloneWaypointFully(Waypoint source)
        {
            return new Waypoint(source) { Task = source.Task };
        }

        private void SwapWaypoints(int indexA, int indexB)
        {
            Waypoint waypointA = CloneWaypointFully(fleet.Waypoints[indexA]);
            Waypoint waypointB = CloneWaypointFully(fleet.Waypoints[indexB]);

            var editA = new WaypointCommand(CommandMode.Edit, waypointB, fleet.Key, indexA);
            Assert.IsTrue(editA.IsValid(empire));
            editA.ApplyToState(empire);

            var editB = new WaypointCommand(CommandMode.Edit, waypointA, fleet.Key, indexB);
            Assert.IsTrue(editB.IsValid(empire));
            editB.ApplyToState(empire);
        }

        [Test]
        public void SwapWaypoints_ExchangesDestinationWarpAndTask()
        {
            SwapWaypoints(1, 2);

            Assert.AreEqual("Home", fleet.Waypoints[0].Destination);
            Assert.AreEqual("B", fleet.Waypoints[1].Destination);
            Assert.AreEqual("A", fleet.Waypoints[2].Destination);
            Assert.AreEqual("C", fleet.Waypoints[3].Destination);

            Assert.AreEqual(7, fleet.Waypoints[1].WarpFactor);
            Assert.AreEqual(5, fleet.Waypoints[2].WarpFactor);

            Assert.IsInstanceOf<ColoniseTask>(fleet.Waypoints[1].Task);
            Assert.IsInstanceOf<ScrapTask>(fleet.Waypoints[2].Task);
        }

        [Test]
        public void SwapWaypoints_DoesNotDisturbOtherWaypoints()
        {
            SwapWaypoints(2, 3);

            Assert.AreEqual("Home", fleet.Waypoints[0].Destination);
            Assert.AreEqual("A", fleet.Waypoints[1].Destination);
            Assert.IsInstanceOf<ScrapTask>(fleet.Waypoints[1].Task);
        }

        [Test]
        public void SwapWaypoints_IsItsOwnInverse()
        {
            SwapWaypoints(1, 2);
            SwapWaypoints(1, 2);

            Assert.AreEqual("A", fleet.Waypoints[1].Destination);
            Assert.AreEqual("B", fleet.Waypoints[2].Destination);
            Assert.IsInstanceOf<ScrapTask>(fleet.Waypoints[1].Task);
            Assert.IsInstanceOf<ColoniseTask>(fleet.Waypoints[2].Task);
        }
    }
}
