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
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// A stand-in for the original's ten starbase design slots (design table entries 16-25,
    /// save-turn-file-format.md; ai-opponent-behavior.md §6 "How the ten starbase slots are
    /// read"): the empire's own starbase designs in creation order (ascending design key), the
    /// first ten filling slots 0-9. Nova removes a deleted design from the table, so an empty slot
    /// stands for the original's "marked deleted".
    /// </summary>
    /// <remarks>
    /// Approximation: Nova numbers no slots, and nothing in the AI deletes starbase designs, so
    /// creation order is stable. A game's starting "Starbase" design is slot 0, and a second
    /// starting starbase (Interstellar Traveller / Packet Physics) slot 1, a packet-hub slot. For
    /// an Alternate Reality race the original has the Starter Colony in slot 0 and the Space
    /// Station in slot 1; here the order is the reverse (Nova creates the Starter Colony second).
    /// </remarks>
    public sealed class StarbaseSlots
    {
        public const int SlotCount = 10;

        private readonly ShipDesign[] slots = new ShipDesign[SlotCount];

        public StarbaseSlots(IEnumerable<ShipDesign> ownStarbaseDesigns)
        {
            int index = 0;
            foreach (ShipDesign design in ownStarbaseDesigns.OrderBy(design => design.Key))
            {
                if (index >= SlotCount)
                {
                    break;
                }

                slots[index++] = design;
            }
        }

        /// <summary>The table for <paramref name="empire"/>'s own starbase designs.</summary>
        public static StarbaseSlots ForEmpire(EmpireData empire)
        {
            return new StarbaseSlots(empire.Designs.Values
                .Where(design => design.Owner == empire.Id && AiDesignRoles.IsStarbaseDesign(design)));
        }

        public ShipDesign this[int slot]
        {
            get { return slot >= 0 && slot < SlotCount ? slots[slot] : null; }
        }

        /// <summary>The slot holds a design (the original: not marked deleted).</summary>
        public bool IsLive(int slot)
        {
            return this[slot] != null;
        }

        /// <summary>The slot of a design, or <see cref="StarbaseAdvisors.NoSlot"/> when it is not
        /// one of the ten (the original's "design index above 9").</summary>
        public int SlotOf(ShipDesign design)
        {
            if (design == null)
            {
                return StarbaseAdvisors.NoSlot;
            }

            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (slots[slot] != null && slots[slot].Key == design.Key)
                {
                    return slot;
                }
            }

            return StarbaseAdvisors.NoSlot;
        }

        /// <summary>The slot's creation year (TurnYear units); an empty slot reads as the game's
        /// starting year.</summary>
        public int CreationYear(int slot)
        {
            return AiDesignRoles.CreationYear(this[slot]);
        }

        /// <summary>The slot is live and its design was created later than
        /// <paramref name="otherSlot"/>'s.</summary>
        public bool IsLiveAndNewerThan(int slot, int otherSlot)
        {
            if (!IsLive(slot))
            {
                return false;
            }

            return !IsLive(otherSlot) || CreationYear(slot) > CreationYear(otherSlot);
        }

        /// <summary>The default starbase slot D (`FUN_1090_50e4`): 5 when slot 5 holds a live
        /// design created later than slot 0's, else 0.</summary>
        public int DefaultSlot()
        {
            return IsLiveAndNewerThan(5, 0) ? 5 : 0;
        }

        /// <summary>The packet-hub generation base H (`FUN_1090_50bc`): 6 when slot 6 holds a live
        /// design newer than slot 1's, else 1.</summary>
        public int HubBase()
        {
            return IsLiveAndNewerThan(6, 1) ? 6 : 1;
        }
    }

    /// <summary>
    /// The AI's two starbase production rules (behavior-specs-10/ai-opponent-behavior.md): the
    /// starbase-building advisor `FUN_1090_510a` (§3), run by the end-of-pass routine before the
    /// per-planet chain, and the starbase-upgrade advisor `FUN_1090_52da` (§6), the first advisor
    /// of that chain. Each returns the starbase slot to queue, or <see cref="NoSlot"/>. Population
    /// figures are in stored units of 100 colonists (§13).
    /// </summary>
    public static class StarbaseAdvisors
    {
        public const int NoSlot = -1;

        /// <summary>The flat per-turn chance of a same-generation upgrade, in percent.</summary>
        public const int SameGenerationChance = 6;

        /// <summary>The surface stock of each mineral a same-generation upgrade needs.</summary>
        public const int UpgradeMineralFloor = 200;

        /// <summary>Category 4 upgrades starbases only from this year on.</summary>
        public const int CybertronUpgradeFirstYear = 40;

        /// <summary>
        /// §3 starbase-building advisor, categories 0-3: a freighter hub (§5) that has no starbase
        /// (or one whose design is not among the ten starbase slots), a population above 79 units
        /// and no urgent-supply flag gets one starbase from the default slot D, unless its queue
        /// already holds a starbase. Category 4 uses <see cref="PlanetTestSlot"/> instead; categories
        /// 5 and 7 (no default slot) get nothing.
        /// </summary>
        public static int HubStarbaseSlot(
            int category, bool isHub, bool hasStarbaseInSlots, int populationUnits, bool urgentSupply, bool starbaseQueued, int defaultSlot)
        {
            if (category < AiCategory.Robotoids || category > AiCategory.Rototills)
            {
                return NoSlot;
            }

            if (!isHub || hasStarbaseInSlots || populationUnits <= 79 || urgentSupply || starbaseQueued)
            {
                return NoSlot;
            }

            return defaultSlot;
        }

        /// <summary>
        /// §3 personality 4's planet test `FUN_10a8_2bf8`: an own planet with no starbase, a
        /// habitability above 14 and a population above 499 units gets one starbase, unless one is
        /// already queued: slot 5 when all three mineral concentrations exceed 15, else slot 6; when
        /// that slot is empty or not newer than slot 0 (slot 1 for slot 6), slot 0 (slot 1).
        /// </summary>
        public static int PlanetTestSlot(
            bool hasStarbase, int habitability, int populationUnits, bool allConcentrationsAbove15, bool starbaseQueued, StarbaseSlots slots)
        {
            if (hasStarbase || habitability <= 14 || populationUnits <= 499 || starbaseQueued)
            {
                return NoSlot;
            }

            if (allConcentrationsAbove15)
            {
                return slots.IsLiveAndNewerThan(5, 0) ? 5 : 0;
            }

            return slots.IsLiveAndNewerThan(6, 1) ? 6 : 1;
        }

        /// <summary>Slots 1, 3, 6 and 8 are the packet-hub ladder (§6).</summary>
        public static bool IsHubSlot(int slot)
        {
            return slot == 1 || slot == 3 || slot == 6 || slot == 8;
        }

        /// <summary>
        /// §6 the other-generation upgrade chance in percent: a = the base design's age in years
        /// minus 10, floored at 0 and halved (rounding down) below 50; the chance is a + 5.
        /// </summary>
        public static int OtherGenerationChance(int baseDesignAgeYears)
        {
            int a = Math.Max(0, baseDesignAgeYears - 10);
            if (a < 50)
            {
                a /= 2;
            }

            return a + 5;
        }

        /// <summary>
        /// §6 starbase-upgrade advisor `FUN_1090_52da`, categories 0-4. Nothing for category 7 (its
        /// driver passes no slot) and nothing for category 5, whose own branch is
        /// <see cref="MacintiUpgradeSlot"/> (urgency marks: <see cref="MacintiMarks"/>). Nothing either when the
        /// queue holds a starbase design, when the planet has no starbase (or one outside the ten
        /// slots), or for category 4 before year 40.
        /// - In the current generation (D ≤ s ≤ D + 4, or H ≤ s ≤ H + 2 for a hub slot) and below
        ///   its top rung (4 or 9, 3 or 8 for a hub): slot s + 2, when that slot is live, a 0-99
        ///   roll is below 6 and the planet holds at least 200 kT of each mineral.
        /// - Otherwise, when a 0-99 roll is below <see cref="OtherGenerationChance"/> of the
        ///   current base design (slot D, or H for a hub): the same rung in the current
        ///   generation, (s mod 5) + D, or (s mod 5) + H - 1 for a hub slot. This branch checks
        ///   neither minerals nor whether the target slot holds a design.
        /// </summary>
        /// <param name="defaultBaseAgeYears">Age in years of slot D's design.</param>
        /// <param name="hubBaseAgeYears">Age in years of slot H's design.</param>
        public static int UpgradeSlot(
            int category,
            int yearCounter,
            int starbaseSlot,
            bool starbaseQueued,
            StarbaseSlots slots,
            int defaultBaseAgeYears,
            int hubBaseAgeYears,
            bool mineralsAtLeast200Each,
            Random random)
        {
            if (category < AiCategory.Robotoids || category > AiCategory.Cybertrons)
            {
                return NoSlot;
            }

            if (starbaseQueued || starbaseSlot < 0 || starbaseSlot >= StarbaseSlots.SlotCount)
            {
                return NoSlot;
            }

            if (category == AiCategory.Cybertrons && yearCounter < CybertronUpgradeFirstYear)
            {
                return NoSlot;
            }

            int s = starbaseSlot;
            bool hub = IsHubSlot(s);
            int generationBase = hub ? slots.HubBase() : slots.DefaultSlot();
            int span = hub ? 2 : 4;

            if (s >= generationBase && s <= generationBase + span)
            {
                if (s == generationBase + span)
                {
                    // The top rung: nothing to climb to.
                    return NoSlot;
                }

                int next = s + 2;
                if (!slots.IsLive(next))
                {
                    return NoSlot;
                }

                if (random.Next(100) >= SameGenerationChance || !mineralsAtLeast200Each)
                {
                    return NoSlot;
                }

                return next;
            }

            int chance = OtherGenerationChance(hub ? hubBaseAgeYears : defaultBaseAgeYears);
            if (random.Next(100) >= chance)
            {
                return NoSlot;
            }

            return hub ? (s % 5) + generationBase - 1 : (s % 5) + generationBase;
        }

        // ==================================================================== category 5

        /// <summary>The chassis order of §6/§15 (0 Orbital Fort, 1 Space Dock, 2 Space Station,
        /// 3 Ultra Station, 4 Death Star); -1 for anything else.</summary>
        public static int ChassisIndex(string hullName)
        {
            switch (hullName)
            {
                case "Orbital Fort":
                    return 0;
                case "Space Dock":
                    return 1;
                case "Space Station":
                    return 2;
                case "Ultra Station":
                    return 3;
                case "Death Star":
                    return 4;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// Category 5's ten-entry urgency marks (`FUN_1090_492e`, §6 "Category 5"), one per
        /// starbase slot, from what the slots hold this turn.
        /// <list type="bullet">
        /// <item>Slot 0 is never marked.</item>
        /// <item>Slots 1-3: an empty slot is 1 ("no design could be made": the designer half of
        /// `FUN_1090_492e` is not ported, see the remarks). A live design with no starbase of it in
        /// existence is 0 (the original rebuilds it every turn, and a successful build is marked
        /// 0). A design in use is 3 at 50 years or more, 2 at 35-49, else 0; from year 26 slot 1 is
        /// 3 even when young unless its design is a Space Dock. Only the most urgent of slots 1-3
        /// keeps a mark above 1.</item>
        /// <item>Slots 4-9 start at 1 when empty, else 0. The group (4-6 or 7-9) whose base design
        /// (slot 4 or 7) is older is then marked as a whole: 2 while that base is under 30 years
        /// old, 3 from 30.</item>
        /// <item>Then, if the newer group's base is on a chassis below Death Star, slot 3 becomes
        /// 2; below Ultra Station, slot 2 too.</item>
        /// </list>
        /// </summary>
        /// <remarks>
        /// Not ported: the designer itself (rebuilding slots 1-3 on Space Dock / Space Station /
        /// Ultra Station and an unused group of 4-6 / 7-9 on Death Star, Ultra Station, Space
        /// Station "in three armament variants"): §15 gives no starbase templates. Readings chosen
        /// where §6 is silent (each listed in the report): the group marking needs both base slots
        /// live and of different ages (equal ages, or an empty base, mark neither group, and then
        /// the slot-2/3 override is skipped too). Ties among slots 1-3 for the most urgent mark
        /// follow the spec: the earlier-created design wins, then the lower slot.
        /// </remarks>
        /// <param name="yearCounter">The year counter, 0 in the first year.</param>
        /// <param name="live">Per slot: holds a design (the original: not marked deleted).</param>
        /// <param name="ageYears">Per slot: the design's age in years.</param>
        /// <param name="inUse">Per slot: at least one starbase of the design exists.</param>
        /// <param name="chassis">Per slot: <see cref="ChassisIndex"/> of the design's hull.</param>
        public static int[] MacintiMarks(int yearCounter, bool[] live, int[] ageYears, bool[] inUse, int[] chassis)
        {
            int[] marks = new int[StarbaseSlots.SlotCount];

            for (int slot = 1; slot <= 3; slot++)
            {
                if (!live[slot])
                {
                    marks[slot] = 1;
                    continue;
                }

                if (!inUse[slot])
                {
                    marks[slot] = 0;
                    continue;
                }

                int age = ageYears[slot];
                marks[slot] = age >= 50 ? 3 : (age >= 35 ? 2 : 0);
                if (slot == 1 && yearCounter >= 26 && chassis[slot] != ChassisIndex("Space Dock"))
                {
                    marks[slot] = 3;
                }
            }

            // Only the most urgent of slots 1-3 keeps a mark above 1 (ai-opponent-behavior.md
            // section 6, category 5): the highest mark wins; between equal marks the design created
            // earlier (smaller creation year, i.e. larger age here) wins; between equal ages the
            // lower slot wins (the loop runs low to high, so a later equal mark does not replace).
            int urgent = 0;
            for (int slot = 1; slot <= 3; slot++)
            {
                if (marks[slot] > 1
                    && (urgent == 0
                        || marks[slot] > marks[urgent]
                        || (marks[slot] == marks[urgent] && ageYears[slot] > ageYears[urgent])))
                {
                    urgent = slot;
                }
            }

            for (int slot = 1; slot <= 3; slot++)
            {
                if (slot != urgent && marks[slot] > 1)
                {
                    marks[slot] = 0;
                }
            }

            for (int slot = 4; slot <= 9; slot++)
            {
                marks[slot] = live[slot] ? 0 : 1;
            }

            if (live[4] && live[7] && ageYears[4] != ageYears[7])
            {
                int olderBase = ageYears[4] > ageYears[7] ? 4 : 7;
                int newerBase = olderBase == 4 ? 7 : 4;
                int groupMark = ageYears[olderBase] < 30 ? 2 : 3;
                for (int slot = olderBase; slot < olderBase + 3; slot++)
                {
                    marks[slot] = groupMark;
                }

                if (chassis[newerBase] < ChassisIndex("Death Star"))
                {
                    marks[3] = 2;
                }

                if (chassis[newerBase] < ChassisIndex("Ultra Station"))
                {
                    marks[2] = 2;
                }
            }

            return marks;
        }

        /// <summary>
        /// "Upgrade" in category 5's branch (§6): for a slot below 4, the first slot above it, up
        /// to slot 8, whose mark is 0, else slot 9; for 4 or more, s + 3, or s - 3 when s + 3 would
        /// pass 9.
        /// </summary>
        public static int MacintiUpgradeTarget(int starbaseSlot, int[] marks)
        {
            if (starbaseSlot < 4)
            {
                for (int slot = starbaseSlot + 1; slot <= 8; slot++)
                {
                    if (marks[slot] == 0)
                    {
                        return slot;
                    }
                }

                return 9;
            }

            return starbaseSlot + 3 <= 9 ? starbaseSlot + 3 : starbaseSlot - 3;
        }

        /// <summary>
        /// Category 5's branch of the starbase-upgrade advisor `FUN_1090_52da` (§6):
        /// <list type="number">
        /// <item>upgrade when slot s is marked 3, or marked 2 and a 1-in-10 roll succeeds;</item>
        /// <item>otherwise, for s of 4, 5, 7 or 8, an 8% roll (a 0-99 roll below 8) queues
        /// s + 1;</item>
        /// <item>otherwise, for s of 0-3, upgrade only when the population is at least 16% of
        /// capacity, with a chance of (percentage - 15) × 6% (certain from 32%).</item>
        /// </list>
        /// The caller has already applied the queue test (only for a planet with a starbase).
        /// </summary>
        /// <param name="populationPercent">`FUN_1048_476c`: population × 100 ÷ capacity,
        /// rounded to the nearest whole number, capped at 999.</param>
        public static int MacintiUpgradeSlot(int starbaseSlot, int[] marks, int populationPercent, Random random)
        {
            if (starbaseSlot < 0 || starbaseSlot >= StarbaseSlots.SlotCount)
            {
                return NoSlot;
            }

            int s = starbaseSlot;
            if (marks[s] == 3 || (marks[s] == 2 && random.Next(10) == 0))
            {
                return MacintiUpgradeTarget(s, marks);
            }

            if (s == 4 || s == 5 || s == 7 || s == 8)
            {
                return random.Next(100) < 8 ? s + 1 : NoSlot;
            }

            if (s <= 3 && populationPercent >= 16 && random.Next(100) < (populationPercent - 15) * 6)
            {
                return MacintiUpgradeTarget(s, marks);
            }

            return NoSlot;
        }

        /// <summary>`FUN_1048_476c`: population × 100 ÷ capacity, rounded to the nearest whole
        /// number and capped at 999. A planet with no capacity (an Alternate Reality planet with no
        /// starbase) reads 0, not 999 - which is why category 5's population upgrade rule can never
        /// fire there (ai-opponent-behavior.md section 6, category 5).</summary>
        public static int PopulationPercent(double colonists, double capacityColonists)
        {
            if (colonists <= 0 || capacityColonists <= 0)
            {
                return 0;
            }

            return (int)Math.Min(999, Math.Floor((colonists * 100.0 / capacityColonists) + 0.5));
        }
    }
}
