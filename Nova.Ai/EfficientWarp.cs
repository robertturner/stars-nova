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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The efficient warp `FUN_1050_69c2` (behavior-specs-10/ai-opponent-behavior.md §12,
    /// "Speeds are set at the end of the pass", step 1): the highest warp, at most 10, at which
    /// every engine in the fleet uses no more than 120% fuel.
    /// </summary>
    public static class EfficientWarp
    {
        /// <summary>The fuel-table figure a warp may not exceed.</summary>
        public const int FuelLimit = 120;

        /// <summary>Engines exempt from the warp-10 cap (indexes 7, 8, 9, 14, 15: Interspace-10,
        /// Enigma Pulsar, Trans-Star 10, Trans-Galactic Mizer Scoop, Galaxy Scoop).</summary>
        private static readonly HashSet<string> WarpTenEngines = new HashSet<string>
        {
            "Interspace-10", "Enigma Pulsar", "Trans-Star 10", "Trans-Galactic Mizer Scoop", "Galaxy Scoop",
        };

        /// <summary>Engines skipped by the free-speed preference (indexes 14 and 15).</summary>
        private static readonly HashSet<string> FreeSpeedExempt = new HashSet<string>
        {
            "Trans-Galactic Mizer Scoop", "Galaxy Scoop",
        };

        /// <summary>
        /// The efficient warp over a fleet's occupied design stacks, in order. Each stack is its
        /// design's engine fuel table (index w - 1 holds warp w, as in <see cref="Engine"/>) and
        /// engine name; a null table (a design with no engine) makes the result 0. The candidate
        /// starts at 10 and only ever steps down: for each stack it drops until that engine's
        /// figure is 120 or less; then, with <paramref name="preferFreeSpeed"/> and an engine
        /// other than the two scoops, a warp that costs fuel drops by 1 if w ≥ 5 and w - 1 is
        /// free, else by 2 if w ≥ 6 and w - 2 is free, else by 3 if w ≥ 7 and w - 3 is free;
        /// finally exactly 10 becomes 9 unless the engine is one of the five warp-10 engines.
        /// The AI's colonizer and hunter orders skip the free-speed step (§12).
        /// </summary>
        public static int Compute(IEnumerable<(int[] FuelTable, string EngineName)> stacks, bool preferFreeSpeed)
        {
            int candidate = 10;
            bool any = false;

            foreach ((int[] table, string name) in stacks)
            {
                any = true;
                if (table == null)
                {
                    return 0;
                }

                while (candidate > 0 && Fuel(table, candidate) > FuelLimit)
                {
                    candidate--;
                }

                if (preferFreeSpeed && !FreeSpeedExempt.Contains(name ?? string.Empty) && Fuel(table, candidate) > 0)
                {
                    if (candidate >= 5 && Fuel(table, candidate - 1) == 0)
                    {
                        candidate -= 1;
                    }
                    else if (candidate >= 6 && Fuel(table, candidate - 2) == 0)
                    {
                        candidate -= 2;
                    }
                    else if (candidate >= 7 && Fuel(table, candidate - 3) == 0)
                    {
                        candidate -= 3;
                    }
                }

                if (candidate == 10 && !WarpTenEngines.Contains(name ?? string.Empty))
                {
                    candidate = 9;
                }
            }

            return any ? candidate : 0;
        }

        /// <summary>The efficient warp of a real fleet (see <see cref="Compute"/>).</summary>
        public static int ForFleet(Fleet fleet, bool preferFreeSpeed)
        {
            List<(int[], string)> stacks = fleet.Composition.Values
                .Where(token => token.Quantity > 0 && token.Design != null)
                .Select(token => (EngineTable(token.Design), EngineName(token.Design)))
                .ToList();

            return Compute(stacks, preferFreeSpeed);
        }

        private static int Fuel(int[] table, int warp)
        {
            if (warp <= 0)
            {
                return 0;
            }

            return warp - 1 < table.Length ? table[warp - 1] : int.MaxValue;
        }

        private static int[] EngineTable(ShipDesign design)
        {
            try
            {
                return design.Engine?.FuelConsumption;
            }
            catch (System.Exception)
            {
                // A design whose summary cannot be built (no blueprint) has no usable engine.
                return null;
            }
        }

        private static string EngineName(ShipDesign design)
        {
            try
            {
                return design.Hull?.Modules?
                    .Select(module => module.AllocatedComponent)
                    .FirstOrDefault(component => component != null && component.Properties != null && component.Properties.ContainsKey("Engine"))?
                    .Name;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
