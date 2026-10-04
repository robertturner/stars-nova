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

namespace Nova.Client.Map
{
    using System;

    /// <summary>The bullseye's matched dark-fill / bright-outline colour pair.</summary>
    public enum HabitabilityRingColour
    {
        /// <summary>Dark red / bright red: outside tolerance (negative value).</summary>
        Red,

        /// <summary>Dark green / bright green: non-negative value.</summary>
        Green,

        /// <summary>Dark yellow (olive) / bright yellow: the exact and estimated readings disagree
        /// in sign, in the Claim-Adjuster-gated cases.</summary>
        Olive,
    }

    /// <summary>The population ring's solid colour.</summary>
    public enum PopulationRingColour
    {
        /// <summary>The viewer's own planet.</summary>
        Green,

        /// <summary>A planet of a race the relationship table marks as seen/friendly.</summary>
        Yellow,

        /// <summary>Unknown or hostile owner.</summary>
        Red,
    }

    /// <summary>
    /// The "Planets:" view-mode overlays (behavior-specs-10/client-interface.md, "Planet
    /// display-mode ring/bar overlay"). Sizes are in zoom-scaled map pixels, i.e. world units in
    /// this client, unless stated otherwise.
    /// </summary>
    public static class PlanetOverlayRules
    {
        /// <summary>The bullseye's radius cap: "clamped to at most 10 zoom-scaled steps".</summary>
        public const double MaxHabitabilityRingRadius = 10;

        /// <summary>SPEC GAP seam: the inner ring's size relative to the outer one ("an outer,
        /// larger circle and an inner, smaller one"). Neutral default: half.</summary>
        public const double InnerRingFraction = 0.5;

        /// <summary>SPEC GAP seam: the side of the small square drawn for an unowned planet in the
        /// habitability mode (size not given).</summary>
        public const double UnownedMarkerSize = 3;

        /// <summary>The bars' 0-20 step scale.</summary>
        public const int MaxBarSteps = 20;

        /// <summary>
        /// SPEC GAP seam: the 19 ascending population thresholds the population ring brackets
        /// against (the spec gives the count, not the values). Neutral placeholder: 0, then doubling
        /// from 1,000 colonists.
        /// </summary>
        public static readonly int[] PopulationRingThresholds = BuildPlaceholderThresholds();

        /// <summary>
        /// SPEC GAP seam: the population ring is "halved at the lowest zoom levels", which levels is
        /// not stated. Neutral reading: every level below 50% (25% and 37.5%).
        /// </summary>
        public const int PopulationRingHalvedAtOrBelowLevel = -3;

        /// <summary>
        /// Which colour pair the bullseye uses. <paramref name="exactValue"/> is the stored-environment
        /// habitability (-45..100), <paramref name="estimatedValue"/> the value re-derived from the
        /// visible/estimated environment readings. Olive is reached only when the two disagree in
        /// sign, and the recheck runs for a negative exact value only when the race is NOT Claim
        /// Adjuster, and for a non-negative exact value only when it IS.
        /// </summary>
        public static HabitabilityRingColour HabitabilityColour(int exactValue, int estimatedValue, bool isClaimAdjuster)
        {
            if (exactValue < 0)
            {
                return !isClaimAdjuster && estimatedValue >= 0 ? HabitabilityRingColour.Olive : HabitabilityRingColour.Red;
            }

            return isClaimAdjuster && estimatedValue < 0 ? HabitabilityRingColour.Olive : HabitabilityRingColour.Green;
        }

        /// <summary>
        /// The bullseye's outer radius: proportional to the value, clamped to 10 steps.
        /// Ambiguity (documented): "proportional" is read as |value| / 10, so 100% reaches the cap
        /// exactly; a negative (hostility) value uses its magnitude the same way.
        /// </summary>
        public static double HabitabilityRingRadius(int value)
        {
            return Math.Min(Math.Abs(value) / 10.0, MaxHabitabilityRingRadius);
        }

        /// <summary>The population ring's 0-18 step count: the index of the highest threshold the
        /// population reaches (0 below the second threshold).</summary>
        public static int PopulationSteps(int population)
        {
            int steps = 0;
            for (int i = 1; i < PopulationRingThresholds.Length; i++)
            {
                if (population >= PopulationRingThresholds[i])
                {
                    steps = i;
                }
            }

            return steps;
        }

        /// <summary>
        /// The population ring's radius: step count plus 2, halved at the lowest zoom levels.
        /// Ambiguity (documented): because the spec halves it at low zoom, the radius is read as
        /// SCREEN pixels (not zoom-scaled); the caller divides by the zoom factor to draw it.
        /// </summary>
        public static double PopulationRingScreenRadius(int steps, int zoomLevel)
        {
            double radius = steps + 2;
            return zoomLevel <= PopulationRingHalvedAtOrBelowLevel ? radius / 2 : radius;
        }

        public static PopulationRingColour PopulationColour(MapOwnership ownership, bool ownerIsSeenFriendly)
        {
            if (ownership == MapOwnership.Own)
            {
                return PopulationRingColour.Green;
            }

            return ownership == MapOwnership.Other && ownerIsSeenFriendly ? PopulationRingColour.Yellow : PopulationRingColour.Red;
        }

        /// <summary>
        /// The amount-mode bar: the value normalised against the shared reference maximum, on the
        /// 0-20 step scale. SPEC GAP: the reference maximum itself is not given; the caller supplies
        /// it (see the map view model's comment for the neutral choice).
        /// </summary>
        public static int AmountBarSteps(int amount, int referenceMaximum)
        {
            if (referenceMaximum <= 0 || amount <= 0)
            {
                return 0;
            }

            long steps = (long)amount * MaxBarSteps / referenceMaximum;
            return (int)Math.Min(steps, MaxBarSteps);
        }

        /// <summary>The concentration-mode bar: a 0-100 percentage divided by 5.</summary>
        public static int ConcentrationBarSteps(int percent)
        {
            return Math.Max(0, Math.Min(100, percent)) / 5;
        }

        private static int[] BuildPlaceholderThresholds()
        {
            var thresholds = new int[19];
            thresholds[0] = 0;
            for (int i = 1; i < thresholds.Length; i++)
            {
                thresholds[i] = 1000 << (i - 1);
            }

            return thresholds;
        }
    }
}
