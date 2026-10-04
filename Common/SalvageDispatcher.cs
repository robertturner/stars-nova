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
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common.Components;

    /// <summary>
    /// The two tables a salvage roll reads, filled from the designs (or, for a captured planet,
    /// the former owner's tech levels) involved in the battle, the scrapping or the capture
    /// (behavior-specs-11/turn-generation-engine.md §5, `FUN_1080_244c`):
    /// <list type="bullet">
    /// <item>the six research-field thresholds - the highest level any part or hull requires; and</item>
    /// <item>the 13-entry rare-part table - one percentage point per installed unit of a rare part,
    /// capped at 25 (bits 0-7, 9 and 11; Mini Morph, Genesis Device and the ships item are never
    /// fed by salvage).</item>
    /// </list>
    /// </summary>
    public sealed class SalvageTables
    {
        /// <summary>Per research field, the highest required level seen (indexed by the field enum).</summary>
        public readonly int[] ResearchThresholds = new int[6];

        /// <summary>Per rare-part gift bit (0-12), the accumulated percentage, capped at 25.</summary>
        public readonly int[] RarePercent = new int[SpecialComponentGrants.GiftBitCount];

        public bool AnyResearch
        {
            get
            {
                foreach (int threshold in ResearchThresholds)
                {
                    if (threshold > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public bool AnyRare
        {
            get
            {
                foreach (int percent in RarePercent)
                {
                    if (percent > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Adds one design and the number of ships of it involved.</summary>
        public void AddDesign(ShipDesign design, int quantity)
        {
            if (design == null || quantity <= 0)
            {
                return;
            }

            AddRequiredTech(design.Blueprint != null ? design.Blueprint.RequiredTech : null);

            if (design.Hull == null)
            {
                return;
            }

            foreach (HullModule module in design.Hull.Modules)
            {
                Component component = module?.AllocatedComponent;
                if (component == null || module.ComponentCount <= 0)
                {
                    continue;
                }

                AddRequiredTech(component.RequiredTech);

                int bit = RareBit(component.Name);
                if (bit >= 0)
                {
                    int units = quantity * module.ComponentCount;
                    RarePercent[bit] = Math.Min(25, RarePercent[bit] + units);
                }
            }
        }

        /// <summary>Adds every ship of a fleet's designs (the Scrap Fleet / Scrap-at-starbase case).</summary>
        public void AddFleet(Fleet fleet)
        {
            if (fleet == null)
            {
                return;
            }

            foreach (ShipToken token in fleet.Composition.Values)
            {
                AddDesign(token.Design, token.Quantity);
            }
        }

        /// <summary>Adds a raw tech profile (the captured planet's former owner's levels).</summary>
        public void AddTechLevel(TechLevel tech)
        {
            AddRequiredTech(tech);
        }

        /// <summary>Merges another table's entries in (thresholds take the max, rare percentages add).</summary>
        public void Merge(SalvageTables other)
        {
            if (other == null)
            {
                return;
            }

            for (int field = 0; field < ResearchThresholds.Length; field++)
            {
                if (other.ResearchThresholds[field] > ResearchThresholds[field])
                {
                    ResearchThresholds[field] = other.ResearchThresholds[field];
                }
            }

            for (int bit = 0; bit < RarePercent.Length; bit++)
            {
                RarePercent[bit] = Math.Min(25, RarePercent[bit] + other.RarePercent[bit]);
            }
        }

        private void AddRequiredTech(TechLevel tech)
        {
            if (tech == null)
            {
                return;
            }

            for (int field = 0; field < ResearchThresholds.Length; field++)
            {
                int level = tech[(TechLevel.ResearchField)field];
                if (level > ResearchThresholds[field])
                {
                    ResearchThresholds[field] = level;
                }
            }
        }

        /// <summary>The gift bit of a salvage-feeding rare part, or -1.</summary>
        private static int RareBit(string componentName)
        {
            if (string.IsNullOrEmpty(componentName)
                || !SpecialComponentGrants.SalvageableComponents.Contains(componentName))
            {
                return -1;
            }

            for (int bit = 0; bit < SpecialComponentGrants.Components.Count; bit++)
            {
                if (SpecialComponentGrants.Components[bit] == componentName)
                {
                    return bit;
                }
            }

            return -1;
        }
    }

    /// <summary>What one salvage success gave the race.</summary>
    public sealed class SalvageResult
    {
        /// <summary>The rare part gained, or null.</summary>
        public string PartName;

        /// <summary>The research field the banked price went to, or null.</summary>
        public TechLevel.ResearchField? Field;

        /// <summary>The resources banked into that field's research pool.</summary>
        public int BankedResources;
    }

    /// <summary>
    /// The shared salvage / reverse-engineering dispatcher (`FUN_10f0_61a2`, behavior-specs-11/
    /// turn-generation-engine.md §5): called at the end of a battle, when Scrap Fleet dismantles at
    /// a starbase, and on planet capture. A race that has not already had a salvage success this
    /// generation rolls once; a 0-99 draw must exceed 49, then a random rare-part slot is tried
    /// first (success chance is that slot's percentage, and only for a part the race lacks), and
    /// otherwise a random research field is tried (if the race's level is below its threshold, the
    /// next level's price is banked into that field's research pool). At most one success per race
    /// per generation (<see cref="EmpireData.TechGainedThisTurn"/>).
    /// </summary>
    public static class SalvageDispatcher
    {
        public static SalvageResult TryGain(EmpireData receiver, SalvageTables tables, Random random)
        {
            if (receiver == null || tables == null || random == null || receiver.TechGainedThisTurn)
            {
                return null;
            }

            // A draw of 0-99 must exceed 49 (a 50% chance, the spec's "exceed 49").
            if (random.Next(100) <= 49)
            {
                return null;
            }

            if (tables.AnyRare)
            {
                int slot = random.Next(SpecialComponentGrants.GiftBitCount);
                string name = SpecialComponentGrants.GiftBitName(slot);
                int percent = tables.RarePercent[slot];

                bool salvageable = SpecialComponentGrants.SalvageableComponents.Contains(name);
                bool alreadyHas = receiver.GrantedSpecialComponents != null
                    && receiver.GrantedSpecialComponents.Contains(name);

                if (salvageable && !alreadyHas && percent > 0 && random.Next(100) < percent)
                {
                    GrantPart(receiver, name);
                    receiver.TechGainedThisTurn = true;
                    return new SalvageResult { PartName = name };
                }
            }

            if (tables.AnyResearch)
            {
                int fieldIndex = random.Next(6);
                TechLevel.ResearchField field = (TechLevel.ResearchField)fieldIndex;
                if (tables.ResearchThresholds[fieldIndex] > receiver.ResearchLevels[field])
                {
                    int cost = Research.Cost(field, receiver.Race, receiver.ResearchLevels, receiver.ResearchLevels[field] + 1);
                    receiver.ResearchResources[field] += cost;
                    receiver.TechGainedThisTurn = true;
                    return new SalvageResult { Field = field, BankedResources = cost };
                }
            }

            return null;
        }

        /// <summary>
        /// Grants a rare part: records it as granted and makes it buildable now when the race's
        /// tech already allows it (StarUpdateStep.TechLevelUp handles the other order).
        /// </summary>
        private static void GrantPart(EmpireData empire, string name)
        {
            empire.GrantedSpecialComponents.Add(name);

            Component component = new AllComponents().Fetch(name);
            if (component != null && empire.ResearchLevels >= component.RequiredTech
                && !RaceComponents.IsRestrictedFor(component, empire.Race))
            {
                empire.AvailableComponents.Add(component);
            }
        }
    }
}
