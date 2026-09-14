#region Copyright Notice
// ============================================================================
// Copyright (C) 2009, 2010 stars-nova
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

    /// <summary>
    /// Race Designer's live "Estimated compatible worlds" figure - ports
    /// race-designer-ui-and-availability.md's galaxy-wide availability estimate. This is a
    /// distinct calculation from Race.HabValue's per-planet habitability formula (see that
    /// method's own remarks) - it estimates how COMMON a compatible world is across the galaxy,
    /// not how habitable any one specific planet is.
    ///
    /// Extracted out of the WinForms RaceDesigner (which originally took its own
    /// Nova.ControlLibrary.Range controls directly) so both that dialog and its Avalonia
    /// equivalent (RaceDesignerViewModel) share one implementation instead of duplicating the
    /// formula - this class only ever touches plain ints/bools, no platform-specific type.
    /// </summary>
    public static class WorldAvailabilityEstimator
    {
        /// <summary>
        /// The estimated percentage of worlds in the galaxy compatible with all three
        /// tolerances, given as three (min, max, immune) triples.
        /// </summary>
        public static double EstimateCompatibleWorldsPercent(
            (int min, int max, bool immune) gravity,
            (int min, int max, bool immune) temperature,
            (int min, int max, bool immune) radiation)
        {
            // Each factor is itself already a 0-100 "percentage of this axis" quantity (a full
            // 0-100 span maxes every one of them out at exactly 100). Their raw product is
            // therefore an "unscaled" figure on a 0-100^3 scale, not itself a percentage -
            // dividing by 100^2 rescales the joint result back down to a 0-100 percentage,
            // equivalent to treating each Ci/100 as an independent probability and multiplying
            // the three together.
            double unscaledCoverage = Coverage(gravity, middleWeighted: true)
                                     * Coverage(temperature, middleWeighted: true)
                                     * Coverage(radiation, middleWeighted: false);
            double coverage = unscaledCoverage / (100.0 * 100.0);

            // Clamp a zero or sub-unit result up to one unit before returning, so a deliberately
            // tiny tolerance area never reports an impossible or undefined result.
            return Math.Max(coverage, 1);
        }

        /// <summary>
        /// One axis's contribution to the galaxy-wide availability estimate. An immune axis
        /// always contributes its full 100. Otherwise: the first two axes (Gravity, Temperature)
        /// use a middle-weighted distribution (worlds cluster toward the 10-89 "common" band,
        /// tapering off toward the edges); Radiation uses a uniform distribution (interval width
        /// alone). The ordering is significant - the spec is explicit that a compatible
        /// implementation must not apply one uniform formula to all three axes.
        /// </summary>
        private static double Coverage((int min, int max, bool immune) tolerance, bool middleWeighted)
        {
            if (tolerance.immune)
            {
                return 100.0;
            }

            if (!middleWeighted)
            {
                return tolerance.max - tolerance.min;
            }

            double sum = 0;
            for (int x = tolerance.min; x <= tolerance.max; x++)
            {
                sum += EdgeWeight(x);
            }

            return sum / 9.0;
        }

        /// <summary>
        /// w(x) = x for 0&lt;=x&lt;10; w(x) = 10 for 10&lt;=x&lt;=89; w(x) = 100-x for
        /// 90&lt;=x&lt;=100 - the middle-weighted distribution's per-setting weight.
        /// </summary>
        private static double EdgeWeight(int x)
        {
            if (x < 10)
            {
                return x;
            }

            if (x <= 89)
            {
                return 10;
            }

            return 100 - x;
        }
    }
}
