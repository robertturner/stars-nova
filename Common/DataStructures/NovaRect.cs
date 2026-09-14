#region Copyright Notice
// ============================================================================
// Copyright (C) 2010-2012 The Stars-Nova Project
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

namespace Nova.Common.DataStructures
{
    using System;

    /// <summary>
    /// A class to represent an axis-aligned rectangle in space.
    /// Like System.Drawing.Rectangle, but without the System.Drawing dependency -
    /// same rationale as NovaPoint, needed so Common stays portable to platforms
    /// (e.g. Android) that don't have System.Drawing/System.Windows.Forms available.
    /// </summary>
    [Serializable]
    public struct NovaRect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public NovaPoint Location => new NovaPoint(X, Y);

        public NovaRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public NovaRect(NovaPoint location, int width, int height)
        {
            X = location.X;
            Y = location.Y;
            Width = width;
            Height = height;
        }
    }
}
