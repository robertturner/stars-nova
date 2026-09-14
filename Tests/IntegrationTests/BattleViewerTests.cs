#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars-Nova.
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

namespace Nova.Tests.IntegrationTests
{
    using System.Drawing;
    using System.Reflection;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.WinForms.Gui;

    using NUnit.Framework;

    /// <summary>
    /// Regression coverage for the BattleViewer replay refactor: opening and stepping/scrubbing
    /// through a battle report must never mutate the stored BattleReport, since the same report
    /// needs to be replayable from the start every time it's reopened. Runs a real BattleEngine
    /// simulation (same fixture pattern as BattleEngineTest) so the report contains a realistic
    /// mix of Movement/Target/Weapons/Destroy steps, not a hand-rolled fake.
    /// </summary>
    [TestFixture]
    public class BattleViewerTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static BattleReport RunSyntheticBattle()
        {
            const int player1Id = 1;
            const int player2Id = 2;

            ServerData serverState = new ServerData();
            BattleReport battle = new BattleReport();
            BattleEngine battleEngine = new BattleEngine(serverState, battle);

            Fleet fleet1 = new Fleet("fleet1", player1Id, 1, new Point(100, 200));
            Fleet fleet2 = new Fleet("fleet2", player2Id, 1, new Point(100, 200));

            Resources cost = new Resources(10, 20, 30, 40);

            EmpireData empireData1 = new EmpireData();
            EmpireData empireData2 = new EmpireData();
            empireData1.Id = 1;
            empireData2.Id = 2;
            empireData1.Race.Name = "Attacker";
            empireData2.Race.Name = "Defender";

            serverState.AllEmpires[empireData1.Id] = empireData1;
            serverState.AllEmpires[empireData2.Id] = empireData2;

            empireData1.BattlePlans["Default"] = new BattlePlan();
            empireData2.BattlePlans["Default"] = new BattlePlan();

            empireData1.EmpireReports.Add(2, new EmpireIntel(empireData2));
            empireData2.EmpireReports.Add(1, new EmpireIntel(empireData1));
            empireData1.EmpireReports[2].Relation = PlayerRelation.Enemy;
            empireData2.EmpireReports[1].Relation = PlayerRelation.Enemy;

            Component shipHull = new Component();
            Hull hull = new Hull();
            hull.FuelCapacity = 100;
            hull.Modules = new System.Collections.Generic.List<HullModule>();
            shipHull.Cost = cost;
            shipHull.Mass = 5000;
            shipHull.Properties.Add("Hull", hull);
            shipHull.Properties.Add("Battle Movement", new DoubleProperty(1.0));

            ShipDesign attackerDesign = new ShipDesign(111111) { Blueprint = shipHull, Name = "Attacker Cruiser" };
            ShipDesign defenderDesign = new ShipDesign(222222) { Blueprint = shipHull, Name = "Defender Frigate" };

            ShipToken attackerToken = new ShipToken(attackerDesign, 1) { Armor = 100 };
            ShipToken defenderToken = new ShipToken(defenderDesign, 1) { Armor = 50 };

            fleet1.Composition.Add(attackerToken.Key, attackerToken);
            fleet2.Composition.Add(defenderToken.Key, defenderToken);

            serverState.AllEmpires[player1Id].OwnedFleets.Add(fleet1);
            serverState.AllEmpires[player2Id].OwnedFleets.Add(fleet2);

            battleEngine.Run();

            return battle;
        }

        [Test]
        public void OpeningAndSteppingThroughBattleDoesNotMutateOriginalReport()
        {
            BattleReport battle = RunSyntheticBattle();

            Assert.Greater(battle.Steps.Count, 0, "Synthetic battle produced no steps - fixture setup didn't trigger combat.");

            var originalSnapshot = new System.Collections.Generic.Dictionary<long, (NovaPoint Position, double Armor, double Shields)>();
            foreach (var pair in battle.Stacks)
            {
                originalSnapshot[pair.Key] = (pair.Value.Position, pair.Value.Token.Armor, pair.Value.Token.Shields);
            }

            BattleViewer viewer = new BattleViewer(battle);
            MethodInfo goToStep = typeof(BattleViewer).GetMethod("GoToStep", PrivateInstance);
            Assert.NotNull(goToStep, "BattleViewer.GoToStep not found via reflection - has it been renamed?");

            // Step all the way to the end - if any step handler still mutates battle.Stacks
            // directly (the original bug), this will show up in the post-comparison below.
            goToStep.Invoke(viewer, new object[] { battle.Steps.Count - 1 });

            foreach (var pair in battle.Stacks)
            {
                var before = originalSnapshot[pair.Key];
                Assert.AreEqual(before.Position, pair.Value.Position, $"Stack {pair.Key:X} Position was mutated on the ORIGINAL report.");
                Assert.AreEqual(before.Armor, pair.Value.Token.Armor, $"Stack {pair.Key:X} Armor was mutated on the ORIGINAL report.");
                Assert.AreEqual(before.Shields, pair.Value.Token.Shields, $"Stack {pair.Key:X} Shields was mutated on the ORIGINAL report.");
            }

            FieldInfo myStacksField = typeof(BattleViewer).GetField("myStacks", PrivateInstance);

            // Jumping to a mid-battle position, forward from the end, must reproduce exactly the
            // same displayed state as jumping there directly - proof that GoToStep always
            // recomputes from scratch off the untouched original, rather than accumulating drift
            // from wherever the viewer last was (the class of bug a naive incremental "apply
            // deltas from current position" implementation would be prone to).
            int midpoint = battle.Steps.Count / 2;
            goToStep.Invoke(viewer, new object[] { midpoint });
            var midpointStateA = SnapshotDisplayedStacks(viewer, myStacksField);

            goToStep.Invoke(viewer, new object[] { battle.Steps.Count - 1 });
            goToStep.Invoke(viewer, new object[] { 0 });
            goToStep.Invoke(viewer, new object[] { midpoint });
            var midpointStateB = SnapshotDisplayedStacks(viewer, myStacksField);

            CollectionAssert.AreEquivalent(midpointStateA.Keys, midpointStateB.Keys, "Same stacks should be present both times at the same step position.");
            foreach (long key in midpointStateA.Keys)
            {
                Assert.AreEqual(midpointStateA[key].Position, midpointStateB[key].Position, $"Stack {key:X} position at step {midpoint} isn't deterministic across navigation order.");
                Assert.AreEqual(midpointStateA[key].Armor, midpointStateB[key].Armor, $"Stack {key:X} armor at step {midpoint} isn't deterministic across navigation order.");
                Assert.AreEqual(midpointStateA[key].Shields, midpointStateB[key].Shields, $"Stack {key:X} shields at step {midpoint} isn't deterministic across navigation order.");
            }
        }

        private static System.Collections.Generic.Dictionary<long, (NovaPoint Position, double Armor, double Shields)> SnapshotDisplayedStacks(BattleViewer viewer, FieldInfo myStacksField)
        {
            var myStacks = (System.Collections.Generic.Dictionary<long, Stack>)myStacksField.GetValue(viewer);
            var snapshot = new System.Collections.Generic.Dictionary<long, (NovaPoint Position, double Armor, double Shields)>();
            foreach (var pair in myStacks)
            {
                snapshot[pair.Key] = (pair.Value.Position, pair.Value.Token.Armor, pair.Value.Token.Shields);
            }

            return snapshot;
        }
    }
}
