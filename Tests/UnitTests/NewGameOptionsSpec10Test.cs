namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;
    using Nova.Server.NewGame;

    /// <summary>
    /// docs/behavior-specs-10/new-game-setup.md: the wizard's galaxy options (section 3: diameter
    /// (size + 1) x 400, star count d^2 / 5000 with the density quarters and the 999 cap, the
    /// candidate rejection sampling, the Galaxy Clumping relaxation pass), "Beginner: Maximum
    /// Minerals" (section 1 option table), the single-human default relation (option bit 0x04),
    /// the starting fleet's slot-0 / galaxy-size rules, the Alternate Reality starbase slots, the
    /// Advanced Remote Mining Potato Bugs (section 5a) and the starting tech-upgrade pass (5b).
    /// </summary>
    [TestFixture]
    public class NewGameOptionsSpec10Test
    {
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

        private int mapWidth;
        private int mapHeight;
        private int numberOfStars;
        private int starSeparation;
        private bool useGalaxyPresets;
        private GalaxySize galaxySize;
        private GalaxyDensity galaxyDensity;
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
            useGalaxyPresets = settings.UseGalaxyPresets;
            galaxySize = settings.GalaxySizeSetting;
            galaxyDensity = settings.StarDensitySetting;
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
            settings.UseGalaxyPresets = useGalaxyPresets;
            settings.GalaxySizeSetting = galaxySize;
            settings.StarDensitySetting = galaxyDensity;
            settings.MaximumMinerals = maximumMinerals;
            settings.GalaxyClumping = galaxyClumping;
            settings.AcceleratedStart = acceleratedStart;
        }

        // ---------------------------------------------------------------- galaxy size and density

        [TestCase(GalaxySize.Tiny, 400)]
        [TestCase(GalaxySize.Small, 800)]
        [TestCase(GalaxySize.Medium, 1200)]
        [TestCase(GalaxySize.Large, 1600)]
        [TestCase(GalaxySize.Huge, 2000)]
        public void GalaxyDiameter_Is400TimesSizePlusOne(GalaxySize size, int diameter)
        {
            Assert.AreEqual(diameter, GameSettings.GalaxyDiameter(size));
        }

        // The full star-count table (every density, Sparse subtracting a quarter) is
        // NewGameSetupSpec11Test.PresetStarCount_SpecElevenTable.

        [Test]
        public void ApplyGalaxyPreset_MakesASquareMapOfTheDiameter_WithThePresetStarCount()
        {
            GameSettings.Data.ApplyGalaxyPreset(GalaxySize.Medium, GalaxyDensity.Dense);

            Assert.IsTrue(GameSettings.Data.UseGalaxyPresets);
            Assert.AreEqual(1200, GameSettings.Data.MapWidth);
            Assert.AreEqual(1200, GameSettings.Data.MapHeight);
            Assert.AreEqual(360, GameSettings.Data.NumberOfStars);
            Assert.AreEqual(2, GameSettings.Data.GalaxySizeIndex);
        }

        [TestCase(300, 0)]
        [TestCase(400, 0)]
        [TestCase(799, 0)]
        [TestCase(800, 1)]
        [TestCase(2000, 4)]
        [TestCase(9999, 4)]
        public void GalaxySizeIndex_WithoutPresets_IsRecoveredFromTheMapWidth(int width, int index)
        {
            GameSettings.Data.MapWidth = width;
            Assert.AreEqual(index, GameSettings.Data.GalaxySizeIndex);
        }

        [Test]
        public void StartingDistance_DefaultsToModerate_TheSimplifiedDialogsStandardValue()
        {
            // behavior-specs-11/new-game-setup.md section 2 (the "reset value, index 2" of the
            // earlier section 8 reading is retracted).
            Assert.AreEqual(StartingDistance.Moderate, new GameSettingsProbe().StartingDistance);
        }

        /// <summary>GameSettings has a private constructor; read the field initializer's value by reflection.</summary>
        private class GameSettingsProbe
        {
            public StartingDistance StartingDistance
            {
                get
                {
                    var ctor = typeof(GameSettings).GetConstructor(
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null);
                    GameSettings fresh = (GameSettings)ctor.Invoke(null);
                    return fresh.StartingDistanceSetting;
                }
            }
        }

        // Placement (the fixed 12 ly sweep, the x-sort and the random trimming) is covered by
        // NewGameSetupSpec11Test.

        [Test]
        public void PresetGalaxy_IsReproducibleFromTheSeed()
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

        // ---------------------------------------------------------------- Galaxy Clumping

        // The relaxation pass (random picks with replacement, the exact bands 144 / 324 / 625 /
        // 1,600 and the corrected weights) is NewGameSetupSpec11Test's RelaxStars tests; the
        // spec-10 reading (walk in order, inverted weights, "~" bounds, no pull past 40 ly) is
        // superseded.

        // ---------------------------------------------------------------- Maximum Minerals

        [Test]
        public void MaximumMinerals_EveryConcentrationIs100_WithNoRaiseOrLowRoll()
        {
            GameSettings.Data.MaximumMinerals = true;
            GameSettings.Data.AcceleratedStart = true;

            Star star = new Star { Radiation = 95 };
            StarMapinitializer.RollMineralConcentrations(star, new NoDrawRandom());

            Assert.AreEqual(100, star.MineralConcentration.Ironium);
            Assert.AreEqual(100, star.MineralConcentration.Boranium);
            Assert.AreEqual(100, star.MineralConcentration.Germanium);
        }

        [Test]
        public void MaximumMinerals_GeneratedGame_HomeWorldsAndPlanetsAllAt100()
        {
            GameSettings.Data.MaximumMinerals = true;

            ServerData serverState = GenerateGame(3, ("Miner", "JOAT", new string[0]));

            Assert.Greater(serverState.AllStars.Count, 1);
            foreach (Star star in serverState.AllStars.Values)
            {
                Assert.AreEqual(100, star.MineralConcentration.Ironium, star.Name);
                Assert.AreEqual(100, star.MineralConcentration.Boranium, star.Name);
                Assert.AreEqual(100, star.MineralConcentration.Germanium, star.Name);
            }
        }

        // ---------------------------------------------------------------- default relations

        [Test]
        public void DefaultRelation_ExactlyOneHuman_IsEnemyForEveryPair()
        {
            var players = new List<PlayerSettings>
            {
                new PlayerSettings { PlayerNumber = 1, AiProgram = "Human" },
                new PlayerSettings { PlayerNumber = 2, AiProgram = "Nova.Ai.DefaultAi" },
                new PlayerSettings { PlayerNumber = 3, AiProgram = "Nova.Ai.DefaultAi" },
            };

            Assert.AreEqual(PlayerRelation.Enemy, Gameinitializer.DefaultRelation(players));
        }

        [Test]
        public void DefaultRelation_TwoHumansOrNone_StaysNeutral()
        {
            var twoHumans = new List<PlayerSettings>
            {
                new PlayerSettings { PlayerNumber = 1, AiProgram = "Human" },
                new PlayerSettings { PlayerNumber = 2 },
                new PlayerSettings { PlayerNumber = 3, AiProgram = "Nova.Ai.DefaultAi" },
            };
            var noHumans = new List<PlayerSettings>
            {
                new PlayerSettings { PlayerNumber = 1, AiProgram = "Nova.Ai.DefaultAi" },
                new PlayerSettings { PlayerNumber = 2, AiProgram = "Nova.Ai.DefaultAi" },
            };

            Assert.AreEqual(PlayerRelation.Neutral, Gameinitializer.DefaultRelation(twoHumans), "a null AiProgram counts as human");
            Assert.AreEqual(PlayerRelation.Neutral, Gameinitializer.DefaultRelation(noHumans));
        }

        // ---------------------------------------------------------------- second home planet and slot 0

        [TestCase("PP", 0, false)]
        [TestCase("PP", 1, true)]
        [TestCase("IT", 0, false)]
        [TestCase("IT", 4, true)]
        [TestCase("JOAT", 4, false)]
        public void SecondHomePlanet_OnlyPacketPhysicsAndInterstellarTraveler_OnANonTinyGalaxy(string prt, int sizeIndex, bool expected)
        {
            Race race = new Race();
            race.Traits.SetPrimary(prt);
            Assert.AreEqual(expected, StarMapinitializer.HasSecondHomePlanet(race, sizeIndex));
        }

        [Test]
        public void InterstellarTraveler_TinyGalaxy_HasOnePlanet()
        {
            ServerData serverState = GenerateGame(4, ("Traveller", "IT", new string[0]));
            Assert.AreEqual(1, serverState.AllStars.Values.Count(s => s.Owner == 1));
        }

        [TestCase("IT", "Scout")]
        [TestCase("PP", "Shielded Scout")]
        public void SecondHomePlanet_GetsOneShipOfDesignSlotZero_TheFirstScout(string prt, string slotZeroName)
        {
            GameSettings.Data.MapWidth = 800;
            GameSettings.Data.MapHeight = 800;

            ServerData serverState = GenerateGame(4, ("Traveller", prt, new string[0]));
            EmpireData empire = serverState.AllEmpires[1];

            Assert.AreEqual(slotZeroName, StarMapinitializer.SlotZeroShipDesign(empire).Name);

            List<Star> owned = serverState.AllStars.Values.Where(s => s.Owner == 1).ToList();
            Assert.AreEqual(2, owned.Count);
            Star second = owned.Single(s => s.Starbase.Composition.Values.First().Design.Name != "Starbase");

            List<Fleet> shipsAtSecond = empire.OwnedFleets.Values
                .Where(f => f.Type != ItemType.Starbase && f.InOrbit == second)
                .ToList();
            Assert.AreEqual(1, shipsAtSecond.Count, "exactly one extra ship at the second planet");
            Assert.AreEqual(slotZeroName, shipsAtSecond[0].Composition.Values.First().Design.Name);
        }

        [TestCase("HE", "Armed Scout")]
        [TestCase("WM", "Armed Scout")]
        [TestCase("JOAT", "Scout")]
        [TestCase("SD", "Scout")]
        public void DesignSlotZero_IsAlwaysTheFirstScout_NeverTheColonizer(string prt, string slotZeroName)
        {
            ServerData serverState = GenerateGame(4, ("Somebody", prt, new string[0]));
            Assert.AreEqual(slotZeroName, StarMapinitializer.SlotZeroShipDesign(serverState.AllEmpires[1]).Name);
        }

        // ---------------------------------------------------------------- Alternate Reality starbases

        [Test]
        public void AlternateReality_StarbaseSlot0IsTheStarterColony_Slot1TheHomeworldStarbase()
        {
            ServerData serverState = GenerateGame(4, ("Realist", "AR", new string[0]));
            EmpireData empire = serverState.AllEmpires[1];

            List<ShipDesign> starbases = empire.Designs.Values
                .Where(d => d.Type == ItemType.Starbase)
                .OrderBy(d => d.Key)
                .ToList();

            Assert.AreEqual(StarterColony.DesignName, starbases[0].Name, "slot 0");
            Assert.AreEqual("Orbital Fort", starbases[0].Blueprint.Name);
            Assert.IsTrue(starbases[0].Hull.Modules.All(m => m.AllocatedComponent == null || m.ComponentCount == 0), "a bare Orbital Fort");
            Assert.AreEqual("Starbase", starbases[1].Name, "slot 1");
            Assert.AreEqual("Space Station", starbases[1].Blueprint.Name);
            Assert.AreEqual(1, starbases.Count(d => d.Name == StarterColony.DesignName), "not duplicated");

            Star home = serverState.AllStars.Values.Single(s => s.Owner == 1);
            Assert.AreEqual("Starbase", home.Starbase.Composition.Values.First().Design.Name, "the homeworld uses slot 1");

            Assert.AreSame(starbases[0], StarterColony.EnsureDesign(empire), "a won colony installs the same slot-0 design");
        }

        [Test]
        public void NonAlternateReality_HasNoStarterColony()
        {
            ServerData serverState = GenerateGame(4, ("Ordinary", "JOAT", new string[0]));
            Assert.IsFalse(serverState.AllEmpires[1].Designs.Values.Any(d => d.Name == StarterColony.DesignName));
        }

        // ---------------------------------------------------------------- Potato Bugs

        [Test]
        public void AdvancedRemoteMining_WithoutOnlyBasic_StartsWithTwoPotatoBugs()
        {
            ServerData serverState = GenerateGame(4, ("Digger", "IS", new[] { "ARM" }));
            EmpireData empire = serverState.AllEmpires[1];

            List<Fleet> bugs = empire.OwnedFleets.Values.Where(f => f.Name.StartsWith("Potato Bug #")).ToList();
            Assert.AreEqual(2, bugs.Count);
            ShipDesign design = bugs[0].Composition.Values.First().Design;
            Assert.AreSame(design, bugs[1].Composition.Values.First().Design, "one design, two ships");
            Assert.AreEqual(1, bugs[0].Composition.Values.First().Quantity);
            Assert.AreEqual("Midget Miner", design.Blueprint.Name);

            HullModule robots = design.Hull.Modules.Single(m => m.ComponentType == "Mining Robot");
            Assert.AreEqual(2, robots.ComponentCount);
            Assert.AreEqual("Robo-Midget Miner", robots.AllocatedComponent.Name, "no AvailableComponents, so no upgrade");
        }

        [TestCase("ARM,OBRM")]
        [TestCase("")]
        public void NoPotatoBugs_WithOnlyBasicRemoteMining_OrWithoutAdvanced(string lesserTraits)
        {
            string[] lesser = lesserTraits.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            ServerData serverState = GenerateGame(4, ("Digger", "IS", lesser));
            EmpireData empire = serverState.AllEmpires[1];

            Assert.IsFalse(empire.OwnedFleets.Values.Any(f => f.Name.StartsWith("Potato Bug")));
            Assert.IsFalse(empire.Designs.Values.Any(d => d.Name == StarMapinitializer.PotatoBugDesignName));
        }

        // ---------------------------------------------------------------- starting tech upgrade

        private static ShipDesign Design(string hullName, params (string Type, string Component)[] parts)
        {
            AllComponents components = new AllComponents();
            ShipDesign design = new ShipDesign(1);
            design.Blueprint = components.Fetch(hullName);
            design.Type = ItemType.Ship;
            design.Name = hullName;
            foreach (var part in parts)
            {
                HullModule module = design.Hull.Modules.First(m => m.ComponentType == part.Type && m.AllocatedComponent == null);
                module.AllocatedComponent = components.Fetch(part.Component);
                module.ComponentCount = 1;
            }
            return design;
        }

        private static EmpireData EmpireWith(Race race, params string[] available)
        {
            EmpireData empire = new EmpireData { Id = 1, Race = race };
            empire.AvailableComponents = new RaceComponents();
            foreach (string name in available)
            {
                empire.AvailableComponents.Add(name);
            }
            return empire;
        }

        [Test]
        public void TechUpgrade_TakesTheFirstBuildableCandidate_KeepsTheQuantity()
        {
            EmpireData empire = EmpireWith(new Race(), "Long Hump 6", "Daddy Long Legs 7", "Mole Scanner", "Rhino Scanner");
            ShipDesign scout = Design("Scout", ("Engine", "Quick Jump 5"), ("Scanner", "Bat Scanner"));
            empire.Designs[scout.Key] = scout;

            StarMapinitializer.ApplyStartingTechUpgrades(empire);

            Assert.AreEqual("Daddy Long Legs 7", scout.Hull.Modules.Single(m => m.ComponentType == "Engine").AllocatedComponent.Name, "Daddy Long Legs 7 is tried before Long Hump 6");
            HullModule scanner = scout.Hull.Modules.Single(m => m.ComponentType == "Scanner");
            Assert.AreEqual("Mole Scanner", scanner.AllocatedComponent.Name, "Mole before Rhino");
            Assert.AreEqual(1, scanner.ComponentCount);
        }

        [Test]
        public void TechUpgrade_NoCandidateBuildable_KeepsTheOriginalPart()
        {
            EmpireData empire = EmpireWith(new Race());
            ShipDesign scout = Design("Scout", ("Engine", "Quick Jump 5"), ("Scanner", "Bat Scanner"));
            empire.Designs[scout.Key] = scout;

            StarMapinitializer.ApplyStartingTechUpgrades(empire);

            Assert.AreEqual("Quick Jump 5", scout.Hull.Modules.Single(m => m.ComponentType == "Engine").AllocatedComponent.Name);
            Assert.AreEqual("Bat Scanner", scout.Hull.Modules.Single(m => m.ComponentType == "Scanner").AllocatedComponent.Name);
        }

        [Test]
        public void TechUpgrade_RamScoop_SkippedForAColonyShip_UnlessRadiationTolerant()
        {
            ShipDesign colonyShip = Design("Colony Ship", ("Engine", "Quick Jump 5"));
            ShipDesign scout = Design("Scout", ("Engine", "Quick Jump 5"));
            Component quickJump = colonyShip.Hull.Modules.Single(m => m.ComponentType == "Engine").AllocatedComponent;

            Race ordinary = new Race();
            EmpireData empire = EmpireWith(ordinary, "Radiating Hydro-Ram Scoop", "Long Hump 6");
            Assert.AreEqual("Long Hump 6", StarMapinitializer.StartingUpgradeFor(quickJump, colonyShip, empire));
            Assert.AreEqual("Radiating Hydro-Ram Scoop", StarMapinitializer.StartingUpgradeFor(quickJump, scout, empire));

            Race immune = new Race();
            immune.RadiationTolerance.Immune = true;
            Assert.AreEqual("Radiating Hydro-Ram Scoop", StarMapinitializer.StartingUpgradeFor(quickJump, colonyShip, EmpireWith(immune, "Radiating Hydro-Ram Scoop", "Long Hump 6")));

            Race hot = new Race();
            hot.RadiationTolerance.MinimumValue = 80;
            hot.RadiationTolerance.MaximumValue = 100; // centre 90
            Assert.AreEqual("Radiating Hydro-Ram Scoop", StarMapinitializer.StartingUpgradeFor(quickJump, colonyShip, EmpireWith(hot, "Radiating Hydro-Ram Scoop", "Long Hump 6")));

            Race edge = new Race();
            edge.RadiationTolerance.MinimumValue = 68;
            edge.RadiationTolerance.MaximumValue = 100; // centre 84: not above 84
            Assert.AreEqual("Long Hump 6", StarMapinitializer.StartingUpgradeFor(quickJump, colonyShip, EmpireWith(edge, "Radiating Hydro-Ram Scoop", "Long Hump 6")));
        }

        [TestCase("Laser", "X-Ray Laser", "X-Ray Laser")]
        [TestCase("Laser", "Yakimora Light Phaser", "Yakimora Light Phaser")]
        [TestCase("X-Ray Laser", "X-Ray Laser", null)]
        [TestCase("Alpha Torpedo", "Beta Torpedo", "Beta Torpedo")]
        [TestCase("Lady Finger Bomb", "Black Cat Bomb", "Black Cat Bomb")]
        [TestCase("Tritanium", "Carbonic Armor", "Carbonic Armor")]
        [TestCase("Mole-skin Shield", "Cow-hide Shield", "Cow-hide Shield")]
        [TestCase("Robo-Mini Miner", "Robo-Midget Miner", "Robo-Midget Miner")]
        [TestCase("Fuel Tank", "Beta Torpedo", null)]
        [TestCase("Colonization Module", "Orbital Construction Module", null)]
        public void TechUpgrade_CandidateTable(string installed, string available, string expected)
        {
            Component part = new AllComponents().Fetch(installed);
            ShipDesign design = Design("Scout");
            Assert.AreEqual(expected, StarMapinitializer.StartingUpgradeFor(part, design, EmpireWith(new Race(), available)));
        }

        [Test]
        public void TechUpgrade_GeneratedJackOfAllTrades_GetsLongHump6Engines_AndStarbasesAreUntouched()
        {
            ServerData serverState = GenerateGame(4, ("Jack", "JOAT", new string[0]), withComponents: true);
            EmpireData empire = serverState.AllEmpires[1];

            ShipDesign santaMaria = empire.Designs.Values.Single(d => d.Name == "Santa Maria");
            Assert.AreEqual("Long Hump 6", santaMaria.Hull.Modules.Single(m => m.ComponentType == "Engine").AllocatedComponent.Name,
                "Propulsion 3 builds the Long Hump 6, the first buildable engine candidate");

            ShipDesign starbase = empire.Designs.Values.Single(d => d.Name == "Starbase");
            Assert.IsTrue(starbase.Hull.Modules.Any(m => m.AllocatedComponent != null && m.AllocatedComponent.Name == "Laser"),
                "starbase designs are not part of the ship-design upgrade pass");
        }

        // ---------------------------------------------------------------- helpers

        private static ServerData GenerateGame(int seed, (string Name, string Primary, string[] Lesser) player, bool withComponents = false)
        {
            Race race = new Race { Name = player.Name };
            race.Traits.SetPrimary(player.Primary);
            foreach (string trait in player.Lesser)
            {
                race.Traits.Add(trait);
            }

            ServerData serverState = new ServerData();
            serverState.AllRaces.Add(race.Name, race);
            serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = 1, RaceName = race.Name, AiProgram = "Human" });

            EmpireData empire = new EmpireData { Id = 1, Race = race };
            if (withComponents)
            {
                Gameinitializer.ProcessPrimaryTraits(empire);
                Gameinitializer.ProcessSecondaryTraits(empire);
                empire.AvailableComponents = new RaceComponents(race, empire.ResearchLevels);
            }
            serverState.AllEmpires[empire.Id] = empire;

            StarMapinitializer initializer = new StarMapinitializer(serverState, new Random(seed));
            initializer.GenerateStars();
            initializer.GeneratePlayerAssets();
            return serverState;
        }
    }
}
