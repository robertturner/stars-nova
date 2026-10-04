namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // Regression test for a real fairness bug: when two different empires each have a qualifying
    // remote-mining fleet at the same (unowned) star, RemoteMiningStep previously always walked
    // ServerData.IterateAllFleets()'s fixed dictionary order, so whichever empire happened to sort
    // first in that order always got the undepleted concentration and the other always got
    // whatever was left - the same empire, every single turn of every game. Now it walks
    // ServerData.ShuffledEmpireOrder (see EmpireOrderShuffleTest), so which empire gets first crack
    // is decided by this turn's shuffle instead of being permanently fixed.
    [TestFixture]
    public class RemoteMiningOrderFairnessTest
    {
        private static Fleet MakeMiningFleet(long key, ushort owner, int mineEquivalents)
        {
            Fleet fleet = new Fleet(key);
            fleet.Owner = owner;

            ShipDesign shipDesign = new ShipDesign(key);
            shipDesign.Blueprint = new Component();
            Hull hull = new Hull { BaseCargo = 10000, Modules = new List<HullModule>() };

            HullModule miningModule = new HullModule();
            Component miningComponent = new Component();
            miningComponent.Properties.Add("Mining Robot", new IntegerProperty(mineEquivalents));
            miningModule.AllocatedComponent = miningComponent;
            hull.Modules.Add(miningModule);

            shipDesign.Blueprint.Properties.Add("Hull", hull);
            ShipToken shipToken = new ShipToken(shipDesign, 1);
            fleet.Composition.Add(shipToken.Key, shipToken);

            Waypoint waypoint = new Waypoint { Task = new NoTask() };
            fleet.Waypoints.Add(waypoint);

            return fleet;
        }

        private static (ServerData serverState, Star star, EmpireData first, EmpireData second) MakeContestedStarScenario()
        {
            ServerData serverState = new ServerData();

            Star star = new Star { Name = "Contested", Owner = Global.Nobody };
            star.MineralConcentration = new Resources(100, 0, 0, 0);
            star.MineralMiningProgress = new Resources();
            serverState.AllStars.Add(star.Name, star);

            EmpireData first = new EmpireData { Id = 1 };
            EmpireData second = new EmpireData { Id = 2 };
            serverState.AllEmpires.Add(first.Id, first);
            serverState.AllEmpires.Add(second.Id, second);

            // Large enough that a single application meaningfully depletes concentration (from
            // 100 down into the 90s - see KtToDropOnePoint), so whichever fleet mines second sees
            // a real, deterministic falloff rather than an amount too small to cross any
            // point-drop threshold at all.
            Fleet firstFleet = MakeMiningFleet(1, first.Id, mineEquivalents: 1000);
            firstFleet.InOrbit = star;
            first.OwnedFleets.Add(firstFleet);

            Fleet secondFleet = MakeMiningFleet(2, second.Id, mineEquivalents: 1000);
            secondFleet.InOrbit = star;
            second.OwnedFleets.Add(secondFleet);

            return (serverState, star, first, second);
        }

        [Test]
        public void FirstEmpireInShuffledOrder_GetsTheUndepletedConcentration()
        {
            var scenario = MakeContestedStarScenario();
            scenario.serverState.ShuffledEmpireOrder = new List<EmpireData> { scenario.first, scenario.second };

            new RemoteMiningStep().Process(scenario.serverState);

            Fleet firstFleet = scenario.first.OwnedFleets.Values.Single();
            Fleet secondFleet = scenario.second.OwnedFleets.Values.Single();

            // Both fleets mine the same mineEquivalents(100), so whichever goes first sees the
            // full concentration(100) and mines more Ironium than whoever goes second, who mines
            // against the already-depleted remainder.
            Assert.Greater(firstFleet.Cargo.Ironium, secondFleet.Cargo.Ironium,
                "The empire placed first in this turn's shuffled order should benefit from the undepleted concentration.");
        }

        [Test]
        public void ReversingTheShuffledOrder_FlipsWhichEmpireBenefits()
        {
            var scenarioA = MakeContestedStarScenario();
            scenarioA.serverState.ShuffledEmpireOrder = new List<EmpireData> { scenarioA.first, scenarioA.second };
            new RemoteMiningStep().Process(scenarioA.serverState);
            int firstEmpireIroniumWhenFirstInOrder = scenarioA.first.OwnedFleets.Values.Single().Cargo.Ironium;

            var scenarioB = MakeContestedStarScenario();
            scenarioB.serverState.ShuffledEmpireOrder = new List<EmpireData> { scenarioB.second, scenarioB.first };
            new RemoteMiningStep().Process(scenarioB.serverState);
            int firstEmpireIroniumWhenSecondInOrder = scenarioB.first.OwnedFleets.Values.Single().Cargo.Ironium;

            Assert.Greater(firstEmpireIroniumWhenFirstInOrder, firstEmpireIroniumWhenSecondInOrder,
                "The same empire should mine more when the shuffle places it first than when the shuffle places it second - proving the outcome now genuinely follows the turn's shuffled order instead of being fixed.");
        }
    }
}
