#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    /// <summary>
    /// Covers ProductionCompletionEstimator against docs/behavior-specs-5/production-queue.md
    /// §8's confirmed (start, finish) color scheme, using the doc's own worked Example 1 numbers
    /// (§ "Worked Examples") as a direct cross-check of the simulator's turn-1 math.
    /// </summary>
    [TestFixture]
    public class ProductionCompletionEstimatorTest
    {
        private static Race MakeDefaultRace()
        {
            var race = new Race();
            race.Traits.SetPrimary("SS"); // avoids AR's alternate resource-rate branch
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10; // 10 factories produce 10 resources (1:1)
            race.OperableFactories = 10; // 10 per 10,000 colonists
            race.FactoryBuildCost = 10;
            race.MineBuildCost = 5;
            race.MineProductionRate = 10; // 10 mines produce 10 kT (1:1) at 100% concentration
            race.OperableMines = 10;
            race.GrowthRate = 0; // keep population fixed - not under test here
            return race;
        }

        [Test]
        public void Estimate_Example1Turn1_FullyAffordableAutoBuildFactories_IsGreen()
        {
            // docs/behavior-specs-5/production-queue.md's own Example 1, Turn 1: 100,000
            // population, no factories yet, 500 kT Germanium on hand, "Factories (Auto Build) Up
            // to 10" - the doc's own math: 100 population resources exactly covers 10 factories
            // (100 resources), Germanium (40 kT needed) is nowhere near the 500 kT on hand, so
            // all 10 complete in a single turn.
            Race race = MakeDefaultRace();
            Star star = new Star { Colonists = 100000, ThisRace = race };
            star.ResourcesOnHand.Germanium = 500;

            var order = new ProductionOrder(10, new FactoryProductionUnit(race), isAutoBuild: true);
            star.ManufacturingQueue.Queue.Add(order);

            ProductionCompletionEstimate estimate = ProductionCompletionEstimator.Estimate(star, 0, race, researchBudget: 0);

            Assert.That(estimate.Color, Is.EqualTo(ProductionQueueColor.Green));
            Assert.That(estimate.YearsToFinish, Is.EqualTo(1));
        }

        [Test]
        public void Estimate_ManualOrderNeedingTwoTurns_IsBlue()
        {
            // A small population generates too few resources to finish even one factory in a
            // single turn (10 resources needed, population here yields far less), so it should
            // take (at least) two turns: some progress this turn, completing on a later one.
            Race race = MakeDefaultRace();
            Star star = new Star { Colonists = 4000, ThisRace = race }; // 4 resources/turn
            star.ResourcesOnHand.Germanium = 100;

            var order = new ProductionOrder(1, new FactoryProductionUnit(race), isAutoBuild: false);
            star.ManufacturingQueue.Queue.Add(order);

            ProductionCompletionEstimate estimate = ProductionCompletionEstimator.Estimate(star, 0, race, researchBudget: 0);

            Assert.That(estimate.Color, Is.EqualTo(ProductionQueueColor.Blue));
            Assert.That(estimate.YearsToFinish, Is.GreaterThan(1));
        }

        [Test]
        public void Estimate_ManualOrderPermanentlyBlockedOnAMineralThatWillNeverArrive_IsRed()
        {
            // Zero concentration of every mineral and zero mines means Germanium can never be
            // mined - a manual (non-auto-build) Factory order blocks forever, matching
            // docs/behavior-specs-5/production-queue.md §8's "practically never" case.
            Race race = MakeDefaultRace();
            Star star = new Star { Colonists = 100000, ThisRace = race };
            // No Germanium on hand and no concentration to mine it from.

            var order = new ProductionOrder(1, new FactoryProductionUnit(race), isAutoBuild: false);
            star.ManufacturingQueue.Queue.Add(order);

            ProductionCompletionEstimate estimate = ProductionCompletionEstimator.Estimate(star, 0, race, researchBudget: 0);

            Assert.That(estimate.Color, Is.EqualTo(ProductionQueueColor.Red));
            Assert.That(estimate.YearsToFinish, Is.EqualTo(100));
        }

        [Test]
        public void Estimate_LargeManualBatch_IsNotGreenJustBecauseTheFirstFewUnitsFinishInOneTurn()
        {
            // A real, live-reproduced bug: a 500x Factory order where a couple of units can
            // easily complete in year 1 (Quantity ticks down, done > 0) was wrongly read as
            // "finished" - the whole batch's remaining ~498 units still need many more years.
            Race race = MakeDefaultRace();
            Star star = new Star { Colonists = 100000, ThisRace = race }; // 100 resources/turn
            star.ResourcesOnHand.Germanium = 100000; // germanium is not the bottleneck here

            var order = new ProductionOrder(500, new FactoryProductionUnit(race), isAutoBuild: false);
            star.ManufacturingQueue.Queue.Add(order);

            ProductionCompletionEstimate estimate = ProductionCompletionEstimator.Estimate(star, 0, race, researchBudget: 0);

            Assert.That(estimate.Color, Is.Not.EqualTo(ProductionQueueColor.Green));
            Assert.That(estimate.YearsToFinish, Is.GreaterThan(1));
        }

        [Test]
        public void Estimate_AutoBuildAlreadyAtItsTarget_IsGrayWithNothingToBuild()
        {
            // "Factories (Auto Build) Up to 10" with 10 already built has nothing left to do at
            // all right now - distinct from an ordinary item that simply hasn't started yet.
            Race race = MakeDefaultRace();
            Star star = new Star { Colonists = 100000, ThisRace = race, Factories = 10 };

            var order = new ProductionOrder(10, new FactoryProductionUnit(race), isAutoBuild: true);
            star.ManufacturingQueue.Queue.Add(order);

            ProductionCompletionEstimate estimate = ProductionCompletionEstimator.Estimate(star, 0, race, researchBudget: 0);

            Assert.That(estimate.Color, Is.EqualTo(ProductionQueueColor.Gray));
            Assert.That(estimate.YearsToFinish, Is.EqualTo(0));
            Assert.That(estimate.PercentComplete, Is.EqualTo(100.0));
        }
    }
}
