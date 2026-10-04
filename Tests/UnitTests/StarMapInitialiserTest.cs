using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;
    using Nova.Common;
    using Nova.Server;
    using Nova.Server.NewGame;


    class TestRace : Nova.Common.Race
    {
        public int TestAdvantagePoints = 0;

        public override int GetAdvantagePoints()
        {
            return TestAdvantagePoints;
        }
    }


    /// <summary>
    /// The authoritative leftover-points table - docs/behavior-specs-10/new-game-setup.md
    /// section 3, "Leftover advantage points at game start": p = min(50, leftover), computer
    /// players always 50; Surface minerals 10p/4 each plus another 10p/4 and the remainder to the
    /// smallest stock (ties Germanium, then Boranium); Concentrations e = p/2 (1 when p is 1-2)
    /// to the lowest (ties Ironium, Boranium, Germanium) then (e+1)/2 to all; Mines p/2;
    /// Factories p/5; Defences (p+5)/10; Alternate Reality then zeroed. Integer arithmetic.
    /// The old tests here asserted the superseded community rules (p/3 to one concentration,
    /// p/10 defences, a float inverse-weighted surface split).
    /// </summary>
    [TestFixture]
    class HomeStarLeftoverpointsAdjusterTest
    {
        private TestRace testRace;
        private Star star;

        [SetUp]
        public void TestSetUp()
        {
            testRace = new TestRace();
            star = new Star();
        }

        [Test]
        public void LeftoverAdvantagePointMinimumTest()
        {
            testRace.TestAdvantagePoints = -1;
            Race race = testRace;
            Assert.AreEqual(-1, race.GetAdvantagePoints());
            Assert.AreEqual(0, race.GetLeftoverAdvantagePoints());
        }

        [Test]
        public void LeftoverAdvantagePointMaximumTest()
        {
            testRace.TestAdvantagePoints = 51;
            Race race = testRace;
            Assert.AreEqual(51, race.GetAdvantagePoints());
            Assert.AreEqual(50, race.GetLeftoverAdvantagePoints());
        }

        [Test]
        public void LeftoverAdvantagePointExactTest()
        {
            testRace.TestAdvantagePoints = 1;
            Race race = testRace;
            Assert.AreEqual(1, race.GetAdvantagePoints());
            Assert.AreEqual(1, race.GetLeftoverAdvantagePoints());
        }

        [Test]
        public void NoMineralConcentration()
        {
            testRace.TestAdvantagePoints = 0;
            testRace.LeftoverPointTarget = "Mineral concentration";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.MineralConcentration.Boranium + star.MineralConcentration.Germanium + star.MineralConcentration.Ironium);
        }

        [Test]
        public void MineralConcentration_OneOrTwoPoints_GiveEOfOne()
        {
            // e = 1 when p is 1 or 2: the lowest (Ironium, first on a tie) +1, then all +(1+1)/2 = +1.
            foreach (int p in new[] { 1, 2 })
            {
                star = new Star();
                testRace.TestAdvantagePoints = p;
                testRace.LeftoverPointTarget = "Mineral concentration";
                HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
                Assert.AreEqual(2, star.MineralConcentration.Ironium, "p = " + p);
                Assert.AreEqual(1, star.MineralConcentration.Boranium, "p = " + p);
                Assert.AreEqual(1, star.MineralConcentration.Germanium, "p = " + p);
            }
        }

        [Test]
        public void MineralConcentration_SevenPoints_LowestPlusFiveOthersPlusTwo()
        {
            star.MineralConcentration.Ironium = 60;
            star.MineralConcentration.Boranium = 40;
            star.MineralConcentration.Germanium = 50;
            testRace.TestAdvantagePoints = 7;
            testRace.LeftoverPointTarget = "Mineral concentration";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(62, star.MineralConcentration.Ironium);
            Assert.AreEqual(45, star.MineralConcentration.Boranium);
            Assert.AreEqual(52, star.MineralConcentration.Germanium);
        }

        [Test]
        public void MineralConcentration_FiftyPoints_LowestPlus38OthersPlus13_AndTheMaximumIs157()
        {
            star.MineralConcentration.Ironium = 119;
            star.MineralConcentration.Boranium = 119;
            star.MineralConcentration.Germanium = 119;
            testRace.TestAdvantagePoints = 50;
            testRace.LeftoverPointTarget = "Mineral concentrations"; // the WinForms designer's plural label
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(157, star.MineralConcentration.Ironium, "Ironium wins the three-way tie");
            Assert.AreEqual(132, star.MineralConcentration.Boranium);
            Assert.AreEqual(132, star.MineralConcentration.Germanium);
        }

        [Test]
        public void MineralConcentration_TieBetweenBoraniumAndGermanium_GoesToBoranium()
        {
            star.MineralConcentration.Ironium = 80;
            star.MineralConcentration.Boranium = 30;
            star.MineralConcentration.Germanium = 30;
            testRace.TestAdvantagePoints = 10;
            testRace.LeftoverPointTarget = "Mineral concentration";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(83, star.MineralConcentration.Ironium);
            Assert.AreEqual(38, star.MineralConcentration.Boranium);
            Assert.AreEqual(33, star.MineralConcentration.Germanium);
        }

        [Test]
        public void NoMines()
        {
            testRace.TestAdvantagePoints = 0;
            testRace.LeftoverPointTarget = "Mines";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.Mines);
            testRace.TestAdvantagePoints = 1;
            testRace.LeftoverPointTarget = "Mines";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.Mines);
        }

        [Test]
        public void SomeMines()
        {
            testRace.TestAdvantagePoints = 10;
            testRace.LeftoverPointTarget = "Mines";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(5, star.Mines);
        }

        [Test]
        public void NoFactories()
        {
            testRace.TestAdvantagePoints = 0;
            testRace.LeftoverPointTarget = "Factories";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.Factories);
            testRace.TestAdvantagePoints = 4;
            testRace.LeftoverPointTarget = "Factories";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.Factories);
        }

        [Test]
        public void SomeFactories()
        {
            testRace.TestAdvantagePoints = 11;
            testRace.LeftoverPointTarget = "Factories";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(2, star.Factories);
        }

        [Test]
        public void NoDefenses()
        {
            testRace.TestAdvantagePoints = 0;
            testRace.LeftoverPointTarget = "Defenses";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.Defenses);
            testRace.TestAdvantagePoints = 4;
            testRace.LeftoverPointTarget = "Defenses";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.Defenses, "(4 + 5) / 10 = 0");
        }

        [TestCase(5, 1)]
        [TestCase(29, 3)]
        [TestCase(50, 5)]
        public void SomeDefenses_AreRoundedHalfUp(int points, int expected)
        {
            testRace.TestAdvantagePoints = points;
            testRace.LeftoverPointTarget = "Defenses";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(expected, star.Defenses);
        }

        [Test]
        public void NoSurfaceMinerals()
        {
            testRace.TestAdvantagePoints = 0;
            testRace.LeftoverPointTarget = "Surface minerals";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(0, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(0, star.ResourcesOnHand.Ironium);
        }

        [Test]
        public void SomeSurfaceMinerals_ThreeWayTie_GoesToGermanium()
        {
            // p = 3: 30 kT; 30 / 4 = 7 each, the smallest (Germanium on a tie) another 7 + 2.
            star.ResourcesOnHand.Boranium = 10;
            star.ResourcesOnHand.Germanium = 10;
            star.ResourcesOnHand.Ironium = 10;
            testRace.TestAdvantagePoints = 3;
            testRace.LeftoverPointTarget = "Surface minerals";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(17, star.ResourcesOnHand.Boranium, "Boranium");
            Assert.AreEqual(26, star.ResourcesOnHand.Germanium, "Germanium");
            Assert.AreEqual(17, star.ResourcesOnHand.Ironium, "Ironium");
        }

        [Test]
        public void SurfaceMinerals_SevenPoints_ThirtySixToThePoorestSeventeenToTheOthers()
        {
            star.ResourcesOnHand.Ironium = 300;
            star.ResourcesOnHand.Boranium = 200;
            star.ResourcesOnHand.Germanium = 400;
            testRace.TestAdvantagePoints = 7;
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace); // null target = Surface minerals
            Assert.AreEqual(317, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(236, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(417, star.ResourcesOnHand.Germanium);
        }

        [Test]
        public void SurfaceMinerals_BoraniumIronumTie_GoesToBoranium()
        {
            star.ResourcesOnHand.Ironium = 100;
            star.ResourcesOnHand.Boranium = 100;
            star.ResourcesOnHand.Germanium = 400;
            testRace.TestAdvantagePoints = 50;
            testRace.LeftoverPointTarget = "Surface minerals";
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(225, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(350, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(525, star.ResourcesOnHand.Germanium);
        }

        [Test]
        public void ComputerPlayer_AlwaysUsesFiftyPoints()
        {
            testRace.TestAdvantagePoints = 0;
            testRace.LeftoverPointTarget = "Mines";
            star.Mines = 10;
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace, isComputerPlayer: true);
            Assert.AreEqual(35, star.Mines, "10 + 50 / 2");
        }

        [TestCase("Mines")]
        [TestCase("Factories")]
        [TestCase("Defenses")]
        [TestCase("Surface minerals")]
        public void AlternateReality_InstallationsAreZeroedAfterTheBonus(string target)
        {
            testRace.Traits.SetPrimary("AR");
            testRace.TestAdvantagePoints = 50;
            testRace.LeftoverPointTarget = target;
            star.Mines = 10;
            star.Factories = 10;
            star.Defenses = 10;
            HomeStarLeftoverpointsAdjuster.Adjust(star, testRace);
            Assert.AreEqual(0, star.Mines);
            Assert.AreEqual(0, star.Factories);
            Assert.AreEqual(0, star.Defenses);
        }
    }
}
