namespace Nova.Tests.UnitTests
{
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;

    /// <summary>
    /// behavior-specs-10/save-turn-file-format.md section 3 (coverage row 8): the score history
    /// keeps a per-turn score record for the most recent 100 turns only.
    /// </summary>
    [TestFixture]
    public class ScoreHistoryTest
    {
        private static ScoreRecord Score(int empire, int score)
        {
            return new ScoreRecord { EmpireId = empire, Score = score, Rank = 1 };
        }

        [Test]
        public void KeepsTheMostRecent100Turns()
        {
            ScoreHistory history = new ScoreHistory();
            for (int year = 2400; year < 2505; year++)
            {
                history.Record(year, new[] { Score(1, year - 2400) });
            }

            Assert.AreEqual(ScoreHistory.RetainedTurns, history.Count);
            Assert.AreEqual(2405, history.Years.First());
            Assert.AreEqual(2504, history.Years.Last());
            Assert.AreEqual(0, history.For(2404).Count, "Dropped");
            Assert.AreEqual(104, history.For(2504)[0].Score);
        }

        [Test]
        public void RecordingAYearAgain_ReplacesIt()
        {
            ScoreHistory history = new ScoreHistory();
            history.Record(2401, new[] { Score(1, 10) });
            history.Record(2401, new[] { Score(1, 20), Score(2, 5) });

            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(2, history.For(2401).Count);
            Assert.AreEqual(20, history.For(2401)[0].Score);
        }

        [Test]
        public void SurvivesAnXmlRoundTrip()
        {
            ScoreHistory history = new ScoreHistory();
            history.Record(2401, new[] { Score(1, 10), Score(2, 7) });
            history.Record(2402, new[] { Score(1, 12) });

            ScoreHistory copy = new ScoreHistory(history.ToXml(new XmlDocument()));

            CollectionAssert.AreEqual(new[] { 2401, 2402 }, copy.Years);
            Assert.AreEqual(7, copy.For(2401)[1].Score);
            Assert.AreEqual(2, copy.For(2401)[1].EmpireId);
        }

        [Test]
        public void IsSavedWithTheServerState()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nova-score-history-" + System.Guid.NewGuid() + ".xml");
            try
            {
                ServerData server = new ServerData();
                server.StatePathName = path;
                server.ScoreHistory.Record(2401, new[] { Score(1, 10) });
                server.ToXml();

                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                ServerData loaded = new ServerData(doc);

                Assert.AreEqual(10, loaded.ScoreHistory.For(2401)[0].Score);
            }
            finally
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
        }
    }
}
