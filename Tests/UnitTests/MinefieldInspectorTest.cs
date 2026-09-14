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

namespace Nova.Tests.UnitTests
{
    using System.Reflection;
    using System.Windows.Forms;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.WinForms.Gui;

    using NUnit.Framework;

    /// <summary>
    /// The test save this session's live-verification pass otherwise relies on has no minefields
    /// at all (confirmed by grepping every .intel file for "Minefiled"), so this covers the new
    /// MinefieldInspector control directly instead: correct field formatting, and that toggling
    /// the display-mode selector doesn't throw and raises StarmapChanged exactly once per change.
    /// </summary>
    [TestFixture]
    public class MinefieldInspectorTest
    {
        [Test]
        public void DisplayingAMinefieldPopulatesAllFields()
        {
            Minefield minefield = new Minefield
            {
                Name = "Test Minefield",
                Owner = 3,
                Position = new NovaPoint(123, 456),
                NumberOfMines = 400,
                SafeSpeed = 5
            };

            using (MinefieldInspector inspector = new MinefieldInspector())
            {
                inspector.Value = minefield;

                FieldInfo ownerField = typeof(MinefieldInspector).GetField("owner", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo positionField = typeof(MinefieldInspector).GetField("position", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo radiusField = typeof(MinefieldInspector).GetField("radius", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo minesField = typeof(MinefieldInspector).GetField("numberOfMines", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo speedField = typeof(MinefieldInspector).GetField("safeSpeed", BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.AreEqual("3", ((Label)ownerField.GetValue(inspector)).Text);
                Assert.AreEqual(minefield.Position.ToString(), ((Label)positionField.GetValue(inspector)).Text);
                Assert.AreEqual("20", ((Label)radiusField.GetValue(inspector)).Text); // sqrt(400)
                Assert.AreEqual("400", ((Label)minesField.GetValue(inspector)).Text);
                Assert.AreEqual("5", ((Label)speedField.GetValue(inspector)).Text);

                // Default display mode, before the user ever touches the selector.
                Assert.AreEqual(MinefieldDisplayMode.RadiusCircle, inspector.DisplayMode);
            }
        }

        [Test]
        public void ChangingDisplayModeRaisesStarmapChangedExactlyOnce()
        {
            using (MinefieldInspector inspector = new MinefieldInspector())
            {
                inspector.Value = new Minefield { NumberOfMines = 100, SafeSpeed = 4 };

                int raisedCount = 0;
                inspector.StarmapChanged += (sender, e) => raisedCount++;

                FieldInfo comboField = typeof(MinefieldInspector).GetField("displayMode", BindingFlags.NonPublic | BindingFlags.Instance);
                ComboBox combo = (ComboBox)comboField.GetValue(inspector);
                combo.SelectedIndex = (int)MinefieldDisplayMode.SafeSpeedLabel;

                Assert.AreEqual(1, raisedCount);
                Assert.AreEqual(MinefieldDisplayMode.SafeSpeedLabel, inspector.DisplayMode);
            }
        }

        [Test]
        public void ClearingSelectionBlanksAllFieldsWithoutThrowing()
        {
            using (MinefieldInspector inspector = new MinefieldInspector())
            {
                inspector.Value = new Minefield { NumberOfMines = 64, SafeSpeed = 3 };
                Assert.DoesNotThrow(() => inspector.Value = null);

                FieldInfo radiusField = typeof(MinefieldInspector).GetField("radius", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.AreEqual("", ((Label)radiusField.GetValue(inspector)).Text);
            }
        }
    }
}
