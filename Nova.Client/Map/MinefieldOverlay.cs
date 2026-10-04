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

    /// <summary>The three per-type minefield fill patterns (behavior-specs-11/client-interface.md,
    /// "Minefield visibility overlay": "draws minefields using one of three distinct fill
    /// patterns (matching the game's three minefield types)").</summary>
    public enum MinefieldPattern
    {
        Standard,
        Heavy,
        SpeedBump,
    }

    /// <summary>
    /// The minefield overlay's independent visibility mask. behavior-specs-11/client-interface.md
    /// describes "an independent 4-bit visibility mask covering the player's own fields, other
    /// owners' fields, and a further split for enemy fields depending on whether they have been
    /// detected (checked against a per-relationship detection table)". The spec gives no bit
    /// ordering, so this is the port's own (SPEC GAP): own, other owners, detected enemy,
    /// undetected enemy.
    /// </summary>
    [Flags]
    public enum MinefieldVisibility
    {
        None = 0,
        Own = 1,
        Others = 2,
        DetectedEnemy = 4,
        UndetectedEnemy = 8,
        All = Own | Others | DetectedEnemy | UndetectedEnemy,
    }

    /// <summary>
    /// The pure minefield-overlay rules kept in Nova.Client so they are testable
    /// (behavior-specs-11/client-interface.md, "Minefield visibility overlay").
    /// </summary>
    public static class MinefieldOverlay
    {
        /// <summary>The fill pattern for a field type (Standard / Heavy / Speed Bump).</summary>
        public static MinefieldPattern PatternOf(MinefieldType type)
        {
            switch (type)
            {
                case MinefieldType.Heavy:
                    return MinefieldPattern.Heavy;
                case MinefieldType.SpeedBump:
                    return MinefieldPattern.SpeedBump;
                default:
                    return MinefieldPattern.Standard;
            }
        }

        /// <summary>The single visibility category a field belongs to. A field the viewer owns is
        /// Own whatever its detection state; a foreign field is split into "other owners" or the
        /// two enemy classes by the relationship and whether it was detected.</summary>
        public static MinefieldVisibility CategoryOf(bool isOwn, bool isEnemy, bool detected)
        {
            if (isOwn)
            {
                return MinefieldVisibility.Own;
            }

            if (isEnemy)
            {
                return detected ? MinefieldVisibility.DetectedEnemy : MinefieldVisibility.UndetectedEnemy;
            }

            return MinefieldVisibility.Others;
        }

        /// <summary>True when <paramref name="mask"/> shows a field of that category.</summary>
        public static bool IsVisible(MinefieldVisibility mask, bool isOwn, bool isEnemy, bool detected)
        {
            return (mask & CategoryOf(isOwn, isEnemy, detected)) != 0;
        }
    }
}
