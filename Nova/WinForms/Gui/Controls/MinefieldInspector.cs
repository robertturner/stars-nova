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

namespace Nova.WinForms.Gui
{
    using System;
    using System.Windows.Forms;

    using Nova.Common;

    /// <summary>
    /// Which overlay a selected minefield draws on the Star Map. Purely a per-viewer display
    /// choice - never persisted, never affects the Minefield object itself.
    /// </summary>
    public enum MinefieldDisplayMode
    {
        RadiusCircle,
        MineCountLabel,
        SafeSpeedLabel
    }

    /// <Summary>
    /// Read-only Minefield Detail display pane - ports client-ui-dialog-catalog.md's minefield
    /// inspector: no field here is editable, since nothing in the game lets a player directly
    /// alter an existing minefield's own stats (only lay/detonate whole fields, which is a fleet
    /// order, not an edit here).
    /// </Summary>
    public partial class MinefieldInspector : UserControl
    {
        private Minefield selectedMinefield;

        /// <Summary>
        /// Raised when the map-overlay selector changes, so the Star Map can redraw the
        /// currently selected minefield with the new overlay. Mirrors FleetDetail's
        /// StarmapChanged event.
        /// </Summary>
        public event EventHandler StarmapChanged;

        public MinefieldInspector()
        {
            InitializeComponent();
        }

        /// <Summary>
        /// Access to the Minefield whose details are displayed in the panel.
        /// </Summary>
        public Minefield Value
        {
            get { return selectedMinefield; }
            set
            {
                selectedMinefield = value;
                UpdateFields();
            }
        }

        /// <Summary>
        /// The overlay currently selected for the inspected minefield.
        /// </Summary>
        public MinefieldDisplayMode DisplayMode
        {
            get { return (MinefieldDisplayMode)Math.Max(0, displayMode.SelectedIndex); }
        }

        private void UpdateFields()
        {
            if (selectedMinefield == null)
            {
                owner.Text = "";
                position.Text = "";
                radius.Text = "";
                numberOfMines.Text = "";
                safeSpeed.Text = "";
                return;
            }

            owner.Text = selectedMinefield.Owner.ToString(System.Globalization.CultureInfo.InvariantCulture);
            position.Text = selectedMinefield.Position.ToString();
            radius.Text = selectedMinefield.Radius.ToString(System.Globalization.CultureInfo.InvariantCulture);
            numberOfMines.Text = selectedMinefield.NumberOfMines.ToString(System.Globalization.CultureInfo.InvariantCulture);
            safeSpeed.Text = selectedMinefield.SafeSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (displayMode.SelectedIndex < 0)
            {
                displayMode.SelectedIndex = 0;
            }
        }

        private void DisplayMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (StarmapChanged != null)
            {
                StarmapChanged(this, EventArgs.Empty);
            }
        }
    }
}
