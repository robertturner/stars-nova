namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    using Nova.Ai;

    /// <summary>One simulated turn as the harness saw it.</summary>
    public sealed class TurnRecord
    {
        public int Turn { get; set; }

        /// <summary>The year after generation.</summary>
        public int Year { get; set; }

        public double AiSeconds { get; set; }

        public double GenerateSeconds { get; set; }

        public double TotalSeconds { get; set; }

        /// <summary>Breakdown: AI intel loading, intel writing, state saving.</summary>
        public double AiLoadSeconds { get; set; }

        public double IntelSeconds { get; set; }

        public double SaveSeconds { get; set; }

        /// <summary>StateHasher hash of the saved state after this turn.</summary>
        public string StateHash { get; set; }

        public bool Reloaded { get; set; }

        public bool RoundTripChecked { get; set; }

        public string RoundTripDifference { get; set; }

        public int Messages { get; set; }

        public int RejectedOrders { get; set; }

        public Dictionary<string, int> RejectedByType { get; set; } = new Dictionary<string, int>();

        public List<string> Errors { get; set; } = new List<string>();

        public int Battles { get; set; }

        public string VictoryMessage { get; set; }

        public int Violations { get; set; }

        public List<EmpireTurnMetrics> Empires { get; set; } = new List<EmpireTurnMetrics>();
    }

    /// <summary>The outcome of one simulation run.</summary>
    public sealed class SimulationResult
    {
        public SimulationConfig Config { get; set; }

        public string GameFolder { get; set; }

        /// <summary>Player number -> the template/category it plays.</summary>
        public Dictionary<int, PlayerSpec> Players { get; set; } = new Dictionary<int, PlayerSpec>();

        public List<TurnRecord> Turns { get; set; } = new List<TurnRecord>();

        public List<InvariantViolation> Violations { get; set; } = new List<InvariantViolation>();

        /// <summary>The exception that stopped the run, if any.</summary>
        public string FatalError { get; set; }

        public bool Completed { get; set; }

        public double ElapsedSeconds { get; set; }

        /// <summary>Errors reported while the game was being created.</summary>
        public List<string> SetupErrors { get; set; } = new List<string>();

        [JsonIgnore]
        public IEnumerable<EmpireTurnMetrics> AllMetrics
        {
            get { return Turns.SelectMany(turn => turn.Empires); }
        }

        [JsonIgnore]
        public bool Passed
        {
            get { return FatalError == null && Violations.Count == 0; }
        }

        /// <summary>The per-turn state hashes, in turn order.</summary>
        [JsonIgnore]
        public IReadOnlyList<string> Hashes
        {
            get { return Turns.Select(turn => turn.StateHash).ToList(); }
        }

        public EmpireTurnMetrics Metric(int turn, int empireId)
        {
            TurnRecord record = Turns.FirstOrDefault(t => t.Turn == turn);
            return record == null ? null : record.Empires.FirstOrDefault(e => e.EmpireId == empireId);
        }

        public IEnumerable<EmpireTurnMetrics> MetricsFor(int empireId)
        {
            return AllMetrics.Where(m => m.EmpireId == empireId).OrderBy(m => m.Turn);
        }

        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public void WriteJson(string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }

        public void WriteCsv(string path)
        {
            SimulationMetrics.WriteCsv(AllMetrics, path);
        }

        /// <summary>A one-screen summary.</summary>
        public string Summary(int maxViolations = 15)
        {
            StringBuilder s = new StringBuilder();
            s.AppendLine("Simulation: " + (Config == null ? "(resumed game)" : Config.Describe()));
            s.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Turns run: {0}{1}, {2:0.0} s total, {3:0.00} s/turn (AI {4:0.00} incl. intel load {6:0.00}; generation {5:0.00} incl. intel write {7:0.00}, save {8:0.00})",
                Turns.Count,
                Completed ? string.Empty : " (STOPPED EARLY)",
                ElapsedSeconds,
                Turns.Count == 0 ? 0 : Turns.Average(t => t.TotalSeconds),
                Turns.Count == 0 ? 0 : Turns.Average(t => t.AiSeconds),
                Turns.Count == 0 ? 0 : Turns.Average(t => t.GenerateSeconds),
                Turns.Count == 0 ? 0 : Turns.Average(t => t.AiLoadSeconds),
                Turns.Count == 0 ? 0 : Turns.Average(t => t.IntelSeconds),
                Turns.Count == 0 ? 0 : Turns.Average(t => t.SaveSeconds)));

            TurnRecord last = Turns.LastOrDefault();
            if (last != null)
            {
                s.AppendLine("Final year " + last.Year + ", state hash " + last.StateHash);
                s.AppendLine("  Id Race                    Cat Plnt   Pop(k)   Res  Tech Fleets Ships Col Frt War Scout SB Score Rank");
                foreach (EmpireTurnMetrics m in last.Empires)
                {
                    s.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  {0,2} {1,-23} {2,3} {3,4} {4,8} {5,5} {6,5} {7,6} {8,5} {9,3} {10,3} {11,3} {12,5} {13,2} {14,5} {15,4}{16}",
                        m.EmpireId,
                        Truncate(m.Race, 23),
                        m.Category,
                        m.Planets,
                        m.Population / 1000,
                        m.Resources,
                        m.ResearchTotal,
                        m.Fleets,
                        m.Ships,
                        m.ColonyShips,
                        m.Freighters,
                        m.Warships,
                        m.ScoutShips,
                        m.Starbases,
                        m.Score,
                        m.Rank,
                        m.Eliminated ? " ELIMINATED" : string.Empty));
                }
            }

            int battles = Turns.Sum(t => t.Battles);
            int rejected = Turns.Sum(t => t.RejectedOrders);
            TurnRecord victory = Turns.FirstOrDefault(t => t.VictoryMessage != null);
            s.AppendLine(string.Format(CultureInfo.InvariantCulture, "Battles {0}, rejected orders {1}, reloads {2}, victory {3}", battles, rejected, Turns.Count(t => t.Reloaded), victory == null ? "none" : "turn " + victory.Turn + ": " + victory.VictoryMessage));

            if (FatalError != null)
            {
                s.AppendLine("FATAL: " + FatalError);
            }

            if (Violations.Count == 0)
            {
                s.AppendLine("Invariants: all passed");
            }
            else
            {
                s.AppendLine("Invariant violations: " + Violations.Count + " (" + string.Join(", ", Violations.GroupBy(v => v.Rule).Select(g => g.Key + " x" + g.Count())) + ")");
                foreach (InvariantViolation violation in Violations.Take(maxViolations))
                {
                    s.AppendLine("  " + violation);
                }

                if (Violations.Count > maxViolations)
                {
                    s.AppendLine("  ... " + (Violations.Count - maxViolations) + " more (see result.json)");
                }
            }

            return s.ToString();
        }

        private static string Truncate(string text, int max)
        {
            text = text ?? string.Empty;
            return text.Length > max ? text.Substring(0, max) : text;
        }
    }
}
