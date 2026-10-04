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

        /// <summary>What kind of object a scan source is: the minefield test differs (a ship also
        /// sees a field it is inside, a Packet Physics packet only within its range).</summary>
        public enum ScanSourceKind
        {
            Ship,
            Planet,
            Packet
        }

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
        /// Minefield detection, complete rule (behavior-specs-11/fleet-movement-scanning-cargo.md
        /// section 3): a field is a circle of radius sqrt(mines); a race that already knows it
        /// (<paramref name="known"/>) sees it within the full normal range r, and any observer sees
        /// it within the penetrating range p or within r/4. A ship (not a planet) also sees a
        /// field its own position is inside (squared distance at most the mine count); a Packet
        /// Physics packet sees one only within its range. No roll.
        /// </summary>
        public static bool DetectsMinefield(NovaPoint scannerPosition, int normalRange, int penetratingRange, ScanSourceKind kind, bool known, Minefield field)
        {
            if (scannerPosition == null || field == null || field.Position == null)
            {
                return false;
            }

            double squared = PointUtilities.DistanceSquare(scannerPosition, field.Position);
            int normal = Math.Max(0, normalRange);
            int penetrating = Math.Max(0, penetratingRange);

            if (kind == ScanSourceKind.Packet)
            {
                return squared <= (double)normal * normal;
            }

            if (known && squared <= (double)normal * normal)
            {
                return true;
            }

            if (squared <= (double)penetrating * penetrating)
            {
                return true;
            }

            double quarter = normal / 4.0;
            if (squared <= quarter * quarter)
            {
                return true;
            }

            return kind == ScanSourceKind.Ship && squared <= field.NumberOfMines;
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
        /// Wormhole detection: a wormhole end is seen within the full normal range r when the race
        /// already has it located, or within r/4, or within the penetrating range p; no roll
        /// (behavior-specs-11/fleet-movement-scanning-cargo.md section 3, the located mask).
        /// </summary>
        public static bool DetectsWormhole(NovaPoint scannerPosition, int normalRange, int penetratingRange, bool located, NovaPoint wormholePosition)
        {
            if (scannerPosition == null || wormholePosition == null)
            {
                return false;
            }

            double squared = PointUtilities.DistanceSquare(scannerPosition, wormholePosition);
            int normal = Math.Max(0, normalRange);
            int penetrating = Math.Max(0, penetratingRange);

            if (located && squared <= (double)normal * normal)
            {
                return true;
            }

            if (squared <= (double)penetrating * penetrating)
            {
                return true;
            }

            double quarter = normal / 4.0;
            return squared <= quarter * quarter;
        }
    }
}
