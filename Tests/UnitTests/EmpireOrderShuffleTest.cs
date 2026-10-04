namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;

    // Regression tests for behavior-specs-7/ai-opponent-behavior.md §8 and turn-generation-engine.md
    // §1's confirmed per-turn Fisher-Yates shuffle of empire/player processing order in the
    // original game's master turn routine - previously this codebase always processed empires in
    // the same fixed (dictionary) order every turn, which meant any turn-processing pass sensitive
    // to "whose fleet is considered first" (e.g. RemoteMiningStep's per-star mining, see
    // RemoteMiningOrderFairnessTest) was systematically biased toward the same empire every game.
    [TestFixture]
    public class EmpireOrderShuffleTest
    {
        private static ServerData MakeServerStateWithEmpires(int count)
        {
            ServerData serverState = new ServerData();
            for (int i = 1; i <= count; i++)
            {
                serverState.AllEmpires.Add(i, new EmpireData { Id = (ushort)i });
            }

            return serverState;
        }

        [Test]
        public void ComputeShuffledEmpireOrder_ReturnsExactlyTheSameEmpires_JustReordered()
        {
            ServerData serverState = MakeServerStateWithEmpires(5);

            List<EmpireData> shuffled = serverState.ComputeShuffledEmpireOrder(new Random());

            CollectionAssert.AreEquivalent(serverState.AllEmpires.Values, shuffled);
        }

        [Test]
        public void ComputeShuffledEmpireOrder_ActuallyVariesAcrossCalls()
        {
            ServerData serverState = MakeServerStateWithEmpires(6);
            Random random = new Random(12345);

            List<ushort>[] orders = Enumerable.Range(0, 30)
                .Select(_ => serverState.ComputeShuffledEmpireOrder(random).Select(e => e.Id).ToList())
                .ToArray();

            bool anyDifferentFromFirst = orders.Skip(1).Any(order => !order.SequenceEqual(orders[0]));

            Assert.IsTrue(anyDifferentFromFirst,
                "Across 30 shuffles of 6 empires (720 possible orders), at least one should differ from the first - this must be a real shuffle, not a no-op.");
        }

        [Test]
        public void IterateAllFleetsInShuffledOrder_FollowsShuffledEmpireOrder_WhenSet()
        {
            ServerData serverState = MakeServerStateWithEmpires(2);
            EmpireData first = serverState.AllEmpires[1];
            EmpireData second = serverState.AllEmpires[2];

            Fleet fleetA = new Fleet(1) { Owner = 1 };
            Fleet fleetB = new Fleet(2) { Owner = 2 };
            first.OwnedFleets.Add(fleetA);
            second.OwnedFleets.Add(fleetB);

            serverState.ShuffledEmpireOrder = new List<EmpireData> { second, first };

            List<Fleet> order = serverState.IterateAllFleetsInShuffledOrder().ToList();

            Assert.AreEqual(new List<Fleet> { fleetB, fleetA }, order,
                "Should walk empires in ShuffledEmpireOrder, not AllEmpires' own dictionary order.");
        }

        [Test]
        public void IterateAllFleetsInShuffledOrder_FallsBackToDictionaryOrder_WhenNotSet()
        {
            ServerData serverState = MakeServerStateWithEmpires(2);
            Fleet fleetA = new Fleet(1) { Owner = 1 };
            Fleet fleetB = new Fleet(2) { Owner = 2 };
            serverState.AllEmpires[1].OwnedFleets.Add(fleetA);
            serverState.AllEmpires[2].OwnedFleets.Add(fleetB);

            Assert.IsNull(serverState.ShuffledEmpireOrder, "Sanity check - nothing has set a shuffle yet.");

            CollectionAssert.AreEqual(serverState.IterateAllFleets().ToList(), serverState.IterateAllFleetsInShuffledOrder().ToList());
        }
    }
}
