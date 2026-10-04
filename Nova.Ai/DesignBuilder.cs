#region Copyright Notice
// ============================================================================
// Copyright (C) 2009 - 2017 stars-nova
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

namespace Nova.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The AI design builder (behavior-specs-10/ai-opponent-behavior.md §15, `FUN_1090_0000`
    /// with the part resolver `FUN_1090_036a`): a hull plus a template of one part-group number
    /// per hull slot. For each slot the resolver walks the group's parts best first and installs
    /// the first one the race can build, filling the slot to its full capacity; if any slot finds
    /// nothing the whole design fails (null) and the caller tries another template or hull.
    /// </summary>
    /// <remarks>
    /// Pure: it reads only the component dictionary it is given (the empire's
    /// <c>AvailableComponents</c>, which is the shared race/tech availability check of
    /// ship-design-and-components.md §4-§5) and draws only from the Random it is given.
    /// One addition the spec does not state: a part is accepted only if the hull slot accepts its
    /// category (and a components.xml module of the hull accepts it), so a template group that
    /// starts with, say, an armour in a slot that takes no armour skips to the group's next part.
    /// </remarks>
    public sealed class DesignBuilder
    {
        private readonly IDictionary<string, Component> available;
        private readonly Random random;

        public DesignBuilder(IDictionary<string, Component> availableComponents, Random random)
        {
            available = availableComponents ?? throw new ArgumentNullException(nameof(availableComponents));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>The available component for a spec part name (trying components.xml's own
        /// spelling where it differs), or null.</summary>
        public Component FindAvailable(string specName)
        {
            foreach (string name in DesignPartGroups.NovaNames(specName))
            {
                if (available.TryGetValue(name, out Component component) && component != null)
                {
                    return component;
                }
            }

            return null;
        }

        public bool CanBuildHull(string hullName)
        {
            return FindAvailable(hullName) != null;
        }

        /// <summary>The first part of a group the race can build that a slot accepting
        /// <paramref name="slotAccepts"/> can take, best first, or null.</summary>
        public PartCandidate? ResolvePart(int group, SlotMask slotAccepts)
        {
            foreach (PartCandidate candidate in DesignPartGroups.Candidates(group))
            {
                if ((slotAccepts & DesignPartGroups.MaskOf(candidate.Category)) != 0 && FindAvailable(candidate.Name) != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds one design from one template, or returns null when the hull is not available
        /// or any slot finds no part. The template must have one group per spec slot of the hull.
        /// </summary>
        public ShipDesign Build(string hullName, IReadOnlyList<int> template, long designKey, string name)
        {
            IReadOnlyList<HullSlot> slots = DesignBuilderHulls.Slots(hullName);
            if (template.Count != slots.Count)
            {
                throw new ArgumentException("Template has " + template.Count + " groups but hull " + hullName + " has " + slots.Count + " slots.");
            }

            Component hullComponent = FindAvailable(hullName);
            if (hullComponent == null || !(hullComponent.Properties.TryGetValue("Hull", out ComponentProperty property) && property is Hull))
            {
                return null;
            }

            Component blueprint = new Component(hullComponent);
            List<HullModule> modules = ((Hull)blueprint.Properties["Hull"]).Modules;
            foreach (HullModule module in modules)
            {
                module.Empty();
            }

            bool[] used = new bool[modules.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                if (!TryFill(slots[i], template[i], modules, used))
                {
                    return null;
                }
            }

            ShipDesign design = new ShipDesign(designKey);
            design.Blueprint = blueprint;
            design.Name = name;
            design.Type = ItemType.Ship;
            design.Icon = new ShipIcon(hullComponent.ImageFile, hullComponent.ComponentImage);
            design.Update();
            return design;
        }

        /// <summary>
        /// Tries the alternatives of one hull in random order (§15: "or" separates the
        /// alternatives a routine picks among at random; a failed one leaves the caller to try
        /// another) and returns the first design that builds, or null.
        /// </summary>
        public ShipDesign BuildAny(string hullName, IReadOnlyList<int[]> alternatives, long designKey, string name)
        {
            foreach (int[] template in Shuffled(alternatives))
            {
                ShipDesign design = Build(hullName, template, designKey, name);
                if (design != null)
                {
                    return design;
                }
            }

            return null;
        }

        /// <summary>Parses a template written as in the spec, e.g. "8.11.10.1.1".</summary>
        public static int[] ParseTemplate(string dotted)
        {
            return dotted.Split('.').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        }

        /// <summary>
        /// Whether two designs are the same build: same hull and the same part and count in
        /// every module (used to avoid re-adding an identical design).
        /// </summary>
        public static bool SameBuild(ShipDesign a, ShipDesign b)
        {
            if (a == null || b == null || a.Blueprint == null || b.Blueprint == null || a.Blueprint.Name != b.Blueprint.Name)
            {
                return false;
            }

            List<string> left = Signature(a);
            List<string> right = Signature(b);
            return left.SequenceEqual(right);
        }

        private static List<string> Signature(ShipDesign design)
        {
            List<string> parts = new List<string>();
            if (design.Blueprint.Properties.TryGetValue("Hull", out ComponentProperty property) && property is Hull hull && hull.Modules != null)
            {
                foreach (HullModule module in hull.Modules)
                {
                    if (module.AllocatedComponent != null)
                    {
                        parts.Add(module.AllocatedComponent.Name + "x" + module.ComponentCount);
                    }
                }
            }

            parts.Sort(StringComparer.Ordinal);
            return parts;
        }

        private List<T> Shuffled<T>(IReadOnlyList<T> items)
        {
            List<T> list = items.ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }

            return list;
        }

        private bool TryFill(HullSlot slot, int group, List<HullModule> modules, bool[] used)
        {
            foreach (PartCandidate candidate in DesignPartGroups.Candidates(group))
            {
                SlotMask category = DesignPartGroups.MaskOf(candidate.Category);
                if ((slot.Accepts & category) == 0)
                {
                    continue;
                }

                Component part = FindAvailable(candidate.Name);
                if (part == null)
                {
                    continue;
                }

                int moduleIndex = PickModule(slot, category, modules, used);
                if (moduleIndex < 0)
                {
                    continue;
                }

                HullModule module = modules[moduleIndex];
                module.AllocatedComponent = part;
                module.ComponentCount = module.ComponentMaximum;
                used[moduleIndex] = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// The components.xml module standing for a spec slot: an unused module that accepts the
        /// part's category, preferring one accepting exactly what the spec slot accepts and of the
        /// same capacity, then the first in module order.
        /// </summary>
        private static int PickModule(HullSlot slot, SlotMask category, List<HullModule> modules, bool[] used)
        {
            int best = -1;
            int bestScore = -1;
            for (int i = 0; i < modules.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                SlotMask accepts = DesignBuilderHulls.NovaModuleAccepts(modules[i].ComponentType);
                if ((accepts & category) == 0)
                {
                    continue;
                }

                int score = (accepts == slot.Accepts ? 2 : 0) + (modules[i].ComponentMaximum == slot.Capacity ? 1 : 0);
                if (score > bestScore)
                {
                    best = i;
                    bestScore = score;
                }
            }

            return best;
        }
    }
}
