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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;

    // behavior-specs-7's turn-generation-engine.md confirms victory evaluation runs AFTER the
    // turn/year counter increments, not before - TurnGenerator.cs previously called
    // victoryCheck.Victor() one line before serverState.TurnYear++, so gameTime (computed as
    // TurnYear - StartingYear inside Victor()) was always one year behind where the original
    // game evaluates it. These tests exercise VictoryCheck.Victor() directly (bypassing
    // TurnGenerator.Generate(), which clears AllMessages before returning - see
    // StargateJumpTest.cs's own comment on the same limitation) to confirm its gameTime gate and
    // per-empire target-count threshold are correct, matching the year value TurnGenerator now
    // feeds it post-increment.
    [TestFixture]
    public class VictoryCheckTest
    {
        // GameSettings.Data is a process-wide singleton: every victory field this fixture touches
        // is swapped for a fresh, all-disabled copy here and the originals restored afterwards,
        // so the tests neither depend on nor leak into another fixture's settings.
        private EnabledValue[] savedConditions;
        private int savedTargetsToMeet;
        private int savedMinimumGameTime;

        [SetUp]
        public void SaveAndClearVictorySettings()
        {
            GameSettings s = GameSettings.Data;
            savedConditions = new[]
            {
                s.PlanetsOwned, s.TechLevels, s.NumberOfFields, s.TotalScore, s.SecondPlaceScore,
                s.ProductionCapacity, s.CapitalShips, s.HighestScore,
            };
            savedTargetsToMeet = s.TargetsToMeet;
            savedMinimumGameTime = s.MinimumGameTime;

            s.PlanetsOwned = new EnabledValue(false, 60);
            s.TechLevels = new EnabledValue(false, 22);
            s.NumberOfFields = new EnabledValue(false, 4);
            s.TotalScore = new EnabledValue(false, 11000);
            s.SecondPlaceScore = new EnabledValue(false, 100);
            s.ProductionCapacity = new EnabledValue(false, 100);
            s.CapitalShips = new EnabledValue(false, 100);
            s.HighestScore = new EnabledValue(false, 100);
            s.TargetsToMeet = 1;
            s.MinimumGameTime = 0;
        }

        [TearDown]
        public void RestoreVictorySettings()
        {
            GameSettings s = GameSettings.Data;
            s.PlanetsOwned = savedConditions[0];
            s.TechLevels = savedConditions[1];
            s.NumberOfFields = savedConditions[2];
            s.TotalScore = savedConditions[3];
            s.SecondPlaceScore = savedConditions[4];
            s.ProductionCapacity = savedConditions[5];
            s.CapitalShips = savedConditions[6];
            s.HighestScore = savedConditions[7];
            s.TargetsToMeet = savedTargetsToMeet;
            s.MinimumGameTime = savedMinimumGameTime;
        }

        private static (ServerData serverData, EmpireData empireA) MakeTwoEmpireGame()
        {
            ServerData serverData = new ServerData();

            Star owned = new Star();
            owned.Name = "Owned";
            owned.Owner = 1;
            Star other = new Star();
            other.Name = "Other";
            other.Owner = 2;
            serverData.AllStars.Add(owned.Key, owned);
            serverData.AllStars.Add(other.Key, other);

            EmpireData empireA = new EmpireData();
            empireA.Id = 1;
            empireA.Race = new Race();
            EmpireData empireB = new EmpireData();
            empireB.Id = 2;
            empireB.Race = new Race();
            serverData.AllEmpires.Add(1, empireA);
            serverData.AllEmpires.Add(2, empireB);

            return (serverData, empireA);
        }

        [Test]
        public void Victor_BelowMinimumGameTime_DeclaresNoWinner_EvenIfEveryOtherConditionIsMet()
        {
            var (serverData, _) = MakeTwoEmpireGame();
            GameSettings.Data.PlanetsOwned.IsChecked = true;
            GameSettings.Data.PlanetsOwned.NumericValue = 50; // empire A owns 1 of 2 stars = 50%
            GameSettings.Data.TargetsToMeet = 1;
            GameSettings.Data.MinimumGameTime = 10;
            serverData.TurnYear = Global.StartingYear + 9; // gameTime = 9, one year short of the gate

            VictoryCheck victoryCheck = new VictoryCheck(serverData, new Scores(serverData));
            victoryCheck.Victor();

            Assert.IsFalse(serverData.AllMessages.Exists(m => m.Text.Contains("have won the game")),
                "A gameTime below MinimumGameTime must not declare a winner, even with every other condition already met");
        }

        [Test]
        public void Victor_AtMinimumGameTime_DeclaresTheQualifyingEmpireTheWinner()
        {
            var (serverData, empireA) = MakeTwoEmpireGame();
            GameSettings.Data.PlanetsOwned.IsChecked = true;
            GameSettings.Data.PlanetsOwned.NumericValue = 50;
            GameSettings.Data.TargetsToMeet = 1;
            GameSettings.Data.MinimumGameTime = 10;
            // This is exactly the post-increment year TurnGenerator now feeds Victor() with -
            // the same turn that, before the ordering fix, would have been evaluated as gameTime 9.
            serverData.TurnYear = Global.StartingYear + 10;

            VictoryCheck victoryCheck = new VictoryCheck(serverData, new Scores(serverData));
            victoryCheck.Victor();

            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Text.Contains("have won the game")),
                "Empire A meets the one required condition (50% planet ownership) exactly at the MinimumGameTime gate");
        }

        [Test]
        public void Victor_LastEmpireStanding_DeclaresVictoryRegardlessOfYearOrOtherConditions()
        {
            ServerData serverData = new ServerData();
            Star onlyStar = new Star();
            onlyStar.Name = "Sole";
            onlyStar.Owner = 1;
            serverData.AllStars.Add(onlyStar.Key, onlyStar);

            EmpireData empireA = new EmpireData();
            empireA.Id = 1;
            empireA.Race = new Race();
            serverData.AllEmpires.Add(1, empireA);

            serverData.TurnYear = Global.StartingYear; // year 0 - far below any real MinimumGameTime
            GameSettings.Data.MinimumGameTime = 999;

            VictoryCheck victoryCheck = new VictoryCheck(serverData, new Scores(serverData));
            victoryCheck.Victor();

            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Text.Contains("have won the game")),
                "A sole remaining empire wins outright - the year-gate only applies to the multi-empire target-count path");
        }

        // ------------------------------------------------------------------------------------
        // behavior-specs-9: the conditions read the score record (victory-conditions.md section 2;
        // client-ui-dialog-catalog.md "Score display"): condition 6 = Resources (summed planet
        // OUTPUT) / 1,000, condition 7 = the Capital ships count, 5 and 8 the Score with shared
        // ranks; and the elimination flag with messages 187/188.
        // ------------------------------------------------------------------------------------

        private static EmpireData AddEmpire(ServerData serverData, ushort id)
        {
            EmpireData empire = new EmpireData();
            empire.Id = id;
            empire.Race = new Race();
            empire.Race.ColonistsPerResource = 1000;
            empire.Race.PluralName = "Race" + id;
            serverData.AllEmpires.Add(id, empire);
            return empire;
        }

        private static Star AddStar(ServerData serverData, EmpireData owner, string name, int colonists)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = owner.Id;
            star.Colonists = colonists;
            star.ThisRace = owner.Race;
            serverData.AllStars.Add(star.Key, star);
            return star;
        }

        private static bool SomeoneWon(ServerData serverData)
        {
            return serverData.AllMessages.Exists(m => m.Text != null && m.Text.Contains("have won the game"));
        }

        [Test]
        public void ProductionCapacity_IsTotalResourceOutputOver1000_NotPerPlanetLeftover()
        {
            ServerData serverData = new ServerData();
            EmpireData a = AddEmpire(serverData, 1);
            EmpireData b = AddEmpire(serverData, 2);
            // Two planets producing 600 resources a year each: 1,200 total = 1 thousand. The old
            // check summed floor(leftover / 1000) per planet, which is 0 + 0 here.
            Star a1 = AddStar(serverData, a, "A1", 600000);
            Star a2 = AddStar(serverData, a, "A2", 600000);
            a1.ResourcesOnHand.Energy = 600;
            a2.ResourcesOnHand.Energy = 600;
            AddStar(serverData, b, "B1", 1000);

            GameSettings.Data.ProductionCapacity = new EnabledValue(true, 1);

            new VictoryCheck(serverData, new Scores(serverData)).Victor();

            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Text.Contains("Race1 have won the game")));
        }

        [Test]
        public void CapitalShipsCondition_CountsShipsWithWeaponRatingOf2000OrMore()
        {
            ServerData serverData = new ServerData();
            EmpireData a = AddEmpire(serverData, 1);
            EmpireData b = AddEmpire(serverData, 2);
            AddStar(serverData, a, "A1", 1000);
            AddStar(serverData, b, "B1", 1000);

            Component beam = new Component();
            beam.Properties.Add("Weapon", new Weapon { Power = 500, Range = 1, Accuracy = 75, Group = WeaponType.standardBeam });
            Hull hull = new Hull { FuelCapacity = 100, ArmorStrength = 50, Modules = new System.Collections.Generic.List<HullModule>() };
            hull.Modules.Add(new HullModule { AllocatedComponent = beam, ComponentCount = 4, ComponentMaximum = 4 }); // (1+3) x 500 x 4 / 4 = 2000
            Component blueprint = new Component { Cost = new Resources(1, 1, 1, 1), Mass = 10 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign battleship = new ShipDesign(1) { Name = "Battleship", Blueprint = blueprint };
            battleship.Update();

            Fleet fleet = new Fleet("Armada", a.Id, 1, new Nova.Common.DataStructures.NovaPoint(0, 0));
            ShipToken token = new ShipToken(battleship, 2);
            fleet.Composition.Add(token.Key, token);
            a.OwnedFleets.Add(fleet);

            GameSettings.Data.CapitalShips = new EnabledValue(true, 2);

            new VictoryCheck(serverData, new Scores(serverData)).Victor();

            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Text.Contains("Race1 have won the game")),
                "two ships of a 2,000-rated design are two Capital ships");
        }

        [Test]
        public void SecondPlaceCondition_TiedLeadersDoNotExceedEachOther()
        {
            // A and B tie for the lead (shared rank 1), C trails (rank 3) - there is no Rank 2.
            // Second place for A is B's score, so neither leader beats "second place" by 100%.
            ServerData serverData = new ServerData();
            EmpireData a = AddEmpire(serverData, 1);
            EmpireData b = AddEmpire(serverData, 2);
            EmpireData c = AddEmpire(serverData, 3);
            foreach (EmpireData e in new[] { a, b, c })
            {
                e.Race.ColonistsPerResource = int.MaxValue; // population points only
            }
            AddStar(serverData, a, "A1", 600000); // 6
            AddStar(serverData, b, "B1", 600000); // 6
            AddStar(serverData, c, "C1", 100000); // 1

            GameSettings.Data.SecondPlaceScore = new EnabledValue(true, 100);

            new VictoryCheck(serverData, new Scores(serverData)).Victor();

            Assert.IsFalse(SomeoneWon(serverData));
        }

        [Test]
        public void SecondPlaceCondition_ClearLeaderBeatsTheBestOtherScore()
        {
            ServerData serverData = new ServerData();
            EmpireData a = AddEmpire(serverData, 1);
            EmpireData b = AddEmpire(serverData, 2);
            EmpireData c = AddEmpire(serverData, 3);
            foreach (EmpireData e in new[] { a, b, c })
            {
                e.Race.ColonistsPerResource = int.MaxValue;
            }
            AddStar(serverData, a, "A1", 600000);
            AddStar(serverData, a, "A2", 600000); // 12
            AddStar(serverData, b, "B1", 500000); // 5
            AddStar(serverData, c, "C1", 500000); // 5 (B and C share rank 2)

            GameSettings.Data.SecondPlaceScore = new EnabledValue(true, 100);

            new VictoryCheck(serverData, new Scores(serverData)).Victor();

            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Text.Contains("Race1 have won the game")), "12 > 5 x 2");
        }

        [Test]
        public void HighestScoreCondition_RequiresASoleLeader()
        {
            ServerData serverData = new ServerData();
            EmpireData a = AddEmpire(serverData, 1);
            EmpireData b = AddEmpire(serverData, 2);
            foreach (EmpireData e in new[] { a, b })
            {
                e.Race.ColonistsPerResource = int.MaxValue;
            }
            AddStar(serverData, a, "A1", 300000);
            AddStar(serverData, b, "B1", 300000);
            serverData.TurnYear = Global.StartingYear + 50;

            GameSettings.Data.HighestScore = new EnabledValue(true, 30);

            new VictoryCheck(serverData, new Scores(serverData)).Victor();

            Assert.IsFalse(SomeoneWon(serverData), "two races tied for the top score: neither is the sole leader");
        }

        [Test]
        public void Elimination_RaceWithNoPlanetsAndNoShips_IsFlaggedOnce_AndEveryOtherRaceIsTold()
        {
            ServerData serverData = new ServerData();
            EmpireData a = AddEmpire(serverData, 1);
            EmpireData b = AddEmpire(serverData, 2);
            EmpireData c = AddEmpire(serverData, 3);
            AddStar(serverData, a, "A1", 100000);
            AddStar(serverData, c, "C1", 100000);
            // b owns nothing at all

            VictoryCheck check = new VictoryCheck(serverData, new Scores(serverData));
            check.Victor();

            Assert.IsTrue(b.Eliminated);
            Assert.IsFalse(a.Eliminated);
            Assert.IsFalse(c.Eliminated);

            List<Message> notices = serverData.AllMessages.FindAll(m => m.Text != null && m.Text.Contains("All traces of the Race2"));
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, notices.ConvertAll(m => m.Audience),
                "message 187 goes to every OTHER race");
            Assert.IsFalse(serverData.AllMessages.Exists(m => m.Text != null && m.Text.Contains("alone remain")),
                "two races are still standing, so no 'you alone are left' message");

            serverData.AllMessages.Clear();
            check.Victor();
            Assert.IsFalse(serverData.AllMessages.Exists(m => m.Text != null && m.Text.Contains("All traces of")),
                "the bit persists, so the notice is not repeated");
        }

        [Test]
        public void Elimination_ARaceWithShipsButNoPlanets_IsNotEliminated()
        {
            ServerData serverData = new ServerData();
            EmpireData a = AddEmpire(serverData, 1);
            EmpireData b = AddEmpire(serverData, 2);
            AddStar(serverData, a, "A1", 100000);

            Hull hull = new Hull { FuelCapacity = 100, ArmorStrength = 20, Modules = new System.Collections.Generic.List<HullModule>() };
            Component blueprint = new Component { Cost = new Resources(1, 1, 1, 1), Mass = 10 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign scout = new ShipDesign(1) { Name = "Scout", Blueprint = blueprint };
            scout.Update();
            Fleet fleet = new Fleet("Scout", b.Id, 1, new Nova.Common.DataStructures.NovaPoint(0, 0));
            ShipToken token = new ShipToken(scout, 1);
            fleet.Composition.Add(token.Key, token);
            b.OwnedFleets.Add(fleet);

            new VictoryCheck(serverData, new Scores(serverData)).Victor();

            Assert.IsFalse(b.Eliminated, "an unarmed ship keeps the race in the game");
        }

        [Test]
        public void Elimination_LeavingOneRace_TellsTheSurvivorItIsAlone()
        {
            ServerData serverData = new ServerData();
            EmpireData a = AddEmpire(serverData, 1);
            EmpireData b = AddEmpire(serverData, 2);
            AddStar(serverData, a, "A1", 100000);

            new VictoryCheck(serverData, new Scores(serverData)).Victor();

            Assert.IsTrue(b.Eliminated);
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("All traces of the Race2")));
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("alone remain")),
                "message 188 to the last race left");
        }

        [Test]
        public void EliminatedFlag_RoundTripsThroughEmpireDataXml()
        {
            EmpireData empire = new EmpireData();
            empire.Id = 4;
            empire.Race = new Race();
            empire.Eliminated = true;

            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            System.Xml.XmlElement root = doc.CreateElement("Root");
            doc.AppendChild(root);
            root.AppendChild(empire.ToXml(doc));

            EmpireData loaded = new EmpireData(root.FirstChild);

            Assert.IsTrue(loaded.Eliminated);
        }
    }
}
