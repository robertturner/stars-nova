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
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The component-category browser's position (behavior-specs-10/client-ui-dialog-catalog.md
    /// "Search and record browser": an outer list of component categories; "Stepping to the
    /// next/previous entry then walks that category's subtypes ..., skipping any subtype the
    /// resolver reports as nonexistent or unavailable to the current race"; the step buttons'
    /// category index wraps around and never sits on an unused slot). Used by the ship
    /// designer's component palette, whose entries are already only the race's available parts.
    /// Ambiguity: the spec says the step buttons wrap over the category index but not what Next
    /// does at the end of a category. Reading used: Next past a category's last entry moves to
    /// the first entry of the next non-empty category (the last category wraps to the first), and
    /// Previous mirrors it. Empty categories are skipped.
    /// </summary>
    /// <typeparam name="T">The entry type.</typeparam>
    public sealed class CategoryStepper<T>
    {
        private readonly List<List<T>> categories;

        public CategoryStepper(IEnumerable<IEnumerable<T>> categories)
        {
            this.categories = (categories ?? Enumerable.Empty<IEnumerable<T>>())
                .Select(entries => (entries ?? Enumerable.Empty<T>()).ToList())
                .ToList();
            SelectCategory(0);
        }

        public int CategoryCount => categories.Count;

        /// <summary>The current category, -1 when every category is empty.</summary>
        public int CategoryIndex { get; private set; } = -1;

        /// <summary>The current entry within the category, -1 when none.</summary>
        public int EntryIndex { get; private set; } = -1;

        public bool HasCurrent => CategoryIndex >= 0 && EntryIndex >= 0;

        public T Current => HasCurrent ? categories[CategoryIndex][EntryIndex] : default(T);

        /// <summary>Selects a category (its first entry); an empty one goes to the next non-empty.</summary>
        public void SelectCategory(int index)
        {
            CategoryIndex = -1;
            EntryIndex = -1;
            if (categories.Count == 0)
            {
                return;
            }

            for (int step = 0; step < categories.Count; step++)
            {
                int candidate = Wrap(index + step);
                if (categories[candidate].Count > 0)
                {
                    CategoryIndex = candidate;
                    EntryIndex = 0;
                    return;
                }
            }
        }

        /// <summary>Selects a given entry (by equality); false when it is not in any category.</summary>
        public bool Select(T entry)
        {
            for (int c = 0; c < categories.Count; c++)
            {
                int e = categories[c].IndexOf(entry);
                if (e >= 0)
                {
                    CategoryIndex = c;
                    EntryIndex = e;
                    return true;
                }
            }

            return false;
        }

        public void Next()
        {
            if (!HasCurrent)
            {
                return;
            }

            if (EntryIndex < categories[CategoryIndex].Count - 1)
            {
                EntryIndex++;
                return;
            }

            SelectCategory(CategoryIndex + 1);
        }

        public void Previous()
        {
            if (!HasCurrent)
            {
                return;
            }

            if (EntryIndex > 0)
            {
                EntryIndex--;
                return;
            }

            for (int step = 1; step <= categories.Count; step++)
            {
                int candidate = Wrap(CategoryIndex - step);
                if (categories[candidate].Count > 0)
                {
                    CategoryIndex = candidate;
                    EntryIndex = categories[candidate].Count - 1;
                    return;
                }
            }
        }

        private int Wrap(int index)
        {
            int count = categories.Count;
            return ((index % count) + count) % count;
        }
    }
}
