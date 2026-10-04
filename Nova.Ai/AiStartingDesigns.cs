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

    /// <summary>What a starting design is, judged by its content.</summary>
    public enum AiStartingRole
    {
        Scout,
        Colonizer,
        Miner,
        Minelayer,
        Other
    }

    /// <summary>One starting design of an AI player as §14 lists it.</summary>
    public sealed class AiStartingDesign
    {
        public int Slot;
        public string Name;
        public AiStartingRole Role;
        public int Ships;

        public AiStartingDesign(int slot, string name, AiStartingRole role, int ships)
        {
            Slot = slot;
            Name = name;
            Role = role;
            Ships = ships;
        }
    }

    /// <summary>
    /// AI starting designs (behavior-specs-10/ai-opponent-behavior.md §14, "Starting ships and
    /// designs of AI players"): the templates hold no designs, so an AI player gets the ordinary
    /// per-PRT starting fleet of new-game-setup.md §5a, filled in creation order - slot 0 is
    /// always a scout and slot 1 always a colonizer. §17 maps them onto roles by content: the
    /// first scout is the slot-0 role, the colonizer the slot-1 role, a starting miner the
    /// miner role (<see cref="RoleTagFor"/>).
    /// </summary>
    /// <remarks>
    /// Creating these named designs at game start is new-game setup (§5a), not this class:
    /// <see cref="ForArchetype"/> only lists what §14 says an AI of each archetype starts with,
    /// and <see cref="AssignRoles"/> classifies whatever starting designs the empire actually has.
    /// </remarks>
    public static class AiStartingDesigns
    {
        /// <summary>§14's per-archetype list. A Turindrones scout is a Smaugarian Peeping Tom
        /// below Energy 2 and a Shadow Sleuth otherwise (new-game-setup.md §5a, as for any SS
        /// race).</summary>
        public static IReadOnlyList<AiStartingDesign> ForArchetype(int archetype, int tier, int startingEnergy = 0)
        {
            switch (archetype)
            {
                case AiCategory.Robotoids:
                    return new[]
                    {
                        new AiStartingDesign(0, "Smaugarian Peeping Tom", AiStartingRole.Scout, 1),
                        new AiStartingDesign(1, "Spore Cloud", AiStartingRole.Colonizer, 3),
                    };
                case AiCategory.Turindrones:
                    return new[]
                    {
                        new AiStartingDesign(0, startingEnergy >= 2 ? "Shadow Sleuth" : "Smaugarian Peeping Tom", AiStartingRole.Scout, 1),
                        new AiStartingDesign(1, "Santa Maria", AiStartingRole.Colonizer, 1),
                        new AiStartingDesign(2, "Potato Bug", AiStartingRole.Minelayer, 2),
                    };
                case AiCategory.Automitrons:
                    return new[]
                    {
                        new AiStartingDesign(0, "Smaugarian Peeping Tom", AiStartingRole.Scout, 1),
                        new AiStartingDesign(1, "Santa Maria", AiStartingRole.Colonizer, 1),
                    };
                case AiCategory.Rototills:
                    return new[]
                    {
                        new AiStartingDesign(0, "Smaugarian Peeping Tom", AiStartingRole.Scout, 1),
                        new AiStartingDesign(1, "Santa Maria", AiStartingRole.Colonizer, 1),
                        new AiStartingDesign(2, "Change of Heart", AiStartingRole.Other, 1),
                    };
                case AiCategory.Cybertrons:
                    return new[]
                    {
                        new AiStartingDesign(0, "Long Range Scout", AiStartingRole.Scout, 1),
                        new AiStartingDesign(1, "Santa Maria", AiStartingRole.Colonizer, 1),
                    };
                case AiCategory.Macinti:
                    List<AiStartingDesign> list = new List<AiStartingDesign>
                    {
                        new AiStartingDesign(0, "Smaugarian Peeping Tom", AiStartingRole.Scout, 1),
                        new AiStartingDesign(1, "Pinta", AiStartingRole.Colonizer, 1),
                    };
                    if (tier >= AiRaceTemplates.Tough)
                    {
                        list.Add(new AiStartingDesign(2, "Potato Bug", AiStartingRole.Minelayer, 2));
                    }

                    return list;
                default:
                    return new AiStartingDesign[0];
            }
        }

        /// <summary>Classifies a design by what it carries.</summary>
        public static AiStartingRole Classify(ShipDesign design)
        {
            if (design == null || design.Blueprint == null || design.Type == ItemType.Starbase
                || !design.Blueprint.Properties.ContainsKey("Hull") || design.IsStarbase)
            {
                return AiStartingRole.Other;
            }

            List<Component> parts = design.Hull.Modules
                .Where(module => module.AllocatedComponent != null)
                .Select(module => module.AllocatedComponent)
                .ToList();

            if (parts.Any(part => part.Properties.ContainsKey("Colonizer")))
            {
                return AiStartingRole.Colonizer;
            }

            if (parts.Any(part => part.Type == ItemType.MiningRobot))
            {
                return AiStartingRole.Miner;
            }

            if (parts.Any(part => part.Type == ItemType.MineLayer))
            {
                return AiStartingRole.Minelayer;
            }

            if (parts.Any(part => part.Type == ItemType.BeamWeapons || part.Type == ItemType.Torpedoes || part.Type == ItemType.Bomb
                || part.Type == ItemType.Weapon))
            {
                return AiStartingRole.Other;
            }

            if (parts.Any(part => part.Type == ItemType.Scanner) || design.Blueprint.Name == "Scout")
            {
                return AiStartingRole.Scout;
            }

            return AiStartingRole.Other;
        }

        /// <summary>The first design (lowest key) of each starting role, starbases skipped.</summary>
        public static Dictionary<AiStartingRole, ShipDesign> AssignRoles(IEnumerable<ShipDesign> designs)
        {
            Dictionary<AiStartingRole, ShipDesign> roles = new Dictionary<AiStartingRole, ShipDesign>();
            foreach (ShipDesign design in designs.OrderBy(d => d.Key))
            {
                AiStartingRole role = Classify(design);
                if (role != AiStartingRole.Other && !roles.ContainsKey(role))
                {
                    roles[role] = design;
                }
            }

            return roles;
        }

        /// <summary>
        /// The slot-0 scout and slot-1 colonizer (§14) the empire lacks, judged by content
        /// (<see cref="AssignRoles"/> over all its designs), built from the available components
        /// and named after §14's list for the archetype; empty when it already has both. Each
        /// gets a fresh key from <paramref name="nextKey"/> and a name no existing design has.
        /// Only the scout (Scout hull: engine and scanner) and the colonizer (Colony Ship, else
        /// Mini-Colony Ship: engine and colonization module) are built; the part choices follow
        /// new-game-setup.md §5b's starting upgrade order (an interpretation: §14 names the
        /// designs, not their parts). A role whose hull or parts are not available is skipped.
        /// </summary>
        public static List<ShipDesign> BuildMissing(
            IEnumerable<ShipDesign> designs,
            IDictionary<string, Component> available,
            int archetype,
            int tier,
            int startingEnergy,
            System.Func<long> nextKey)
        {
            List<ShipDesign> existing = designs.Where(design => design != null).ToList();
            Dictionary<AiStartingRole, ShipDesign> have = AssignRoles(existing);
            HashSet<string> names = new HashSet<string>(existing.Where(design => design.Name != null).Select(design => design.Name));
            List<ShipDesign> built = new List<ShipDesign>();

            foreach (AiStartingDesign start in ForArchetype(archetype, tier, startingEnergy).Where(s => s.Slot <= 1))
            {
                if (have.ContainsKey(start.Role))
                {
                    continue;
                }

                ShipDesign design = null;
                if (start.Role == AiStartingRole.Scout)
                {
                    design = BuildStarting(available, new[] { "Scout" }, "Scanner", StartingScanners);
                }
                else if (start.Role == AiStartingRole.Colonizer)
                {
                    design = BuildStarting(available, new[] { "Colony Ship", "Mini-Colony Ship" }, "Mechanical", ColonizationModules);
                }

                if (design == null)
                {
                    continue;
                }

                string name = start.Name;
                for (int attempt = 2; names.Contains(name); attempt++)
                {
                    name = start.Name + " " + attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                design.Name = name;
                design.Key = nextKey();
                design.Update();
                names.Add(name);
                have[start.Role] = design;
                built.Add(design);
            }

            return built;
        }

        /// <summary>Starting engines, best first (new-game-setup.md §5b's Quick Jump 5 upgrade
        /// list, then Quick Jump 5 itself).</summary>
        private static readonly string[] StartingEngines =
        {
            "Radiating Hydro-Ram Scoop", "Alpha Drive 8", "Daddy Long Legs 7", "Fuel Mizer", "Long Hump 6", "Quick Jump 5"
        };

        /// <summary>Starting scanners (§5b's Bat Scanner upgrade list, then the Bat Scanner).</summary>
        private static readonly string[] StartingScanners = { "Possum Scanner", "Mole Scanner", "Rhino Scanner", "Bat Scanner" };

        /// <summary>Group 31 (§15): the Orbital Construction Module, then the Colonization
        /// Module; race availability decides which one a race has.</summary>
        private static readonly string[] ColonizationModules = { "Orbital Construction Module", "Colonization Module" };

        private static Component FirstAvailable(IDictionary<string, Component> available, IEnumerable<string> specNames)
        {
            foreach (string specName in specNames)
            {
                foreach (string name in DesignPartGroups.NovaNames(specName))
                {
                    if (available.TryGetValue(name, out Component component) && component != null)
                    {
                        return component;
                    }
                }
            }

            return null;
        }

        /// <summary>A design on the first available hull with one engine and one part of the
        /// given module type, or null. The Mini-Colony Ship takes the Settler's Delight first,
        /// as Nova's new-game setup gives it.</summary>
        private static ShipDesign BuildStarting(IDictionary<string, Component> available, string[] hulls, string moduleType, string[] parts)
        {
            if (available == null)
            {
                return null;
            }

            Component hullComponent = FirstAvailable(available, hulls);
            Component part = FirstAvailable(available, parts);
            if (hullComponent == null || part == null || !(hullComponent.Properties.TryGetValue("Hull", out ComponentProperty property) && property is Hull))
            {
                return null;
            }

            IEnumerable<string> engines = hullComponent.Name == "Mini-Colony Ship" ? new[] { "Settler's Delight" }.Concat(StartingEngines) : StartingEngines;
            Component engine = FirstAvailable(available, engines);
            if (engine == null)
            {
                return null;
            }

            Component blueprint = new Component(hullComponent);
            bool engineFitted = false;
            bool partFitted = false;
            foreach (HullModule module in ((Hull)blueprint.Properties["Hull"]).Modules)
            {
                module.Empty();
                if (!engineFitted && module.ComponentType == "Engine")
                {
                    module.AllocatedComponent = engine;
                    module.ComponentCount = 1;
                    engineFitted = true;
                }
                else if (!partFitted && module.ComponentType == moduleType)
                {
                    module.AllocatedComponent = part;
                    module.ComponentCount = 1;
                    partFitted = true;
                }
            }

            if (!engineFitted || !partFitted)
            {
                return null;
            }

            ShipDesign design = new ShipDesign(0);
            design.Blueprint = blueprint;
            design.Type = ItemType.Ship;
            design.Icon = new ShipIcon(hullComponent.ImageFile, hullComponent.ComponentImage);
            return design;
        }

        /// <summary>§17: the role tag a starting design of a category stands in for - the
        /// scout the slot-0 role, the colonizer the slot-1 role, a miner the first remote-miner
        /// role - or null.</summary>
        public static string RoleTagFor(int category, AiStartingRole startingRole)
        {
            IReadOnlyList<AiDesignRole> roles = AiDesignRoleTable.ForCategory(category);
            AiDesignRole match = null;
            switch (startingRole)
            {
                case AiStartingRole.Scout:
                    match = roles.FirstOrDefault(role => role.Slot == 0);
                    break;
                case AiStartingRole.Colonizer:
                    match = roles.FirstOrDefault(role => role.Slot == 1);
                    break;
                case AiStartingRole.Miner:
                    match = roles.FirstOrDefault(role => role.Kind == AiDesignRoleKind.RemoteMiner);
                    break;
            }

            return match == null ? null : match.Tag;
        }
    }
}
