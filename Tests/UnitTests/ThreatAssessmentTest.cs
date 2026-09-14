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
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    using NUnit.Framework;

    /// <summary>
    /// Unit tests for the AI's threat rating and defense-need evaluator
    /// (docs/behavior-specs-3/ai-opponent-behavior.md section 4) - see ThreatAssessment's own
    /// class comment for the two spec inputs it deliberately approximates (the unnamed
    /// per-race trait value) rather than guesses at.
    /// </summary>
    [TestFixture]
    public class ThreatAssessmentTest
    {
        private const ushort OwnerId = 1;
        private const ushort EnemyId = 2;

        private static FleetIntel MakeFleetReport(ushort owner, NovaPoint position, int shipCount)
        {
            FleetIntel report = new FleetIntel();
            report.Owner = owner;
            report.Position = position;
            report.Composition = new Dictionary<long, ShipToken>();

            if (shipCount > 0)
            {
                ShipToken token = new ShipToken(new ShipDesign(1), shipCount);
                report.Composition.Add(1, token);
            }

            return report;
        }

        [Test]
        public void BaseThreatRating_IsAlwaysWithinTheSpecsStatedRange()
        {
            Random random = new Random(1);
            for (int trial = 0; trial < 100; trial++)
            {
                int rating = ThreatAssessment.BaseThreatRating(random);
                Assert.GreaterOrEqual(rating, ThreatAssessment.MinBaseThreatRating);
                Assert.LessOrEqual(rating, ThreatAssessment.MaxBaseThreatRating);
            }
        }

        [Test]
        public void AggregateThreatRating_EqualsBaseRating_WhenNoFleetsAreNearby()
        {
            int baseRating = ThreatAssessment.BaseThreatRating(new Random(42));
            int aggregate = ThreatAssessment.AggregateThreatRating(
                new NovaPoint(0, 0), new List<FleetIntel>(), OwnerId, scanRadius: 200, random: new Random(42));

            Assert.AreEqual(baseRating, aggregate);
        }

        [Test]
        public void AggregateThreatRating_IgnoresOwnFleets()
        {
            var reports = new List<FleetIntel> { MakeFleetReport(OwnerId, new NovaPoint(1, 1), 20) };

            int baseRating = ThreatAssessment.BaseThreatRating(new Random(7));
            int aggregate = ThreatAssessment.AggregateThreatRating(
                new NovaPoint(0, 0), reports, OwnerId, scanRadius: 200, random: new Random(7));

            Assert.AreEqual(baseRating, aggregate);
        }

        [Test]
        public void AggregateThreatRating_IgnoresUnownedFleets()
        {
            var reports = new List<FleetIntel> { MakeFleetReport((ushort)Global.Nobody, new NovaPoint(1, 1), 20) };

            int baseRating = ThreatAssessment.BaseThreatRating(new Random(9));
            int aggregate = ThreatAssessment.AggregateThreatRating(
                new NovaPoint(0, 0), reports, OwnerId, scanRadius: 200, random: new Random(9));

            Assert.AreEqual(baseRating, aggregate);
        }

        [Test]
        public void AggregateThreatRating_IgnoresFleetsBeyondTheScanRadius()
        {
            var reports = new List<FleetIntel> { MakeFleetReport(EnemyId, new NovaPoint(500, 500), 50) };

            int baseRating = ThreatAssessment.BaseThreatRating(new Random(3));
            int aggregate = ThreatAssessment.AggregateThreatRating(
                new NovaPoint(0, 0), reports, OwnerId, scanRadius: 50, random: new Random(3));

            Assert.AreEqual(baseRating, aggregate);
        }

        [Test]
        public void AggregateThreatRating_RisesWithANearbyEnemyFleet()
        {
            var reports = new List<FleetIntel> { MakeFleetReport(EnemyId, new NovaPoint(10, 10), 50) };

            int withoutEnemy = ThreatAssessment.AggregateThreatRating(
                new NovaPoint(0, 0), new List<FleetIntel>(), OwnerId, scanRadius: 200, random: new Random(5));
            int withEnemy = ThreatAssessment.AggregateThreatRating(
                new NovaPoint(0, 0), reports, OwnerId, scanRadius: 200, random: new Random(5));

            Assert.Greater(withEnemy, withoutEnemy);
        }

        [Test]
        public void NeedsDefensiveMinelaying_RecommendsBuilding_WhenThreatAndMinefieldsAreBothWithinThreshold()
        {
            // threatThreshold = (1*20)+10 = 30 with the placeholder trait value.
            Assert.IsTrue(ThreatAssessment.NeedsDefensiveMinelaying(threatRating: 10, ownedPlanetCount: 5, existingMinefieldUnits: 0, random: new Random(1)));
        }

        [Test]
        public void NeedsDefensiveMinelaying_RefusesBuilding_WhenThreatExceedsThreshold()
        {
            Assert.IsFalse(ThreatAssessment.NeedsDefensiveMinelaying(threatRating: 999, ownedPlanetCount: 5, existingMinefieldUnits: 0, random: new Random(1)));
        }

        [Test]
        public void NeedsDefensiveMinelaying_RefusesBuilding_WhenMinefieldCapIsAlreadyExceeded()
        {
            // minefieldCap = (5*4)/5 = 4.
            Assert.IsFalse(ThreatAssessment.NeedsDefensiveMinelaying(threatRating: 10, ownedPlanetCount: 5, existingMinefieldUnits: 999, random: new Random(1)));
        }

        [Test]
        public void NeedsDefensiveMinelaying_BelowThreatLevelFive_ProducesBothOutcomesAcrossManySeeds()
        {
            bool sawTrue = false;
            bool sawFalse = false;

            for (int seed = 0; seed < 50 && !(sawTrue && sawFalse); seed++)
            {
                bool result = ThreatAssessment.NeedsDefensiveMinelaying(threatRating: 3, ownedPlanetCount: 5, existingMinefieldUnits: 0, random: new Random(seed));
                sawTrue |= result;
                sawFalse |= !result;
            }

            Assert.IsTrue(sawTrue, "Expected at least one seed to flip the coin to build anyway.");
            Assert.IsTrue(sawFalse, "Expected at least one seed to flip the coin to not build.");
        }
    }
}
