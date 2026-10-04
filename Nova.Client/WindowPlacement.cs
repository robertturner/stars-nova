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
    using System.Globalization;

    /// <summary>
    /// The main window's persisted placement (behavior-specs-10/client-interface.md "persisted
    /// settings": the client keeps its window placement in its settings file between sessions).
    /// Stored in the client's config file (Nova.Common.Config, nova.conf) under
    /// <see cref="PreferenceKey"/> as "x,y,width,height,maximized".
    /// SPEC GAP: the original's .ini key names and value layout are not needed for a clean-room
    /// client and are not reproduced; nor are the PBEM and tracked-object entries (no such
    /// features here).
    /// </summary>
    public sealed class WindowPlacement
    {
        public const string PreferenceKey = "MainWindowPlacement";

        /// <summary>Smallest width/height accepted back (a saved collapsed window is ignored).</summary>
        public const int MinimumSize = 200;

        public WindowPlacement(int x, int y, int width, int height, bool maximized)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Maximized = maximized;
        }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }

        public bool Maximized { get; }

        public string Format()
        {
            return string.Join(
                ",",
                X.ToString(CultureInfo.InvariantCulture),
                Y.ToString(CultureInfo.InvariantCulture),
                Width.ToString(CultureInfo.InvariantCulture),
                Height.ToString(CultureInfo.InvariantCulture),
                Maximized ? "1" : "0");
        }

        /// <summary>The stored placement, or null when absent, malformed or too small.</summary>
        public static WindowPlacement Parse(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
            {
                return null;
            }

            string[] parts = stored.Split(',');
            if (parts.Length != 5)
            {
                return null;
            }

            int[] values = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                {
                    return null;
                }
            }

            if (values[2] < MinimumSize || values[3] < MinimumSize)
            {
                return null;
            }

            return new WindowPlacement(values[0], values[1], values[2], values[3], parts[4].Trim() == "1");
        }
    }
}
