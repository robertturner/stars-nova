namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.RaceDefinition;
    using Nova.Server;
    using Nova.Server.NewGame;

    /// <summary>
    /// docs/behavior-specs-10/new-game-setup.md section 3 (home-world template, ordinary-planet
    /// concentration steps), section 5b (10 mines / factories / defences, the Alternate Reality
    /// home world's missing scanner), "Starting population, exact order", and the small
    /// starting-tech / name-pool fixes from race-traits.md.
    /// </summary>
    [TestFixture]
    public class NewGameSetupSpec10Test
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

        /// <summary>Always draws the largest value allowed.</summary>
        private class MaxRandom : Random
        {
            public override int Next(int minValue, int maxValue)
            {
                return maxValue - 1;
            }

            public override int Next(int maxValue)
            {
                return maxValue - 1;
            }
        }

        private bool originalAcceleratedStart;

        [SetUp]
        public void Init()
        {
            originalAcceleratedStart = GameSettings.Data.AcceleratedStart;
            GameSettings.Data.AcceleratedStart = false;
            GameSettings.Data.MapHeight = 400;
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.StarDensity = 60;
            GameSettings.Data.StarSeparation = 10;
            GameSettings.Data.StarUniformity = 60;
        }

        [TearDown]
        public void Cleanup()
        {
            GameSettings.Data.AcceleratedStart = originalAcceleratedStart;
        }

        // ---------------------------------------------------------------- ordinary planets

        [TestCase(26, 0)]
        [TestCase(18, 0)]
        [TestCase(17, 1)]
        [TestCase(9, 1)]
        [TestCase(8, 1)]
        [TestCase(7, 1)]
        [TestCase(6, 2)]
        [TestCase(3, 2)]
        [TestCase(2, 3)]
        [TestCase(1, 3)]
        [TestCase(0, 4)]
        public void LowConcentrationRoll_ReplacementCounts(int roll, int replacements)
        {
            Assert.AreEqual(replacements, StarMapinitializer.LowConcentrationReplacements(roll));
        }

        [Test]
        public void Concentrations_AreTwoRollsPlus31_InIroniumBoraniumGermaniumOrder()
        {
            Star star = new Star { Radiation = 50 };
            var random = new ScriptedRandom(0, 0, 44, 44, 10, 10, /* low roll */ 26);
            StarMapinitializer.RollMineralConcentrations(star, random);

            Assert.AreEqual(31, star.MineralConcentration.Ironium);
            Assert.AreEqual(119, star.MineralConcentration.Boranium);
            Assert.AreEqual(51, star.MineralConcentration.Germanium);
            Assert.AreEqual(0, random.Remaining);
        }

        [Test]
        public void HighRadiation_RaisesEachConcentrationByHalfOfARandomUpTo98MinusC()
        {
            Star star = new Star { Radiation = 90 };
            var random = new ScriptedRandom(
                0, 0,      // Ironium 31
                44, 44,    // Boranium 119 - at 99 or more, no draw and no raise
                10, 10,    // Germanium 51
                67,        // Ironium: random 0..67 -> 67 / 2 = +33
                47,        // Germanium: random 0..47 -> 47 / 2 = +23
                18);       // low roll: nothing
            StarMapinitializer.RollMineralConcentrations(star, random);

            Assert.AreEqual(64, star.MineralConcentration.Ironium);
            Assert.AreEqual(119, star.MineralConcentration.Boranium);
            Assert.AreEqual(74, star.MineralConcentration.Germanium);
            Assert.AreEqual(0, random.Remaining);
        }

        [Test]
        public void AcceleratedBbs_AddsFiveToEachConcentrationUnder40()
        {
            GameSettings.Data.AcceleratedStart = true;
            Star star = new Star { Radiation = 10 };
            var random = new ScriptedRandom(0, 0, 4, 4, 44, 44, 26);
            StarMapinitializer.RollMineralConcentrations(star, random);

            Assert.AreEqual(36, star.MineralConcentration.Ironium, "31 -> 36");
            Assert.AreEqual(44, star.MineralConcentration.Boranium, "39 -> 44");
            Assert.AreEqual(119, star.MineralConcentration.Germanium, "40 or more: unchanged");
        }

        [Test]
        public void LowConcentrationRollOfZero_ReplacesFourTimesWith1To30_MineralsMayRepeat()
        {
            Star star = new Star { Radiation = 10 };
            var random = new ScriptedRandom(
                20, 20, 20, 20, 20, 20, // 71 each
                0,                      // four replacements
                0, 5,                   // Ironium -> 5
                1, 6,                   // Boranium -> 6
                2, 1,                   // Germanium -> 1
                2, 30);                 // Germanium again -> 30
            StarMapinitializer.RollMineralConcentrations(star, random);

            Assert.AreEqual(5, star.MineralConcentration.Ironium);
            Assert.AreEqual(6, star.MineralConcentration.Boranium);
            Assert.AreEqual(30, star.MineralConcentration.Germanium);
            Assert.AreEqual(0, random.Remaining);
        }

        [Test]
        public void GenerateStars_OrdinaryConcentrations_Are1To119_AndAboutTwoInThreePlanetsHaveOneAtMost30()
        {
            ServerData serverState = new ServerData();
            for (int i = 0; i < 4; i++)
            {
                serverState.AllPlayers.Add(new PlayerSettings());
            }
            new StarMapinitializer(serverState, new Random(999)).GenerateStars();

            int withLow = 0;
            foreach (Star star in serverState.AllStars.Values)
            {
                int[] c = { star.MineralConcentration.Ironium, star.MineralConcentration.Boranium, star.MineralConcentration.Germanium };
                foreach (int value in c)
                {
                    Assert.That(value, Is.InRange(1, 119));
                }
                if (c.Any(value => value <= 30))
                {
                    withLow++;
                }
            }

            double share = (double)withLow / serverState.AllStars.Count;
            Assert.Greater(serverState.AllStars.Count, 20, "test setup");
            Assert.That(share, Is.InRange(0.4, 0.9), "18 of 27 low-concentration rolls replace at least one mineral");
        }

        // ---------------------------------------------------------------- home-world template

        [Test]
        public void HomeSurfaceStock_TopsUpUnder200()
        {
            // random 0..499 -> 0, +10 = 10 (< 200) -> + 155 + 149 = 314
            Assert.AreEqual(314, StarMapinitializer.RollHomeSurfaceStock(50, new ScriptedRandom(0, 149)));
            // 499 + 10 = 509, no top-up
            Assert.AreEqual(509, StarMapinitializer.RollHomeSurfaceStock(50, new ScriptedRandom(499)));
        }

        [Test]
        public void HomeSurfaceStock_AcceleratedBbs_AddsAQuarter()
        {
            GameSettings.Data.AcceleratedStart = true;
            Assert.AreEqual(314 + 78, StarMapinitializer.RollHomeSurfaceStock(50, new ScriptedRandom(0, 149)));
        }

        [Test]
        public void HomeWorldTemplate_FloorsEachConcentrationAt30_AndCopiesTheSurfaceStocks()
        {
            Star star = new Star();
            StarMapinitializer.ApplyHomeWorldTemplate(star, new Resources(5, 30, 119, 0), new Resources(300, 250, 400, 0));

            Assert.AreEqual(30, star.MineralConcentration.Ironium);
            Assert.AreEqual(30, star.MineralConcentration.Boranium);
            Assert.AreEqual(119, star.MineralConcentration.Germanium);
            Assert.AreEqual(300, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(250, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(400, star.ResourcesOnHand.Germanium);
        }

        private static ServerData Generate(int seed, params (Race Race, string AiProgram)[] players)
        {
            ServerData serverState = new ServerData();
            ushort id = 1;
            foreach (var player in players)
            {
                serverState.AllRaces.Add(player.Race.Name, player.Race);
                serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = id, RaceName = player.Race.Name, AiProgram = player.AiProgram });

                EmpireData empire = new EmpireData { Id = id, Race = player.Race };
                serverState.AllEmpires[empire.Id] = empire;
                id++;
            }

            StarMapinitializer initializer = new StarMapinitializer(serverState, new Random(seed));
            initializer.GenerateStars();
            initializer.GeneratePlayerAssets();
            return serverState;
        }

        private static List<Star> OwnedStars(ServerData serverState, int owner)
        {
            return serverState.AllStars.Values.Where(star => star.Owner == owner).ToList();
        }

        private static TestRace NamedRace(string name, string primary = "JOAT", string target = "Mines")
        {
            TestRace race = new TestRace { Name = name, TestAdvantagePoints = 0, LeftoverPointTarget = target };
            race.Traits.SetPrimary(primary);
            return race;
        }

        [Test]
        public void EveryHomeWorld_StartsWithTenMinesFactoriesAndDefences_AndTheSameTemplate()
        {
            for (int seed = 1; seed <= 5; seed++)
            {
                ServerData serverState = Generate(seed, (NamedRace("A" + seed), "Human"), (NamedRace("B" + seed), "Human"));
                Star a = OwnedStars(serverState, 1).Single();
                Star b = OwnedStars(serverState, 2).Single();

                foreach (Star home in new[] { a, b })
                {
                    Assert.AreEqual(10, home.Mines);
                    Assert.AreEqual(10, home.Factories);
                    Assert.AreEqual(10, home.Defenses, "defences were never set before");
                    foreach (int c in new[] { home.MineralConcentration.Ironium, home.MineralConcentration.Boranium, home.MineralConcentration.Germanium })
                    {
                        Assert.That(c, Is.InRange(30, 119), "template 1-119 floored at 30; no 100-299 roll");
                    }
                    foreach (int s in new[] { home.ResourcesOnHand.Ironium, home.ResourcesOnHand.Boranium, home.ResourcesOnHand.Germanium })
                    {
                        Assert.That(s, Is.InRange(165, 1199), "rand(0..10c-1) + 10, topped up by 155-304 under 200");
                    }
                }

                Assert.AreEqual(a.MineralConcentration.Ironium, b.MineralConcentration.Ironium, "every home world copies one template");
                Assert.AreEqual(a.MineralConcentration.Boranium, b.MineralConcentration.Boranium);
                Assert.AreEqual(a.MineralConcentration.Germanium, b.MineralConcentration.Germanium);
                Assert.AreEqual(a.ResourcesOnHand.Ironium, b.ResourcesOnHand.Ironium);
                Assert.AreEqual(a.ResourcesOnHand.Boranium, b.ResourcesOnHand.Boranium);
                Assert.AreEqual(a.ResourcesOnHand.Germanium, b.ResourcesOnHand.Germanium);
            }
        }

        [Test]
        public void ComputerPlayer_GetsFiftyLeftoverPoints_HumanGetsItsOwn()
        {
            ServerData serverState = Generate(7, (NamedRace("Human1"), "Human"), (NamedRace("Robot1"), "Nova.Ai.DefaultAi"));

            Assert.AreEqual(10, OwnedStars(serverState, 1).Single().Mines, "0 leftover points");
            Assert.AreEqual(35, OwnedStars(serverState, 2).Single().Mines, "computer players always count 50");
        }

        [Test]
        public void AlternateRealityHomeWorld_HasNoPlanetaryScanner_AndNoInstallations()
        {
            ServerData serverState = Generate(3, (NamedRace("Arish", "AR", "Mines"), "Human"));
            Star home = OwnedStars(serverState, 1).Single();

            Assert.AreEqual("None", home.ScannerType);
            Assert.AreEqual(0, home.Mines);
            Assert.AreEqual(0, home.Factories);
            Assert.AreEqual(0, home.Defenses);
        }

        // ---------------------------------------------------------------- starting population

        [Test]
        public void StartingPopulation_LowStartingPopulationSetsTheBase_AcceleratedBbsMultiplies()
        {
            GameSettings.Data.AcceleratedStart = true;
            Race race = new Race { GrowthRate = 15 };
            race.Traits.Add("LSP");
            Assert.AreEqual(70000, race.GetStartingPopulation(), "175 x 40 / 10 = 700 units");

            GameSettings.Data.AcceleratedStart = false;
            Assert.AreEqual(17500, race.GetStartingPopulation());
        }

        [Test]
        public void StartingPopulation_HyperExpansionDoublesTheGrowthRateForAcceleratedBbs()
        {
            GameSettings.Data.AcceleratedStart = true;
            Race race = new Race { GrowthRate = 10 };
            race.Traits.SetPrimary("HE");
            Assert.AreEqual(125000, race.GetStartingPopulation(), "g = 20: 250 x 50 / 10");

            race.Traits.Add("LSP");
            Assert.AreEqual(87500, race.GetStartingPopulation(), "the spec's own example: 875 units");
        }

        [Test]
        public void StartingPopulation_TruncatesInUnitsOf100()
        {
            GameSettings.Data.AcceleratedStart = true;
            Race race = new Race { GrowthRate = 1 };
            race.Traits.Add("LSP");
            // 175 x 12 / 10 = 210 units exactly; 13% growth: 175 x 36 / 10 = 630
            Assert.AreEqual(21000, race.GetStartingPopulation());
            race.GrowthRate = 13;
            Assert.AreEqual(63000, race.GetStartingPopulation());
            race.GrowthRate = 2;
            // 175 x 14 / 10 = 245 units
            Assert.AreEqual(24500, race.GetStartingPopulation());
            race.Traits.Remove("LSP");
            race.GrowthRate = 3;
            // 250 x 16 / 10 = 400
            Assert.AreEqual(40000, race.GetStartingPopulation());
        }

        [Test]
        public void SecondHomePlanet_GetsTwoFifths_AndTheHomePlanetKeepsFourFifths()
        {
            Race race = new Race { GrowthRate = 15 };
            race.Traits.SetPrimary("PP");
            race.Traits.Add("LSP");
            GameSettings.Data.AcceleratedStart = true;

            Star home = new Star();
            Star second = new Star();
            StarMapinitializer.SplitStartingPopulation(home, second, race);

            Assert.AreEqual(28000, second.Colonists, "the spec's own example: 280 units");
            Assert.AreEqual(56000, home.Colonists, "560 units");
        }

        [Test]
        public void PacketPhysics_GeneratedGame_SplitsThePopulationAcrossItsTwoPlanets()
        {
            Race race = NamedRace("Packeteer", "PP", "Surface minerals");
            race.GrowthRate = 15;
            // The second home planet needs a non-Tiny galaxy (galaxy-size index >= 1).
            GameSettings.Data.MapWidth = 800;
            GameSettings.Data.MapHeight = 800;
            ServerData serverState = Generate(11, (race, "Human"));

            List<int> populations = OwnedStars(serverState, 1).Select(star => star.Colonists).OrderBy(p => p).ToList();
            CollectionAssert.AreEqual(new[] { 10000, 20000 }, populations, "250 units: 100 and 200");
        }

        // ---------------------------------------------------------------- starting tech

        private static EmpireData Empire(string primary, params string[] lesser)
        {
            Race race = new Race();
            race.Traits.SetPrimary(primary);
            foreach (string trait in lesser)
            {
                race.Traits.Add(trait);
            }
            EmpireData empire = new EmpireData { Race = race };
            Gameinitializer.ProcessPrimaryTraits(empire);
            Gameinitializer.ProcessSecondaryTraits(empire);
            return empire;
        }

        [Test]
        public void PacketPhysics_StartsAtEnergy4()
        {
            Assert.AreEqual(4, Empire("PP").ResearchLevels[TechLevel.ResearchField.Energy]);
        }

        [Test]
        public void CheapEngines_AddsOneStartingPropulsion_LikeImprovedFuelEfficiency()
        {
            Assert.AreEqual(1, Empire("HE", "CE").ResearchLevels[TechLevel.ResearchField.Propulsion]);
            Assert.AreEqual(1, Empire("HE", "IFE").ResearchLevels[TechLevel.ResearchField.Propulsion]);
            Assert.AreEqual(2, Empire("HE", "IFE", "CE").ResearchLevels[TechLevel.ResearchField.Propulsion]);
            Assert.AreEqual(6, Empire("IT", "CE").ResearchLevels[TechLevel.ResearchField.Propulsion]);
        }

        // ---------------------------------------------------------------- star names

        [Test]
        public void NextStarName_CanDrawTheLastNameInThePool()
        {
            NameGenerator names = new NameGenerator(new MaxRandom());
            Assert.AreEqual("Zulu", names.NextStarName);
        }
    }
}
