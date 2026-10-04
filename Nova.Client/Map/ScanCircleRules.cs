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

    /// <summary>One queued map circle: centre and radius in world units.</summary>
    public readonly struct MapCircle
    {
        public MapCircle(double x, double y, double radius)
        {
            X = x;
            Y = y;
            Radius = radius;
        }

        public double X { get; }

        public double Y { get; }

        public double Radius { get; }

        /// <summary>True when this circle lies entirely inside <paramref name="outer"/> (touching
        /// the outer edge from inside counts as inside).</summary>
        public bool IsInside(MapCircle outer)
        {
            if (Radius > outer.Radius)
            {
                return false;
            }

            double dx = X - outer.X;
            double dy = Y - outer.Y;
            double reach = outer.Radius - Radius;
            return (dx * dx) + (dy * dy) <= reach * reach;
        }
    }

    /// <summary>
    /// Scan-circle overlay arithmetic (behavior-specs-10/client-interface.md, "Scan-range overlay"
    /// and the "Resolved this pass: the exact arithmetic" paragraph).
    /// </summary>
    public static class ScanCircleRules
    {
        /// <summary>
        /// The drawn radius: below the default 100% the true radius is multiplied by 100 and divided
        /// by the scanner percentage (one integer multiply-then-divide), which ENLARGES the circle
        /// (up to 50x at 2%); at 100% the correction is skipped.
        /// </summary>
        public static int DisplayRadius(int trueRadius, int scannerPercentage)
        {
            if (scannerPercentage >= 100 || scannerPercentage <= 0)
            {
                return trueRadius;
            }

            return trueRadius * 100 / scannerPercentage;
        }

        /// <summary>
        /// The batching helper's overlap culling: walks the circles in queue order and skips any
        /// circle fully nested inside a circle already queued before it (only earlier circles are
        /// tested, so a small circle queued before a large one that covers it is still kept).
        /// Returns the indices of the circles to draw, in order. The spec culls per redraw region;
        /// this client redraws the whole map at once, so the whole list is one region.
        /// </summary>
        public static List<int> CullNested(IReadOnlyList<MapCircle> circles)
        {
            var kept = new List<int>();
            for (int i = 0; i < circles.Count; i++)
            {
                bool nested = false;
                for (int j = 0; j < i; j++)
                {
                    if (circles[i].IsInside(circles[j]))
                    {
                        nested = true;
                        break;
                    }
                }

                if (!nested)
                {
                    kept.Add(i);
                }
            }

            return kept;
        }
    }
}
