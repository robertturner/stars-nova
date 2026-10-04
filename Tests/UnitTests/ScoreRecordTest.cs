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
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// The per-race score record and Score formula of docs/behavior-specs-9/victory-conditions.md
    /// section 2 and client-ui-dialog-catalog.md's "Score display" entry:
    /// Score = sum over planets of min(6, ceil(pop / 100,000)) + floor(Resources / 30)
    ///       + 3 x dock-capable starbases + tech curve (L / 2L-3 / 3(L-3) / 4L-18 per field)
    ///       + floor(min(unarmed, P) / 2) + 2 x min(escort, P) + [C > 0] floor(8PC / (P + C));
    /// Rank = 1 + number of races with a strictly higher score. Ship classes come from the design
    /// weapon value of ship-design-and-components.md section 9.
    /// </summary>
    [TestFixture]
    public class ScoreRecordTest
    {
        private ServerData serverData;
        private uint nextFleetId;
        private long nextDesignKey;

        [SetUp]
        public void Init()
        {
            serverData = new ServerData();
            nextFleetId = 1;
            nextDesignKey = 1;
        }

        private EmpireData AddEmpire(ushort id)
        {
            EmpireData empire = new EmpireData();
            empire.Id = id;
            empire.Race = new Race();
            empire.Race.ColonistsPerResource = 1000;
            empire.Race.PluralName = "Race" + id;
            serverData.AllEmpires.Add(id, empire);
            return empire;
        }

        private Star AddStar(EmpireData owner, string name, int colonists)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = owner.Id;
            star.Colonists = colonists;
            star.Factories = 0;
            star.ThisRace = owner.Race;
            serverData.AllStars.Add(star.Key, star);
            return star;
        }

        private ShipDesign MakeDesign(bool starbase, int dockCapacity, params (Component component, int count)[] slots)
        {
            Component blueprint = new Component { Cost = new Resources(1, 1, 1, 1), Mass = 10 };
            Hull hull = new Hull
            {
                FuelCapacity = starbase ? 0 : 100,
                DockCapacity = dockCapacity,
                ArmorStrength = 50,
                Modules = new List<HullModule>(),
            };
            foreach ((Component component, int count) in slots)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = component, ComponentCount = count, ComponentMaximum = count });
            }
            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(nextDesignKey++) { Name = "Design" + nextDesignKey, Blueprint = blueprint };
            design.Update();
            return design;
        }

        private static Component WeaponComponent(WeaponType group, int power, int range)
        {
            Component component = new Component();
            component.Properties.Add("Weapon", new Weapon { Power = power, Range = range, Accuracy = 75, Group = group });
            return component;
        }

        private static Component BombComponent(double popKillPercent, int installations)
        {
            Component component = new Component();
            component.Properties.Add("Bomb", new Bomb(installations, popKillPercent, 0, false));
            return component;
        }

        private static Component CapacitorComponent(double percent)
        {
            Component component = new Component();
            component.Properties.Add("Capacitor", new CapacitorProperty(percent));
            return component;
        }

        private Fleet AddFleet(EmpireData owner, ShipDesign design, int quantity)
        {
            Fleet fleet = new Fleet("fleet" + nextFleetId, owner.Id, nextFleetId++, new NovaPoint(0, 0));
            ShipToken token = new ShipToken(design, quantity);
            fleet.Composition.Add(token.Key, token);
            owner.OwnedFleets.Add(fleet);
            return fleet;
        }

        private ScoreRecord ScoreOf(EmpireData empire)
        {
            return new Scores(serverData).GetScores().Single(s => s.EmpireId == empire.Id);
        }

        // ------------------------------------------------------------------------------------
        // Planet terms
        // ------------------------------------------------------------------------------------

        [Test]
        public void PopulationPoints_AreRoundedUpPer100000Colonists_AndCappedAtSixPerPlanet()
        {
            Assert.AreEqual(0, Scores.PopulationPoints(0));
            Assert.AreEqual(1, Scores.PopulationPoints(1));
            Assert.AreEqual(1, Scores.PopulationPoints(100000));
            Assert.AreEqual(2, Scores.PopulationPoints(100001));
            Assert.AreEqual(6, Scores.PopulationPoints(600000));
            Assert.AreEqual(6, Scores.PopulationPoints(2000000), "capped at 6 per planet");
        }

        [Test]
        public void Score_PopulationTerm_IsCeilingAndCapPerPlanet()
        {
            EmpireData empire = AddEmpire(1);
            empire.Race.ColonistsPerResource = int.MaxValue; // keep the Resources term at 0
            AddStar(empire, "A", 250000);   // ceil(2.5) = 3 (the old floor gave 2)
            AddStar(empire, "B", 1500000);  // 15 -> capped at 6 (the old code gave 15)

            ScoreRecord score = ScoreOf(empire);

            Assert.AreEqual(2, score.Planets);
            // A populated planet makes at least 1 resource (race-traits.md section 5a step 6), so
            // the Resources figure is 2 here; 2 / 30 still adds nothing to the score.
            Assert.AreEqual(2, score.Resources);
            Assert.AreEqual(9, score.Score, "3 + 6 population points, nothing else");
        }

        [Test]
        public void Resources_IsSummedPlanetOutput_NotLeftoverResourcesOnHand()
        {
            EmpireData empire = AddEmpire(1);
            Star a = AddStar(empire, "A", 60000);   // 60 resources/year
            Star b = AddStar(empire, "B", 45000);   // 45 resources/year
            a.ResourcesOnHand.Energy = 0;           // all spent by production
            b.ResourcesOnHand.Energy = 7;

            ScoreRecord score = ScoreOf(empire);

            Assert.AreEqual(a.GetResourceRate() + b.GetResourceRate(), score.Resources);
            Assert.AreEqual(105, score.Resources);
            // population 1 + 1, resources floor(105 / 30) = 3
            Assert.AreEqual(5, score.Score);
        }

        [Test]
        public void Starbases_OnlyHullsWithDockCapacityCount_ThreePointsEach()
        {
            EmpireData empire = AddEmpire(1);
            empire.Race.ColonistsPerResource = int.MaxValue;
            Star withFort = AddStar(empire, "Fort", 0);
            Star withDock = AddStar(empire, "Dock", 0);

            ShipDesign fort = MakeDesign(true, 0);    // Orbital Fort: no dock
            ShipDesign dock = MakeDesign(true, 200);  // Space Dock

            withFort.Starbase = AddFleet(empire, fort, 1);
            withDock.Starbase = AddFleet(empire, dock, 1);

            ScoreRecord score = ScoreOf(empire);

            Assert.AreEqual(1, score.Starbases, "an Orbital Fort does not count");
            Assert.AreEqual(3, score.Score);
            Assert.AreEqual(0, score.UnarmedShips + score.EscortShips + score.CapitalShips,
                "starbases are scored through their planet, not as ships");
        }

        // ------------------------------------------------------------------------------------
        // Tech term
        // ------------------------------------------------------------------------------------

        [Test]
        public void TechLevelPoints_FollowTheSteppedCurve()
        {
            int[] expected = { 0, 1, 2, 3, 5, 7, 9, 12, 15, 18, 22, 26, 30 };
            for (int level = 0; level < expected.Length; level++)
            {
                Assert.AreEqual(expected[level], Scores.TechLevelPoints(level), "level " + level);
            }
            Assert.AreEqual(4 * 26 - 18, Scores.TechLevelPoints(26));
        }

        [Test]
        public void Score_TechTerm_ReadsResearchLevels_AndTechRowIsThePlainSum()
        {
            EmpireData empire = AddEmpire(1);
            AddStar(empire, "Home", 0); // owned so the race is not eliminated
            empire.ResearchLevels[TechLevel.ResearchField.Biotechnology] = 0;
            empire.ResearchLevels[TechLevel.ResearchField.Electronics] = 3;
            empire.ResearchLevels[TechLevel.ResearchField.Energy] = 4;
            empire.ResearchLevels[TechLevel.ResearchField.Propulsion] = 6;
            empire.ResearchLevels[TechLevel.ResearchField.Weapons] = 7;
            empire.ResearchLevels[TechLevel.ResearchField.Construction] = 10;

            ScoreRecord score = ScoreOf(empire);

            Assert.AreEqual(30, score.TechLevel, "Tech Levels row = plain sum of the six levels");
            Assert.AreEqual(0 + 3 + 5 + 9 + 12 + 22, score.Score);
        }

        [Test]
        public void Score_TechTerm_IsSkippedOnceTheRaceIsEliminated()
        {
            EmpireData empire = AddEmpire(1);
            empire.ResearchLevels[TechLevel.ResearchField.Weapons] = 10;
            empire.Eliminated = true;

            Assert.AreEqual(0, ScoreOf(empire).Score);
        }

        // ------------------------------------------------------------------------------------
        // Ship classes and their terms
        // ------------------------------------------------------------------------------------

        [Test]
        public void DesignWeaponRating_FollowsTheSection9Formula()
        {
            // beam: (1 + 3) x 10 x 2 / 4 = 20
            ShipDesign beam = MakeDesign(false, 0, (WeaponComponent(WeaponType.standardBeam, 10, 1), 2));
            Assert.AreEqual(20, Scores.DesignWeaponRating(beam));

            // torpedo: (4 - 2) x 5 x 3 / 2 = 15
            ShipDesign torpedo = MakeDesign(false, 0, (WeaponComponent(WeaponType.torpedo, 5, 4), 3));
            Assert.AreEqual(15, Scores.DesignWeaponRating(torpedo));

            // bomb: (6 tenths-of-a-percent + 2 installations) x 4 x 2 = 64
            ShipDesign bomber = MakeDesign(false, 0, (BombComponent(0.6, 2), 4));
            Assert.AreEqual(64, Scores.DesignWeaponRating(bomber));

            // two 10% capacitors compound onto the beam sum only: 20 x 1.21 = 24, plus torpedo 15
            ShipDesign mixed = MakeDesign(false, 0,
                (WeaponComponent(WeaponType.standardBeam, 10, 1), 2),
                (WeaponComponent(WeaponType.torpedo, 5, 4), 3),
                (CapacitorComponent(10), 2));
            Assert.AreEqual(24 + 15, Scores.DesignWeaponRating(mixed));

            ShipDesign unarmed = MakeDesign(false, 0);
            Assert.AreEqual(0, Scores.DesignWeaponRating(unarmed));
        }

        [Test]
        public void ShipClasses_ByWeaponRating_CountShipsNotTokens()
        {
            EmpireData empire = AddEmpire(1);
            empire.Race.ColonistsPerResource = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                AddStar(empire, "P" + i, 0); // P = 4
            }

            ShipDesign scout = MakeDesign(false, 0);                                                      // rating 0
            ShipDesign escort = MakeDesign(false, 0, (WeaponComponent(WeaponType.standardBeam, 10, 1), 1)); // rating 10
            ShipDesign escortTop = MakeDesign(false, 0, (WeaponComponent(WeaponType.standardBeam, 1999, 1), 1)); // (4 x 1999) / 4 = 1999
            ShipDesign capital = MakeDesign(false, 0, (WeaponComponent(WeaponType.standardBeam, 500, 1), 4)); // (4 x 500 x 4) / 4 = 2000

            Assert.AreEqual(1999, Scores.DesignWeaponRating(escortTop));
            Assert.AreEqual(2000, Scores.DesignWeaponRating(capital));

            AddFleet(empire, scout, 7);
            AddFleet(empire, escort, 2);
            AddFleet(empire, escortTop, 1);
            AddFleet(empire, capital, 4);

            ScoreRecord score = ScoreOf(empire);

            Assert.AreEqual(7, score.UnarmedShips);
            Assert.AreEqual(3, score.EscortShips);
            Assert.AreEqual(4, score.CapitalShips);

            // unarmed: floor(min(7, 4) / 2) = 2; escort: 2 x min(3, 4) = 6;
            // capital: floor(8 x 4 x 4 / (4 + 4)) = 16
            Assert.AreEqual(2 + 6 + 16, score.Score);
        }

        [Test]
        public void ShipTerms_AreCappedByPlanetCount()
        {
            EmpireData empire = AddEmpire(1);
            empire.Race.ColonistsPerResource = int.MaxValue;
            AddStar(empire, "Only", 0); // P = 1

            ShipDesign scout = MakeDesign(false, 0);
            ShipDesign escort = MakeDesign(false, 0, (WeaponComponent(WeaponType.standardBeam, 10, 1), 1));
            AddFleet(empire, scout, 10);
            AddFleet(empire, escort, 10);

            // floor(min(10, 1) / 2) = 0; 2 x min(10, 1) = 2
            Assert.AreEqual(2, ScoreOf(empire).Score);
        }

        // ------------------------------------------------------------------------------------
        // Ranks
        // ------------------------------------------------------------------------------------

        [Test]
        public void Ranks_AreShared_OnePlusTheNumberOfStrictlyHigherScores()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            EmpireData c = AddEmpire(3);
            foreach (EmpireData e in new[] { a, b, c })
            {
                e.Race.ColonistsPerResource = int.MaxValue;
            }
            AddStar(a, "A", 300000); // 3
            AddStar(b, "B", 300000); // 3
            AddStar(c, "C", 100000); // 1

            List<ScoreRecord> scores = new Scores(serverData).GetScores();

            Assert.AreEqual(1, scores.Single(s => s.EmpireId == 1).Rank);
            Assert.AreEqual(1, scores.Single(s => s.EmpireId == 2).Rank, "tied races share a rank");
            Assert.AreEqual(3, scores.Single(s => s.EmpireId == 3).Rank);
        }

        [Test]
        public void Rank_IsOneForALoneRace()
        {
            EmpireData a = AddEmpire(1);
            Assert.AreEqual(1, ScoreOf(a).Rank);
        }
    }
}
