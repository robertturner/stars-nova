#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
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

namespace Nova.Common.Components
{
    using System;

    /// <summary>
    /// Converts a design or fleet's aggregate raw cloak-rating units into an effective cloak
    /// percentage, and the observing side's Tachyon Detector count into a counter-cloak
    /// multiplier - both confirmed by decompile in behavior-specs-7/combat-resolution.md §11.
    ///
    /// Raw cloak-rating units (a design's summed installed-component stats, plus a flat +300
    /// baseline for Super Stealth and this codebase's own back-solved +40 for Improved
    /// Starbases - see ShipDesign.Update) are NOT directly a percentage: they must be run through
    /// the single piecewise curve below exactly once, after combining every contributing design
    /// in a fleet (see Fleet.RecalculateCloak's mass-weighted average). Pre-curved percentages
    /// (like the values this codebase's components.xml stored before this fix) cannot be summed
    /// or averaged directly - floor() loses information, so there's no way back from a curved
    /// percentage to the raw units it came from.
    /// </summary>
    public static class CloakCalculator
    {
        /// <summary>Raw totals above this count as no cloak at all (see PercentFromRawUnits).</summary>
        public const double MaximumRawUnits = 25000;

        /// <summary>
        /// The piecewise raw-units-to-percent curve, hard capped at 98% (never 100%).
        /// </summary>
        public static int PercentFromRawUnits(double rawUnits)
        {
            // behavior-specs-8/combat-resolution.md section 11 addendum: the single-design version of
            // this calculation (`FUN_1048_57b6`) returns 0 when the raw total is zero, negative or
            // above 25,000 - an absurdly overloaded total counts as "no cloak", not the 98% ceiling.
            if (rawUnits <= 0 || rawUnits > MaximumRawUnits)
            {
                return 0;
            }

            if (rawUnits < 100)
            {
                return (int)Math.Floor(rawUnits / 2.0);
            }

            if (rawUnits < 300)
            {
                return 50 + (int)Math.Floor((rawUnits - 100) / 8.0);
            }

            if (rawUnits < 612)
            {
                return 75 + (int)Math.Floor((rawUnits - 300) / 24.0);
            }

            if (rawUnits < 1125)
            {
                return 88 + (int)Math.Floor((rawUnits - 612) / 64.0);
            }

            if (rawUnits < 1612)
            {
                return (rawUnits - 612) > 767 ? 97 : 96;
            }

            return 98;
        }

        /// <summary>
        /// The Tachyon Detector counter-cloak table: index 0-17 by the OBSERVING side's installed
        /// detector count (clamped to the last entry beyond 17), giving a multiplier applied to
        /// the TARGET's effective cloak percentage - not the detector-carrying ship's own. 0
        /// detectors is a neutral 100%; more detectors give diminishing returns down to 81%.
        /// </summary>
        private static readonly int[] TachyonDetectorMultiplierPercent =
        {
            100, 95, 93, 91, 90, 89, 88, 87, 86, 86, 85, 84, 84, 83, 83, 82, 82, 81,
        };

        /// <summary>
        /// The effective cloak percentage a target presents to an observer with the given
        /// installed Tachyon Detector count, after applying the counter-cloak multiplier.
        /// </summary>
        public static double ApplyTachyonDetectors(double targetCloakPercent, int observerDetectorCount)
        {
            int clampedCount = Math.Max(0, Math.Min(observerDetectorCount, TachyonDetectorMultiplierPercent.Length - 1));
            return targetCloakPercent * TachyonDetectorMultiplierPercent[clampedCount] / 100.0;
        }
    }
}
