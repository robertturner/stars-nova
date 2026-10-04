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
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The Technology Browser's model (behavior-specs-10/client-ui-dialog-catalog.md "Additional
    /// confirmed surfaces": Prev/Next paging through tech entries, a category combo, and a "Show
    /// Only Available Technology" filter; client-interface.md: Help > Technology Browser / F2
    /// shows or closes the modeless browser; turn-generation-engine.md section 5: the browser
    /// paints the race's six tech levels labelled "Ener:", "Weap:", "Prop:", "Const:", "Elect:",
    /// "Bio:"). The entry card reuses the detail card's single status line (TechStatusLine).
    /// SPEC GAP: the category list, the entry order within a category and which category opens
    /// first are not given. Neutral choices: the categories present in the component data in
    /// ItemType order, entries in component-file order, the first category first.
    /// </summary>
    public sealed class TechBrowser
    {
        /// <summary>The level labels, in the original's field order (research-tech-tree.md).</summary>
        public static readonly (string Label, TechLevel.ResearchField Field)[] LevelLabels =
        {
            ("Ener:", TechLevel.ResearchField.Energy),
            ("Weap:", TechLevel.ResearchField.Weapons),
            ("Prop:", TechLevel.ResearchField.Propulsion),
            ("Const:", TechLevel.ResearchField.Construction),
            ("Elect:", TechLevel.ResearchField.Electronics),
            ("Bio:", TechLevel.ResearchField.Biotechnology),
        };

        private readonly List<Component> all;
        private readonly Func<Component, bool> isAvailable;

        private List<Component> entries = new List<Component>();
        private ItemType category;
        private bool showOnlyAvailable;

        public TechBrowser(IEnumerable<Component> components, Func<Component, bool> isAvailable)
        {
            all = (components ?? Enumerable.Empty<Component>()).Where(component => component != null).ToList();
            this.isAvailable = isAvailable ?? (_ => true);

            Categories = all.Select(component => component.Type)
                .Distinct()
                .OrderBy(type => (int)type)
                .ToList();
            category = Categories.Count > 0 ? Categories[0] : ItemType.None;
            Rebuild();
        }

        public IReadOnlyList<ItemType> Categories { get; }

        public ItemType Category
        {
            get => category;
            set
            {
                if (category != value)
                {
                    category = value;
                    Rebuild();
                }
            }
        }

        public bool ShowOnlyAvailable
        {
            get => showOnlyAvailable;
            set
            {
                if (showOnlyAvailable != value)
                {
                    showOnlyAvailable = value;
                    Rebuild();
                }
            }
        }

        /// <summary>The entries the current category and filter leave.</summary>
        public IReadOnlyList<Component> Entries => entries;

        /// <summary>The shown entry's index in <see cref="Entries"/>, -1 when empty.</summary>
        public int Index { get; private set; } = -1;

        public Component Current => Index >= 0 && Index < entries.Count ? entries[Index] : null;

        public bool CanPrevious => Index > 0;

        public bool CanNext => Index >= 0 && Index < entries.Count - 1;

        public void Next()
        {
            if (CanNext)
            {
                Index++;
            }
        }

        public void Previous()
        {
            if (CanPrevious)
            {
                Index--;
            }
        }

        /// <summary>Shows the named component (switching to its category, and clearing the
        /// filter when the filter hides it). False when it is unknown.</summary>
        public bool Show(string componentName)
        {
            Component target = all.FirstOrDefault(component => component.Name == componentName);
            if (target == null)
            {
                return false;
            }

            category = target.Type;
            if (showOnlyAvailable && !isAvailable(target))
            {
                showOnlyAvailable = false;
            }

            Rebuild();
            Index = entries.IndexOf(target);
            return true;
        }

        public bool IsAvailable(Component component) => component != null && isAvailable(component);

        /// <summary>"Ener: 3  Weap: 1  ..." for the race's current levels.</summary>
        public static string LevelsLine(TechLevel levels)
        {
            if (levels == null)
            {
                return string.Empty;
            }

            return string.Join("  ", LevelLabels.Select(entry => entry.Label + " " + levels[entry.Field].ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>A component's required levels in the same order, omitting zero fields
        /// ("None" when it needs nothing).</summary>
        public static string RequirementLine(TechLevel required)
        {
            if (required == null)
            {
                return "None";
            }

            List<string> parts = LevelLabels
                .Where(entry => required[entry.Field] > 0)
                .Select(entry => entry.Label + " " + required[entry.Field].ToString(CultureInfo.InvariantCulture))
                .ToList();
            return parts.Count == 0 ? "None" : string.Join("  ", parts);
        }

        private void Rebuild()
        {
            Component previous = Current;
            entries = all
                .Where(component => component.Type == category)
                .Where(component => !showOnlyAvailable || isAvailable(component))
                .ToList();

            int keep = previous != null ? entries.IndexOf(previous) : -1;
            Index = keep >= 0 ? keep : (entries.Count > 0 ? 0 : -1);
        }
    }
}
