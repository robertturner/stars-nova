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
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.NewGame;
    using Nova.Server.TurnSteps;

    using NUnit.Framework;

    [TestFixture]
    public class WormholeTest
    {
        [SetUp]
        public void SetUp()
        {
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.MapHeight = 400;
        }

        [Test]
        public void GenerateWormholes_PlacesLinkedPairs_AwayFromEveryStar()
        {
            ServerData serverData = new ServerData();
            for (int i = 0; i < 40; i++)
            {
                Star star = new Star();
                star.Name = "Star" + i;
                star.Position = new NovaPoint((i * 37) % 400, (i * 53) % 400);
                serverData.AllStars.Add(star.Key, star);
            }

            StarMapinitializer initialiser = new StarMapinitializer(serverData, new Random(12345));
            initialiser.GenerateWormholes();

            Assert.IsTrue(serverData.AllWormholes.Count > 0, "Expected at least one wormhole pair to be placed");
            Assert.AreEqual(0, serverData.AllWormholes.Count % 2, "Wormholes must always come in pairs");

            foreach (Wormhole wormhole in serverData.AllWormholes.Values)
            {
                Assert.IsTrue(serverData.AllWormholes.ContainsKey(wormhole.PairedKey), "Every wormhole must link to another real wormhole");
                Assert.AreEqual(wormhole.Key, serverData.AllWormholes[wormhole.PairedKey].PairedKey, "Pairing must be symmetric");

                foreach (Star star in serverData.AllStars.Values)
                {
                    Assert.GreaterOrEqual(PointUtilities.Distance(wormhole.Position, star.Position), 30.0,
                        "A wormhole must not be placed on top of a star's gravity well");
                }
            }
        }

        [Test]
        public void WormholeDriftStep_NeverMovesAWormholeOutsideMapBounds()
        {
            ServerData serverData = new ServerData();
            Wormhole wormhole = new Wormhole();
            wormhole.Key = 1;
            wormhole.PairedKey = 1; // self-paired is fine for this test - only position matters
            wormhole.Position = new NovaPoint(5, 395); // near two edges at once
            wormhole.StabilityTier = 6; // "Very Unstable" - drifts most often and furthest
            serverData.AllWormholes.Add(wormhole.Key, wormhole);

            WormholeDriftStep driftStep = new WormholeDriftStep();
            for (int turn = 0; turn < 500; turn++)
            {
                driftStep.Process(serverData);
                Assert.GreaterOrEqual(wormhole.Position.X, 0);
                Assert.LessOrEqual(wormhole.Position.X, GameSettings.Data.MapWidth);
                Assert.GreaterOrEqual(wormhole.Position.Y, 0);
                Assert.LessOrEqual(wormhole.Position.Y, GameSettings.Data.MapHeight);
            }
        }

        [Test]
        public void Generate_FleetArrivingAtAWormhole_TeleportsToThePairedEnd()
        {
            ServerData serverData = new ServerData();

            Wormhole entrance = new Wormhole();
            entrance.Key = 1;
            entrance.Position = new NovaPoint(0, 0);
            Wormhole exit = new Wormhole();
            exit.Key = 2;
            exit.Position = new NovaPoint(300, 300);
            entrance.PairedKey = exit.Key;
            exit.PairedKey = entrance.Key;
            serverData.AllWormholes.Add(entrance.Key, entrance);
            serverData.AllWormholes.Add(exit.Key, exit);

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverData.AllEmpires.Add(empire.Id, empire);

            Fleet fleet = new Fleet(1);
            fleet.Owner = 1;
            fleet.Position = new NovaPoint(0, 0); // already sitting at the entrance

            ShipDesign design = new ShipDesign(1);
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);

            Waypoint waypoint = new Waypoint();
            waypoint.Position = entrance.Position;
            waypoint.WarpFactor = 0;
            waypoint.Task = new NoTask();
            waypoint.Destination = "the wormhole"; // not a real star name - matches by position only
            fleet.Waypoints.Add(waypoint);

            empire.AddOrUpdateFleet(fleet);

            SimpleTurnGenerator turnGenerator = new SimpleTurnGenerator(serverData);
            turnGenerator.Generate();

            Assert.AreEqual(exit.Position, fleet.Position, "Fleet should emerge at the paired wormhole's position the same turn");
        }
    }
}
