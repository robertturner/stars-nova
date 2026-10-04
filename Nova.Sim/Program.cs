namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.Json;

    using Nova.Common;
    using Nova.Server;

    /// <summary>
    /// Nova.Sim command line (docs/SIMULATION.md):
    ///   run      --seed 1 --players 4 --turns 200 --out dir     one game, summary, exit 1 on invariant failure
    ///   batch    --seeds 1-20 [--jobs 4] ...run options...       many seeds
    ///   compare  a.csv b.csv                                     metric differences, exit 3 if any
    ///   replay   --game folder --turns 50 [--out dir]            reload a saved game and continue it
    ///   generate --game folder --turns 10|100|1000               the host's batch generation loop
    /// </summary>
    public static class Program
    {
        public const int ExitOk = 0;
        public const int ExitInvariantFailure = 1;
        public const int ExitBehaviourFailure = 2;
        public const int ExitDifferent = 3;
        public const int ExitUsage = 64;

        public static int Main(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintUsage();
                return args.Length == 0 ? ExitUsage : ExitOk;
            }

            try
            {
                Options options = Options.Parse(args.Skip(1));
                switch (args[0].ToLowerInvariant())
                {
                    case "run":
                        return Run(options);
                    case "batch":
                        return Batch(options);
                    case "compare":
                        return Compare(options);
                    case "replay":
                        return Replay(options);
                    case "generate":
                        return Generate(options);
                    case "profile-save":
                        return ProfileSave(options);
                    default:
                        Console.Error.WriteLine("Unknown command '" + args[0] + "'.");
                        PrintUsage();
                        return ExitUsage;
                }
            }
            catch (UsageException e)
            {
                Console.Error.WriteLine(e.Message);
                PrintUsage();
                return ExitUsage;
            }
        }

        private static bool IsHelp(string arg)
        {
            return arg == "-h" || arg == "--help" || arg == "help" || arg == "/?";
        }

        private static void PrintUsage()
        {
            Console.WriteLine(
@"Nova.Sim - headless whole-game simulation (docs/SIMULATION.md)

  Nova.Sim run --seed 1 --players 4 --turns 200 --out dir [options]
  Nova.Sim batch --seeds 1-20 [--jobs N] [run options]       (out/seed-N/... + out/batch.csv)
  Nova.Sim compare a.csv b.csv                               (exit 3 when they differ)
  Nova.Sim replay --game <folder> --turns N [--out dir]      (continue a saved game)
  Nova.Sim generate --game <folder> --turns 10|100|1000      (host batch generation, any key aborts)

Run options:
  --players N | ""Robotoids/Expert,Macinti,2/1@7""   archetype[/tier][@category]; N = archetypes 0..N-1, Standard
  --tier Easy|Standard|Tough|Expert|Random            tier used with --players N (default Standard)
  --size Tiny|Small|Medium|Large|Huge  --density Sparse|Normal|Dense|Packed  --stars N
  --distance Close|Moderate|Farther|Distant  --accelerated  --no-random-events  --slow-tech
  --max-minerals  --clumping  --reload-every K (default 5, 0 = never)  --no-roundtrip
  --stop-on-violation  --disable Rule1,Rule2  --max-turn-seconds S  --max-messages N
  --behaviour            also evaluate the AI behaviour expectations (exit 2 on failure)
  --components path      components.xml (default: found next to/above the exe or cwd)
  --keep-backups         keep TurnGenerator's per-year backup folders

Exit codes: 0 ok, 1 invariant failure or crash, 2 behaviour expectation failed, 3 compare differs, 64 usage.");
        }

        // ------------------------------------------------------------------------ commands

        private static int Run(Options options)
        {
            SimulationConfig config = BuildConfig(options);
            string outDir = options.Get("out") ?? Path.Combine(Directory.GetCurrentDirectory(), "sim-out", "seed-" + config.Seed);
            return RunAndReport(config, outDir, options.Has("behaviour"), resumeFrom: null);
        }

        private static int Replay(Options options)
        {
            string game = options.Require("game");
            SimulationConfig config = BuildConfig(options, requirePlayers: false);
            string outDir = options.Get("out");
            if (outDir == null)
            {
                // Continue in place: no copy.
                config.WorkFolder = null;
            }

            return RunAndReport(config, outDir, options.Has("behaviour"), resumeFrom: game);
        }

        private static int RunAndReport(SimulationConfig config, string outDir, bool behaviour, string resumeFrom)
        {
            if (outDir != null)
            {
                outDir = Path.GetFullPath(outDir);
                Directory.CreateDirectory(outDir);
                config.WorkFolder = outDir;
                config.DeleteWorkFolder = false;
            }

            Console.WriteLine((resumeFrom == null ? "Running " : "Resuming " + resumeFrom + " for ") + config.Describe());
            SimulationRunner runner = new SimulationRunner(config)
            {
                AfterTurn = context =>
                {
                    if (context.Turn % 10 == 0 || context.Violations.Count > 0)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  turn {0,4} year {1} {2:0.00}s hash {3}{4}", context.Turn, context.Record.Year, context.Record.TotalSeconds, context.Record.StateHash, context.Violations.Count > 0 ? "  " + context.Violations.Count + " violation(s)" : string.Empty));
                    }
                },
            };

            SimulationResult result = resumeFrom == null ? runner.Run() : runner.Resume(resumeFrom);

            StringBuilder report = new StringBuilder(result.Summary());
            List<ExpectationOutcome> outcomes = null;
            if (behaviour || result.Turns.Count >= 50)
            {
                outcomes = AiBehaviourAssertions.Evaluate(result, AiBehaviourAssertions.ForNormalGame());
                report.AppendLine("AI behaviour:");
                report.Append(AiBehaviourAssertions.ObservedBehaviour(result));
                foreach (ExpectationOutcome outcome in outcomes)
                {
                    report.AppendLine("  " + outcome);
                }
            }

            Console.Write(report.ToString());

            if (outDir != null)
            {
                result.WriteCsv(Path.Combine(outDir, "metrics.csv"));
                result.WriteJson(Path.Combine(outDir, "result.json"));
                File.WriteAllText(Path.Combine(outDir, "summary.txt"), report.ToString());
                Console.WriteLine("Wrote " + Path.Combine(outDir, "metrics.csv") + ", result.json, summary.txt" + (result.GameFolder != null ? "; game in " + result.GameFolder : string.Empty));
            }

            if (!result.Passed)
            {
                return ExitInvariantFailure;
            }

            if (behaviour && outcomes != null && outcomes.Any(o => !o.Passed))
            {
                return ExitBehaviourFailure;
            }

            return ExitOk;
        }

        private static int Batch(Options options)
        {
            List<int> seeds = ParseSeeds(options.Get("seeds") ?? "1-5");
            string outDir = Path.GetFullPath(options.Get("out") ?? Path.Combine(Directory.GetCurrentDirectory(), "sim-batch"));
            int jobs = Math.Max(1, options.GetInt("jobs") ?? 1);
            Directory.CreateDirectory(outDir);

            Dictionary<int, int> exitCodes = new Dictionary<int, int>();
            if (jobs == 1)
            {
                foreach (int seed in seeds)
                {
                    Options single = options.With("seed", seed.ToString(CultureInfo.InvariantCulture));
                    SimulationConfig config = BuildConfig(single);
                    exitCodes[seed] = RunAndReport(config, Path.Combine(outDir, "seed-" + seed), single.Has("behaviour"), null);
                }
            }
            else
            {
                exitCodes = RunChildren(options, seeds, outDir, jobs);
            }

            // Aggregate.
            StringBuilder csv = new StringBuilder("Seed,Exit,Turns,Completed,Violations,Rules,Fatal,ElapsedSeconds,FinalYear,Battles,RejectedOrders,Planets,Scores\n");
            int failures = 0;
            foreach (int seed in seeds)
            {
                string json = Path.Combine(outDir, "seed-" + seed, "result.json");
                exitCodes.TryGetValue(seed, out int exit);
                if (exit != ExitOk)
                {
                    failures++;
                }

                if (!File.Exists(json))
                {
                    csv.AppendLine(seed + "," + exit + ",0,false,,,no result.json,,,,,,");
                    continue;
                }

                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(json)))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement turns = root.GetProperty("Turns");
                    JsonElement violations = root.GetProperty("Violations");
                    JsonElement last = turns.GetArrayLength() > 0 ? turns[turns.GetArrayLength() - 1] : default;
                    string rules = string.Join(" ", violations.EnumerateArray().Select(v => v.GetProperty("Rule").GetString()).Distinct());
                    string planets = last.ValueKind == JsonValueKind.Object ? string.Join(" ", last.GetProperty("Empires").EnumerateArray().Select(e => e.GetProperty("Planets").GetInt32())) : string.Empty;
                    string scores = last.ValueKind == JsonValueKind.Object ? string.Join(" ", last.GetProperty("Empires").EnumerateArray().Select(e => e.GetProperty("Score").GetInt32())) : string.Empty;
                    int battles = turns.EnumerateArray().Sum(t => t.GetProperty("Battles").GetInt32());
                    int rejected = turns.EnumerateArray().Sum(t => t.GetProperty("RejectedOrders").GetInt32());
                    string fatal = root.TryGetProperty("FatalError", out JsonElement f) && f.ValueKind == JsonValueKind.String ? Quote(FirstLine(f.GetString())) : string.Empty;
                    csv.AppendLine(string.Join(",",
                        seed,
                        exit,
                        turns.GetArrayLength(),
                        root.GetProperty("Completed").GetBoolean(),
                        violations.GetArrayLength(),
                        Quote(rules),
                        fatal,
                        root.GetProperty("ElapsedSeconds").GetDouble().ToString("0.0", CultureInfo.InvariantCulture),
                        last.ValueKind == JsonValueKind.Object ? last.GetProperty("Year").GetInt32().ToString(CultureInfo.InvariantCulture) : string.Empty,
                        battles,
                        rejected,
                        Quote(planets),
                        Quote(scores)));
                }
            }

            File.WriteAllText(Path.Combine(outDir, "batch.csv"), csv.ToString());
            Console.WriteLine();
            Console.WriteLine("Batch: " + seeds.Count + " seeds, " + failures + " failing; " + Path.Combine(outDir, "batch.csv"));
            Console.Write(csv.ToString());
            return failures == 0 ? ExitOk : (exitCodes.Values.Any(code => code == ExitInvariantFailure) ? ExitInvariantFailure : ExitBehaviourFailure);
        }

        private static Dictionary<int, int> RunChildren(Options options, List<int> seeds, string outDir, int jobs)
        {
            Dictionary<int, int> exitCodes = new Dictionary<int, int>();
            Queue<int> pending = new Queue<int>(seeds);
            List<(int Seed, Process Process)> running = new List<(int, Process)>();

            while (pending.Count > 0 || running.Count > 0)
            {
                while (running.Count < jobs && pending.Count > 0)
                {
                    int seed = pending.Dequeue();
                    string seedOut = Path.Combine(outDir, "seed-" + seed);
                    Directory.CreateDirectory(seedOut);
                    ProcessStartInfo start = SelfStartInfo();
                    start.ArgumentList.Add("run");
                    foreach (string arg in options.With("seed", seed.ToString(CultureInfo.InvariantCulture)).With("out", seedOut).Without("seeds").Without("jobs").ToArgs())
                    {
                        start.ArgumentList.Add(arg);
                    }

                    start.RedirectStandardOutput = true;
                    start.RedirectStandardError = true;
                    start.UseShellExecute = false;
                    Process process = Process.Start(start);
                    string log = Path.Combine(seedOut, "console.txt");
                    StreamWriter writer = new StreamWriter(log) { AutoFlush = true };
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (writer) { writer.WriteLine(e.Data); } } };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (writer) { writer.WriteLine(e.Data); } } };
                    process.Exited += (s, e) => { };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    running.Add((seed, process));
                    Console.WriteLine("  started seed " + seed + " (pid " + process.Id + ")");
                }

                System.Threading.Thread.Sleep(250);
                foreach ((int seed, Process process) in running.ToList())
                {
                    if (process.HasExited)
                    {
                        process.WaitForExit();
                        exitCodes[seed] = process.ExitCode;
                        Console.WriteLine("  seed " + seed + " finished, exit " + process.ExitCode);
                        running.Remove((seed, process));
                    }
                }
            }

            return exitCodes;
        }

        private static ProcessStartInfo SelfStartInfo()
        {
            string host = Environment.ProcessPath;
            ProcessStartInfo start = new ProcessStartInfo(host);
            if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            }

            return start;
        }

        private static int Compare(Options options)
        {
            if (options.Positional.Count != 2)
            {
                throw new UsageException("compare needs two CSV files");
            }

            List<string> differences = SimulationMetrics.Compare(options.Positional[0], options.Positional[1]);
            if (differences.Count == 0)
            {
                Console.WriteLine("Identical: " + options.Positional[0] + " and " + options.Positional[1]);
                return ExitOk;
            }

            Console.WriteLine("Differences between " + options.Positional[0] + " and " + options.Positional[1] + ":");
            foreach (string difference in differences)
            {
                Console.WriteLine("  " + difference);
            }

            return ExitDifferent;
        }

        /// <summary>The plain host batch loop (AutoTurnGenerator) on an existing game, isolated
        /// from the shared nova.conf; any key press aborts between turns.</summary>
        private static int Generate(Options options)
        {
            string game = Path.GetFullPath(options.Require("game"));
            int turns = options.GetInt("turns") ?? 10;
            string root = Path.Combine(Path.GetTempPath(), "NovaSim", "generate-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (SimulationEnvironment environment = SimulationEnvironment.Enter(root, options.Get("components")))
            {
                string settingsFile = Directory.GetFiles(game, "*" + Global.SettingsExtension).FirstOrDefault();
                if (settingsFile != null)
                {
                    GameSettings.Data.SettingsPathName = settingsFile;
                    GameSettings.Restore();
                    GameSettings.Data.SettingsPathName = settingsFile;
                }

                string statePath = Directory.GetFiles(game, "*" + Global.ServerStateExtension).FirstOrDefault() ?? throw new UsageException("no .sstate in " + game);
                ServerData state = new ServerData { StatePathName = statePath };
                state.Restore();
                state.GameFolder = game;
                state.StatePathName = statePath;

                AutoTurnGenerator host = new AutoTurnGenerator(state) { PublishGameFolder = environment.SetGameFolder };
                Console.WriteLine("Generating up to " + turns + " turns from year " + state.TurnYear + " (press any key to abort)...");
                AutoGenerateResult outcome = host.GenerateTurns(
                    turns,
                    abortRequested: () => !Console.IsInputRedirected && Console.KeyAvailable,
                    afterTurn: step =>
                    {
                        Console.WriteLine("  year " + host.State.TurnYear + (step.AiFailures.Count > 0 ? "  (" + step.AiFailures.Count + " AI failure(s))" : string.Empty));
                    });
                List<string> errors = environment.TakeErrors();
                Console.WriteLine("Generated " + outcome.TurnsGenerated + " turn(s)" + (outcome.Aborted ? " (aborted)" : string.Empty) + "; now year " + host.State.TurnYear + "; " + errors.Count + " reported error(s).");
                foreach (string error in errors.Take(10))
                {
                    Console.WriteLine("  " + FirstLine(error));
                }

                return errors.Count == 0 ? ExitOk : ExitInvariantFailure;
            }
        }

        /// <summary>Diagnostics: times the pieces of saving a game's state.</summary>
        private static int ProfileSave(Options options)
        {
            string game = Path.GetFullPath(options.Require("game"));
            string root = Path.Combine(Path.GetTempPath(), "NovaSim", "profile-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (SimulationEnvironment environment = SimulationEnvironment.Enter(root, options.Get("components")))
            {
                environment.SetGameFolder(game);
                string statePath = Directory.GetFiles(game, "*" + Global.ServerStateExtension).First();
                Stopwatch clock = Stopwatch.StartNew();
                ServerData state = new ServerData { StatePathName = statePath };
                state.Restore();
                Console.WriteLine("restore " + clock.Elapsed.TotalMilliseconds + " ms");
                string scratch = Path.Combine(root, "x.sstate");
                state.StatePathName = scratch;
                for (int i = 0; i < 3; i++)
                {
                    clock.Restart();
                    state.Save();
                    Console.WriteLine("save " + clock.Elapsed.TotalMilliseconds + " ms");
                }

                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                foreach (EmpireData empire in state.AllEmpires.Values)
                {
                    clock.Restart();
                    empire.ToXml(doc);
                    Console.WriteLine("empire " + empire.Id + " " + clock.Elapsed.TotalMilliseconds + " ms; designs " + empire.Designs.Count + " fleets " + empire.OwnedFleets.Count + " starreports " + empire.StarReports.Count);
                    clock.Restart();
                    foreach (Nova.Common.Components.ShipDesign design in empire.Designs.Values)
                    {
                        design.ToXml(doc);
                    }

                    Console.WriteLine("   designs " + clock.Elapsed.TotalMilliseconds + " ms");
                    clock.Restart();
                    foreach (Fleet fleet in empire.OwnedFleets.Values)
                    {
                        fleet.ToXml(doc);
                    }

                    Console.WriteLine("   fleets " + clock.Elapsed.TotalMilliseconds + " ms");
                    clock.Restart();
                    foreach (StarIntel report in empire.StarReports.Values)
                    {
                        report.ToXml(doc);
                    }

                    Console.WriteLine("   star reports " + clock.Elapsed.TotalMilliseconds + " ms");
                    clock.Restart();
                    foreach (EmpireIntel report in empire.EmpireReports.Values)
                    {
                        report.ToXml(doc);
                    }

                    Console.WriteLine("   empire reports " + clock.Elapsed.TotalMilliseconds + " ms");
                }

                clock.Restart();
                foreach (Star star in state.AllStars.Values)
                {
                    star.ToXml(doc);
                }

                Console.WriteLine("stars " + clock.Elapsed.TotalMilliseconds + " ms (" + state.AllStars.Count + ")");
                return ExitOk;
            }
        }

        // ------------------------------------------------------------------------ config

        public static SimulationConfig BuildConfig(Options options, bool requirePlayers = true)
        {
            SimulationConfig config = new SimulationConfig
            {
                Seed = options.GetInt("seed") ?? 1,
                Turns = options.GetInt("turns") ?? 100,
                GalaxySize = options.GetEnum("size", GalaxySize.Small),
                Density = options.GetEnum("density", GalaxyDensity.Normal),
                StartingDistance = options.GetEnum("distance", StartingDistance.Moderate),
                NumberOfStars = options.GetInt("stars"),
                AcceleratedStart = options.Has("accelerated"),
                NoRandomEvents = options.Has("no-random-events"),
                SlowTechAdvance = options.Has("slow-tech"),
                MaximumMinerals = options.Has("max-minerals"),
                GalaxyClumping = options.Has("clumping"),
                ReloadEvery = options.GetInt("reload-every") ?? 5,
                CheckRoundTrip = !options.Has("no-roundtrip"),
                KeepTurnBackups = options.Has("keep-backups"),
                StopOnViolation = options.Has("stop-on-violation"),
                ComponentsFile = options.Get("components"),
            };

            string disabled = options.Get("disable");
            if (disabled != null)
            {
                foreach (string rule in disabled.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    config.DisabledInvariants.Add(rule.Trim());
                }
            }

            double? maxSeconds = options.GetDouble("max-turn-seconds");
            if (maxSeconds.HasValue)
            {
                config.Limits.MaxTurnSeconds = maxSeconds.Value;
            }

            int? maxMessages = options.GetInt("max-messages");
            if (maxMessages.HasValue)
            {
                config.Limits.MaxMessagesPerEmpirePerTurn = maxMessages.Value;
            }

            string players = options.Get("players");
            if (players == null)
            {
                if (requirePlayers)
                {
                    config.Players = SimulationConfig.DefaultPlayers(4);
                }
            }
            else if (int.TryParse(players, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                string tierText = options.Get("tier") ?? "Standard";
                int tier = PlayerSpec.Parse("0/" + tierText).Tier;
                config.Players = SimulationConfig.DefaultPlayers(count, tier);
            }
            else
            {
                config.Players = players.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(PlayerSpec.Parse).ToList();
            }

            return config;
        }

        public static List<int> ParseSeeds(string text)
        {
            List<int> seeds = new List<int>();
            foreach (string part in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] range = part.Split('-');
                if (range.Length == 2)
                {
                    int from = int.Parse(range[0], CultureInfo.InvariantCulture);
                    int to = int.Parse(range[1], CultureInfo.InvariantCulture);
                    for (int seed = from; seed <= to; seed++)
                    {
                        seeds.Add(seed);
                    }
                }
                else
                {
                    seeds.Add(int.Parse(part, CultureInfo.InvariantCulture));
                }
            }

            return seeds;
        }

        private static string Quote(string text)
        {
            return "\"" + (text ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        private static string FirstLine(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            string flat = text.Replace("\r", string.Empty);
            int newline = flat.IndexOf('\n');
            string line = newline >= 0 ? flat.Substring(0, newline) : flat;
            return line.Length > 200 ? line.Substring(0, 200) : line;
        }

        public sealed class UsageException : Exception
        {
            public UsageException(string message)
                : base(message)
            {
            }
        }

        /// <summary>--key value / --flag / positional argument bag.</summary>
        public sealed class Options
        {
            private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public List<string> Positional { get; } = new List<string>();

            private static readonly HashSet<string> Flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "accelerated", "no-random-events", "slow-tech", "max-minerals", "clumping", "no-roundtrip",
                "keep-backups", "stop-on-violation", "behaviour",
            };

            public static Options Parse(IEnumerable<string> args)
            {
                Options options = new Options();
                List<string> list = args.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    string arg = list[i];
                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        string key = arg.Substring(2);
                        string value = null;
                        int equals = key.IndexOf('=');
                        if (equals >= 0)
                        {
                            value = key.Substring(equals + 1);
                            key = key.Substring(0, equals);
                        }
                        else if (!Flags.Contains(key))
                        {
                            if (i + 1 >= list.Count)
                            {
                                throw new UsageException("--" + key + " needs a value");
                            }

                            value = list[++i];
                        }

                        options.values[key] = value ?? "true";
                    }
                    else
                    {
                        options.Positional.Add(arg);
                    }
                }

                return options;
            }

            public bool Has(string key)
            {
                return values.TryGetValue(key, out string value) && value != "false";
            }

            public string Get(string key)
            {
                return values.TryGetValue(key, out string value) ? value : null;
            }

            public string Require(string key)
            {
                return Get(key) ?? throw new UsageException("--" + key + " is required");
            }

            public int? GetInt(string key)
            {
                string value = Get(key);
                if (value == null)
                {
                    return null;
                }

                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    throw new UsageException("--" + key + " needs a whole number, got '" + value + "'");
                }

                return parsed;
            }

            public double? GetDouble(string key)
            {
                string value = Get(key);
                if (value == null)
                {
                    return null;
                }

                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    throw new UsageException("--" + key + " needs a number, got '" + value + "'");
                }

                return parsed;
            }

            public T GetEnum<T>(string key, T fallback)
                where T : struct
            {
                string value = Get(key);
                if (value == null)
                {
                    return fallback;
                }

                if (!Enum.TryParse(value, true, out T parsed))
                {
                    throw new UsageException("--" + key + ": unknown value '" + value + "' (expected " + string.Join("|", Enum.GetNames(typeof(T))) + ")");
                }

                return parsed;
            }

            public Options With(string key, string value)
            {
                Options copy = Copy();
                copy.values[key] = value;
                return copy;
            }

            public Options Without(string key)
            {
                Options copy = Copy();
                copy.values.Remove(key);
                return copy;
            }

            public IEnumerable<string> ToArgs()
            {
                foreach (KeyValuePair<string, string> pair in values)
                {
                    if (Flags.Contains(pair.Key))
                    {
                        if (pair.Value != "false")
                        {
                            yield return "--" + pair.Key;
                        }
                    }
                    else
                    {
                        yield return "--" + pair.Key;
                        yield return pair.Value;
                    }
                }
            }

            private Options Copy()
            {
                Options copy = new Options();
                foreach (KeyValuePair<string, string> pair in values)
                {
                    copy.values[pair.Key] = pair.Value;
                }

                copy.Positional.AddRange(Positional);
                return copy;
            }
        }
    }
}
