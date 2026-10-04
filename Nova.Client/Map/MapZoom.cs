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

namespace Nova.Client.Map
{
    using System;

    /// <summary>
    /// The map's 9 fixed zoom steps (behavior-specs-10/client-interface.md, "Exact zoom scale
    /// factors" and the View > Zoom items 3901-3909, "Sets zoom step -4 to +4, recentres the map").
    /// The scale factors are exact integer ratios, not floating-point percentages: 1/4, 3/8 (the
    /// "38%" item is really 37.5%), 1/2, 3/4, 1, 5/4, 3/2, 2 and 4. Levels are -4..+4, 0 = 100%.
    /// </summary>
    public static class MapZoom
    {
        public const int MinLevel = -4;
        public const int MaxLevel = 4;
        public const int DefaultLevel = 0;

        private static readonly int[] Numerators = { 1, 3, 1, 3, 1, 5, 3, 2, 4 };
        private static readonly int[] Denominators = { 4, 8, 2, 4, 1, 4, 2, 1, 1 };
        private static readonly string[] MenuLabels = { "25%", "38%", "50%", "75%", "100%", "125%", "150%", "200%", "400%" };

        /// <summary>Clamps a level into -4..+4.</summary>
        public static int Clamp(int level)
        {
            return Math.Max(MinLevel, Math.Min(MaxLevel, level));
        }

        /// <summary>The exact scale factor of a level (0.375 for level -3, and so on).</summary>
        public static double ScaleFactor(int level)
        {
            int index = Clamp(level) - MinLevel;
            return (double)Numerators[index] / Denominators[index];
        }

        /// <summary>The Zoom menu caption of a level ("38%" for 37.5%).</summary>
        public static string Label(int level)
        {
            return MenuLabels[Clamp(level) - MinLevel];
        }

        /// <summary>World-to-screen in the client's integer arithmetic (multiply, then divide).</summary>
        public static int ToScreen(int world, int level)
        {
            int index = Clamp(level) - MinLevel;
            return world * Numerators[index] / Denominators[index];
        }

        /// <summary>Screen-to-world, the inverse ratio in the same integer arithmetic.</summary>
        public static int ToWorld(int screen, int level)
        {
            int index = Clamp(level) - MinLevel;
            return screen * Denominators[index] / Numerators[index];
        }

        /// <summary>The level whose factor is closest to <paramref name="factor"/> (for callers that
        /// still hold a continuous zoom value).</summary>
        public static int NearestLevel(double factor)
        {
            int best = DefaultLevel;
            double bestDistance = double.MaxValue;
            for (int level = MinLevel; level <= MaxLevel; level++)
            {
                double distance = Math.Abs(ScaleFactor(level) - factor);
                if (distance < bestDistance)
                {
                    best = level;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// The scroll offset that keeps the same world point at the centre of the viewport after a
        /// zoom change ("selecting a Zoom level explicitly recenters the map view on the same world
        /// point it was centered on before the change, computed from the visible client area's
        /// half-width/half-height in the new scale"). Offsets are in screen (scaled) units; the
        /// result is not clamped to the scrollable extent (the caller's scroller does that).
        /// </summary>
        public static (double X, double Y) RecenteredOffset(double offsetX, double offsetY, double viewportWidth, double viewportHeight, double oldScale, double newScale)
        {
            if (oldScale <= 0 || newScale <= 0)
            {
                return (offsetX, offsetY);
            }

            double worldCentreX = (offsetX + (viewportWidth / 2)) / oldScale;
            double worldCentreY = (offsetY + (viewportHeight / 2)) / oldScale;
            return ((worldCentreX * newScale) - (viewportWidth / 2), (worldCentreY * newScale) - (viewportHeight / 2));
        }

        /// <summary>The scroll offset that puts world point (x, y) at the centre of the viewport.</summary>
        public static (double X, double Y) CentredOn(double worldX, double worldY, double viewportWidth, double viewportHeight, double scale)
        {
            return ((worldX * scale) - (viewportWidth / 2), (worldY * scale) - (viewportHeight / 2));
        }
    }
}
