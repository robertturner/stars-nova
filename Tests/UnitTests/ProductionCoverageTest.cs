#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Spec-driven coverage of behavior-specs-10/production-queue.md, coverage-table rows 1, 2, 7,
    /// 15, 17 and 36. Every expected figure below is the specification's, not the code's.
    /// </summary>
    internal static class ProductionCoverageKit
    {
        /// <summary>The default (Humanoid) economy of the §4 settings table, no growth.</summary>
        public static Race DefaultRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.OperableFactories = 10;
            race.MineProductionRate = 10;
            race.MineBuildCost = 5;
            race.OperableMines = 10;
            race.GrowthRate = 0;
            return race;
        }

        /// <summary>An owned planet at the race's ideal environment (so its build caps are large).</summary>
        public static Star IdealPlanet(Race race, int colonists)
        {
            Star star = new Star();
            star.Name = "Tierra";
            star.Owner = 1;
            star.ThisRace = race;
            star.Colonists = colonists;
            star.Gravity = race.GravityTolerance.OptimumLevel;
            star.Temperature = race.TemperatureTolerance.OptimumLevel;
            star.Radiation = race.RadiationTolerance.OptimumLevel;
            return star;
        }

        /// <summary>A ship design whose only cost is <paramref name="resources"/> resources.</summary>
        public static ShipDesign ResourceOnlyDesign(long key, int resources)
        {
            Component blueprint = new Component { Name = "Costly Hull", Mass = 10, Cost = new Resources(0, 0, 0, resources) };
            Hull hull = new Hull { Modules = new List<HullModule>(), ArmorStrength = 10 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Name = "Costly " + key, Blueprint = blueprint };
            design.Update();
            return design;
        }

        /// <summary>A one-planet game for StarUpdateStep. Every research field is at the maximum
        /// level, so research resources only accumulate (no level-up spends them).</summary>
        public static ServerData OnePlanetGame(Race race, int researchBudget, out EmpireData empire, out Star star)
        {
            ServerData serverState = new ServerData();
            empire = new EmpireData { Id = 1, ResearchBudget = researchBudget };
            empire.Race = race;
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                empire.ResearchLevels[field] = TechLevel.MaxLevel;
            }

            serverState.AllEmpires.Add(empire.Id, empire);
            star = IdealPlanet(race, 100000);
            serverState.AllStars.Add(star.Key, star);
            empire.OwnedStars.Add(star);
            return serverState;
        }

        public static int TotalResearch(EmpireData empire)
        {
            int sum = 0;
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                sum += empire.ResearchResources[field];
            }

            return sum;
        }
    }

    /// <summary>
    /// Rows 1 and 2: resources from population (§1) and factories (§2), and the factory's build
    /// cost (§2 and §6 "Also confirmed by inspection": 4 kT Germanium, minus 1 with the "Factories
    /// cost 1 kT less" option).
    /// </summary>
    [TestFixture]
    public class PlanetResourceOutputCoverageTest
    {
        /// <summary>§1: population resources = population / colonists-per-resource, rounded down,
        /// across the setting's legal range 700..2,500 (§4 settings table).</summary>
        [TestCase(100000, 1000, 100)]
        [TestCase(25999, 1000, 25)]
        [TestCase(1999, 1000, 1)]
        [TestCase(25999, 700, 37)]
        [TestCase(25999, 2500, 10)]
        [TestCase(4999, 2500, 1)]
        public void PopulationResources_ArePopulationOverTheSetting_RoundedDown(int colonists, int colonistsPerResource, int expected)
        {
            Race race = ProductionCoverageKit.DefaultRace();
            race.ColonistsPerResource = colonistsPerResource;
            Star star = ProductionCoverageKit.IdealPlanet(race, colonists);

            Assert.AreEqual(expected, star.GetResourceRate(), "No factories: only the population term");
        }

        /// <summary>§2: the "resources produced per 10 operating factories" setting (5..15, default
        /// 10, i.e. 1 per factory by default) on top of the population term. 25,000 colonists and 10
        /// factories at the defaults give the 35 resources a year of the §10h live test.</summary>
        [TestCase(10, 35)]
        [TestCase(15, 40)]
        [TestCase(5, 30)]
        public void TenOperatingFactories_AddTheirPerTenSetting(int perTenFactories, int expected)
        {
            Race race = ProductionCoverageKit.DefaultRace();
            race.FactoryProduction = perTenFactories;
            Star star = ProductionCoverageKit.IdealPlanet(race, 25000);
            star.Factories = 10;

            Assert.AreEqual(expected, star.GetResourceRate());
        }

        [Test]
        public void DefaultFactory_Costs10ResourcesAnd4Germanium_AndNoOtherMineral()
        {
            Race race = ProductionCoverageKit.DefaultRace();

            Resources cost = race.GetFactoryResources();

            Assert.AreEqual(10, cost.Energy);
            Assert.AreEqual(4, cost.Germanium);
            Assert.AreEqual(0, cost.Ironium, "Factories use no other mineral");
            Assert.AreEqual(0, cost.Boranium, "Factories use no other mineral");
            Assert.AreEqual(cost, new FactoryProductionUnit(race).Cost, "The queue item is priced the same way");
        }

        [Test]
        public void FactoriesCostOneLess_Takes3Germanium()
        {
            Race race = ProductionCoverageKit.DefaultRace();
            race.Traits.Add("CF");

            Assert.AreEqual(3, race.GetFactoryResources().Germanium);
            Assert.AreEqual(10, race.GetFactoryResources().Energy, "Only the Germanium changes");
        }

        /// <summary>The factory build cost is the race's own setting (5..25 resources).</summary>
        [TestCase(5)]
        [TestCase(25)]
        public void FactoryResourceCost_IsTheRaceSetting(int buildCost)
        {
            Race race = ProductionCoverageKit.DefaultRace();
            race.FactoryBuildCost = buildCost;

            Assert.AreEqual(buildCost, race.GetFactoryResources().Energy);
        }

        /// <summary>A manual Factory order completes on exactly 10 resources and 4 kT Germanium
        /// (3 kT with the option), spending exactly that.</summary>
        [TestCase(false, 4)]
        [TestCase(true, 3)]
        public void ManualFactory_IsBuiltForExactlyItsCost(bool cheapFactories, int germanium)
        {
            Race race = ProductionCoverageKit.DefaultRace();
            if (cheapFactories)
            {
                race.Traits.Add("CF");
            }

            Star star = ProductionCoverageKit.IdealPlanet(race, 100000);
            star.ResourcesOnHand = new Resources(0, 0, germanium, 10);
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new FactoryProductionUnit(race), false));

            star.ManufacturingQueue.ProcessYear(star, null);

            Assert.AreEqual(1, star.Factories);
            Assert.AreEqual(0, star.ResourcesOnHand.Energy);
            Assert.AreEqual(0, star.ResourcesOnHand.Germanium);
        }
    }

    /// <summary>
    /// Row 7: the race-design economic settings table (§4 "Race-design economic settings,
    /// recovered from the binary"): seven settings with their minimum, maximum and Humanoid
    /// default, plus the Germanium checkbox; slot 0 is stored 7..25 and shown x 100; Alternate
    /// Reality cannot change the six factory/mine settings and selecting it resets them.
    /// </summary>
    [TestFixture]
    public class EconomicSettingsTableCoverageTest
    {
        // slot, min, max, default - in stored units (slot 0 x 100 colonists).
        [TestCase(0, 7, 25, 10)]  // colonists per 1 resource 700..2,500, default 1,000
        [TestCase(1, 5, 15, 10)]  // resources per 10 operating factories
        [TestCase(2, 5, 25, 10)]  // resources to build one factory
        [TestCase(3, 5, 25, 10)]  // factories operable per 10,000 colonists
        [TestCase(4, 5, 25, 10)]  // kT of each mineral per 10 operating mines
        [TestCase(5, 2, 15, 5)]   // resources to build one mine (minimum 2)
        [TestCase(6, 5, 25, 10)]  // mines operable per 10,000 colonists
        public void EachSetting_HasTheSpecsRangeAndDefault(int slot, int min, int max, int defaultValue)
        {
            Assert.AreEqual(7, RaceDesignerRules.EconomySlots.Length, "Seven economic settings");
            Assert.AreEqual((min, max, defaultValue), RaceDesignerRules.EconomySlots[slot]);

            Assert.AreEqual(min, RaceDesignerRules.ClampSlot(slot, min - 1), "Below the minimum is raised to it");
            Assert.AreEqual(max, RaceDesignerRules.ClampSlot(slot, max + 1), "Above the maximum is lowered to it");
            Assert.AreEqual(min, RaceDesignerRules.ClampSlot(slot, min));
            Assert.AreEqual(max, RaceDesignerRules.ClampSlot(slot, max));
        }

        [TestCase(7, 700)]
        [TestCase(25, 2500)]
        [TestCase(6, 700)]
        [TestCase(26, 2500)]
        public void ColonistsPerResource_IsStoredSevenTo25_AndUsedTimes100(int stored, int colonists)
        {
            Race race = ProductionCoverageKit.DefaultRace();

            RaceDesignerRules.SetSlot(race, 0, stored);

            Assert.AreEqual(colonists, race.ColonistsPerResource);
            Assert.AreEqual(colonists / 100, RaceDesignerRules.GetSlot(race, 0));
        }

        [Test]
        public void HumanoidPreset_SeedsExactlyTheDefaults_WithTheGermaniumBoxUnchecked()
        {
            Race race = new Race();
            RacePresets.Apply(RacePresets.Humanoid, race);

            Assert.AreEqual(1000, race.ColonistsPerResource);
            Assert.AreEqual(10, race.FactoryProduction);
            Assert.AreEqual(10, race.FactoryBuildCost);
            Assert.AreEqual(10, race.OperableFactories);
            Assert.AreEqual(10, race.MineProductionRate);
            Assert.AreEqual(5, race.MineBuildCost);
            Assert.AreEqual(10, race.OperableMines);
            Assert.AreEqual(4, race.GetFactoryResources().Germanium, "Factory Germanium default 4 kT (box unchecked)");
        }

        [Test]
        public void SelectingAlternateReality_ResetsTheSixFactoryAndMineSettings_AndLocksThem()
        {
            Race race = ProductionCoverageKit.DefaultRace();
            race.ColonistsPerResource = 1500;
            race.FactoryProduction = 15;
            race.FactoryBuildCost = 5;
            race.OperableFactories = 25;
            race.MineProductionRate = 25;
            race.MineBuildCost = 2;
            race.OperableMines = 25;
            race.Traits.Add("CF");

            RaceDesignerRules.ApplyPrimaryTrait(race, "AR");

            Assert.AreEqual(10, race.FactoryProduction);
            Assert.AreEqual(10, race.FactoryBuildCost);
            Assert.AreEqual(10, race.OperableFactories);
            Assert.AreEqual(10, race.MineProductionRate);
            Assert.AreEqual(5, race.MineBuildCost);
            Assert.AreEqual(10, race.OperableMines);
            Assert.AreEqual(1500, race.ColonistsPerResource, "Colonists per resource is not one of the six");
            for (int slot = 1; slot <= 6; slot++)
            {
                Assert.IsFalse(RaceDesignerRules.IsEconomySlotEditable(race, slot), "AR cannot change slot " + slot);
            }
        }
    }

    /// <summary>
    /// Row 15: §8 "Insertion ahead of a partially-built item does not erase its progress, only
    /// pauses it ... work on the partly finished item resumes only once the newly inserted item
    /// ahead of it is finished or deleted", and "Deleting a partially-built item forfeits
    /// everything spent on it".
    /// </summary>
    [TestFixture]
    public class QueueInsertionAndDeletionCoverageTest
    {
        private static Star PlanetWithEnergy(int energy)
        {
            Star star = ProductionCoverageKit.IdealPlanet(ProductionCoverageKit.DefaultRace(), 100000);
            star.ResourcesOnHand = new Resources(0, 0, 0, energy);
            return star;
        }

        [Test]
        public void InsertingAheadOfAPartlyBuiltItem_PausesIt_AndItResumesFromItsProgress()
        {
            Star star = PlanetWithEnergy(40);
            ProductionOrder first = new ProductionOrder(1, new ShipProductionUnit(ProductionCoverageKit.ResourceOnlyDesign(1, 100)), false);
            star.ManufacturingQueue.Queue.Add(first);

            // Year 1: 40 of 100 paid.
            star.ManufacturingQueue.ProcessYear(star, null);
            Assert.AreEqual(60, first.Unit.RemainingCost.Energy);

            // A new item is inserted ahead of it.
            ProductionOrder inserted = new ProductionOrder(1, new ShipProductionUnit(ProductionCoverageKit.ResourceOnlyDesign(2, 100)), false);
            star.ManufacturingQueue.Queue.Insert(0, inserted);

            // Year 2: everything goes to the inserted item; the first is paused, not erased.
            star.ResourcesOnHand = new Resources(0, 0, 0, 40);
            star.ManufacturingQueue.ProcessYear(star, null);
            Assert.AreEqual(60, inserted.Unit.RemainingCost.Energy);
            Assert.AreEqual(60, first.Unit.RemainingCost.Energy, "Paused: no further work, no progress lost");

            // Year 3: the inserted item finishes.
            star.ResourcesOnHand = new Resources(0, 0, 0, 60);
            star.ManufacturingQueue.ProcessYear(star, null);
            Assert.IsFalse(star.ManufacturingQueue.Queue.Contains(inserted), "Finished and removed");
            Assert.AreEqual(60, first.Unit.RemainingCost.Energy);

            // Year 4: the first item resumes with only its remaining 60 to pay, and completes.
            star.ResourcesOnHand = new Resources(0, 0, 0, 60);
            star.ManufacturingQueue.ProcessYear(star, null);
            Assert.IsFalse(star.ManufacturingQueue.Queue.Contains(first), "Completed on its remaining cost, not the full 100");
            Assert.AreEqual(0, star.ResourcesOnHand.Energy);
        }

        [Test]
        public void DeletingTheInsertedItem_AlsoLetsThePausedItemResume()
        {
            Star star = PlanetWithEnergy(40);
            ProductionOrder first = new ProductionOrder(1, new ShipProductionUnit(ProductionCoverageKit.ResourceOnlyDesign(1, 100)), false);
            star.ManufacturingQueue.Queue.Add(first);
            star.ManufacturingQueue.ProcessYear(star, null);

            ProductionOrder inserted = new ProductionOrder(1, new ShipProductionUnit(ProductionCoverageKit.ResourceOnlyDesign(2, 100)), false);
            star.ManufacturingQueue.Queue.Insert(0, inserted);
            star.ResourcesOnHand = new Resources(0, 0, 0, 30);
            star.ManufacturingQueue.ProcessYear(star, null);
            Assert.AreEqual(60, first.Unit.RemainingCost.Energy);

            star.ManufacturingQueue.Queue.Remove(inserted);
            star.ResourcesOnHand = new Resources(0, 0, 0, 30);
            star.ManufacturingQueue.ProcessYear(star, null);

            Assert.AreEqual(30, first.Unit.RemainingCost.Energy, "Work resumes on the paused item");
        }

        [Test]
        public void DeletingAPartlyBuiltItem_ForfeitsWhatWasSpent()
        {
            Star star = PlanetWithEnergy(40);
            ShipDesign design = ProductionCoverageKit.ResourceOnlyDesign(1, 100);
            ProductionOrder order = new ProductionOrder(1, new ShipProductionUnit(design), false);
            star.ManufacturingQueue.Queue.Add(order);
            star.ManufacturingQueue.ProcessYear(star, null);
            Assert.AreEqual(60, order.Unit.RemainingCost.Energy);
            Assert.AreEqual(0, star.ResourcesOnHand.Energy);

            star.ManufacturingQueue.Queue.Remove(order);

            Assert.AreEqual(0, star.ResourcesOnHand.Energy, "Nothing spent on the deleted item comes back");

            ProductionOrder again = new ProductionOrder(1, new ShipProductionUnit(design), false);
            Assert.AreEqual(100, again.Unit.RemainingCost.Energy, "A new order of the same item starts from nothing");
        }
    }

    /// <summary>
    /// Row 17: §8 "Contribute only leftover resources to research" - unchecked, the player's
    /// research percentage goes to research first and the remainder funds the queue; checked,
    /// the queue is fully funded first and only the true leftover goes to research. Either way
    /// whatever the queue leaves goes to research the same year (turn-generation-engine.md §6,
    /// AR-alchemy verdict: the percentage "is taken off the top unless the planet's 'leftover
    /// only' flag is set").
    /// </summary>
    [TestFixture]
    public class LeftoverOnlyCoverageTest
    {
        private static Random Seam()
        {
            return new Random(1);
        }

        [Test]
        public void Unchecked_TheResearchShareComesOffTheTop_TheQueueGetsTheRest()
        {
            ServerData serverState = ProductionCoverageKit.OnePlanetGame(ProductionCoverageKit.DefaultRace(), 15, out EmpireData empire, out Star star);
            Assert.AreEqual(100, star.GetResourceRate(), "test setup: 100 resources a year");
            ProductionOrder ship = new ProductionOrder(1, new ShipProductionUnit(ProductionCoverageKit.ResourceOnlyDesign(1, 1000)), false);
            star.ManufacturingQueue.Queue.Add(ship);
            star.OnlyLeftover = false;

            new StarUpdateStep(Seam()).Process(serverState);

            Assert.AreEqual(15, ProductionCoverageKit.TotalResearch(empire), "15% to research first");
            Assert.AreEqual(1000 - 85, ship.Unit.RemainingCost.Energy, "The queue gets the other 85");
        }

        [Test]
        public void Checked_TheQueueIsFundedFirst_NothingLeftMeansNoResearch()
        {
            ServerData serverState = ProductionCoverageKit.OnePlanetGame(ProductionCoverageKit.DefaultRace(), 15, out EmpireData empire, out Star star);
            ProductionOrder ship = new ProductionOrder(1, new ShipProductionUnit(ProductionCoverageKit.ResourceOnlyDesign(1, 1000)), false);
            star.ManufacturingQueue.Queue.Add(ship);
            star.OnlyLeftover = true;

            new StarUpdateStep(Seam()).Process(serverState);

            Assert.AreEqual(0, ProductionCoverageKit.TotalResearch(empire));
            Assert.AreEqual(1000 - 100, ship.Unit.RemainingCost.Energy, "The queue gets the whole output");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WhatTheQueueLeaves_GoesToResearchTheSameYear(bool onlyLeftover)
        {
            ServerData serverState = ProductionCoverageKit.OnePlanetGame(ProductionCoverageKit.DefaultRace(), 15, out EmpireData empire, out Star star);
            star.OnlyLeftover = onlyLeftover;

            // Two mines at 5 resources each: 10 of the 100.
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new MineProductionUnit(empire.Race), false));

            new StarUpdateStep(Seam()).Process(serverState);

            Assert.AreEqual(2, star.Mines);
            Assert.AreEqual(90, ProductionCoverageKit.TotalResearch(empire), "Nothing is wasted: 100 - 10 to research");
        }
    }

    /// <summary>
    /// Row 36: §10's live test - "mining is added to the stockpile before the production queue is
    /// processed in the same generation".
    /// </summary>
    [TestFixture]
    public class MiningBeforeQueueCoverageTest
    {
        [Test]
        public void ThisYearsMinedGermanium_PaysForThisYearsFactory()
        {
            ServerData serverState = ProductionCoverageKit.OnePlanetGame(ProductionCoverageKit.DefaultRace(), 0, out EmpireData empire, out Star star);
            star.Mines = 10;
            star.MineralConcentration.Germanium = 100;
            star.ResourcesOnHand = new Resources();
            int mined = star.GetMiningRate(100);
            Assert.GreaterOrEqual(mined, 4, "test setup: a year's mining covers a factory's 4 kT");

            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new FactoryProductionUnit(empire.Race), false));

            new StarUpdateStep(new Random(1)).Process(serverState);

            Assert.AreEqual(1, star.Factories, "Built from Germanium mined this same generation");
            Assert.AreEqual(mined - 4, star.ResourcesOnHand.Germanium);
        }
    }
}
