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
    using System.Collections.Generic;
    using System.Globalization;

    using Nova.Common;

    /// <summary>
    /// The minefield inspector's display options (behavior-specs-10/client-interface.md: "The
    /// minefield inspector includes a compact child selector for its active display option.
    /// Selecting a different option updates the stored local view preference and redraws the
    /// inspector; it does not modify the minefield itself.").
    /// SPEC GAP: the options themselves are not listed. Neutral set: the field's own figures
    /// (Field), and what crossing it costs, from the code-confirmed type table of
    /// fleet-movement-scanning-cargo.md section 5 "Minefield rules" (Transit). The choice is a
    /// local preference (persisted through the client's config file by the Inspector).
    /// </summary>
    public static class MinefieldDisplay
    {
        public enum Option
        {
            Field = 0,
            Transit = 1,
        }

        public static readonly string[] OptionLabels = { "Field", "Transit" };

        /// <summary>The config key under which the chosen option is stored.</summary>
        public const string PreferenceKey = "MinefieldInspectorDisplay";

        public static readonly string[] TypeNames = { "Standard", "Heavy", "Speed Bump" };

        /// <summary>The inspector rows for the field under the chosen option.</summary>
        public static List<KeyValuePair<string, string>> Rows(Minefield field, Option option, string ownerName)
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

            if (option == Option.Transit)
            {
                rows.Add(Row("Type", TypeNames[type]));
                rows.Add(Row("Safe speed", "Warp " + Minefield.SafeWarpByType[type].ToString(CultureInfo.InvariantCulture)));
                rows.Add(Row("Hit chance", (Minefield.HitRatePerMilleByType[type] / 10.0).ToString("0.0", CultureInfo.InvariantCulture)
                    + "% per light-year per warp above safe"));
                rows.Add(Row("Damage per ship", Minefield.DamagePerShipByType[type].ToString(CultureInfo.InvariantCulture)
                    + " (" + Minefield.ScoopDamagePerShipByType[type].ToString(CultureInfo.InvariantCulture) + " ram-scoop)"));
                rows.Add(Row("Fleet minimum", Minefield.FleetMinimumByType[type].ToString(CultureInfo.InvariantCulture)
                    + " (" + Minefield.ScoopFleetMinimumByType[type].ToString(CultureInfo.InvariantCulture) + " ram-scoop)"));
                return rows;
            }

            rows.Add(Row("Owner", ownerName ?? string.Empty));
            rows.Add(Row("Type", TypeNames[type]));
            rows.Add(Row("Position", field.Position != null ? field.Position.ToString() : string.Empty));
            rows.Add(Row("Radius", field.Radius.ToString(CultureInfo.InvariantCulture) + " ly"));
            rows.Add(Row("Number of mines", field.NumberOfMines.ToString(CultureInfo.InvariantCulture)));
            rows.Add(Row("Detonating", field.Detonate ? "Yes" : "No"));
            return rows;
        }

        /// <summary>The stored option, or Field for anything unrecognised.</summary>
        public static Option Parse(string stored)
        {
            if (int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                && value >= 0 && value < OptionLabels.Length)
            {
                return (Option)value;
            }

            return Option.Field;
        }

        private static KeyValuePair<string, string> Row(string label, string value)
        {
            return new KeyValuePair<string, string>(label, value);
        }
    }
}
