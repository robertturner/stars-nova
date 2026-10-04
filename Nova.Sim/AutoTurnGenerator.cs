namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Server;
    using Nova.Server.NewGame;

    /// <summary>What happened in one generated turn of an <see cref="AutoTurnGenerator"/> run.</summary>
    public sealed class AutoTurnStep
    {
        /// <summary>1-based index within this batch.</summary>
        public int Index { get; set; }

        /// <summary>The year the turn was played in (before generation).</summary>
        public int YearPlayed { get; set; }

        public ServerData State { get; set; }

        public TurnGenerator Generator { get; set; }

        public Dictionary<int, AiSubmission> Submissions { get; } = new Dictionary<int, AiSubmission>();

        /// <summary>AI empires whose orders OrderReader did not accept.</summary>
        public HashSet<int> OrdersNotAccepted { get; } = new HashSet<int>();

        /// <summary>AI empires whose move threw (the turn still generates without their orders).</summary>
        public List<KeyValuePair<int, Exception>> AiFailures { get; } = new List<KeyValuePair<int, Exception>>();

        public double AiSeconds { get; set; }

        public double GenerateSeconds { get; set; }

        /// <summary>Part of the AI time spent loading intel (ClientData.Initialize).</summary>
        public double AiLoadSeconds { get; set; }

        /// <summary>Part of GenerateSeconds spent saving the state.</summary>
        public double SaveSeconds { get; set; }
    }

    public sealed class AutoGenerateResult
    {
        public int TurnsGenerated { get; set; }

        public bool Aborted { get; set; }
    }

    /// <summary>
    /// Batch / auto turn generation (turn-generation-engine.md, host commands: "three additional
    /// command variants request generating 10, 100, or 1000 turns in a row automatically, looping
    /// the same generate/advance sequence that a single manual turn uses", aborted early by the
    /// host's abort check, polled once per iteration). Each iteration is the single-turn host
    /// sequence of TurnHost / NovaConsole: every computer player that has not turned in computes
    /// and writes its orders (DefaultAi.DoMove, OrderWriter), the orders are read (OrderReader),
    /// the turn is generated (TurnGenerator.Generate) and the state saved (ServerData.Save).
    /// Players who have not submitted (humans) simply contribute no orders, as with the manual
    /// "Generate" command.
    /// </summary>
    /// <remarks>
    /// The abort test is a callback rather than the original's held-key poll so any host (CLI,
    /// UI, tests) can supply its own. The AI and turn-generator factories are seams for the
    /// simulation harness (seeded AIs, an observing generator).
    /// </remarks>
    public sealed class AutoTurnGenerator
    {
        /// <summary>The three batch sizes of the host's batch commands.</summary>
        public static readonly IReadOnlyList<int> BatchSizes = new[] { 10, 100, 1000 };

        public AutoTurnGenerator(ServerData state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>The game being advanced; a caller may swap in a reloaded copy between turns.</summary>
        public ServerData State { get; set; }

        /// <summary>Builds the AI for one player and turn; default a plain DefaultAi.</summary>
        public Func<PlayerSettings, int, AbstractAI> AiFactory { get; set; } = (settings, year) => new DefaultAi();

        /// <summary>Builds the turn generator; default the real TurnGenerator.</summary>
        public Func<ServerData, TurnGenerator> GeneratorFactory { get; set; } = state => new TurnGenerator(state);

        /// <summary>Called before the AIs run with the game folder; ClientData.Initialize finds
        /// the game folder through nova.conf's ServerFolder, so the default records it there.</summary>
        public Action<string> PublishGameFolder { get; set; } = DefaultPublishGameFolder;

        /// <summary>Save the state after every generated turn (the host always does).</summary>
        public bool SaveAfterEachTurn { get; set; } = true;

        /// <summary>
        /// Generates up to <paramref name="count"/> turns. <paramref name="abortRequested"/> is
        /// polled before every turn; <paramref name="afterTurn"/> sees each finished turn (and may
        /// replace <see cref="State"/>).
        /// </summary>
        public AutoGenerateResult GenerateTurns(int count, Func<bool> abortRequested = null, Action<AutoTurnStep> afterTurn = null)
        {
            AutoGenerateResult result = new AutoGenerateResult();
            for (int i = 1; i <= count; i++)
            {
                if (abortRequested != null && abortRequested())
                {
                    result.Aborted = true;
                    break;
                }

                AutoTurnStep step = GenerateOne(i);
                result.TurnsGenerated++;
                afterTurn?.Invoke(step);
            }

            return result;
        }

        /// <summary>One host turn: pending AI moves, read orders, generate, save.</summary>
        public AutoTurnStep GenerateOne(int index = 1)
        {
            AutoTurnStep step = new AutoTurnStep { Index = index, YearPlayed = State.TurnYear, State = State };

            Stopwatch ai = Stopwatch.StartNew();
            RunPendingAiTurns(step);
            ai.Stop();
            step.AiSeconds = ai.Elapsed.TotalSeconds;

            new OrderReader(State).ReadOrders();
            foreach (PlayerSettings settings in State.AllPlayers)
            {
                if (!Gameinitializer.IsHumanPlayer(settings) && !IsTurnedIn(State, settings))
                {
                    step.OrdersNotAccepted.Add(settings.PlayerNumber);
                }
            }

            Stopwatch generate = Stopwatch.StartNew();
            TurnGenerator generator = GeneratorFactory(State);
            step.Generator = generator;
            generator.Generate();
            if (SaveAfterEachTurn)
            {
                Stopwatch save = Stopwatch.StartNew();
                State.Save();
                step.SaveSeconds = save.Elapsed.TotalSeconds;
            }

            generate.Stop();
            step.GenerateSeconds = generate.Elapsed.TotalSeconds;
            return step;
        }

        /// <summary>Runs every computer player that has not turned in this year.</summary>
        public void RunPendingAiTurns(AutoTurnStep step)
        {
            PublishGameFolder?.Invoke(State.GameFolder);

            // The orders files already on disk decide who has turned in (TurnHost's rule).
            new OrderReader(State).ReadOrders();

            foreach (PlayerSettings settings in State.AllPlayers)
            {
                if (Gameinitializer.IsHumanPlayer(settings) || IsTurnedIn(State, settings))
                {
                    continue;
                }

                try
                {
                    step.Submissions[settings.PlayerNumber] = RunOneAiTurn(settings, step);
                }
                catch (Exception e)
                {
                    step.AiFailures.Add(new KeyValuePair<int, Exception>(settings.PlayerNumber, e));
                }
            }
        }

        private AiSubmission RunOneAiTurn(PlayerSettings settings, AutoTurnStep step)
        {
            CommandArguments args = new CommandArguments();
            args.Add(CommandArguments.Option.RaceName, settings.RaceName);
            args.Add(CommandArguments.Option.Turn, State.TurnYear);
            args.Add(CommandArguments.Option.IntelFileName, Path.Combine(State.GameFolder, settings.RaceName + Global.IntelExtension));

            // The built-in AI picker stores the archetype as the spec's AI category
            // (ai-opponent-behavior.md section 1a); DefaultAi takes it as its -n personality code.
            if (settings.AiCategory >= 0)
            {
                args.Add(CommandArguments.Option.AiPersonality, NewGameSetup.PersonalityCodeForCategory(settings.AiCategory));
            }

            AbstractAI ai = AiFactory(settings, State.TurnYear);
            Stopwatch load = Stopwatch.StartNew();
            ai.Initialize(args);
            step.AiLoadSeconds += load.Elapsed.TotalSeconds;
            ai.DoMove();

            ClientData client = ai.ClientState;
            AiSubmission submission = new AiSubmission
            {
                EmpireId = settings.PlayerNumber,
                Commands = client.Commands.Count,
                QueuedOrders = client.EmpireState.OwnedStars.Values
                    .Where(star => star.Owner == client.EmpireState.Id && star.ManufacturingQueue != null)
                    .Sum(star => star.ManufacturingQueue.Queue.Count),
            };

            foreach (ICommand command in client.Commands)
            {
                string type = command.GetType().Name;
                submission.CommandsByType.TryGetValue(type, out int count);
                submission.CommandsByType[type] = count + 1;
            }

            new OrderWriter(client).WriteOrders();
            return submission;
        }

        public static bool IsTurnedIn(ServerData state, PlayerSettings settings)
        {
            return state.AllEmpires.TryGetValue(settings.PlayerNumber, out EmpireData empire)
                && empire.TurnYear == state.TurnYear
                && empire.TurnSubmitted;
        }

        private static void DefaultPublishGameFolder(string gameFolder)
        {
            using (Config conf = new Config())
            {
                if (conf[Global.ServerFolderKey] != gameFolder)
                {
                    conf[Global.ServerFolderKey] = gameFolder;
                }
            }
        }
    }
}
