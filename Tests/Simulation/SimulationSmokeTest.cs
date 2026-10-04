namespace Nova.Tests.Simulation
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using Nova.Sim;

    using NUnit.Framework;

    /// <summary>
    /// The fast whole-game check of the normal suite (docs/SIMULATION.md): a Tiny galaxy, three
    /// built-in AIs (Robotoids, Turindrones, Automitrons, Standard tier), 30 turns with a
    /// save/reload every 5, every invariant checked every turn.
    /// </summary>
    [TestFixture]
    [Category("Simulation")]
    public class SimulationSmokeTest
    {
        private static SimulationResult smoke;

        private static SimulationResult Smoke
        {
            get
            {
                if (smoke == null)
                {
                    SimulationConfig config = SimulationTestSupport.SmallConfig(seed: 7, players: 3, turns: 30);
                    smoke = SimulationRunner.RunOnce(config);
                }

                return smoke;
            }
        }

        [Test]
        public void SmokeGame_ThreeAis_ThirtyTurns_HoldsEveryInvariant()
        {
            SimulationResult result = Smoke;

            SimulationTestSupport.AssertHealthy(result);
            Assert.AreEqual(30, result.Turns.Count);
            Assert.AreEqual(3, result.Turns.Last().Empires.Count);
            Assert.AreEqual(6, result.Turns.Count(t => t.Reloaded), "reloaded every 5 turns");
            Assert.IsTrue(result.Turns.Where(t => t.Reloaded).All(t => t.RoundTripChecked));
            Assert.IsTrue(result.Turns.All(t => !string.IsNullOrEmpty(t.StateHash)));
            Assert.AreEqual(2130, result.Turns.Last().Year);
        }

        [Test]
        public void SmokeGame_AisPlay_TheyDoNotJustSit()
        {
            SimulationResult result = Smoke;
            TestContext.WriteLine(AiBehaviourAssertions.ObservedBehaviour(result));

            // Short-game versions of the AiBehaviourAssertions expectations.
            BehaviourThresholds thresholds = new BehaviourThresholds
            {
                ColonizeByTurn = 30,
                MinPlanetsAtColonizeTurn = 1,
                BuildsShipsByTurn = 30,
                FreighterByTurn = 30,
                FreighterShare = 0,
                WarshipByTurn = 30,
                WarshipShare = 0,
                ResearchByTurn = 30,
                MinResearchGain = 1,
                MaxIdleQueueTurns = 10,
                NoGameEndBeforeTurn = 30,
                MinPopulationGrowth = 1.0,
            };

            List<ExpectationOutcome> outcomes = AiBehaviourAssertions.Evaluate(result, AiBehaviourAssertions.ForNormalGame(thresholds));
            foreach (ExpectationOutcome outcome in outcomes)
            {
                TestContext.WriteLine(outcome);
            }

            CollectionAssert.IsEmpty(outcomes.Where(o => !o.Passed).Select(o => o.ToString()));
        }

        [Test]
        public void SmokeGame_EveryAiSubmitsOrdersEveryTurn_AndNoneAreRejected()
        {
            SimulationResult result = Smoke;
            foreach (TurnRecord turn in result.Turns)
            {
                Assert.AreEqual(0, turn.RejectedOrders, "turn " + turn.Turn + ": " + string.Join(", ", turn.RejectedByType.Select(p => p.Key + " x" + p.Value)));
                Assert.IsTrue(turn.Empires.All(e => e.Orders > 0), "turn " + turn.Turn + ": every AI issues at least one command");
            }
        }

        [Test]
        public void Metrics_ExportToCsvAndJson_AndCompareEqualToThemselves()
        {
            SimulationResult result = Smoke;
            string folder = Path.Combine(Path.GetTempPath(), "NovaSimTest_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string csv = Path.Combine(folder, "metrics.csv");
                string json = Path.Combine(folder, "result.json");
                result.WriteCsv(csv);
                result.WriteJson(json);

                List<Dictionary<string, string>> rows = SimulationMetrics.ReadCsv(csv);
                Assert.AreEqual(30 * 3, rows.Count, "one row per empire per turn");
                Assert.AreEqual("1", rows[0]["Turn"]);
                CollectionAssert.IsEmpty(SimulationMetrics.Compare(csv, csv));
                StringAssert.Contains("\"StateHash\"", File.ReadAllText(json));

                // A changed cell is reported with its turn, empire and column.
                string[] lines = File.ReadAllLines(csv);
                List<string> header = lines[0].Split(',').ToList();
                string[] cells = lines[5].Split(',');
                int planets = header.IndexOf("Planets");
                cells[planets] = "999";
                lines[5] = string.Join(",", cells);
                string changed = Path.Combine(folder, "changed.csv");
                File.WriteAllLines(changed, lines);
                List<string> differences = SimulationMetrics.Compare(csv, changed);
                Assert.IsTrue(differences.Any(d => d.Contains("Planets") && d.Contains("-> 999")), string.Join("\n", differences));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
