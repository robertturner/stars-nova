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

namespace Nova.Tests.UnitTests
{
    using Nova.Ai;

    using NUnit.Framework;

    /// <summary>
    /// Unit tests for the AI's combat-readiness attack-profile check
    /// (docs/behavior-specs-3/ai-opponent-behavior.md section 3) - see
    /// CombatReadinessAdvisor's own class comment for why this isn't yet wired to an actual
    /// fleet action.
    /// </summary>
    [TestFixture]
    public class CombatReadinessAdvisorTest
    {
        [Test]
        public void DetermineProfile_IsCautious_WhenTooCloseToTarget()
        {
            var profile = CombatReadinessAdvisor.DetermineProfile(distanceToTarget: 10, cargoOrFuelAmount: 1000, weaponSlotCounts: new[] { 20, 20, 20 });
            Assert.AreEqual(CombatReadinessAdvisor.AttackProfile.Cautious, profile);
        }

        [Test]
        public void DetermineProfile_IsCautious_WhenCargoOrFuelIsTooLow()
        {
            var profile = CombatReadinessAdvisor.DetermineProfile(distanceToTarget: 100, cargoOrFuelAmount: 10, weaponSlotCounts: new[] { 20, 20, 20 });
            Assert.AreEqual(CombatReadinessAdvisor.AttackProfile.Cautious, profile);
        }

        [Test]
        public void DetermineProfile_IsAggressive_WhenGatePassesButNotEveryWeaponSlotEscalates()
        {
            var profile = CombatReadinessAdvisor.DetermineProfile(distanceToTarget: 100, cargoOrFuelAmount: 1000, weaponSlotCounts: new[] { 20, 20, 5 });
            Assert.AreEqual(CombatReadinessAdvisor.AttackProfile.Aggressive, profile);
        }

        [Test]
        public void DetermineProfile_IsAggressive_WhenGatePassesWithNoWeapons()
        {
            var profile = CombatReadinessAdvisor.DetermineProfile(distanceToTarget: 100, cargoOrFuelAmount: 1000, weaponSlotCounts: new int[0]);
            Assert.AreEqual(CombatReadinessAdvisor.AttackProfile.Aggressive, profile);
        }

        [Test]
        public void DetermineProfile_IsHighlyAggressive_WhenGatePassesAndEveryWeaponSlotEscalates()
        {
            var profile = CombatReadinessAdvisor.DetermineProfile(distanceToTarget: 100, cargoOrFuelAmount: 1000, weaponSlotCounts: new[] { 16, 20, 100 });
            Assert.AreEqual(CombatReadinessAdvisor.AttackProfile.HighlyAggressive, profile);
        }

        [Test]
        public void DetermineProfile_IsAggressive_NotHighlyAggressive_AtExactlyTheWeaponThreshold()
        {
            var profile = CombatReadinessAdvisor.DetermineProfile(distanceToTarget: 100, cargoOrFuelAmount: 1000, weaponSlotCounts: new[] { 15, 20, 20 });
            Assert.AreEqual(CombatReadinessAdvisor.AttackProfile.Aggressive, profile);
        }
    }
}
