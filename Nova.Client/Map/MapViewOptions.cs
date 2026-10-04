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

    /// <summary>What the "Planets:" view mode paints on each planet (behavior-specs-10/
    /// client-interface.md, "Planet display-mode ring/bar overlay").</summary>
    public enum PlanetOverlayKind
    {
        /// <summary>No extra per-planet overlay (two of the six modes).</summary>
        None,

        /// <summary>3-segment bar, each value normalised against a shared reference maximum.</summary>
        MineralAmount,

        /// <summary>3-segment bar from 0-100 percentages divided by 5.</summary>
        MineralConcentration,

        /// <summary>Two-tone habitability "bullseye" ring pair.</summary>
        Habitability,

        /// <summary>Single population ring.</summary>
        Population,
    }

    /// <summary>
    /// The map's two packed view-option words and their addressable slots (behavior-specs-10/
    /// client-interface.md, "Shared view-option slots", "View-option bit 0x10 (first word)",
    /// "Remaining bits of the second view-options word", and "Reusable control behavior" for the
    /// digit keys).
    /// First word: low nibble = the 6-way "Planets:" mode (0-5, slots 0-5, shared with the
    /// object-information panel's content mode); 0x10 = the latched "Shift held" course-plotting
    /// substitute (slot 6, never bound to a digit key); 0x20 = scan circles (slot 7, key 7);
    /// 0x40 = minefields (slot 8, key 8); 0x80 = route-overlap dashing (slot 9, key 9).
    /// Second word: 0x01 slot 10, 0x04 slot 11 (planet names, key 0), 0x02 slot 12, 0x08 slot 14,
    /// 0x10 slot 17 (ship-count badge, Shift+0); 0x20 = owner colouring of badges and names
    /// (View > Player Colors, not a slot); 0x40/0x80 unused.
    /// </summary>
    public sealed class MapViewOptions
    {
        public const int ModeMask = 0x0F;
        public const int ShiftLatchBit = 0x10;
        public const int ScanCirclesBit = 0x20;
        public const int MinefieldsBit = 0x40;
        public const int RouteOverlapBit = 0x80;

        public const int BadgeExcludeUndetectedBit = 0x01;
        public const int BadgeExcludeForeignBit = 0x02;
        public const int PlanetNamesBit = 0x04;
        public const int BadgeForeignRaceMaskBit = 0x08;
        public const int BadgeBit = 0x10;
        public const int PlayerColorsBit = 0x20;

        public const int ModeCount = 6;
        public const int MinScannerPercentage = 2;
        public const int MaxScannerPercentage = 100;

        /// <summary>
        /// SPEC GAP seam: which overlay each of the six mode values (0-5) paints. The spec names the
        /// four painting modes and says two paint nothing, but not their slot order. Neutral default:
        /// the spec's own listing order (the two bar modes, the bullseye, the population ring), then
        /// the two non-painting modes.
        /// </summary>
        public static readonly PlanetOverlayKind[] ModeOverlays =
        {
            PlanetOverlayKind.MineralAmount,
            PlanetOverlayKind.MineralConcentration,
            PlanetOverlayKind.Habitability,
            PlanetOverlayKind.Population,
            PlanetOverlayKind.None,
            PlanetOverlayKind.None,
        };

        /// <summary>SPEC GAP seam: the selector's captions (no strings are given). Neutral labels
        /// carrying the digit key that selects each mode.</summary>
        public static readonly string[] ModeLabels =
        {
            "1: Mineral amounts",
            "2: Mineral concentrations",
            "3: Habitability",
            "4: Population",
            "5: No overlay",
            "6: No overlay (alternate)",
        };

        /// <summary>SPEC GAP seam: the initial mode (not specified). Neutral default: the first
        /// non-painting mode, so a fresh map looks like it did before the overlays existed.</summary>
        public const int DefaultMode = 4;

        /// <summary>SPEC GAP seam: the initial first word (not specified). Neutral default keeps what
        /// this port already drew (scan circles, minefields) and turns on route dashing.</summary>
        public const int DefaultWord1 = DefaultMode | ScanCirclesBit | MinefieldsBit | RouteOverlapBit;

        /// <summary>SPEC GAP seam: the initial second word. Neutral default keeps what this port
        /// already drew (planet names, ship-count badges).</summary>
        public const int DefaultWord2 = PlanetNamesBit | BadgeBit;

        private int word1 = DefaultWord1;
        private int word2 = DefaultWord2;
        private int scannerPercentage = MaxScannerPercentage;

        /// <summary>The 4-bit minefield owner mask (slot 8's companion at DS 0x4a7c); the field
        /// overlay button "reads as pressed only when the bit is set and the mask is full"
        /// (behavior-specs-11/client-interface.md, "Shared view-option slots"). Default: all
        /// categories shown.</summary>
        private MinefieldVisibility minefieldMask = MinefieldVisibility.All;

        /// <summary>Raised after any change.</summary>
        public event EventHandler Changed;

        public int Word1 => word1;

        public int Word2 => word2;

        /// <summary>The "Planets:" mode, 0-5.</summary>
        public int Mode
        {
            get => word1 & ModeMask;
            set
            {
                if (value < 0 || value >= ModeCount)
                {
                    return;
                }

                SetWord1((word1 & ~ModeMask) | value);
            }
        }

        public PlanetOverlayKind Overlay => ModeOverlays[Mode];

        public bool ShowScanCircles
        {
            get => (word1 & ScanCirclesBit) != 0;
            set => SetWord1(value ? word1 | ScanCirclesBit : word1 & ~ScanCirclesBit);
        }

        public bool ShowMinefields
        {
            get => (word1 & MinefieldsBit) != 0;
            set => SetWord1(value ? word1 | MinefieldsBit : word1 & ~MinefieldsBit);
        }

        /// <summary>The minefield owner visibility mask (see the field's own comment).</summary>
        public MinefieldVisibility MinefieldMask
        {
            get => minefieldMask;
            set
            {
                if (minefieldMask != value)
                {
                    minefieldMask = value;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool ShowRouteOverlap
        {
            get => (word1 & RouteOverlapBit) != 0;
            set => SetWord1(value ? word1 | RouteOverlapBit : word1 & ~RouteOverlapBit);
        }

        public bool ShowPlanetNames
        {
            get => (word2 & PlanetNamesBit) != 0;
            set => SetWord2(value ? word2 | PlanetNamesBit : word2 & ~PlanetNamesBit);
        }

        public bool ShowShipCountBadges
        {
            get => (word2 & BadgeBit) != 0;
            set => SetWord2(value ? word2 | BadgeBit : word2 & ~BadgeBit);
        }

        /// <summary>
        /// The scanner display percentage (2-100, default 100). Changing it always force-enables the
        /// scan-circle overlay (client-interface.md, "Scan-range overlay").
        /// </summary>
        public int ScannerPercentage
        {
            get => scannerPercentage;
            set
            {
                int clamped = Math.Max(MinScannerPercentage, Math.Min(MaxScannerPercentage, value));
                bool changed = clamped != scannerPercentage;
                scannerPercentage = clamped;
                if ((word1 & ScanCirclesBit) == 0)
                {
                    word1 |= ScanCirclesBit;
                    changed = true;
                }

                if (changed)
                {
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Reads one addressable slot: 1 when the slot's bit is set (or, for 0-5, when that mode is
        /// current), 0 otherwise or for a slot this client does not address.
        /// </summary>
        public int GetSlot(int slot)
        {
            if (slot >= 0 && slot < ModeCount)
            {
                return Mode == slot ? 1 : 0;
            }

            (bool first, int bit) = SlotBit(slot);
            if (bit == 0)
            {
                return 0;
            }

            return ((first ? word1 : word2) & bit) != 0 ? 1 : 0;
        }

        /// <summary>
        /// Writes one slot: for 0-5 a non-zero value selects that mode (zero does nothing - the six
        /// modes are mutually exclusive), for a bit slot zero clears and non-zero sets the bit.
        /// </summary>
        public void SetSlot(int slot, int value)
        {
            if (slot >= 0 && slot < ModeCount)
            {
                if (value != 0)
                {
                    Mode = slot;
                }

                return;
            }

            (bool first, int bit) = SlotBit(slot);
            if (bit == 0)
            {
                return;
            }

            if (first)
            {
                SetWord1(value != 0 ? word1 | bit : word1 & ~bit);
            }
            else
            {
                SetWord2(value != 0 ? word2 | bit : word2 & ~bit);
            }
        }

        /// <summary>Flips a bit slot (or selects a mode slot).</summary>
        public void ToggleSlot(int slot)
        {
            SetSlot(slot, slot < ModeCount ? 1 : 1 - GetSlot(slot));
        }

        /// <summary>
        /// The slot a top-row digit key drives: 1-6 = slots 0-5, 7/8/9 = slots 7/8/9 (slot 6 is
        /// skipped), 0 = slot 11, Shift+0 = slot 17. Null for anything else (Shift with 1-9 is not
        /// bound by the spec).
        /// </summary>
        public static int? SlotForDigitKey(int digit, bool shift)
        {
            if (digit == 0)
            {
                return shift ? 17 : 11;
            }

            if (shift || digit < 1 || digit > 9)
            {
                return null;
            }

            return digit <= 6 ? digit - 1 : digit;
        }

        /// <summary>Applies a digit key press; false when the key is not bound.</summary>
        public bool ApplyDigitKey(int digit, bool shift)
        {
            int? slot = SlotForDigitKey(digit, shift);
            if (slot == null)
            {
                return false;
            }

            ToggleSlot(slot.Value);
            return true;
        }

        private static (bool First, int Bit) SlotBit(int slot)
        {
            switch (slot)
            {
                case 6: return (true, ShiftLatchBit);
                case 7: return (true, ScanCirclesBit);
                case 8: return (true, MinefieldsBit);
                case 9: return (true, RouteOverlapBit);
                case 10: return (false, BadgeExcludeUndetectedBit);
                case 11: return (false, PlanetNamesBit);
                case 12: return (false, BadgeExcludeForeignBit);
                case 14: return (false, BadgeForeignRaceMaskBit);
                case 17: return (false, BadgeBit);
                default: return (true, 0);
            }
        }

        private void SetWord1(int value)
        {
            value &= 0xFF;
            if (value != word1)
            {
                word1 = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void SetWord2(int value)
        {
            value &= 0xFF;
            if (value != word2)
            {
                word2 = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
