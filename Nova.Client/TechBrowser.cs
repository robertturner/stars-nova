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
    /// The Technology Browser's model (behavior-specs-11/research-tech-tree.md section 7a, and
    /// client-ui-dialog-catalog.md "Search and record browser"): a modeless window showing one
    /// component at a time on the shared detail card, with Prev / Next, a category drop-down, a
    /// "Show Only Available Technology" checkbox and Close.
    /// <list type="bullet">
    /// <item><b>Categories.</b> The drop-down holds 17 entries: All, then the 16 component
    /// categories in this alphabetical order - Armor, Beam Weapons, Bombs, Electrical, Engines,
    /// Mechanical, Mine Layers, Mining Robots, Orbital, Planetary, Scanners, Shields, Ship Hulls,
    /// Starbase Hulls, Terraforming, Torpedoes. Entries 1-16 map to the component-category bit
    /// flags (Armor 0x0008, Beam Weapons 0x0010, Bombs 0x0040, Electrical 0x0800, Engines 0x0001,
    /// Mechanical 0x1000, Mine Layers 0x0100, Mining Robots 0x0080, Orbital 0x0200, Planetary
    /// 0x8000, Scanners 0x0002, Shields 0x0004, Ship Hulls 0x4000, Starbase Hulls 0x0400,
    /// Terraforming 0x2000, Torpedoes 0x0020).</item>
    /// <item><b>Opening state.</b> The drop-down is All, the checkbox is clear, and the card shows
    /// Armor item 0 (Tritanium) - unless another surface opened the browser on a specific item, in
    /// which case that item is shown.</item>
    /// <item><b>Entry order and paging.</b> Items are visited in subtype order within a category.
    /// Next and Prev step one subtype at a time and wrap: with a specific category selected they
    /// wrap within it; with All selected they run on into the next or previous category of the
    /// table (wrapping from Torpedoes to Armor and back).</item>
    /// <item><b>Filter.</b> An item is shown when the availability resolver reports it available,
    /// or, with the checkbox clear, when it exists at all - except that a part from the one-time
    /// gift set (Mystery Trader and similar parts, ship-design-and-components.md section 14a) is
    /// skipped while the race has not received it. With the checkbox clear the browser also shows
    /// parts the race cannot build yet or can never build; with it set, only what the race can
    /// build now.</item>
    /// </list>
    /// SEAMS for spec-silent details (reported): "subtype order" is taken to be the order the
    /// caller supplies the components in (the component data's own order); the gift-received
    /// predicate is supplied by the caller (default: nothing received); and the drop-down names
    /// are placeholders for the dynamic strings 1087-1103.
    /// </summary>
    public sealed class TechBrowser
    {
        /// <summary>The 17 drop-down entries (dynamic strings 1087-1103 seam: All first, then
        /// the 16 categories alphabetically). Index 0 is All.</summary>
        public static readonly IReadOnlyList<string> CategoryNames = new[]
        {
            "All",
            "Armor",
            "Beam Weapons",
            "Bombs",
            "Electrical",
            "Engines",
            "Mechanical",
            "Mine Layers",
            "Mining Robots",
            "Orbital",
            "Planetary",
            "Scanners",
            "Shields",
            "Ship Hulls",
            "Starbase Hulls",
            "Terraforming",
            "Torpedoes",
        };

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
        private readonly Func<Component, bool> isGiftReceived;

        private readonly List<Component>[] byCategory = new List<Component>[CategoryNames.Count];

        private List<Component> entries = new List<Component>();
        private int categoryIndex;
        private bool showOnlyAvailable;

        /// <param name="components">Every component the browser can show (all of them, not only
        /// the race's available set), in subtype order within each category.</param>
        /// <param name="isAvailable">Whether the race can build the component now (the availability
        /// resolver).</param>
        /// <param name="isGiftReceived">Whether the race has received this one-time gift part;
        /// null means nothing has been received.</param>
        public TechBrowser(IEnumerable<Component> components, Func<Component, bool> isAvailable, Func<Component, bool> isGiftReceived = null)
        {
            all = (components ?? Enumerable.Empty<Component>()).Where(component => component != null).ToList();
            this.isAvailable = isAvailable ?? (_ => true);
            this.isGiftReceived = isGiftReceived ?? (_ => false);

            for (int i = 1; i < CategoryNames.Count; i++)
            {
                byCategory[i] = all.Where(component => Matches(i, component)).ToList();
            }

            byCategory[0] = Enumerable.Range(1, CategoryNames.Count - 1)
                .SelectMany(i => byCategory[i])
                .ToList();

            categoryIndex = 0;
            Rebuild();
        }

        /// <summary>The 17 drop-down entries, All first.</summary>
        public IReadOnlyList<string> Categories => CategoryNames;

        /// <summary>The selected category's index (0 = All).</summary>
        public int CategoryIndex => categoryIndex;

        /// <summary>The selected category's name.</summary>
        public string Category => CategoryNames[categoryIndex];

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

        /// <summary>Paging wraps, so a category with any shown entry can step either way.</summary>
        public bool CanPrevious => entries.Count > 0;

        public bool CanNext => entries.Count > 0;

        /// <summary>Steps to the next subtype, wrapping within a category and, under All, on into
        /// the next category.</summary>
        public void Next()
        {
            if (entries.Count == 0)
            {
                return;
            }

            Index = (Index + 1) % entries.Count;
        }

        public void Previous()
        {
            if (entries.Count == 0)
            {
                return;
            }

            Index = ((Index - 1) % entries.Count + entries.Count) % entries.Count;
        }

        /// <summary>Selects a drop-down entry (0 = All) and shows its first item.</summary>
        public void SetCategory(int index)
        {
            int clamped = Math.Max(0, Math.Min(index, CategoryNames.Count - 1));
            if (clamped != categoryIndex)
            {
                categoryIndex = clamped;
                Rebuild();
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

            if (showOnlyAvailable && !ShouldShow(target))
            {
                showOnlyAvailable = false;
            }

            categoryIndex = CategoryOf(target);
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

        /// <summary>The component-category index for a component (1-16), or 0 (All) when none.</summary>
        public static int CategoryOf(Component component)
        {
            for (int i = 1; i < CategoryNames.Count; i++)
            {
                if (Matches(i, component))
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>Whether a component belongs to one of the 16 categories.</summary>
        private static bool Matches(int category, Component component)
        {
            switch (category)
            {
                case 1: return component.Type == ItemType.Armor;
                case 2: return component.Type == ItemType.BeamWeapons;
                case 3: return component.Type == ItemType.Bomb;
                case 4: return component.Type == ItemType.Electrical;
                case 5: return component.Type == ItemType.Engine;
                case 6: return component.Type == ItemType.Mechanical;
                case 7: return component.Type == ItemType.MineLayer;
                case 8: return component.Type == ItemType.MiningRobot;
                case 9: return component.Type == ItemType.Orbital || component.Type == ItemType.Gate;
                case 10: return component.Type == ItemType.PlanetaryInstallations || component.Type == ItemType.Defense;
                case 11: return component.Type == ItemType.Scanner;
                case 12: return component.Type == ItemType.Shield;
                case 13: return component.Type == ItemType.Hull && !IsStarbaseHull(component);
                case 14: return component.Type == ItemType.Hull && IsStarbaseHull(component);
                case 15: return component.Type == ItemType.Terraforming;
                case 16: return component.Type == ItemType.Torpedoes;
                default: return false;
            }
        }

        private static bool IsStarbaseHull(Component component)
        {
            return component.Properties != null
                && component.Properties.TryGetValue("Hull", out ComponentProperty property)
                && property is Hull hull
                && hull.IsStarbase;
        }

        /// <summary>The one-time gift parts are hidden until the race has received them.</summary>
        private bool ShouldShow(Component component)
        {
            if (SpecialComponentGrants.IsSpecialGrant(component.Name) && !isGiftReceived(component))
            {
                return false;
            }

            return !showOnlyAvailable || isAvailable(component);
        }

        private void Rebuild()
        {
            Component previous = Current;
            entries = byCategory[categoryIndex].Where(ShouldShow).ToList();

            int keep = previous != null ? entries.IndexOf(previous) : -1;
            Index = keep >= 0 ? keep : (entries.Count > 0 ? 0 : -1);
        }
    }
}
