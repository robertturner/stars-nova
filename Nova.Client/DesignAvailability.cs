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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>The graded result of the tech-level prerequisite check.</summary>
    public enum TechShortfallGrade
    {
        /// <summary>Every field is at or above its requirement.</summary>
        Available,

        /// <summary>Exactly one field is short, it is the field being researched, and the gap is one level.</summary>
        OneLevelAway,

        /// <summary>Exactly one field is short, it is the field being researched, and the gap is two or more levels.</summary>
        Further,

        /// <summary>Two or more fields are short, or the single short field is not the one being researched.</summary>
        FarAway,
    }

    /// <summary>The grade and, for the two near-miss grades, the number of levels the researched field is short.</summary>
    public struct TechShortfall
    {
        public TechShortfallGrade Grade;

        /// <summary>Levels short in the single researched field (1 for OneLevelAway, 2+ for Further); 0 otherwise.</summary>
        public int Gap;

        /// <summary>
        /// The original's return value: 0 available, 1 for a one-level gap, the gap plus one for
        /// a larger gap, and <see cref="DesignAvailability.FarAwayCode"/> otherwise.
        /// </summary>
        public int Code
        {
            get
            {
                switch (Grade)
                {
                    case TechShortfallGrade.Available:
                        return 0;
                    case TechShortfallGrade.OneLevelAway:
                        return 1;
                    case TechShortfallGrade.Further:
                        return Gap + 1;
                    default:
                        return DesignAvailability.FarAwayCode;
                }
            }
        }
    }

    /// <summary>
    /// Pure design-editor checks (behavior-specs-10/ship-design-and-components.md §3 and §11,
    /// research-tech-tree.md §4 "How the game checks the bytes").
    /// </summary>
    public static class DesignAvailability
    {
        /// <summary>
        /// SPEC GAP (reported): research-tech-tree.md §4 calls the "far away" result "one fixed
        /// value" without giving it; this stand-in only has to differ from every near-miss code.
        /// </summary>
        public const int FarAwayCode = int.MaxValue;

        /// <summary>
        /// The graded prerequisite check (FUN_1008_5916): requirements are met when every field
        /// of <paramref name="current"/> is at least the item's <paramref name="required"/> level
        /// (a requirement of 0 is always met). Otherwise, when exactly one field is short and it
        /// is <paramref name="researching"/>, the result is "one level away" for a one-level gap
        /// and "further" (gap + 1) for a larger gap; every other case - two or more short fields,
        /// or one short field that is not the one being researched - is "far away". Availability
        /// itself is the plain per-field minimum; the near-miss grades are display-only.
        /// </summary>
        public static TechShortfall GradeTechShortfall(TechLevel required, TechLevel current, TechLevel.ResearchField? researching)
        {
            List<TechLevel.ResearchField> shortFields = Research.OriginalFieldOrder
                .Where(field => current[field] < required[field])
                .ToList();

            if (shortFields.Count == 0)
            {
                return new TechShortfall { Grade = TechShortfallGrade.Available };
            }

            if (shortFields.Count == 1 && researching.HasValue && shortFields[0] == researching.Value)
            {
                int gap = required[shortFields[0]] - current[shortFields[0]];
                return new TechShortfall { Grade = gap == 1 ? TechShortfallGrade.OneLevelAway : TechShortfallGrade.Further, Gap = gap };
            }

            return new TechShortfall { Grade = TechShortfallGrade.FarAway };
        }

        /// <summary>
        /// The field an empire is researching: the first field of its research topics set to 1
        /// (the same rule the server's research pass uses), or null when none is.
        /// </summary>
        public static TechLevel.ResearchField? ResearchingField(TechLevel researchTopics)
        {
            if (researchTopics == null)
            {
                return null;
            }

            foreach (TechLevel.ResearchField field in System.Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (researchTopics[field] == 1)
                {
                    return field;
                }
            }

            return null;
        }

        /// <summary>
        /// The retroactive fleet-consistency check before a slot is removed from a saved design
        /// (ship-design-and-components.md §3, FUN_10f0_0ff0): every existing fleet of the design's
        /// owner that holds ships of this design and has the slot filled. A non-empty result means
        /// the editor must ask for confirmation (a yes/no prompt; "no" aborts the whole edit);
        /// an empty one lets the removal proceed silently.
        /// </summary>
        /// <remarks>
        /// In this port every ship of a design shares the design's own slot contents (there is no
        /// per-fleet cached slot count), so "that fleet already has that slot filled" is the
        /// design's slot being occupied, and the fleets affected are those holding the design.
        /// </remarks>
        /// <param name="fleets">The owner's fleets.</param>
        /// <param name="design">The saved design being edited.</param>
        /// <param name="slotIndex">The index in <c>design.Hull.Modules</c> of the slot being emptied.</param>
        public static List<Fleet> FleetsAffectedBySlotRemoval(IEnumerable<Fleet> fleets, ShipDesign design, int slotIndex)
        {
            List<Fleet> affected = new List<Fleet>();
            if (fleets == null || design == null || design.Hull == null || slotIndex < 0 || slotIndex >= design.Hull.Modules.Count)
            {
                return affected;
            }

            HullModule slot = design.Hull.Modules[slotIndex];
            if (slot.AllocatedComponent == null || slot.ComponentCount <= 0)
            {
                return affected;
            }

            foreach (Fleet fleet in fleets)
            {
                if (fleet != null && fleet.Composition.Values.Any(token => token.Design != null && token.Design.Key == design.Key && token.Quantity > 0))
                {
                    affected.Add(fleet);
                }
            }

            return affected;
        }

        /// <summary>
        /// Whether removing <paramref name="slotIndex"/> from <paramref name="design"/> needs the
        /// confirmation prompt (see <see cref="FleetsAffectedBySlotRemoval"/>).
        /// </summary>
        public static bool SlotRemovalNeedsConfirmation(IEnumerable<Fleet> fleets, ShipDesign design, int slotIndex)
        {
            return FleetsAffectedBySlotRemoval(fleets, design, slotIndex).Count > 0;
        }
    }
}
