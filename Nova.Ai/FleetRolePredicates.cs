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

    /// <summary>
    /// The three fleet-role classifiers of behavior-specs-10/ai-opponent-behavior.md §11
    /// (`FUN_1090_2db6`, `FUN_1090_2e52`, `FUN_1090_2e94`), plus the hull and engine index
    /// tables they and the drivers read. A stack "counts" when its ship count is above zero.
    /// </summary>
    /// <remarks>
    /// §11/§60 defect: the original applies the combat-fleet test to a foreign fleet through the
    /// AI's own design table, so the verdict says nothing about the foreign ships. The spec
    /// recommends testing the foreign fleet's real designs; these predicates take the
    /// composition's own designs, so they do exactly that for a foreign FleetIntel too.
    /// </remarks>
    public static class FleetRolePredicates
    {
        /// <summary>A hull id that matches no ship hull (starbase chassis, unknown names).</summary>
        public const int NoHull = -1;

        public const int Frigate = 5;
        public const int Nubian = 29;
        public const int MetaMorph = 31;

        /// <summary>The cargo capacity at which an armed Meta Morph stops being a combat fleet
        /// and becomes a hauler (§11: "under 500 kT" / "500 kT or more").</summary>
        public const int HaulerCargoThreshold = 500;

        /// <summary>The 32 ship hulls in the original's index order (component-stats.tsv,
        /// category 0x4000).</summary>
        private static readonly string[] HullNames =
        {
            "Small Freighter", "Medium Freighter", "Large Freighter", "Super Freighter",
            "Scout", "Frigate", "Destroyer", "Cruiser", "Battle Cruiser", "Battleship", "Dreadnought",
            "Privateer", "Rogue", "Galleon", "Mini-Colony Ship", "Colony Ship",
            "Mini Bomber", "B-17 Bomber", "Stealth Bomber", "B-52 Bomber",
            "Midget Miner", "Mini-Miner", "Miner", "Maxi-Miner", "Ultra-Miner",
            "Fuel Transport", "Super-Fuel Xport", "Mini Mine Layer", "Super Mine Layer",
            "Nubian", "Mini Morph", "Meta Morph",
        };

        /// <summary>The 16 engines in the original's component index order (component-stats.tsv,
        /// category 0x0001).</summary>
        private static readonly string[] EngineNames =
        {
            "Settler's Delight", "Quick Jump 5", "Fuel Mizer", "Long Hump 6", "Daddy Long Legs 7",
            "Alpha Drive 8", "Trans-Galactic Drive", "Interspace-10", "Enigma Pulsar", "Trans-Star 10",
            "Radiating Hydro-Ram Scoop", "Sub-Galactic Fuel Scoop", "Trans-Galactic Fuel Scoop",
            "Trans-Galactic Super Scoop", "Trans-Galactic Mizer Scoop", "Galaxy Scoop",
        };

        private static readonly Dictionary<string, int> HullIndex = BuildIndex(HullNames, new Dictionary<string, string>
        {
            // Nova spells a few hulls differently from the original's table.
            { "Super-Fuel Transport", "Super-Fuel Xport" },
        });

        private static readonly Dictionary<string, int> EngineIndex = BuildIndex(EngineNames, null);

        /// <summary>The original hull id of a design (design offset +0), or <see cref="NoHull"/>.</summary>
        public static int HullId(ShipDesign design)
        {
            string name = design?.Blueprint?.Name;
            return name != null && HullIndex.TryGetValue(Normalise(name), out int id) ? id : NoHull;
        }

        /// <summary>
        /// The component index of a design's first engine (the drivers' "engine index" tests,
        /// e.g. §12 personality 1: "component index 3 or higher (Long Hump 6 or better)"), or -1
        /// when the design has no engine Nova can name.
        /// </summary>
        public static int EngineIndexOf(ShipDesign design)
        {
            try
            {
                Component engine = design?.Hull?.Modules?
                    .Select(module => module.AllocatedComponent)
                    .FirstOrDefault(component => component != null && component.Properties != null && component.Properties.ContainsKey("Engine"));
                return engine != null && EngineIndex.TryGetValue(Normalise(engine.Name), out int index) ? index : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// The design's weapon value (design offset +0x87, §11) is positive exactly when it
        /// mounts a beam, torpedo or bomb (ship-design-and-components.md §9); this returns that
        /// test.
        /// </summary>
        public static bool HasWeaponValue(ShipDesign design)
        {
            return HasBeamOrTorpedo(design) || IsBomberDesign(design);
        }

        /// <summary>`FUN_1090_3c1e`: the design mounts a beam or torpedo weapon (the hunter
        /// variant's "explores instead" test).</summary>
        public static bool HasBeamOrTorpedo(ShipDesign design)
        {
            try
            {
                design.Update();
                return design.Weapons != null && design.Weapons.Count > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsBomberDesign(ShipDesign design)
        {
            try
            {
                return design.IsBomber;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Cargo capacity, `FUN_1050_2a2e` mode 2: the hull's cargo plus its pods (Nova's
        /// summary "Cargo" figure is exactly that sum).</summary>
        public static int CargoCapacity(ShipDesign design)
        {
            try
            {
                design.Update();
                return design.CargoCapacity;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>`FUN_1090_2db6` for one design: hull 6-10 with no armament test; or an armed
        /// Frigate; or an armed Nubian or Meta Morph with under 500 kT of cargo capacity.</summary>
        public static bool IsCombatDesign(ShipDesign design)
        {
            int hull = HullId(design);
            if (hull >= 6 && hull <= 10)
            {
                return true;
            }

            if (hull == Frigate)
            {
                return HasWeaponValue(design);
            }

            if (hull == Nubian || hull == MetaMorph)
            {
                return HasWeaponValue(design) && CargoCapacity(design) < HaulerCargoThreshold;
            }

            return false;
        }

        /// <summary>`FUN_1090_2e52` for one design: hull 4-10, no armament test.</summary>
        public static bool IsScoutOrWarshipLineDesign(ShipDesign design)
        {
            int hull = HullId(design);
            return hull >= 4 && hull <= 10;
        }

        /// <summary>`FUN_1090_2e94` for one design: hull 0-3 or 11-13, or an armed Meta Morph with
        /// 500 kT or more of cargo capacity.</summary>
        public static bool IsHaulerDesign(ShipDesign design)
        {
            int hull = HullId(design);
            if ((hull >= 0 && hull <= 3) || (hull >= 11 && hull <= 13))
            {
                return true;
            }

            return hull == MetaMorph && HasWeaponValue(design) && CargoCapacity(design) >= HaulerCargoThreshold;
        }

        /// <summary>Combat fleet (§11): some occupied stack passes <see cref="IsCombatDesign"/>.</summary>
        public static bool IsCombatFleet(IEnumerable<ShipToken> composition)
        {
            return Occupied(composition).Any(token => IsCombatDesign(token.Design));
        }

        /// <summary>Scout-or-warship-line fleet (§11).</summary>
        public static bool IsScoutOrWarshipLineFleet(IEnumerable<ShipToken> composition)
        {
            return Occupied(composition).Any(token => IsScoutOrWarshipLineDesign(token.Design));
        }

        /// <summary>Hauler fleet (§11).</summary>
        public static bool IsHaulerFleet(IEnumerable<ShipToken> composition)
        {
            return Occupied(composition).Any(token => IsHaulerDesign(token.Design));
        }

        /// <summary>The fleet's ship count (the 16-word sum at fleet +0x0c).</summary>
        public static int ShipCount(IEnumerable<ShipToken> composition)
        {
            return Occupied(composition).Sum(token => token.Quantity);
        }

        private static IEnumerable<ShipToken> Occupied(IEnumerable<ShipToken> composition)
        {
            return (composition ?? Enumerable.Empty<ShipToken>()).Where(token => token != null && token.Quantity > 0 && token.Design != null);
        }

        private static Dictionary<string, int> BuildIndex(string[] names, Dictionary<string, string> aliases)
        {
            Dictionary<string, int> index = new Dictionary<string, int>();
            for (int i = 0; i < names.Length; i++)
            {
                index[Normalise(names[i])] = i;
            }

            if (aliases != null)
            {
                foreach (KeyValuePair<string, string> alias in aliases)
                {
                    index[Normalise(alias.Key)] = index[Normalise(alias.Value)];
                }
            }

            return index;
        }

        /// <summary>Case, space and hyphen insensitive ("Mini Miner" = "Mini-Miner").</summary>
        private static string Normalise(string name)
        {
            return new string((name ?? string.Empty).Where(c => c != ' ' && c != '-').ToArray()).ToLowerInvariant();
        }
    }
}
