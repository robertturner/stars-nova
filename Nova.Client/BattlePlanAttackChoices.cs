namespace Nova.Client
{
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// The battle-plan editor's "Attack" choices (behavior-specs-10/race-designer-ui-and-
    /// availability.md, "Battle-plan editor"; combat-resolution.md): the four policy settings
    /// (BattlePlan.AttackOptions) followed by one entry per opponent for the fifth, specific-target
    /// category. "Opponent choices exclude the owner." Choosing an opponent stores the
    /// specific-player Attack value with that player's id in BattlePlan.TargetId, the pair the
    /// battle engine already reads (BattleEngine.IsLegitimateTarget).
    /// </summary>
    public static class BattlePlanAttackChoices
    {
        /// <summary>The Attack value of the specific-target category (equals BattleEngine.SpecificPlayerAttack, pinned by a test).</summary>
        public const string SpecificPlayer = "Specific Player";

        /// <summary>Every other empire the owner knows of, by id, with its race name (the owner excluded).</summary>
        public static IReadOnlyList<KeyValuePair<ushort, string>> Opponents(EmpireData owner)
        {
            return owner.EmpireReports.Values
                .Where(report => report.Id != owner.Id)
                .OrderBy(report => report.Id)
                .Select(report => new KeyValuePair<ushort, string>(report.Id, report.RaceName ?? ("Player " + report.Id)))
                .ToList();
        }

        /// <summary>The four policies, then one label per opponent.</summary>
        public static IReadOnlyList<string> Choices(IReadOnlyList<KeyValuePair<ushort, string>> opponents)
        {
            return BattlePlan.AttackOptions.Concat(opponents.Select(o => o.Value)).ToList();
        }

        /// <summary>The choice a plan currently shows: its policy, or its target's name.</summary>
        public static string Current(BattlePlan plan, IReadOnlyList<KeyValuePair<ushort, string>> opponents)
        {
            if (BattlePlan.AttackOptions.Contains(plan.Attack))
            {
                return plan.Attack;
            }

            foreach (KeyValuePair<ushort, string> opponent in opponents)
            {
                if (opponent.Key == plan.TargetId)
                {
                    return opponent.Value;
                }
            }

            return plan.Attack;
        }

        /// <summary>
        /// Stores a choice: a policy as itself (TargetId left as is - the engine consults it
        /// only for the specific-player setting), or an opponent as the specific-player setting
        /// with that opponent's id. Returns false for an unknown label.
        /// </summary>
        public static bool Apply(BattlePlan plan, string choice, IReadOnlyList<KeyValuePair<ushort, string>> opponents)
        {
            if (BattlePlan.AttackOptions.Contains(choice))
            {
                plan.Attack = choice;
                return true;
            }

            foreach (KeyValuePair<ushort, string> opponent in opponents)
            {
                if (opponent.Value == choice)
                {
                    plan.Attack = SpecificPlayer;
                    plan.TargetId = opponent.Key;
                    return true;
                }
            }

            return false;
        }
    }
}
