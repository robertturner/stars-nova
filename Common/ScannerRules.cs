#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Scanning and detection rules shared by the server's visibility pass (ScanStep) and any
    /// client that wants to show the same ranges. behavior-specs-10/fleet-movement-scanning-cargo.md
    /// section 3 and race-traits.md section 2.
    /// </summary>
    public static class ScannerRules
    {
        /// <summary>The hulls that carry the Jack of All Trades built-in scanner.</summary>
        public static readonly string[] JoatScannerHulls = { "Scout", "Frigate", "Destroyer" };

        /// <summary>Normal range of the JOAT built-in scanner per Electronics level (ly).</summary>
        public const int JoatNormalRangePerElectronicsLevel = 20;

        /// <summary>Penetrating range of the JOAT built-in scanner per Electronics level (ly).</summary>
        public const int JoatPenetratingRangePerElectronicsLevel = 10;

        /// <summary>Added to a minefield's radius to give its flat detection radius.</summary>
        public const int MinefieldDetectionPadding = 4;

        /// <summary>A wormhole is cloaked 75% until the observer has discovered it once.</summary>
        public const int UndiscoveredWormholeCloakPercent = 75;

        /// <summary>
        /// Combines two scanner ranges the way one design's scanners combine: the fourth root of
        /// the sum of the fourth powers (section 3, "Combining multiple scanners on one design").
        /// </summary>
        public static int Combine(int first, int second)
        {
            if (first <= 0)
            {
                return Math.Max(0, second);
            }

            if (second <= 0)
            {
                return first;
            }

            return (int)Math.Pow(Math.Pow(first, 4) + Math.Pow(second, 4), 0.25);
        }

        /// <summary>
        /// True when the design gets the Jack of All Trades built-in penetrating scanner: the race
        /// is JOAT and the hull is a Scout, Frigate or Destroyer (race-traits.md section 2; the
        /// scanner combination routine preloads it for those hull indices when the PRT is 9,
        /// ship-design-and-components.md, FUN_1038_337e).
        /// </summary>
        public static bool HasJoatBuiltInScanner(ShipDesign design, Race race)
        {
            if (design == null || race == null || design.Blueprint == null || !race.HasTrait("JOAT"))
            {
                return false;
            }

            return Array.IndexOf(JoatScannerHulls, design.Blueprint.Name) >= 0;
        }

        /// <summary>
        /// The JOAT built-in scanner at the given Electronics level: 20 ly normal and 10 ly
        /// penetrating per level. The specs say only that it "scales with Electronics tech"; the
        /// per-level figures are the published community values.
        /// </summary>
        public static void JoatBuiltInScanner(int electronicsLevel, out int normal, out int penetrating)
        {
            int level = Math.Max(0, electronicsLevel);
            normal = JoatNormalRangePerElectronicsLevel * level;
            penetrating = JoatPenetratingRangePerElectronicsLevel * level;
        }

        /// <summary>
        /// A design's normal and penetrating ranges for its owner, including the JOAT built-in
        /// scanner. The design's own figures already carry No Advanced Scanners' doubling of the
        /// normal range (ShipDesign.Update); the built-in normal range is doubled too under NAS,
        /// since the doubling is applied to the whole combined range, while the built-in
        /// penetrating range survives NAS (section 3: it is an inherent racial ability).
        /// </summary>
        public static void DesignScanRanges(ShipDesign design, Race race, TechLevel techLevels, out int normal, out int penetrating)
        {
            normal = design == null ? 0 : design.ScanRangeNormal;
            penetrating = design == null ? 0 : design.ScanRangePenetrating;

            if (techLevels == null || !HasJoatBuiltInScanner(design, race))
            {
                return;
            }

            JoatBuiltInScanner(techLevels[TechLevel.ResearchField.Electronics], out int builtInNormal, out int builtInPenetrating);
            if (race.HasTrait("NAS"))
            {
                builtInNormal *= 2;
            }

            normal = Combine(normal, builtInNormal);
            penetrating = Combine(penetrating, builtInPenetrating);
        }

        /// <summary>
        /// A fleet's normal and penetrating ranges: the best design in it (as Fleet.ScanRange and
        /// Fleet.PenScanRange), with each design's JOAT built-in scanner included.
        /// </summary>
        public static void FleetScanRanges(Fleet fleet, Race race, TechLevel techLevels, out int normal, out int penetrating)
        {
            normal = 0;
            penetrating = 0;
            if (fleet == null)
            {
                return;
            }

            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (token.Design == null)
                {
                    continue;
                }

                DesignScanRanges(token.Design, race, techLevels, out int designNormal, out int designPenetrating);
                normal = Math.Max(normal, designNormal);
                penetrating = Math.Max(penetrating, designPenetrating);
            }
        }

        /// <summary>
        /// A minefield's flat detection radius: its size value plus 4 (section 3, "Minefield
        /// detection ... the minefield's stored warning/size value plus 4, squared"). The size
        /// value is read here as the field's radius, sqrt(mines).
        /// </summary>
        public static int MinefieldDetectionRadius(Minefield field)
        {
            return field == null ? 0 : field.Radius + MinefieldDetectionPadding;
        }

        /// <summary>
        /// True when a scanner at <paramref name="scannerPosition"/> with normal range
        /// <paramref name="scanRange"/> detects the field: squared distance at most
        /// (range + detection radius) squared. Minefields carry no cloak, so the cloak-reduced
        /// fleet test does not apply.
        /// </summary>
        public static bool DetectsMinefield(NovaPoint scannerPosition, int scanRange, Minefield field)
        {
            if (scannerPosition == null || field == null || field.Position == null)
            {
                return false;
            }

            double reach = Math.Max(0, scanRange) + MinefieldDetectionRadius(field);
            return PointUtilities.DistanceSquare(scannerPosition, field.Position) <= reach * reach;
        }

        /// <summary>True when the point lies inside the field (squared distance at most the mine count).</summary>
        public static bool IsInsideMinefield(NovaPoint position, Minefield field)
        {
            if (position == null || field == null || field.Position == null)
            {
                return false;
            }

            return PointUtilities.DistanceSquare(position, field.Position) <= field.NumberOfMines;
        }

        /// <summary>
        /// Space Demolition minefield detection: a fleet inside one of the race's fields is
        /// spotted with chance (100 - cloak%) per year, one 0-99 roll (section 3, "Cloaking vs.
        /// scanning"; Example 3). An uncloaked fleet is always spotted and needs no roll.
        /// </summary>
        public static bool SpaceDemolitionFieldDetects(double cloakPercent, Random random)
        {
            int chance = 100 - (int)Math.Ceiling(Math.Max(0, cloakPercent));
            if (chance >= 100)
            {
                return true;
            }

            if (chance <= 0)
            {
                return false;
            }

            return random.Next(100) < chance;
        }

        /// <summary>
        /// Wormhole detection: a flat radius test (the observer's unreduced normal range) and,
        /// while the wormhole is still cloaked to the observer, a 0-99 roll that must reach the
        /// cloak percentage (section 3, "Wormhole ... detection is ... probabilistic"; section 5,
        /// wormholes are cloaked 75% until discovered once). No roll is made out of range or once
        /// the wormhole is known.
        /// </summary>
        public static bool DetectsWormhole(NovaPoint scannerPosition, int scanRange, NovaPoint wormholePosition, bool alreadyDiscovered, Random random)
        {
            if (scannerPosition == null || wormholePosition == null)
            {
                return false;
            }

            double range = Math.Max(0, scanRange);
            if (PointUtilities.DistanceSquare(scannerPosition, wormholePosition) > range * range)
            {
                return false;
            }

            if (alreadyDiscovered)
            {
                return true;
            }

            return random.Next(100) >= UndiscoveredWormholeCloakPercent;
        }
    }
}
