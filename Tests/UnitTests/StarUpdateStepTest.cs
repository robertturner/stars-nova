#region Copyright Notice
// ============================================================================
// Copyright (C) 2010 stars-nova
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

using System.Reflection;
using NUnit.Framework;

using Nova.Common;
using Nova.Server.TurnSteps;

namespace Nova.Tests.UnitTests
{
    /// <Summary>
    /// Regression coverage for the "field already at max tech level" crash: ApplyLevelUps
    /// used to call Research.Cost(..., ResearchLevels[area] + 1) unconditionally, which
    /// indexes an array sized for levels 1..TechLevel.MaxLevel and throws
    /// IndexOutOfRangeException once a field reaches MaxLevel while the empire still has
    /// banked research resources for it (e.g. from a big single-turn contribution, or
    /// Generalized Research still feeding a maxed field its 15% share every turn).
    /// </Summary>
    [TestFixture]
    public class StarUpdateStepTest
    {
        [Test]
        public void ApplyLevelUps_FieldAlreadyAtMaxLevel_DoesNotThrow()
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Race.ResearchCosts[TechLevel.ResearchField.Energy] = 100;
            empire.ResearchLevels[TechLevel.ResearchField.Energy] = TechLevel.MaxLevel;
            empire.ResearchResources[TechLevel.ResearchField.Energy] = 999999;

            StarUpdateStep step = new StarUpdateStep();
            MethodInfo applyLevelUps = typeof(StarUpdateStep).GetMethod(
                "ApplyLevelUps", BindingFlags.NonPublic | BindingFlags.Instance);

            Assert.DoesNotThrow(() =>
                applyLevelUps.Invoke(step, new object[] { TechLevel.ResearchField.Energy, empire }));

            Assert.AreEqual(
                TechLevel.MaxLevel,
                empire.ResearchLevels[TechLevel.ResearchField.Energy],
                "Level should stay capped at MaxLevel.");
            Assert.AreEqual(
                999999,
                empire.ResearchResources[TechLevel.ResearchField.Energy],
                "Banked resources for a maxed field should be left untouched, not spent.");
        }
    }
}
