namespace Nova.Tests.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Sim;

    using NUnit.Framework;

    /// <summary>
    /// Each invariant rule catches the corruption it is named for: one real one-turn game is
    /// played, then every scenario reloads that turn's saved state, breaks one thing and runs the
    /// checker, so a rule that silently stopped working would fail here (and the clean baseline
    /// shows the rules do not fire on a healthy state).
    /// </summary>
    [TestFixture]
    [Category("Simulation")]
    public class InvariantCheckerTest
    {
        private sealed class Scenario
        {
            public string Name;
            public string ExpectedRule;
            public Action<ServerData, EmpireData> Corrupt;
            public Action<TurnObservation, EmpireData> Observe;
            public HashSet<string> Disabled;
        }

        private static readonly Dictionary<string, List<InvariantViolation>> Outcomes = new Dictionary<string, List<InvariantViolation>>();
        private static string setupFailure;

        private static IEnumerable<Scenario> Scenarios()
        {
            yield return new Scenario { Name = "Baseline" };
            yield return new Scenario
            {
                Name = "NegativeMinerals",
                ExpectedRule = "StockpilesNonNegative",
                Corrupt = (state, empire) => Home(state, empire).ResourcesOnHand.Ironium = -5,
            };
            yield return new Scenario
            {
                Name = "NegativeMineralsWithRuleDisabled",
                Corrupt = (state, empire) => Home(state, empire).ResourcesOnHand.Ironium = -5,
                Disabled = new HashSet<string> { "StockpilesNonNegative" },
            };
            yield return new Scenario
            {
                Name = "NaNFuel",
                ExpectedRule = "NumbersFinite",
                Corrupt = (state, empire) => AnyFleet(empire).FuelAvailable = double.NaN,
            };
            yield return new Scenario
            {
                Name = "TechAbove26",
                ExpectedRule = "TechLevelsInRange",
                Corrupt = (state, empire) => empire.ResearchLevels[TechLevel.ResearchField.Energy] = TechLevel.MaxLevel + 1,
            };
            yield return new Scenario
            {
                Name = "FleetOffTheMap",
                ExpectedRule = "FleetsInsideMap",
                Corrupt = (state, empire) =>
                {
                    Fleet fleet = AnyFleet(empire);
                    fleet.InOrbit = null;
                    fleet.Position = new NovaPoint(100000, 5);
                },
            };
            yield return new Scenario
            {
                Name = "WaypointToAMissingStar",
                ExpectedRule = "WaypointTargetsResolvable",
                Corrupt = (state, empire) => AnyFleet(empire).Waypoints.Add(new Waypoint
                {
                    Position = new NovaPoint(Home(state, empire).Position),
                    Destination = "No Such Star",
                    TargetKind = WaypointTargetKind.Planet,
                }),
            };
            yield return new Scenario
            {
                Name = "EmptyFleet",
                ExpectedRule = "NoEmptyFleets",
                Corrupt = (state, empire) => AnyFleet(empire).Composition.Clear(),
            };
            yield return new Scenario
            {
                Name = "OwnerThatDoesNotExist",
                ExpectedRule = "PlanetOwnersExist",
                Corrupt = (state, empire) => state.AllStars.Values.First(star => star.Owner == Global.Nobody).Owner = 15,
            };
            yield return new Scenario
            {
                Name = "SeventeenDesigns",
                ExpectedRule = "DesignCap",
                Corrupt = (state, empire) =>
                {
                    ShipDesign template = empire.Designs.Values.First(design => !design.IsStarbase);
                    for (int i = 0; i < Global.MaxDesignsAmount + 1; i++)
                    {
                        ShipDesign copy = new ShipDesign(template) { Key = empire.GetNextDesignKey() };
                        copy.Name = "Copy " + i;
                        empire.Designs[copy.Key] = copy;
                    }
                },
            };
            yield return new Scenario
            {
                Name = "QueuedShipOfAnUnknownDesign",
                ExpectedRule = "ProductionQueuesValid",
                Corrupt = (state, empire) =>
                {
                    ShipDesign ghost = new ShipDesign(empire.Designs.Values.First(design => !design.IsStarbase)) { Key = empire.GetNextDesignKey() };
                    Home(state, empire).ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(ghost), false));
                },
            };
            yield return new Scenario
            {
                Name = "RunawayPopulation",
                ExpectedRule = "PopulationWithinCapacity",
                Corrupt = (state, empire) => Home(state, empire).Colonists = 500000000,
            };
            yield return new Scenario
            {
                Name = "FlaggedEliminatedWhileAlive",
                ExpectedRule = "ScoreEliminationConsistent",
                Corrupt = (state, empire) => empire.Eliminated = true,
            };
            yield return new Scenario
            {
                Name = "PlanetOfAnotherRace",
                ExpectedRule = "PlanetRaceMatchesOwner",
                Corrupt = (state, empire) => Home(state, empire).ThisRace = state.AllEmpires[2].Race,
            };
            yield return new Scenario
            {
                Name = "StarbaseOnAnUnownedPlanet",
                ExpectedRule = "StarbaseConsistent",
                Corrupt = (state, empire) => Home(state, empire).Owner = Global.Nobody,
            };
            yield return new Scenario
            {
                Name = "FleetKeyStoredUnderAnotherKey",
                ExpectedRule = "KeysUnique",
                Corrupt = (state, empire) =>
                {
                    Fleet fleet = AnyFleet(empire);
                    empire.OwnedFleets.Remove(fleet.Key);
                    empire.OwnedFleets[fleet.Key + 1000] = fleet;
                },
            };
            yield return new Scenario
            {
                Name = "OrbitAwayFromItsStar",
                ExpectedRule = "OrbitConsistent",
                Corrupt = (state, empire) =>
                {
                    Fleet fleet = empire.OwnedFleets.Values.First(f => f.InOrbit != null && !f.IsStarbase);
                    fleet.Position = new NovaPoint(fleet.InOrbit.Position.X + 3, fleet.InOrbit.Position.Y);
                },
            };
            yield return new Scenario
            {
                Name = "MessageStorm",
                ExpectedRule = "MessageCountBounded",
                Observe = (turn, empire) =>
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        turn.Generation.Messages.Add(new Message(empire.Id, "storm " + i, "Test", null));
                    }
                },
            };
            yield return new Scenario
            {
                Name = "RejectedOrders",
                ExpectedRule = "AiOrdersAccepted",
                Observe = (turn, empire) => turn.Generation.RejectedByEmpire[empire.Id] = 2,
            };
            yield return new Scenario
            {
                Name = "OrdersFileNotAccepted",
                ExpectedRule = "AiOrdersAccepted",
                Observe = (turn, empire) => turn.OrdersNotAccepted.Add(empire.Id),
            };
            yield return new Scenario
            {
                Name = "ReportedError",
                ExpectedRule = "NoReportedErrors",
                Observe = (turn, empire) => turn.Errors.Add("Nova has encountered an error: boom"),
            };
            yield return new Scenario
            {
                Name = "SlowTurn",
                ExpectedRule = "TurnTimeBounded",
                Observe = (turn, empire) => turn.TurnSeconds = 100000,
            };
            yield return new Scenario
            {
                Name = "RoundTripDiffers",
                ExpectedRule = "SaveRoundTrip",
                Observe = (turn, empire) =>
                {
                    turn.RoundTripChecked = true;
                    turn.RoundTripDifference = "line 1: a vs b";
                },
            };
        }

        private static Star Home(ServerData state, EmpireData empire)
        {
            return state.AllStars.Values.First(star => star.Owner == empire.Id && star.Starbase != null);
        }

        private static Fleet AnyFleet(EmpireData empire)
        {
            return empire.OwnedFleets.Values.First(fleet => !fleet.IsStarbase);
        }

        [OneTimeSetUp]
        public void PlayOneTurnAndRunEveryScenario()
        {
            SimulationConfig config = SimulationTestSupport.SmallConfig(seed: 3, players: 2, turns: 1);
            SimulationRunner runner = new SimulationRunner(config)
            {
                AfterTurn = context =>
                {
                    foreach (Scenario scenario in Scenarios())
                    {
                        ServerData state = new ServerData { StatePathName = context.State.StatePathName };
                        state.Restore();
                        state.GameFolder = context.State.GameFolder;
                        EmpireData empire = state.AllEmpires[1];

                        TurnObservation observation = new TurnObservation { Turn = 1, Year = state.TurnYear };
                        observation.AiEmpires.UnionWith(state.AllEmpires.Keys);
                        scenario.Corrupt?.Invoke(state, empire);
                        scenario.Observe?.Invoke(observation, empire);

                        Outcomes[scenario.Name] = new InvariantChecker().Check(new InvariantContext(state, observation, config.Limits), scenario.Disabled);
                    }
                },
            };

            SimulationResult result = runner.Run();
            if (result.FatalError != null)
            {
                setupFailure = result.FatalError;
            }
        }

        private static List<InvariantViolation> Outcome(string name)
        {
            Assert.IsNull(setupFailure, setupFailure);
            Assert.IsTrue(Outcomes.ContainsKey(name), "scenario " + name + " did not run");
            return Outcomes[name];
        }

        [Test]
        public void TheCleanState_BreaksNoRule()
        {
            List<InvariantViolation> violations = Outcome("Baseline");
            CollectionAssert.IsEmpty(violations.Select(v => v.ToString()));
        }

        [TestCase("NegativeMinerals")]
        [TestCase("NaNFuel")]
        [TestCase("TechAbove26")]
        [TestCase("FleetOffTheMap")]
        [TestCase("WaypointToAMissingStar")]
        [TestCase("EmptyFleet")]
        [TestCase("OwnerThatDoesNotExist")]
        [TestCase("SeventeenDesigns")]
        [TestCase("QueuedShipOfAnUnknownDesign")]
        [TestCase("RunawayPopulation")]
        [TestCase("FlaggedEliminatedWhileAlive")]
        [TestCase("PlanetOfAnotherRace")]
        [TestCase("StarbaseOnAnUnownedPlanet")]
        [TestCase("FleetKeyStoredUnderAnotherKey")]
        [TestCase("OrbitAwayFromItsStar")]
        [TestCase("MessageStorm")]
        [TestCase("RejectedOrders")]
        [TestCase("OrdersFileNotAccepted")]
        [TestCase("ReportedError")]
        [TestCase("SlowTurn")]
        [TestCase("RoundTripDiffers")]
        public void EachCorruption_IsCaughtByItsNamedRule(string scenarioName)
        {
            Scenario scenario = Scenarios().Single(s => s.Name == scenarioName);
            List<InvariantViolation> violations = Outcome(scenarioName);
            Assert.IsTrue(
                violations.Any(v => v.Rule == scenario.ExpectedRule),
                scenario.ExpectedRule + " did not fire; got: " + string.Join(" | ", violations.Select(v => v.ToString())));
        }

        [Test]
        public void AViolation_NamesTheRuleTurnEmpireAndObject()
        {
            InvariantViolation violation = Outcome("NegativeMinerals").Single(v => v.Rule == "StockpilesNonNegative");
            Assert.AreEqual(1, violation.Turn);
            Assert.AreEqual(2101, violation.Year);
            Assert.AreEqual(1, violation.EmpireId);
            StringAssert.StartsWith("star ", violation.Subject);
            StringAssert.Contains("-5", violation.Message);
            StringAssert.Contains("[StockpilesNonNegative] turn 1 (year 2101) empire 1 star ", violation.ToString());
        }

        [Test]
        public void ADisabledRule_DoesNotReport()
        {
            CollectionAssert.IsEmpty(Outcome("NegativeMineralsWithRuleDisabled").Where(v => v.Rule == "StockpilesNonNegative"));
        }

        [Test]
        public void EveryRule_HasAUniqueNameAndADescription()
        {
            List<InvariantRule> rules = InvariantChecker.DefaultRules();
            CollectionAssert.AllItemsAreUnique(rules.Select(r => r.Name));
            Assert.IsTrue(rules.All(r => !string.IsNullOrWhiteSpace(r.Description)));
            Assert.GreaterOrEqual(rules.Count, 22);
        }
    }
}
