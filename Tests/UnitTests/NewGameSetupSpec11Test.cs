namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Server;
    using Nova.Server.NewGame;

    /// <summary>
    /// docs/behavior-specs-11/new-game-setup.md, the reversals and corrections of the spec-11
    /// pass: section 3's star-count formula (Sparse subtracts a quarter, truncation), the fixed
    /// 12 ly separation sweep with x-sort and random trimming, the Galaxy Clumping pass (random
    /// picks with replacement, exact bands, corrected weights, re-sort), ordinary planets with
    /// no surface minerals, the second planet's own state; section 2's Simplified player-count
    /// table by difficulty and the state OK writes (and the port's reset); and "Starting
    /// population, exact order" step 2 (Expert +10%).
    /// </summary>
    [TestFixture]
    public class NewGameSetupSpec11Test
    {
        /// <summary>Returns queued values in order, checking each lies in the requested range.</summary>
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> values;

            public ScriptedRandom(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public int Remaining => values.Count;

            public override int Next(int minValue, int maxValue)
            {
                Assert.IsTrue(values.Count > 0, $"Unexpected extra draw Next({minValue}, {maxValue})");
                int value = values.Dequeue();
                Assert.That(value, Is.InRange(minValue, maxValue - 1), $"Scripted value outside Next({minValue}, {maxValue})");
                return value;
            }

            public override int Next(int maxValue)
            {
                return Next(0, maxValue);
            }
        }

        /// <summary>Fails on any draw: proves a code path makes no random draws.</summary>
        private class NoDrawRandom : Random
        {
            public override int Next(int minValue, int maxValue)
            {
                Assert.Fail($"Unexpected draw Next({minValue}, {maxValue})");
                return 0;
            }

            public override int Next(int maxValue)
            {
                return Next(0, maxValue);
            }
        }

        /// <summary>Always draws 0 and counts the draws.</summary>
        private class ZeroRandom : Random
        {
            public int Draws;

            public override int Next(int minValue, int maxValue)
            {
                Draws++;
                return minValue;
            }

            public override int Next(int maxValue)
            {
                return Next(0, maxValue);
            }
        }

        private int mapWidth;
        private int mapHeight;
        private int numberOfStars;
        private int starSeparation;
        private int starDensity;
        private int starUniformity;
        private bool useGalaxyPresets;
        private GalaxySize galaxySize;
        private GalaxyDensity galaxyDensity;
        private StartingDistance startingDistance;
        private bool maximumMinerals;
        private bool galaxyClumping;
        private bool acceleratedStart;

        [SetUp]
        public void Init()
        {
            GameSettings settings = GameSettings.Data;
            mapWidth = settings.MapWidth;
            mapHeight = settings.MapHeight;
            numberOfStars = settings.NumberOfStars;
            starSeparation = settings.StarSeparation;
            starDensity = settings.StarDensity;
            starUniformity = settings.StarUniformity;
            useGalaxyPresets = settings.UseGalaxyPresets;
            galaxySize = settings.GalaxySizeSetting;
            galaxyDensity = settings.StarDensitySetting;
            startingDistance = settings.StartingDistanceSetting;
            maximumMinerals = settings.MaximumMinerals;
            galaxyClumping = settings.GalaxyClumping;
            acceleratedStart = settings.AcceleratedStart;

            settings.MapWidth = 400;
            settings.MapHeight = 400;
            settings.StarDensity = 60;
            settings.StarSeparation = 10;
            settings.StarUniformity = 60;
            settings.UseGalaxyPresets = false;
            settings.MaximumMinerals = false;
            settings.GalaxyClumping = false;
            settings.AcceleratedStart = false;
        }

        [TearDown]
        public void Cleanup()
        {
            GameSettings settings = GameSettings.Data;
            settings.MapWidth = mapWidth;
            settings.MapHeight = mapHeight;
            settings.NumberOfStars = numberOfStars;
            settings.StarSeparation = starSeparation;
            settings.StarDensity = starDensity;
            settings.StarUniformity = starUniformity;
            settings.UseGalaxyPresets = useGalaxyPresets;
            settings.GalaxySizeSetting = galaxySize;
            settings.StarDensitySetting = galaxyDensity;
            settings.StartingDistanceSetting = startingDistance;
            settings.MaximumMinerals = maximumMinerals;
            settings.GalaxyClumping = galaxyClumping;
            settings.AcceleratedStart = acceleratedStart;
        }

        // ---------------------------------------------------------------- (1) star count

        // n = D^2 / 5000, plus (n / 4) x (density - 1) truncated for EVERY density (Sparse
        // subtracts a quarter, Normal is the base), Packed then adds a further truncated quarter;
        // cap 999.
        [TestCase(GalaxySize.Tiny, GalaxyDensity.Sparse, 24)]
        [TestCase(GalaxySize.Tiny, GalaxyDensity.Normal, 32)]
        [TestCase(GalaxySize.Tiny, GalaxyDensity.Dense, 40)]
        [TestCase(GalaxySize.Tiny, GalaxyDensity.Packed, 60)]
        [TestCase(GalaxySize.Small, GalaxyDensity.Sparse, 96)]
        [TestCase(GalaxySize.Small, GalaxyDensity.Normal, 128)]
        [TestCase(GalaxySize.Small, GalaxyDensity.Dense, 160)]
        [TestCase(GalaxySize.Small, GalaxyDensity.Packed, 240)]
        [TestCase(GalaxySize.Medium, GalaxyDensity.Sparse, 216)]
        [TestCase(GalaxySize.Medium, GalaxyDensity.Normal, 288)]
        [TestCase(GalaxySize.Medium, GalaxyDensity.Dense, 360)]
        [TestCase(GalaxySize.Medium, GalaxyDensity.Packed, 540)]
        [TestCase(GalaxySize.Large, GalaxyDensity.Sparse, 384)]
        [TestCase(GalaxySize.Large, GalaxyDensity.Normal, 512)]
        [TestCase(GalaxySize.Large, GalaxyDensity.Dense, 640)]
        [TestCase(GalaxySize.Large, GalaxyDensity.Packed, 960)]
        [TestCase(GalaxySize.Huge, GalaxyDensity.Sparse, 600)]
        [TestCase(GalaxySize.Huge, GalaxyDensity.Normal, 800)]
        [TestCase(GalaxySize.Huge, GalaxyDensity.Dense, 999)]
        [TestCase(GalaxySize.Huge, GalaxyDensity.Packed, 999)]
        public void PresetStarCount_SpecElevenTable(GalaxySize size, GalaxyDensity density, int count)
        {
            Assert.AreEqual(count, GameSettings.PresetStarCount(size, density));
        }

        // ---------------------------------------------------------------- (2) Galaxy Clumping

        // Two stars at (0, 0) and (nx, ny); one step picking star 0 moves only star 0 by the
        // band of the squared distance: <= 144 nothing, 145-324 (4 own + nb) / 5, 325-625
        // (2 own + nb) / 3, 626-1,600 (own + nb) / 2, 1,601+ (own + 2 nb) / 3 with no outer limit.
        [TestCase(12, 0, 0, 0, "d^2 = 144: no move")]
        [TestCase(12, 1, 2, 0, "d^2 = 145: (4 x 0 + 12) / 5 = 2, (4 x 0 + 1) / 5 = 0")]
        [TestCase(18, 0, 3, 0, "d^2 = 324: 18 / 5 = 3")]
        [TestCase(18, 1, 6, 0, "d^2 = 325: 18 / 3 = 6")]
        [TestCase(25, 0, 8, 0, "d^2 = 625: 25 / 3 = 8")]
        [TestCase(25, 1, 12, 0, "d^2 = 626: 25 / 2 = 12")]
        [TestCase(40, 0, 20, 0, "d^2 = 1600: 40 / 2 = 20")]
        [TestCase(40, 1, 26, 0, "d^2 = 1601: 80 / 3 = 26")]
        [TestCase(100, 0, 66, 0, "d^2 = 10000: 200 / 3 = 66 - no outer limit")]
        public void RelaxStars_Bands_AreExact_AndOnlyThePickedStarMoves(int nx, int ny, int ex, int ey, string why)
        {
            // The pass makes one step per star, so a far sentinel soaks up the other two steps
            // (it moves toward the pair but stays thousands of ly away) and star 0's single
            // move is observed on its own; the sentinel is dropped afterwards.
            var stars = new List<int[]> { new[] { 0, 0 }, new[] { nx, ny }, new[] { 100000, 100000 } };
            var random = new ScriptedRandom(0, 2, 2);

            StarMapGenerator.RelaxStars(stars, null, random);

            Assert.AreEqual(0, random.Remaining, "one draw per step, one step per star");
            stars.RemoveAll(s => s[0] > 1000);
            Assert.AreEqual(2, stars.Count);
            Assert.AreEqual(new[] { nx, ny }, stars.Single(s => s[0] == nx && s[1] == ny), "the neighbour never moves");
            Assert.AreEqual(new[] { ex, ey }, stars.Single(s => !(s[0] == nx && s[1] == ny)), why);
        }

        [Test]
        public void RelaxStars_OneStepPerStar_PicksAtRandomWithReplacement_TiesToTheLowerIndex_AndResortsByX()
        {
            // Star 0 at (20, 0) has (0, 0) and (40, 0) both at d^2 = 400: the lower index wins,
            // so it moves toward (0, 0): (2 x 20 + 0) / 3 = 13. Step 2 picks index 1, (0, 0),
            // whose nearest is now (13, 0), d^2 = 169: (4 x 0 + 13) / 5 = 2. Step 3 picks index
            // 1 again (with replacement): (2, 0) to (13, 0) is d^2 = 121, no move. Then the list
            // is re-sorted by x.
            var stars = new List<int[]> { new[] { 20, 0 }, new[] { 0, 0 }, new[] { 40, 0 } };
            var random = new ScriptedRandom(0, 1, 1);

            StarMapGenerator.RelaxStars(stars, null, random);

            Assert.AreEqual(0, random.Remaining, "three stars, three steps, one draw each");
            Assert.AreEqual(new[] { 2, 0 }, stars[0]);
            Assert.AreEqual(new[] { 13, 0 }, stars[1]);
            Assert.AreEqual(new[] { 40, 0 }, stars[2]);
        }

        [Test]
        public void RelaxStars_FixedStarsPullButNeverMove()
        {
            var stars = new List<int[]> { new[] { 100, 100 } };
            var fixedStars = new List<int[]> { new[] { 120, 100 } };
            StarMapGenerator.RelaxStars(stars, fixedStars, new ScriptedRandom(0));

            Assert.AreEqual(new[] { 106, 100 }, stars[0], "d^2 = 400: (2 x 100 + 120) / 3");
            Assert.AreEqual(new[] { 120, 100 }, fixedStars[0]);
        }

        [Test]
        public void RelaxStars_ResortsByX_Stably()
        {
            var stars = new List<int[]> { new[] { 30, 5 }, new[] { 0, 0 }, new[] { 30, 1 } };
            // Step 1 picks index 1, (0, 0): nearest is (30, 1) (d^2 = 901) over (30, 5)
            // (d^2 = 925): (0 + 30) / 2 = 15, (0 + 1) / 2 = 0. Steps 2-3 pick index 2 twice:
            // (30, 1) to (30, 5) d^2 = 16, no move.
            StarMapGenerator.RelaxStars(stars, null, new ScriptedRandom(1, 2, 2));

            Assert.AreEqual(new[] { 15, 0 }, stars[0]);
            Assert.AreEqual(new[] { 30, 5 }, stars[1], "equal x keep their order");
            Assert.AreEqual(new[] { 30, 1 }, stars[2]);
        }

        [Test]
        public void PresetGalaxy_IsReproducibleFromTheSeed_WithClumping()
        {
            GameSettings.Data.UseGalaxyPresets = true;
            GameSettings.Data.GalaxySizeSetting = GalaxySize.Tiny;
            GameSettings.Data.StarDensitySetting = GalaxyDensity.Packed;
            GameSettings.Data.GalaxyClumping = true;

            List<string> Run()
            {
                ServerData serverState = new ServerData();
                serverState.AllPlayers.Add(new PlayerSettings());
                serverState.AllPlayers.Add(new PlayerSettings());
                new StarMapinitializer(serverState, new Random(77)).GenerateStars();
                return serverState.AllStars.Values.Select(s => s.Name + "@" + s.Position.X + "," + s.Position.Y).OrderBy(s => s).ToList();
            }

            CollectionAssert.AreEqual(Run(), Run());
        }

        // ---------------------------------------------------------------- (3)/(4) placement

        private static void AssertAllPairsFartherThan12(IList<int[]> stars)
        {
            for (int i = 0; i < stars.Count; i++)
            {
                for (int j = i + 1; j < stars.Count; j++)
                {
                    long dx = stars[i][0] - stars[j][0];
                    long dy = stars[i][1] - stars[j][1];
                    Assert.GreaterOrEqual((dx * dx) + (dy * dy), 145, "survivors are more than 12 ly apart");
                }
            }
        }

        [Test]
        public void PresetPlacement_UsesTheFixed12LySeparation_NotTheStarSeparationSetting()
        {
            Assert.AreEqual(12, StarMapGenerator.PresetStarSeparation);

            // A 200 x 200 map with the free map's separation at 40: the wizard sweep only
            // removes candidates within 12 ly, so pairs closer than 40 ly survive.
            StarMapGenerator map = new StarMapGenerator(200, 200, 40, 60, 60, new Random(3)) { TargetStarCount = 20 };
            map.Generate(0);

            AssertAllPairsFartherThan12(map.Stars);
            bool somePairCloserThan40 = false;
            for (int i = 0; i < map.Stars.Count && !somePairCloserThan40; i++)
            {
                for (int j = i + 1; j < map.Stars.Count; j++)
                {
                    long dx = map.Stars[i][0] - map.Stars[j][0];
                    long dy = map.Stars[i][1] - map.Stars[j][1];
                    if ((dx * dx) + (dy * dy) < 1600)
                    {
                        somePairCloserThan40 = true;
                        break;
                    }
                }
            }
            Assert.IsTrue(somePairCloserThan40, "the 40 ly setting is not applied to the wizard galaxy");
        }

        [Test]
        public void PresetPlacement_TrimsRandomSurvivorsToTheCount_AndKeepsThemSortedByX()
        {
            // 32 wanted, 36 candidates on a 2,000 ly map: (almost) no collisions, so exactly 4
            // random survivors are trimmed.
            StarMapGenerator map = new StarMapGenerator(2000, 2000, 10, 60, 60, new Random(9)) { TargetStarCount = 32 };
            map.Generate(0);

            Assert.AreEqual(32, map.Stars.Count);
            AssertAllPairsFartherThan12(map.Stars);
            for (int i = 1; i < map.Stars.Count; i++)
            {
                Assert.LessOrEqual(map.Stars[i - 1][0], map.Stars[i][0], "sorted by x");
            }
        }

        [Test]
        public void PresetPlacement_LeavesFewerStarsWhenTheSweepRemovesMoreThanTheSurplus()
        {
            // 36 candidates in a 60 x 60 ly box (the 100 ly map less the edge margin) cannot all
            // be more than 12 ly apart, so the galaxy has fewer than its 32 stars.
            StarMapGenerator map = new StarMapGenerator(100, 100, 10, 60, 60, new Random(4)) { TargetStarCount = 32 };
            map.Generate(0);

            Assert.Greater(map.Stars.Count, 0);
            Assert.Less(map.Stars.Count, 32, "no retry: the sweep's losses are not made up");
            AssertAllPairsFartherThan12(map.Stars);
        }

        [Test]
        public void PresetGalaxy_PlacesAtMostTheFormulaCount_HomeWorldsIncluded_MoreThan12LyApart_WithNoSurfaceMinerals()
        {
            GameSettings.Data.UseGalaxyPresets = true;
            GameSettings.Data.GalaxySizeSetting = GalaxySize.Small;
            GameSettings.Data.StarDensitySetting = GalaxyDensity.Normal;
            GameSettings.Data.StarSeparation = 30;

            ServerData serverState = new ServerData();
            for (int i = 0; i < 3; i++)
            {
                serverState.AllPlayers.Add(new PlayerSettings());
            }

            new StarMapinitializer(serverState, new Random(5)).GenerateStars();

            Assert.AreEqual(800, GameSettings.Data.MapWidth, "the initialiser re-applies the preset");
            Assert.AreEqual(128, GameSettings.Data.NumberOfStars);

            List<Star> stars = serverState.AllStars.Values.ToList();
            Assert.That(stars.Count, Is.InRange(110, 128 - 3), "the count less the three home worlds, minus any sweep losses beyond the surplus");
            foreach (Star star in stars)
            {
                Assert.That(star.Position.X, Is.InRange(0, 799));
                Assert.That(star.Position.Y, Is.InRange(0, 799));
                Assert.AreEqual(0, star.ResourcesOnHand.Ironium, star.Name + ": ordinary planets start with no surface minerals");
                Assert.AreEqual(0, star.ResourcesOnHand.Boranium, star.Name);
                Assert.AreEqual(0, star.ResourcesOnHand.Germanium, star.Name);
            }

            AssertAllPairsFartherThan12(stars.Select(s => new[] { s.Position.X, s.Position.Y }).ToList());
            bool somePairCloserThan30 = stars.Any(a => stars.Any(b => a != b
                && ((long)(a.Position.X - b.Position.X) * (a.Position.X - b.Position.X)) + ((long)(a.Position.Y - b.Position.Y) * (a.Position.Y - b.Position.Y)) < 900));
            Assert.IsTrue(somePairCloserThan30, "the StarSeparation setting (30 here) plays no part in the wizard galaxy");
        }

        // ---------------------------------------------------------------- (5) Simplified setup

        [TestCase(GalaxySize.Tiny, 2)]
        [TestCase(GalaxySize.Small, 3)]
        [TestCase(GalaxySize.Medium, 7)]
        [TestCase(GalaxySize.Large, 12)]
        [TestCase(GalaxySize.Huge, 16)]
        public void SimplifiedPlayerCount_EasyAndStandard_AreFixed_WithNoDraw(GalaxySize size, int count)
        {
            Assert.AreEqual(count, NewGameSetup.ChooseSimplifiedPlayerCount(size, NewGameSetup.Easy, new NoDrawRandom()));
            Assert.AreEqual(count, NewGameSetup.ChooseSimplifiedPlayerCount(size, NewGameSetup.Standard, new NoDrawRandom()));
        }

        [Test]
        public void SimplifiedPlayerCount_TinyHarder_IsTwoWithNoDraw()
        {
            Assert.AreEqual(2, NewGameSetup.ChooseSimplifiedPlayerCount(GalaxySize.Tiny, NewGameSetup.Harder, new NoDrawRandom()));
        }

        // Each "1 in n" is a separate uniform 0 .. n - 1 draw hitting on 0, in the order listed;
        // the first hit decides. The scripted values are range-checked against each draw's n.
        [TestCase(GalaxySize.Small, NewGameSetup.Harder, new[] { 0 }, 4)]
        [TestCase(GalaxySize.Small, NewGameSetup.Harder, new[] { 3 }, 3)]
        [TestCase(GalaxySize.Medium, NewGameSetup.Harder, new[] { 0 }, 8)]
        [TestCase(GalaxySize.Medium, NewGameSetup.Harder, new[] { 4, 0 }, 6)]
        [TestCase(GalaxySize.Medium, NewGameSetup.Harder, new[] { 4, 4 }, 7)]
        [TestCase(GalaxySize.Large, NewGameSetup.Harder, new[] { 0 }, 13)]
        [TestCase(GalaxySize.Large, NewGameSetup.Harder, new[] { 1, 0 }, 11)]
        [TestCase(GalaxySize.Large, NewGameSetup.Harder, new[] { 4, 4 }, 12)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Harder, new[] { 0 }, 14)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Harder, new[] { 6, 0 }, 15)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Harder, new[] { 6, 4 }, 16)]
        [TestCase(GalaxySize.Tiny, NewGameSetup.Expert, new[] { 0 }, 3)]
        [TestCase(GalaxySize.Tiny, NewGameSetup.Expert, new[] { 2 }, 2)]
        [TestCase(GalaxySize.Small, NewGameSetup.Expert, new[] { 0 }, 5)]
        [TestCase(GalaxySize.Small, NewGameSetup.Expert, new[] { 3, 0 }, 4)]
        [TestCase(GalaxySize.Small, NewGameSetup.Expert, new[] { 3, 2 }, 3)]
        [TestCase(GalaxySize.Medium, NewGameSetup.Expert, new[] { 0 }, 9)]
        [TestCase(GalaxySize.Medium, NewGameSetup.Expert, new[] { 9, 0 }, 5)]
        [TestCase(GalaxySize.Medium, NewGameSetup.Expert, new[] { 9, 9, 0 }, 8)]
        [TestCase(GalaxySize.Medium, NewGameSetup.Expert, new[] { 9, 9, 3, 0 }, 6)]
        [TestCase(GalaxySize.Medium, NewGameSetup.Expert, new[] { 9, 9, 3, 3 }, 7)]
        [TestCase(GalaxySize.Large, NewGameSetup.Expert, new[] { 0, 0 }, 14)]
        [TestCase(GalaxySize.Large, NewGameSetup.Expert, new[] { 0, 1 }, 15)]
        [TestCase(GalaxySize.Large, NewGameSetup.Expert, new[] { 9, 0, 0 }, 9)]
        [TestCase(GalaxySize.Large, NewGameSetup.Expert, new[] { 9, 0, 1 }, 10)]
        [TestCase(GalaxySize.Large, NewGameSetup.Expert, new[] { 9, 9, 0 }, 13)]
        [TestCase(GalaxySize.Large, NewGameSetup.Expert, new[] { 9, 9, 3, 0 }, 11)]
        [TestCase(GalaxySize.Large, NewGameSetup.Expert, new[] { 9, 9, 3, 3 }, 12)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Expert, new[] { 0, 0 }, 11)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Expert, new[] { 0, 1 }, 12)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Expert, new[] { 0, 2 }, 13)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Expert, new[] { 9, 0 }, 14)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Expert, new[] { 9, 5, 0 }, 15)]
        [TestCase(GalaxySize.Huge, NewGameSetup.Expert, new[] { 9, 5, 3 }, 16)]
        public void SimplifiedPlayerCount_HarderAndExpert_DrawInOrder_FirstHitDecides(GalaxySize size, int difficulty, int[] draws, int count)
        {
            var random = new ScriptedRandom(draws);
            Assert.AreEqual(count, NewGameSetup.ChooseSimplifiedPlayerCount(size, difficulty, random));
            Assert.AreEqual(0, random.Remaining, "no draw after the deciding one");
        }

        [Test]
        public void SimplifiedPlayerCountRange_FollowsTheTable()
        {
            Assert.AreEqual((7, 7), NewGameSetup.SimplifiedPlayerCountRange(GalaxySize.Medium, NewGameSetup.Easy));
            Assert.AreEqual((3, 4), NewGameSetup.SimplifiedPlayerCountRange(GalaxySize.Small, NewGameSetup.Harder));
            Assert.AreEqual((9, 15), NewGameSetup.SimplifiedPlayerCountRange(GalaxySize.Large, NewGameSetup.Expert));
            Assert.AreEqual((14, 16), NewGameSetup.SimplifiedPlayerCountRange(GalaxySize.Huge, NewGameSetup.Harder));
            Assert.AreEqual((11, 16), NewGameSetup.SimplifiedPlayerCountRange(GalaxySize.Huge, NewGameSetup.Expert));
        }

        [Test]
        public void SimplifiedStartingDistance_ModerateForEasyAndStandard_FartherForHarderAndExpert()
        {
            Assert.AreEqual(StartingDistance.Moderate, NewGameSetup.SimplifiedStartingDistance(NewGameSetup.Easy));
            Assert.AreEqual(StartingDistance.Moderate, NewGameSetup.SimplifiedStartingDistance(NewGameSetup.Standard));
            Assert.AreEqual(StartingDistance.Farther, NewGameSetup.SimplifiedStartingDistance(NewGameSetup.Harder));
            Assert.AreEqual(StartingDistance.Farther, NewGameSetup.SimplifiedStartingDistance(NewGameSetup.Expert));
        }

        [Test]
        public void SimplifiedDefaults_KeepTheSize_SetNormalDensity_DistanceByDifficulty_AndClearTheFlags()
        {
            GameSettings settings = (GameSettings)Activator.CreateInstance(typeof(GameSettings), nonPublic: true);
            settings.ApplyGalaxyPreset(GalaxySize.Huge, GalaxyDensity.Packed);
            settings.StartingDistanceSetting = StartingDistance.Distant;
            settings.MaximumMinerals = true;
            settings.SlowTechAdvance = true;
            settings.AcceleratedStart = true;
            settings.NoRandomEvents = true;
            settings.GalaxyClumping = true;

            NewGameSetup.ApplySimplifiedDefaults(settings, NewGameSetup.Expert);

            Assert.IsTrue(settings.UseGalaxyPresets);
            Assert.AreEqual(GalaxySize.Huge, settings.GalaxySizeSetting, "the chosen size is kept");
            Assert.AreEqual(GalaxyDensity.Normal, settings.StarDensitySetting);
            Assert.AreEqual(800, settings.NumberOfStars, "Huge Normal");
            Assert.AreEqual(StartingDistance.Farther, settings.StartingDistanceSetting);
            Assert.IsFalse(settings.MaximumMinerals || settings.SlowTechAdvance || settings.AcceleratedStart
                || settings.NoRandomEvents || settings.GalaxyClumping);

            NewGameSetup.ApplySimplifiedDefaults(settings, NewGameSetup.Easy);
            Assert.AreEqual(StartingDistance.Moderate, settings.StartingDistanceSetting);
        }

        // ---------------------------------------------------------------- (6) second planet

        private static Race NarrowRace()
        {
            // Bands 10-14 / 20-24 / 30-34: centre (12, 22, 32), anything else far out of band.
            Race race = new Race();
            race.GravityTolerance.MinimumValue = 10;
            race.GravityTolerance.MaximumValue = 14;
            race.TemperatureTolerance.MinimumValue = 20;
            race.TemperatureTolerance.MaximumValue = 24;
            race.RadiationTolerance.MinimumValue = 30;
            race.RadiationTolerance.MaximumValue = 34;
            return race;
        }

        [Test]
        public void SecondPlanetEnvironment_AlreadyHabitable_MakesNoDraw()
        {
            Race race = NarrowRace();
            Star second = new Star { Gravity = 12, Temperature = 22, Radiation = 32 };
            Star home = new Star { Gravity = 1, Temperature = 1, Radiation = 1 };

            StarMapinitializer.RollSecondPlanetEnvironment(second, home, race, new NoDrawRandom());

            Assert.AreEqual(12, second.Gravity);
            Assert.AreEqual(12, second.OriginalGravity);
        }

        [Test]
        public void SecondPlanetEnvironment_RerollsAllThreeTo2Plus0To96_GravityTemperatureRadiation_UntilHabitabilityReaches10()
        {
            Race race = NarrowRace();
            Star second = new Star { Gravity = 1, Temperature = 1, Radiation = 1 };
            Star home = new Star { Gravity = 12, Temperature = 22, Radiation = 32 };

            // First re-roll still out of band (99, 99, 99); the second lands on the centre.
            var random = new ScriptedRandom(96, 96, 96, 10, 20, 30);
            StarMapinitializer.RollSecondPlanetEnvironment(second, home, race, random);

            Assert.AreEqual(0, random.Remaining);
            Assert.AreEqual(12, second.Gravity);
            Assert.AreEqual(22, second.Temperature);
            Assert.AreEqual(32, second.Radiation);
            Assert.AreEqual(12, second.OriginalGravity, "current and original copies alike");
            Assert.AreEqual(22, second.OriginalTemperature);
            Assert.AreEqual(32, second.OriginalRadiation);
            Assert.GreaterOrEqual(race.HabPercent(second), 10);
        }

        [Test]
        public void SecondPlanetEnvironment_AfterAHundredRerolls_CopiesTheHomeWorld()
        {
            Race race = NarrowRace();
            Star second = new Star { Gravity = 1, Temperature = 1, Radiation = 1 };
            Star home = new Star { Gravity = 12, Temperature = 22, Radiation = 32 };

            ZeroRandom random = new ZeroRandom(); // always 2 + 0: never habitable
            StarMapinitializer.RollSecondPlanetEnvironment(second, home, race, random);

            Assert.AreEqual(300, random.Draws, "100 re-rolls of three values");
            Assert.AreEqual(12, second.Gravity);
            Assert.AreEqual(22, second.Temperature);
            Assert.AreEqual(32, second.Radiation);
            Assert.AreEqual(32, second.OriginalRadiation);
        }

        [Test]
        public void SecondPlanetSurfaceStock_Is100PlusARandom0To199_NoAcceleratedBonus()
        {
            GameSettings.Data.AcceleratedStart = true;
            Assert.AreEqual(100, StarMapinitializer.RollSecondPlanetSurfaceStock(new ScriptedRandom(0)));
            Assert.AreEqual(299, StarMapinitializer.RollSecondPlanetSurfaceStock(new ScriptedRandom(199)));
        }

        private static ServerData GenerateSmallGame(int seed, Race race, string aiProgram = "Human", int aiSkill = -1)
        {
            GameSettings.Data.UseGalaxyPresets = true;
            GameSettings.Data.GalaxySizeSetting = GalaxySize.Small;
            GameSettings.Data.StarDensitySetting = GalaxyDensity.Normal;

            ServerData serverState = new ServerData();
            serverState.AllRaces.Add(race.Name, race);
            serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = 1, RaceName = race.Name, AiProgram = aiProgram, AiSkill = aiSkill });
            serverState.AllEmpires[1] = new EmpireData { Id = 1, Race = race };

            StarMapinitializer initializer = new StarMapinitializer(serverState, new Random(seed));
            initializer.GenerateStars();
            initializer.GeneratePlayerAssets();
            return serverState;
        }

        private static TestRace PrtRace(string name, string primary)
        {
            TestRace race = new TestRace { Name = name, TestAdvantagePoints = 0, LeftoverPointTarget = "Surface minerals" };
            race.Traits.SetPrimary(primary);
            return race;
        }

        [TestCase("PP", "Mass Driver Base", "Shielded Scout #3")]
        [TestCase("IT", "Stargate", "Scout #2")]
        public void SecondPlanet_HasItsOwnState(string prt, string starbaseSlotOne, string extraFleetName)
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                TestRace race = PrtRace("Second" + prt + seed, prt);
                GameSettings.Data.UseGalaxyPresets = true;
                GameSettings.Data.GalaxySizeSetting = GalaxySize.Small;
                GameSettings.Data.StarDensitySetting = GalaxyDensity.Normal;

                ServerData serverState = new ServerData();
                serverState.AllRaces.Add(race.Name, race);
                serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = 1, RaceName = race.Name, AiProgram = "Human" });
                EmpireData empire = new EmpireData { Id = 1, Race = race };
                serverState.AllEmpires[1] = empire;

                StarMapinitializer initializer = new StarMapinitializer(serverState, new Random(seed));
                initializer.GenerateStars();
                Dictionary<string, (int I, int B, int G)> rolled = serverState.AllStars.Values.ToDictionary(
                    s => s.Name, s => (s.MineralConcentration.Ironium, s.MineralConcentration.Boranium, s.MineralConcentration.Germanium));
                initializer.GeneratePlayerAssets();

                List<Star> owned = serverState.AllStars.Values.Where(s => s.Owner == 1).ToList();
                Assert.AreEqual(2, owned.Count, "seed " + seed);
                Star home = owned.Single(s => s.IsHomeWorld);
                Star second = owned.Single(s => !s.IsHomeWorld);

                // (1) Choice: in the band (15D / 100)^2 .. (23D / 100)^2 from the home world, D = 800.
                long dx = second.Position.X - home.Position.X;
                long dy = second.Position.Y - home.Position.Y;
                Assert.That((dx * dx) + (dy * dy), Is.InRange(120 * 120, 184 * 184), "seed " + seed + ": candidate band 120-184 ly");

                // (2) Environment: habitability at least 10 for the race.
                Assert.GreaterOrEqual(race.HabPercent(second), 10, "seed " + seed);
                Assert.AreEqual(second.Gravity, second.OriginalGravity);

                // (3) Ownership and installations.
                Assert.AreSame(race, second.ThisRace);
                Assert.IsFalse(second.HasArtifact);
                Assert.AreEqual(10, second.Mines, "seed " + seed);
                Assert.AreEqual(4, second.Factories);
                Assert.AreEqual(0, second.Defenses);
                Assert.AreEqual("Viewer 50", second.ScannerType, "planetary scanner type 0");
                Assert.AreEqual(50, second.ScanRange);
                Assert.AreEqual(starbaseSlotOne, second.Starbase.Composition.Values.First().Design.Name, "starbase design slot 1");

                // (4) Population: 2/5 of 250 units, the home world keeping 4/5.
                Assert.AreEqual(10000, second.Colonists);
                Assert.AreEqual(20000, home.Colonists);

                // (5) Minerals: 100-299 kT each; its own rolled concentrations, no floor of 30.
                foreach (int stock in new[] { second.ResourcesOnHand.Ironium, second.ResourcesOnHand.Boranium, second.ResourcesOnHand.Germanium })
                {
                    Assert.That(stock, Is.InRange(100, 299), "seed " + seed);
                }
                Assert.AreEqual(rolled[second.Name], (second.MineralConcentration.Ironium, second.MineralConcentration.Boranium, second.MineralConcentration.Germanium),
                    "seed " + seed + ": the concentrations GenerateStars rolled, untouched");

                // (6) Not a home world. (7) One ship of design slot 0 as a fleet of its own.
                Assert.IsFalse(second.IsHomeWorld);
                List<Fleet> shipsAtSecond = empire.OwnedFleets.Values.Where(f => f.Type != ItemType.Starbase && f.InOrbit == second).ToList();
                Assert.AreEqual(1, shipsAtSecond.Count);
                Assert.AreEqual(extraFleetName, shipsAtSecond[0].Name);
                Assert.AreSame(StarMapinitializer.SlotZeroShipDesign(empire), shipsAtSecond[0].Composition.Values.Single().Design);
                Assert.AreEqual(1, shipsAtSecond[0].Composition.Values.Single().Quantity);
            }
        }

        [Test]
        public void SecondPlanet_KeepsLowConcentrations_WhereTheHomeWorldIsFloored()
        {
            // Over a few seeds at least one second planet has a concentration under 30 (two
            // planets in three do), proving the home-world floor is not applied to it.
            bool sawLow = false;
            for (int seed = 1; seed <= 12 && !sawLow; seed++)
            {
                ServerData serverState = GenerateSmallGame(seed, PrtRace("Low" + seed, "PP"));
                Star second = serverState.AllStars.Values.Single(s => s.Owner == 1 && !s.IsHomeWorld);
                Star home = serverState.AllStars.Values.Single(s => s.Owner == 1 && s.IsHomeWorld);
                Assert.GreaterOrEqual(Math.Min(home.MineralConcentration.Ironium, Math.Min(home.MineralConcentration.Boranium, home.MineralConcentration.Germanium)), 30);
                sawLow = Math.Min(second.MineralConcentration.Ironium, Math.Min(second.MineralConcentration.Boranium, second.MineralConcentration.Germanium)) < 30;
            }

            Assert.IsTrue(sawLow);
        }

        // ---------------------------------------------------------------- (7) Expert +10%

        [Test]
        public void StartingPopulation_ExpertComputerPlayer_GetsATenthMore_BeforeAcceleratedBbs()
        {
            Race race = new Race { GrowthRate = 15 };
            Assert.AreEqual(25000, race.GetStartingPopulation(expertComputerPlayer: false));
            Assert.AreEqual(27500, race.GetStartingPopulation(expertComputerPlayer: true), "250 + 25 units");

            race.Traits.Add("LSP");
            Assert.AreEqual(19200, race.GetStartingPopulation(expertComputerPlayer: true), "175 + 17 units, truncated");

            GameSettings.Data.AcceleratedStart = true;
            Assert.AreEqual(76800, race.GetStartingPopulation(expertComputerPlayer: true), "192 x 40 / 10 = 768 units: the tenth comes before the multiplier");
            race.Traits.Remove("LSP");
            Assert.AreEqual(110000, race.GetStartingPopulation(expertComputerPlayer: true), "275 x 4");
        }

        [Test]
        public void SplitStartingPopulation_UsesTheExpertBonus()
        {
            Race race = new Race { GrowthRate = 15 };
            race.Traits.SetPrimary("PP");
            Star home = new Star();
            Star second = new Star();

            StarMapinitializer.SplitStartingPopulation(home, second, race, expertComputerPlayer: true);

            Assert.AreEqual(11000, second.Colonists, "275 x 2 / 5 = 110 units");
            Assert.AreEqual(22000, home.Colonists, "275 x 4 / 5 = 220 units");
        }

        [TestCase("Nova.Ai.DefaultAi", 3, 27500, "an Expert computer player")]
        [TestCase("Nova.Ai.DefaultAi", 2, 25000, "a Tough computer player gets nothing")]
        [TestCase("Nova.Ai.DefaultAi", -1, 25000, "no tier recorded")]
        [TestCase("Human", 3, 25000, "a human with a tier recorded is still human")]
        public void GeneratedHomeWorld_ExpertComputerPlayersStartWithTenPercentMore(string aiProgram, int aiSkill, int colonists, string why)
        {
            TestRace race = new TestRace { Name = "Tier" + aiSkill + aiProgram.Length, TestAdvantagePoints = 0, LeftoverPointTarget = "Mines" };
            race.Traits.SetPrimary("JOAT");
            ServerData serverState = GenerateSmallGame(21, race, aiProgram, aiSkill);

            Star home = serverState.AllStars.Values.Single(s => s.Owner == 1);
            Assert.AreEqual(colonists, home.Colonists, why);
        }
    }
}
