namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    using Nova.Ai;

    /// <summary>The verdict of one behaviour expectation on one run.</summary>
    public sealed class ExpectationOutcome
    {
        public string Name { get; set; }

        public bool Passed { get; set; }

        /// <summary>What was actually observed (always filled, pass or fail).</summary>
        public string Observed { get; set; }

        public override string ToString()
        {
            return (Passed ? "PASS " : "FAIL ") + Name + ": " + Observed;
        }
    }

    /// <summary>A named sanity expectation about how the AI plays a whole game.</summary>
    public sealed class BehaviourExpectation
    {
        public BehaviourExpectation(string name, string description, Func<SimulationResult, ExpectationOutcome> evaluate)
        {
            Name = name;
            Description = description;
            Evaluate = result =>
            {
                ExpectationOutcome outcome = evaluate(result);
                outcome.Name = name;
                return outcome;
            };
        }

        public string Name { get; }

        public string Description { get; }

        public Func<SimulationResult, ExpectationOutcome> Evaluate { get; }
    }

    /// <summary>
    /// The thresholds of <see cref="AiBehaviourAssertions.ForNormalGame"/>. Ranges, not exact
    /// values, tuned conservatively below what the AI actually does today (docs/SIMULATION.md
    /// records the observed numbers they were derived from), so they catch an AI that has stopped
    /// playing rather than one that plays slightly differently.
    /// </summary>
    public sealed class BehaviourThresholds
    {
        /// <summary>Every driven AI owns at least <see cref="MinPlanetsAtColonizeTurn"/> planets
        /// by this turn.</summary>
        public int ColonizeByTurn { get; set; } = 50;

        public int MinPlanetsAtColonizeTurn { get; set; } = 2;

        /// <summary>Every driven AI has built at least one ship beyond its starting fleet by this turn.</summary>
        public int BuildsShipsByTurn { get; set; } = 30;

        /// <summary>At least this many of the driven AIs field a freighter by FreighterByTurn.</summary>
        public int FreighterByTurn { get; set; } = 60;

        public double FreighterShare { get; set; } = 0.5;

        /// <summary>At least this share of the driven AIs field a warship by WarshipByTurn.</summary>
        public int WarshipByTurn { get; set; } = 80;

        public double WarshipShare { get; set; } = 0.5;

        /// <summary>Every AI's research total rises by at least this much over the game's first
        /// ResearchByTurn turns.</summary>
        public int ResearchByTurn { get; set; } = 50;

        public int MinResearchGain { get; set; } = 3;

        /// <summary>No AI goes more than this many consecutive turns with an empty queue on every
        /// planet while it still owns planets.</summary>
        public int MaxIdleQueueTurns { get; set; } = 10;

        /// <summary>No AI goes more than this many consecutive turns producing zero resources while
        /// it still owns planets.</summary>
        public int MaxZeroResourceTurns { get; set; } = 3;

        /// <summary>No victory is declared and nobody is eliminated before this turn.</summary>
        public int NoGameEndBeforeTurn { get; set; } = 30;

        /// <summary>Every AI's population at the end is at least this share of its starting population.</summary>
        public double MinPopulationGrowth { get; set; } = 1.0;
    }

    /// <summary>
    /// Sanity expectations about the AI on a normal game (four AIs, 100 turns), evaluated on a
    /// finished <see cref="SimulationResult"/>. Only "driven" AIs (categories 0-5) are held to the
    /// fleet expectations; the economy-only and no-driver categories issue no fleet orders by
    /// design (ai-opponent-behavior.md section 12).
    /// </summary>
    public static class AiBehaviourAssertions
    {
        public static List<BehaviourExpectation> ForNormalGame(BehaviourThresholds t = null)
        {
            t = t ?? new BehaviourThresholds();
            return new List<BehaviourExpectation>
            {
                new BehaviourExpectation("Colonizes", "every driven AI owns at least " + t.MinPlanetsAtColonizeTurn + " planets by turn " + t.ColonizeByTurn,
                    r => PerEmpire(r, true, id => Planets(r, id, t.ColonizeByTurn), v => v >= t.MinPlanetsAtColonizeTurn, "planets@" + t.ColonizeByTurn)),
                new BehaviourExpectation("BuildsShips", "every driven AI has more ships at turn " + t.BuildsShipsByTurn + " than it started with, or has colonised (colony ships are spent)",
                    r => PerEmpire(r, true, id => ShipsBuiltBy(r, id, t.BuildsShipsByTurn), v => v > 0, "ships+planets gained@" + t.BuildsShipsByTurn)),
                new BehaviourExpectation("BuildsFreighters", "at least " + Percent(t.FreighterShare) + " of the driven AIs field a freighter by turn " + t.FreighterByTurn,
                    r => Share(r, id => FirstTurnAbove(r, id, m => m.Freighters), turn => turn > 0 && turn <= t.FreighterByTurn, t.FreighterShare, "first freighter built")),
                new BehaviourExpectation("BuildsWarships", "at least " + Percent(t.WarshipShare) + " of the driven AIs field a warship by turn " + t.WarshipByTurn,
                    r => Share(r, id => FirstTurnAbove(r, id, m => m.Warships), turn => turn > 0 && turn <= t.WarshipByTurn, t.WarshipShare, "first warship built")),
                new BehaviourExpectation("Researches", "every AI's research total rises by at least " + t.MinResearchGain + " over the first " + t.ResearchByTurn + " turns",
                    r => PerEmpire(r, false, id => ResearchGain(r, id, t.ResearchByTurn), v => v >= t.MinResearchGain, "research gain@" + t.ResearchByTurn)),
                new BehaviourExpectation("NoIdleProduction", "no AI with planets has an empty queue everywhere for more than " + t.MaxIdleQueueTurns + " consecutive turns",
                    r => PerEmpire(r, true, id => LongestRun(r, id, m => m.Planets > 0 && m.QueuedOrders == 0), v => v <= t.MaxIdleQueueTurns, "longest idle-queue run")),
                new BehaviourExpectation("NoZeroResources", "no AI with planets produces zero resources for more than " + t.MaxZeroResourceTurns + " consecutive turns",
                    r => PerEmpire(r, false, id => LongestRun(r, id, m => m.Planets > 0 && m.Resources == 0), v => v <= t.MaxZeroResourceTurns, "longest zero-resource run")),
                new BehaviourExpectation("NoEarlyGameEnd", "no victory and no elimination before turn " + t.NoGameEndBeforeTurn,
                    r => NoEarlyEnd(r, t.NoGameEndBeforeTurn)),
                new BehaviourExpectation("PopulationGrows", "every AI ends with at least " + t.MinPopulationGrowth.ToString("0.##", CultureInfo.InvariantCulture) + "x its starting population",
                    r => PerEmpire(r, false, id => PopulationRatio(r, id), v => v >= t.MinPopulationGrowth, "final/initial population")),
            };
        }

        public static List<ExpectationOutcome> Evaluate(SimulationResult result, IEnumerable<BehaviourExpectation> expectations)
        {
            return expectations.Select(e => e.Evaluate(result)).ToList();
        }

        /// <summary>The headline numbers of a run, one line per empire (for reports and tuning).</summary>
        public static string ObservedBehaviour(SimulationResult result)
        {
            StringBuilder s = new StringBuilder();
            s.AppendLine("  Id Template              Cat P@25 P@50 P@100 Pend Res@50 ResEnd Tech0 Tech@50 TechEnd 1stFrt 1stWar 1stCol MaxIdle Ships Score Battles");
            foreach (int id in EmpireIds(result))
            {
                EmpireTurnMetrics first = result.MetricsFor(id).FirstOrDefault();
                EmpireTurnMetrics last = result.MetricsFor(id).LastOrDefault();
                if (first == null)
                {
                    continue;
                }

                PlayerSpec spec = result.Players.TryGetValue(id, out PlayerSpec p) ? p : null;
                s.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,2} {1,-21} {2,3} {3,4} {4,4} {5,5} {6,4} {7,6} {8,6} {9,5} {10,7} {11,7} {12,6} {13,6} {14,6} {15,7} {16,5} {17,5} {18,7}",
                    id,
                    spec == null ? "?" : spec.ToString(),
                    first.Category,
                    Cell(result, id, 25, m => m.Planets),
                    Cell(result, id, 50, m => m.Planets),
                    Cell(result, id, 100, m => m.Planets),
                    last.Planets,
                    Cell(result, id, 50, m => m.Resources),
                    last.Resources,
                    first.ResearchTotal,
                    Cell(result, id, 50, m => m.ResearchTotal),
                    last.ResearchTotal,
                    Dash(FirstTurnAbove(result, id, m => m.Freighters)),
                    Dash(FirstTurnAbove(result, id, m => m.Warships)),
                    Dash(FirstTurn(result, id, m => m.Turn > 1 && m.ColonyShips > first.ColonyShips)),
                    LongestRun(result, id, m => m.Planets > 0 && m.QueuedOrders == 0),
                    last.Ships,
                    last.Score,
                    result.MetricsFor(id).Sum(m => m.Battles)));
            }

            return s.ToString();
        }

        // ------------------------------------------------------------------------ evaluators

        private static IEnumerable<int> EmpireIds(SimulationResult result)
        {
            return result.AllMetrics.Select(m => m.EmpireId).Distinct().OrderBy(id => id);
        }

        private static bool IsDriven(SimulationResult result, int id)
        {
            EmpireTurnMetrics first = result.MetricsFor(id).FirstOrDefault();
            return first != null && first.Category >= AiCategory.Robotoids && first.Category <= AiCategory.Macinti;
        }

        private static ExpectationOutcome PerEmpire(SimulationResult result, bool drivenOnly, Func<int, double> measure, Func<double, bool> ok, string label)
        {
            List<string> parts = new List<string>();
            bool passed = true;
            foreach (int id in EmpireIds(result))
            {
                if (drivenOnly && !IsDriven(result, id))
                {
                    continue;
                }

                double value = measure(id);
                bool good = ok(value);
                passed &= good;
                parts.Add("#" + id + "=" + value.ToString("0.##", CultureInfo.InvariantCulture) + (good ? string.Empty : " (FAIL)"));
            }

            return new ExpectationOutcome { Passed = passed, Observed = label + ": " + string.Join(", ", parts) };
        }

        private static ExpectationOutcome Share(SimulationResult result, Func<int, int> measure, Func<int, bool> ok, double share, string label)
        {
            List<int> ids = EmpireIds(result).Where(id => IsDriven(result, id)).ToList();
            List<string> parts = new List<string>();
            int good = 0;
            foreach (int id in ids)
            {
                int value = measure(id);
                if (ok(value))
                {
                    good++;
                }

                parts.Add("#" + id + "=" + Dash(value));
            }

            bool passed = ids.Count == 0 || good >= Math.Ceiling(share * ids.Count);
            return new ExpectationOutcome { Passed = passed, Observed = label + ": " + string.Join(", ", parts) + " (" + good + "/" + ids.Count + " in range)" };
        }

        private static ExpectationOutcome NoEarlyEnd(SimulationResult result, int beforeTurn)
        {
            TurnRecord victory = result.Turns.FirstOrDefault(t => t.VictoryMessage != null);
            EmpireTurnMetrics eliminated = result.AllMetrics.Where(m => m.Eliminated).OrderBy(m => m.Turn).FirstOrDefault();
            bool passed = (victory == null || victory.Turn >= beforeTurn) && (eliminated == null || eliminated.Turn >= beforeTurn);
            string observed = "victory " + (victory == null ? "none" : "turn " + victory.Turn) + ", first elimination " + (eliminated == null ? "none" : "turn " + eliminated.Turn + " (#" + eliminated.EmpireId + ")");
            return new ExpectationOutcome { Passed = passed, Observed = observed };
        }

        private static double Planets(SimulationResult result, int id, int turn)
        {
            return Value(result, id, turn, m => m.Planets);
        }

        /// <summary>Ships gained since turn 1 plus planets gained (colony ships are consumed
        /// when they colonise, so a colonising AI's ship count can fall).</summary>
        private static double ShipsBuiltBy(SimulationResult result, int id, int turn)
        {
            EmpireTurnMetrics first = result.MetricsFor(id).FirstOrDefault();
            EmpireTurnMetrics at = AtOrBefore(result, id, turn);
            if (first == null || at == null)
            {
                return 0;
            }

            int maxShips = result.MetricsFor(id).Where(m => m.Turn <= turn).Max(m => m.Ships);
            return Math.Max(0, maxShips - first.Ships) + Math.Max(0, at.Planets - first.Planets) + Math.Max(0, at.Starbases - first.Starbases);
        }

        private static double ResearchGain(SimulationResult result, int id, int turn)
        {
            EmpireTurnMetrics first = result.MetricsFor(id).FirstOrDefault();
            EmpireTurnMetrics at = AtOrBefore(result, id, turn);
            return first == null || at == null ? 0 : at.ResearchTotal - first.ResearchTotal;
        }

        private static double PopulationRatio(SimulationResult result, int id)
        {
            EmpireTurnMetrics first = result.MetricsFor(id).FirstOrDefault();
            EmpireTurnMetrics last = result.MetricsFor(id).LastOrDefault();
            return first == null || first.Population <= 0 ? 0 : (double)last.Population / first.Population;
        }

        /// <summary>The first turn a condition holds, or -1.</summary>
        public static int FirstTurn(SimulationResult result, int id, Func<EmpireTurnMetrics, bool> condition)
        {
            EmpireTurnMetrics hit = result.MetricsFor(id).FirstOrDefault(condition);
            return hit == null ? -1 : hit.Turn;
        }

        /// <summary>The first turn a count exceeds its turn-1 value (something was built), or -1.</summary>
        public static int FirstTurnAbove(SimulationResult result, int id, Func<EmpireTurnMetrics, int> count)
        {
            EmpireTurnMetrics first = result.MetricsFor(id).FirstOrDefault();
            if (first == null)
            {
                return -1;
            }

            int start = count(first);
            return FirstTurn(result, id, m => count(m) > start);
        }

        /// <summary>The longest run of consecutive turns a condition holds.</summary>
        public static int LongestRun(SimulationResult result, int id, Func<EmpireTurnMetrics, bool> condition)
        {
            int best = 0;
            int current = 0;
            foreach (EmpireTurnMetrics m in result.MetricsFor(id))
            {
                current = condition(m) ? current + 1 : 0;
                best = Math.Max(best, current);
            }

            return best;
        }

        private static EmpireTurnMetrics AtOrBefore(SimulationResult result, int id, int turn)
        {
            return result.MetricsFor(id).Where(m => m.Turn <= turn).LastOrDefault();
        }

        private static string Cell(SimulationResult result, int id, int turn, Func<EmpireTurnMetrics, long> get)
        {
            EmpireTurnMetrics m = result.Metric(turn, id);
            return m == null ? "-" : get(m).ToString(CultureInfo.InvariantCulture);
        }

        private static double Value(SimulationResult result, int id, int turn, Func<EmpireTurnMetrics, int> get)
        {
            EmpireTurnMetrics m = AtOrBefore(result, id, turn);
            return m == null ? 0 : get(m);
        }

        private static string Dash(int turn)
        {
            return turn < 0 ? "-" : turn.ToString(CultureInfo.InvariantCulture);
        }

        private static string Percent(double share)
        {
            return (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
