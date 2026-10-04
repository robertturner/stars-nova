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
    using System.Globalization;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The one status line printed on a component/tech detail card under its list of required
    /// tech levels (behavior-specs-10/research-tech-tree.md §4 "the four variants, exact";
    /// client-ui-dialog-catalog.md "Slot/equipment editor"):
    /// - forbidden for the race, not a valid item, or any requirement above 26: a red
    ///   "unavailable" label (dynamic string 850);
    /// - otherwise the research resources still needed to raise every deficient field to the
    ///   item's requirement (the cost of every missing level, field by field, less what is
    ///   already banked in that field, never below zero per field):
    ///   0 prints "available" (851), 1-99,999 the number (849), 100,000 or more the number in
    ///   thousands with a "k" suffix, rounded to the nearest thousand (848).
    /// The resolver's finer "one level short / further away" grades get no wording of their own.
    /// </summary>
    public static class TechStatusLine
    {
        /// <summary>Returned by <see cref="ResourcesStillNeeded"/> for an unresearchable item.</summary>
        public const int Unavailable = -1;

        /// <summary>The kind of line the card shows.</summary>
        public enum Kind
        {
            Unavailable,
            Available,
            Cost,
            CostInThousands,
        }

        /// <summary>
        /// Research resources still needed to bring every field below <paramref name="required"/>
        /// up to it, simulating the climb level by level (each level's cost uses the levels
        /// reached so far, as Research.Cost's total-levels surcharge requires) and crediting each
        /// field's banked resources against that field's own total, never below zero. The real
        /// levels are not modified. <see cref="Unavailable"/> when any requirement is above
        /// <see cref="TechLevel.MaxLevel"/>.
        /// </summary>
        public static int ResourcesStillNeeded(Race race, TechLevel current, TechLevel banked, TechLevel required)
        {
            if (race == null || current == null || required == null)
            {
                return Unavailable;
            }

            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (required[field] > TechLevel.MaxLevel)
                {
                    return Unavailable;
                }
            }

            TechLevel trial = new TechLevel(current);
            long total = 0;

            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                long fieldCost = 0;
                for (int level = current[field] + 1; level <= required[field]; level++)
                {
                    fieldCost += Research.Cost(field, race, trial, level);
                    trial[field] = level;
                }

                if (fieldCost > 0)
                {
                    int bankedHere = banked != null ? banked[field] : 0;
                    total += Math.Max(0, fieldCost - bankedHere);
                }
            }

            return (int)Math.Min(total, int.MaxValue);
        }

        /// <summary>
        /// The status for a component: unavailable when the race's traits forbid it (or it is
        /// null), else <see cref="ResourcesStillNeeded"/> for its RequiredTech.
        /// </summary>
        public static int ForComponent(Component component, Race race, TechLevel current, TechLevel banked)
        {
            if (component == null || race == null || RaceComponents.IsRestrictedFor(component, race))
            {
                return Unavailable;
            }

            return ResourcesStillNeeded(race, current, banked, component.RequiredTech);
        }

        /// <summary>The kind of line for a figure from <see cref="ResourcesStillNeeded"/>.</summary>
        public static Kind KindOf(int resourcesStillNeeded)
        {
            if (resourcesStillNeeded < 0)
            {
                return Kind.Unavailable;
            }

            if (resourcesStillNeeded == 0)
            {
                return Kind.Available;
            }

            return resourcesStillNeeded < 100000 ? Kind.Cost : Kind.CostInThousands;
        }

        /// <summary>The line's text (our wording; the original's strings 848-851).</summary>
        public static string Format(int resourcesStillNeeded)
        {
            switch (KindOf(resourcesStillNeeded))
            {
                case Kind.Unavailable:
                    return "Unavailable";
                case Kind.Available:
                    return "Available";
                case Kind.Cost:
                    return resourcesStillNeeded.ToString(CultureInfo.InvariantCulture) + " resources needed";
                default:
                    long thousands = (long)Math.Round(resourcesStillNeeded / 1000.0, MidpointRounding.AwayFromZero);
                    return thousands.ToString(CultureInfo.InvariantCulture) + "k resources needed";
            }
        }
    }
}
