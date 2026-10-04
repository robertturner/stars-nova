namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Server;
    using Nova.Server.NewGame;

    using NUnit.Framework;

    /// <summary>
    /// Whole-game determinism: a new game plus N turns of the real TurnGenerator and the
    /// in-process AI (the same file-based flow as Nova.Avalonia's TurnHost) must be bit-for-bit
    /// repeatable from the game seed - the saved server state text is the comparison - and a game
    /// saved and reloaded mid-run must continue exactly as one that kept running in memory.
    /// </summary>
    [TestFixture]
    public class DeterminismTest
    {
        private const string GameName = "DeterminismGame";
        private const int Turns = 5;

        /// <summary>RunGame's reloadAfterTurn value for "restore the state after every turn".</summary>
        private const int ReloadEveryTurn = -1;

        private string root;
        private string gameFolder;
        private Func<string> previousRoot;
        private Action<string> previousFatal;
        private Action<string> previousError;
        private GameSettings previousSettings;
        private readonly List<string> errors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            // Find the real data files before redirecting where Nova looks for them.
            string components = FileSearcher.GetComponentFile();
            string graphics = Path.Combine(Path.GetDirectoryName(components), Global.GraphicsFolderName);

            // A private Nova root holding its own nova.conf: Gameinitializer, IntelWriter and the
            // AI's ClientData find the game folder through it, and the shared nova.conf must not
            // be touched. One path per process (other test runs may be going on at the same time),
            // fixed within it, so the saved GameFolder/StatePathName text is the same in every run
            // being compared.
            root = Path.Combine(Path.GetTempPath(), "NovaDeterminismTest_" + Environment.ProcessId);
            gameFolder = Path.Combine(root, "game");
            DeleteFolder(root);
            Directory.CreateDirectory(root);

            previousRoot = PlatformHooks.NovaRootOverride;
            previousFatal = PlatformHooks.ShowFatalError;
            previousError = PlatformHooks.ShowError;
            previousSettings = GameSettings.Data;

            PlatformHooks.NovaRootOverride = () => root;

            // Report.FatalError would otherwise end the test process.
            PlatformHooks.ShowFatalError = message => throw new InvalidOperationException(message);
            PlatformHooks.ShowError = message => errors.Add(message);

            using (Config conf = new Config())
            {
                conf[Global.ComponentFileKey] = components;
                if (Directory.Exists(graphics))
                {
                    conf[Global.GraphicsFolderKey] = graphics;
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            PlatformHooks.NovaRootOverride = previousRoot;
            PlatformHooks.ShowFatalError = previousFatal;
            PlatformHooks.ShowError = previousError;
            GameSettings.Data = previousSettings;
            DeleteFolder(root);
        }

        [Test]
        public void TheSameSeed_PlaysTheSameGame()
        {
            List<string> first = RunGame(20261002, Turns, reloadAfterTurn: null);
            List<string> second = RunGame(20261002, Turns, reloadAfterTurn: null);

            AssertSameStates(first, second);
            Assert.AreNotEqual(first[0], first[first.Count - 1], "the turns actually changed the game");
        }

        [Test]
        public void DifferentSeeds_PlayDifferentGames()
        {
            List<string> first = RunGame(1111, 2, reloadAfterTurn: null);
            List<string> second = RunGame(2222, 2, reloadAfterTurn: null);

            Assert.AreNotEqual(first.Last(), second.Last());
        }

        [Test]
        public void ASaveAndReloadMidRun_ContinuesIdentically()
        {
            List<string> kept = RunGame(424242, Turns, reloadAfterTurn: null);
            List<string> reloaded = RunGame(424242, Turns, reloadAfterTurn: 2);

            AssertSameStates(kept, reloaded);
        }

        /// <summary>
        /// The long form of the save/reload check: six AI personalities on an 800 ly map, the
        /// state restored from its save after EVERY turn (as TurnHost does) against one kept in
        /// memory (as NovaConsole does). Minutes long, so explicit; run it after touching turn
        /// generation, persistence or the AI.
        /// </summary>
        [Test]
        [Explicit("several minutes")]
        [TestCase(31337, 30)]
        [TestCase(99, 45)]
        [TestCase(2026, 25)]
        public void SixAis_ReloadedEveryTurn_PlayLikeAGameKeptInMemory(int seed, int turns)
        {
            int[] all = { 0, 1, 2, 3, 4, 5 };
            List<string> kept = RunGame(seed, turns, reloadAfterTurn: null, archetypes: all, mapSize: 800);
            List<string> reloaded = RunGame(seed, turns, reloadAfterTurn: ReloadEveryTurn, archetypes: all, mapSize: 800);

            AssertSameStates(kept, reloaded);
        }

        /// <summary>
        /// Regression: a star that lost its owner but still names its old race (Owner 0 with a
        /// ThisRace) made ServerData.Restore throw (no empire 0), so the save could not be
        /// loaded - found by the six-AI reload stress (seed 31337, turn 30).
        /// </summary>
        [Test]
        public void AnOwnerlessStarWithARaceName_StillLoads()
        {
            Race race = new Race { Name = "Lost" };
            ServerData server = new ServerData { StatePathName = Path.Combine(root, "ownerless" + Global.ServerStateExtension) };
            EmpireData empire = new EmpireData { Id = 1, Race = race };
            server.AllEmpires.Add(1, empire);
            server.AllRaces.Add(race.Name, race);

            Star abandoned = new Star { Name = "Abandoned", Position = new Nova.Common.DataStructures.NovaPoint(10, 10) };
            abandoned.Owner = Global.Nobody;
            abandoned.ThisRace = race;
            server.AllStars.Add(abandoned.Name, abandoned);
            server.Save();

            ServerData restored = new ServerData { StatePathName = server.StatePathName };
            Assert.DoesNotThrow(() => restored.Restore());
            Assert.IsTrue(restored.AllStars.ContainsKey("Abandoned"));
            Assert.IsNull(restored.AllStars["Abandoned"].ThisRace, "an ownerless star has no race");

            // A game kept in memory ends each generation the same way.
            server.CompactCollections();
            Assert.IsNull(abandoned.ThisRace);
        }

        /// <summary>A turn step that records the settings turn generation sees.</summary>
        private sealed class SettingsProbeStep : Nova.Server.TurnSteps.ITurnStep
        {
            public int MapWidth = -1;
            public bool SlowTechAdvance;
            public bool NoRandomEvents;

            public void Process(ServerData serverState)
            {
                MapWidth = GameSettings.Data.MapWidth;
                SlowTechAdvance = GameSettings.Data.SlowTechAdvance;
                NoRandomEvents = GameSettings.Data.NoRandomEvents;
            }
        }

        private static GameSettings OtherSettings(int mapWidth)
        {
            GameSettings other = (GameSettings)Activator.CreateInstance(typeof(GameSettings), nonPublic: true);
            other.GameName = "SomeOtherGame";
            other.MapWidth = mapWidth;
            other.MapHeight = mapWidth;
            return other;
        }

        /// <summary>
        /// GameSettings.Data is one process-wide instance; a game now keeps its own settings
        /// (ServerData.Settings, saved in the state) and turn generation installs them. Two
        /// games with different settings in one process: generating the first after the second
        /// was created (which left its own settings in GameSettings.Data) still sees the first
        /// game's settings, and Data is put back afterwards.
        /// </summary>
        [Test]
        public void TwoGamesWithDifferentSettings_DoNotLeakIntoEachOther()
        {
            RunGame(5150, 0, reloadAfterTurn: null, mapSize: 800, configure: settings =>
            {
                settings.SlowTechAdvance = true;
                settings.NoRandomEvents = true;
            });

            // A second game, different settings, made in the same process.
            string otherFolder = Path.Combine(root, "other");
            Directory.CreateDirectory(otherFolder);
            GameSettings.Data = OtherSettings(400);
            GameSettings.Data.Seed = 6;
            Race race = AiRaceTemplates.CreateRace(AiCategory.Rototills, AiRaceTemplates.Standard);
            race.Name = "OtherGameRace";
            Gameinitializer.Initialize(
                otherFolder,
                new List<PlayerSettings> { new PlayerSettings { PlayerNumber = 1, RaceName = race.Name, AiProgram = "Default AI" } },
                new Dictionary<string, Race> { { race.Name, race } });
            GameSettings leftOver = GameSettings.Data;

            // The ServerFolder of nova.conf points at the second game now; the first is generated.
            using (Config conf = new Config())
            {
                conf[Global.ServerFolderKey] = gameFolder;
            }

            ServerData first = Load();
            Assert.IsNotNull(first.Settings, "the game's settings are saved with its state");
            Assert.AreEqual(800, first.Settings.MapWidth);

            TurnGenerator generator = new TurnGenerator(first);
            SettingsProbeStep probe = new SettingsProbeStep();
            SortedList<int, Nova.Server.TurnSteps.ITurnStep> steps = (SortedList<int, Nova.Server.TurnSteps.ITurnStep>)typeof(TurnGenerator)
                .GetField("turnSteps", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(generator);
            steps.Add(50, probe);
            generator.Generate();

            Assert.AreEqual(800, probe.MapWidth, "the first game's map, not the second game's");
            Assert.IsTrue(probe.SlowTechAdvance);
            Assert.IsTrue(probe.NoRandomEvents);
            Assert.AreSame(leftOver, GameSettings.Data, "the process-wide instance is put back");
        }

        /// <summary>
        /// The whole game is unaffected by what the process-wide settings hold between turns
        /// (here: a different map size and game options before every turn).
        /// </summary>
        [Test]
        public void AGame_IgnoresWhateverSettingsTheProcessHoldsBetweenTurns()
        {
            Action<GameSettings> options = settings =>
            {
                settings.SlowTechAdvance = true;
                settings.NoRandomEvents = true;
            };

            List<string> clean = RunGame(8080, Turns, reloadAfterTurn: null, configure: options);
            List<string> polluted = RunGame(8080, Turns, reloadAfterTurn: null, configure: options, beforeEachTurn: () => GameSettings.Data = OtherSettings(2000));

            AssertSameStates(clean, polluted);
        }

        /// <summary>
        /// A save from before the settings were stored in the state adopts the game folder's
        /// .settings file (named after the state file) the first time a turn is generated.
        /// </summary>
        [Test]
        public void AnOlderSave_AdoptsItsGameFoldersSettingsFile()
        {
            RunGame(31, 0, reloadAfterTurn: null, mapSize: 800);
            ServerData server = Load();
            File.WriteAllText(Path.ChangeExtension(StatePath(), Global.SettingsExtension), server.Settings.ToXmlText());
            server.Settings = null;

            GameSettings.Data = OtherSettings(400);
            using (server.UseSettings())
            {
                Assert.AreEqual(800, GameSettings.Data.MapWidth);
            }

            Assert.AreEqual(400, GameSettings.Data.MapWidth);
            Assert.IsNotNull(server.Settings, "adopted, and saved with the state from now on");
        }

        /// <summary>
        /// A design that arrives in an order (read back from the orders XML, as OrderReader
        /// does, then applied as TurnGenerator.ParseCommands does) is already the design a
        /// reload would produce: linked to the master components and computed for the race and
        /// tech in the SAME turn - not run on the order's placeholder parts until a reload.
        /// </summary>
        [Test]
        public void AnOrderedDesign_IsLinkedForTheCurrentTurn_AsAReloadWouldLinkIt()
        {
            Nova.Common.Components.AllComponents components = new Nova.Common.Components.AllComponents();
            Dictionary<string, Nova.Common.Components.Component> parts = new Dictionary<string, Nova.Common.Components.Component>(components.GetAll);

            EmpireData empire = new EmpireData { Id = 1, Race = AiRaceTemplates.CreateRace(AiCategory.Automitrons, AiRaceTemplates.Standard) };
            empire.ResearchLevels = new TechLevel(5);
            Nova.Common.Components.ShipDesign design = new DesignBuilder(parts, new Random(1))
                .Build("Scout", DesignBuilder.ParseTemplate("30.26.26"), empire.GetNextDesignKey(), "Ordered scout");

            // Through the orders file's XML form.
            System.Xml.XmlDocument orders = new System.Xml.XmlDocument();
            orders.AppendChild(new Nova.Common.Commands.DesignCommand(Nova.Common.Commands.CommandMode.Add, design).ToXml(orders));
            Nova.Common.Commands.DesignCommand ordered = new Nova.Common.Commands.DesignCommand(orders.DocumentElement);
            Assert.IsTrue(ordered.IsValid(empire));
            ordered.ApplyToState(empire);

            Nova.Common.Components.ShipDesign applied = empire.Designs[design.Key];
            Assert.Greater(applied.FuelCapacity, 0, "the real hull/engine figures this turn");

            // What the same empire looks like after a save and reload.
            System.Xml.XmlDocument saved = new System.Xml.XmlDocument();
            saved.AppendChild(empire.ToXml(saved));
            EmpireData reloaded = new EmpireData(saved.DocumentElement);

            System.Xml.XmlDocument a = new System.Xml.XmlDocument();
            a.AppendChild(applied.ToXml(a));
            System.Xml.XmlDocument b = new System.Xml.XmlDocument();
            b.AppendChild(reloaded.Designs[design.Key].ToXml(b));
            Assert.AreEqual(b.OuterXml, a.OuterXml);
            Assert.AreEqual(reloaded.Designs[design.Key].FuelCapacity, applied.FuelCapacity);
            Assert.AreEqual(reloaded.Designs[design.Key].Mass, applied.Mass);
        }

        /// <summary>
        /// The same game played under Turkish culture (dotted/dotless i: "ID".ToLower() is
        /// "ıd", so culture-sensitive lower-casing breaks the loaders' tag switches; decimal
        /// comma) saves exactly what it saves under the invariant culture.
        /// </summary>
        [Test]
        public void AGamePlayedUnderTurkishCulture_SavesExactlyTheInvariantGame()
        {
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
            List<string> invariant;
            List<string> turkish;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
                invariant = RunGame(1923, Turns, reloadAfterTurn: 2);

                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("tr-TR");
                turkish = RunGame(1923, Turns, reloadAfterTurn: 2);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;
                System.Globalization.CultureInfo.CurrentUICulture = uiCulture;
            }

            AssertSameStates(invariant, turkish);
        }

        /// <summary>
        /// An owned planet is saved twice (AllStars and its owner's OwnedStars) and loads as two
        /// objects; TurnGenerator.ParseCommands then makes the owner's copy the AllStars entry
        /// at the start of every generation. That is lossless only while the two saved copies
        /// agree - checked here (invariant: identical saved text) - and re-saving a just-loaded
        /// state reproduces the file exactly.
        /// </summary>
        [Test]
        public void AnOwnedStarsTwoSavedCopies_Agree_SoParseCommandsLosesNothing()
        {
            List<string> states = RunGame(4711, 3, reloadAfterTurn: null);

            ServerData loaded = Load();
            int owned = 0;
            foreach (EmpireData empire in loaded.AllEmpires.Values)
            {
                foreach (Star star in empire.OwnedStars.Values)
                {
                    if (star.Owner == empire.Id)
                    {
                        owned++;
                        System.Xml.XmlDocument a = new System.Xml.XmlDocument();
                        a.AppendChild(star.ToXml(a));
                        System.Xml.XmlDocument b = new System.Xml.XmlDocument();
                        b.AppendChild(loaded.AllStars[star.Key].ToXml(b));
                        Assert.AreEqual(b.OuterXml, a.OuterXml, star.Name + ": the owner's copy and the galaxy's copy agree");
                    }
                }
            }

            Assert.Greater(owned, 0);

            loaded.Save();
            Assert.AreEqual(states.Last(), File.ReadAllText(StatePath()), "loading and re-saving loses nothing");
        }

        [Test]
        public void TheSeedIsSaved_WithTheGameState()
        {
            RunGame(777, 0, reloadAfterTurn: null);

            ServerData restored = new ServerData { StatePathName = StatePath() };
            restored.Restore();

            Assert.AreEqual(777, restored.Seed);
            Assert.IsTrue(restored.AllEmpires.Values.All(empire => empire.RandomSeed != 0), "every empire has its own AI seed");
            Assert.AreEqual(restored.AllEmpires.Count, restored.AllEmpires.Values.Select(empire => empire.RandomSeed).Distinct().Count());
        }

        [Test]
        public void DerivedStreams_DependOnlyOnTheirKeys()
        {
            Assert.AreEqual(GameRandom.DeriveSeed(5, 2101, "Scan"), GameRandom.DeriveSeed(5, 2101, "Scan"));
            Assert.AreNotEqual(GameRandom.DeriveSeed(5, 2101, "Scan"), GameRandom.DeriveSeed(5, 2102, "Scan"));
            Assert.AreNotEqual(GameRandom.DeriveSeed(5, 2101, "Scan"), GameRandom.DeriveSeed(5, 2101, "Battle"));
            Assert.AreNotEqual(GameRandom.DeriveSeed(5, 2101, "Scan"), GameRandom.DeriveSeed(6, 2101, "Scan"));
            Assert.AreNotEqual(GameRandom.DeriveSeed(5, 2101, "Fleet", 1), GameRandom.DeriveSeed(5, 2101, "Fleet", 2));

            // A second request for the same stream in one generation is a fresh, reproducible
            // sequence; a new generation epoch starts the counts again.
            ServerData server = new ServerData { Seed = 9, TurnYear = 2105 };
            server.BeginRandomTurn();
            int a = server.CreateRandom("Step").Next();
            int b = server.CreateRandom("Step").Next();
            server.BeginRandomTurn();
            Assert.AreEqual(a, server.CreateRandom("Step").Next());
            Assert.AreEqual(b, server.CreateRandom("Step").Next());
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void TheAmbientStream_IsScoped()
        {
            Random outer = new Random(1);
            Random inner = new Random(2);
            using (GameRandom.Use(outer))
            {
                Assert.AreSame(outer, GameRandom.Current);
                using (GameRandom.Use(inner))
                {
                    Assert.AreSame(inner, GameRandom.Current);
                }

                Assert.AreSame(outer, GameRandom.Current);
                using (GameRandom.Use(null))
                {
                    Assert.AreSame(outer, GameRandom.Current, "a null stream keeps the current one");
                }
            }

            Assert.IsFalse(GameRandom.HasAmbient);
        }

        /// <summary>
        /// Creates a two-AI game from <paramref name="seed"/> and plays <paramref name="turns"/>
        /// turns: each AI computes its orders from its own turn file, then the turn is generated
        /// and saved. With <paramref name="reloadAfterTurn"/> the server state is thrown away after
        /// that turn and restored from the save, as a restarted host would. Returns the saved
        /// state text after creation and after every turn.
        /// </summary>
        private List<string> RunGame(int seed, int turns, int? reloadAfterTurn, int[] archetypes = null, int mapSize = 400, Action<GameSettings> configure = null, Action beforeEachTurn = null)
        {
            DeleteFolder(gameFolder);
            Directory.CreateDirectory(gameFolder);

            GameSettings.Data = (GameSettings)Activator.CreateInstance(typeof(GameSettings), nonPublic: true);
            GameSettings.Data.GameName = GameName;
            GameSettings.Data.MapWidth = mapSize;
            GameSettings.Data.MapHeight = mapSize;
            GameSettings.Data.Seed = seed;
            configure?.Invoke(GameSettings.Data);

            Dictionary<string, Race> races = new Dictionary<string, Race>();
            List<PlayerSettings> players = new List<PlayerSettings>();
            archetypes = archetypes ?? new[] { AiCategory.Robotoids, AiCategory.Automitrons };
            foreach (int archetype in archetypes)
            {
                Race race = AiRaceTemplates.CreateRace(archetype, AiRaceTemplates.Standard);
                race.Name = "Determinism" + archetype;
                race.PluralName = race.Name;
                races[race.Name] = race;
                players.Add(new PlayerSettings
                {
                    PlayerNumber = (ushort)(players.Count + 1),
                    RaceName = race.Name,
                    AiProgram = "Default AI",
                    AiCategory = archetype,
                });
            }

            Gameinitializer.Initialize(gameFolder, players, races);

            List<string> states = new List<string> { File.ReadAllText(StatePath()) };
            ServerData server = Load();

            for (int turn = 1; turn <= turns; turn++)
            {
                beforeEachTurn?.Invoke();

                // As TurnHost: the AIs run with the game's own settings installed.
                using (server.UseSettings())
                {
                    foreach (PlayerSettings settings in server.AllPlayers)
                    {
                        RunAiTurn(server, settings);
                    }
                }

                new TurnGenerator(server).Generate();
                server.Save();
                states.Add(File.ReadAllText(StatePath()));

                if (reloadAfterTurn == turn || reloadAfterTurn == ReloadEveryTurn)
                {
                    server = Load();
                }
            }

            return states;
        }

        /// <summary>One AI player's turn, as Nova.Avalonia's TurnHost.RunOneAiTurn does it.</summary>
        private static void RunAiTurn(ServerData server, PlayerSettings settings)
        {
            CommandArguments args = new CommandArguments();
            args.Add(CommandArguments.Option.RaceName, settings.RaceName);
            args.Add(CommandArguments.Option.Turn, server.TurnYear);
            args.Add(CommandArguments.Option.IntelFileName, Path.Combine(server.GameFolder, settings.RaceName + Global.IntelExtension));
            if (settings.AiCategory >= 0)
            {
                args.Add(CommandArguments.Option.AiPersonality, NewGameSetup.PersonalityCodeForCategory(settings.AiCategory));
            }

            AbstractAI ai = new DefaultAi();
            ai.Initialize(args);
            ai.DoMove();
            new OrderWriter(ai.ClientState).WriteOrders();
        }

        private ServerData Load()
        {
            ServerData server = new ServerData { StatePathName = StatePath() };
            server.Restore();
            return server;
        }

        private string StatePath()
        {
            return Path.Combine(gameFolder, GameName + Global.ServerStateExtension);
        }

        private static void AssertSameStates(List<string> first, List<string> second)
        {
            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                if (first[i] != second[i])
                {
                    Assert.Fail("The saved state differs after turn " + i + ": " + FirstDifference(first[i], second[i]));
                }
            }
        }

        private static string FirstDifference(string a, string b)
        {
            int index = 0;
            while (index < a.Length && index < b.Length && a[index] == b[index])
            {
                index++;
            }

            int start = Math.Max(0, index - 300);
            return "at character " + index + ":\n--- first ---\n" + Excerpt(a, start) + "\n--- second ---\n" + Excerpt(b, start);
        }

        private static string Excerpt(string text, int start)
        {
            return start >= text.Length ? string.Empty : text.Substring(start, Math.Min(600, text.Length - start));
        }

        private static void DeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (IOException)
            {
                // Best effort; the next run recreates it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
