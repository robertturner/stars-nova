namespace Nova.Tests.UnitTests
{
    using System.Drawing;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Covers BattleEngine.AreEnemies's handling of every BattlePlan.Attack policy value -
    /// docs/behavior-specs-3/combat-resolution.md confirms (via decompile of the exported
    /// client) exactly five "legitimate enemies" categories: none, every Enemy-relationship
    /// race, every Enemy-or-Neutral race, all races, or one specific race (via TargetId).
    /// Before this fix, "Enemies and Neutrals" - a selectable value in BattlePlan.AttackOptions
    /// since Phase 3 of this session's audit - was never checked in AreEnemies at all, so
    /// choosing it silently behaved exactly like "None" (attack nobody) instead of its intended
    /// meaning.
    /// </summary>
    [TestFixture]
    public class BattlePlanAttackPolicyTest
    {
        private ServerData serverState;
        private BattleEngine battleEngine;
        private Fleet wolf;
        private Fleet enemyLamb;
        private Fleet neutralLamb;
        private Fleet friendLamb;

        [SetUp]
        public void Init()
        {
            serverState = new ServerData();
            battleEngine = new BattleEngine(serverState, new BattleReport());

            EmpireData wolfEmpire = new EmpireData { Id = 1 };
            EmpireData enemyEmpire = new EmpireData { Id = 2 };
            EmpireData neutralEmpire = new EmpireData { Id = 3 };
            EmpireData friendEmpire = new EmpireData { Id = 4 };

            serverState.AllEmpires[wolfEmpire.Id] = wolfEmpire;
            serverState.AllEmpires[enemyEmpire.Id] = enemyEmpire;
            serverState.AllEmpires[neutralEmpire.Id] = neutralEmpire;
            serverState.AllEmpires[friendEmpire.Id] = friendEmpire;

            wolfEmpire.EmpireReports.Add(enemyEmpire.Id, new EmpireIntel(enemyEmpire) { Relation = PlayerRelation.Enemy });
            wolfEmpire.EmpireReports.Add(neutralEmpire.Id, new EmpireIntel(neutralEmpire) { Relation = PlayerRelation.Neutral });
            wolfEmpire.EmpireReports.Add(friendEmpire.Id, new EmpireIntel(friendEmpire) { Relation = PlayerRelation.Friend });

            wolf = new Fleet("wolf", wolfEmpire.Id, 1, new Point(0, 0));
            enemyLamb = new Fleet("enemyLamb", enemyEmpire.Id, 1, new Point(0, 0));
            neutralLamb = new Fleet("neutralLamb", neutralEmpire.Id, 1, new Point(0, 0));
            friendLamb = new Fleet("friendLamb", friendEmpire.Id, 1, new Point(0, 0));
        }

        private void SetWolfAttackPolicy(string attack)
        {
            var plan = new BattlePlan { Attack = attack };
            wolf.BattlePlan = "Default";
            serverState.AllEmpires[wolf.Owner].BattlePlans["Default"] = plan;
        }

        [Test]
        public void None_NeverEngagesAnyone()
        {
            SetWolfAttackPolicy("None");

            Assert.IsFalse(battleEngine.AreEnemies(wolf, enemyLamb));
            Assert.IsFalse(battleEngine.AreEnemies(wolf, neutralLamb));
            Assert.IsFalse(battleEngine.AreEnemies(wolf, friendLamb));
        }

        [Test]
        public void Enemies_OnlyEngagesEnemyRelation()
        {
            SetWolfAttackPolicy("Enemies");

            Assert.IsTrue(battleEngine.AreEnemies(wolf, enemyLamb));
            Assert.IsFalse(battleEngine.AreEnemies(wolf, neutralLamb));
            Assert.IsFalse(battleEngine.AreEnemies(wolf, friendLamb));
        }

        [Test]
        public void EnemiesAndNeutrals_EngagesBothButNotFriends()
        {
            SetWolfAttackPolicy("Enemies and Neutrals");

            Assert.IsTrue(battleEngine.AreEnemies(wolf, enemyLamb), "Enemy-relationship fleets should be engaged under 'Enemies and Neutrals'.");
            Assert.IsTrue(battleEngine.AreEnemies(wolf, neutralLamb), "Neutral-relationship fleets should be engaged under 'Enemies and Neutrals' - this was the missing case.");
            Assert.IsFalse(battleEngine.AreEnemies(wolf, friendLamb), "Friend-relationship fleets should never be engaged under 'Enemies and Neutrals'.");
        }

        [Test]
        public void Everyone_EngagesRegardlessOfRelation()
        {
            SetWolfAttackPolicy("Everyone");

            Assert.IsTrue(battleEngine.AreEnemies(wolf, enemyLamb));
            Assert.IsTrue(battleEngine.AreEnemies(wolf, neutralLamb));
            Assert.IsTrue(battleEngine.AreEnemies(wolf, friendLamb));
        }

        [Test]
        public void SpecificTargetId_EngagesOnlyThatEmpireRegardlessOfAttackString()
        {
            SetWolfAttackPolicy("None");
            serverState.AllEmpires[wolf.Owner].BattlePlans["Default"].TargetId = friendLamb.Owner;

            Assert.IsTrue(battleEngine.AreEnemies(wolf, friendLamb), "A specific TargetId match should engage that empire even under an otherwise passive Attack policy.");
            Assert.IsFalse(battleEngine.AreEnemies(wolf, enemyLamb));
        }
    }
}
