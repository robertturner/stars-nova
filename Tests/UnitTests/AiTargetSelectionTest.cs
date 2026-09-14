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
    using System;
    using System.Collections.Generic;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.DataStructures;

    using NUnit.Framework;

    /// <summary>
    /// Unit tests for the AI's colonization-target and freighter-routing selectors
    /// (docs/behavior-specs-3/ai-opponent-behavior.md sections 2 and 5). These selectors are
    /// deliberately probabilistic (see their own class comments), so these tests only pin down
    /// their *deterministic* guarantees - never targeting an owned/already-claimed object, and
    /// falling back sensibly rather than throwing when nothing scores well - not exact RNG
    /// outcomes, which would make the tests fragile against reasonable future tuning.
    /// </summary>
    [TestFixture]
    public class AiTargetSelectionTest
    {
        private ClientData clientState;

        [SetUp]
        public void SetUp()
        {
            clientState = new ClientData();
            clientState.EmpireState.Id = 1;
            clientState.EmpireState.Race = new Race();
        }

        /// <summary>
        /// Adds a StarIntel report. Gravity/Temperature/Radiation are left at their default
        /// (0) - well outside a default-constructed Race's [15,85] tolerance range - so every
        /// report added this way is uninhabitable and only reachable via the exploratory
        /// fallback, not the scored-candidate path; that's deliberate for these tests, which
        /// only need to check where a target is (never-owned, correctly-excluded) rather than
        /// exercise the habitability-scoring formula itself.
        /// </summary>
        private StarIntel AddStarReport(string name, NovaPoint position, ushort owner)
        {
            StarIntel report = new StarIntel();
            report.Name = name;
            report.Position = position;
            report.Owner = owner;
            clientState.EmpireState.StarReports.Add(name, report);
            return report;
        }

        private Star AddOwnedStar(string name, NovaPoint position, int ironium, int boranium, int germanium)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = clientState.EmpireState.Id;
            star.Position = position;
            star.ResourcesOnHand = new Resources(ironium, boranium, germanium, 0);
            clientState.EmpireState.OwnedStars.Add(star);
            return star;
        }

        [Test]
        public void ColonizationTargetSelector_NeverReturnsAnOwnedStar()
        {
            AddStarReport("Home", new NovaPoint(0, 0), clientState.EmpireState.Id);
            AddStarReport("Elsewhere", new NovaPoint(10, 10), Global.Nobody);

            var selector = new ColonizationTargetSelector(clientState, new Random(42));
            Fleet fleet = new Fleet("Colonizer", clientState.EmpireState.Id, 1, new NovaPoint(0, 0));

            for (int trial = 0; trial < 25; trial++)
            {
                StarIntel target = selector.SelectTarget(fleet, currentTurn: 1);
                if (target != null)
                {
                    Assert.AreEqual("Elsewhere", target.Name);
                }
            }
        }

        [Test]
        public void ColonizationTargetSelector_ReturnsNull_WhenNothingIsUnowned()
        {
            AddStarReport("Home", new NovaPoint(0, 0), clientState.EmpireState.Id);

            var selector = new ColonizationTargetSelector(clientState, new Random(1));
            Fleet fleet = new Fleet("Colonizer", clientState.EmpireState.Id, 1, new NovaPoint(0, 0));

            Assert.IsNull(selector.SelectTarget(fleet, currentTurn: 1));
        }

        [Test]
        public void ColonizationTargetSelector_FallsBackToExploratoryTarget_WhenNoCandidateScoresWell()
        {
            // Radiation/gravity/temperature all left at their StarIntel default (0), maximally
            // far from this race's tolerances (all centered at 0.5) - HabitalValue should be
            // deeply negative/zero, so this star never enters the scored-candidate list at all,
            // and the exploratory (reservoir-sampled) fallback is the only way it can be picked.
            AddStarReport("Uninhabitable", new NovaPoint(500, 500), Global.Nobody);

            var selector = new ColonizationTargetSelector(clientState, new Random(7));
            Fleet fleet = new Fleet("Colonizer", clientState.EmpireState.Id, 1, new NovaPoint(0, 0));

            StarIntel target = selector.SelectTarget(fleet, currentTurn: 1);

            Assert.IsNotNull(target);
            Assert.AreEqual("Uninhabitable", target.Name);
        }

        [Test]
        public void FreighterRoutingSelector_PicksNearestShortfallPlanetAndASurplusSource()
        {
            AddOwnedStar("Surplus", new NovaPoint(0, 0), 2000, 2000, 2000);
            Star shortfall = AddOwnedStar("Shortfall", new NovaPoint(5, 0), 100, 100, 100);

            var selector = new FreighterRoutingSelector(clientState, new Random(1));
            Fleet fleet = new Fleet("Transport", clientState.EmpireState.Id, 1, new NovaPoint(0, 0));

            FreighterRun run = selector.SelectRun(fleet, new HashSet<string>());

            Assert.IsNotNull(run);
            Assert.AreEqual(shortfall.Name, run.Target.Name);
            Assert.AreEqual("Surplus", run.Source.Name);
        }

        [Test]
        public void FreighterRoutingSelector_ReturnsNull_WhenNoShortfallExists()
        {
            AddOwnedStar("Healthy", new NovaPoint(0, 0), 2000, 2000, 2000);

            var selector = new FreighterRoutingSelector(clientState, new Random(1));
            Fleet fleet = new Fleet("Transport", clientState.EmpireState.Id, 1, new NovaPoint(0, 0));

            Assert.IsNull(selector.SelectRun(fleet, new HashSet<string>()));
        }

        [Test]
        public void FreighterRoutingSelector_ReturnsNull_WhenShortfallExistsButNothingCanSpare()
        {
            AddOwnedStar("AlsoShortfall", new NovaPoint(5, 0), 100, 100, 100);
            AddOwnedStar("Shortfall", new NovaPoint(0, 0), 50, 50, 50);

            var selector = new FreighterRoutingSelector(clientState, new Random(1));
            Fleet fleet = new Fleet("Transport", clientState.EmpireState.Id, 1, new NovaPoint(0, 0));

            Assert.IsNull(selector.SelectRun(fleet, new HashSet<string>()));
        }

        [Test]
        public void FreighterRoutingSelector_SkipsAlreadyClaimedTargets()
        {
            AddOwnedStar("Surplus", new NovaPoint(0, 0), 2000, 2000, 2000);
            AddOwnedStar("Shortfall", new NovaPoint(5, 0), 100, 100, 100);

            var selector = new FreighterRoutingSelector(clientState, new Random(1));
            Fleet fleet = new Fleet("Transport", clientState.EmpireState.Id, 1, new NovaPoint(0, 0));

            Assert.IsNull(selector.SelectRun(fleet, new HashSet<string> { "Shortfall" }));
        }
    }
}
