#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
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

namespace Nova.WinForms.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Text;
    using System.Windows.Forms;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <Summary>
    /// Dialog for viewing battle progress and outcome. Ports client-ui-dialog-catalog.md's
    /// "Event replay" surface: "a current playback position and transport controls... moving
    /// playback position changes the displayed event state only... cannot change the saved game
    /// state or event recording."
    ///
    /// The original stepped only forward and mutated `theBattle.Stacks` directly (despite its own
    /// constructor comment saying it deep-copies into `myStacks` "so we don't disturb the master
    /// copy") - a real bug, since every step handler actually read/wrote `theBattle.Stacks`, not
    /// the copy; opening a battle report and stepping through it permanently mutated the stored
    /// report. Fixed here by making every step a pure function of (state, step), and deriving the
    /// display at any position by folding the full step list from scratch on a fresh clone of the
    /// ORIGINAL stacks each time - see GoToStep/ApplyStep.
    /// </Summary>
    public partial class BattleViewer : Form
    {
        private readonly BattleReport theBattle;
        private Dictionary<long, Stack> myStacks = new Dictionary<long, Stack>();
        private int eventCount;
        private bool playing;

        /// <Summary>
        /// Initializes a new instance of the BattleViewer class.
        /// </Summary>
        /// <param name="report">The <see cref="BattleReport"/> to be displayed.</param>
        public BattleViewer(BattleReport report)
        {
            InitializeComponent();
            theBattle = report;
            eventCount = 0;
        }

        /// <Summary>
        /// Initialisation performed on a load of the whole dialog.
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void OnLoad(object sender, EventArgs e)
        {
            battleLocation.Text = theBattle.Location;

            battlePanel.BackgroundImage = Nova.Properties.Resources.Plasma;
            battlePanel.BackgroundImageLayout = ImageLayout.Stretch;

            stepPosition.Minimum = 0;
            stepPosition.Maximum = Math.Max(0, theBattle.Steps.Count - 1);

            GoToStep(0);
        }

        /// <Summary>
        /// Draw the battle panel by placing the images for the stacks in the
        /// appropriate position.
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void OnPaint(object sender, PaintEventArgs e)
        {
            base.OnPaint(e); // added

            Graphics graphics = e.Graphics;
            Size panelSize = battlePanel.Size;
            float scalingFactor = (float)panelSize.Width /
                                     (float)Global.MaxWeaponRange;

            graphics.PageScale = scalingFactor;
            graphics.ScaleTransform(0.5F, 0.5F);

            foreach (Stack stack in myStacks.Values)
            {
                graphics.DrawImage((Image)stack.Icon.Image, (Point)stack.Position);
            }
        }

        /// <Summary>
        /// Recomputes the entire display state for the given step position from scratch: clone
        /// the ORIGINAL stacks fresh, then fold every step from 0 up to and including this
        /// position through ApplyStep. This is the "jump to any position" primitive that both
        /// Next/Previous and the scrub bar are built on - a battle's step count is realistically
        /// tens (capped by BattleEngine's maxBattleRounds), so re-folding from scratch on every
        /// move is cheap, and it means no per-step-type inverse/undo logic is ever needed.
        /// </Summary>
        private void GoToStep(int position)
        {
            if (theBattle.Steps.Count == 0)
            {
                return;
            }

            position = Math.Max(0, Math.Min(position, theBattle.Steps.Count - 1));
            eventCount = position;

            // Not solely reliant on OnLoad having already run - GoToStep is the one place that
            // actually needs stepPosition.Maximum to be correct, so it sets it itself.
            stepPosition.Maximum = Math.Max(0, theBattle.Steps.Count - 1);

            myStacks = CloneOriginalStacks();

            for (int i = 0; i < position; i++)
            {
                ApplyStep(theBattle.Steps[i], myStacks);
            }

            // The step currently being displayed gets its "before" state captured (for
            // Movement's from/to fields) immediately before it's applied, matching what the
            // detail panel is meant to show for that one step specifically.
            BattleStep currentStep = theBattle.Steps[position];
            NovaPoint? movedFromPosition = null;
            if (currentStep is BattleStepMovement movement &&
                myStacks.TryGetValue(movement.StackKey, out Stack stackBeforeMove))
            {
                movedFromPosition = stackBeforeMove.Position;
            }

            ApplyStep(currentStep, myStacks);
            ShowStepDetails(currentStep, movedFromPosition);
            SetStepNumber(currentStep);

            stepPosition.Value = position;
            previousStep.Enabled = position > 0;
            nextStep.Enabled = position < theBattle.Steps.Count - 1;

            battlePanel.Invalidate();
        }

        private Dictionary<long, Stack> CloneOriginalStacks()
        {
            var clone = new Dictionary<long, Stack>();
            foreach (Stack stack in theBattle.Stacks.Values)
            {
                clone[stack.Key] = new Stack(stack);
            }

            return clone;
        }

        /// <Summary>
        /// Pure state transition - mutates only the passed-in dictionary, never
        /// `theBattle.Stacks`. BattleStepTarget carries no state change of its own (targeting
        /// info only), so it's a no-op here; its fields are read directly by ShowStepDetails.
        /// </Summary>
        private static void ApplyStep(BattleStep step, Dictionary<long, Stack> state)
        {
            switch (step)
            {
                case BattleStepMovement movement:
                    if (state.TryGetValue(movement.StackKey, out Stack movedStack))
                    {
                        movedStack.Position = movement.Position;
                    }

                    break;

                case BattleStepWeapons weapons:
                    if (state.TryGetValue(weapons.WeaponTarget.TargetKey, out Stack lamb))
                    {
                        if (weapons.Targeting == BattleStepWeapons.TokenDefence.Shields)
                        {
                            lamb.Token.Shields -= weapons.Damage;
                        }
                        else
                        {
                            lamb.Token.Armor -= weapons.Damage;
                        }
                    }

                    break;

                case BattleStepDestroy destroy:
                    state.Remove(destroy.StackKey);
                    break;
            }
        }

        /// <Summary>
        /// Updates the detail panel labels for whichever step is now current - reads from
        /// `myStacks` (the state already folded up to and including this step) so target
        /// shields/armor reflect the value AFTER this step's effect, matching the original's own
        /// "apply then show" behavior for weapons fire.
        /// </Summary>
        private void ShowStepDetails(BattleStep step, NovaPoint? movedFromPosition)
        {
            ClearMovementDetails();
            ClearTargetDetails();
            ClearWeapons();

            switch (step)
            {
                case BattleStepMovement movement:
                    myStacks.TryGetValue(movement.StackKey, out Stack movedStack);
                    UpdateStackDetails(movedStack);
                    movedFrom.Text = movedFromPosition?.ToString() ?? "";
                    movedTo.Text = movement.Position.ToString();
                    break;

                case BattleStepTarget target:
                    myStacks.TryGetValue(target.TargetKey, out Stack lambTarget);
                    myStacks.TryGetValue(target.StackKey, out Stack wolfTarget);
                    UpdateStackDetails(wolfTarget);
                    UpdateTargetDetails(lambTarget);
                    break;

                case BattleStepWeapons weapons:
                    myStacks.TryGetValue(weapons.WeaponTarget.TargetKey, out Stack lambWeapons);
                    myStacks.TryGetValue(weapons.WeaponTarget.StackKey, out Stack wolfWeapons);
                    UpdateStackDetails(wolfWeapons);
                    UpdateTargetDetails(lambWeapons);

                    weaponPower.Text = weapons.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    componentTarget.Text = weapons.Targeting == BattleStepWeapons.TokenDefence.Shields
                        ? "Damage to shields"
                        : "Damage to armor";
                    damage.Text = weaponPower.Text + " " + componentTarget.Text;
                    break;

                case BattleStepDestroy destroy:
                    damage.Text = "Ship destroyed";
                    break;
            }
        }

        /// <Summary>
        /// Step forward one position.
        /// </Summary>
        private void NextStep_Click(object sender, EventArgs e)
        {
            GoToStep(eventCount + 1);
        }

        /// <Summary>
        /// Step backward one position.
        /// </Summary>
        private void PreviousStep_Click(object sender, EventArgs e)
        {
            GoToStep(eventCount - 1);
        }

        /// <Summary>
        /// Drag the scrub bar to jump directly to any position.
        /// </Summary>
        private void StepPosition_Scroll(object sender, EventArgs e)
        {
            StopPlaying();
            GoToStep(stepPosition.Value);
        }

        /// <Summary>
        /// Toggle auto-advance playback.
        /// </Summary>
        private void PlayPauseButton_Click(object sender, EventArgs e)
        {
            if (playing)
            {
                StopPlaying();
            }
            else
            {
                playing = true;
                playPauseButton.Text = "Pause";
                playTimer.Start();
            }
        }

        private void PlayTimer_Tick(object sender, EventArgs e)
        {
            if (eventCount >= theBattle.Steps.Count - 1)
            {
                StopPlaying();
                return;
            }

            GoToStep(eventCount + 1);
        }

        private void StopPlaying()
        {
            playing = false;
            playPauseButton.Text = "Play";
            playTimer.Stop();
        }

        /// <summary>
        /// Write out the Battle viewer stack details
        /// </summary>
        /// <param name="wolf"></param>
        private void UpdateStackDetails(Stack wolf)
        {
            if (wolf != null)
            {
                stackOwner.Text = wolf.Owner.ToString("X");
                stackKey.Text = wolf.Key.ToString("X");
                stackQuantity.Text = wolf.Token.Quantity.ToString();
                stackDesign.Text = wolf.Token.Design.Name;
                stackShields.Text = wolf.TotalShieldStrength.ToString();
                stackArmor.Text = wolf.TotalArmorStrength.ToString();
            }
            else
            {
                stackKey.Text = "";
                stackOwner.Text = "";
                stackDesign.Text = "";
                stackShields.Text = "";
                stackArmor.Text = "";
            }
        }

        /// <summary>
        /// Write out the target details
        /// </summary>
        /// <param name="lamb"></param>
        private void UpdateTargetDetails(Stack lamb)
        {
            if (lamb != null)
            {
                targetOwner.Text = lamb.Owner.ToString("X");
                targetKey.Text = lamb.Key.ToString("X");
                targetQuantity.Text = lamb.Token.Quantity.ToString();
                targetDesign.Text = lamb.Token.Design.Name;

                targetShields.Text = lamb.TotalShieldStrength.ToString(System.Globalization.CultureInfo.InvariantCulture);
                targetArmor.Text = lamb.TotalArmorStrength.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                ClearTargetDetails();
            }
        }

        /// <Summary>
        /// Set the details for the target to "" on the UI.
        /// </Summary>
        private void ClearTargetDetails()
        {
            targetDesign.Text = "";
            targetOwner.Text = "";
            targetShields.Text = "";
            targetArmor.Text = "";
        }

        /// <summary>
        /// Clear the BattleViewer weapon details.
        /// </summary>
        private void ClearWeapons()
        {
            weaponPower.Text = "";
            componentTarget.Text = "";
            damage.Text = "";
        }

        /// <summary>
        /// Clear the BattleViewer movement details.
        /// </summary>
        private void ClearMovementDetails()
        {
            movedFrom.Text = "";
            movedTo.Text = "";
        }

        /// <Summary>
        /// Just display the currrent step number in the battle replay control panel.
        /// </Summary>
        private void SetStepNumber(BattleStep thisStep)
        {
            StringBuilder title = new StringBuilder();

            title.AppendFormat(
                "Step {0} of {1}: {2}",
                eventCount + 1,
                theBattle.Steps.Count,
                thisStep.Type
                );

            stepNumber.Text = title.ToString();
        }
    }
}
