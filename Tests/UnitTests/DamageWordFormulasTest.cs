namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common.Combat;

    /// <summary>
    /// The damage-word formulas of behavior-specs-11/combat-resolution.md §8 ("The damage word
    /// outside battle"): the pooled-only conversion, the fleet-merge recombination, the
    /// move-between-fleets (split) rule and the new-ship join. These are the spec's own formulas,
    /// asserted independently of any fleet plumbing.
    /// </summary>
    [TestFixture]
    public class DamageWordFormulasTest
    {
        private static KeyValuePair<int, DamageWord> Source(int ships, DamageWord word)
        {
            return new KeyValuePair<int, DamageWord>(ships, word);
        }

        [Test]
        public void PooledConversion_IsTotalTimes500OverShipsTimesArmor_RoundedUp_AtLeast1_AtMost499()
        {
            // 500 damage over 2 ships of 1,000 armor: 500 x 500 / (2 x 1,000) = 125.
            DamageWord word = DamageWord.FromPooledDamage(500, 2, 1000);
            Assert.AreEqual(100, word.Percent);
            Assert.AreEqual(125, word.Units);

            // The single-division spec formula, not per-ship-then-units: 1 point over 3 ships of
            // 100 armor is ceil(500 / 300) = 2, where rounding the per-ship damage first gave 5.
            Assert.AreEqual(2, DamageWord.FromPooledDamage(1, 3, 100).Units);

            Assert.AreEqual(1, DamageWord.FromPooledDamage(1, 1, 100000).Units, "at least 1");
            Assert.AreEqual(DamageWord.MaxUnits, DamageWord.FromPooledDamage(10000, 1, 100).Units, "at most 499");
            Assert.IsTrue(DamageWord.FromPooledDamage(0, 3, 100).IsUndamaged);
        }

        [Test]
        public void Merge_SumsTheDamagedShipsAndUnits_RoundingThePercentageUp()
        {
            // A: 10 ships at 40% x 100/500 -> D = 4, U = 400. B: 5 ships at 20% x 200/500 ->
            // D = 1, U = 200. Over N = 15: D = 5, U = 600, percent = ceil(500/15) = 34,
            // figure = floor(600/5) = 120.
            DamageWord merged = DamageWord.Merge(
                15,
                new[] { Source(10, new DamageWord(40, 100)), Source(5, new DamageWord(20, 200)) });

            Assert.AreEqual(34, merged.Percent);
            Assert.AreEqual(120, merged.Units);
        }

        [Test]
        public void Merge_UsesAtLeastOneDamagedShipPerNonzeroSource()
        {
            // 3 ships at 10% x 50/500: 10 x 3 / 100 truncates to 0, so D = max(1, 0) = 1;
            // percent = ceil(100/3) = 34, figure = 50.
            DamageWord merged = DamageWord.Merge(3, new[] { Source(3, new DamageWord(10, 50)) });

            Assert.AreEqual(34, merged.Percent);
            Assert.AreEqual(50, merged.Units);
        }

        [Test]
        public void Merge_OfUndamagedStacks_IsZero()
        {
            DamageWord merged = DamageWord.Merge(
                20,
                new[] { Source(10, new DamageWord(0, 0)), Source(10, new DamageWord(0, 0)) });

            Assert.IsTrue(merged.IsUndamaged);
            Assert.AreEqual(0, merged.Packed);
        }

        [Test]
        public void Move_UndamagedGiver_DamagedReceiver_KeepsTheReceiversFigure()
        {
            DamageWord.MoveShips(
                new DamageWord(0, 0), 10,
                new DamageWord(40, 100), 10,
                moving: 5,
                out DamageWord giver, out DamageWord receiver);

            Assert.IsTrue(giver.IsUndamaged);
            Assert.AreEqual(27, receiver.Percent, "d_r = 4 over the 15-ship receiver, rounded up");
            Assert.AreEqual(100, receiver.Units);
        }

        [Test]
        public void Move_BothUndamaged_ClearsTheReceiver()
        {
            DamageWord.MoveShips(
                new DamageWord(0, 0), 10,
                new DamageWord(0, 0), 5,
                moving: 3,
                out DamageWord giver, out DamageWord receiver);

            Assert.IsTrue(giver.IsUndamaged);
            Assert.IsTrue(receiver.IsUndamaged);
        }

        [Test]
        public void Move_DamagedGiver_UndamagedReceiver_TakesTheGiversFigure()
        {
            // d_g = 4, d_r = 0, m = min(4, 2) = 2; the receiver's 7 ships take the giver's figure
            // at ceil(2 x 100 / 7) = 29. The giver keeps 2 damaged of its 8 -> ceil(200/8) = 25.
            DamageWord.MoveShips(
                new DamageWord(40, 100), 10,
                new DamageWord(0, 0), 5,
                moving: 2,
                out DamageWord giver, out DamageWord receiver);

            Assert.AreEqual(29, receiver.Percent);
            Assert.AreEqual(100, receiver.Units);
            Assert.AreEqual(25, giver.Percent);
            Assert.AreEqual(100, giver.Units);
        }

        [Test]
        public void Move_BothDamaged_DividesByTheReceiversWholeNewCount()
        {
            // d_g = 4, d_r = 2, m = 3. Receiver new count 13: figure = ceil((200 x 2 + 100 x 3) / 13)
            // = ceil(700/13) = 54; percent = ceil((2 + 3) x 100 / 13) = 39. The giver keeps 1 damaged
            // of 7: ceil(100/7) = 15.
            DamageWord.MoveShips(
                new DamageWord(40, 100), 10,
                new DamageWord(20, 200), 10,
                moving: 3,
                out DamageWord giver, out DamageWord receiver);

            Assert.AreEqual(39, receiver.Percent);
            Assert.AreEqual(54, receiver.Units);
            Assert.AreEqual(15, giver.Percent);
            Assert.AreEqual(100, giver.Units);
        }

        [Test]
        public void Move_WhenAllDamagedShipsLeave_ClearsTheGiver()
        {
            DamageWord.MoveShips(
                new DamageWord(40, 100), 10,
                new DamageWord(0, 0), 0,
                moving: 4,
                out DamageWord giver, out DamageWord receiver);

            Assert.IsTrue(giver.IsUndamaged, "m = d_g = 4, so the giver's word is cleared");
            Assert.AreEqual(100, receiver.Percent, "all 4 moved ships are damaged: ceil(400/4)");
            Assert.AreEqual(100, receiver.Units);
        }

        [Test]
        public void JoinNewShips_SpreadsTheExistingDamageOverTheWholeStack()
        {
            // 10 ships at 40% x 250/500 (4 damaged, 1,000 damage points) plus 10 undamaged ships:
            // D = 4, T = (250 x 500 / 10) x 4 / 50 = 1,000, percent = 4 x 100 / 20 = 20,
            // D' = 4, figure = (1,000 x 5 / 4) x 100 / 500 = 250.
            DamageWord joined = DamageWord.JoinNewShips(new DamageWord(40, 250), 10, 10, 500);

            Assert.AreEqual(20, joined.Percent);
            Assert.AreEqual(250, joined.Units);
            Assert.AreEqual(1000, joined.TotalDamage(20, 500), "the damage is conserved");
        }

        [Test]
        public void JoinNewShips_IntoAnUndamagedStack_StaysUndamaged()
        {
            DamageWord joined = DamageWord.JoinNewShips(new DamageWord(0, 0), 10, 5, 500);

            Assert.IsTrue(joined.IsUndamaged);
        }
    }
}
