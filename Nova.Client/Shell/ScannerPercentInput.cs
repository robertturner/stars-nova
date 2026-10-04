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

namespace Nova.Client.Shell
{
    using System;
    using System.Globalization;

    using Nova.Client.Map;

    /// <summary>
    /// The planet inspector's "scanner display" percentage control (behavior-specs-10/
    /// client-ui-dialog-catalog.md "Planet inspector": accepts 2-100, "formatted as "N%": pressing
    /// Enter commits a typed value, Escape reverts to the previously stored value, and either
    /// action updates an associated slider position and unconditionally turns on the map's
    /// scan-range-circle overlay. While this control is being adjusted, a custom hover-tooltip
    /// window shows the live value, its visibility gated by how recently the value last changed
    /// (roughly a 400ms window)"). The stored value itself is MapViewOptions.ScannerPercentage.
    /// Ambiguity: a typed number outside 2-100 is pulled to the nearest limit (the spec gives the
    /// range, not what happens to an out-of-range entry); text that is not a number reverts.
    /// </summary>
    public static class ScannerPercentInput
    {
        /// <summary>The live-tooltip window ("roughly a 400ms window").</summary>
        public static readonly TimeSpan TooltipWindow = TimeSpan.FromMilliseconds(400);

        public static string Format(int percentage)
        {
            return percentage.ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>
        /// The value Enter commits for the typed text ("N" or "N%"), clamped to 2-100; the stored
        /// value when the text is not a number.
        /// </summary>
        public static int Commit(string typed, int stored)
        {
            string text = (typed ?? string.Empty).Trim();
            if (text.EndsWith("%", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1).Trim();
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return stored;
            }

            return Math.Max(MapViewOptions.MinScannerPercentage, Math.Min(MapViewOptions.MaxScannerPercentage, value));
        }

        /// <summary>Whether the live tooltip shows, given the last change time.</summary>
        public static bool IsTooltipVisible(DateTime lastChange, DateTime now)
        {
            TimeSpan since = now - lastChange;
            return since >= TimeSpan.Zero && since < TooltipWindow;
        }
    }
}
