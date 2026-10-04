#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
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

namespace Nova.Common.Components
{
    using System.Collections.Generic;

    /// <summary>
    /// The fixed roster of 12 components behavior-specs-7/ship-design-and-components.md §14a
    /// confirms are gated by a one-time, per-race, per-component random grant (won by fighting
    /// and surviving battles - see BattleEngine.GrantOneTimeSpecialComponent) rather than by any
    /// PRT/LRT trait, tracked in EmpireData.GrantedSpecialComponents. Order matches the spec's own
    /// bit table (0-11) purely for documentation/traceability - nothing in this codebase encodes
    /// them as literal bits, since a HashSet&lt;string&gt; of names gives the same "never grant
    /// the same reward twice" guarantee without needing to hardcode bit positions.
    /// </summary>
    public static class SpecialComponentGrants
    {
        public static readonly IReadOnlyList<string> Components = new[]
        {
            "Multi Cargo Pod",
            "Multi Function Pod",
            "Langston Shell",
            "Mega Poly Shell",
            "Alien Miner",
            "Hush-a-Boom",
            "Anti Matter Torpedo",
            "Multi Contained Munition",
            "Mini Morph",
            "Enigma Pulsar",
            "Genesis Device",
            "Jump Gate",
        };

        /// <summary>
        /// The parts battle salvage can feed: bits 0-7, 9 and 11. Bits 8 (Mini Morph) and 10
        /// (Genesis Device) are never fed by salvage and can only be reached through the Mystery
        /// Trader (behavior-specs-10/turn-generation-engine.md §5, ship-design-and-components.md §14a).
        /// </summary>
        public static readonly IReadOnlyList<string> SalvageableComponents = new[]
        {
            "Multi Cargo Pod",
            "Multi Function Pod",
            "Langston Shell",
            "Mega Poly Shell",
            "Alien Miner",
            "Hush-a-Boom",
            "Anti Matter Torpedo",
            "Multi Contained Munition",
            "Enigma Pulsar",
            "Jump Gate",
        };

        /// <summary>
        /// Gift-mask bit 12, the Mystery Trader's "auxiliary ships" item (value 0x1000,
        /// ship-design-and-components.md §14a "Closed (raw-byte pass)"). Not a component: kept in
        /// EmpireData.GrantedSpecialComponents under this name only when bit 12 is set (in the
        /// original only by the computer players' planet trade; the human ships gift never sets it).
        /// </summary>
        public const string TraderShipsItem = "Mystery Trader Ships";

        /// <summary>The gift mask's whole vocabulary: bits 0-12.</summary>
        public const int GiftBitCount = 13;

        /// <summary>The name EmpireData.GrantedSpecialComponents uses for gift-mask bit 0-12.</summary>
        public static string GiftBitName(int bit)
        {
            return bit < Components.Count ? Components[bit] : TraderShipsItem;
        }

        private static readonly HashSet<string> ComponentSet = new HashSet<string>(Components);

        /// <summary>Whether this component name is one of the 12 one-time-grant specials.</summary>
        public static bool IsSpecialGrant(string componentName)
        {
            return ComponentSet.Contains(componentName);
        }
    }
}
