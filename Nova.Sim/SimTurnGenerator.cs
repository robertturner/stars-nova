namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    using Nova.Ai;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Server;

    /// <summary>What one turn generation saw: commands rejected by ICommand.IsValid (by empire
    /// and command type) and the messages it delivered.</summary>
    public sealed class GenerationObservations
    {
        public Dictionary<int, int> RejectedByEmpire { get; } = new Dictionary<int, int>();

        public Dictionary<string, int> RejectedByType { get; } = new Dictionary<string, int>();

        public Dictionary<int, int> AcceptedByEmpire { get; } = new Dictionary<int, int>();

        /// <summary>A short description of each rejected command (bounded).</summary>
        public List<string> RejectedSamples { get; } = new List<string>();

        public List<Message> Messages { get; } = new List<Message>();

        /// <summary>Seconds spent writing the intel files.</summary>
        public double IntelSeconds { get; set; }

        public int TotalRejected
        {
            get { return RejectedByEmpire.Values.Sum(); }
        }
    }

    /// <summary>
    /// The real TurnGenerator with three observation seams overridden (all protected virtual in
    /// TurnGenerator): ParseCommands wraps each command so a rejection by IsValid is counted
    /// (then lets the base class apply them exactly as usual); WriteIntel snapshots the turn's
    /// messages before they are cleared; BackupTurn is skipped unless backups are wanted (they
    /// copy the whole game folder every year).
    /// </summary>
    public sealed class SimTurnGenerator : TurnGenerator
    {
        private const int MaxSamples = 20;

        private readonly ServerData state;
        private readonly bool keepBackups;

        public GenerationObservations Observations { get; } = new GenerationObservations();

        public SimTurnGenerator(ServerData serverState, bool keepBackups)
            : base(serverState)
        {
            state = serverState;
            this.keepBackups = keepBackups;
        }

        protected override void BackupTurn()
        {
            if (keepBackups)
            {
                base.BackupTurn();
            }
        }

        protected override void ParseCommands()
        {
            foreach (int empireId in state.AllCommands.Keys.ToList())
            {
                Stack<ICommand> commands = state.AllCommands[empireId];
                ICommand[] topFirst = commands.ToArray();
                Stack<ICommand> wrapped = new Stack<ICommand>();
                for (int i = topFirst.Length - 1; i >= 0; i--)
                {
                    ICommand command = topFirst[i];

                    // TurnGenerator.ParseCommands type-tests DetonateCommand after applying it;
                    // a wrapper would hide that, so detonations pass through uncounted.
                    wrapped.Push(command is DetonateCommand ? command : new CountingCommand(command, empireId, this));
                }

                state.AllCommands[empireId] = wrapped;
            }

            base.ParseCommands();
        }

        protected override void WriteIntel()
        {
            Observations.Messages.AddRange(state.AllMessages);
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            base.WriteIntel();
            Observations.IntelSeconds = clock.Elapsed.TotalSeconds;
        }

        private void Record(int empireId, ICommand command, bool valid)
        {
            Dictionary<int, int> target = valid ? Observations.AcceptedByEmpire : Observations.RejectedByEmpire;
            target.TryGetValue(empireId, out int count);
            target[empireId] = count + 1;

            if (valid)
            {
                return;
            }

            string type = command.GetType().Name;
            Observations.RejectedByType.TryGetValue(type, out int byType);
            Observations.RejectedByType[type] = byType + 1;

            if (Observations.RejectedSamples.Count < MaxSamples)
            {
                Observations.RejectedSamples.Add("empire " + empireId + " " + type + ": " + Describe(command));
            }
        }

        private static string Describe(ICommand command)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                string xml = command.ToXml(document).OuterXml;
                return xml.Length > 300 ? xml.Substring(0, 300) + "..." : xml;
            }
            catch (Exception e)
            {
                return "(could not describe: " + e.Message + ")";
            }
        }

        /// <summary>Delegates everything; records IsValid's verdict.</summary>
        private sealed class CountingCommand : ICommand
        {
            private readonly ICommand inner;
            private readonly int empireId;
            private readonly SimTurnGenerator owner;

            public CountingCommand(ICommand inner, int empireId, SimTurnGenerator owner)
            {
                this.inner = inner;
                this.empireId = empireId;
                this.owner = owner;
            }

            public bool IsValid(EmpireData empire)
            {
                bool valid = inner.IsValid(empire);
                owner.Record(empireId, inner, valid);
                return valid;
            }

            public void ApplyToState(EmpireData empire)
            {
                inner.ApplyToState(empire);
            }

            public XmlElement ToXml(XmlDocument xmldoc)
            {
                return inner.ToXml(xmldoc);
            }
        }
    }

    /// <summary>DefaultAi with its Random seeded (DefaultAi's AiRandom seam), so an AI turn is
    /// reproducible from (simulation seed, player, year).</summary>
    public sealed class SeededDefaultAi : DefaultAi
    {
        public SeededDefaultAi(int seed)
        {
            AiRandom = new Random(seed);
        }

        /// <summary>A stable mix of the three numbers (string.GetHashCode is randomised per
        /// process, so it is not used).</summary>
        public static int SeedFor(int simulationSeed, int playerNumber, int year)
        {
            unchecked
            {
                int hash = (int)2166136261;
                hash = (hash ^ simulationSeed) * 16777619;
                hash = (hash ^ playerNumber) * 16777619;
                hash = (hash ^ year) * 16777619;
                return hash & int.MaxValue;
            }
        }
    }
}
