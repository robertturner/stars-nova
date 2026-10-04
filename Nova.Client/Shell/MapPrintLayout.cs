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
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    /// <summary>One output page: a rectangle of the rendered map, in pixels.</summary>
    public struct MapPrintPage
    {
        public MapPrintPage(int number, int x, int y, int width, int height)
        {
            Number = number;
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>1-based, left to right then top to bottom.</summary>
        public int Number { get; }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }
    }

    /// <summary>
    /// File > Print Map (behavior-specs-10/client-interface.md command 213: "Page-count dialog
    /// (template 214), then the system print dialog; prints the map tiled over the chosen pages;
    /// failure gives dynamic string 1320"; client-ui-dialog-catalog.md "Map printing": the
    /// configuration stays local until the user confirms; Cancel sends no output).
    /// This client has no printer path on every platform, so "printing" writes one PNG image per
    /// page into the game folder (the lead's chosen substitute).
    /// SPEC GAP: the page-count dialog's fields and limits, how the chosen page count is arranged
    /// into a grid, and the text of dynamic string 1320. Stand-ins: pages across and pages down,
    /// each 1-<see cref="MaxPagesPerSide"/>; the map is cut into equal tiles (the last row and
    /// column take any remainder pixels); <see cref="FailureText"/>.
    /// </summary>
    public static class MapPrintLayout
    {
        /// <summary>SPEC GAP seam: the largest page count per side.</summary>
        public const int MaxPagesPerSide = 4;

        /// <summary>SPEC GAP seam: the failure text (dynamic string 1320 is not given).</summary>
        public const string FailureText = "The map could not be printed.";

        public static int ClampPages(int pages)
        {
            return Math.Max(1, Math.Min(MaxPagesPerSide, pages));
        }

        /// <summary>Cuts a width x height image into across x down pages covering every pixel once.</summary>
        public static IReadOnlyList<MapPrintPage> Pages(int width, int height, int across, int down)
        {
            List<MapPrintPage> pages = new List<MapPrintPage>();
            if (width <= 0 || height <= 0)
            {
                return pages;
            }

            across = Math.Min(ClampPages(across), width);
            down = Math.Min(ClampPages(down), height);
            int tileWidth = width / across;
            int tileHeight = height / down;

            int number = 1;
            for (int row = 0; row < down; row++)
            {
                int y = row * tileHeight;
                int h = row == down - 1 ? height - y : tileHeight;
                for (int column = 0; column < across; column++)
                {
                    int x = column * tileWidth;
                    int w = column == across - 1 ? width - x : tileWidth;
                    pages.Add(new MapPrintPage(number++, x, y, w, h));
                }
            }

            return pages;
        }

        /// <summary>The image file for one page: "&lt;race&gt;-map-&lt;year&gt;-page&lt;n&gt;.png".
        /// SPEC GAP: the original prints; the file name is Nova's own.</summary>
        public static string PageFileName(string folder, string raceName, int year, int pageNumber)
        {
            string name = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-map-{1}-page{2}.png",
                string.IsNullOrWhiteSpace(raceName) ? "nova" : raceName.Trim(),
                year,
                pageNumber);
            return Path.Combine(folder ?? string.Empty, name);
        }
    }
}
