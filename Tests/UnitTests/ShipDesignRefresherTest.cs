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
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;

    using NUnit.Framework;

    /// <summary>
    /// Unit tests for the AI's ship-design age tracking and best-available-engine selection
    /// (docs/behavior-specs-3/ai-opponent-behavior.md section 7, scoped to transport designs -
    /// see ShipDesignRefresher's own class comment for what was deliberately left out).
    /// </summary>
    [TestFixture]
    public class ShipDesignRefresherTest
    {
        [Test]
        public void NameWithTurnSuffix_EncodesTurnAndGetCreationTurn_RecoversIt()
        {
            string name = ShipDesignRefresher.NameWithTurnSuffix("AI Transport", 42);

            Assert.AreEqual("AI Transport T42", name);

            ShipDesign design = new ShipDesign(1);
            design.Name = name;

            Assert.AreEqual(42, ShipDesignRefresher.GetCreationTurn(design));
        }

        [Test]
        public void GetCreationTurn_ReturnsNull_ForANameWithNoTurnSuffix()
        {
            ShipDesign design = new ShipDesign(1);
            design.Name = "Santa Maria";

            Assert.IsNull(ShipDesignRefresher.GetCreationTurn(design));
        }

        [Test]
        public void IsDueForRefresh_IsFalse_ForANameWithNoTurnSuffix()
        {
            ShipDesign design = new ShipDesign(1);
            design.Name = "Scout";

            Assert.IsFalse(ShipDesignRefresher.IsDueForRefresh(design, currentTurn: 9999));
        }

        [Test]
        public void IsDueForRefresh_IsFalse_BeforeTheModerateAgeThreshold()
        {
            ShipDesign design = new ShipDesign(1);
            design.Name = ShipDesignRefresher.NameWithTurnSuffix("AI Transport", 10);

            Assert.IsFalse(ShipDesignRefresher.IsDueForRefresh(design, currentTurn: 10 + ShipDesignRefresher.ModerateAgeThreshold - 1));
        }

        [Test]
        public void IsDueForRefresh_IsTrue_AtOrPastTheModerateAgeThreshold()
        {
            ShipDesign design = new ShipDesign(1);
            design.Name = ShipDesignRefresher.NameWithTurnSuffix("AI Transport", 10);

            Assert.IsTrue(ShipDesignRefresher.IsDueForRefresh(design, currentTurn: 10 + ShipDesignRefresher.ModerateAgeThreshold));
        }

        [Test]
        public void BestAvailableEngine_PicksTheHighestTechLevelEngine_AndIgnoresNonEngineComponents()
        {
            ClientData clientState = new ClientData();
            clientState.EmpireState.Id = 1;

            Component weakEngine = new Component();
            weakEngine.Name = "Weak Engine";
            weakEngine.RequiredTech = new TechLevel(0, 0, 0, 1, 0, 0);
            weakEngine.Properties.Add("Engine", new Engine());

            Component strongEngine = new Component();
            strongEngine.Name = "Strong Engine";
            strongEngine.RequiredTech = new TechLevel(0, 0, 0, 7, 0, 0);
            strongEngine.Properties.Add("Engine", new Engine());

            Component hull = new Component();
            hull.Name = "Large Freighter";
            hull.RequiredTech = new TechLevel(0, 0, 0, 0, 0, 20);

            clientState.EmpireState.AvailableComponents.Add(weakEngine);
            clientState.EmpireState.AvailableComponents.Add(strongEngine);
            clientState.EmpireState.AvailableComponents.Add(hull);

            Component best = ShipDesignRefresher.BestAvailableEngine(clientState);

            Assert.IsNotNull(best);
            Assert.AreEqual("Strong Engine", best.Name);
        }

        [Test]
        public void BestAvailableEngine_ReturnsNull_WhenNoEngineIsAvailable()
        {
            ClientData clientState = new ClientData();
            clientState.EmpireState.Id = 1;

            Component hull = new Component();
            hull.Name = "Large Freighter";
            clientState.EmpireState.AvailableComponents.Add(hull);

            Assert.IsNull(ShipDesignRefresher.BestAvailableEngine(clientState));
        }
    }
}
