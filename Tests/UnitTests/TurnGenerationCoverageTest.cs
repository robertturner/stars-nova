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
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    /// <summary>
    /// Turn-generation step 6 (behavior-specs-10/turn-generation-engine.md §1a,
    /// save-turn-file-format.md §10.6) and the starbase rule of the colonist unload outcome table
    /// (fleet-movement-scanning-cargo.md §4).
    /// </summary>
    [TestFixture]
    public class TurnGenerationCoverageTest
    {
        private const int HostTurn = 2110;

        private string tempFolder;
        private List<string> reportedErrors;

        private sealed class OrdersOnlyTurnGenerator : TurnGenerator
        {
            public OrdersOnlyTurnGenerator(ServerData serverState) : base(serverState)
            {
            }

            /// <summary>Step 6 alone: load every order file, then replay the orders.</summary>
            public void LoadAndApplyOrders()
            {
                ReadOrders();
                ParseCommands();
            }
        }

        [SetUp]
        public void SetUp()
        {
            tempFolder = Path.Combine(Path.GetTempPath(), "NovaTurnGenCoverage_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);
            reportedErrors = new List<string>();
            PlatformHooks.ShowError = message => reportedErrors.Add(message);
        }

        [TearDown]
        public void TearDown()
        {
            PlatformHooks.ShowError = _ => { };
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }

        // ------------------------------------------------------------------ helpers

        private ServerData NewServer()
        {
            return new ServerData { GameFolder = tempFolder, TurnYear = HostTurn };
        }

        private static EmpireData AddEmpire(ServerData server, ushort id, string raceName)
        {
            EmpireData empire = new EmpireData { Id = id, Race = new Race { Name = raceName } };
            server.AllEmpires.Add(empire.Id, empire);
            return empire;
        }

        private static Fleet AddFleet(EmpireData empire, uint id, string name)
        {
            Fleet fleet = new Fleet(((long)empire.Id << 32) | id) { Name = name, Position = new NovaPoint(0, 0) };
            empire.OwnedFleets.Add(fleet);
            return fleet;
        }

        private static string Rename(Fleet fleet, string newName)
        {
            return "<Order Type=\"RenameFleet\"><FleetKey>" + fleet.Key.ToString("X") + "</FleetKey><NewName>" + newName + "</NewName></Order>";
        }

        private static string RenameKey(long key, string newName)
        {
            return "<Order Type=\"RenameFleet\"><FleetKey>" + key.ToString("X") + "</FleetKey><NewName>" + newName + "</NewName></Order>";
        }

        private string WriteOrders(EmpireData slot, int turn, int id, params string[] orders)
        {
            string path = Path.Combine(tempFolder, slot.Race.Name + Global.OrdersExtension);
            File.WriteAllText(path, "<ROOT><Turn>" + turn + "</Turn><Id>" + id + "</Id><Orders>" + string.Concat(orders) + "</Orders></ROOT>");
            return path;
        }

        // ------------------------------------------------------------------ save-turn-file-format.md row 15

        /// <summary>§1a item 5(b): a file whose turn number is below the host's is a stale file
        /// from an earlier turn and is skipped silently - nothing applied, nothing shown.</summary>
        [Test]
        public void StaleOrdersFile_FromAnEarlierTurn_IsSkippedSilently()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Stale");
            Fleet fleet = AddFleet(empire, 1, "Original");
            WriteOrders(empire, HostTurn - 1, empire.Id, Rename(fleet, "FromLastYear"));

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("Original", fleet.Name, "nothing from the stale file is applied");
            Assert.IsFalse(server.AllCommands.ContainsKey(empire.Id));
            Assert.IsFalse(empire.TurnSubmitted, "the slot contributes nothing");
            CollectionAssert.IsEmpty(reportedErrors, "the skip is silent");
        }

        /// <summary>§1a item 5(b) and its table: a file whose turn number is ABOVE the host's is
        /// skipped as well (the original records error 30 but, while generating, never shows it).</summary>
        [Test]
        public void OrdersFile_FromALaterTurn_IsSkipped_WithoutADialog()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Early");
            Fleet fleet = AddFleet(empire, 1, "Original");
            WriteOrders(empire, HostTurn + 1, empire.Id, Rename(fleet, "FromNextYear"));

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("Original", fleet.Name);
            Assert.IsFalse(empire.TurnSubmitted);
            CollectionAssert.IsEmpty(reportedErrors, "no dialog ever appears from these checks");
        }

        /// <summary>
        /// A file whose Id names another empire is skipped silently, so its orders are never
        /// replayed as this slot's. NOTE (divergence, reported): the original's step-6 loader
        /// matches a slot to its file by name only and does not compare the header's player field
        /// (turn-generation-engine.md §1a); only FUN_1070_2dfc does (save-turn-file-format.md
        /// §10.6). In this port every order is applied to the slot's own empire without per-opcode
        /// owner checks, so the Id check is what keeps a copied file from acting for the wrong
        /// race.
        /// </summary>
        [Test]
        public void OrdersFile_TaggedForAnotherEmpire_IsSkippedSilently()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Victim");
            AddEmpire(server, 2, "Forger");
            Fleet fleet = AddFleet(empire, 1, "Original");
            WriteOrders(empire, HostTurn, 2, Rename(fleet, "Forged"));

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("Original", fleet.Name);
            Assert.IsFalse(server.AllCommands.ContainsKey(empire.Id));
            Assert.IsFalse(empire.TurnSubmitted);
            CollectionAssert.IsEmpty(reportedErrors);
        }

        /// <summary>A current file for the right empire is applied, and marks the turn as
        /// submitted.</summary>
        [Test]
        public void MatchingOrdersFile_IsApplied()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Current");
            Fleet fleet = AddFleet(empire, 1, "Original");
            WriteOrders(empire, HostTurn, empire.Id, Rename(fleet, "Renamed"));

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("Renamed", fleet.Name);
            Assert.IsTrue(empire.TurnSubmitted);
            Assert.AreEqual(HostTurn, empire.LastTurnSubmitted);
            CollectionAssert.IsEmpty(reportedErrors);
        }

        /// <summary>§1a table: a slot with no order file (never submitted, AI without a file,
        /// eliminated race) is not an error; it simply contributes nothing.</summary>
        [Test]
        public void MissingOrdersFile_IsNotAnError()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Absent");
            Fleet fleet = AddFleet(empire, 1, "Original");

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("Original", fleet.Name);
            Assert.IsFalse(empire.TurnSubmitted);
            CollectionAssert.IsEmpty(reportedErrors);
        }

        /// <summary>
        /// §1a item 2: while generating, a failed open (the file is locked, not missing) is
        /// retried rather than given up at once. The retry interval and limit are not pinned
        /// here (see the report: Global.FileWaitRetryTime / TotalFileWaitTime are 100 ms / 8 s,
        /// the spec gives 0.5 s / 4 s); the file is released after 300 ms, well inside both.
        /// </summary>
        [Test]
        public void LockedOrdersFile_IsRetried_AndReadOnceReleased()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Locked");
            Fleet fleet = AddFleet(empire, 1, "Original");
            string path = WriteOrders(empire, HostTurn, empire.Id, Rename(fleet, "AfterTheLock"));

            FileStream exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Task release = Task.Run(() =>
            {
                Thread.Sleep(300);
                exclusive.Dispose();
            });

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();
            release.Wait();

            Assert.AreEqual("AfterTheLock", fleet.Name);
            Assert.IsTrue(empire.TurnSubmitted);
        }

        // ------------------------------------------------------------------ turn-generation-engine.md row 5

        /// <summary>
        /// Step 6 replays the file record by record and a bad record costs only itself (this port
        /// deliberately does better than the original, which aborts the whole run): an order that
        /// fails validation at replay (a fleet the race does not own) and an unrecognised order
        /// type (the original counts a record type outside the decoder's cases as success) are
        /// skipped, and every good order before and after them still lands.
        /// </summary>
        [Test]
        public void ReplayIsPerRecord_InvalidAndUnknownOrdersCostOnlyThemselves()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Isolated");
            EmpireData other = AddEmpire(server, 2, "Neighbour");
            Fleet first = AddFleet(empire, 1, "First");
            Fleet second = AddFleet(empire, 2, "Second");
            Fleet foreign = AddFleet(other, 1, "Theirs");

            WriteOrders(
                empire,
                HostTurn,
                empire.Id,
                Rename(first, "FirstRenamed"),
                Rename(foreign, "Hijacked"),
                RenameKey(((long)empire.Id << 32) | 99, "NoSuchFleet"),
                "<Order Type=\"NoSuchOrderType\"><Anything>1</Anything></Order>",
                Rename(second, "SecondRenamed"));

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("FirstRenamed", first.Name);
            Assert.AreEqual("SecondRenamed", second.Name, "orders after the bad records still land");
            Assert.AreEqual("Theirs", foreign.Name, "another race's fleet is refused");
            Assert.IsTrue(empire.TurnSubmitted);
            Assert.AreEqual(0, server.AllCommands[empire.Id].Count, "the whole buffer was walked");
        }

        /// <summary>A malformed record (unparseable at load) and an invalid one (refused at
        /// replay) in one file: both are lost, the rest of that file and every other slot's file
        /// are unaffected.</summary>
        [Test]
        public void BadRecordsInOneSlotsFile_DoNotAffectAnotherSlot()
        {
            ServerData server = NewServer();
            EmpireData broken = AddEmpire(server, 1, "Broken");
            EmpireData clean = AddEmpire(server, 2, "Clean");
            Fleet brokenFleet = AddFleet(broken, 1, "B1");
            Fleet cleanFleet = AddFleet(clean, 1, "C1");

            WriteOrders(
                broken,
                HostTurn,
                broken.Id,
                "<Order Type=\"RenameFleet\"><FleetKey>NOT_HEX</FleetKey><NewName>Bad</NewName></Order>",
                Rename(cleanFleet, "Stolen"),
                Rename(brokenFleet, "B1Renamed"));
            WriteOrders(clean, HostTurn, clean.Id, Rename(cleanFleet, "C1Renamed"));

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("B1Renamed", brokenFleet.Name);
            Assert.AreEqual("C1Renamed", cleanFleet.Name);
            Assert.IsTrue(broken.TurnSubmitted);
            Assert.IsTrue(clean.TurnSubmitted);
        }

        /// <summary>The records are replayed in the order they were made, so a later edit of the
        /// same thing wins. The orders file lists the newest order first (the client's command
        /// stack); the replay applies the oldest first.</summary>
        [Test]
        public void Replay_AppliesTheOrdersInTheOrderTheyWereMade()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Ordered");
            Fleet fleet = AddFleet(empire, 1, "Original");

            // Newest first, as the client writes its command stack.
            WriteOrders(empire, HostTurn, empire.Id, Rename(fleet, "Newest"), Rename(fleet, "Middle"), Rename(fleet, "Oldest"));

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("Newest", fleet.Name);
        }

        /// <summary>§1a item 5: the "submitted" flag is not tested - saved orders are applied
        /// even when the player never pressed submit. In this port the file carries no such flag,
        /// and an orders file for the current turn is applied whatever the empire's
        /// TurnSubmitted state was before the load.</summary>
        [Test]
        public void OrdersAreApplied_WhetherOrNotTheTurnWasMarkedSubmitted()
        {
            ServerData server = NewServer();
            EmpireData empire = AddEmpire(server, 1, "Unsubmitted");
            empire.TurnSubmitted = false;
            Fleet fleet = AddFleet(empire, 1, "Original");
            WriteOrders(empire, HostTurn, empire.Id, Rename(fleet, "Applied"));

            new OrdersOnlyTurnGenerator(server).LoadAndApplyOrders();

            Assert.AreEqual("Applied", fleet.Name);
        }

        // ------------------------------------------------------------------ turn-generation-engine.md row 32

        private static void Invasion(out EmpireData attacker, out EmpireData defender, out Star star, out Fleet fleet)
        {
            attacker = new EmpireData { Id = 1, Race = new Race() };
            defender = new EmpireData { Id = 2, Race = new Race() };
            attacker.EmpireReports.Add(defender.Id, new EmpireIntel(defender) { Relation = PlayerRelation.Enemy });
            defender.EmpireReports.Add(attacker.Id, new EmpireIntel(attacker) { Relation = PlayerRelation.Enemy });

            star = new Star { Name = "Bastion", Owner = defender.Id, Colonists = 2000 };
            defender.OwnedStars.Add(star);

            fleet = new Fleet(((long)attacker.Id << 32) | 1) { Owner = attacker.Id, Name = "Landing Party" };
            fleet.InOrbit = star;
            fleet.Cargo.ColonistsInKilotons = 50;
        }

        /// <summary>
        /// fleet-movement-scanning-cargo.md §4, colonist unload outcome table, "Another race's /
        /// starbase yes / Other race, Unload task": refused with message 309 and the colonists
        /// stay aboard - nobody is killed. (Message 88, every landing killed, belongs to the
        /// Transfer-dialog route, which files the colonists in the landing ledger at step 6; this
        /// port has no such route.)
        /// </summary>
        [Test]
        public void UnloadOntoAnotherRacesPlanetWithAStarbase_IsRefused_TheColonistsStayAboard()
        {
            Invasion(out EmpireData attacker, out EmpireData defender, out Star star, out Fleet fleet);
            star.Starbase = new Fleet(((long)defender.Id << 32) | 7);
            InvadeTask task = new InvadeTask();

            bool valid = task.IsValid(fleet, star, attacker, defender);

            Assert.IsFalse(valid, "the handler refuses: no invasion record is made");
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons, "the colonists stay aboard (309), they are not killed (88)");
            Assert.AreEqual(2000, star.Colonists);
            Assert.AreEqual(defender.Id, star.Owner);
            Assert.AreEqual(1, task.Messages.Count, "one refusal message");
            Assert.AreEqual(attacker.Id, task.Messages[0].Audience, "told to the fleet's owner");
        }

        /// <summary>The same planet without a starbase is invaded (§11: destroying the starbase
        /// opens the planet to invasion).</summary>
        [Test]
        public void UnloadOntoTheSamePlanetWithoutAStarbase_IsAnInvasion()
        {
            Invasion(out EmpireData attacker, out EmpireData defender, out Star star, out Fleet fleet);
            InvadeTask task = new InvadeTask();

            Assert.IsTrue(task.IsValid(fleet, star, attacker, defender));
            task.Perform(fleet, star, attacker, defender);

            // 5,000 troops x 110% = 5,500 against 2,000 defenders: the attackers take the planet.
            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons, "the troops are committed");
            Assert.AreEqual(attacker.Id, star.Owner);
        }

        /// <summary>Table row "Own / any starbase": an ordinary transfer, the starbase is
        /// irrelevant.</summary>
        [Test]
        public void UnloadOntoOwnPlanetWithAStarbase_IsAnOrdinaryTransfer()
        {
            Invasion(out EmpireData attacker, out EmpireData defender, out Star star, out Fleet fleet);
            star.Owner = attacker.Id;
            star.Starbase = new Fleet(((long)attacker.Id << 32) | 7);
            InvadeTask task = new InvadeTask();

            task.IsValid(fleet, star, attacker, attacker);

            Assert.AreEqual(0, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(2000 + 50 * Global.ColonistsPerKiloton, star.Colonists, "added to the population at once");
            Assert.AreEqual(attacker.Id, star.Owner);
        }

        /// <summary>Table row "Unowned": refused with message 85, colonists stay aboard (nobody
        /// colonises by unloading).</summary>
        [Test]
        public void UnloadOntoAnUnownedPlanet_IsRefused_TheColonistsStayAboard()
        {
            Invasion(out EmpireData attacker, out EmpireData defender, out Star star, out Fleet fleet);
            star.Owner = Global.Nobody;
            star.Colonists = 0;
            InvadeTask task = new InvadeTask();

            Assert.IsFalse(task.IsValid(fleet, star, attacker, null));
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons);
            Assert.AreEqual(Global.Nobody, star.Owner);
            Assert.AreEqual(0, star.Colonists);
        }
    }
}
