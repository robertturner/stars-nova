#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// Battle-plan list edits that must keep the fleets' plan assignments consistent
    /// (behavior-specs-10/client-interface.md "Battle Plans and Relations dialogs"):
    /// - the first record cannot be removed;
    /// - deleting a plan still assigned to at least one fleet asks for confirmation first
    ///   (declining changes nothing); accepting, or deleting an unused plan, removes the record and
    ///   walks every fleet so its assignment stays consistent with the shorter list "rather than
    ///   silently pointing at the wrong (shifted) plan".
    /// Nova keys plans by name, not by index, so there is no index shift to fix up; what can go
    /// stale is a fleet still naming the deleted (or renamed) plan, which the battle engine would
    /// fail to find.
    /// The spec (behavior-specs-11/client-interface.md, "Battle Plans and Relations dialogs", and
    /// ship-design-and-components.md section 3) says a fleet on the deleted plan gets "the plan
    /// just above it" - the plan immediately preceding it in the list. The first record can never
    /// be deleted, so one always exists.
    /// </summary>
    public static class BattlePlanRules
    {
        /// <summary>The fleets assigned to the named plan.</summary>
        public static List<Fleet> FleetsUsing(string planName, IEnumerable<Fleet> fleets)
        {
            if (fleets == null)
            {
                return new List<Fleet>();
            }

            return fleets.Where(fleet => fleet != null && fleet.BattlePlan == planName).ToList();
        }

        /// <summary>True when deleting the plan needs the Yes/No confirmation.</summary>
        public static bool DeleteNeedsConfirmation(string planName, IEnumerable<Fleet> fleets)
        {
            return FleetsUsing(planName, fleets).Count > 0;
        }

        /// <summary>True for any plan but the first record.</summary>
        public static bool CanDelete(IDictionary<string, BattlePlan> plans, string planName)
        {
            return plans != null
                && planName != null
                && plans.ContainsKey(planName)
                && plans.Keys.First() != planName;
        }

        /// <summary>
        /// The plan immediately above <paramref name="planName"/> in the list (the plan a deleted
        /// plan's fleets are reassigned to), or null when it is missing or is already the first.
        /// </summary>
        public static string PlanAbove(IDictionary<string, BattlePlan> plans, string planName)
        {
            if (plans == null || planName == null)
            {
                return null;
            }

            string above = null;
            foreach (string key in plans.Keys)
            {
                if (key == planName)
                {
                    return above;
                }

                above = key;
            }

            return null;
        }

        /// <summary>
        /// Removes the plan and moves every fleet that used it to the plan just above it (the
        /// preceding record; the protected first plan guarantees one exists). Returns the fleets
        /// moved (empty when nothing was deleted).
        /// </summary>
        public static List<Fleet> Delete(Dictionary<string, BattlePlan> plans, string planName, IEnumerable<Fleet> fleets)
        {
            if (!CanDelete(plans, planName))
            {
                return new List<Fleet>();
            }

            string above = PlanAbove(plans, planName);
            List<Fleet> moved = FleetsUsing(planName, fleets);
            foreach (Fleet fleet in moved)
            {
                fleet.BattlePlan = above;
            }

            plans.Remove(planName);
            return moved;
        }

        /// <summary>
        /// Renames a plan in place (the list order, and so the protected first record, is kept)
        /// and follows the rename on every fleet that used it. Returns null on success, or why the
        /// name was refused (accept-time validation, <see cref="RenameRules"/>).
        /// </summary>
        public static string Rename(Dictionary<string, BattlePlan> plans, string oldName, string newName, IEnumerable<Fleet> fleets)
        {
            if (plans == null || oldName == null || !plans.TryGetValue(oldName, out BattlePlan plan))
            {
                return "No such plan.";
            }

            string error = RenameRules.ValidateOnAccept(newName, plans.Keys.Where(name => name != oldName));
            if (error != null)
            {
                return error;
            }

            string accepted = RenameRules.Normalise(newName);
            if (accepted == oldName)
            {
                return null;
            }

            List<KeyValuePair<string, BattlePlan>> ordered = plans.ToList();
            plans.Clear();
            foreach (KeyValuePair<string, BattlePlan> entry in ordered)
            {
                if (entry.Key == oldName)
                {
                    plan.Name = accepted;
                    plans.Add(accepted, plan);
                }
                else
                {
                    plans.Add(entry.Key, entry.Value);
                }
            }

            foreach (Fleet fleet in FleetsUsing(oldName, fleets))
            {
                fleet.BattlePlan = accepted;
            }

            return null;
        }

        /// <summary>
        /// Points every fleet whose plan no longer exists at the first plan (a repair for state
        /// edited by an older client). Returns the fleets changed.
        /// </summary>
        public static List<Fleet> RepairAssignments(IDictionary<string, BattlePlan> plans, IEnumerable<Fleet> fleets)
        {
            List<Fleet> changed = new List<Fleet>();
            if (plans == null || plans.Count == 0 || fleets == null)
            {
                return changed;
            }

            string firstPlan = plans.Keys.First();
            foreach (Fleet fleet in fleets)
            {
                if (fleet != null && (fleet.BattlePlan == null || !plans.ContainsKey(fleet.BattlePlan)))
                {
                    fleet.BattlePlan = firstPlan;
                    changed.Add(fleet);
                }
            }

            return changed;
        }
    }
}
