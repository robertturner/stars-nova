using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using Nova.Client;
using Nova.Common;
using Nova.Server;

namespace Nova.Tests.UnitTests
{
    /// <summary>
    /// The battle-plan editor's opponent selector for the specific-target Attack category
    /// (behavior-specs-10/race-designer-ui-and-availability.md, "Battle-plan editor": opponent
    /// choices exclude the owner), Nova.Client.BattlePlanAttackChoices, checked end to end against
    /// the battle engine's own target rule.
    /// </summary>
    [TestFixture]
    public class BattlePlanAttackChoicesTest
    {
        private static EmpireData Owner()
        {
            EmpireData owner = Empire(1, "Mine");
            owner.EmpireReports[1] = new EmpireIntel(owner);
            owner.EmpireReports[2] = new EmpireIntel(Empire(2, "Robotoids")) { Relation = PlayerRelation.Enemy };
            owner.EmpireReports[3] = new EmpireIntel(Empire(3, "Macinti")) { Relation = PlayerRelation.Friend };
            return owner;
        }

        private static EmpireData Empire(ushort id, string raceName)
        {
            return new EmpireData { Id = id, Race = new Race { Name = raceName } };
        }

        [Test]
        public void SpecificPlayerValue_IsTheOneTheBattleEngineReads()
        {
            Assert.AreEqual(BattleEngine.SpecificPlayerAttack, BattlePlanAttackChoices.SpecificPlayer);
        }

        [Test]
        public void Choices_AreThePoliciesThenEveryOpponent_OwnerExcluded()
        {
            IReadOnlyList<KeyValuePair<ushort, string>> opponents = BattlePlanAttackChoices.Opponents(Owner());

            CollectionAssert.AreEqual(new ushort[] { 2, 3 }, opponents.Select(o => o.Key).ToArray());
            CollectionAssert.AreEqual(
                new[] { "None", "Enemies", "Enemies and Neutrals", "Everyone", "Robotoids", "Macinti" },
                BattlePlanAttackChoices.Choices(opponents).ToArray());
        }

        [Test]
        public void ChoosingAnOpponent_StoresTheSpecificTarget_AndShowsItsName()
        {
            IReadOnlyList<KeyValuePair<ushort, string>> opponents = BattlePlanAttackChoices.Opponents(Owner());
            BattlePlan plan = new BattlePlan();

            Assert.IsTrue(BattlePlanAttackChoices.Apply(plan, "Macinti", opponents));
            Assert.AreEqual(BattlePlanAttackChoices.SpecificPlayer, plan.Attack);
            Assert.AreEqual(3, plan.TargetId);
            Assert.AreEqual("Macinti", BattlePlanAttackChoices.Current(plan, opponents));

            Assert.IsTrue(BattlePlanAttackChoices.Apply(plan, "Everyone", opponents));
            Assert.AreEqual("Everyone", plan.Attack);
            Assert.AreEqual("Everyone", BattlePlanAttackChoices.Current(plan, opponents));

            Assert.IsFalse(BattlePlanAttackChoices.Apply(plan, "Nobody we know", opponents));
            Assert.AreEqual("Everyone", plan.Attack);
        }

        [Test]
        public void AChosenOpponent_IsTheOnlyLegitimateTarget()
        {
            ServerData serverState = new ServerData();
            EmpireData owner = Owner();
            serverState.AllEmpires[1] = owner;
            BattlePlan plan = new BattlePlan { Name = "Default" };
            owner.BattlePlans["Default"] = plan;

            BattlePlanAttackChoices.Apply(plan, "Macinti", BattlePlanAttackChoices.Opponents(owner));

            Assert.IsTrue(BattleEngine.IsLegitimateTarget(serverState, 1, "Default", 3), "the chosen (Friend) opponent");
            Assert.IsFalse(BattleEngine.IsLegitimateTarget(serverState, 1, "Default", 2), "not even an Enemy otherwise");
        }
    }
}
