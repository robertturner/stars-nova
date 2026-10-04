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
    using System.Collections.Generic;

    /// <summary>
    /// The tracked-object fleet marker tiles (behavior-specs-10/client-interface.md, "Deep-space
    /// fleet marker" and its "Shape correction" paragraph, decoded from the map-icon sheet).
    /// A deep-space fleet that is the tracked (selected) object is drawn as the 11x11 chevron, blue
    /// for the viewer's own fleet and red otherwise. The 5x5 triangle is finished art that no code
    /// path reaches; it is provided for completeness only.
    /// Masks are indexed [row, column], row 0 at the top.
    /// </summary>
    public static class TrackedMarkerShape
    {
        public const int ChevronSize = 11;
        public const int TriangleSize = 5;

        /// <summary>
        /// The 11x11 right-pointing arrow: a 3-pixel-thick horizontal bar across the middle (rows
        /// 4-6 zero-based, i.e. "rows 5-7"; the centre row spans all 11 pixels, the rows above and
        /// below 10) plus two 3-pixel-wide diagonal strokes from the top-left and bottom-left corners
        /// running in toward the centre, meeting the bar at about the middle, the bar continuing
        /// past the point to the right edge.
        /// Ambiguity (documented): the 10-pixel rows are read as starting at the left edge, and each
        /// diagonal row r (0-4 from the corner) as covering columns r to r + 2.
        /// </summary>
        public static bool[,] Chevron11()
        {
            var mask = new bool[ChevronSize, ChevronSize];
            const int centre = ChevronSize / 2;

            for (int column = 0; column < ChevronSize; column++)
            {
                mask[centre, column] = true;
            }

            for (int column = 0; column < ChevronSize - 1; column++)
            {
                mask[centre - 1, column] = true;
                mask[centre + 1, column] = true;
            }

            for (int r = 0; r < centre; r++)
            {
                for (int column = r; column <= r + 2 && column < ChevronSize; column++)
                {
                    mask[r, column] = true;
                    mask[ChevronSize - 1 - r, column] = true;
                }
            }

            return mask;
        }

        /// <summary>The 5x5 right triangle: row k (0-4) is filled from the left edge for k + 1
        /// pixels (right angle bottom-left, hypotenuse top-left to bottom-right).</summary>
        public static bool[,] Triangle5()
        {
            var mask = new bool[TriangleSize, TriangleSize];
            for (int row = 0; row < TriangleSize; row++)
            {
                for (int column = 0; column <= row; column++)
                {
                    mask[row, column] = true;
                }
            }

            return mask;
        }

        /// <summary>The set pixels of a mask as (column, row) pairs, for drawing as unit squares.</summary>
        public static List<(int Column, int Row)> Pixels(bool[,] mask)
        {
            var pixels = new List<(int Column, int Row)>();
            for (int row = 0; row < mask.GetLength(0); row++)
            {
                for (int column = 0; column < mask.GetLength(1); column++)
                {
                    if (mask[row, column])
                    {
                        pixels.Add((column, row));
                    }
                }
            }

            return pixels;
        }
    }
}
