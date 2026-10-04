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
    /// <summary>
    /// The Packet Physics Mass-Driver range overlay (behavior-specs-11/client-interface.md,
    /// "Mass-Driver range overlay for Packet Physics, confirmed by inspection of the exported
    /// client"): when the race's primary trait is Packet Physics the map draws an extra range
    /// circle around each qualifying Mass-Driver-class starbase component, sharing the scan-range
    /// overlay's own toggle bit (0x20) and "scaled the same way as scan-range circles".
    /// SPEC GAP: the spec gives no numeric radius formula ("radius derived from the driver level").
    /// Stand-in: the driver's rated warp squared, the distance its packets cover in one year
    /// (fleet-movement-scanning-cargo.md section 2, "distance covered per year scales with the
    /// square of the warp factor"). Disclose this as the port's own choice.
    /// </summary>
    public static class MassDriverRangeRules
    {
        /// <summary>The overlay's true-scale radius for a driver of rated warp
        /// <paramref name="driverLevel"/>: level squared (0 with no driver).</summary>
        public static int RangeCircleRadius(int driverLevel)
        {
            return driverLevel <= 0 ? 0 : driverLevel * driverLevel;
        }
    }
}
