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

    /// <summary>
    /// Ports the combat-readiness check from docs/behavior-specs-3/ai-opponent-behavior.md
    /// section 3 ("Starbase management"): "A combat-readiness check gates whether a fleet uses an
    /// aggressive or a cautious attack-order profile: it requires the fleet be farther than
    /// roughly 14 units from its target, hold more than roughly 499 cargo/fuel, and - if every one
    /// of the fleet's three weapon-slot counts individually exceeds 15 - escalates to the more
    /// aggressive profile."
    ///
    /// Read as three tiers rather than a single boolean: below the distance/cargo gate, a
    /// <see cref="AttackProfile.Cautious"/> profile; past the gate but without every weapon slot
    /// individually exceeding 15, a standard <see cref="AttackProfile.Aggressive"/> profile; past
    /// the gate *and* every weapon slot exceeding 15, the escalated
    /// <see cref="AttackProfile.HighlyAggressive"/> profile the spec calls out specifically.
    ///
    /// **Not wired to an actual fleet action.** This codebase's AI has no invasion/attack-fleet
    /// targeting mechanic at all yet (only colonization, scouting, and mineral/cargo transport -
    /// see mechanics 2, 8, 5) - "the fleet's... target" this check depends on doesn't exist as a
    /// concept the AI currently computes. Implemented here as its own pure, tested unit (like
    /// ShipDesignRefresher/ThreatAssessment elsewhere in this rebuild) so the spec's one fully
    /// concrete, numbered sub-mechanic in section 3 is captured and ready to wire in once this
    /// codebase's AI gains real attack-fleet decisions - building that targeting mechanic from
    /// scratch here would be a disproportionately large, separate undertaking.
    ///
    /// Also, "three weapon-slot counts" describes the original game's fixed three-weapon-slot
    /// ship structure; this codebase's <c>ShipDesign.Weapons</c> is a variable-length list instead
    /// (see ShipDesign.cs), so "every... individually exceeds 15" is read here as "every mounted
    /// weapon type's count exceeds 15", generalizing the same rule to any number of weapon types
    /// rather than assuming exactly three.
    /// </summary>
    public static class CombatReadinessAdvisor
    {
        public const double MinDistanceFromTarget = 14;
        public const double MinCargoOrFuel = 499;
        public const int WeaponCountEscalationThreshold = 15;

        public enum AttackProfile
        {
            Cautious,
            Aggressive,
            HighlyAggressive
        }

        public static AttackProfile DetermineProfile(double distanceToTarget, double cargoOrFuelAmount, IEnumerable<int> weaponSlotCounts)
        {
            if (distanceToTarget <= MinDistanceFromTarget || cargoOrFuelAmount <= MinCargoOrFuel)
            {
                return AttackProfile.Cautious;
            }

            bool everyWeaponSlotEscalates = weaponSlotCounts != null
                && weaponSlotCounts.Any()
                && weaponSlotCounts.All(count => count > WeaponCountEscalationThreshold);

            return everyWeaponSlotEscalates ? AttackProfile.HighlyAggressive : AttackProfile.Aggressive;
        }
    }
}
