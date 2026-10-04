namespace Nova.Tests.UnitTests
{
    using System;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // Regression tests for Claim Adjuster (CA)'s two previously entirely-unimplemented secondary
    // effects (behavior-specs-7/race-traits.md row 7): automatic, free, instant per-turn
    // terraforming of the worst environment axis (up to the same flat 15%/30% cap the paid
    // TerraformProductionUnit already uses), and a separate 10%-per-year chance of one randomly
    // chosen axis permanently drifting 1% toward the race's ideal, uncapped.
    [TestFixture]
    public class ClaimAdjusterTerraformingTest
    {
        // NextDouble() >= 0.10 always fails Claim Adjuster's planet-drift roll, isolating these
        // tests to the deterministic, always-on terraforming mechanic alone.
        private class NeverDriftsRandom : Random
        {
            public override double NextDouble() => 1.0;
        }

        private static (ServerData serverState, Star star) BuildScenario(int gravity, int originalGravity)
        {
            ServerData serverState = new ServerData();

            EmpireData empire = new EmpireData { Id = 1 };
            empire.Race.Traits.SetPrimary("CA");
            serverState.AllEmpires.Add(empire.Id, empire);

            Star star = new Star { Name = "Homeworld", Owner = empire.Id, Colonists = 1000, Gravity = gravity, OriginalGravity = originalGravity };
            serverState.AllStars.Add(star.Key, star);
            empire.OwnedStars.Add(star);

            return (serverState, star);
        }

        [Test]
        public void Process_ClaimAdjusterRace_AutomaticallyImprovesTheWorstAxis_ForFree_EveryTurn()
        {
            // A default Race's Gravity ideal is 50 (EnvironmentTolerance's default 20-80 band's
            // median); starting at 0 is as far as possible from it.
            var scenario = BuildScenario(gravity: 0, originalGravity: 0);
            int energyBefore = scenario.star.ResourcesOnHand.Energy;

            new StarUpdateStep(new NeverDriftsRandom()).Process(scenario.serverState);

            Assert.AreEqual(1, scenario.star.Gravity, "Gravity should nudge exactly one step toward the ideal (50), free of charge");
            Assert.AreEqual(energyBefore, scenario.star.ResourcesOnHand.Energy, "Claim Adjuster's terraforming is free - it must not touch ResourcesOnHand at all");
        }

        [Test]
        public void Process_ClaimAdjusterRace_StopsOnceTheFlatFifteenPercentCapIsReached()
        {
            // Gravity has already moved the full 15 points away from OriginalGravity (the flat,
            // already-disclosed simplification MaxTerraformPercent uses for "up to current tech"),
            // even though it's still far from the race's ideal (50) - the cap, not the ideal,
            // must be what stops further automatic improvement.
            var scenario = BuildScenario(gravity: 15, originalGravity: 0);

            new StarUpdateStep(new NeverDriftsRandom()).Process(scenario.serverState);

            Assert.AreEqual(15, scenario.star.Gravity, "Already at the 15% terraform cap - must not improve further even though far from ideal");
        }

        [Test]
        public void Process_NonClaimAdjusterRace_GetsNoAutomaticTerraforming()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 }; // no CA trait
            serverState.AllEmpires.Add(empire.Id, empire);
            Star star = new Star { Name = "Homeworld", Owner = empire.Id, Colonists = 1000, Gravity = 0, OriginalGravity = 0 };
            serverState.AllStars.Add(star.Key, star);

            new StarUpdateStep(new NeverDriftsRandom()).Process(serverState);

            Assert.AreEqual(0, star.Gravity, "Without Claim Adjuster, environment axes never move on their own");
        }
    }

    [TestFixture]
    public class ClaimAdjusterPlanetDriftTest
    {
        private class FixedRollRandom : Random
        {
            private readonly double doubleValue;
            private readonly int intValue;

            public FixedRollRandom(double doubleValue, int intValue)
            {
                this.doubleValue = doubleValue;
                this.intValue = intValue;
            }

            public override double NextDouble() => doubleValue;

            public override int Next(int maxValue) => intValue;
        }

        private static (ServerData serverState, Star star) BuildScenarioAtTerraformCap()
        {
            ServerData serverState = new ServerData();

            EmpireData empire = new EmpireData { Id = 1 };
            empire.Race.Traits.SetPrimary("CA");
            serverState.AllEmpires.Add(empire.Id, empire);

            // Gravity is already at the 15% terraform cap (see ClaimAdjusterTerraformingTest),
            // isolating these assertions to the SEPARATE, uncapped drift mechanic - the
            // deterministic terraforming above must have nothing left to do here.
            Star star = new Star { Name = "Homeworld", Owner = empire.Id, Colonists = 1000, Gravity = 15, OriginalGravity = 0 };
            serverState.AllStars.Add(star.Key, star);
            empire.OwnedStars.Add(star);

            return (serverState, star);
        }

        [Test]
        public void Process_TenPercentRollSucceeds_NudgesTheRandomlyChosenAxis_EvenPastTheTerraformCap()
        {
            // NextDouble() = 0.05 < 0.10 -> the drift roll succeeds; Next(3) = 0 -> Gravity (the
            // 0th of the three axes) is the one randomly chosen.
            var scenario = BuildScenarioAtTerraformCap();

            new StarUpdateStep(new FixedRollRandom(0.05, 0)).Process(scenario.serverState);

            Assert.AreEqual(16, scenario.star.Gravity, "Drift has no cap - it must move Gravity even though ordinary terraforming is capped out at 15");
        }

        [Test]
        public void Process_TenPercentRollFails_LeavesEveryAxisUntouched()
        {
            // NextDouble() = 0.50 >= 0.10 -> the drift roll fails this turn.
            var scenario = BuildScenarioAtTerraformCap();

            new StarUpdateStep(new FixedRollRandom(0.50, 0)).Process(scenario.serverState);

            Assert.AreEqual(15, scenario.star.Gravity, "A failed roll must leave the star exactly as it was");
        }
    }
}
