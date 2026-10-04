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

    /// <summary>One slot of a hull in the original's slot order: the categories it accepts and
    /// its capacity.</summary>
    public struct HullSlot
    {
        public SlotMask Accepts;
        public int Capacity;

        public HullSlot(SlotMask accepts, int capacity)
        {
            Accepts = accepts;
            Capacity = capacity;
        }

        public override string ToString()
        {
            return Accepts + " x" + Capacity;
        }
    }

    /// <summary>
    /// The ship-hull slot lists of behavior-specs-10/component-stats.tsv (category 0x4000, the
    /// slot array at hull record +58), which fix the order an AI design template's bytes are read
    /// in (ai-opponent-behavior.md §15: "one byte per hull slot, in the hull's slot order"). The
    /// slot order of this repository's components.xml modules differs, so
    /// <see cref="DesignBuilder"/> maps each spec slot onto a module by what it accepts.
    /// </summary>
    public static class DesignBuilderHulls
    {
        private static readonly Dictionary<string, string> Layouts = new Dictionary<string, string>
        {
            { "Small Freighter", "Engx1 Scan+Elec+Mechx1 Shld+Armx1" },
            { "Medium Freighter", "Engx1 Scan+Elec+Mechx1 Shld+Armx1" },
            { "Large Freighter", "Engx2 Scan+Elec+Mechx2 Shld+Armx2" },
            { "Super Freighter", "Engx3 Scan+Elec+Mechx3 Shld+Armx5 Elecx2" },
            { "Scout", "Engx1 Scanx1 GPx1" },
            { "Frigate", "Engx1 Scanx2 GPx3 Shld+Armx2" },
            { "Destroyer", "Engx1 Beam+Torpx1 Beam+Torpx1 GPx1 Armx2 Mechx1 Elecx1" },
            { "Cruiser", "Engx2 Shld+Elec+Mechx1 Shld+Elec+Mechx1 Beam+Torpx2 Beam+Torpx2 GPx2 Shld+Armx2" },
            { "Battle Cruiser", "Engx2 Shld+Elec+Mechx2 Shld+Elec+Mechx2 Beam+Torpx3 Beam+Torpx3 GPx3 Shld+Armx4" },
            { "Battleship", "Engx4 Scan+Elec+Mechx1 Shldx8 Beam+Torpx6 Beam+Torpx6 Beam+Torpx2 Beam+Torpx2 Beam+Torpx4 Armx6 Elecx3 Elecx3" },
            { "Dreadnought", "Engx5 Shld+Armx4 Shld+Armx4 Beam+Torpx6 Beam+Torpx6 Elecx4 Elecx4 Beam+Torpx8 Beam+Torpx8 Armx8 Shld+Beam+Torpx5 Shld+Beam+Torpx5 GPx2" },
            { "Privateer", "Engx1 Shld+Armx2 Scan+Elec+Mechx1 GPx1 GPx1" },
            { "Rogue", "Engx2 Shld+Armx3 MLay+Elec+Mechx2 Scanx1 GPx2 GPx2 MLay+Elec+Mechx2 Elecx1 Elecx1" },
            { "Galleon", "Engx4 Shld+Armx2 Shld+Armx2 GPx3 GPx3 MLay+Elec+Mechx2 Elec+Mechx2 Scanx2" },
            { "Mini-Colony Ship", "Engx1 Mechx1" },
            { "Colony Ship", "Engx1 Mechx1" },
            { "Mini Bomber", "Engx1 Bombx2" },
            { "B-17 Bomber", "Engx2 Bombx4 Bombx4 Scan+Elec+Mechx1" },
            { "Stealth Bomber", "Engx2 Bombx4 Bombx4 Scan+Elec+Mechx1 Elecx3" },
            { "B-52 Bomber", "Engx3 Bombx4 Bombx4 Bombx4 Bombx4 Scan+Elec+Mechx2 Shldx2" },
            { "Midget Miner", "Engx1 Minex2" },
            { "Mini-Miner", "Engx1 Scan+Elec+Mechx1 Minex1 Minex1" },
            { "Miner", "Engx2 Scan+Arm+Elec+Mechx2 Minex2 Minex1 Minex2 Minex1" },
            { "Maxi-Miner", "Engx3 Scan+Arm+Elec+Mechx2 Minex4 Minex1 Minex4 Minex1" },
            { "Ultra-Miner", "Engx2 Scan+Arm+Elec+Mechx3 Minex4 Minex2 Minex4 Minex2" },
            { "Fuel Transport", "Engx1 Shldx1" },
            { "Super-Fuel Xport", "Engx2 Shldx2 Scanx1" },
            { "Mini Mine Layer", "Engx1 MLayx2 MLayx2 Scan+Elec+Mechx1" },
            { "Super Mine Layer", "Engx3 MLayx8 MLayx8 Shld+Armx3 Scan+Elec+Mechx3 MLay+Elec+Mechx3" },
            { "Nubian", "Engx3 GPx3 GPx3 GPx3 GPx3 GPx3 GPx3 GPx3 GPx3 GPx3 GPx3 GPx3 GPx3" },
            { "Mini Morph", "Engx2 GPx3 GPx1 GPx1 GPx1 GPx2 GPx2" },
            { "Meta Morph", "Engx3 GPx8 GPx2 GPx2 GPx1 GPx2 GPx2" },
        };

        private static readonly Dictionary<string, SlotMask> Tokens = new Dictionary<string, SlotMask>
        {
            { "Eng", SlotMask.Engine },
            { "Scan", SlotMask.Scanner },
            { "Shld", SlotMask.Shield },
            { "Arm", SlotMask.Armor },
            { "Beam", SlotMask.Beam },
            { "Torp", SlotMask.Torpedo },
            { "Bomb", SlotMask.Bomb },
            { "Mine", SlotMask.MiningRobot },
            { "MLay", SlotMask.MineLayer },
            { "Orb", SlotMask.Orbital },
            { "Elec", SlotMask.Electrical },
            { "Mech", SlotMask.Mechanical },
            { "GP", SlotMask.GeneralPurpose },
        };

        /// <summary>The hull names (spec spelling) with a known slot list.</summary>
        public static IEnumerable<string> HullNames
        {
            get { return Layouts.Keys; }
        }

        public static bool IsKnown(string hullName)
        {
            return Layouts.ContainsKey(hullName);
        }

        /// <summary>The spec slot list of a hull, in the original's slot order.</summary>
        public static IReadOnlyList<HullSlot> Slots(string hullName)
        {
            if (!Layouts.TryGetValue(hullName, out string layout))
            {
                throw new ArgumentException("No slot list for hull " + hullName, nameof(hullName));
            }

            List<HullSlot> slots = new List<HullSlot>();
            foreach (string token in layout.Split(' '))
            {
                int x = token.LastIndexOf('x');
                SlotMask mask = SlotMask.None;
                foreach (string part in token.Substring(0, x).Split('+'))
                {
                    mask |= Tokens[part];
                }

                slots.Add(new HullSlot(mask, int.Parse(token.Substring(x + 1), System.Globalization.CultureInfo.InvariantCulture)));
            }

            return slots;
        }

        /// <summary>
        /// What a components.xml module type string accepts, on the spec's category scale. "Base
        /// Cargo" and "Space Dock" are pseudo-modules that take no part.
        /// </summary>
        public static SlotMask NovaModuleAccepts(string componentType)
        {
            switch (componentType)
            {
                case "Engine": return SlotMask.Engine;
                case "Scanner": return SlotMask.Scanner;
                case "Shield": return SlotMask.Shield;
                case "Armor": return SlotMask.Armor;
                case "Weapon": return SlotMask.Beam | SlotMask.Torpedo;
                case "Bomb": return SlotMask.Bomb;
                case "Mining Robot": return SlotMask.MiningRobot;
                case "Mine Layer": return SlotMask.MineLayer;
                case "Electrical": return SlotMask.Electrical;
                case "Mechanical": return SlotMask.Mechanical;
                case "Shield or Armor": return SlotMask.Shield | SlotMask.Armor;
                case "General Purpose": return SlotMask.GeneralPurpose;
                case "Scanner Electrical Mechanical": return SlotMask.Scanner | SlotMask.Electrical | SlotMask.Mechanical;
                case "Shield Electrical Mechanical": return SlotMask.Shield | SlotMask.Electrical | SlotMask.Mechanical;
                case "Mine Layer Electrical Mechanical": return SlotMask.MineLayer | SlotMask.Electrical | SlotMask.Mechanical;
                case "Armor Scanner Elect Mech": return SlotMask.Armor | SlotMask.Scanner | SlotMask.Electrical | SlotMask.Mechanical;
                case "Electrical or Mechanical": return SlotMask.Electrical | SlotMask.Mechanical;
                case "Weapon or Shield": return SlotMask.Shield | SlotMask.Beam | SlotMask.Torpedo;
                case "Orbital or Electrical": return SlotMask.Orbital | SlotMask.Electrical;
                default: return SlotMask.None;
            }
        }
    }
}
