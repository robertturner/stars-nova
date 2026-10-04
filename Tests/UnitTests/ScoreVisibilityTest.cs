namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;

    /// <summary>
    /// Score visibility (behavior-specs-11/save-turn-file-format.md §3, "Score records"): a
    /// player's turn file carries race k's record only when k is the viewer's own race, k is
    /// eliminated, the game is over, or "Public Player Scores" is on and the new turn counter
    /// exceeds 19 (the file for 2420 onward).
    /// </summary>
    [TestFixture]
    public class ScoreVisibilityTest
    {
        private bool savedPublicScores;

        [SetUp]
        public void SaveSettings()
        {
            savedPublicScores = GameSettings.Data.PublicPlayerScores;
            GameSettings.Data.PublicPlayerScores = false;
        }

        [TearDown]
        public void RestoreSettings()
        {
            GameSettings.Data.PublicPlayerScores = savedPublicScores;
        }

        private static (ServerData server, EmpireData viewer, List<ScoreRecord> scores) MakeGame()
        {
            ServerData server = new ServerData { TurnYear = Global.StartingYear + 1 };
            EmpireData viewer = new EmpireData { Id = 1, Race = new Race() };
            EmpireData other = new EmpireData { Id = 2, Race = new Race() };
            EmpireData third = new EmpireData { Id = 3, Race = new Race() };
            server.AllEmpires[1] = viewer;
            server.AllEmpires[2] = other;
            server.AllEmpires[3] = third;

            List<ScoreRecord> scores = new List<ScoreRecord>
            {
                new ScoreRecord { EmpireId = 1, Score = 100 },
                new ScoreRecord { EmpireId = 2, Score = 50 },
                new ScoreRecord { EmpireId = 3, Score = 10 },
            };

            return (server, viewer, scores);
        }

        private static IntelWriter Writer(ServerData server) => new IntelWriter(server, new Scores(server));

        [Test]
        public void OwnRecord_IsAlwaysVisible()
        {
            var (server, viewer, scores) = MakeGame();

            List<ScoreRecord> visible = Writer(server).VisibleScores(viewer, scores);

            CollectionAssert.Contains(visible.Select(r => r.EmpireId).ToList(), viewer.Id);
            Assert.AreEqual(1, visible.Count, "with no option only the viewer's own record is sent");
        }

        [Test]
        public void EliminatedRace_IsVisibleToEveryone()
        {
            var (server, viewer, scores) = MakeGame();
            server.AllEmpires[2].Eliminated = true;

            List<ScoreRecord> visible = Writer(server).VisibleScores(viewer, scores);

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, visible.Select(r => r.EmpireId).ToList());
        }

        [Test]
        public void GameOver_RevealsEveryRecord()
        {
            var (server, viewer, scores) = MakeGame();
            server.AllEmpires[2].Winner = true;

            List<ScoreRecord> visible = Writer(server).VisibleScores(viewer, scores);

            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, visible.Select(r => r.EmpireId).ToList());
        }

        [Test]
        public void PublicScores_RevealEveryRecordOnlyAfterCounter19()
        {
            var (server, viewer, scores) = MakeGame();
            GameSettings.Data.PublicPlayerScores = true;

            server.TurnYear = Global.StartingYear + 19; // counter 19: still private
            Assert.AreEqual(1, Writer(server).VisibleScores(viewer, scores).Count);

            server.TurnYear = Global.StartingYear + 20; // 2420 onward: public
            Assert.AreEqual(3, Writer(server).VisibleScores(viewer, scores).Count);
        }

        [Test]
        public void PublicScoresOff_KeepsOtherRecordsPrivateEvenLate()
        {
            var (server, viewer, scores) = MakeGame();
            GameSettings.Data.PublicPlayerScores = false;
            server.TurnYear = Global.StartingYear + 50;

            Assert.AreEqual(1, Writer(server).VisibleScores(viewer, scores).Count);
        }
    }
}
