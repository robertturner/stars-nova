#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Tests.UnitTests
{
    using System.Drawing;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Covers two real bugs found while adding turn-end battle reporting (a "You had N battles
    /// this turn" summary message, plus fixing tap-to-replay): BattleEngine.Run()'s per-location
    /// loop used to `return` (not `continue`) the instant any ONE location turned out to have no
    /// hostile orders - silently abandoning every other, genuinely hostile, battle location later
    /// in the same turn - and every battle in a turn shared and mutated a single BattleReport
    /// instance, so a turn with 2+ battles left every stored report/message pointing at whichever
    /// battle happened to run last.
    /// </summary>
    [TestFixture]
    public class BattleReportGenerationTest
    {
        private const int Empire1Id = 1; // fights at both locations
        private const int Empire2Id = 2; // co-located with Empire1 at NoBattle - no hostile orders
        private const int Empire3Id = 3; // fights Empire1 at BattleOne

        private ServerData serverState;
        private BattleEngine battleEngine;

        private static ShipDesign MakeDesign(long key, string name)
        {
            Component blueprint = new Component { Cost = new Resources(10, 20, 30, 40), Mass = 100 };
            Hull hull = new Hull { FuelCapacity = 100, ArmorStrength = 50 };
            hull.Modules = new System.Collections.Generic.List<HullModule>();
            blueprint.Properties.Add("Hull", hull);
            blueprint.Properties.Add("Battle Movement", new DoubleProperty(1.0));

            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Name = name };
            design.Update();
            return design;
        }

        private static uint nextFleetId = 1;

        private static void AddFleet(EmpireData empire, ShipDesign design, string name, Point position)
        {
            Fleet fleet = new Fleet(name, empire.Id, nextFleetId++, position);
            ShipToken token = new ShipToken(design, 1) { Armor = design.Armor };
            fleet.Composition.Add(token.Key, token);
            empire.OwnedFleets.Add(fleet);
        }

        [SetUp]
        public void Init()
        {
            serverState = new ServerData();
            serverState.TurnYear = 2400;
            battleEngine = new BattleEngine(serverState, new BattleReport());

            EmpireData empire1 = new EmpireData { Id = Empire1Id };
            EmpireData empire2 = new EmpireData { Id = Empire2Id };
            EmpireData empire3 = new EmpireData { Id = Empire3Id };

            serverState.AllEmpires[empire1.Id] = empire1;
            serverState.AllEmpires[empire2.Id] = empire2;
            serverState.AllEmpires[empire3.Id] = empire3;

            empire1.EmpireReports.Add(empire2.Id, new EmpireIntel(empire2) { Relation = PlayerRelation.Neutral });
            empire2.EmpireReports.Add(empire1.Id, new EmpireIntel(empire1) { Relation = PlayerRelation.Neutral });
            empire1.EmpireReports.Add(empire3.Id, new EmpireIntel(empire3) { Relation = PlayerRelation.Enemy });
            empire3.EmpireReports.Add(empire1.Id, new EmpireIntel(empire1) { Relation = PlayerRelation.Enemy });

            // NoBattle location: both sides passive - SelectTargets must return 0 here.
            empire1.BattlePlans["Default"] = new BattlePlan { Attack = "None" };
            empire2.BattlePlans["Default"] = new BattlePlan { Attack = "None" };
            // BattleOne location: both sides hostile - a real battle.
            empire3.BattlePlans["Default"] = new BattlePlan { Attack = "Everyone" };

            ShipDesign design = MakeDesign(1, "Test Ship");

            // Empire1's own fleets are added NoBattle-position first, BattleOne-position second,
            // so DetermineCoLocatedFleets/engagements naturally discovers NoBattle first - the
            // exact ordering the old `return` bug needed to skip the real battle that follows it.
            AddFleet(empire1, design, "Empire1 NoBattle Fleet", new Point(0, 0));
            AddFleet(empire2, design, "Empire2 NoBattle Fleet", new Point(0, 0));
            AddFleet(empire1, design, "Empire1 Battle Fleet", new Point(500, 500));
            AddFleet(empire3, design, "Empire3 Battle Fleet", new Point(500, 500));
        }

        [Test]
        public void NonHostileLocationProcessedFirst_DoesNotSkipARealBattleLaterInTheSameTurn()
        {
            battleEngine.Run();

            EmpireData empire1 = serverState.AllEmpires[Empire1Id];
            EmpireData empire3 = serverState.AllEmpires[Empire3Id];

            Assert.AreEqual(1, empire1.BattleReports.Count, "The real battle at BattleOne must still run even though the co-located, non-hostile NoBattle location was processed first.");
            Assert.AreEqual(1, empire3.BattleReports.Count);
            Assert.AreEqual("coordinates (500, 500)", empire1.BattleReports[0].Location);
        }

        [Test]
        public void EachBattleLocationGetsItsOwnDistinctReport_NotASharedMutatedInstance()
        {
            // Add a second, independent real battle at a third location so this Run() call
            // processes two genuine battles - the exact scenario the shared/mutated `battle`
            // field bug corrupted (every stored report would end up showing only the last one).
            EmpireData empire1 = serverState.AllEmpires[Empire1Id];
            EmpireData empire4 = new EmpireData { Id = 4 };
            serverState.AllEmpires[empire4.Id] = empire4;
            empire1.EmpireReports.Add(empire4.Id, new EmpireIntel(empire4) { Relation = PlayerRelation.Enemy });
            empire4.EmpireReports.Add(empire1.Id, new EmpireIntel(empire1) { Relation = PlayerRelation.Enemy });
            empire4.BattlePlans["Default"] = new BattlePlan { Attack = "Everyone" };

            ShipDesign design = MakeDesign(2, "Test Ship 2");
            AddFleet(empire1, design, "Empire1 Second Battle Fleet", new Point(900, 900));
            AddFleet(empire4, design, "Empire4 Battle Fleet", new Point(900, 900));

            battleEngine.Run();

            Assert.AreEqual(2, empire1.BattleReports.Count, "Empire1 fought in two distinct battles this turn.");
            BattleReport first = empire1.BattleReports[0];
            BattleReport second = empire1.BattleReports[1];

            Assert.AreNotSame(first, second, "Each battle location must produce its own BattleReport instance, not share/mutate one.");
            Assert.AreNotEqual(first.Location, second.Location);
            Assert.AreEqual(serverState.TurnYear, first.Year, "Year must be stamped on the report so its Key stays unique across turns.");
            Assert.AreEqual(serverState.TurnYear, second.Year);
        }

        [Test]
        public void GeneratesAPerEmpireBattleCountSummaryMessage_OnlyForEmpiresThatActuallyFought()
        {
            battleEngine.Run();

            Message empire1Summary = serverState.AllMessages.FirstOrDefault(m => m.Type == "BattleSummary" && m.Audience == Empire1Id);
            Message empire3Summary = serverState.AllMessages.FirstOrDefault(m => m.Type == "BattleSummary" && m.Audience == Empire3Id);
            bool empire2HasSummary = serverState.AllMessages.Any(m => m.Type == "BattleSummary" && m.Audience == Empire2Id);

            Assert.IsNotNull(empire1Summary, "Empire1 fought a real battle and should get a summary message.");
            Assert.AreEqual("You had 1 battle this turn.", empire1Summary.Text);
            Assert.IsNotNull(empire3Summary);
            Assert.IsFalse(empire2HasSummary, "Empire2's only location was the non-hostile NoBattle one - it never actually fought and shouldn't get a summary.");
        }

        [Test]
        public void BattleSummaryMessage_PrecedesThatTurnsPerBattleDetailMessages()
        {
            battleEngine.Run();

            int summaryIndex = serverState.AllMessages.FindIndex(m => m.Type == "BattleSummary" && m.Audience == Empire1Id);
            int detailIndex = serverState.AllMessages.FindIndex(m => m.Type == "BattleReport" && m.Audience == Empire1Id);

            Assert.GreaterOrEqual(summaryIndex, 0);
            Assert.GreaterOrEqual(detailIndex, 0);
            Assert.Less(summaryIndex, detailIndex, "The 'You had N battles' summary should read as a lead-in, before the detail message(s) it summarizes.");
        }
    }
}
