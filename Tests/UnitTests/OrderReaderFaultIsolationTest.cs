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
    using System;
    using System.IO;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server;

    // Regression test for a real bug: behavior-specs-7/save-turn-file-format.md's §5 documents
    // per-record fault isolation for incoming order replay (one bad record costs only that
    // record, never every other command already read or still to come). OrderReader.ReadPlayerTurn
    // previously wrapped its ENTIRE per-node parsing loop in one shared try/catch, so a single
    // malformed order (e.g. a RenameFleet command with an unparseable FleetKey) discarded every
    // other command in the file too, and even left the empire's TurnSubmitted flag unset - as if
    // no orders file existed for that turn at all.
    [TestFixture]
    public class OrderReaderFaultIsolationTest
    {
        private string tempFolder;

        [SetUp]
        public void SetUp()
        {
            tempFolder = Path.Combine(Path.GetTempPath(), "NovaOrderReaderTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Test]
        public void ReadPlayerTurn_OneMalformedCommand_DoesNotDiscardTheOthersInTheSameFile()
        {
            const string raceName = "FaultIsolationRace";
            const int turnYear = 2101;

            string ordersXml =
                "<ROOT>" +
                "<Turn>" + turnYear + "</Turn>" +
                "<Id>1</Id>" +
                "<Orders>" +
                // Valid command #1 - should still be applied.
                "<Order Type=\"RenameFleet\"><FleetKey>A</FleetKey><NewName>First</NewName></Order>" +
                // Malformed command - FleetKey isn't valid hex, throws during construction.
                "<Order Type=\"RenameFleet\"><FleetKey>NOT_HEX</FleetKey><NewName>Bad</NewName></Order>" +
                // Valid command #2, after the bad one - should still be reached and applied.
                "<Order Type=\"RenameFleet\"><FleetKey>B</FleetKey><NewName>Second</NewName></Order>" +
                "</Orders>" +
                "</ROOT>";

            File.WriteAllText(Path.Combine(tempFolder, raceName + Global.OrdersExtension), ordersXml);

            ServerData serverState = new ServerData();
            serverState.GameFolder = tempFolder;
            serverState.TurnYear = turnYear;

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            empire.Race = new Race();
            empire.Race.Name = raceName;
            serverState.AllEmpires.Add(empire.Id, empire);

            new OrderReader(serverState).ReadOrders();

            Assert.IsTrue(serverState.AllCommands.ContainsKey(empire.Id),
                "The two valid commands must still be recorded even though one sibling command was malformed");
            Assert.AreEqual(2, serverState.AllCommands[empire.Id].Count,
                "Both valid commands (before and after the bad one) must survive - only the malformed one should be skipped");
            Assert.IsTrue(empire.TurnSubmitted,
                "A file with some valid orders must still count as a submitted turn, even if one record in it was bad");
        }
    }
}
