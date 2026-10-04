namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;
    using Nova.Server.NewGame;
    using Nova.Server.TurnSteps;

    using static Nova.Common.TechLevel;

    /// <summary>
    /// behavior-specs-10/research-tech-tree.md coverage rows 1, 2, 3, 4, 5, 6, 9, 10, 13, 15, 17,
    /// 18, 19 and 29: every number below is the spec's, with the expected values derived in the
    /// test from the spec's formulas (section 1 resources, section 3 cost curve and base table,
    /// section 4 allocation, section 5 / Open Questions buy loop, race-traits.md section 5a for
    /// Alternate Reality, production-queue.md section 10h for the live resource readings).
    /// </summary>
    [TestFixture]
    public class ResearchCoverageTest
    {
        /// <summary>Section 3's base cost table, levels 1-26, typed in from the spec.</summary>
        private static readonly int[] SpecBaseCost =
        {
            0, 50, 80, 130, 210, 340, 550, 890, 1440, 2330, 3770, 6100, 9870, 13850,
            18040, 22440, 27050, 31870, 36900, 42140, 47590, 53250, 59120, 65200, 71490, 77990, 84700,
        };

        private static readonly ResearchField[] Fields =
        {
            ResearchField.Energy, ResearchField.Weapons, ResearchField.Propulsion,
            ResearchField.Construction, ResearchField.Electronics, ResearchField.Biotechnology,
        };

        private static Race RaceWithCosts(int factor)
        {
            Race race = new Race();
            race.Traits.SetPrimary("HE");
            foreach (ResearchField field in Fields)
            {
                race.ResearchCosts[field] = factor;
            }

            return race;
        }

        private static TechLevel Levels(int each)
        {
            TechLevel levels = new TechLevel();
            foreach (ResearchField field in Fields)
            {
                levels[field] = each;
            }

            return levels;
        }

        // ---- Row 1: six fields, levels 0-26 ----

        [Test]
        public void SixFields_LevelsRunFromZeroTo26()
        {
            Assert.AreEqual(6, Enum.GetValues(typeof(ResearchField)).Length);
            Assert.AreEqual(26, TechLevel.MaxLevel);
            CollectionAssert.AreEquivalent(Fields, Enum.GetValues(typeof(ResearchField)).Cast<ResearchField>());
        }

        [Test]
        public void TheBuyLoopNeverRaisesAFieldAbove26_AndBanksTheRest()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "HE");
            empire.ResearchLevels = Levels(25);
            Star star = new Star { Name = "Home", Owner = 1 };

            Contribute(new StarUpdateStep(), server, star, 1000000);

            Assert.AreEqual(26, empire.ResearchLevels[ResearchField.Weapons]);
            int cost26 = SpecBaseCost[26] + (10 * 25 * 6);
            Assert.AreEqual(1000000 - cost26, empire.ResearchResources[ResearchField.Weapons], "the surplus stays banked in the field");
        }

        // ---- Rows 7, 9, 19: the cost curve over a grid ----

        private static IEnumerable<TestCaseData> CostGrid()
        {
            int[] factors = { 50, 100, 175 };
            int[] surcharges = { 0, 3, 17, 60, 155 };
            foreach (int factor in factors)
            {
                foreach (int total in surcharges)
                {
                    foreach (int level in new[] { 1, 2, 5, 10, 13, 20, 26 })
                    {
                        // Where 1.75 leaves a fraction the spec does not say how it is rounded.
                        if ((SpecBaseCost[level] + (10L * total)) * factor % 100 == 0)
                        {
                            yield return new TestCaseData(factor, total, level);
                        }
                    }
                }
            }
        }

        /// <summary>Section 3: (base cost of the level + 10 x the empire's total levels over all six
        /// fields) x the field's cost factor (0.5 cheap, 1 normal, 1.75 expensive). The test
        /// spreads the total over the fields so only the sum can matter. Where 1.75 leaves a
        /// fraction the spec does not say how it is rounded, so the grid leaves those cases out.</summary>
        [TestCaseSource(nameof(CostGrid))]
        public void CostCurve(int factor, int totalLevels, int level)
        {
            Race race = RaceWithCosts(factor);
            TechLevel levels = new TechLevel();
            int remaining = totalLevels;
            foreach (ResearchField field in Fields)
            {
                int here = Math.Min(26, remaining);
                levels[field] = here;
                remaining -= here;
            }

            long scaled = (long)(SpecBaseCost[level] + (10 * totalLevels)) * factor;
            Assert.AreEqual(scaled / 100, Research.Cost(ResearchField.Weapons, race, levels, level, false));
        }

        /// <summary>Section 3: every field from 0 to 26 at normal cost, from an untouched empire,
        /// totals 4,185,240 resources (the surcharge folded in along the way).</summary>
        [Test]
        public void AllSixFieldsToLevel26AtNormalCost_Total4185240()
        {
            Race race = RaceWithCosts(100);
            TechLevel levels = Levels(0);
            long total = 0;
            for (int level = 1; level <= 26; level++)
            {
                foreach (ResearchField field in Fields)
                {
                    total += Research.Cost(field, race, levels, level, false);
                    levels[field] = level;
                }
            }

            Assert.AreEqual(4185240, total);
        }

        [Test]
        public void TotalLevelsAreReadLive_ALevelBoughtEarlierInTheTurnRaisesTheNextPrice()
        {
            // Row 19: the buy loop re-prices with the new total after every level.
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "HE");
            Star star = new Star { Name = "Home", Owner = 1 };

            // Weapons 1 costs 50; Weapons 2 then costs 80 + 10 x 1 = 90 (not 80).
            Contribute(new StarUpdateStep(), server, star, 50 + 89);
            Assert.AreEqual(1, empire.ResearchLevels[ResearchField.Weapons]);
            Assert.AreEqual(89, empire.ResearchResources[ResearchField.Weapons]);

            Contribute(new StarUpdateStep(), server, star, 1);
            Assert.AreEqual(2, empire.ResearchLevels[ResearchField.Weapons]);
            Assert.AreEqual(0, empire.ResearchResources[ResearchField.Weapons]);
        }

        // ---- Rows 4, 13, 17, 18: 1:1, one target field, carryover, several levels a turn ----

        private static void Contribute(StarUpdateStep step, ServerData server, Star star, int amount)
        {
            typeof(StarUpdateStep).GetField("serverState", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(step, server);
            typeof(StarUpdateStep).GetMethod("ContributeResearch", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(step, new object[] { star, amount });
        }

        private static EmpireData NewEmpire(ServerData server, int id, string primaryTrait, params string[] lesserTraits)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Id = (ushort)id;
            empire.Race = RaceWithCosts(100);
            empire.Race.Traits.SetPrimary(primaryTrait);
            foreach (string trait in lesserTraits)
            {
                empire.Race.Traits.Add(trait);
            }

            empire.AvailableComponents = new RaceComponents();
            empire.ResearchLevels = Levels(0);
            empire.ResearchTopics = new TechLevel();
            empire.ResearchTopics[ResearchField.Weapons] = 1;
            server.AllEmpires.Add(id, empire);
            return empire;
        }

        [Test]
        public void ResourcesBecomeResearchOneForOne_AllInTheSelectedField()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "HE");
            Star star = new Star { Name = "Home", Owner = 1 };

            Contribute(new StarUpdateStep(), server, star, 37);

            Assert.AreEqual(37, empire.ResearchResources[ResearchField.Weapons]);
            foreach (ResearchField field in Fields.Where(f => f != ResearchField.Weapons))
            {
                Assert.AreEqual(0, empire.ResearchResources[field], field.ToString());
            }
        }

        [Test]
        public void ProgressCarriesOverAcrossTurns()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "HE");
            Star star = new Star { Name = "Home", Owner = 1 };
            StarUpdateStep step = new StarUpdateStep();

            Contribute(step, server, star, 30);
            Assert.AreEqual(0, empire.ResearchLevels[ResearchField.Weapons]);
            Contribute(step, server, star, 19);
            Assert.AreEqual(0, empire.ResearchLevels[ResearchField.Weapons], "49 of 50");
            Contribute(step, server, star, 1);
            Assert.AreEqual(1, empire.ResearchLevels[ResearchField.Weapons]);
            Assert.AreEqual(0, empire.ResearchResources[ResearchField.Weapons]);
        }

        [Test]
        public void ALargePoolBuysSeveralLevelsInOneTurn()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "HE");
            Star star = new Star { Name = "Home", Owner = 1 };

            // Weapons 1, 2, 3 at total levels 0, 1, 2: 50 + 90 + 150 = 290, then 4 costs 240.
            Contribute(new StarUpdateStep(), server, star, 290 + 239);

            Assert.AreEqual(3, empire.ResearchLevels[ResearchField.Weapons]);
            Assert.AreEqual(239, empire.ResearchResources[ResearchField.Weapons]);
        }

        // ---- Rows 15, 29: Generalized Research ----

        [Test]
        public void GeneralizedResearch_HalfToTheTarget_FifteenPercentToEachOtherField()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "HE", "GR");
            empire.ResearchLevels = Levels(26);
            Star star = new Star { Name = "Home", Owner = 1 };

            Contribute(new StarUpdateStep(), server, star, 1000);

            Assert.AreEqual(500, empire.ResearchResources[ResearchField.Weapons]);
            foreach (ResearchField field in Fields.Where(f => f != ResearchField.Weapons))
            {
                Assert.AreEqual(150, empire.ResearchResources[field], field.ToString());
            }

            Assert.AreEqual(1250, Fields.Sum(f => empire.ResearchResources[f]), "125% in all");
        }

        [Test]
        public void ForecastFraction_IsHalfForGeneralizedResearch_WholeOtherwise()
        {
            Race gr = RaceWithCosts(100);
            gr.Traits.Add("GR");
            Assert.AreEqual(0.5, Research.TargetFieldContributionFraction(gr));
            Assert.AreEqual(1.0, Research.TargetFieldContributionFraction(RaceWithCosts(100)));
        }

        // ---- Rows 5, 6: research percentage and leftover-only ----

        private static Race EconomyRace(string primaryTrait = "IS")
        {
            Race race = new Race();
            race.Traits.SetPrimary(primaryTrait);
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.OperableFactories = 10;
            race.MineProductionRate = 10;
            race.MineBuildCost = 5;
            race.OperableMines = 10;
            race.GrowthRate = 15;
            return race;
        }

        private static Star HomeStar(Race race, int colonists, int factories)
        {
            Star star = new Star { Name = "Home", Owner = 1, ThisRace = race, Colonists = colonists, Factories = factories };
            star.Gravity = race.GravityTolerance.OptimumLevel;
            star.Temperature = race.TemperatureTolerance.OptimumLevel;
            star.Radiation = race.RadiationTolerance.OptimumLevel;
            return star;
        }

        [TestCase(0, 0)]
        [TestCase(10, 10)]
        [TestCase(50, 50)]
        [TestCase(100, 100)]
        public void ResearchPercentage_ReservesThatShareOfThePlanetsResources(int percent, int expected)
        {
            Star star = HomeStar(EconomyRace(), 75000, 25); // 75 + 25 = 100 resources
            Assume.That(star.GetResourceRate(), Is.EqualTo(100));

            star.UpdateResearch(percent);
            star.UpdateResources();

            Assert.AreEqual(expected, star.ResearchAllocation);
            Assert.AreEqual(100 - expected, star.ResourcesOnHand.Energy, "the rest goes to the queue");
        }

        [Test]
        public void LeftoverOnly_IgnoresThePercentage()
        {
            Star star = HomeStar(EconomyRace(), 75000, 25);
            star.OnlyLeftover = true;

            star.UpdateResearch(50);
            star.UpdateResources();

            Assert.AreEqual(0, star.ResearchAllocation);
            Assert.AreEqual(100, star.ResourcesOnHand.Energy);
        }

        // ---- Row 2: colony resources ----

        /// <summary>production-queue.md section 10h, live (Stars! 2.70j): a Humanoid homeworld's
        /// resources per year against its population (operable cap 10 per 10,000) and factories.
        /// Population part = population / 1,000; factory part = the operating factories at
        /// 10 resources per 10 factories.</summary>
        [TestCase(25000, 10, 35)]
        [TestCase(28000, 13, 41)]
        [TestCase(33000, 17, 50)]
        [TestCase(38000, 22, 60)]
        [TestCase(43000, 28, 71)]
        [TestCase(50000, 31, 81)]
        [TestCase(57000, 33, 90)]
        [TestCase(66000, 35, 101)]
        public void LiveHomeworldReadings(int colonists, int factories, int resources)
        {
            Assert.AreEqual(resources, HomeStar(EconomyRace(), colonists, factories).GetResourceRate());
        }

        [TestCase(700, 70000, 100)]
        [TestCase(1000, 70000, 70)]
        [TestCase(2500, 70000, 28)]
        [TestCase(1000, 999, 1)]
        [TestCase(700, 1399, 1)]
        public void PopulationPart_IsPopulationOverColonistsPerResource_RoundedDown(int colonistsPerResource, int colonists, int resources)
        {
            Race race = EconomyRace();
            race.ColonistsPerResource = colonistsPerResource;
            Assert.AreEqual(resources, HomeStar(race, colonists, 0).GetResourceRate());
        }

        [Test]
        public void OnlyOperableFactoriesProduce()
        {
            // 25,000 colonists at 10 per 10,000 operate 25 of the 40 factories: 25 + 25.
            Assert.AreEqual(50, HomeStar(EconomyRace(), 25000, 40).GetResourceRate());
        }

        /// <summary>race-traits.md section 5a step 6: "A result of 0 is raised to 1. This shared
        /// ending applies to both branches."</summary>
        [Test]
        public void APopulatedPlanetProducesAtLeastOneResource()
        {
            Assert.AreEqual(1, HomeStar(EconomyRace(), 100, 0).GetResourceRate());
        }

        /// <summary>race-traits.md section 5a step 3, every PRT: colonists above capacity C count at
        /// half weight, C + (P - C) / 2, never beyond 2C.</summary>
        [TestCase(1000000, 1000)]
        [TestCase(1200000, 1100)]
        [TestCase(2000000, 1500)]
        [TestCase(3000000, 2000)]
        [TestCase(3500000, 2000)]
        public void ColonistsAboveCapacityCountAtHalfWeight(int colonists, int resources)
        {
            // 100% planet, capacity 1,000,000 (10,000 units).
            Assert.AreEqual(resources, HomeStar(EconomyRace(), colonists, 0).GetResourceRate());
        }

        // ---- Row 3: Alternate Reality ----

        private static Star ArStar(int coefficient, int colonists, int energy, string chassis = "Space Station")
        {
            Race race = EconomyRace("AR");
            race.ColonistsPerResource = coefficient * 100;
            Star star = HomeStar(race, colonists, 0);
            star.EnergyTechLevel = energy;
            if (chassis != null)
            {
                Component blueprint = new Component { Name = chassis, Mass = 0 };
                Hull hull = new Hull { FuelCapacity = 0, DockCapacity = 100, Modules = new List<HullModule>() };
                blueprint.Properties.Add("Hull", hull);
                ShipDesign design = new ShipDesign(1) { Blueprint = blueprint, Name = chassis };
                design.Update();
                Fleet starbase = new Fleet(1) { Owner = 1 };
                starbase.Composition.Add(design.Key, new ShipToken(design, 1));
                star.Starbase = starbase;
            }

            return star;
        }

        /// <summary>race-traits.md section 5a, the twelve live readings (planet value 100%,
        /// homeworld on a Space Station): floor(sqrt(P / c x E) x H x 0.1 + 0.999).</summary>
        [TestCase(10, 25000, 1, 50)]
        [TestCase(10, 28700, 1, 54)]
        [TestCase(10, 33000, 2, 82)]
        [TestCase(10, 38000, 2, 88)]
        [TestCase(10, 43700, 3, 115)]
        [TestCase(10, 50200, 3, 123)]
        [TestCase(10, 57700, 4, 152)]
        [TestCase(17, 25000, 1, 39)]
        [TestCase(17, 28700, 1, 42)]
        [TestCase(17, 33000, 1, 45)]
        [TestCase(17, 38000, 2, 67)]
        [TestCase(17, 43700, 2, 72)]
        public void AlternateReality_LiveReadings(int coefficient, int colonists, int energy, int resources)
        {
            Assert.AreEqual(resources, ArStar(coefficient, colonists, energy).GetResourceRate());
        }

        [Test]
        public void AlternateReality_EnergyIsFlooredAtOne()
        {
            Assert.AreEqual(50, ArStar(10, 25000, 0).GetResourceRate());
        }

        [Test]
        public void AlternateReality_PlanetValueIsFlooredAt25()
        {
            // Out of band on every axis (a negative value): counts as 25%.
            // sqrt(250 / 10 x 1) x 25 x 0.1 + 0.999 = 13.499 -> 13.
            Star star = ArStar(10, 25000, 1);
            star.Gravity = 100;
            star.Temperature = 100;
            star.Radiation = 100;
            Assume.That(star.ThisRace.HabPercent(star), Is.LessThan(25));
            Assert.AreEqual(13, star.GetResourceRate());
        }

        [Test]
        public void AlternateReality_WithoutAStarbase_CountsNobody_AndMakesTheMinimumOne()
        {
            // population-growth.md section 3: no starbase means capacity 0, so the resource
            // routine's capacity-adjusted population is 0 and the planet makes the minimum 1.
            Assert.AreEqual(1, ArStar(10, 25000, 1, null).GetResourceRate());
        }

        [Test]
        public void AlternateReality_ColonistsAboveTheChassisCapacityCountAtHalfWeight()
        {
            // Orbital Fort: 2,500 units. 4,500 units count as 2,500 + 1,000 = 3,500.
            // sqrt(3500 / 10 x 1) x 100 x 0.1 + 0.999 = 188.08 -> 188.
            Assert.AreEqual(188, ArStar(10, 450000, 1, "Orbital Fort").GetResourceRate());
        }

        // ---- Row 10: the "expensive fields start at 3 (4 for JOAT)" option ----

        [TestCase("HE", 3)]
        [TestCase("SS", 3)]
        [TestCase("JOAT", 4)]
        public void ExtraTech_ExpensiveFieldsStartAtThree_FourForJackOfAllTrades(string primaryTrait, int expected)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Race = RaceWithCosts(100);
            empire.Race.Traits.SetPrimary(primaryTrait);
            empire.Race.Traits.Add("ExtraTech");
            empire.Race.ResearchCosts[ResearchField.Weapons] = 175;
            empire.Race.ResearchCosts[ResearchField.Biotechnology] = 175;

            Gameinitializer.ProcessPrimaryTraits(empire);
            int normalFieldBefore = empire.ResearchLevels[ResearchField.Energy];
            Gameinitializer.ProcessSecondaryTraits(empire);

            Assert.AreEqual(expected, empire.ResearchLevels[ResearchField.Weapons]);
            Assert.AreEqual(expected, empire.ResearchLevels[ResearchField.Biotechnology]);
            Assert.AreEqual(normalFieldBefore, empire.ResearchLevels[ResearchField.Energy], "a field that is not Expensive is untouched");
        }
    }
}
