#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Server
{
    using System;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// Class to identify weapon capability and their targets which is sortable by
    /// weapon system initiative.
    /// </summary>
    public class WeaponDetails : IComparable
    {
        public Stack TargetStack;
        public Stack SourceStack;
        public Weapon Weapon;

        /// <summary>
        /// Total initiative for this weapon slot: hull base initiative + computer bonus (both
        /// carried by the firing ship design) + the weapon's own initiative. See
        /// docs/behavior-specs/combat-resolution.md §5.
        /// </summary>
        public int TotalInitiative
        {
            get { return SourceStack.Token.Design.Initiative + Weapon.Initiative; }
        }

        /// <summary>
        /// Slots are ordered by total initiative, highest first. Slots tied on total initiative
        /// fire the shorter-ranged weapon first. A true remaining tie is meant to be broken by a
        /// coin flip that then stays fixed for the rest of the battle (docs/behavior-specs/
        /// combat-resolution.md §5) — that per-pair persistence isn't tracked here, so a
        /// still-tied comparison is left at 0 (List.Sort is not guaranteed stable, so this
        /// residual case is effectively an unpersisted random order rather than a fixed one).
        /// </summary>
        public int CompareTo(object rightHandSide)
        {
            WeaponDetails rhs = (WeaponDetails)rightHandSide;

            int initiativeComparison = rhs.TotalInitiative.CompareTo(this.TotalInitiative);
            if (initiativeComparison != 0)
            {
                return initiativeComparison;
            }

            return this.Weapon.Range.CompareTo(rhs.Weapon.Range);
        }
    }
}
