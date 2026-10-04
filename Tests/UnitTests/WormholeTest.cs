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
        /// <summary>
        /// A deterministic Random: Next(max) is always 0 and Next(min, max) is always min, so the
        /// tier roll is 0 and every drawn offset is the minimum. This forces a jump for any
        /// positive tier and, for tier 0, an ordinary drift of -12 on each axis.
        /// </summary>
        private class ZeroRandom : Random
        {
            public override int Next(int maxValue)
            {
                return 0;
            }

            public override int Next(int minValue, int maxValue)
            {
                return minValue;
            }
        }

        [SetUp]
        public void SetUp()
        {
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.MapHeight = 400;
            GameSettings.Data.NoRandomEvents = false;
            GameSettings.Data.UseGalaxyPresets = false;
        }

        [Test]
        public void GenerateWormholes_PlacesLinkedPairs_AwayFromEveryStar()
        {
            // Medium (index 2) so at least one pair is always created.
            GameSettings.Data.MapWidth = 1200;
            GameSettings.Data.MapHeight = 1200;

            ServerData serverData = new ServerData();
            for (int i = 0; i < 40; i++)
            {
                Star star = new Star();
                star.Name = "Star" + i;
                star.Position = new NovaPoint((i * 37) % 1200, (i * 53) % 1200);
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
                    // A score-0 placement is at least 28 ly from every planet.
                    Assert.GreaterOrEqual(PointUtilities.Distance(wormhole.Position, star.Position), 28.0,
                        "A wormhole must not be placed inside a star's gravity well");
                }
            }
        }

        [Test]
        public void GenerateWormholes_NoRandomEvents_CreatesNone()
        {
            GameSettings.Data.NoRandomEvents = true;
            ServerData serverData = new ServerData();

            new StarMapinitializer(serverData, new Random(1)).GenerateWormholes();

            Assert.IsEmpty(serverData.AllWormholes, "No wormholes exist under No Random Events");
        }

        [Test]
        public void GenerateWormholes_PairCountFollowsTheGalaxySizeTable()
        {
            // Pair counts: Tiny 0-2, Small 1-3, Medium 1-5, Large 3-6, Huge 4-8.
            int[] minimumPairs = { 0, 1, 1, 3, 4 };
            int[] pairSpan = { 3, 3, 5, 4, 5 };

            for (int sizeIndex = 0; sizeIndex < 5; sizeIndex++)
            {
                GameSettings.Data.MapWidth = 400 * (sizeIndex + 1);
                GameSettings.Data.MapHeight = GameSettings.Data.MapWidth;

                for (int seed = 0; seed < 40; seed++)
                {
                    ServerData serverData = new ServerData();
                    new StarMapinitializer(serverData, new Random(seed)).GenerateWormholes();

                    int pairs = serverData.AllWormholes.Count / 2;
                    Assert.GreaterOrEqual(pairs, minimumPairs[sizeIndex]);
                    Assert.LessOrEqual(pairs, minimumPairs[sizeIndex] + pairSpan[sizeIndex] - 1);
                }
            }
        }

        [Test]
        public void StabilityTier_IsBasePlusAgeOverFiveMinusTwo()
        {
            Wormhole wormhole = new Wormhole { BaseStability = 0, Age = 0 };
            Assert.AreEqual(0, wormhole.StabilityTier, "base 0 age 0 is clamped up to 0");

            wormhole = new Wormhole { BaseStability = 2, Age = 0 };
            Assert.AreEqual(0, wormhole.StabilityTier, "base 2 age 0 is tier 0");

            wormhole = new Wormhole { BaseStability = 2, Age = 5 };
            Assert.AreEqual(1, wormhole.StabilityTier);

            wormhole = new Wormhole { BaseStability = 0, Age = 15 };
            Assert.AreEqual(1, wormhole.StabilityTier, "base 0 reaches tier 1 at age 15");

            wormhole = new Wormhole { BaseStability = 2, Age = 30 };
            Assert.AreEqual(6, wormhole.StabilityTier, "base 2 reaches tier 6 at age 30");

            wormhole = new Wormhole { BaseStability = 0, Age = 40 };
            Assert.AreEqual(6, wormhole.StabilityTier, "base 0 reaches tier 6 at age 40");

            wormhole = new Wormhole { BaseStability = 2, Age = 1000 };
            Assert.AreEqual(6, wormhole.StabilityTier, "the tier clamps at 6");
        }

        [Test]
        public void PlacementScore_UsesTheSpecifiedSquaredDistanceTiers()
        {
            List<Star> planets = new List<Star> { new Star { Position = new NovaPoint(100, 100) } };

            // Exactly on a planet is unusable.
            Assert.AreEqual(WormholePlacement.OutsideGalaxyScore,
                WormholePlacement.Score(new NovaPoint(100, 100), 400, 400, planets, null, null, 0, 0));

            // Planet tiers: 8 < 25, 4 < 100, 2 < 400, 1 < 784.
            Assert.AreEqual(8, WormholePlacement.Score(new NovaPoint(104, 100), 400, 400, planets, null, null, 0, 0));
            Assert.AreEqual(4, WormholePlacement.Score(new NovaPoint(109, 100), 400, 400, planets, null, null, 0, 0));
            Assert.AreEqual(2, WormholePlacement.Score(new NovaPoint(119, 100), 400, 400, planets, null, null, 0, 0));
            Assert.AreEqual(1, WormholePlacement.Score(new NovaPoint(127, 100), 400, 400, planets, null, null, 0, 0));
            Assert.AreEqual(0, WormholePlacement.Score(new NovaPoint(128, 100), 400, 400, planets, null, null, 0, 0));

            // The partner end has wider tiers: 8 < 25, 4 < 100, 2 < 900, 1 < 4,900.
            List<Wormhole> ends = new List<Wormhole>
            {
                new Wormhole { Key = 2, Position = new NovaPoint(200, 100) }
            };
            Assert.AreEqual(8, WormholePlacement.Score(new NovaPoint(204, 100), 400, 400, null, ends, null, 2, 1));
            Assert.AreEqual(4, WormholePlacement.Score(new NovaPoint(209, 100), 400, 400, null, ends, null, 2, 1));
            Assert.AreEqual(2, WormholePlacement.Score(new NovaPoint(229, 100), 400, 400, null, ends, null, 2, 1));
            Assert.AreEqual(1, WormholePlacement.Score(new NovaPoint(260, 100), 400, 400, null, ends, null, 2, 1));

            // Every other end has tighter tiers: 8 < 16, 4 < 64, 2 < 225, 1 < 900.
            Assert.AreEqual(8, WormholePlacement.Score(new NovaPoint(203, 100), 400, 400, null, ends, null, 99, 1));
            Assert.AreEqual(4, WormholePlacement.Score(new NovaPoint(207, 100), 400, 400, null, ends, null, 99, 1));
            Assert.AreEqual(2, WormholePlacement.Score(new NovaPoint(214, 100), 400, 400, null, ends, null, 99, 1));
            Assert.AreEqual(1, WormholePlacement.Score(new NovaPoint(229, 100), 400, 400, null, ends, null, 99, 1));

            // Within 10 ly of an edge adds 4; outside the galaxy is unusable.
            Assert.AreEqual(4, WormholePlacement.Score(new NovaPoint(5, 200), 400, 400, null, null, null, 0, 0));
            Assert.AreEqual(WormholePlacement.OutsideGalaxyScore,
                WormholePlacement.Score(new NovaPoint(-1, 200), 400, 400, null, null, null, 0, 0));
        }

        [Test]
        public void WormholeJump_ClearsLocatedAndResetsTheAge()
        {
            ServerData serverData = new ServerData();
            Wormhole wormhole = new Wormhole
            {
                Key = 1,
                PairedKey = 2,
                Position = new NovaPoint(200, 200),
                BaseStability = 2,
                Age = 5
            };
            wormhole.Located.Add(5);
            serverData.AllWormholes.Add(wormhole.Key, wormhole);

            new WormholeDriftStep(new ZeroRandom()).Process(serverData);

            // Tier 1 and a 0 roll: it jumped.
            Assert.AreEqual(new NovaPoint(0, 0), wormhole.Position, "a jump relocates the end anywhere");
            Assert.AreEqual(0, wormhole.Age, "a jump resets the age");
            Assert.AreEqual(2, wormhole.BaseStability, "a jump keeps the base");
            Assert.IsFalse(wormhole.IsLocatedBy(5), "a jump clears every located bit");
        }

        [Test]
        public void WormholeDrift_AgesAndMovesAtMostTwelveLightYearsPerAxis()
        {
            ServerData serverData = new ServerData();
            Wormhole wormhole = new Wormhole
            {
                Key = 1,
                PairedKey = 2,
                Position = new NovaPoint(200, 200),
                BaseStability = 0,
                Age = 0
            };
            wormhole.Located.Add(5);
            serverData.AllWormholes.Add(wormhole.Key, wormhole);

            new WormholeDriftStep(new ZeroRandom()).Process(serverData);

            // Tier 0 and a 0 roll (not below 0): it ages and drifts, -12 on each axis here.
            Assert.AreEqual(1, wormhole.Age);
            Assert.LessOrEqual(Math.Abs(wormhole.Position.X - 200), 12);
            Assert.LessOrEqual(Math.Abs(wormhole.Position.Y - 200), 12);
            Assert.IsTrue(wormhole.IsLocatedBy(5), "a drift is silent and keeps the located bit");
        }

        [Test]
        public void WormholeDriftStep_NeverMovesAWormholeOutsideMapBounds()
        {
            ServerData serverData = new ServerData();
            Wormhole wormhole = new Wormhole();
            wormhole.Key = 1;
            wormhole.PairedKey = 1; // self-paired is fine for this test - only position matters
            wormhole.Position = new NovaPoint(5, 395); // near two edges at once
            wormhole.BaseStability = 2;
            wormhole.Age = 30; // tier 6 - "Very Unstable", jumps at 6%
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
            NovaPoint exitAtTransit = new NovaPoint(exit.Position);
            turnGenerator.Generate();

            // The yearly drift (after movement) may move the exit afterwards; the fleet stays
            // where it came out, holding its own copy of the point.
            Assert.AreEqual(exitAtTransit, fleet.Position, "Fleet should emerge at the paired wormhole's position the same turn");
            Assert.AreNotSame(exit.Position, fleet.Position, "The fleet must not share the wormhole's position object, or it drifts with the mouth");

            // The race has now used the wormhole: its bit is set on both ends (the "known" test
            // of the AI's diversion scoring, ai-opponent-behavior.md §12).
            Assert.IsTrue(entrance.IsUsedBy(1));
            Assert.IsTrue(exit.IsUsedBy(1));
            Assert.IsFalse(exit.IsUsedBy(2));
        }
    }
}
