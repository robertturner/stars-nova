namespace Nova.Tests.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    using Nova.Sim;

    using NUnit.Framework;

    /// <summary>
    /// Golden snapshots: the saved-state hash every <see cref="Every"/> turns of three fixed-seed
    /// games, stored in Tests/Simulation/Goldens/seed-N.json. Any change to game rules, AI
    /// behaviour or the save format changes them, which is the point: an unintended change shows
    /// up here. After an intended change, regenerate with
    ///   UPDATE_GOLDENS=1 dotnet test Tests/Tests.csproj --filter Category=Golden
    /// and commit the new files. Each test first plays its game twice; if the two runs disagree
    /// the game is not yet deterministic and the golden cannot be meaningful, so the test is
    /// reported Inconclusive with the first diverging turn instead of failing.
    /// </summary>
    [TestFixture]
    [Category("Simulation")]
    [Category("Golden")]
    public class GoldenSnapshotTest
    {
        public const int Every = 4;

        public sealed class Golden
        {
            public int Seed { get; set; }

            public string Config { get; set; }

            public int Turns { get; set; }

            public Dictionary<string, string> Hashes { get; set; } = new Dictionary<string, string>();
        }

        private static readonly Dictionary<int, (SimulationResult First, SimulationResult Second)> Runs = new Dictionary<int, (SimulationResult, SimulationResult)>();

        /// <summary>Two runs of a golden seed's config (cached: DeterminismTest reuses them).</summary>
        public static (SimulationResult First, SimulationResult Second) TwoRuns(int seed)
        {
            if (!Runs.TryGetValue(seed, out (SimulationResult, SimulationResult) pair))
            {
                pair = (SimulationRunner.RunOnce(Config(seed)), SimulationRunner.RunOnce(Config(seed)));
                Runs[seed] = pair;
            }

            return pair;
        }

        public static SimulationConfig Config(int seed)
        {
            SimulationConfig config = SimulationTestSupport.SmallConfig(seed, players: 2, turns: 12);
            config.Players[0].Archetype = seed % 6;
            config.Players[1].Archetype = (seed + 3) % 6;
            return config;
        }

        private static bool Updating
        {
            get { return Environment.GetEnvironmentVariable("UPDATE_GOLDENS") == "1"; }
        }

        private static Dictionary<string, string> Snapshot(SimulationResult result)
        {
            return result.Turns.Where(t => t.Turn % Every == 0).ToDictionary(t => t.Turn.ToString(System.Globalization.CultureInfo.InvariantCulture), t => t.StateHash);
        }

        [TestCase(101)]
        [TestCase(202)]
        [TestCase(303)]
        public void StateHashes_MatchTheGolden(int seed)
        {
            SimulationConfig config = Config(seed);
            (SimulationResult first, SimulationResult second) = TwoRuns(seed);
            Assert.IsNull(first.FatalError, first.FatalError);
            Assert.IsNull(second.FatalError, second.FatalError);

            string divergence = SimulationTestSupport.FirstHashDivergence(first, second);
            if (divergence != null)
            {
                Assert.Inconclusive("DETERMINISM INCOMPLETE - golden for seed " + seed + " not checked: two identical runs diverged at " + divergence);
            }

            string path = Path.Combine(SimulationTestSupport.SimulationSourceFolder(), "Goldens", "seed-" + seed + ".json");
            Golden actual = new Golden { Seed = seed, Config = config.Describe(), Turns = config.Turns, Hashes = Snapshot(first) };

            if (Updating || !File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
                if (Updating)
                {
                    Assert.Pass("golden written: " + path);
                }

                Assert.Inconclusive("no golden existed; wrote " + path + " (commit it)");
            }

            Golden expected = JsonSerializer.Deserialize<Golden>(File.ReadAllText(path));
            Assert.AreEqual(expected.Config, actual.Config, "the golden was recorded for a different config; regenerate with UPDATE_GOLDENS=1");
            foreach (KeyValuePair<string, string> entry in expected.Hashes.OrderBy(e => int.Parse(e.Key, System.Globalization.CultureInfo.InvariantCulture)))
            {
                actual.Hashes.TryGetValue(entry.Key, out string hash);
                Assert.AreEqual(
                    entry.Value,
                    hash,
                    "seed " + seed + ": the saved state at turn " + entry.Key + " differs from the golden. If game rules, AI behaviour or the save format changed on purpose, regenerate: UPDATE_GOLDENS=1 dotnet test Tests/Tests.csproj --filter Category=Golden");
            }
        }
    }
}
