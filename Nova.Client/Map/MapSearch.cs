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

    /// <summary>
    /// The Find dialog's query (behavior-specs-10/client-interface.md "Map canvas": "A search dialog
    /// can locate a named or otherwise filterable object and then focus the map on the result";
    /// client-ui-dialog-catalog.md "Search and record browser": the search "accepts a query,
    /// evaluates it against the relevant object collection, and returns a selected result or no
    /// result").
    /// SPEC GAP: template 4202's controls and matching rules are not specified. Neutral reading:
    /// a case-insensitive name match, optionally restricted to one object kind; exact matches
    /// first, then names starting with the query, then names containing it, each group sorted by
    /// name; an empty query lists every object of the kind.
    /// </summary>
    public static class MapSearch
    {
        public static List<MapObjectEntry> Find(IEnumerable<MapObjectEntry> objects, string query, MapObjectKind? kind)
        {
            string trimmed = (query ?? string.Empty).Trim();

            IEnumerable<MapObjectEntry> candidates = objects.Where(entry => kind == null || entry.Kind == kind.Value);

            if (trimmed.Length == 0)
            {
                return candidates.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }

            return candidates
                .Select(entry => (entry, rank: Rank(entry.Name, trimmed)))
                .Where(t => t.rank >= 0)
                .OrderBy(t => t.rank)
                .ThenBy(t => t.entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(t => t.entry)
                .ToList();
        }

        /// <summary>The single result to focus, or null for "no result".</summary>
        public static MapObjectEntry FindFirst(IEnumerable<MapObjectEntry> objects, string query, MapObjectKind? kind)
        {
            return Find(objects, query, kind).FirstOrDefault();
        }

        private static int Rank(string name, string query)
        {
            if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : -1;
        }
    }
}
