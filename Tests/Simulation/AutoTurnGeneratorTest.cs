namespace Nova.Tests.Simulation
{
    using System;
    using System.IO;
    using System.Linq;

    using Nova.Common;
    using Nova.Server;
    using Nova.Sim;

    using NUnit.Framework;

    /// <summary>
    /// Batch / auto turn generation (turn-generation-engine.md, host commands: 10, 100 or 1000
    /// turns in a row, looping the single-turn generate/advance sequence, abortable between
    /// turns; coverage row 45), and resuming a saved game with the simulation runner.
    /// </summary>
    [TestFixture]
    [Category("Simulation")]
    public class AutoTurnGeneratorTest
    {
        private string work;

        [SetUp]
        public void SetUp()
        {
            work = Path.Combine(Path.GetTempPath(), "NovaSimTest_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(work))
                {
                    Directory.Delete(work, true);
                }
            }
            catch (IOException)
            {
                // best effort
            }
        }

        /// <summary>Creates a 2-AI game (no turns played) in work/created/game.</summary>
        private string CreateGame(int turns = 0)
        {
            SimulationConfig config = SimulationTestSupport.SmallConfig(seed: 9, players: 2, turns: turns);
            config.WorkFolder = Path.Combine(work, "created");
            config.DeleteWorkFolder = false;
            SimulationResult result = SimulationRunner.RunOnce(config);
            Assert.IsNull(result.FatalError, result.FatalError);
            return result.GameFolder;
        }

        private static ServerData Load(string game)
        {
            string settings = Directory.GetFiles(game, "*" + Global.SettingsExtension).Single();
            GameSettings.Data.SettingsPathName = settings;
            GameSettings.Restore();
            string path = Directory.GetFiles(game, "*" + Global.ServerStateExtension).Single();
            ServerData state = new ServerData { StatePathName = path };
            state.Restore();
            return state;
        }

        [Test]
        public void TheBatchSizes_AreTenHundredAndThousand()
        {
            CollectionAssert.AreEqual(new[] { 10, 100, 1000 }, AutoTurnGenerator.BatchSizes);
        }

        [Test]
        public void GenerateTurns_AdvancesThatManyYears_AndSavesEachOne()
        {
            string game = CreateGame();
            using (SimulationEnvironment environment = SimulationEnvironment.Enter(Path.Combine(work, "root")))
            {
                ServerData state = Load(game);
                int start = state.TurnYear;
                AutoTurnGenerator host = new AutoTurnGenerator(state) { PublishGameFolder = environment.SetGameFolder };
                int seen = 0;
                AutoGenerateResult outcome = host.GenerateTurns(AutoTurnGenerator.BatchSizes[0], afterTurn: step =>
                {
                    seen++;
                    Assert.AreEqual(start + seen - 1, step.YearPlayed);
                    Assert.AreEqual(2, step.Submissions.Count, "both AIs moved");
                    CollectionAssert.IsEmpty(step.OrdersNotAccepted);
                    CollectionAssert.IsEmpty(step.AiFailures.Select(f => f.Value.ToString()));
                });

                Assert.AreEqual(10, outcome.TurnsGenerated);
                Assert.IsFalse(outcome.Aborted);
                Assert.AreEqual(start + 10, state.TurnYear);
                Assert.AreEqual(start + 10, Load(game).TurnYear, "the state on disk is the advanced one");
            }
        }

        [Test]
        public void GenerateTurns_StopsWhenTheAbortCheckSaysSo_BeforeTheNextTurn()
        {
            string game = CreateGame();
            using (SimulationEnvironment environment = SimulationEnvironment.Enter(Path.Combine(work, "root")))
            {
                ServerData state = Load(game);
                int start = state.TurnYear;
                int polls = 0;
                AutoTurnGenerator host = new AutoTurnGenerator(state) { PublishGameFolder = environment.SetGameFolder };
                AutoGenerateResult outcome = host.GenerateTurns(100, abortRequested: () => ++polls > 3);

                Assert.AreEqual(3, outcome.TurnsGenerated);
                Assert.IsTrue(outcome.Aborted);
                Assert.AreEqual(start + 3, state.TurnYear);
            }
        }

        [Test]
        public void APlayerWhoHasNotSubmitted_DoesNotStopGeneration()
        {
            string game = CreateGame();
            using (SimulationEnvironment environment = SimulationEnvironment.Enter(Path.Combine(work, "root")))
            {
                ServerData state = Load(game);
                state.AllPlayers[1].AiProgram = "Human";
                int start = state.TurnYear;
                AutoTurnGenerator host = new AutoTurnGenerator(state) { PublishGameFolder = environment.SetGameFolder };
                AutoTurnStep step = host.GenerateOne();

                CollectionAssert.AreEqual(new[] { (int)state.AllPlayers[0].PlayerNumber }, step.Submissions.Keys.ToArray(), "only the AI moved");
                Assert.AreEqual(start + 1, state.TurnYear);
            }
        }

        [Test]
        public void Resume_ContinuesASavedGame_FromWhereItStopped()
        {
            string game = CreateGame(turns: 3);

            SimulationConfig config = new SimulationConfig { Seed = 9, Turns = 4, WorkFolder = Path.Combine(work, "resumed"), DeleteWorkFolder = false, ReloadEvery = 2 };
            SimulationResult result = new SimulationRunner(config).Resume(game);

            SimulationTestSupport.AssertHealthy(result);
            Assert.AreEqual(4, result.Turns.Count);
            Assert.AreEqual(Global.StartingYear + 7, result.Turns.Last().Year);
            Assert.AreEqual(2, result.Players.Count, "both template AIs were recognised");
            Assert.IsTrue(result.Players.Values.All(p => p.Archetype >= 0));
            string saved = File.ReadAllText(Directory.GetFiles(game, "*" + Global.ServerStateExtension).Single());
            StringAssert.Contains("<TurnYear>" + (Global.StartingYear + 3) + "</TurnYear>", saved, "the source folder was copied, not changed");
        }
    }
}
