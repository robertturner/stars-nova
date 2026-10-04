namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Drawing;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // behavior-specs-8/turn-generation-engine.md section 1 rebuilds the master routine's order from
    // its real call sequence (40 steps). What matters here:
    //  - the battle pass (step 23) runs AFTER fleet movement (16) and the production hub (20) - the
    //    old list put combat at phase 9, before both - so a fleet that arrives fights the same turn
    //    and ships completed this turn can be fought at their build location;
    //  - the year counter (33), victory evaluation (35) and the per-player output/scan (39) come
    //    after all of that simulation.
    [TestFixture]
    public class TurnOrderTest
    {
        /// <summary>A turn step that only records what the galaxy looked like when it ran.</summary>
        private class ProbeStep : ITurnStep
        {
            private readonly ServerData serverState;
            private readonly EmpireData watched;

            public int BattleReportsSeen = -1;
            public int TurnYearSeen = -1;
            public bool Ran;

            public ProbeStep(ServerData serverState, EmpireData watched)
            {
                this.serverState = serverState;
                this.watched = watched;
            }

            public void Process(ServerData state)
            {
                Ran = true;
                BattleReportsSeen = watched.BattleReports.Count;
                TurnYearSeen = serverState.TurnYear;
            }
        }

        private const int StartYear = 2400;

        private ServerData serverState;
        private EmpireData empire1;
        private SimpleTurnGenerator generator;

        [SetUp]
        public void Init()
        {
            serverState = new ServerData { TurnYear = StartYear };

            empire1 = new SimpleEmpireData { Id = 1 };
            EmpireData empire2 = new SimpleEmpireData { Id = 2 };
            serverState.AllEmpires[empire1.Id] = empire1;
            serverState.AllEmpires[empire2.Id] = empire2;

            empire1.EmpireReports.Add(empire2.Id, new EmpireIntel(empire2) { Relation = PlayerRelation.Enemy });
            empire2.EmpireReports.Add(empire1.Id, new EmpireIntel(empire1) { Relation = PlayerRelation.Enemy });
            empire1.BattlePlans["Default"] = new BattlePlan { Attack = "Everyone" };
            empire2.BattlePlans["Default"] = new BattlePlan { Attack = "Everyone" };

            Component blueprint = new Component { Cost = new Resources(10, 20, 30, 40), Mass = 100 };
            Hull hull = new Hull { FuelCapacity = 100, ArmorStrength = 50 };
            hull.Modules = new List<HullModule>();
            blueprint.Properties.Add("Hull", hull);
            blueprint.Properties.Add("Battle Movement", new DoubleProperty(1.0));
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint, Name = "Test Ship" };
            design.Update();

            uint fleetId = 1;
            foreach (EmpireData empire in new[] { empire1, empire2 })
            {
                Fleet fleet = new Fleet("Fleet " + empire.Id, empire.Id, fleetId++, new Point(500, 500));
                ShipToken token = new ShipToken(design, 1) { Armor = design.Armor };
                fleet.Composition.Add(token.Key, token);
                empire.OwnedFleets.Add(fleet);
            }

            // Victory evaluation divides by the star count, so the galaxy needs at least one.
            Star star = new Star { Name = "Nowhere" };
            serverState.AllStars.Add(star.Key, star);

            generator = new SimpleTurnGenerator(serverState);
        }

        private ProbeStep AddProbe(int order)
        {
            FieldInfo field = typeof(TurnGenerator).GetField("turnSteps", BindingFlags.Instance | BindingFlags.NonPublic);
            var steps = (SortedList<int, ITurnStep>)field.GetValue(generator);

            ProbeStep probe = new ProbeStep(serverState, empire1);
            steps.Add(order, probe);
            return probe;
        }

        [Test]
        public void Battle_RunsAfterTheProductionSteps_AndBeforeTheLaterSteps()
        {
            // Production steps are keyed 11 (remote mining) and 12 (planet update); the steps that
            // follow combat start at bombing (19).
            ProbeStep afterProduction = AddProbe(13);
            ProbeStep justBeforeBombing = AddProbe(18);
            ProbeStep afterBattle = AddProbe(22);

            generator.Generate();

            Assert.IsTrue(afterProduction.Ran && justBeforeBombing.Ran && afterBattle.Ran);
            Assert.AreEqual(0, afterProduction.BattleReportsSeen,
                "The production steps (11, 12) must already have run by the time combat starts, so combat has not happened yet");
            Assert.AreEqual(0, justBeforeBombing.BattleReportsSeen,
                "Every step up to and including the production ones still precedes combat");
            Assert.AreEqual(1, afterBattle.BattleReportsSeen,
                "Combat resolves once production is done, before the later steps (bombing onward)");
        }

        [Test]
        public void YearCounterAndVictoryCome_AfterAllSimulation_AndBeforeTheScanOutputStep()
        {
            ProbeStep lateSimulation = AddProbe(60);   // after every simulation step, before scan (99)
            ProbeStep afterScan = AddProbe(100);       // after the scan/output step

            generator.Generate();

            Assert.AreEqual(StartYear, lateSimulation.TurnYearSeen, "Not incremented until all simulation has run");
            Assert.AreEqual(StartYear + 1, afterScan.TurnYearSeen, "Incremented before the output/scan step");
            Assert.AreEqual(StartYear + 1, serverState.TurnYear);
            Assert.AreEqual(1, lateSimulation.BattleReportsSeen, "Combat already happened");
        }
    }
}
