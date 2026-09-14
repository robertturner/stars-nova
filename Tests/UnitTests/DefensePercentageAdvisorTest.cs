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
    /// Unit tests for the AI's defense-percentage-target advisor
    /// (docs/behavior-specs-3/ai-opponent-behavior.md section 6) - see DefensePercentageAdvisor's
    /// own class comment for why this uses current shortfall rather than genuine turn-tracking.
    /// </summary>
    [TestFixture]
    public class DefensePercentageAdvisorTest
    {
        [Test]
        public void ActChancePercent_IsFull_WhenNoDefensesExistYet()
        {
            Assert.AreEqual(100, DefensePercentageAdvisor.ActChancePercent(currentDefenses: 0, maxDefenses: 100));
        }

        [Test]
        public void ActChancePercent_IsFloored_OnceAtTarget()
        {
            Assert.AreEqual(DefensePercentageAdvisor.MinActChancePercent, DefensePercentageAdvisor.ActChancePercent(currentDefenses: 100, maxDefenses: 100));
        }

        [Test]
        public void ActChancePercent_IsFloored_OncePastTarget()
        {
            Assert.AreEqual(DefensePercentageAdvisor.MinActChancePercent, DefensePercentageAdvisor.ActChancePercent(currentDefenses: 150, maxDefenses: 100));
        }

        [Test]
        public void ActChancePercent_IsNeverBelowTheFloor_ForATinyShortfall()
        {
            Assert.AreEqual(DefensePercentageAdvisor.MinActChancePercent, DefensePercentageAdvisor.ActChancePercent(currentDefenses: 99, maxDefenses: 100));
        }

        [Test]
        public void ActChancePercent_DecreasesAsTheShortfallCloses()
        {
            int farFromTarget = DefensePercentageAdvisor.ActChancePercent(currentDefenses: 10, maxDefenses: 100);
            int closeToTarget = DefensePercentageAdvisor.ActChancePercent(currentDefenses: 90, maxDefenses: 100);

            Assert.Greater(farFromTarget, closeToTarget);
        }
    }
}
