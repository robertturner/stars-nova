namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Covers a real, confirmed-live crash: a "Merge With Fleet" waypoint order (OtherFleetKey
    /// != 0) whose target fleet can no longer be found at execution time (e.g. already merged/
    /// scrapped/destroyed elsewhere earlier the same turn) used to silently fall through to the
    /// split path below with none of the LeftComposition/RightComposition data a real split
    /// needs (InspectorViewModel.AddWaypoint passes empty dictionaries for a merge order, since
    /// the real merge path never reads them) - fabricating a genuine, permanent, empty-
    /// Composition fleet added straight to OwnedFleets. That fleet's Icon (Fleet.Icon returns
    /// null for an empty Composition rather than throwing) then blew up FleetIntel.ToXml's
    /// unguarded Icon.Source on save - the reported "crashed on End Turn, then crashes on every
    /// subsequent load" symptom. Perform should just no-op instead: see its own comment for why.
    /// </summary>
    [TestFixture]
    public class SplitMergeTaskMissingTargetTest
    {
        private static Fleet MakeFleet(long key, int shipCount = 2)
        {
            Fleet fleet = new Fleet(key);
            fleet.Owner = 1;

            ShipDesign design = new ShipDesign(key);
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.FuelCapacity = 100;
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);

            ShipToken token = new ShipToken(design, shipCount);
            fleet.Composition.Add(token.Key, token);
            fleet.FuelAvailable = shipCount * hull.FuelCapacity; // full tank - deterministic merge, no fuel-shortfall stranding (see FleetMergeFuelShortfallTest)

            return fleet;
        }

        [Test]
        public void MergeWithMissingTarget_DoesNotFabricateAnEmptyFleet()
        {
            Fleet fleet = MakeFleet(1);
            EmpireData empire = new SimpleEmpireData();

            const long missingTargetKey = 999;
            var task = new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), missingTargetKey);

            bool result = task.Perform(fleet, fleet, empire, empire);

            Assert.That(result, Is.True, "Perform should still report success - there's nothing invalid about the order, just nothing left to do.");
            Assert.That(empire.TemporaryFleets, Is.Empty, "No phantom fleet should be created when the merge target can't be found.");
            Assert.That(fleet.Composition.Values.Sum(t => t.Quantity), Is.EqualTo(2), "The original fleet must be untouched.");
        }

        [Test]
        public void MergeWithRealTarget_StillMerges()
        {
            Fleet left = MakeFleet(1);
            Fleet right = MakeFleet(2, shipCount: 3);
            EmpireData empire = new SimpleEmpireData();
            empire.OwnedFleets.Add(right);

            var task = new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), right.Key);
            task.Perform(left, right, empire, empire);

            Assert.That(left.Composition.Values.Sum(t => t.Quantity), Is.EqualTo(5), "Existing merge behavior must be unaffected by the missing-target fix.");
            Assert.That(empire.TemporaryFleets, Is.Empty, "A successful merge never touches TemporaryFleets.");
        }
    }
}
