namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    // Regression tests for behavior-specs-7/ship-design-and-components.md §14a's confirmed
    // "one-time, per-race, per-component random grant" mechanism: 12 named components (Multi
    // Cargo Pod, Langston Shell, Enigma Pulsar, etc.) were previously entirely absent from
    // components.xml, and the game had no mechanism to award them at all -
    // BattleEngine.GrantBattleTechGains's own comment explicitly disclosed it only grants a plain
    // research-field bump, never a specific component. These tests cover both halves of the fix:
    // the components now exist and are earnable via battle victory (BattleEngine), and a
    // race can never build one without having actually been granted it first (RaceComponents/
    // StarUpdateStep), regardless of tech level.
    [TestFixture]
    public class SpecialComponentGrantTest
    {
        private static Stack MakeSurvivingStack(EmpireData empire)
        {
            ShipDesign design = new ShipDesign(1) { Blueprint = new Component { Mass = 100 } };
            Hull hull = new Hull { Modules = new List<HullModule>() };
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();

            Fleet fleet = new Fleet("fleet-" + empire.Id, (ushort)empire.Id, 1, new Point(0, 0));
            ShipToken token = new ShipToken(design, 1) { Armor = 100 };
            fleet.Composition.Add(token.Key, token);
            return new Stack(fleet, 0, token);
        }

        [Test]
        public void GrantOneTimeSpecialComponent_EventuallyGrantsAllTenSalvageable_AndNeverRepeats()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverState.AllEmpires.Add(empire.Id, empire);
            BattleEngine battleEngine = new BattleEngine(serverState, new BattleReport());

            Stack survivor = MakeSurvivingStack(empire);
            List<Stack> battlingStacks = new List<Stack> { survivor };

            // ~50% chance per call; 300 calls makes "never even one success" astronomically
            // unlikely (0.5^300), and comfortably enough to exhaust all 12 possible grants too.
            for (int i = 0; i < 300; i++)
            {
                battleEngine.GrantOneTimeSpecialComponent(battlingStacks);
            }

            // behavior-specs-10: salvage never feeds Mini Morph (bit 8) or Genesis Device (bit 10).
            Assert.AreEqual(10, empire.GrantedSpecialComponents.Count,
                "300 qualifying battles should be more than enough to eventually be granted all ten salvageable parts.");
            CollectionAssert.AreEquivalent(SpecialComponentGrants.SalvageableComponents, empire.GrantedSpecialComponents);
        }

        [Test]
        public void GrantOneTimeSpecialComponent_PostsAMessage_WhenSomethingIsGranted()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            serverState.AllEmpires.Add(empire.Id, empire);
            BattleEngine battleEngine = new BattleEngine(serverState, new BattleReport());

            Stack survivor = MakeSurvivingStack(empire);
            List<Stack> battlingStacks = new List<Stack> { survivor };

            for (int i = 0; i < 20 && empire.GrantedSpecialComponents.Count == 0; i++)
            {
                battleEngine.GrantOneTimeSpecialComponent(battlingStacks);
            }

            Assert.IsNotEmpty(empire.GrantedSpecialComponents, "Sanity check - should have been granted something within 20 tries.");
            Assert.IsTrue(serverState.AllMessages.Any(m => m.Audience == 1 && m.Text.Contains("earned your race the plans")));
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
        public void GrantOneTimeSpecialComponent_DoesNotMakeAHighTechRewardImmediatelyBuildable_AtZeroTech()
        {
            // A freshly-created empire (zero research everywhere) can still be granted, say,
            // Genesis Device (a very high tech-level requirement) via a lucky early battle - the
            // grant itself should record the award, but the component must stay unbuildable
            // until tech level actually catches up (BattleEngine.GrantOneTimeSpecialComponent's
            // own immediate-availability check, and StarUpdateStep.TechLevelUp's gate on future
            // turns, both key off the same "granted AND tech-sufficient" condition).
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1, Race = new Race() };
            serverState.AllEmpires.Add(empire.Id, empire);
            BattleEngine battleEngine = new BattleEngine(serverState, new BattleReport());

            Stack survivor = MakeSurvivingStack(empire);
            List<Stack> battlingStacks = new List<Stack> { survivor };

            for (int i = 0; i < 300; i++)
            {
                battleEngine.GrantOneTimeSpecialComponent(battlingStacks);
            }

            // Jump Gate (Propulsion/Construction 20) replaces Genesis Device here: battle can no
            // longer grant Genesis Device at all (behavior-specs-10, only the Mystery Trader does).
            Assert.Contains("Jump Gate", empire.GrantedSpecialComponents.ToList(),
                "Sanity check - 300 tries should have granted all ten salvageable parts, including Jump Gate.");
            Assert.IsFalse(empire.AvailableComponents.Contains("Jump Gate"),
                "Granted doesn't mean buildable yet - this empire has zero tech levels, far short of Genesis Device's requirement.");
        }
    }
}
