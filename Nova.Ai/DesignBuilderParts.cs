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

    /// <summary>The component categories the AI design builder's part groups name
    /// (behavior-specs-10/component-stats.tsv category tables).</summary>
    public enum PartCategory
    {
        Engine,
        Scanner,
        Shield,
        Armor,
        Beam,
        Torpedo,
        Bomb,
        MiningRobot,
        MineLayer,
        Orbital,
        Electrical,
        Mechanical
    }

    /// <summary>The categories a hull slot accepts (the slot array's allowed-category bitmask
    /// in component-stats.tsv: Eng, Scan, Shld, Arm, Beam, Torp, Bomb, Mine, MLay, Orb, Elec,
    /// Mech).</summary>
    [Flags]
    public enum SlotMask
    {
        None = 0,
        Engine = 1 << PartCategory.Engine,
        Scanner = 1 << PartCategory.Scanner,
        Shield = 1 << PartCategory.Shield,
        Armor = 1 << PartCategory.Armor,
        Beam = 1 << PartCategory.Beam,
        Torpedo = 1 << PartCategory.Torpedo,
        Bomb = 1 << PartCategory.Bomb,
        MiningRobot = 1 << PartCategory.MiningRobot,
        MineLayer = 1 << PartCategory.MineLayer,
        Orbital = 1 << PartCategory.Orbital,
        Electrical = 1 << PartCategory.Electrical,
        Mechanical = 1 << PartCategory.Mechanical,

        /// <summary>The general-purpose slot: everything but engines, bombs, mining robots and
        /// orbital parts.</summary>
        GeneralPurpose = Scanner | Shield | Armor | Beam | Torpedo | MineLayer | Electrical | Mechanical
    }

    /// <summary>One entry of a part group: a component category, a top subtype (0-based index
    /// into that category's table) and a number of steps tried downward from it.</summary>
    public struct PartGroupEntry
    {
        public PartCategory Category;
        public int Top;
        public int Steps;

        public PartGroupEntry(PartCategory category, int top, int steps)
        {
            Category = category;
            Top = top;
            Steps = steps;
        }
    }

    /// <summary>A part named by its category and its spec name.</summary>
    public struct PartCandidate
    {
        public PartCategory Category;
        public string Name;

        public PartCandidate(PartCategory category, string name)
        {
            Category = category;
            Name = name;
        }
    }

    /// <summary>
    /// The 45 part groups of the AI design builder (behavior-specs-10/ai-opponent-behavior.md
    /// §15, resolver `FUN_1090_036a`; 139 entries at segment 19 offset 0x23e). Each entry names a
    /// category, a top subtype and a number of steps and is tried from the top subtype downward
    /// through that category's table; the category tables (names in table order) are those of
    /// behavior-specs-10/component-stats.tsv. Spec names that this repository's components.xml
    /// spells differently are mapped by <see cref="NovaNames"/>.
    /// </summary>
    public static class DesignPartGroups
    {
        public const int GroupCount = 45;

        /// <summary>The number of entries in the original's table, including group 44's
        /// zero-step Settler's Delight entry.</summary>
        public const int EntryCount = 139;

        private static readonly Dictionary<PartCategory, string[]> Tables = new Dictionary<PartCategory, string[]>
        {
            {
                PartCategory.Engine, new[]
                {
                    "Settler's Delight", "Quick Jump 5", "Fuel Mizer", "Long Hump 6", "Daddy Long Legs 7", "Alpha Drive 8",
                    "Trans-Galactic Drive", "Interspace-10", "Enigma Pulsar", "Trans-Star 10", "Radiating Hydro-Ram Scoop",
                    "Sub-Galactic Fuel Scoop", "Trans-Galactic Fuel Scoop", "Trans-Galactic Super Scoop",
                    "Trans-Galactic Mizer Scoop", "Galaxy Scoop"
                }
            },
            {
                PartCategory.Scanner, new[]
                {
                    "Bat Scanner", "Rhino Scanner", "Mole Scanner", "DNA Scanner", "Possum Scanner", "Pick Pocket Scanner",
                    "Chameleon Scanner", "Ferret Scanner", "Dolphin Scanner", "Gazelle Scanner", "RNA Scanner",
                    "Cheetah Scanner", "Elephant Scanner", "Eagle Eye Scanner", "Robber Baron Scanner", "Peerless Scanner"
                }
            },
            {
                PartCategory.Shield, new[]
                {
                    "Mole-skin Shield", "Cow-hide Shield", "Wolverine Diffuse Shield", "Croby Sharmor", "Shadow Shield",
                    "Bear Neutrino Barrier", "Langston Shell", "Gorilla Delagator", "Elephant Hide Fortress",
                    "Complete Phase Shield"
                }
            },
            {
                PartCategory.Armor, new[]
                {
                    "Tritanium", "Crobmnium", "Carbonic Armor", "Strobnium", "Organic Armor", "Kelarium", "Fielded Kelarium",
                    "Depleted Neutronium", "Neutronium", "Mega Poly Shell", "Valanium", "Superlatanium"
                }
            },
            {
                PartCategory.Beam, new[]
                {
                    "Laser", "X-Ray Laser", "Mini Gun", "Yakimora Light Phaser", "Blackjack", "Phaser Bazooka", "Pulsed Sapper",
                    "Colloidal Phaser", "Gatling Gun", "Mini Blaster", "Bludgeon", "Mark IV Blaster", "Phased Sapper",
                    "Heavy Blaster", "Gatling Neutrino Cannon", "Myopic Disruptor", "Blunderbuss", "Disruptor",
                    "Multi Contained Munition", "Syncro Sapper", "Mega Disruptor", "Big Mutha Cannon", "Streaming Pulverizer",
                    "Anti-Matter Pulverizer"
                }
            },
            {
                PartCategory.Torpedo, new[]
                {
                    "Alpha Torpedo", "Beta Torpedo", "Delta Torpedo", "Epsilon Torpedo", "Rho Torpedo", "Upsilon Torpedo",
                    "Omega Torpedo", "Anti Matter Torpedo", "Jihad Missile", "Juggernaut Missile", "Doomsday Missile",
                    "Armageddon Missile"
                }
            },
            {
                PartCategory.Bomb, new[]
                {
                    "Lady Finger Bomb", "Black Cat Bomb", "M-70 Bomb", "M-80 Bomb", "Cherry Bomb", "LBU-17 Bomb", "LBU-32 Bomb",
                    "LBU-74 Bomb", "Hush-a-Boom", "Retro Bomb", "Smart Bomb", "Neutron Bomb", "Enriched Neutron Bomb",
                    "Peerless Bomb", "Annihilator Bomb"
                }
            },
            {
                PartCategory.MiningRobot, new[]
                {
                    "Robo-Midget Miner", "Robo-Mini-Miner", "Robo-Miner", "Robo-Maxi-Miner", "Robo-Super-Miner",
                    "Robo-Ultra-Miner", "Alien Miner", "Orbital Adjuster"
                }
            },
            {
                PartCategory.MineLayer, new[]
                {
                    "Mine Dispenser 40", "Mine Dispenser 50", "Mine Dispenser 80", "Mine Dispenser 130", "Heavy Dispenser 50",
                    "Heavy Dispenser 110", "Heavy Dispenser 200", "Speed Trap 20", "Speed Trap 30", "Speed Trap 50"
                }
            },
            {
                PartCategory.Orbital, new[]
                {
                    "Stargate 100/250", "Stargate any/300", "Stargate 150/600", "Stargate 300/500", "Stargate 100/any",
                    "Stargate any/800", "Stargate any/any", "Mass Driver 5", "Mass Driver 6", "Mass Driver 7", "Super Driver 8",
                    "Super Driver 9", "Ultra Driver 10", "Ultra Driver 11", "Ultra Driver 12", "Ultra Driver 13"
                }
            },
            {
                PartCategory.Electrical, new[]
                {
                    "Transport Cloaking", "Stealth Cloak", "Super-Stealth Cloak", "Ultra-Stealth Cloak", "Multi Function Pod",
                    "Battle Computer", "Battle Super Computer", "Battle Nexus", "Jammer 10", "Jammer 20", "Jammer 30",
                    "Jammer 50", "Energy Capacitor", "Flux Capacitor", "Energy Dampener", "Tachyon Detector",
                    "Anti-matter Generator"
                }
            },
            {
                PartCategory.Mechanical, new[]
                {
                    "Colonization Module", "Orbital Construction Module", "Cargo Pod", "Super Cargo Pod", "Multi Cargo Pod",
                    "Fuel Tank", "Super Fuel Tank", "Maneuvering Jet", "Overthruster", "Jump Gate", "Beam Deflector"
                }
            },
        };

        /// <summary>Spec names (component-stats.tsv) that components.xml spells differently.</summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>
        {
            { "Interspace-10", "Interspace 10" },
            { "Robber Baron Scanner", "Robber Barron Scanner" },
            { "Gorilla Delagator", "Gorilla Delegator" },
            { "Gatling Neutrino Cannon", "Gatling Neutrino Cannnon" },
            { "Armageddon Missile", "Armegeddon Missile" },
            { "Robo-Mini-Miner", "Robo-Mini Miner" },
            { "Robo-Maxi-Miner", "Robo-Maxi Miner" },
            { "Robo-Super-Miner", "Robo-Super Miner" },
            { "Robo-Ultra-Miner", "Robo-Ultra Miner" },
            { "Mini-Miner", "Mini Miner" },
            { "Maxi-Miner", "Maxi Miner" },
            { "Ultra-Miner", "Ultra Miner" },
            { "Super-Fuel Xport", "Super-Fuel Transport" },
        };

        private static readonly PartGroupEntry[][] Groups = BuildGroups();

        /// <summary>The spec name of subtype <paramref name="index"/> of a category.</summary>
        public static string PartName(PartCategory category, int index)
        {
            return Tables[category][index];
        }

        /// <summary>The number of subtypes in a category's table.</summary>
        public static int TableLength(PartCategory category)
        {
            return Tables[category].Length;
        }

        /// <summary>The names to look a spec part up by in this repository's component data:
        /// the spec name itself, then the components.xml spelling where it differs.</summary>
        public static IEnumerable<string> NovaNames(string specName)
        {
            yield return specName;
            if (Aliases.TryGetValue(specName, out string alias))
            {
                yield return alias;
            }
        }

        public static SlotMask MaskOf(PartCategory category)
        {
            return (SlotMask)(1 << (int)category);
        }

        /// <summary>The entries of one group, in the order the resolver tries them.</summary>
        public static IReadOnlyList<PartGroupEntry> Entries(int group)
        {
            return Groups[group];
        }

        /// <summary>Every part a group tries, best first: each entry from its top subtype down
        /// for its number of steps.</summary>
        public static IEnumerable<PartCandidate> Candidates(int group)
        {
            foreach (PartGroupEntry entry in Groups[group])
            {
                for (int step = 0; step < entry.Steps; step++)
                {
                    yield return new PartCandidate(entry.Category, Tables[entry.Category][entry.Top - step]);
                }
            }
        }

        private static PartGroupEntry One(PartCategory category, string name)
        {
            return Range(category, name, name);
        }

        private static PartGroupEntry Range(PartCategory category, string top, string bottom)
        {
            int topIndex = Array.IndexOf(Tables[category], top);
            int bottomIndex = Array.IndexOf(Tables[category], bottom);
            if (topIndex < 0 || bottomIndex < 0 || bottomIndex > topIndex)
            {
                throw new InvalidOperationException("Bad part-group range " + top + " .. " + bottom);
            }

            return new PartGroupEntry(category, topIndex, topIndex - bottomIndex + 1);
        }

        private static PartGroupEntry[][] BuildGroups()
        {
            const PartCategory Eng = PartCategory.Engine;
            const PartCategory Scan = PartCategory.Scanner;
            const PartCategory Shld = PartCategory.Shield;
            const PartCategory Arm = PartCategory.Armor;
            const PartCategory Beam = PartCategory.Beam;
            const PartCategory Torp = PartCategory.Torpedo;
            const PartCategory Bomb = PartCategory.Bomb;
            const PartCategory Mine = PartCategory.MiningRobot;
            const PartCategory MLay = PartCategory.MineLayer;
            const PartCategory Orb = PartCategory.Orbital;
            const PartCategory Elec = PartCategory.Electrical;
            const PartCategory Mech = PartCategory.Mechanical;

            return new[]
            {
                // 0
                new[] { Range(Torp, "Anti Matter Torpedo", "Alpha Torpedo") },
                // 1
                new[] { Range(Torp, "Armageddon Missile", "Jihad Missile") },
                // 2
                new[] { One(Beam, "Multi Contained Munition"), One(Beam, "Mega Disruptor"), One(Beam, "Heavy Blaster"), One(Beam, "Colloidal Phaser") },
                // 3
                new[] { One(Beam, "Anti-Matter Pulverizer"), One(Beam, "Disruptor"), One(Beam, "Mark IV Blaster"), One(Beam, "Phaser Bazooka") },
                // 4
                new[]
                {
                    One(Beam, "Streaming Pulverizer"), One(Beam, "Myopic Disruptor"), One(Beam, "Mini Blaster"),
                    One(Beam, "Yakimora Light Phaser"), One(Beam, "X-Ray Laser"), One(Beam, "Laser")
                },
                // 5
                new[] { One(Beam, "Blunderbuss"), One(Beam, "Bludgeon"), One(Beam, "Blackjack") },
                // 6
                new[] { One(Beam, "Big Mutha Cannon"), One(Beam, "Gatling Neutrino Cannon"), One(Beam, "Gatling Gun"), One(Beam, "Mini Gun") },
                // 7
                new[] { One(Beam, "Syncro Sapper"), One(Beam, "Phased Sapper"), One(Beam, "Pulsed Sapper") },
                // 8
                new[]
                {
                    One(Eng, "Galaxy Scoop"), One(Eng, "Enigma Pulsar"),
                    Range(Eng, "Trans-Galactic Mizer Scoop", "Radiating Hydro-Ram Scoop"), One(Eng, "Fuel Mizer")
                },
                // 9
                new[]
                {
                    One(Arm, "Superlatanium"), One(Arm, "Mega Poly Shell"), One(Arm, "Valanium"), One(Arm, "Depleted Neutronium"),
                    One(Arm, "Neutronium"), Range(Arm, "Fielded Kelarium", "Tritanium")
                },
                // 10
                new[]
                {
                    Range(Shld, "Complete Phase Shield", "Elephant Hide Fortress"), One(Shld, "Langston Shell"),
                    One(Shld, "Gorilla Delagator"), One(Shld, "Croby Sharmor"), One(Shld, "Shadow Shield"),
                    One(Shld, "Bear Neutrino Barrier"), Range(Shld, "Wolverine Diffuse Shield", "Mole-skin Shield")
                },
                // 11
                new[] { Range(Elec, "Battle Nexus", "Battle Computer") },
                // 12
                new[]
                {
                    One(Elec, "Multi Function Pod"), Range(Elec, "Jammer 50", "Jammer 10"), One(Mech, "Beam Deflector"),
                    Range(Mech, "Overthruster", "Maneuvering Jet")
                },
                // 13
                new[]
                {
                    One(Elec, "Multi Function Pod"), Range(Mech, "Overthruster", "Maneuvering Jet"), One(Elec, "Ultra-Stealth Cloak"),
                    One(Mech, "Beam Deflector"), Range(Elec, "Super-Stealth Cloak", "Stealth Cloak")
                },
                // 14
                new[] { Range(Elec, "Flux Capacitor", "Jammer 10"), Range(Mech, "Super Fuel Tank", "Fuel Tank") },
                // 15
                new[]
                {
                    One(Mech, "Beam Deflector"), Range(Elec, "Flux Capacitor", "Jammer 10"), Range(Elec, "Jammer 50", "Jammer 10"),
                    Range(Mech, "Super Fuel Tank", "Fuel Tank")
                },
                // 16
                new[] { Range(Mech, "Multi Cargo Pod", "Cargo Pod") },
                // 17
                new[] { One(Arm, "Mega Poly Shell"), Range(Arm, "Superlatanium", "Tritanium") },
                // 18
                new[] { Range(Mech, "Overthruster", "Maneuvering Jet"), One(Mech, "Beam Deflector"), Range(Mech, "Super Fuel Tank", "Fuel Tank") },
                // 19
                new[] { Range(Elec, "Jammer 50", "Jammer 10"), Range(Elec, "Battle Nexus", "Battle Computer") },
                // 20
                new[]
                {
                    Range(Elec, "Flux Capacitor", "Jammer 10"), Range(Elec, "Jammer 50", "Jammer 10"),
                    Range(Elec, "Multi Function Pod", "Stealth Cloak"), Range(Elec, "Battle Nexus", "Battle Computer")
                },
                // 21
                new[] { One(Bomb, "Hush-a-Boom"), Range(Bomb, "Cherry Bomb", "Lady Finger Bomb") },
                // 22
                new[] { One(Bomb, "Hush-a-Boom"), One(Bomb, "Retro Bomb"), Range(Bomb, "Annihilator Bomb", "Smart Bomb") },
                // 23
                new[]
                {
                    One(Bomb, "Hush-a-Boom"), Range(Bomb, "Annihilator Bomb", "Smart Bomb"), Range(Bomb, "Cherry Bomb", "Lady Finger Bomb"),
                    One(Bomb, "Retro Bomb")
                },
                // 24
                new[] { One(Eng, "Galaxy Scoop"), One(Eng, "Radiating Hydro-Ram Scoop") },
                // 25
                new[] { Range(MLay, "Mine Dispenser 130", "Mine Dispenser 40") },
                // 26
                new[]
                {
                    One(Scan, "Elephant Scanner"), One(Scan, "Robber Baron Scanner"), One(Scan, "Dolphin Scanner"),
                    One(Scan, "Chameleon Scanner"), One(Scan, "Ferret Scanner"), One(Scan, "Gazelle Scanner"), One(Scan, "Possum Scanner")
                },
                // 27
                new[] { One(Scan, "Robber Baron Scanner"), One(Scan, "Pick Pocket Scanner"), Range(Scan, "Chameleon Scanner", "Bat Scanner") },
                // 28
                new[] { Range(Mine, "Alien Miner", "Robo-Midget Miner") },
                // 29
                new[] { One(Mine, "Orbital Adjuster") },
                // 30
                new[]
                {
                    Range(Eng, "Trans-Star 10", "Enigma Pulsar"), One(Eng, "Radiating Hydro-Ram Scoop"),
                    Range(Eng, "Trans-Galactic Super Scoop", "Sub-Galactic Fuel Scoop"), Range(Eng, "Trans-Galactic Drive", "Long Hump 6")
                },
                // 31
                new[] { Range(Mech, "Orbital Construction Module", "Colonization Module") },
                // 32
                new[] { Range(MLay, "Speed Trap 50", "Speed Trap 20") },
                // 33
                new[] { One(Beam, "Multi Contained Munition") },
                // 34
                new[]
                {
                    Range(Orb, "Ultra Driver 13", "Mass Driver 5"), Range(Elec, "Multi Function Pod", "Stealth Cloak"),
                    Range(Elec, "Jammer 50", "Jammer 10"), Range(Elec, "Battle Nexus", "Battle Computer")
                },
                // 35
                new[]
                {
                    One(Torp, "Armageddon Missile"), One(Torp, "Omega Torpedo"), One(Torp, "Doomsday Missile"),
                    Range(Torp, "Upsilon Torpedo", "Alpha Torpedo")
                },
                // 36
                new[]
                {
                    Range(Beam, "Anti-Matter Pulverizer", "Streaming Pulverizer"), One(Beam, "Disruptor"), One(Beam, "Myopic Disruptor"),
                    One(Beam, "Mark IV Blaster"), One(Beam, "Mini Blaster"), One(Beam, "Phaser Bazooka"),
                    One(Beam, "Yakimora Light Phaser"), Range(Beam, "X-Ray Laser", "Laser")
                },
                // 37
                new[] { One(Shld, "Langston Shell"), Range(Shld, "Complete Phase Shield", "Mole-skin Shield") },
                // 38
                new[]
                {
                    One(Beam, "Mega Disruptor"), One(Beam, "Heavy Blaster"), One(Beam, "Colloidal Phaser"), One(Beam, "Phaser Bazooka"),
                    One(Beam, "Laser")
                },
                // 39
                new[] { Range(Elec, "Jammer 50", "Jammer 10"), Range(Elec, "Multi Function Pod", "Stealth Cloak") },
                // 40
                new[] { One(Mech, "Orbital Construction Module") },
                // 41
                new[]
                {
                    One(Arm, "Mega Poly Shell"), Range(Elec, "Jammer 50", "Jammer 10"), Range(Mech, "Overthruster", "Maneuvering Jet"),
                    One(Mech, "Beam Deflector"), Range(Mech, "Super Fuel Tank", "Fuel Tank")
                },
                // 42
                new[] { Range(Mine, "Alien Miner", "Robo-Maxi-Miner") },
                // 43
                new[] { Range(Mine, "Alien Miner", "Robo-Ultra-Miner"), One(Mine, "Robo-Midget Miner") },
                // 44: the third entry, Settler's Delight, has zero steps and is never tried.
                new[]
                {
                    Range(Eng, "Galaxy Scoop", "Sub-Galactic Fuel Scoop"), One(Eng, "Fuel Mizer"),
                    new PartGroupEntry(Eng, 0, 0)
                },
            };
        }
    }
}
