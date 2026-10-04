namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Combat;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The merge and split plumbing uses the damage-word formulas of
    /// behavior-specs-11/combat-resolution.md §8 (rows 43 and 44): a merge recombines the two
    /// stacks' words, and splitting ships into a new fleet moves damaged ships first.
    /// </summary>
    [TestFixture]
    public class DamageWordMergeSplitTest
    {
        private const int Armor = 500;

        private EmpireData empire;

        [SetUp]
        public void Init()
        {
            empire = new EmpireData { Id = 1, Race = new Race() };
        }

        private static ShipDesign MakeDesign(long key)
        {
            Component blueprint = new Component { Name = "Hull", Mass = 100 };
            Hull hull = new Hull { ArmorStrength = Armor };
            hull.Modules = new List<HullModule>();
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Name = "Hull" };
            design.Update();
            return design;
        }

        private static Fleet MakeFleet(long key, ShipDesign design, int ships)
        {
            Fleet fleet = new Fleet(key) { Owner = 1, Name = "Fleet " + key };
            ShipToken token = new ShipToken(design, ships);
            fleet.Composition.Add(token.Key, token);
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(0, 0), Destination = "Space", WarpFactor = 0 });
            return fleet;
        }

        [Test]
        public void Merge_RecombinesTheDamageWords()
        {
            ShipDesign design = MakeDesign(1);
            Fleet left = MakeFleet(1, design, 10);
            Fleet right = MakeFleet(2, design, 10);
            DamageWord.Store(left.Composition[design.Key], new DamageWord(40, 100));
            DamageWord.Store(right.Composition[design.Key], new DamageWord(20, 200));
            empire.OwnedFleets.Add(right);

            new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), right.Key)
                .Perform(left, right, empire, empire);

            DamageWord merged = DamageWord.For(left.Composition[design.Key]);
            Assert.AreEqual(20, left.Composition[design.Key].Quantity);
            // A: 4 damaged x 100; B: 2 damaged x 200. D = 6, U = 800 over N = 20:
            // percent = ceil(600/20) = 30, figure = floor(800/6) = 133.
            Assert.AreEqual(30, merged.Percent);
            Assert.AreEqual(133, merged.Units);
        }

        [Test]
        public void Split_MovesDamagedShipsFirstAndRecombinesTheWords()
        {
            ShipDesign design = MakeDesign(1);
            Fleet source = MakeFleet(1, design, 10);
            DamageWord.Store(source.Composition[design.Key], new DamageWord(40, 100));
            empire.OwnedFleets.Add(source);

            // Keep 8 ships on the source, move 2 into the new fleet.
            Dictionary<long, ShipToken> left = new Dictionary<long, ShipToken>
            {
                { design.Key, new ShipToken(design, 8) },
            };
            new SplitMergeTask(left, new Dictionary<long, ShipToken>()).Perform(source, null, empire, empire);

            Fleet split = empire.TemporaryFleets.Single();
            DamageWord kept = DamageWord.For(source.Composition[design.Key]);
            DamageWord moved = DamageWord.For(split.Composition[design.Key]);

            Assert.AreEqual(8, source.Composition[design.Key].Quantity);
            Assert.AreEqual(2, split.Composition[design.Key].Quantity);

            // d_g = 4, so m = min(4, 2) = 2 damaged ships move into the 2-ship receiver:
            // it is 100% x 100/500. The source keeps 2 damaged of its 8: ceil(200/8) = 25.
            Assert.AreEqual(100, moved.Percent);
            Assert.AreEqual(100, moved.Units);
            Assert.AreEqual(25, kept.Percent);
            Assert.AreEqual(100, kept.Units);
        }
    }
}
