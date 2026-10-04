namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    using Nova.Ai;
    using Nova.Common;
    using Nova.Server;
    using Nova.Server.NewGame;

    /// <summary>What a per-turn hook sees. Setting <see cref="StopRequested"/> ends the run after
    /// this turn.</summary>
    public sealed class SimulationTurnContext
    {
        public SimulationRunner Runner { get; set; }

        /// <summary>The live game state (before generation in BeforeTurn, after it in AfterTurn).</summary>
        public ServerData State { get; set; }

        /// <summary>1-based turn number of this run.</summary>
        public int Turn { get; set; }

        /// <summary>Null in BeforeTurn.</summary>
        public TurnRecord Record { get; set; }

        /// <summary>This turn's violations (empty in BeforeTurn).</summary>
        public List<InvariantViolation> Violations { get; set; } = new List<InvariantViolation>();

        public bool StopRequested { get; set; }
    }

    /// <summary>
    /// Runs a whole game headlessly: creates it from a <see cref="SimulationConfig"/> in a work
    /// folder (Gameinitializer.Initialize, exactly as the New Game screen does, with the built-in
    /// AI race templates), then loops turns in-process - every player's DefaultAi.DoMove ->
    /// OrderWriter -> OrderReader -> TurnGenerator.Generate -> ServerData.Save, reloading the game
    /// from disk every ReloadEvery turns - checking the invariants, collecting metrics and hashing
    /// the saved state every turn. Never touches the UI. See docs/SIMULATION.md.
    /// </summary>
    public sealed class SimulationRunner
    {
        private const string VictoryText = "have won the game";

        private readonly SimulationConfig config;
        private SimulationEnvironment environment;
        private string workFolder;
        private bool ownsWorkFolder;
        private string gameFolder;
        private SimulationResult result;
        private bool stopRequested;

        public SimulationRunner(SimulationConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>Called at the start of every turn, before the AIs move.</summary>
        public Action<SimulationTurnContext> BeforeTurn { get; set; }

        /// <summary>Called after every turn, once metrics and invariants are in.</summary>
        public Action<SimulationTurnContext> AfterTurn { get; set; }

        public InvariantChecker Checker { get; set; } = new InvariantChecker();

        /// <summary>The live game state while running.</summary>
        public ServerData State { get; private set; }

        public SimulationConfig Config
        {
            get { return config; }
        }

        /// <summary>Convenience: run a config start to finish.</summary>
        public static SimulationResult RunOnce(SimulationConfig config)
        {
            return new SimulationRunner(config).Run();
        }

        /// <summary>Creates the game and plays <see cref="SimulationConfig.Turns"/> turns.</summary>
        public SimulationResult Run()
        {
            return Execute(() =>
            {
                CreateGame();
                return config.Turns;
            });
        }

        /// <summary>
        /// Reloads an existing game folder and continues it for <see cref="SimulationConfig.Turns"/>
        /// turns. With a WorkFolder in the config the game files are copied there first (the
        /// source folder is left untouched); without one the game continues in place.
        /// </summary>
        public SimulationResult Resume(string sourceGameFolder)
        {
            return Execute(() =>
            {
                LoadGame(sourceGameFolder);
                return config.Turns;
            });
        }

        private SimulationResult Execute(Func<int> setup)
        {
            Stopwatch total = Stopwatch.StartNew();
            result = new SimulationResult { Config = config };
            stopRequested = false;
            PrepareWorkFolder();

            try
            {
                using (environment = SimulationEnvironment.Enter(Path.Combine(workFolder, "novaroot"), config.ComponentsFile))
                {
                    int turns;
                    try
                    {
                        turns = setup();
                        result.SetupErrors = environment.TakeErrors();
                    }
                    catch (Exception e)
                    {
                        result.FatalError = "game setup failed: " + e;
                        result.SetupErrors = environment.TakeErrors();
                        return result;
                    }

                    result.GameFolder = gameFolder;
                    PlayTurns(turns);
                }
            }
            finally
            {
                environment = null;
                total.Stop();
                result.ElapsedSeconds = total.Elapsed.TotalSeconds;
                if (ownsWorkFolder && config.DeleteWorkFolder)
                {
                    TryDelete(workFolder);
                    if (gameFolder != null && gameFolder.StartsWith(workFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        result.GameFolder = null;
                    }
                }
            }

            return result;
        }

        private void PrepareWorkFolder()
        {
            if (string.IsNullOrEmpty(config.WorkFolder))
            {
                workFolder = Path.Combine(Path.GetTempPath(), "NovaSim", "run-" + config.Seed + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                ownsWorkFolder = true;
            }
            else
            {
                workFolder = Path.GetFullPath(config.WorkFolder);
                ownsWorkFolder = false;
            }

            Directory.CreateDirectory(workFolder);
        }

        // ------------------------------------------------------------------------- game setup

        private void CreateGame()
        {
            gameFolder = Path.Combine(workFolder, "game");
            if (Directory.Exists(gameFolder))
            {
                Directory.Delete(gameFolder, true);
            }

            Directory.CreateDirectory(gameFolder);

            GameSettings settings = GameSettings.Data;
            settings.GameName = config.GameName;
            settings.Seed = config.Seed;
            settings.ApplyGalaxyPreset(config.GalaxySize, config.Density);
            if (config.NumberOfStars.HasValue)
            {
                settings.NumberOfStars = config.NumberOfStars.Value;
            }

            settings.StartingDistanceSetting = config.StartingDistance;
            settings.AcceleratedStart = config.AcceleratedStart;
            settings.NoRandomEvents = config.NoRandomEvents;
            settings.SlowTechAdvance = config.SlowTechAdvance;
            settings.MaximumMinerals = config.MaximumMinerals;
            settings.GalaxyClumping = config.GalaxyClumping;

            if (config.Players.Count == 0)
            {
                throw new InvalidOperationException("a simulation needs at least one player");
            }

            // The New Game screen's built-in AI rows (NewGameViewModel.BuildPlayers): Random tier
            // then Random archetype resolved from the seed, the template's race under a unique
            // name, category = archetype unless the spec overrides it.
            Random setupRandom = new Random(config.Seed);
            Dictionary<string, Race> races = new Dictionary<string, Race>();
            List<PlayerSettings> players = new List<PlayerSettings>();
            foreach (PlayerSpec requested in config.Players)
            {
                int archetype = requested.Archetype;
                int tier = requested.Tier;
                AiRaceTemplates.ResolveRandom(ref archetype, ref tier, setupRandom);
                PlayerSpec spec = new PlayerSpec(archetype, tier, requested.Category);

                Race race = AiRaceTemplates.CreateRace(archetype, tier);
                race.Name = Nova.Client.NewGameSetup.UniqueRaceName(race.Name, races.Keys);
                race.PluralName = race.Name;
                races[race.Name] = race;

                ushort number = (ushort)(players.Count + 1);
                players.Add(new PlayerSettings
                {
                    PlayerNumber = number,
                    RaceName = race.Name,
                    AiProgram = "Default AI",
                    AiCategory = spec.EffectiveCategory,
                });
                result.Players[number] = spec;
            }

            Gameinitializer.Initialize(gameFolder, players, races);
            GameSettings.Save();

            State = LoadState(gameFolder);
        }

        private void LoadGame(string sourceGameFolder)
        {
            string source = Path.GetFullPath(sourceGameFolder);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(source);
            }

            if (ownsWorkFolder)
            {
                // No work folder requested: continue in place.
                gameFolder = source;
            }
            else
            {
                gameFolder = Path.Combine(workFolder, "game");
                if (!string.Equals(Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar), source.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(gameFolder))
                    {
                        Directory.Delete(gameFolder, true);
                    }

                    Directory.CreateDirectory(gameFolder);
                    foreach (string file in Directory.GetFiles(source))
                    {
                        File.Copy(file, Path.Combine(gameFolder, Path.GetFileName(file)));
                    }
                }
            }

            string settingsFile = Directory.GetFiles(gameFolder, "*" + Global.SettingsExtension).FirstOrDefault();
            if (settingsFile != null)
            {
                GameSettings.Data.SettingsPathName = settingsFile;
                GameSettings.Restore();
                GameSettings.Data.SettingsPathName = settingsFile;
            }

            State = LoadState(gameFolder);

            foreach (PlayerSettings player in State.AllPlayers)
            {
                if (Gameinitializer.IsHumanPlayer(player))
                {
                    continue;
                }

                Race race = State.AllRaces.TryGetValue(player.RaceName, out Race found) ? found : null;
                if (race != null && AiRaceTemplates.TryIdentify(race, out int archetype, out int tier))
                {
                    result.Players[player.PlayerNumber] = new PlayerSpec(archetype, tier, player.AiCategory >= 0 && player.AiCategory != archetype ? player.AiCategory : (int?)null);
                }
                else
                {
                    result.Players[player.PlayerNumber] = new PlayerSpec(-1, -1, player.AiCategory);
                }
            }
        }

        /// <summary>Loads the game's state file and points it at <paramref name="folder"/> (a
        /// copied game still names its original folder inside the file).</summary>
        private static ServerData LoadState(string folder)
        {
            string statePath = Directory.GetFiles(folder, "*" + Global.ServerStateExtension).FirstOrDefault();
            if (statePath == null)
            {
                throw new FileNotFoundException("no " + Global.ServerStateExtension + " file in " + folder);
            }

            ServerData state = new ServerData { StatePathName = statePath };
            state.Restore();
            state.GameFolder = folder;
            state.StatePathName = statePath;
            return state;
        }

        // ------------------------------------------------------------------------- turn loop

        private void PlayTurns(int turns)
        {
            AutoTurnGenerator host = new AutoTurnGenerator(State)
            {
                AiFactory = (settings, year) => new SeededDefaultAi(SeededDefaultAi.SeedFor(config.Seed, settings.PlayerNumber, year)),
                GeneratorFactory = state => new SimTurnGenerator(state, config.KeepTurnBackups),
                PublishGameFolder = folder => environment.SetGameFolder(folder),
                SaveAfterEachTurn = true,
            };

            int turn = 0;
            while (turn < turns && !stopRequested)
            {
                turn++;
                if (!PlayOneTurn(host, turn))
                {
                    break;
                }
            }

            result.Completed = turn >= turns && result.FatalError == null;
        }

        /// <summary>Plays one turn; false stops the run.</summary>
        private bool PlayOneTurn(AutoTurnGenerator host, int turn)
        {
            Stopwatch clock = Stopwatch.StartNew();
            host.State = State;

            SimulationTurnContext before = new SimulationTurnContext { Runner = this, State = State, Turn = turn };
            BeforeTurn?.Invoke(before);
            if (before.StopRequested)
            {
                stopRequested = true;
                return false;
            }

            AutoTurnStep step;
            try
            {
                step = host.GenerateOne(turn);
            }
            catch (Exception e)
            {
                result.FatalError = "turn " + turn + " (year " + State.TurnYear + ") threw: " + e;
                result.Violations.Add(new InvariantViolation
                {
                    Rule = InvariantChecker.NoExceptionRule,
                    Turn = turn,
                    Year = State.TurnYear,
                    Message = e.GetType().Name + ": " + e.Message + " at " + FirstFrame(e),
                });
                result.Turns.Add(new TurnRecord { Turn = turn, Year = State.TurnYear, Errors = environment.TakeErrors() });
                return false;
            }

            clock.Stop();
            GenerationObservations observed = ((SimTurnGenerator)step.Generator).Observations;

            TurnObservation observation = new TurnObservation
            {
                Turn = turn,
                Year = State.TurnYear,
                Generation = observed,
                Submissions = step.Submissions,
                OrdersNotAccepted = step.OrdersNotAccepted,
                AiEmpires = new HashSet<int>(State.AllPlayers.Where(p => !Gameinitializer.IsHumanPlayer(p)).Select(p => (int)p.PlayerNumber)),
                Errors = environment.TakeErrors(),
                TurnSeconds = clock.Elapsed.TotalSeconds,
            };

            TurnRecord record = new TurnRecord
            {
                Turn = turn,
                Year = State.TurnYear,
                AiSeconds = step.AiSeconds,
                GenerateSeconds = step.GenerateSeconds,
                TotalSeconds = clock.Elapsed.TotalSeconds,
                AiLoadSeconds = step.AiLoadSeconds,
                IntelSeconds = observed.IntelSeconds,
                SaveSeconds = step.SaveSeconds,
                Messages = observed.Messages.Count,
                RejectedOrders = observed.TotalRejected,
                RejectedByType = new Dictionary<string, int>(observed.RejectedByType),
                Errors = observation.Errors,
                Battles = State.AllEmpires.Values.Sum(e => e.BattleReports.Count),
                VictoryMessage = observed.Messages.Where(m => m.Text != null && m.Text.Contains(VictoryText)).Select(m => m.Text).FirstOrDefault(),
            };

            List<InvariantViolation> violations = new List<InvariantViolation>();
            foreach (KeyValuePair<int, Exception> failure in step.AiFailures)
            {
                violations.Add(new InvariantViolation
                {
                    Rule = InvariantChecker.NoExceptionRule,
                    Turn = turn,
                    Year = State.TurnYear,
                    EmpireId = failure.Key,
                    Subject = "AI move",
                    Message = failure.Value.GetType().Name + ": " + failure.Value.Message + " at " + FirstFrame(failure.Value),
                });
            }

            string savedText = File.ReadAllText(State.StatePathName);
            record.StateHash = StateHasher.HashText(savedText);

            bool reloadTurn = config.ReloadEvery > 0 && turn % config.ReloadEvery == 0;
            ServerData reloaded = null;
            if (reloadTurn)
            {
                try
                {
                    reloaded = LoadState(State.GameFolder);
                    if (config.CheckRoundTrip)
                    {
                        observation.RoundTripChecked = true;
                        observation.RoundTripDifference = RoundTripDifference(reloaded, savedText);
                    }
                }
                catch (Exception e)
                {
                    observation.RoundTripChecked = true;
                    observation.RoundTripDifference = "reload threw " + e.GetType().Name + ": " + e.Message + " at " + FirstFrame(e);
                    reloaded = null;
                }

                // Reloading reports its own errors (ServerData's loader is non-fatal per node).
                observation.Errors.AddRange(environment.TakeErrors());
            }

            record.RoundTripChecked = observation.RoundTripChecked;
            record.RoundTripDifference = observation.RoundTripDifference;

            record.Empires = SimulationMetrics.Collect(State, turn, result.Players, observed, step.Submissions);

            InvariantContext context = new InvariantContext(State, observation, config.Limits);
            violations.AddRange(Checker.Check(context, config.DisabledInvariants));
            record.Violations = violations.Count;
            result.Violations.AddRange(violations);
            result.Turns.Add(record);

            if (reloaded != null)
            {
                State = reloaded;
                record.Reloaded = true;
            }

            SimulationTurnContext after = new SimulationTurnContext { Runner = this, State = State, Turn = turn, Record = record, Violations = violations };
            AfterTurn?.Invoke(after);
            if (after.StopRequested || (config.StopOnViolation && violations.Count > 0))
            {
                stopRequested = true;
                return false;
            }

            return true;
        }

        /// <summary>Saves the freshly reloaded state to a scratch file and compares it with what
        /// was saved; null when identical.</summary>
        private string RoundTripDifference(ServerData reloaded, string savedText)
        {
            string original = reloaded.StatePathName;
            string scratch = Path.Combine(workFolder, "roundtrip" + Global.ServerStateExtension);
            try
            {
                reloaded.StatePathName = scratch;
                reloaded.Save();
            }
            finally
            {
                reloaded.StatePathName = original;
            }

            string resaved = StateHasher.Normalize(File.ReadAllText(scratch));
            string saved = StateHasher.Normalize(savedText);
            File.Delete(scratch);
            return saved == resaved ? null : StateHasher.FirstDifference(saved, resaved) ?? "texts differ";
        }

        private static string FirstFrame(Exception e)
        {
            string trace = e.StackTrace ?? string.Empty;
            string first = trace.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "(no stack)";
            return first;
        }

        private static void TryDelete(string folder)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        Directory.Delete(folder, true);
                    }

                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(100);
                }
            }
        }
    }
}
