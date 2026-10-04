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

namespace Nova.Common
{
    using System.Collections.Generic;

    /// <summary>
    /// Puts a collection into the enumeration order it would have after a save and reload.
    /// </summary>
    /// <remarks>
    /// A Dictionary or HashSet enumerates in slot order, and a removal leaves a free slot that
    /// the NEXT addition reuses - so in a game kept in memory an item added after a removal is
    /// walked in the middle, while after a save and reload (which re-adds in saved, i.e.
    /// enumeration, order, with no free slots) the same item comes last. Walk order decides
    /// fleet movement order, saved text order and more, so the server compacts its collections
    /// at the end of every generation (ServerData.CompactCollections): the state kept in memory
    /// then behaves exactly like the reloaded one. Compaction keeps the same instances (Clear
    /// then re-add), so references to the collections stay valid.
    /// </remarks>
    public static class CanonicalOrder
    {
        /// <summary>Removes the free slots, keeping the current enumeration order.</summary>
        public static void Compact<TKey, TValue>(Dictionary<TKey, TValue> dictionary)
        {
            if (dictionary == null || dictionary.Count == 0)
            {
                return;
            }

            List<KeyValuePair<TKey, TValue>> entries = new List<KeyValuePair<TKey, TValue>>(dictionary);
            dictionary.Clear();
            foreach (KeyValuePair<TKey, TValue> entry in entries)
            {
                dictionary.Add(entry.Key, entry.Value);
            }
        }

        /// <summary>Removes the free slots, keeping the current enumeration order.</summary>
        public static void Compact<T>(HashSet<T> set)
        {
            if (set == null || set.Count == 0)
            {
                return;
            }

            List<T> items = new List<T>(set);
            set.Clear();
            foreach (T item in items)
            {
                set.Add(item);
            }
        }

        /// <summary>Re-adds the items in sorted order: for sets that are saved sorted, so a
        /// reload reads them back in that order.</summary>
        public static void CompactSorted<T>(HashSet<T> set)
        {
            if (set == null || set.Count == 0)
            {
                return;
            }

            List<T> items = new List<T>(set);
            items.Sort();
            set.Clear();
            foreach (T item in items)
            {
                set.Add(item);
            }
        }
    }
}
