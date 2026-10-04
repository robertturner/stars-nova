namespace Nova.Tests.UnitTests
{
    using System;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;

    // Regression tests for behavior-specs-11/turn-generation-engine.md §5's shared salvage
    // dispatcher: the 13-entry rare-part table (bits 0-7, 9 and 11 salvage-fed) is tried first,
    // then the six research fields; a race gets at most one success per generation. The 12 named
    // special components were previously absent from components.xml and are earned only through
    // this dispatcher (or the Mystery Trader), never from tech alone.
    [TestFixture]
    public class SpecialComponentGrantTest
    {
        private static readonly int[] SalvageBits = { 0, 1, 2, 3, 4, 5, 6, 7, 9, 11 };

        /// <summary>A table with every salvageable rare part at the 25% cap.</summary>
        private static SalvageTables FullRareTable()
        {
            SalvageTables tables = new SalvageTables();
            foreach (int bit in SalvageBits)
            {
                tables.RarePercent[bit] = 25;
            }

            return tables;
        }

        [Test]
        public void TryGain_EventuallyGrantsAllTenSalvageable_AndNeverRepeats()
        {
            EmpireData empire = new EmpireData { Id = 1, Race = new Race() };
            SalvageTables tables = FullRareTable();
            Random random = new Random(1234);

            for (int i = 0; i < 1000 && empire.GrantedSpecialComponents.Count < SalvageBits.Length; i++)
            {
                empire.TechGainedThisTurn = false; // one fresh generation per call
                SalvageDispatcher.TryGain(empire, tables, random);
            }

            Assert.AreEqual(10, empire.GrantedSpecialComponents.Count,
                "repeated qualifying battles should eventually grant all ten salvageable parts exactly once each.");
            CollectionAssert.AreEquivalent(SpecialComponentGrants.SalvageableComponents, empire.GrantedSpecialComponents);
            Assert.IsFalse(empire.GrantedSpecialComponents.Contains("Mini Morph"));
            Assert.IsFalse(empire.GrantedSpecialComponents.Contains("Genesis Device"));
        }

        [Test]
        public void TryGain_ReportsTheGainedPart()
        {
            EmpireData empire = new EmpireData { Id = 1, Race = new Race() };
            SalvageTables tables = FullRareTable();
            Random random = new Random(7);

            SalvageResult result = null;
            for (int i = 0; i < 200 && result == null; i++)
            {
                empire.TechGainedThisTurn = false;
                result = SalvageDispatcher.TryGain(empire, tables, random);
            }

            Assert.IsNotNull(result, "sanity check - a part should have been granted within 200 tries");
            Assert.IsNotNull(result.PartName);
            Assert.IsTrue(empire.GrantedSpecialComponents.Contains(result.PartName));
        }

        [Test]
        public void RaceComponents_NeverIncludesASpecialGrant_EvenAtMaxTech_UnlessGranted()
        {
            Race race = new Race();
            TechLevel maxTech = new TechLevel(26);

            RaceComponents components = new RaceComponents(race, maxTech);

            foreach (string specialName in SpecialComponentGrants.Components)
            {
                Assert.IsFalse(components.Contains(specialName),
                    $"{specialName} must not be available from tech alone - it requires a battle grant, per behavior-specs-7's confirmed mechanism.");
            }
        }

        [Test]
        public void TryGain_DoesNotMakeAHighTechRewardImmediatelyBuildable_AtZeroTech()
        {
            // A freshly-created empire (zero research everywhere) can still be granted a very
            // high-tech part; the grant records the award, but the component stays unbuildable
            // until tech catches up (StarUpdateStep.TechLevelUp's gate).
            EmpireData empire = new EmpireData { Id = 1, Race = new Race() };
            SalvageTables tables = new SalvageTables();
            tables.RarePercent[11] = 25; // Jump Gate (Propulsion/Construction 20)
            Random random = new Random(11);

            for (int i = 0; i < 1000 && !empire.GrantedSpecialComponents.Contains("Jump Gate"); i++)
            {
                empire.TechGainedThisTurn = false;
                SalvageDispatcher.TryGain(empire, tables, random);
            }

            Assert.Contains("Jump Gate", empire.GrantedSpecialComponents.ToList(),
                "sanity check - should have been granted within 1000 tries.");
            Assert.IsFalse(empire.AvailableComponents.Contains("Jump Gate"),
                "granted does not mean buildable yet - this empire has zero tech levels");
        }
    }
}
