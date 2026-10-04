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

    /// <summary>
    /// The original's AI "category" (behavior-specs-10/ai-opponent-behavior.md §1 and §1a): the
    /// 3-bit archetype field the personality dispatcher switches on. 0-5 are the six setup-menu
    /// archetypes, 6 has no driver at all and 7 is the economy-only driver of an Expansion player
    /// or an inactive human.
    /// </summary>
    /// <remarks>
    /// Nova's own <c>-n</c> personality code (DefaultAi) predates the spec's category table and
    /// numbers its slots differently: 0 is the disabled slot, 1 the passive one and 2-7 the
    /// "Standard" range, default 4. <see cref="ForPersonality"/> maps it onto the spec: Nova 0 is
    /// category 6, Nova 1 is category 7 and Nova 2-7 are categories 0-5 in order. So Nova's
    /// default code 4 plays category 2 (Automitrons). That choice is this rebuild's own (the spec
    /// leaves "which archetype Nova's single AI represents" as a decision), made because category
    /// 2 is the one whose roles match what Nova's shared path already does: scouts as explorers
    /// (§12 personality 2, slot 0), a colony ship, freighters routed by the shared router of §5
    /// (categories 0-3 only) and the Defenses and Terraform advisors of §6.
    /// </remarks>
    public static class AiCategory
    {
        public const int Robotoids = 0;
        public const int Turindrones = 1;
        public const int Automitrons = 2;
        public const int Rototills = 3;
        public const int Cybertrons = 4;
        public const int Macinti = 5;
        public const int NoDriver = 6;
        public const int EconomyOnly = 7;

        /// <summary>The skill field's values (§1a): 0 Easy, 1 Standard, 2 Tough, 3 Expert. Nova
        /// has no skill picker, so its AI plays Standard.</summary>
        public const int StandardSkill = 1;

        /// <summary>Maps Nova's <c>-n</c> personality code onto the spec's category (see the
        /// class remarks). Codes above 7 clamp to category 5, below 0 to the disabled slot.</summary>
        public static int ForPersonality(int novaPersonalityCode)
        {
            if (novaPersonalityCode <= DefaultAi.DisabledPersonality)
            {
                return NoDriver;
            }

            if (novaPersonalityCode == DefaultAi.PassivePersonality)
            {
                return EconomyOnly;
            }

            int clamped = Math.Min(DefaultAi.MaxStandardPersonality, novaPersonalityCode);
            return clamped - DefaultAi.MinStandardPersonality;
        }

        /// <summary>
        /// Colonists an idle colony fleet loads before it leaves (§12, tabulated in §13), in the
        /// original's stored units of 100 colonists (1 unit = 1 kT of hold): category 0 loads 10,
        /// categories 1 and 3 load 25, category 2 loads 150, category 4 loads 250 and category 5
        /// loads min(25, population ÷ 10). The caller still caps the load by the free hold and
        /// the planet's own population.
        /// </summary>
        public static int ColonistLoadUnits(int category, int planetPopulationUnits)
        {
            switch (category)
            {
                case Robotoids:
                    return 10;
                case Turindrones:
                case Rototills:
                    return 25;
                case Automitrons:
                    return 150;
                case Cybertrons:
                    return 250;
                case Macinti:
                    return Math.Min(25, Math.Max(0, planetPopulationUnits) / 10);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Whether a colony fleet standing at a planet with no colonization target is scrapped
        /// there (§12: categories 0 and 5 scrap it) rather than left to try again next turn
        /// (category 2 "keeps its colonists and tries again next turn"; category 4 "simply
        /// waits; it is never scrapped for that"). Category 1 and 3's own fallbacks (a Transport
        /// landing, a wormhole) are not ported, so they wait too.
        /// </summary>
        public static bool ScrapsTargetlessColonyFleet(int category)
        {
            return category == Robotoids || category == Macinti;
        }

        /// <summary>
        /// §2: for categories other than 0 and 5 the colonization search leaves out planets with
        /// negative terraformed habitability, and its "already claimed" classification is built
        /// once per run from every own fleet's next waypoint. Categories 0 and 5 have no
        /// habitability filter and re-check claims (Colonize waypoints only) on every search.
        /// </summary>
        public static bool UsesColonyHabitabilityFilter(int category)
        {
            return category != Robotoids && category != Macinti;
        }
    }
}
