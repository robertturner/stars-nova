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
    /// The fleet roles the drivers of behavior-specs-10/ai-opponent-behavior.md §12 read from
    /// the AI's design-slot ranges.
    /// </summary>
    public enum AiFleetRole
    {
        None,
        Colonizer,
        Minelayer,
        RemoteMiner,
        Freighter,
        Explorer,
        Hunter,

        /// <summary>A strike warship counted once in the strength S (personality 0 slots 2-5,
        /// personality 5 slots 2-4, personality 4's lower battle-group slots).</summary>
        StrikeLight,

        /// <summary>A strike warship counted twice in S (personality 0 slots 6-7, personality 5
        /// slots 5-7, personality 4's third battle-group slot; personality 2's slot 9-10
        /// garrison, which counts twice in its strength test and escorts its bombers).</summary>
        StrikeHeavy,

        /// <summary>The bomber count B (personality 0 slots 9-10, personality 5 slots 8-9,
        /// personality 2 slots 2-3, personality 1 slots 13-14).</summary>
        Bomber,

        /// <summary>Line warships that rendezvous or hunt (personality 0 slots 14-15,
        /// personality 5 slots 12-13).</summary>
        LineWarship,
    }

    /// <summary>
    /// The original's design slots as roles (§17 reimplementation note: "Replace slot indexes
    /// with a role tag ... Map the starting designs to roles by their content"). A design the
    /// AI's design builder made carries its role tag in its name and is classified by the
    /// personality's role table; any other design is read from its content: the module it
    /// carries (colonizer, mine layer, mining robots), the §11 hull classifiers, and, for
    /// warships, the hull each personality's slot table puts in each role (§12, §17). Where a
    /// personality's table gives the same hull two roles, the first listed wins.
    /// </summary>
    public static class AiFleetRoles
    {
        /// <summary>The role of one design for the given personality category.</summary>
        public static AiFleetRole Classify(ShipDesign design, int category)
        {
            if (design == null || design.IsStarbase)
            {
                return AiFleetRole.None;
            }

            try
            {
                design.Update();
            }
            catch (System.Exception)
            {
                return AiFleetRole.None;
            }

            AiFleetRole? tagged = TaggedRole(design, category);
            if (tagged.HasValue)
            {
                return tagged.Value;
            }

            if (design.CanColonize)
            {
                return AiFleetRole.Colonizer;
            }

            if (design.MineEquivalents > 0)
            {
                return AiFleetRole.RemoteMiner;
            }

            if (design.StandardMines.LayerRate + design.HeavyMines.LayerRate + design.SpeedBumbMines.LayerRate > 0)
            {
                return AiFleetRole.Minelayer;
            }

            int hull = FleetRolePredicates.HullId(design);
            bool bomberHull = hull >= 16 && hull <= 19;
            if (bomberHull || design.IsBomber)
            {
                return AiFleetRole.Bomber;
            }

            if (FleetRolePredicates.IsHaulerDesign(design))
            {
                return AiFleetRole.Freighter;
            }

            if (hull == 4)
            {
                // Scout hull: explorers and hunters (personalities 1-3 slot 0; the hunter variant
                // sends an unarmed one exploring).
                return FleetRolePredicates.HasBeamOrTorpedo(design) ? AiFleetRole.Hunter : AiFleetRole.Explorer;
            }

            if (!FleetRolePredicates.IsCombatDesign(design))
            {
                return AiFleetRole.None;
            }

            return WarshipRole(hull, category);
        }

        /// <summary>
        /// The role of a design the AI's design builder made: its role tag (AiDesignRoleTag,
        /// §17) looked up in the personality's role table. Null for an untagged design (a
        /// starting design or one from another personality), which is then read by content.
        /// Personality 4's battle-group slots count as in its S and B: slots 6, 7, 10, 11 once,
        /// 8 and 12 twice, 9 and 13 as bombers; guards and reserve roles have no fleet branch.
        /// </summary>
        private static AiFleetRole? TaggedRole(ShipDesign design, int category)
        {
            string tag = AiDesignRoleTag.TagOf(design);
            if (tag == null)
            {
                return null;
            }

            AiDesignRole role = AiDesignRoleTable.ForCategory(category).FirstOrDefault(r => r.Tag == tag);
            if (role == null)
            {
                return null;
            }

            switch (role.Kind)
            {
                case AiDesignRoleKind.Explorer:
                    return FleetRolePredicates.HasBeamOrTorpedo(design) ? AiFleetRole.Hunter : AiFleetRole.Explorer;
                case AiDesignRoleKind.Hunter:
                    return AiFleetRole.Hunter;
                case AiDesignRoleKind.Minelayer:
                    return AiFleetRole.Minelayer;
                case AiDesignRoleKind.Colonizer:
                    return AiFleetRole.Colonizer;
                case AiDesignRoleKind.StrikeWarship1:
                case AiDesignRoleKind.HalfStrengthWarship:
                    return AiFleetRole.StrikeLight;
                case AiDesignRoleKind.StrikeWarship2:
                case AiDesignRoleKind.Garrison:
                    return AiFleetRole.StrikeHeavy;
                case AiDesignRoleKind.Bomber:
                    return AiFleetRole.Bomber;
                case AiDesignRoleKind.Freighter:
                case AiDesignRoleKind.Hauler:
                    return AiFleetRole.Freighter;
                case AiDesignRoleKind.LineWarship:
                    return AiFleetRole.LineWarship;
                case AiDesignRoleKind.RemoteMiner:
                    return AiFleetRole.RemoteMiner;
                case AiDesignRoleKind.BattleGroup:
                    if (role.Slot == 9 || role.Slot == 13)
                    {
                        return AiFleetRole.Bomber;
                    }

                    return role.Slot == 8 || role.Slot == 12 ? AiFleetRole.StrikeHeavy : AiFleetRole.StrikeLight;
                default:
                    return AiFleetRole.None;
            }
        }

        /// <summary>
        /// The warship role of a combat-classified hull per personality (§12 slot tables):
        /// <list type="bullet">
        /// <item>0: Meta Morph strength 1 (slots 2-5), Battleship strength 2 (6-7), Nubian and
        /// Destroyer line warships (14-15); other combat hulls, which personality 0 never
        /// builds, are taken as strength 1 (Frigate, Cruiser) or 2 (Battle Cruiser,
        /// Dreadnought).</item>
        /// <item>1: Frigate and Destroyer hunters (slots 0 and 10), Battleship half-strength
        /// warships (slot 4, counted against K/2).</item>
        /// <item>2: Battleship garrison (slots 9-10, counted twice in the strength test, never
        /// moved); every other warship is the slot 11-12 kind, counted once and never moved.</item>
        /// <item>3: starting designs only; a warship counts as a hunter.</item>
        /// <item>4: Destroyer hunters (slots 4-5); Cruiser lower battle-group slots (strength 1);
        /// Nubian and Battleship upper slots (strength 2).</item>
        /// <item>5: Cruiser strength 1 (2-4), Nubian and Battleship strength 2 (5-7), Destroyer
        /// line warships (12-13).</item>
        /// </list>
        /// </summary>
        public static AiFleetRole WarshipRole(int hull, int category)
        {
            const int Destroyer = 6, Cruiser = 7, BattleCruiser = 8, Battleship = 9, Dreadnought = 10;

            switch (category)
            {
                case AiCategory.Robotoids:
                    if (hull == FleetRolePredicates.MetaMorph || hull == FleetRolePredicates.Frigate || hull == Cruiser)
                    {
                        return AiFleetRole.StrikeLight;
                    }

                    if (hull == Battleship || hull == BattleCruiser || hull == Dreadnought)
                    {
                        return AiFleetRole.StrikeHeavy;
                    }

                    return AiFleetRole.LineWarship;

                case AiCategory.Turindrones:
                    return hull == Battleship ? AiFleetRole.StrikeLight : AiFleetRole.Hunter;

                case AiCategory.Automitrons:
                    return hull == Battleship ? AiFleetRole.StrikeHeavy : AiFleetRole.StrikeLight;

                case AiCategory.Rototills:
                    return AiFleetRole.Hunter;

                case AiCategory.Cybertrons:
                    if (hull == Destroyer || hull == FleetRolePredicates.Frigate)
                    {
                        return AiFleetRole.Hunter;
                    }

                    return hull == Cruiser || hull == FleetRolePredicates.MetaMorph ? AiFleetRole.StrikeLight : AiFleetRole.StrikeHeavy;

                case AiCategory.Macinti:
                    if (hull == Destroyer || hull == FleetRolePredicates.Frigate)
                    {
                        return AiFleetRole.LineWarship;
                    }

                    return hull == Cruiser || hull == FleetRolePredicates.MetaMorph ? AiFleetRole.StrikeLight : AiFleetRole.StrikeHeavy;

                default:
                    return AiFleetRole.None;
            }
        }

        /// <summary>Ships per role in a fleet's occupied stacks.</summary>
        public static Dictionary<AiFleetRole, int> CountRoles(IEnumerable<ShipToken> composition, int category)
        {
            Dictionary<AiFleetRole, int> counts = new Dictionary<AiFleetRole, int>();
            foreach (ShipToken token in (composition ?? Enumerable.Empty<ShipToken>()).Where(t => t != null && t.Quantity > 0 && t.Design != null))
            {
                AiFleetRole role = Classify(token.Design, category);
                counts.TryGetValue(role, out int existing);
                counts[role] = existing + token.Quantity;
            }

            return counts;
        }

        public static int Count(Dictionary<AiFleetRole, int> counts, AiFleetRole role)
        {
            return counts != null && counts.TryGetValue(role, out int count) ? count : 0;
        }
    }
}
