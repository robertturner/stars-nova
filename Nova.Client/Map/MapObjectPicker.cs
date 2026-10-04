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
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>The kinds of object the map can show.</summary>
    public enum MapObjectKind
    {
        Planet,
        Fleet,
        Minefield,
        Wormhole,
        Packet,
    }

    /// <summary>One selectable object with its map position (world units).</summary>
    public sealed class MapObjectEntry
    {
        public MapObjectEntry(object item, string name, MapObjectKind kind, double x, double y)
        {
            Item = item;
            Name = name ?? string.Empty;
            Kind = kind;
            X = x;
            Y = y;
        }

        /// <summary>What selecting this entry publishes.</summary>
        public object Item { get; }

        public string Name { get; }

        public MapObjectKind Kind { get; }

        public double X { get; }

        public double Y { get; }

        public double DistanceSquaredTo(double x, double y)
        {
            return ((X - x) * (X - x)) + ((Y - y) * (Y - y));
        }
    }

    /// <summary>One line of the disambiguation popup.</summary>
    public sealed class MapPickerLine
    {
        public MapPickerLine(MapObjectEntry entry, bool isChecked, bool separatorBefore)
        {
            Entry = entry;
            IsChecked = isChecked;
            SeparatorBefore = separatorBefore;
        }

        public MapObjectEntry Entry { get; }

        /// <summary>The currently-selected object is shown checked.</summary>
        public bool IsChecked { get; }

        /// <summary>True on the first planet line when fleet lines precede it (the divider).</summary>
        public bool SeparatorBefore { get; }
    }

    /// <summary>
    /// The map's right-click object-disambiguation picker (behavior-specs-10/client-interface.md,
    /// "Map canvas", correction paragraph): it lists every fleet present at the clicked position,
    /// then every planet present there, with a divider between the two groups when both exist and
    /// the currently-selected object checked. It is not an action menu: picking a line only
    /// selects that object.
    /// Ambiguity (documented): "present there" is read as within the map's hit radius of the
    /// click; within each group lines are nearest-first, ties keeping the caller's order.
    /// </summary>
    public static class MapObjectPicker
    {
        public static List<MapPickerLine> Build(IEnumerable<MapObjectEntry> objects, double x, double y, double hitRadius, object selected)
        {
            double radiusSquared = hitRadius * hitRadius;
            List<MapObjectEntry> inRange = objects
                .Where(entry => entry.DistanceSquaredTo(x, y) <= radiusSquared)
                .ToList();

            List<MapObjectEntry> fleets = NearestFirst(inRange.Where(entry => entry.Kind == MapObjectKind.Fleet), x, y);
            List<MapObjectEntry> planets = NearestFirst(inRange.Where(entry => entry.Kind == MapObjectKind.Planet), x, y);

            var lines = new List<MapPickerLine>();
            foreach (MapObjectEntry fleet in fleets)
            {
                lines.Add(new MapPickerLine(fleet, ReferenceEquals(fleet.Item, selected), false));
            }

            for (int i = 0; i < planets.Count; i++)
            {
                lines.Add(new MapPickerLine(planets[i], ReferenceEquals(planets[i].Item, selected), i == 0 && fleets.Count > 0));
            }

            return lines;
        }

        private static List<MapObjectEntry> NearestFirst(IEnumerable<MapObjectEntry> entries, double x, double y)
        {
            // OrderBy is stable, so equal distances keep the caller's order.
            return entries.OrderBy(entry => entry.DistanceSquaredTo(x, y)).ToList();
        }
    }
}
