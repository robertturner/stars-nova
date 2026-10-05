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

    using Nova.Common;

    /// <summary>The bullseye's matched dark-fill / bright-outline colour pair.</summary>
    public enum HabitabilityRingColour
    {
        /// <summary>Dark red / bright red: outside tolerance (a hostile value).</summary>
        Red,

        /// <summary>Dark green / bright green: habitable (the value used is non-negative).</summary>
        Green,

        /// <summary>Dark yellow (olive) / bright yellow: habitable after terraforming, but not
        /// now.</summary>
        Yellow,
    }

    /// <summary>The population ring's solid colour.</summary>
    public enum PopulationRingColour
    {
        /// <summary>The viewer's own planet.</summary>
        Green,

        /// <summary>A planet of a race the viewer rates Friend.</summary>
        Yellow,

        /// <summary>A neutral or enemy owner.</summary>
        Red,
    }

    /// <summary>
    /// The "Planets:" view-mode overlays and their exact arithmetic
    /// (behavior-specs-11/client-interface.md, "Planet views"). All radii are in screen pixels;
    /// callers divide by the zoom scale factor to draw them.
    /// </summary>
    public static class PlanetOverlayRules
    {
        /// <summary>The bullseye outer radius cap: 2 to 10 pixels.</summary>
        public const int MaxHabitabilityOuterRadius = 10;

        /// <summary>The bars' 0-20 step scale.</summary>
        public const int MaxBarSteps = 20;

        /// <summary>
        /// The mineral-chart scale M default for mode 1: 5,000 kT (client-interface.md, mode 1).
        /// The original also offers 100, 500, 1,000, 2,500, 5,000, 7,500, 10,000, 20,000 and 30,000
        /// from the planet summary's scale popup, saved in the .ini; the port has no such control
        /// yet, so M is fixed at the default.
        /// </summary>
        public const int DefaultMineralScale = 5000;

        /// <summary>
        /// The 19 ascending population thresholds, in units of 100 colonists (client-interface.md,
        /// mode 4): 2,500 to 2,500,000 colonists. There is no zero threshold, so a population under
        /// 2,500 gives a step count of 0.
        /// </summary>
        public static readonly int[] PopulationRingThresholds =
        {
            25, 50, 100, 200, 400, 800, 1000, 1500, 2250, 3000,
            4000, 5000, 6000, 7500, 9000, 11000, 14000, 18000, 25000,
        };

        /// <summary>
        /// The bullseye's reading (client-interface.md, mode 3): the colour and the value whose
        /// magnitude sizes the disc. <paramref name="current"/> is the viewer's habitability for
        /// the planet's stored environment (-45..100) and <paramref name="terraformed"/> the same
        /// evaluation after moving each axis toward the viewer's ideal as far as the viewer's
        /// terraforming reaches. A non-Claim-Adjuster viewer uses the current value when it is
        /// non-negative (green), otherwise the terraformed value (yellow when non-negative -
        /// "habitable after terraforming" - and red otherwise). A Claim Adjuster viewer always
        /// uses the terraformed value and sees only green or red.
        /// </summary>
        public static (HabitabilityRingColour Colour, int Value) HabitabilityReading(int current, int terraformed, bool isClaimAdjuster)
        {
            if (!isClaimAdjuster && current >= 0)
            {
                return (HabitabilityRingColour.Green, current);
            }

            if (terraformed >= 0)
            {
                return (isClaimAdjuster ? HabitabilityRingColour.Green : HabitabilityRingColour.Yellow, terraformed);
            }

            return (HabitabilityRingColour.Red, terraformed);
        }

        /// <summary>
        /// The bullseye's outer radius: value / 11 + 2 for a non-negative value, |value| / 5 + 2 for
        /// a negative one, both integer division, capped at 10 (so 2 to 10 pixels).
        /// </summary>
        public static int HabitabilityRingRadius(int value)
        {
            int radius = value >= 0 ? (value / 11) + 2 : (Math.Abs(value) / 5) + 2;
            return Math.Min(radius, MaxHabitabilityOuterRadius);
        }

        /// <summary>
        /// The inner disc radius: outer - 2, or outer - 1 when outer - 2 would be below 3, and never
        /// below 1 (client-interface.md, mode 3).
        /// </summary>
        public static int HabitabilityInnerRadius(int outerRadius)
        {
            int radius = outerRadius - 2;
            if (radius < 3)
            {
                radius = outerRadius - 1;
            }

            return Math.Max(1, radius);
        }

        /// <summary>
        /// The population step count n: how many of the 19 thresholds are at most
        /// <paramref name="population"/> (which is in units of 100 colonists). 0 to 19.
        /// </summary>
        public static int PopulationSteps(int population)
        {
            int steps = 0;
            foreach (int threshold in PopulationRingThresholds)
            {
                if (population < threshold)
                {
                    break;
                }

                steps++;
            }

            return steps;
        }

        /// <summary>
        /// The population ring radius in screen pixels (client-interface.md, mode 4): n + 2 at 200%
        /// and 400% zoom (levels +3 and +4), otherwise (n + 3) / 2 with integer division.
        /// </summary>
        public static int PopulationRingScreenRadius(int steps, int zoomLevel)
        {
            return zoomLevel >= MapZoom.DefaultLevel + 3 ? steps + 2 : (steps + 3) / 2;
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
        /// The amount-mode bar (client-interface.md, mode 1): (amount + M/40) / (M/20) steps, all
        /// divisions truncating, capped at 20. M is the planet summary's mineral-chart scale
        /// (5,000 kT by default), supplied by the caller.
        /// </summary>
        public static int AmountBarSteps(int amount, int referenceMaximum)
        {
            if (referenceMaximum <= 0 || amount <= 0)
            {
                return 0;
            }

            int unit = referenceMaximum / 20;
            if (unit <= 0)
            {
                return MaxBarSteps;
            }

            int steps = (amount + (referenceMaximum / 40)) / unit;
            return Math.Min(steps, MaxBarSteps);
        }

        /// <summary>The concentration-mode bar (client-interface.md, mode 2): concentration / 5,
        /// capped at 20.</summary>
        public static int ConcentrationBarSteps(int percent)
        {
            return Math.Max(0, Math.Min(100, percent)) / 5;
        }

        /// <summary>Mode 5 ("no player information") skips the second pass and draws no fleets
        /// (client-interface.md, mode 5).</summary>
        public static bool ModeShowsFleets(int mode)
        {
            return mode != 5;
        }

        /// <summary>The fleet-in-orbit ring is drawn only in planet views 0-2
        /// (client-interface.md, "Fleet-in-orbit ring").</summary>
        public static bool ModeShowsOrbitRing(int mode)
        {
            return mode >= 0 && mode <= 2;
        }

        /// <summary>
        /// The minimum viewer report level a mode needs before its second-pass figure is drawn
        /// (client-interface.md, modes 1-4): mode 1 (surface minerals) level 4, modes 2-4
        /// (concentrations / value / population) level 3, all others none.
        /// </summary>
        public static int ModeMinReportLevel(int mode)
        {
            switch (mode)
            {
                case 1: return 4;
                case 2:
                case 3:
                case 4: return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// The terraformed habitability t (client-interface.md, mode 3): move each axis from the
        /// planet's ORIGINAL value toward the race's ideal, but no further than that axis's
        /// terraforming reach (the target rule of production-queue.md 10a), then evaluate.
        /// </summary>
        public static int TerraformedHabitability(Race race, TerraformReach reach, int gravity, int temperature, int radiation,
            int originalGravity, int originalTemperature, int originalRadiation)
        {
            if (race == null)
            {
                return 0;
            }

            var projected = new Star
            {
                Gravity = TerraformProductionUnit.AxisTarget(race, TerraformReach.GravityAxis, originalGravity, reach?.Gravity ?? 0),
                Temperature = TerraformProductionUnit.AxisTarget(race, TerraformReach.TemperatureAxis, originalTemperature, reach?.Temperature ?? 0),
                Radiation = TerraformProductionUnit.AxisTarget(race, TerraformReach.RadiationAxis, originalRadiation, reach?.Radiation ?? 0),
            };

            // AxisTarget returns -1 when the axis has no target (immune, or no reach): keep the
            // planet's original value there.
            if (projected.Gravity < 0) projected.Gravity = originalGravity;
            if (projected.Temperature < 0) projected.Temperature = originalTemperature;
            if (projected.Radiation < 0) projected.Radiation = originalRadiation;

            return race.HabPercent(projected);
        }

        /// <summary>The terraformed habitability of a live star this race owns.</summary>
        public static int TerraformedHabitability(Race race, TerraformReach reach, Star star)
        {
            if (race == null || star == null)
            {
                return 0;
            }

            return TerraformedHabitability(race, reach, star.Gravity, star.Temperature, star.Radiation,
                star.OriginalGravity, star.OriginalTemperature, star.OriginalRadiation);
        }
    }
}
