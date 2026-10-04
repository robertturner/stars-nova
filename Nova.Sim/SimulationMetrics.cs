namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Nova.Ai;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;

    /// <summary>One empire's state at the end of one simulated turn.</summary>
    public sealed class EmpireTurnMetrics
    {
        public int Turn { get; set; }

        public int Year { get; set; }

        public int EmpireId { get; set; }

        public string Race { get; set; }

        public int Archetype { get; set; }

        public int Tier { get; set; }

        public int Category { get; set; }

        public int Planets { get; set; }

        public long Population { get; set; }

        /// <summary>Summed resource output of the owned planets (the score's Resources row).</summary>
        public long Resources { get; set; }

        public long Ironium { get; set; }

        public long Boranium { get; set; }

        public long Germanium { get; set; }

        public long Factories { get; set; }

        public long Mines { get; set; }

        public long Defenses { get; set; }

        public int Energy { get; set; }

        public int Weapons { get; set; }

        public int Propulsion { get; set; }

        public int Construction { get; set; }

        public int Electronics { get; set; }

        public int Biotechnology { get; set; }

        public int ResearchTotal { get; set; }

        public int Fleets { get; set; }

        public int Ships { get; set; }

        public int ScoutShips { get; set; }

        public int ColonyShips { get; set; }

        public int Freighters { get; set; }

        public int Warships { get; set; }

        public int Bombers { get; set; }

        public int Minelayers { get; set; }

        public int OtherShips { get; set; }

        public int Starbases { get; set; }

        public int ShipDesigns { get; set; }

        public int StarbaseDesigns { get; set; }

        /// <summary>Components available to the race (its "techs").</summary>
        public int Techs { get; set; }

        public int Score { get; set; }

        public int Rank { get; set; }

        public int Minefields { get; set; }

        public long MinesLaid { get; set; }

        public int Packets { get; set; }

        /// <summary>Battle reports this empire received this turn.</summary>
        public int Battles { get; set; }

        public int Messages { get; set; }

        /// <summary>Commands in the AI's orders this turn.</summary>
        public int Orders { get; set; }

        public int RejectedOrders { get; set; }

        /// <summary>Production orders across all owned planets when the AI submitted.</summary>
        public int QueuedOrders { get; set; }

        public bool Eliminated { get; set; }

        /// <summary>The CSV columns, in order (also the order Compare reports).</summary>
        public static readonly IReadOnlyList<(string Name, Func<EmpireTurnMetrics, string> Get)> Columns = new (string, Func<EmpireTurnMetrics, string>)[]
        {
            ("Turn", m => I(m.Turn)),
            ("Year", m => I(m.Year)),
            ("EmpireId", m => I(m.EmpireId)),
            ("Race", m => m.Race),
            ("Archetype", m => I(m.Archetype)),
            ("Tier", m => I(m.Tier)),
            ("Category", m => I(m.Category)),
            ("Planets", m => I(m.Planets)),
            ("Population", m => I(m.Population)),
            ("Resources", m => I(m.Resources)),
            ("Ironium", m => I(m.Ironium)),
            ("Boranium", m => I(m.Boranium)),
            ("Germanium", m => I(m.Germanium)),
            ("Factories", m => I(m.Factories)),
            ("Mines", m => I(m.Mines)),
            ("Defenses", m => I(m.Defenses)),
            ("Energy", m => I(m.Energy)),
            ("Weapons", m => I(m.Weapons)),
            ("Propulsion", m => I(m.Propulsion)),
            ("Construction", m => I(m.Construction)),
            ("Electronics", m => I(m.Electronics)),
            ("Biotechnology", m => I(m.Biotechnology)),
            ("ResearchTotal", m => I(m.ResearchTotal)),
            ("Fleets", m => I(m.Fleets)),
            ("Ships", m => I(m.Ships)),
            ("ScoutShips", m => I(m.ScoutShips)),
            ("ColonyShips", m => I(m.ColonyShips)),
            ("Freighters", m => I(m.Freighters)),
            ("Warships", m => I(m.Warships)),
            ("Bombers", m => I(m.Bombers)),
            ("Minelayers", m => I(m.Minelayers)),
            ("OtherShips", m => I(m.OtherShips)),
            ("Starbases", m => I(m.Starbases)),
            ("ShipDesigns", m => I(m.ShipDesigns)),
            ("StarbaseDesigns", m => I(m.StarbaseDesigns)),
            ("Techs", m => I(m.Techs)),
            ("Score", m => I(m.Score)),
            ("Rank", m => I(m.Rank)),
            ("Minefields", m => I(m.Minefields)),
            ("MinesLaid", m => I(m.MinesLaid)),
            ("Packets", m => I(m.Packets)),
            ("Battles", m => I(m.Battles)),
            ("Messages", m => I(m.Messages)),
            ("Orders", m => I(m.Orders)),
            ("RejectedOrders", m => I(m.RejectedOrders)),
            ("QueuedOrders", m => I(m.QueuedOrders)),
            ("Eliminated", m => m.Eliminated ? "1" : "0"),
        };

        private static string I(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The ship role a design is counted under (first match wins).</summary>
    public enum ShipRole
    {
        Colonizer,
        Bomber,
        Minelayer,
        Warship,
        Freighter,
        Scout,
        Other,
    }

    /// <summary>
    /// Collects <see cref="EmpireTurnMetrics"/> from a ServerData and exports/compares them.
    /// </summary>
    public static class SimulationMetrics
    {
        public static ShipRole RoleOf(ShipDesign design)
        {
            if (design.CanColonize)
            {
                return ShipRole.Colonizer;
            }

            if (design.IsBomber)
            {
                return ShipRole.Bomber;
            }

            if (design.MineCount > 0)
            {
                return ShipRole.Minelayer;
            }

            if (design.HasWeapons)
            {
                return ShipRole.Warship;
            }

            if (design.CargoCapacity > 0)
            {
                return ShipRole.Freighter;
            }

            if (design.CanScan)
            {
                return ShipRole.Scout;
            }

            return ShipRole.Other;
        }

        /// <summary>
        /// Measures every empire. <paramref name="observations"/> (may be null) supplies the
        /// turn's messages and rejected orders; <paramref name="submitted"/> (may be null) the
        /// per-empire AI submission figures.
        /// </summary>
        public static List<EmpireTurnMetrics> Collect(
            ServerData state,
            int turn,
            IReadOnlyDictionary<int, PlayerSpec> players,
            GenerationObservations observations,
            IReadOnlyDictionary<int, AiSubmission> submitted)
        {
            Dictionary<int, ScoreRecord> scores = new Scores(state).GetScores().ToDictionary(record => record.EmpireId);
            List<EmpireTurnMetrics> result = new List<EmpireTurnMetrics>();

            foreach (EmpireData empire in state.AllEmpires.Values.OrderBy(e => e.Id))
            {
                EmpireTurnMetrics m = new EmpireTurnMetrics
                {
                    Turn = turn,
                    Year = state.TurnYear,
                    EmpireId = empire.Id,
                    Race = empire.Race == null ? string.Empty : empire.Race.Name,
                    Eliminated = empire.Eliminated,
                };

                if (players != null && players.TryGetValue(empire.Id, out PlayerSpec spec))
                {
                    m.Archetype = spec.Archetype;
                    m.Tier = spec.Tier;
                    m.Category = spec.EffectiveCategory;
                }
                else
                {
                    m.Archetype = m.Tier = m.Category = -1;
                }

                foreach (Star star in state.AllStars.Values)
                {
                    if (star.Owner != empire.Id)
                    {
                        continue;
                    }

                    m.Planets++;
                    m.Population += star.Colonists;
                    m.Resources += star.GetResourceRate();
                    if (star.ResourcesOnHand != null)
                    {
                        m.Ironium += star.ResourcesOnHand.Ironium;
                        m.Boranium += star.ResourcesOnHand.Boranium;
                        m.Germanium += star.ResourcesOnHand.Germanium;
                    }

                    m.Factories += star.Factories;
                    m.Mines += star.Mines;
                    m.Defenses += star.Defenses;
                    if (star.Starbase != null && star.Starbase.Composition.Count > 0)
                    {
                        m.Starbases++;
                    }
                }

                TechLevel levels = empire.ResearchLevels;
                m.Energy = levels[TechLevel.ResearchField.Energy];
                m.Weapons = levels[TechLevel.ResearchField.Weapons];
                m.Propulsion = levels[TechLevel.ResearchField.Propulsion];
                m.Construction = levels[TechLevel.ResearchField.Construction];
                m.Electronics = levels[TechLevel.ResearchField.Electronics];
                m.Biotechnology = levels[TechLevel.ResearchField.Biotechnology];
                m.ResearchTotal = m.Energy + m.Weapons + m.Propulsion + m.Construction + m.Electronics + m.Biotechnology;

                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    if (fleet.IsStarbase)
                    {
                        continue;
                    }

                    m.Fleets++;
                    foreach (ShipToken token in fleet.Composition.Values)
                    {
                        if (token.Design == null)
                        {
                            continue;
                        }

                        m.Ships += token.Quantity;
                        switch (RoleOf(token.Design))
                        {
                            case ShipRole.Colonizer: m.ColonyShips += token.Quantity; break;
                            case ShipRole.Bomber: m.Bombers += token.Quantity; break;
                            case ShipRole.Minelayer: m.Minelayers += token.Quantity; break;
                            case ShipRole.Warship: m.Warships += token.Quantity; break;
                            case ShipRole.Freighter: m.Freighters += token.Quantity; break;
                            case ShipRole.Scout: m.ScoutShips += token.Quantity; break;
                            default: m.OtherShips += token.Quantity; break;
                        }
                    }
                }

                foreach (ShipDesign design in empire.Designs.Values)
                {
                    if (design.IsStarbase)
                    {
                        m.StarbaseDesigns++;
                    }
                    else
                    {
                        m.ShipDesigns++;
                    }
                }

                m.Techs = empire.AvailableComponents == null ? 0 : empire.AvailableComponents.Count;

                if (scores.TryGetValue(empire.Id, out ScoreRecord record))
                {
                    m.Score = record.Score;
                    m.Rank = record.Rank;
                }

                foreach (Minefield field in state.AllMinefields.Values)
                {
                    if (field.Owner == empire.Id)
                    {
                        m.Minefields++;
                        m.MinesLaid += field.NumberOfMines;
                    }
                }

                m.Packets = state.AllMineralPackets.Values.Count(packet => packet.Owner == empire.Id);
                m.Battles = empire.BattleReports.Count;

                if (observations != null)
                {
                    m.Messages = observations.Messages.Count(message => message.Audience == empire.Id || message.Audience == Global.Everyone);
                    observations.RejectedByEmpire.TryGetValue(empire.Id, out int rejected);
                    m.RejectedOrders = rejected;
                }

                if (submitted != null && submitted.TryGetValue(empire.Id, out AiSubmission submission))
                {
                    m.Orders = submission.Commands;
                    m.QueuedOrders = submission.QueuedOrders;
                }

                result.Add(m);
            }

            return result;
        }

        public static void WriteCsv(IEnumerable<EmpireTurnMetrics> metrics, string path)
        {
            using (StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(string.Join(",", EmpireTurnMetrics.Columns.Select(c => c.Name)));
                foreach (EmpireTurnMetrics m in metrics)
                {
                    writer.WriteLine(string.Join(",", EmpireTurnMetrics.Columns.Select(c => Escape(c.Get(m)))));
                }
            }
        }

        private static string Escape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        /// <summary>Reads a metrics CSV back as rows of column name to text.</summary>
        public static List<Dictionary<string, string>> ReadCsv(string path)
        {
            List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0)
            {
                return rows;
            }

            List<string> header = SplitCsvLine(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                List<string> cells = SplitCsvLine(lines[i]);
                Dictionary<string, string> row = new Dictionary<string, string>();
                for (int c = 0; c < header.Count; c++)
                {
                    row[header[c]] = c < cells.Count ? cells[c] : string.Empty;
                }

                rows.Add(row);
            }

            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            List<string> cells = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else if (ch == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
                else if (ch == '"')
                {
                    quoted = true;
                }
                else if (ch == ',')
                {
                    cells.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(ch);
                }
            }

            cells.Add(current.ToString());
            return cells;
        }

        /// <summary>
        /// Compares two metrics CSVs row by row (keyed by Turn and EmpireId). Returns the human
        /// readable differences; empty means identical.
        /// </summary>
        public static List<string> Compare(string pathA, string pathB, int maxReported = 40)
        {
            List<Dictionary<string, string>> a = ReadCsv(pathA);
            List<Dictionary<string, string>> b = ReadCsv(pathB);
            List<string> differences = new List<string>();

            Dictionary<string, Dictionary<string, string>> indexB = b.ToDictionary(Key);
            HashSet<string> seen = new HashSet<string>();
            string firstDivergence = null;

            foreach (Dictionary<string, string> rowA in a)
            {
                string key = Key(rowA);
                seen.Add(key);
                if (!indexB.TryGetValue(key, out Dictionary<string, string> rowB))
                {
                    differences.Add(key + ": only in A");
                    continue;
                }

                foreach (KeyValuePair<string, string> cell in rowA)
                {
                    rowB.TryGetValue(cell.Key, out string other);
                    if (cell.Value != other)
                    {
                        firstDivergence = firstDivergence ?? key;
                        differences.Add(key + " " + cell.Key + ": " + cell.Value + " -> " + other);
                    }
                }
            }

            foreach (Dictionary<string, string> rowB in b)
            {
                if (!seen.Contains(Key(rowB)))
                {
                    differences.Add(Key(rowB) + ": only in B");
                }
            }

            if (differences.Count > maxReported)
            {
                int total = differences.Count;
                differences = differences.Take(maxReported).ToList();
                differences.Add("... " + (total - maxReported) + " more differences");
            }

            if (firstDivergence != null)
            {
                differences.Insert(0, "first divergence at " + firstDivergence);
            }

            return differences;
        }

        private static string Key(Dictionary<string, string> row)
        {
            row.TryGetValue("Turn", out string turn);
            row.TryGetValue("EmpireId", out string empire);
            return "turn " + turn + " empire " + empire;
        }
    }

    /// <summary>What one AI handed in for one turn.</summary>
    public sealed class AiSubmission
    {
        public int EmpireId { get; set; }

        public int Commands { get; set; }

        public int QueuedOrders { get; set; }

        public Dictionary<string, int> CommandsByType { get; set; } = new Dictionary<string, int>();
    }
}
