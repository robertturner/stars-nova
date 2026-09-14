#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010, 2011 The Stars-Nova Project
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

namespace Nova.WinForms.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows.Forms;

    using Nova.Common;

    /// <Summary>
    /// Dialog to manage battle plans. Ports race-designer-ui-and-availability.md's "Battle-plan
    /// editor" section: an ordered list of per-owner plan records, a working copy of the active
    /// record committed on record-switch/create/delete/accept, the first ("Default") plan
    /// protected from deletion, creating a plan copies the active one as a template with an
    /// auto-incremented name, and a MaxBattlePlans limit. This dialog used to be entirely
    /// display-only (New/Modify were disabled with no handlers, planList had no
    /// SelectedIndexChanged) - see docs/PROJECT-STATUS.md for the audit that found this.
    ///
    /// Not implemented: the spec's "opponent choices exclude the owner" / "constrained game mode
    /// substitutes a fixed opponent policy" - BattlePlan has no per-opponent target list at all
    /// (Attack is a general policy string: Enemies/Enemies and Neutrals/Everyone, not a per-empire
    /// picker), and no game-mode flag exists anywhere in this fork to gate such a thing even if it
    /// did. There's nothing here for that behavior to attach to.
    /// </Summary>
    public partial class BattlePlans : Form
    {
        private readonly Dictionary<string, BattlePlan> battlePlans;
        private BattlePlan workingCopy;
        private bool isDirty;
        private bool loadingSelection;

        /// <Summary>
        /// Initializes a new instance of the BattlePlans class.
        /// </Summary>
        public BattlePlans(Dictionary<string, BattlePlan> battlePlans)
        {
            this.battlePlans = battlePlans;

            InitializeComponent();

            primaryTarget.Items.AddRange(BattlePlan.TargetOptions);
            secondaryTarget.Items.AddRange(BattlePlan.TargetOptions);
            tactic.Items.AddRange(BattlePlan.TacticOptions);
            attack.Items.AddRange(BattlePlan.AttackOptions);

            foreach (BattlePlan plan in battlePlans.Values)
            {
                planList.Items.Add(plan.Name);
            }

            planList.SelectedIndexChanged += PlanList_SelectedIndexChanged;
            planName.TextChanged += FieldChanged;
            primaryTarget.SelectedIndexChanged += FieldChanged;
            secondaryTarget.SelectedIndexChanged += FieldChanged;
            tactic.SelectedIndexChanged += FieldChanged;
            attack.SelectedIndexChanged += FieldChanged;
            newPlan.Click += NewPlan_Click;
            modifyPlan.Click += ModifyPlan_Click;
            deletePlan.Click += DeletePlan_Click;

            planList.SelectedIndex = 0;
            UpdateButtonStates();
        }

        /// ----------------------------------------------------------------------------
        /// <Summary>
        /// Selecting a different plan commits any pending edits to the previously active
        /// record, then loads the newly selected one.
        /// </Summary>
        /// ----------------------------------------------------------------------------
        private void PlanList_SelectedIndexChanged(object sender, EventArgs e)
        {
            CommitWorkingCopy();
            LoadSelectedPlan();
        }

        private void LoadSelectedPlan()
        {
            string selection = planList.SelectedItem as string;
            if (selection == null)
            {
                // ListBox.Items.RemoveAt on the currently-selected row transiently fires
                // SelectedIndexChanged with no selection before DeletePlan_Click's own explicit
                // re-selection lands - nothing to load yet, that follow-up event will do it.
                workingCopy = null;
                return;
            }

            loadingSelection = true;
            workingCopy = battlePlans[selection];

            planName.Text = workingCopy.Name;
            primaryTarget.SelectedItem = workingCopy.PrimaryTarget;
            secondaryTarget.SelectedItem = workingCopy.SecondaryTarget;
            tactic.SelectedItem = workingCopy.Tactic;
            attack.SelectedItem = workingCopy.Attack;

            isDirty = false;
            loadingSelection = false;
            UpdateButtonStates();
        }

        private void FieldChanged(object sender, EventArgs e)
        {
            if (!loadingSelection)
            {
                isDirty = true;
            }
        }

        private void UpdateButtonStates()
        {
            // The first (Default) plan is protected from deletion.
            deletePlan.Enabled = planList.SelectedIndex > 0;
            newPlan.Enabled = battlePlans.Count < Global.MaxBattlePlans;
        }

        /// ----------------------------------------------------------------------------
        /// <Summary>
        /// Writes the working copy's edited fields back into the plan dictionary. A no-op
        /// unless something was actually changed since the plan was loaded.
        /// </Summary>
        /// ----------------------------------------------------------------------------
        private void CommitWorkingCopy()
        {
            if (workingCopy == null || !isDirty)
            {
                return;
            }

            string oldName = workingCopy.Name;
            string newName = string.IsNullOrWhiteSpace(planName.Text) ? oldName : planName.Text;

            workingCopy.Name = newName;
            workingCopy.PrimaryTarget = primaryTarget.SelectedItem as string ?? workingCopy.PrimaryTarget;
            workingCopy.SecondaryTarget = secondaryTarget.SelectedItem as string ?? workingCopy.SecondaryTarget;
            workingCopy.Tactic = tactic.SelectedItem as string ?? workingCopy.Tactic;
            workingCopy.Attack = attack.SelectedItem as string ?? workingCopy.Attack;

            if (newName != oldName && !battlePlans.ContainsKey(newName))
            {
                battlePlans.Remove(oldName);
                battlePlans[newName] = workingCopy;

                int index = planList.Items.IndexOf(oldName);
                if (index >= 0)
                {
                    planList.Items[index] = newName;
                }
            }

            isDirty = false;
        }

        /// ----------------------------------------------------------------------------
        /// <Summary>
        /// Explicit "Apply" button - the four combos and the name box already live-edit the
        /// working copy, so this just forces an immediate commit without needing to switch
        /// records first, giving the user visible confirmation an edit stuck.
        /// </Summary>
        /// ----------------------------------------------------------------------------
        private void ModifyPlan_Click(object sender, EventArgs e)
        {
            CommitWorkingCopy();
        }

        /// ----------------------------------------------------------------------------
        /// <Summary>
        /// Creates a new plan as a copy of the active one, with an auto-incremented name, and
        /// makes it the active record.
        /// </Summary>
        /// ----------------------------------------------------------------------------
        private void NewPlan_Click(object sender, EventArgs e)
        {
            CommitWorkingCopy();

            if (battlePlans.Count >= Global.MaxBattlePlans)
            {
                return;
            }

            BattlePlan template = workingCopy ?? battlePlans.Values.First();
            BattlePlan copy = new BattlePlan
            {
                Name = NextPlanName(template.Name),
                PrimaryTarget = template.PrimaryTarget,
                SecondaryTarget = template.SecondaryTarget,
                Tactic = template.Tactic,
                Attack = template.Attack,
            };

            battlePlans[copy.Name] = copy;
            planList.Items.Add(copy.Name);
            planList.SelectedItem = copy.Name;
        }

        /// ----------------------------------------------------------------------------
        /// <Summary>
        /// Removes the active plan, unless it's the protected first (Default) plan.
        /// </Summary>
        /// ----------------------------------------------------------------------------
        private void DeletePlan_Click(object sender, EventArgs e)
        {
            if (planList.SelectedIndex <= 0)
            {
                return;
            }

            string name = planList.SelectedItem as string;
            int index = planList.SelectedIndex;

            battlePlans.Remove(name);
            workingCopy = null;
            isDirty = false;
            planList.Items.RemoveAt(index);
            planList.SelectedIndex = Math.Max(0, index - 1);
        }

        /// ----------------------------------------------------------------------------
        /// <Summary>
        /// If the name ends in a recognized one-character numeric suffix enclosed by
        /// parentheses (e.g. "Plan(3)"), increments that suffix with wraparound after nine;
        /// otherwise appends a generic default suffix. Naming convenience only - does not
        /// affect plan behavior.
        /// </Summary>
        /// ----------------------------------------------------------------------------
        private string NextPlanName(string baseName)
        {
            string candidate;

            if (baseName.Length >= 3 && baseName[baseName.Length - 1] == ')' &&
                baseName[baseName.Length - 3] == '(' && char.IsDigit(baseName[baseName.Length - 2]))
            {
                int digit = baseName[baseName.Length - 2] - '0';
                int next = (digit + 1) % 10;
                candidate = baseName.Substring(0, baseName.Length - 2) + next + ")";
            }
            else
            {
                candidate = baseName + "(1)";
            }

            // Guard against an accidental collision even after incrementing/appending.
            int suffix = 2;
            string unique = candidate;
            while (battlePlans.ContainsKey(unique))
            {
                unique = candidate + suffix;
                suffix++;
            }

            return unique;
        }

        /// ----------------------------------------------------------------------------
        /// <Summary>
        /// Done button pressed.
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        /// ----------------------------------------------------------------------------
        private void DoneButton_Click(object sender, EventArgs e)
        {
            CommitWorkingCopy();
            Close();
        }
    }
}
