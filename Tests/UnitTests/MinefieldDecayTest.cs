namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // behavior-specs-8/turn-generation-engine.md section 3 (segment-24 sweep): a minefield loses
    // 2 + 4 x S percent of its mines every year (S = stars inside its circle; the 4 becomes 1 for
    // Space Demolition), capped at 50%, but never fewer mines than the percentage figure itself and
    // never fewer than 10, and a field that is not above the loss is deleted. There is no regrowth.
    [TestFixture]
    public class MinefieldDecayTest
    {
        private ServerData serverState;
        private EmpireData empire;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            empire = new SimpleEmpireData { Id = 1, Race = new Race() };
            serverState.AllEmpires.Add(empire.Id, empire);
        }

        private Minefield AddField(int mines, int x, int y)
        {
            Minefield field = new Minefield { NumberOfMines = mines, Owner = empire.Id };
            field.Key = empire.GetNextMinefieldKey();
            field.Position = new NovaPoint(x, y);
            field.Name = "Field " + serverState.AllMinefields.Count;
            serverState.AllMinefields[field.Key] = field;
            return field;
        }

        private void AddStar(string name, int x, int y)
        {
            Star star = new Star { Name = name };
            star.Position = new NovaPoint(x, y);
            serverState.AllStars.Add(star.Key, star);
        }

        [Test]
        public void LossPercent_IsTwoPlusFourPerStar_CappedAtFifty()
        {
            Assert.AreEqual(2, MinefieldDecayStep.LossPercent(0, false));
            Assert.AreEqual(14, MinefieldDecayStep.LossPercent(3, false));
            Assert.AreEqual(50, MinefieldDecayStep.LossPercent(12, false), "2 + 48 = 50");
            Assert.AreEqual(50, MinefieldDecayStep.LossPercent(40, false), "capped");
        }

        [Test]
        public void LossPercent_ForSpaceDemolition_UsesAMultiplierOfOne()
        {
            Assert.AreEqual(5, MinefieldDecayStep.LossPercent(3, true));
        }

        [Test]
        public void MinesLost_IsThePercentageOfTheField_ButNeverBelowThePercentageFigureOrTen()
        {
            Assert.AreEqual(20, MinefieldDecayStep.MinesLost(1000, 2), "2% of 1000");
            Assert.AreEqual(10, MinefieldDecayStep.MinesLost(300, 2), "2% of 300 is 6, floored at 10");
            Assert.AreEqual(50, MinefieldDecayStep.MinesLost(60, 50), "50% of 60 is 30, but never fewer than the percentage figure itself (50)");
        }

        [Test]
        public void Step_DecaysAFieldByTwoPercent_WhenNoStarLiesInside()
        {
            Minefield field = AddField(1000, 0, 0);
            AddStar("Far", 500, 500);

            new MinefieldDecayStep().Process(serverState);

            Assert.AreEqual(980, field.NumberOfMines);
        }

        [Test]
        public void Step_CountsOnlyStarsInsideTheFieldsCircle()
        {
            // 1000 mines: radius ~31.6 ly, so squared distance must be <= 1000.
            Minefield field = AddField(1000, 0, 0);
            AddStar("Inside", 20, 20);    // 800
            AddStar("Edge", 10, 30);      // 1000 exactly
            AddStar("Outside", 25, 25);   // 1250 - not inside

            new MinefieldDecayStep().Process(serverState);

            // S = 2 -> 2 + 8 = 10% of 1000 = 100 mines lost.
            Assert.AreEqual(900, field.NumberOfMines);
        }

        [Test]
        public void Step_SpaceDemolitionOwnersFieldsDecayMoreSlowlyAroundStars()
        {
            empire.Race.Traits.SetPrimary("SD");
            Minefield field = AddField(1000, 0, 0);
            AddStar("Inside", 20, 20);
            AddStar("Edge", 10, 30);

            new MinefieldDecayStep().Process(serverState);

            // S = 2 -> 2 + 2 = 4% of 1000 = 40 lost.
            Assert.AreEqual(960, field.NumberOfMines);
        }

        [Test]
        public void Step_DeletesAFieldWhoseStrengthIsNotAboveTheLoss()
        {
            Minefield tiny = AddField(10, 0, 0);

            new MinefieldDecayStep().Process(serverState);

            Assert.IsFalse(serverState.AllMinefields.ContainsKey(tiny.Key), "10 mines, loss 10: gone");
        }

        [Test]
        public void Step_DecaysEveryFieldOncePerYear_NotOncePerMovingFleet()
        {
            Minefield one = AddField(1000, 0, 0);
            Minefield two = AddField(500, 900, 900);

            new MinefieldDecayStep().Process(serverState);

            Assert.AreEqual(980, one.NumberOfMines);
            Assert.AreEqual(490, two.NumberOfMines);
        }
    }
}
