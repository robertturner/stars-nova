#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars! Nova.
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
    using System.Collections.Generic;
    using System.Globalization;

    using Nova.Common;

    /// <summary>
    /// The read-only rows the minefield inspector shows for the selected field
    /// (behavior-specs-11/client-interface.md, "Minefield inspector"; corrected by the planet-view
    /// pass: "The pane has no display-option selector. Its only child control is one
    /// auto-checkbox ... (detonate this minefield next year).").
    /// The old Field/Transit display selector and its config-file preference are a Nova invention
    /// and have been dropped: this class only builds the field's own figures, and the detonate
    /// checkbox lives in the Inspector.
    /// </summary>
    public static class MinefieldDisplay
    {
        public static readonly string[] TypeNames = { "Standard", "Heavy", "Speed Bump" };

        /// <summary>The inspector rows for the field (owner, type, position, radius, mines, detonating).</summary>
        public static List<KeyValuePair<string, string>> Rows(Minefield field, string ownerName)
        {
            List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
            if (field == null)
            {
                return rows;
            }

            int type = (int)field.FieldType;
            if (type < 0 || type >= TypeNames.Length)
            {
                type = 0;
            }

            rows.Add(Row("Owner", ownerName ?? string.Empty));
            rows.Add(Row("Type", TypeNames[type]));
            rows.Add(Row("Position", field.Position != null ? field.Position.ToString() : string.Empty));
            rows.Add(Row("Radius", field.Radius.ToString(CultureInfo.InvariantCulture) + " ly"));
            rows.Add(Row("Number of mines", field.NumberOfMines.ToString(CultureInfo.InvariantCulture)));
            rows.Add(Row("Detonating", field.Detonate ? "Yes" : "No"));
            return rows;
        }

        private static KeyValuePair<string, string> Row(string label, string value)
        {
            return new KeyValuePair<string, string>(label, value);
        }
    }
}
