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
    using System.Collections;
    using System.Collections.Generic;
    using System.Windows.Forms;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;

    /// <Summary>
    /// Describes the possible player relation stances.
    /// </Summary>
    public partial class PlayerRelations : Form
    {
        private Dictionary<ushort, EmpireIntel> empireReports;
        private ushort empireId;
        private Stack<ICommand> commands;

        /// <Summary>
        /// Initializes a new instance of the PlayerRelations class.
        /// </Summary>
        /// <param name="commands">
        /// The empire's pending command stack. A relation change is a queued turn order (see
        /// docs/behavior-specs-3/diplomacy-relations.md §2, verified against a decompile of the
        /// exported client), not an immediate change - it's pushed here for the eventual
        /// .orders file and applied to the local EmpireData immediately after for optimistic UI
        /// feedback, the same pattern every other order-issuing dialog in this codebase follows.
        /// </param>
        public PlayerRelations(Dictionary<ushort, EmpireIntel> empireReports, ushort empireId, Stack<ICommand> commands)
        {
            this.empireReports = empireReports;
            this.empireId = empireId;
            this.commands = commands;

            InitializeComponent();

            foreach (ushort otherEmpireId in this.empireReports.Keys)
            {
                if (otherEmpireId != this.empireId)
                {
                    empireList.Items.Add(otherEmpireId);
                }
            }

            if (empireList.Items.Count > 0)
            {
                empireList.SelectedIndex = 0;
            }
        }


        /// <Summary>
        /// Exit dialog button pressed
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void DoneBUtton_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <Summary>
        /// Selected race has changed, update the relation details
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void SelectedRaceChanged(object sender, EventArgs e)
        {
            ushort selectedEmpire = (ushort) empireList.SelectedItem;

            if (empireReports[selectedEmpire].Relation == PlayerRelation.Enemy)
            {
                enemyButton.Checked = true;
            }
            else if (empireReports[selectedEmpire].Relation == PlayerRelation.Neutral)
            {
                neutralButton.Checked = true;
            }
            else
            {
                friendButton.Checked = true;
            }
        }

        /// <Summary>
        /// Player relationship changed
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void RelationChanged(object sender, EventArgs e)
        {
            ushort selectedEmpire = (ushort)empireList.SelectedItem;

            PlayerRelation newRelation;
            if (enemyButton.Checked)
            {
                newRelation = PlayerRelation.Enemy;
            }
            else if (friendButton.Checked)
            {
                newRelation = PlayerRelation.Friend;
            }
            else
            {
                newRelation = PlayerRelation.Neutral;
            }

            if (empireReports[selectedEmpire].Relation == newRelation)
            {
                return;
            }

            commands.Push(new RelationCommand(selectedEmpire, newRelation));

            // Applied immediately to the local report too, for optimistic UI feedback ahead of
            // the real server-side turn processing - see the constructor's doc comment.
            empireReports[selectedEmpire].Relation = newRelation;
        }
    }
}
