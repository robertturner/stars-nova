namespace Nova.Tests.Simulation
{
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Ai;
    using Nova.Common;
    using Nova.Sim;

    using NUnit.Framework;

    /// <summary>
    /// The long simulations: several seeds x player counts (2 to 8) x 150-300 turns with
    /// accelerated start on and off, each of the 24 built-in AI templates once, and the normal
    /// 4-AI 100-turn game's behaviour expectations. Explicit, so the default test run skips them;
    /// run with
    ///   dotnet test Tests/Tests.csproj --filter Category=Soak
    /// (docs/SIMULATION.md). Expect minutes per case.
    /// </summary>
    [TestFixture]
    [Category("Soak")]
    [Explicit("long-running simulations: dotnet test --filter Category=Soak")]
    public class SimulationSoakTest
    {
        public static IEnumerable<TestCaseData> SeedAndPlayerCases()
        {
            foreach (int seed in new[] { 1, 2, 3 })
            {
                foreach (int players in new[] { 2, 4, 8 })
                {
                    int turns = players <= 2 ? 300 : players <= 4 ? 200 : 150;
                    bool accelerated = (seed + players) % 2 == 0;
                    yield return new TestCaseData(seed, players, turns, accelerated)
                        .SetName("Soak_seed" + seed + "_" + players + "p_" + turns + "t" + (accelerated ? "_accelerated" : string.Empty));
                }
            }
        }

        [TestCaseSource(nameof(SeedAndPlayerCases))]
        public void LongGame_HoldsEveryInvariant(int seed, int players, int turns, bool accelerated)
        {
            SimulationConfig config = new SimulationConfig
            {
                Seed = seed,
                GameName = "Soak",
                GalaxySize = players <= 2 ? GalaxySize.Tiny : players <= 4 ? GalaxySize.Small : GalaxySize.Medium,
                Density = GalaxyDensity.Normal,
                Players = SimulationConfig.DefaultPlayers(players),
                Turns = turns,
                AcceleratedStart = accelerated,
                ReloadEvery = 10,
            };

            SimulationResult result = SimulationRunner.RunOnce(config);
            TestContext.WriteLine(AiBehaviourAssertions.ObservedBehaviour(result));
            SimulationTestSupport.AssertHealthy(result);
        }

        /// <summary>Six games of four players cover the 24 templates (index = archetype x 4 +
        /// tier): game g plays templates g, g + 6, g + 12 and g + 18.</summary>
        public static IEnumerable<TestCaseData> TemplateCases()
        {
            for (int game = 0; game < 6; game++)
            {
                int[] templates = { game, game + 6, game + 12, game + 18 };
                yield return new TestCaseData(game, templates)
                    .SetName("Soak_templates_" + string.Join("_", templates));
            }
        }

        [TestCaseSource(nameof(TemplateCases))]
        public void EveryAiTemplate_PlaysAWholeGame(int game, int[] templates)
        {
            SimulationConfig config = new SimulationConfig
            {
                Seed = 40 + game,
                GameName = "SoakTemplates",
                GalaxySize = GalaxySize.Small,
                Density = GalaxyDensity.Normal,
                Players = templates.Select(index => new PlayerSpec(AiRaceTemplates.All[index].Archetype, AiRaceTemplates.All[index].Tier)).ToList(),
                Turns = 150,
                ReloadEvery = 10,
            };

            SimulationResult result = SimulationRunner.RunOnce(config);
            TestContext.WriteLine(AiBehaviourAssertions.ObservedBehaviour(result));
            SimulationTestSupport.AssertHealthy(result);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void NormalGame_FourAis_HundredTurns_MeetTheBehaviourExpectations(int seed)
        {
            SimulationConfig config = new SimulationConfig
            {
                Seed = seed,
                GameName = "SoakNormal",
                GalaxySize = GalaxySize.Small,
                Density = GalaxyDensity.Normal,
                Players = SimulationConfig.DefaultPlayers(4),
                Turns = 100,
                ReloadEvery = 5,
            };

            SimulationResult result = SimulationRunner.RunOnce(config);
            TestContext.WriteLine(AiBehaviourAssertions.ObservedBehaviour(result));
            List<ExpectationOutcome> outcomes = AiBehaviourAssertions.Evaluate(result, AiBehaviourAssertions.ForNormalGame());
            foreach (ExpectationOutcome outcome in outcomes)
            {
                TestContext.WriteLine(outcome);
            }

            SimulationTestSupport.AssertHealthy(result);
            CollectionAssert.IsEmpty(outcomes.Where(o => !o.Passed).Select(o => o.ToString()));
        }

        /// <summary>Reloading from disk every turn must not change the game compared with never
        /// reloading: anything the in-memory state carries but the save loses shows up as a hash
        /// difference (meaningful once the game is deterministic).</summary>
        [Test]
        public void ReloadingEveryTurn_PlaysTheSameGameAsNeverReloading()
        {
            SimulationConfig always = SimulationTestSupport.SmallConfig(seed: 12, players: 3, turns: 30);
            always.ReloadEvery = 1;
            always.CheckRoundTrip = false;
            SimulationConfig never = SimulationTestSupport.SmallConfig(seed: 12, players: 3, turns: 30);
            never.ReloadEvery = 0;

            SimulationResult a = SimulationRunner.RunOnce(always);
            SimulationResult b = SimulationRunner.RunOnce(never);
            SimulationResult c = SimulationRunner.RunOnce(never);
            if (SimulationTestSupport.FirstHashDivergence(b, c) != null)
            {
                Assert.Inconclusive("DETERMINISM INCOMPLETE: two never-reloaded runs already diverge at " + SimulationTestSupport.FirstHashDivergence(b, c));
            }

            string divergence = SimulationTestSupport.FirstHashDivergence(a, b);
            Assert.IsNull(divergence, "reloading every turn changed the game from " + divergence);
        }
    }
}
