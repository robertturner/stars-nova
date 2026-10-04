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

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The ship designer's slot-category rule (behavior-specs-10/ship-design-and-components.md
    /// §4 and §15e): every component carries exactly one of the sixteen category bits, every hull
    /// slot carries an allowed-category mask, and a component is legal in a slot when its one bit
    /// is in the slot's mask. This port's hull data (components.xml) spells each slot's mask out
    /// as a list of family names in <c>HullModule.ComponentType</c> ("Weapon", "Shield or Armor",
    /// "Scanner Electrical Mechanical", "Armor Scanner Elect Mech", "General Purpose", ...), so
    /// the mask is read back from those names here. §15e's recurring masks: a weapons slot is
    /// beam-plus-torpedo; engine, bomb, mining and mine-layer slots are single-category; the
    /// scanner/electrical/mechanical and shield/armor combinations recur; a general-purpose slot
    /// is "the union of the eight ship-mountable families" (see <see cref="GeneralPurposeFamilies"/>).
    /// Also kept here (from the older designer): a component with a Hull Affinity only fits that
    /// hull, and a "Transport Ships Only" part will not sit on a hull that already carries a weapon.
    /// </summary>
    public static class SlotCompatibility
    {
        /// <summary>The slot type name of a general-purpose slot in components.xml.</summary>
        public const string GeneralPurposeSlot = "General Purpose";

        /// <summary>
        /// The families a general-purpose slot accepts. The spec (§15e) says "the union of the
        /// eight ship-mountable families" without listing them. AMBIGUITY (reported): of the
        /// sixteen categories, engines have their own single-category slot and the spec names
        /// bombs, mining robots and mine layers as single-category slots too; the stand-in reading
        /// is the eight families that are neither engines, nor planetary/starbase-only (hulls,
        /// chassis, stargates and mass drivers, planetary installations), nor the two single-slot
        /// ground-work families (bombs, mining robots): scanners, shields, armor, beam weapons,
        /// torpedoes, electrical, mechanical and mine layers. Change this one set if the spec team
        /// names a different eight.
        /// </summary>
        public static readonly IReadOnlyCollection<ItemType> GeneralPurposeFamilies = new HashSet<ItemType>
        {
            ItemType.Scanner,
            ItemType.Shield,
            ItemType.Armor,
            ItemType.BeamWeapons,
            ItemType.Torpedoes,
            ItemType.Electrical,
            ItemType.Mechanical,
            ItemType.MineLayer,
        };

        /// <summary>
        /// The family names that can appear in a slot's ComponentType, in the spelling (and the
        /// abbreviations) components.xml uses, mapped to the component types they admit. Words
        /// not listed here ("or", "Base", "Cargo", "Space", "Dock") admit nothing: a "Base Cargo"
        /// or "Space Dock" slot is a built-in feature of the hull, not a mount.
        /// </summary>
        private static readonly (string Word, ItemType[] Types)[] FamilyWords =
        {
            (GeneralPurposeSlot, null),
            ("Mining Robot", new[] { ItemType.MiningRobot }),
            ("Mine Layer", new[] { ItemType.MineLayer }),
            ("Engine", new[] { ItemType.Engine }),
            ("Scanner", new[] { ItemType.Scanner }),
            ("Shield", new[] { ItemType.Shield }),
            ("Armor", new[] { ItemType.Armor }),
            ("Weapon", new[] { ItemType.BeamWeapons, ItemType.Torpedoes }),
            ("Bomb", new[] { ItemType.Bomb }),
            ("Electrical", new[] { ItemType.Electrical }),
            ("Elect", new[] { ItemType.Electrical }),
            ("Mechanical", new[] { ItemType.Mechanical }),
            ("Mech", new[] { ItemType.Mechanical }),
            ("Orbital", new[] { ItemType.Orbital, ItemType.Gate }),
        };

        /// <summary>
        /// The component types a slot of the given ComponentType admits (its allowed-category
        /// mask, as a set). An unknown or empty slot type admits nothing.
        /// </summary>
        public static HashSet<ItemType> AllowedTypes(string slotType)
        {
            HashSet<ItemType> allowed = new HashSet<ItemType>();
            if (string.IsNullOrWhiteSpace(slotType))
            {
                return allowed;
            }

            string remaining = slotType;
            foreach ((string word, ItemType[] types) in FamilyWords)
            {
                int at = remaining.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                while (at >= 0)
                {
                    foreach (ItemType type in types ?? GeneralPurposeFamilies)
                    {
                        allowed.Add(type);
                    }

                    // Blank the matched word so "Electrical" is not re-read as "Elect" and
                    // "Mine Layer" is not also read as a bare "Mine".
                    remaining = remaining.Substring(0, at) + new string(' ', word.Length) + remaining.Substring(at + word.Length);
                    at = remaining.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                }
            }

            return allowed;
        }

        /// <summary>Whether a slot of the given ComponentType admits a component of <paramref name="componentType"/>.</summary>
        public static bool SlotAdmits(string slotType, ItemType componentType)
        {
            return AllowedTypes(slotType).Contains(componentType);
        }

        /// <summary>
        /// The full designer check for one component in one slot: the category mask, then the
        /// component's Hull Affinity (it only fits the hull it names) and the "Transport Ships
        /// Only" rule (it will not share a hull with an allocated weapon). Hulls never go in a
        /// slot.
        /// </summary>
        /// <param name="component">The component being placed.</param>
        /// <param name="slot">The slot it is being placed in.</param>
        /// <param name="hullName">The name of the hull being designed.</param>
        /// <param name="allModulesOnHull">Every slot of the hull, with their current allocations.</param>
        public static bool Accepts(Component component, HullModule slot, string hullName, IEnumerable<HullModule> allModulesOnHull)
        {
            if (component == null || slot == null || component.Properties.ContainsKey("Hull"))
            {
                return false;
            }

            if (!SlotAdmits(slot.ComponentType, component.Type))
            {
                return false;
            }

            if (component.Properties.TryGetValue("Hull Affinity", out ComponentProperty affinityProperty)
                && affinityProperty is HullAffinity affinity
                && affinity.Value != hullName)
            {
                return false;
            }

            if (component.Properties.ContainsKey("Transport Ships Only") && allModulesOnHull != null)
            {
                foreach (HullModule otherSlot in allModulesOnHull)
                {
                    if (otherSlot != null && otherSlot.AllocatedComponent != null && otherSlot.AllocatedComponent.Properties.ContainsKey("Weapon"))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
