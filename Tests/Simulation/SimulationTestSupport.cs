namespace Nova.Tests.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Nova.Common;
    using Nova.Sim;

    using NUnit.Framework;

    /// <summary>
    /// A violation the simulation finds today that is a reported, not-yet-fixed game or AI bug
    /// (see docs/SIMULATION.md "Known issues"). Matching violations are listed in the test
    /// output instead of failing the suite, so the harness can land before every bug it found is
    /// fixed; once an issue no longer reproduces the test output says so and the entry should be
    /// removed.
    /// </summary>
    public sealed class KnownIssue
    {
        public KnownIssue(string id, string rule, string messageContains, string description)
        {
            Id = id;
            Rule = rule;
            MessageContains = messageContains;
            Description = description;
        }

        public string Id { get; }

        public string Rule { get; }

        /// <summary>Null matches any message of the rule.</summary>
        public string MessageContains { get; }

        public string Description { get; }

        public bool Matches(InvariantViolation violation)
        {
            return violation.Rule == Rule
                && (MessageContains == null || (violation.Message ?? string.Empty).IndexOf(MessageContains, StringComparison.OrdinalIgnoreCase) >= 0
                    || (violation.Subject ?? string.Empty).IndexOf(MessageContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    public static class SimulationTestSupport
    {
        /// <summary>
        /// The bugs the harness found that are reported but owned by other work (each entry is a
        /// BUG FOUND in the T2 report). Remove an entry once its fix lands.
        /// </summary>
        public static readonly IReadOnlyList<KnownIssue> KnownIssues = new List<KnownIssue>
        {
            new KnownIssue(
                "SIM-2",
                "DesignCap",
                "ship designs",
                "Automitrons (category 2) adds a new 'Medium Freighter [colonizer] T<year>' design every turn (an identical design re-created each year), passing the 16-design cap within ~15 turns."),
            new KnownIssue(
                "SIM-4",
                "StockpilesNonNegative",
                "negative stockpile",
                "Planet mineral stockpiles reach -1 kT (seen on Robotoids planets from turn ~48 of seed 1, Small, 4 AIs): some per-turn mineral deduction rounds past zero."),
            new KnownIssue(
                "SIM-5",
                "NoReportedErrors",
                "contains no ships",
                "An empire keeps a FleetIntel report with an empty composition (a colony fleet that colonised), so every save/intel write reports 'EmpireData.ToXml(): Fleet ... contains no ships.'"),
            new KnownIssue(
                "SIM-6",
                "WaypointTargetsResolvable",
                "planet target 'Space at",
                "TurnGenerator.UpdateFleet relabels an in-transit fleet's current-position waypoint 'Space at (x, y)' but leaves its TargetKind (Planet when it left a star), so a Planet-kind waypoint names no star."),
        };

        /// <summary>A fast config: Tiny galaxy, the given archetypes (Standard tier).</summary>
        public static SimulationConfig SmallConfig(int seed, int players, int turns)
        {
            return new SimulationConfig
            {
                Seed = seed,
                GameName = "SimTest",
                GalaxySize = GalaxySize.Tiny,
                Density = GalaxyDensity.Normal,
                Players = SimulationConfig.DefaultPlayers(players),
                Turns = turns,
                ReloadEvery = 5,
            };
        }

        public static List<InvariantViolation> UnknownViolations(SimulationResult result)
        {
            return result.Violations.Where(v => !KnownIssues.Any(k => k.Matches(v))).ToList();
        }

        /// <summary>
        /// Fails on a crash, an early stop or any violation outside the known issues; writes the
        /// summary and the known issues seen to the test output either way.
        /// </summary>
        public static void AssertHealthy(SimulationResult result)
        {
            TestContext.WriteLine(result.Summary(10));

            foreach (IGrouping<KnownIssue, InvariantViolation> group in result.Violations
                .Select(v => new { Violation = v, Issue = KnownIssues.FirstOrDefault(k => k.Matches(v)) })
                .Where(x => x.Issue != null)
                .GroupBy(x => x.Issue, x => x.Violation))
            {
                InvariantViolation first = group.First();
                TestContext.WriteLine("KNOWN ISSUE " + group.Key.Id + " (" + group.Count() + "x, first turn " + first.Turn + "): " + group.Key.Description);
            }

            Assert.IsNull(result.FatalError, "the simulation crashed: " + result.FatalError);
            Assert.IsTrue(result.Completed, "the simulation stopped early after " + result.Turns.Count + " turns");

            List<InvariantViolation> unknown = UnknownViolations(result);
            if (unknown.Count > 0)
            {
                StringBuilder message = new StringBuilder();
                message.AppendLine(unknown.Count + " invariant violation(s) (" + result.Config.Describe() + "):");
                foreach (InvariantViolation violation in unknown.Take(20))
                {
                    message.AppendLine("  " + violation);
                }

                Assert.Fail(message.ToString());
            }
        }

        /// <summary>The source folder of Tests/Simulation (for the goldens), found by walking up
        /// from the test binaries.</summary>
        public static string SimulationSourceFolder()
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "Tests", "Simulation");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Tests/Simulation not found above " + TestContext.CurrentContext.TestDirectory);
        }

        /// <summary>The first turn whose state hash differs between two runs, or null.</summary>
        public static string FirstHashDivergence(SimulationResult a, SimulationResult b)
        {
            int count = Math.Min(a.Turns.Count, b.Turns.Count);
            for (int i = 0; i < count; i++)
            {
                if (a.Turns[i].StateHash != b.Turns[i].StateHash)
                {
                    return "turn " + a.Turns[i].Turn + " (year " + a.Turns[i].Year + "): " + a.Turns[i].StateHash + " vs " + b.Turns[i].StateHash;
                }
            }

            if (a.Turns.Count != b.Turns.Count)
            {
                return "run lengths differ: " + a.Turns.Count + " vs " + b.Turns.Count + " turns";
            }

            return null;
        }
    }
}
