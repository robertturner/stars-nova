namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// behavior-specs-10/population-growth.md conformance over grids, every expected value derived
    /// in the test from the spec's own formulas (written independently of the code):
    /// - section 2 / race-traits.md section 1b: the integer habitability evaluator (rows 1-5) over
    ///   a grid of races x planets, plus the boundary cases;
    /// - section 3: capacity = habitability % x 100 units, halved for Hyper Expansion, +20% Jack of
    ///   All Trades, +10% Only Basic Remote Mining (rows 9, 10); growth = units x rate % x
    ///   habitability % / 10,000 with the 0-99 carry, the rate doubled for Hyper Expansion
    ///   (rows 8, 19), and the crowding factor (1000 - thousandths)^2 / 562,500 above a quarter
    ///   of capacity (row 6);
    /// - section 4: the homeworld (row 20); section 5: own-mine yield and its stochastic rounding
    ///   (rows 21, 22), the 4,000 mine-equivalent fleet cap (row 25) and sequential depletion
    ///   (row 26).
    /// </summary>
    [TestFixture]
    public class PopulationGrowthCoverageTest
    {
        // ---- Habitability: an independent evaluator of the spec text ----

        private sealed class Band
        {
            public int Min;
            public int Max;
            public bool Immune;

            public Band(int min, int max, bool immune = false)
            {
                Min = min;
                Max = max;
                Immune = immune;
            }

            public override string ToString()
            {
                return Immune ? "immune" : Min + "-" + Max;
            }
        }

        private static Race RaceWith(Band g, Band t, Band r, string primaryTrait = "IS")
        {
            Race race = new Race();
            race.Traits.SetPrimary(primaryTrait);
            void Apply(EnvironmentTolerance tolerance, Band band)
            {
                tolerance.MinimumValue = band.Min;
                tolerance.MaximumValue = band.Max;
                tolerance.Immune = band.Immune;
            }

            Apply(race.GravityTolerance, g);
            Apply(race.TemperatureTolerance, t);
            Apply(race.RadiationTolerance, r);
            return race;
        }

        /// <summary>
        /// The spec's evaluator (FUN_1048_490e): closeness sum (10,000 per immune axis,
        /// (100 - floor(100d/h))^2 per in-band axis), ideality from 10,000 scaled by (3h - 2d)/(2h)
        /// per in-band axis with 2d &gt; h, out-of-band penalty min(outside, 15) per axis; result
        /// -penalty, or floor(sqrt(closeness / 3) + 0.9) x ideality / 10,000. The ideality is
        /// scaled in integer arithmetic; the spec does not give the axis order, so the evaluator
        /// returns null when the six possible orders disagree. Bands are of even width, so the
        /// centre is the exact midpoint.
        /// </summary>
        private static int? SpecHabitability(Band[] bands, int[] values)
        {
            long closeness = 0;
            int penalty = 0;
            List<(long num, long den)> factors = new List<(long, long)>();
            for (int axis = 0; axis < 3; axis++)
            {
                Band band = bands[axis];
                int v = values[axis];
                if (band.Immune)
                {
                    closeness += 10000;
                    continue;
                }

                if (v < band.Min || v > band.Max)
                {
                    penalty += Math.Min(v < band.Min ? band.Min - v : v - band.Max, 15);
                    continue;
                }

                int centre = (band.Min + band.Max) / 2;
                int d = Math.Abs(v - centre);
                int h = v < centre ? centre - band.Min : band.Max - centre;
                long term = 100 - (100L * d / h);
                closeness += term * term;
                if (2 * d > h)
                {
                    factors.Add(((3L * h) - (2L * d), 2L * h));
                }
            }

            if (penalty != 0)
            {
                return -penalty;
            }

            long root = (long)Math.Floor(Math.Sqrt(closeness / 3.0) + 0.9);
            HashSet<long> results = new HashSet<long>();
            foreach (IEnumerable<(long num, long den)> order in Permutations(factors))
            {
                long ideality = 10000;
                foreach ((long num, long den) in order)
                {
                    ideality = ideality * num / den;
                }

                results.Add(root * ideality / 10000);
            }

            return results.Count == 1 ? (int)results.First() : (int?)null;
        }

        private static IEnumerable<IEnumerable<T>> Permutations<T>(List<T> items)
        {
            if (items.Count <= 1)
            {
                yield return items;
                yield break;
            }

            for (int i = 0; i < items.Count; i++)
            {
                List<T> rest = items.Where((_, j) => j != i).ToList();
                foreach (IEnumerable<T> tail in Permutations(rest))
                {
                    yield return new[] { items[i] }.Concat(tail);
                }
            }
        }

        private static readonly Band[][] RaceGrid =
        {
            new[] { new Band(20, 80), new Band(20, 80), new Band(20, 80) },
            new[] { new Band(0, 100), new Band(0, 100), new Band(0, 100) },
            new[] { new Band(10, 50), new Band(40, 60), new Band(30, 90) },
            new[] { new Band(0, 40), new Band(56, 100), new Band(14, 86) },
            new[] { new Band(0, 0, true), new Band(20, 80), new Band(34, 66) },
            new[] { new Band(44, 56), new Band(0, 0, true), new Band(0, 0, true) },
            new[] { new Band(0, 0, true), new Band(0, 0, true), new Band(0, 0, true) },
            new[] { new Band(48, 52), new Band(2, 98), new Band(60, 70) },
        };

        [Test]
        public void HabPercent_MatchesTheSpecEvaluator_OverARaceByPlanetGrid()
        {
            List<string> mismatches = new List<string>();
            int compared = 0;
            foreach (Band[] bands in RaceGrid)
            {
                Race race = RaceWith(bands[0], bands[1], bands[2]);
                for (int g = 0; g <= 100; g += 4)
                {
                    for (int t = 0; t <= 100; t += 4)
                    {
                        for (int r = 0; r <= 100; r += 4)
                        {
                            int? expected = SpecHabitability(bands, new[] { g, t, r });
                            if (expected == null)
                            {
                                continue;
                            }

                            compared++;
                            int actual = race.HabPercent(new Star { Gravity = g, Temperature = t, Radiation = r });
                            if (actual != expected.Value && mismatches.Count < 10)
                            {
                                mismatches.Add($"{string.Join("/", bands.Select(b => b.ToString()))} at {g}/{t}/{r}: spec {expected}, code {actual}");
                            }
                        }
                    }
                }
            }

            Assert.Greater(compared, 100000);
            Assert.IsEmpty(mismatches, string.Join("\n", mismatches));
        }

        /// <summary>Boundary cases on a 20-80 race (centre 50, h = 30 both sides), the other two
        /// axes at the centre.</summary>
        [TestCase(50, 100)]   // centre
        [TestCase(65, 87)]    // 2d == h: term (100 - 50)^2, no edge factor: floor(sqrt(22500 / 3) + 0.9) = 87
        [TestCase(80, 41)]    // the band edge, in band: term 0, edge factor 1/2
        [TestCase(20, 41)]
        [TestCase(81, -1)]    // one click out
        [TestCase(95, -15)]   // 15 out
        [TestCase(96, -15)]   // 16 out: capped at 15
        [TestCase(0, -15)]
        public void HabPercent_BoundaryCases(int gravity, int expected)
        {
            Band[] bands = { new Band(20, 80), new Band(20, 80), new Band(20, 80) };
            int? spec = SpecHabitability(bands, new[] { gravity, 50, 50 });
            Assert.AreEqual(spec, RaceWith(bands[0], bands[1], bands[2]).HabPercent(new Star { Gravity = gravity, Temperature = 50, Radiation = 50 }));
            Assert.AreEqual(expected, spec, "hand-worked value");
        }

        [Test]
        public void HabPercent_PenaltiesAddAcrossAxes_DownToMinus45()
        {
            Race race = RaceWith(new Band(20, 80), new Band(20, 80), new Band(20, 80));
            Assert.AreEqual(-45, race.HabPercent(new Star { Gravity = 0, Temperature = 100, Radiation = 0 }));
            Assert.AreEqual(-17, race.HabPercent(new Star { Gravity = 18, Temperature = 95, Radiation = 50 }), "2 + 15");
        }

        [Test]
        public void HabPercent_AnImmuneAxisCountsAsPerfect()
        {
            Race race = RaceWith(new Band(0, 0, true), new Band(0, 0, true), new Band(0, 0, true));
            Assert.AreEqual(100, race.HabPercent(new Star { Gravity = 1, Temperature = 99, Radiation = 0 }));
        }

        // ---- Capacity (rows 7, 9, 10, and OBRM) ----

        private static IEnumerable<TestCaseData> CapacityCases()
        {
            string[][] traitSets =
            {
                new[] { "IS" }, new[] { "HE" }, new[] { "JOAT" }, new[] { "IS", "OBRM" }, new[] { "HE", "OBRM" }, new[] { "JOAT", "OBRM" },
            };
            foreach (string[] traits in traitSets)
            {
                yield return new TestCaseData((object)traits).SetName("{m}(" + string.Join("+", traits) + ")");
            }
        }

        [TestCaseSource(nameof(CapacityCases))]
        public void Capacity_IsHabitabilityTimes100Units_WithTheTraitModifiers(string[] traits)
        {
            Race race = RaceWith(new Band(0, 100), new Band(0, 100), new Band(0, 100), traits[0]);
            foreach (string lesser in traits.Skip(1))
            {
                race.Traits.Add(lesser);
            }

            int checkedValues = 0;
            for (int g = 0; g <= 100; g += 2)
            {
                for (int t = 0; t <= 100; t += 10)
                {
                    Star star = new Star { Gravity = g, Temperature = t, Radiation = 50 };
                    int hab = race.HabPercent(star);
                    if (hab <= 0)
                    {
                        continue; // the spec gives no capacity for a planet of no value (Nova uses a stand-in)
                    }

                    long units = hab * 100L;
                    if (traits[0] == "HE")
                    {
                        units /= 2;
                    }

                    if (traits[0] == "JOAT")
                    {
                        units += units / 5;
                    }

                    if (traits.Contains("OBRM"))
                    {
                        units += units / 10;
                    }

                    Assert.AreEqual(units * 100, star.CapacityColonists(race), 1e-6, $"hab {hab}%");
                    checkedValues++;
                }
            }

            Assert.Greater(checkedValues, 100);
        }

        [Test]
        public void HyperExpansion_HalvesAndJackOfAllTrades_AddsAFifth_ToTheNominalMillion()
        {
            Race he = RaceWith(new Band(20, 80), new Band(20, 80), new Band(20, 80), "HE");
            Race joat = RaceWith(new Band(20, 80), new Band(20, 80), new Band(20, 80), "JOAT");
            Race plain = RaceWith(new Band(20, 80), new Band(20, 80), new Band(20, 80), "IS");
            Star home = new Star { Gravity = 50, Temperature = 50, Radiation = 50 };

            Assert.AreEqual(1000000, plain.MaxPopulation);
            Assert.AreEqual(500000, he.MaxPopulation);
            Assert.AreEqual(1200000, joat.MaxPopulation);
            Assert.AreEqual(500000, home.CapacityColonists(he), 1e-6);
            Assert.AreEqual(1200000, home.CapacityColonists(joat), 1e-6);
        }

        // ---- Growth (rows 6, 8, 19) ----

        private static Race GrowthRace(string primaryTrait, int growthRate)
        {
            Race race = RaceWith(new Band(0, 100), new Band(0, 100), new Band(0, 100), primaryTrait);
            race.GrowthRate = growthRate;
            return race;
        }

        /// <summary>
        /// At a quarter of capacity or less: units x (growth % x habitability %) / 10,000 units,
        /// whole units to the population and the remainder (hundredths of a unit, i.e. colonists)
        /// to the carry. Hyper Expansion doubles the growth rate.
        /// </summary>
        [Test]
        public void UncrowdedGrowth_IsUnitsTimesRateTimesHabitability_WithTheRemainderCarried()
        {
            List<string> mismatches = new List<string>();
            int compared = 0;
            foreach (string prt in new[] { "IS", "HE" })
            {
                foreach (int g in new[] { 1, 4, 7, 10, 15, 19, 20 })
                {
                    Race race = GrowthRace(prt, g);
                    int rate = prt == "HE" ? 2 * g : g;
                    foreach (int v in new[] { 50, 46, 40, 33, 27, 20, 12, 3 })
                    {
                        foreach (int colonists in new[] { 100, 2500, 4300, 10000, 50000, 125000 })
                        {
                            Star star = new Star { Gravity = v, Temperature = 50, Radiation = 50, Colonists = colonists };
                            int hab = race.HabPercent(star);
                            double capacity = star.CapacityColonists(race);
                            if (hab <= 0 || colonists * 4 > capacity)
                            {
                                continue;
                            }

                            long total = (colonists / 100L) * rate * hab / 100; // colonists (hundredths of a unit)
                            int whole = star.GrowthWithCarry(race, out int carry);
                            compared++;
                            if (whole != total / 100 * 100 || carry != total % 100)
                            {
                                mismatches.Add($"{prt} g{g} hab{hab} pop{colonists}: spec +{total / 100 * 100} carry {total % 100}, code +{whole} carry {carry}");
                            }
                        }
                    }
                }
            }

            Assert.Greater(compared, 300);
            Assert.IsEmpty(mismatches.Take(10), string.Join("\n", mismatches.Take(10)) + $"\n({mismatches.Count} in all)");
        }

        [Test]
        public void HyperExpansion_GrowsLikeTwiceTheSliderRate()
        {
            Star star = new Star { Gravity = 30, Temperature = 50, Radiation = 50, Colonists = 20000 };
            Race he = GrowthRace("HE", 9);
            Race plain = GrowthRace("IS", 18);
            int heWhole = star.GrowthWithCarry(he, out int heCarry);
            int plainWhole = star.GrowthWithCarry(plain, out int plainCarry);
            Assert.AreEqual(plainWhole, heWhole);
            Assert.AreEqual(plainCarry, heCarry);
        }

        /// <summary>
        /// Above a quarter of capacity and below it: the rate is further multiplied by
        /// (1000 - capacity in thousandths, rounded down)^2 / 562,500. The spec does not say
        /// whether the scaled rate is truncated before use, so the comparison allows the most
        /// that truncation could cost (under one unit of rate: units / 100 colonists) plus one.
        /// </summary>
        [Test]
        public void CrowdedGrowth_UsesTheThousandthsCrowdingFactor()
        {
            List<string> mismatches = new List<string>();
            int compared = 0;
            foreach (int g in new[] { 10, 15, 20 })
            {
                Race race = GrowthRace("IS", g);
                foreach (int v in new[] { 50, 40, 30 })
                {
                    Star probe = new Star { Gravity = v, Temperature = 50, Radiation = 50 };
                    int hab = race.HabPercent(probe);
                    double capacity = probe.CapacityColonists(race);
                    for (double share = 0.26; share < 0.995; share += 0.0373)
                    {
                        int colonists = (int)(capacity * share) / 100 * 100;
                        if (colonists * 4 <= capacity || colonists >= capacity)
                        {
                            continue;
                        }

                        Star star = new Star { Gravity = v, Temperature = 50, Radiation = 50, Colonists = colonists };
                        long units = colonists / 100;
                        long thousandths = (long)Math.Floor(colonists * 1000.0 / capacity);
                        double expected = units * (double)(g * hab) * (1000 - thousandths) * (1000 - thousandths) / 562500.0 / 100.0;
                        int whole = star.GrowthWithCarry(race, out int carry);
                        double tolerance = (units / 100.0) + 1;
                        compared++;
                        if (Math.Abs((whole + carry) - expected) > tolerance)
                        {
                            mismatches.Add($"g{g} hab{hab} pop{colonists} ({thousandths}/1000): spec {expected:F1}, code {whole + carry}");
                        }
                    }
                }
            }

            Assert.Greater(compared, 100);
            Assert.IsEmpty(mismatches.Take(10), string.Join("\n", mismatches.Take(10)) + $"\n({mismatches.Count} in all)");
        }

        [Test]
        public void AFreshColonyGrowsAtTheFullRate()
        {
            // Row 19: 2,500 colonists on a 70% planet of a 15% race: 25 x 15 x 70 / 100 = 262.5 ->
            // 262 colonists: 200 to the population, 62 to the carry.
            Race race = GrowthRace("IS", 15);
            Star star = new Star { Gravity = 35, Temperature = 50, Radiation = 50, Colonists = 2500 };
            Assume.That(race.HabPercent(star), Is.EqualTo(SpecHabitability(new[] { new Band(0, 100), new Band(0, 100), new Band(0, 100) }, new[] { 35, 50, 50 })));
            int hab = race.HabPercent(star);
            long total = 25L * 15 * hab / 100;
            int whole = star.GrowthWithCarry(race, out int carry);
            Assert.AreEqual(total / 100 * 100, whole);
            Assert.AreEqual(total % 100, carry);
        }

        // ---- Homeworld (row 20) ----

        [Test]
        public void AHomeworldAtTheRacesCentreIsWorth100Percent_AndHoldsAMillion()
        {
            foreach (Band[] bands in RaceGrid)
            {
                Race race = RaceWith(bands[0], bands[1], bands[2]);
                Star home = new Star
                {
                    Gravity = race.GravityTolerance.OptimumLevel,
                    Temperature = race.TemperatureTolerance.OptimumLevel,
                    Radiation = race.RadiationTolerance.OptimumLevel,
                };
                Assert.AreEqual(100, race.HabPercent(home), string.Join("/", bands.Select(b => b.ToString())));
                Assert.AreEqual(1000000, home.CapacityColonists(race), 1e-6);
            }
        }

        [Test]
        public void TheHomeworldStartsWith25000Colonists_17500WithLowStartingPopulation_AndTenOfEachInstallation()
        {
            bool savedAccelerated = GameSettings.Data.AcceleratedStart;
            try
            {
                GameSettings.Data.AcceleratedStart = false;
                Race race = RaceWith(new Band(20, 80), new Band(20, 80), new Band(20, 80));
                Assert.AreEqual(25000, race.GetStartingPopulation());
                race.Traits.Add("LSP");
                Assert.AreEqual(17500, race.GetStartingPopulation());
            }
            finally
            {
                GameSettings.Data.AcceleratedStart = savedAccelerated;
            }

            Assert.AreEqual(10, Nova.Server.NewGame.StarMapinitializer.HomeWorldStartingInstallations);
        }

        // ---- Mining (rows 21, 22, 25, 26) ----

        /// <summary>A Random whose NextDouble is fixed, for the stochastic-rounding draw.</summary>
        private sealed class FixedRandom : Random
        {
            private readonly double value;

            public FixedRandom(double value)
            {
                this.value = value;
            }

            public override double NextDouble()
            {
                return value;
            }

            protected override double Sample()
            {
                return value;
            }
        }

        private static Star MiningStar(int mines, int outputSetting)
        {
            Race race = RaceWith(new Band(20, 80), new Band(20, 80), new Band(20, 80));
            race.MineProductionRate = outputSetting;
            race.OperableMines = 10;
            race.OperableFactories = 10;
            race.ColonistsPerResource = 1000;
            return new Star { Name = "Pit", Owner = 1, ThisRace = race, Colonists = 1000000, Mines = mines, Gravity = 50, Temperature = 50, Radiation = 50 };
        }

        /// <summary>
        /// Section 5: own mines yield mines x concentration x output setting / 1,000 kT, the
        /// remainder (hundredths) rounded up when a 0-99 draw is below it. A draw at the very top
        /// of the range never rounds up; a draw of 0 rounds up any non-zero remainder.
        /// </summary>
        [Test]
        public void OwnMineYield_OverAGrid_WithTheStochasticRoundingAtBothExtremes()
        {
            List<string> mismatches = new List<string>();
            foreach (int mines in new[] { 1, 10, 15, 24, 100, 250, 999 })
            {
                foreach (int setting in new[] { 5, 10, 17, 25 })
                {
                    foreach (int concentration in new[] { 1, 7, 25, 30, 99, 100, 150 })
                    {
                        Star star = MiningStar(mines, setting);
                        long product = (long)mines * concentration * setting;
                        long floor = product / 1000;
                        long ceiling = (product + 999) / 1000;

                        int low;
                        using (GameRandom.Use(new FixedRandom(0.0)))
                        {
                            low = star.GetMiningRate(concentration);
                        }

                        int high;
                        using (GameRandom.Use(new FixedRandom(0.9999999)))
                        {
                            high = star.GetMiningRate(concentration);
                        }

                        if (low != ceiling || high != floor)
                        {
                            mismatches.Add($"{mines} mines x{setting} at {concentration}: spec {floor}/{ceiling}, code {high}/{low}");
                        }
                    }
                }
            }

            Assert.IsEmpty(mismatches, string.Join("\n", mismatches.Take(10)));
        }

        /// <summary>Row 22: the remainder r (in hundredths) rounds up exactly when the draw is
        /// below r: 2.25 kT rounds up for a draw of 24 and not for 25.</summary>
        [TestCase(0.24, 3)]
        [TestCase(0.25, 2)]
        [TestCase(0.5, 2)]
        public void StochasticRounding_UpWhenTheDrawIsBelowTheRemainder(double draw, int expected)
        {
            // 9 mines x 25 concentration x 10 / 1,000 = 2.25 kT.
            Star star = MiningStar(9, 10);
            using (GameRandom.Use(new FixedRandom(draw)))
            {
                Assert.AreEqual(expected, star.GetMiningRate(25));
            }
        }

        /// <summary>Row 25: a fleet's remote mining is capped at 4,000 mine-equivalents; at
        /// concentration 1 such a fleet yields exactly 40 kT.</summary>
        [Test]
        public void RemoteMining_TheFleetCapIs4000MineEquivalents_YieldingFortyKtAtConcentrationOne()
        {
            Assert.AreEqual(4000, MinerFleet(3, 2000).MineEquivalents, "6,000 robots' worth is capped");
            Assert.AreEqual(3999, MinerFleet(1, 3999).MineEquivalents);

            int concentration = 1;
            int progress = 0;
            using (GameRandom.Use(new FixedRandom(0.5)))
            {
                Assert.AreEqual(40, Star.MineForFleet(4000, ref concentration, ref progress, 10));
            }
        }

        private static Fleet MinerFleet(int ships, int robotValuePerShip)
        {
            Component hullBlueprint = new Component { Name = "Miner Hull", Mass = 10, Cost = new Resources(1, 1, 1, 1) };
            Hull hull = new Hull { FuelCapacity = 100, Modules = new List<HullModule>() };
            Component robot = new Component { Name = "Robot", Mass = 1, Cost = new Resources(0, 0, 0, 1), Type = ItemType.MiningRobot };
            robot.Properties.Add("Mining Robot", new IntegerProperty(robotValuePerShip));
            hull.Modules.Add(new HullModule { CellNumber = 0, ComponentType = "Mining Robot", ComponentMaximum = 1, ComponentCount = 1, AllocatedComponent = robot });
            hullBlueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = hullBlueprint, Name = "Miner" };
            design.Update();

            Fleet fleet = new Fleet(1) { Owner = 1 };
            fleet.Composition.Add(design.Key, new ShipToken(design, ships));
            return fleet;
        }

        /// <summary>Row 26: several mining applications in one turn act in sequence, each on the
        /// concentration the previous one left (section 5; Example 5's first fleet mines 4,000 kT
        /// at concentration 100).</summary>
        [Test]
        public void SuccessiveMiningApplicationsSeeTheConcentrationThePreviousOneLeft()
        {
            int concentration = 100;
            int progress = 0;
            using (GameRandom.Use(new FixedRandom(0.5)))
            {
                int first = Star.MineForFleet(4000, ref concentration, ref progress, 10);
                Assert.AreEqual(4000, first);
                Assert.Less(concentration, 100);

                int left = concentration;
                int second = Star.MineForFleet(4000, ref concentration, ref progress, 10);
                Assert.AreEqual(4000 * left / 100, second, "4,000 x the concentration the first fleet left / 100");
                Assert.Less(concentration, left);
            }
        }
    }
}
