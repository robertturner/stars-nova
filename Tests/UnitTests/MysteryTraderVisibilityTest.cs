namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Mystery Trader visibility (behavior-specs-11/turn-generation-engine.md §5a, "Who sees a
    /// Trader"): the visibility pass marks every Trader entry as seen with no test at all, so
    /// distance, scanners, cloak, Tachyon Detectors and race play no part - every player sees every
    /// Mystery Trader every turn. Its whole entry is written into every player's turn file as a
    /// visible special object. This covers the data path end to end: Intel XML save/load, the
    /// IntelWriter's per-empire turn files, and the client-side collection/rule.
    /// </summary>
    [TestFixture]
    public class MysteryTraderVisibilityTest
    {
        private static MysteryTrader MakeTrader(long key, int x, int y, int item)
        {
            MysteryTrader trader = new MysteryTrader
            {
                Position = new NovaPoint(x, y),
                Destination = new NovaPoint(x + 100, y + 50),
                Speed = 9,
                Item = item,
            };
            trader.Key = key;
            trader.Name = "Trader " + key;
            return trader;
        }

        private static XmlDocument SaveAndReload(Intel intel)
        {
            XmlDocument xmldoc = new XmlDocument();
            Global.InitializeXmlDocument(xmldoc);
            xmldoc.ChildNodes.Item(1).AppendChild(intel.ToXml(xmldoc));

            using (MemoryStream stream = new MemoryStream())
            {
                xmldoc.Save(stream);
                stream.Position = 0;
                XmlDocument reloadDoc = new XmlDocument();
                reloadDoc.Load(stream);
                return reloadDoc;
            }
        }

        [Test]
        public void Intel_RoundTripsMysteryTraders_ThroughSaveAndLoad()
        {
            Intel original = new Intel();

            MysteryTrader technology = MakeTrader(1, 10, 20, MysteryTrader.TechnologyItem);
            MysteryTrader ships = MakeTrader(2, 30, 40, MysteryTrader.ShipsItem);
            ships.ServedRaces.Add(3);
            ships.ServedRaces.Add(7);
            original.AllMysteryTraders.Add(technology.Key, technology);
            original.AllMysteryTraders.Add(ships.Key, ships);

            Intel reloaded = new Intel(SaveAndReload(original));

            Assert.AreEqual(2, reloaded.AllMysteryTraders.Count, "Both Traders must survive the round trip.");

            MysteryTrader loadedTechnology = reloaded.AllMysteryTraders[1];
            Assert.AreEqual(10, loadedTechnology.Position.X);
            Assert.AreEqual(20, loadedTechnology.Position.Y);
            Assert.AreEqual(110, loadedTechnology.Destination.X);
            Assert.AreEqual(70, loadedTechnology.Destination.Y);
            Assert.AreEqual(9, loadedTechnology.Speed);
            Assert.AreEqual(MysteryTrader.TechnologyItem, loadedTechnology.Item);
            Assert.IsTrue(loadedTechnology.CarriesTechnology);

            MysteryTrader loadedShips = reloaded.AllMysteryTraders[2];
            Assert.AreEqual(MysteryTrader.ShipsItem, loadedShips.Item);
            CollectionAssert.AreEquivalent(new[] { 3, 7 }, loadedShips.ServedRaces.ToList(),
                "The served mask is part of the entry written to the file.");
        }

        [Test]
        public void VisibleMysteryTradersFor_ReturnsEveryTrader_WithNoTest()
        {
            ServerData server = new ServerData();
            server.AllMysteryTraders.Add(1, MakeTrader(1, 0, 0, MysteryTrader.TechnologyItem));
            server.AllMysteryTraders.Add(2, MakeTrader(2, 380, 380, 5));

            Dictionary<long, MysteryTrader> visible = IntelWriter.VisibleMysteryTradersFor(server);

            CollectionAssert.AreEquivalent(new long[] { 1, 2 }, visible.Keys.ToList(),
                "Every Trader is visible regardless of distance, scanners, cloak or race (there is no observer test).");
        }

        [Test]
        public void WriteIntel_EveryEmpiresTurnFileListsEveryTrader()
        {
            ServerData server = new ServerData { TurnYear = Global.StartingYear };
            EmpireData first = new EmpireData { Id = 1, Race = new Race { Name = "Alpha" } };
            EmpireData second = new EmpireData { Id = 2, Race = new Race { Name = "Beta" } };
            server.AllEmpires[1] = first;
            server.AllEmpires[2] = second;
            server.AllMysteryTraders.Add(1, MakeTrader(1, 5, 6, MysteryTrader.TechnologyItem));
            server.AllMysteryTraders.Add(2, MakeTrader(2, 355, 6, MysteryTrader.ShipsItem));

            string folder = Path.Combine(Path.GetTempPath(), "NovaTraderIntel_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                server.GameFolder = folder;
                new IntelWriter(server, new Scores(server)).WriteIntel();

                foreach (string race in new[] { "Alpha", "Beta" })
                {
                    string file = Path.Combine(folder, race + Global.IntelExtension);
                    Assert.IsTrue(File.Exists(file), race + "'s turn file must exist.");

                    XmlDocument doc = new XmlDocument();
                    doc.Load(file);
                    Intel intel = new Intel(doc);

                    CollectionAssert.AreEquivalent(new long[] { 1, 2 }, intel.AllMysteryTraders.Keys.ToList(),
                        race + "'s turn file must list every Trader, wherever it is.");
                }
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void IntelReader_MirrorsTheTurnFilesTradersIntoTheClient()
        {
            ClientData client = new ClientData { EmpireState = new EmpireData { Id = 1, Race = new Race { Name = "Alpha" } } };
            client.InputTurn = new Intel();
            client.InputTurn.AllMysteryTraders.Add(1, MakeTrader(1, 5, 6, MysteryTrader.TechnologyItem));
            client.InputTurn.AllMysteryTraders.Add(2, MakeTrader(2, 355, 6, MysteryTrader.ShipsItem));

            new IntelReader(client).ProcessIntel();

            CollectionAssert.AreEquivalent(new long[] { 1, 2 }, client.AllMysteryTraders.Keys.ToList(),
                "The client receives every Trader from the turn file.");
        }

        [Test]
        public void VisibleTraders_ListsEveryTrader_ForAnyObserver()
        {
            List<MysteryTrader> traders = new List<MysteryTrader>
            {
                MakeTrader(2, 380, 380, 4),
                MakeTrader(1, 0, 0, MysteryTrader.TechnologyItem),
            };

            List<MysteryTrader> forFirst = MysteryTraderVisibility.VisibleTraders(traders, new EmpireData { Id = 1 });
            List<MysteryTrader> forSecond = MysteryTraderVisibility.VisibleTraders(traders, new EmpireData { Id = 2 });

            CollectionAssert.AreEqual(new long[] { 1, 2 }, forFirst.Select(t => t.Key).ToList(), "All Traders, ordered by key.");
            CollectionAssert.AreEqual(forFirst.Select(t => t.Key).ToList(), forSecond.Select(t => t.Key).ToList(),
                "The observer race makes no difference.");
        }

        [Test]
        public void VisibleTraders_ClientOverload_ListsTheClientsWholeCollection()
        {
            ClientData client = new ClientData { EmpireState = new EmpireData { Id = 1 } };
            client.AllMysteryTraders.Add(2, MakeTrader(2, 380, 380, 4));
            client.AllMysteryTraders.Add(1, MakeTrader(1, 0, 0, MysteryTrader.TechnologyItem));

            CollectionAssert.AreEqual(new long[] { 1, 2 },
                MysteryTraderVisibility.VisibleTraders(client).Select(t => t.Key).ToList());
        }
    }
}
