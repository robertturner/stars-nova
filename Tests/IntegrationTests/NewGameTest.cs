#region Copyright Notice
// ============================================================================
// Copyright (C) 2010 stars-nova
//
// This file is part of Stars-Nova.
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

#region Module Description
// ===========================================================================
// Test of game generation code.
// This test was created to capture issue #3029446 caused by attempting to 
// find a home Star in a rectangular map.
// ===========================================================================
#endregion

using System;
using System.Collections.Generic;
using System.Linq;

using Nova.Common;
using Nova.Common.Components;
using Nova.Server;
using Nova.Server.NewGame;
using NUnit.Framework;

namespace Nova.Tests.IntegrationTests
{
    /// <Summary>
    /// Test for game generation
    /// </Summary>
    [TestFixture]
    public class NewGameTest
    {
        /// <summary>
        /// Builds a fresh, unpopulated ServerData with the given number of same-named-race
        /// players, matching Map800x400Test's own setup - shared by the determinism tests below
        /// so two independent generations start from byte-identical inputs.
        /// </summary>
        private static ServerData BuildServerState(int playerCount)
        {
            var serverState = new ServerData();
            serverState.AllRaces.Clear();

            for (int i = 0; i < playerCount; i++)
            {
                Race race = new Race { Name = "Seedrace" + i };
                serverState.AllRaces.Add(race.Name, race);
                serverState.AllPlayers.Add(new PlayerSettings());
            }

            return serverState;
        }

        /// <summary>
        /// A minimal fingerprint of a generated galaxy - position, mineral concentration, and
        /// name (which also indirectly proves the shared NameGenerator drew names in the same
        /// order) for every star, keyed by generation order rather than by name (two runs with
        /// different seeds may allocate the same star names in a different order).
        /// </summary>
        private static List<(int X, int Y, int Boranium, int Ironium, int Germanium, string Name)> Fingerprint(ServerData serverState)
        {
            return serverState.AllStars.Values
                .OrderBy(star => star.Position.X).ThenBy(star => star.Position.Y)
                .Select(star => (star.Position.X, star.Position.Y, star.MineralConcentration.Boranium, star.MineralConcentration.Ironium, star.MineralConcentration.Germanium, star.Name))
                .ToList();
        }

        /// <summary>
        /// The core reproducibility guarantee GameSettings.Seed exists for: two independent
        /// StarMapinitializer runs given the same explicit seed must produce byte-identical
        /// galaxies (star positions, mineral concentrations, and name allocation order) - proof
        /// that every RNG-driven step (StarMapGenerator's placement, GenerateStars' minerals,
        /// and the shared NameGenerator) is actually threaded through the one seeded Random
        /// rather than silently falling back to its own independent new Random() somewhere.
        /// </summary>
        [Test]
        public void SameSeedProducesIdenticalGalaxy()
        {
            GameSettings.Data.MapHeight = 400;
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.StarDensity = 60;
            GameSettings.Data.StarSeparation = 10;
            GameSettings.Data.StarUniformity = 60;

            const int seed = 424242;

            ServerData first = BuildServerState(4);
            new StarMapinitializer(first, new Random(seed)).GenerateStars();

            ServerData second = BuildServerState(4);
            new StarMapinitializer(second, new Random(seed)).GenerateStars();

            var firstFingerprint = Fingerprint(first);
            var secondFingerprint = Fingerprint(second);

            Assert.Greater(firstFingerprint.Count, 0, "Generation produced no stars - test setup is broken.");
            CollectionAssert.AreEqual(firstFingerprint, secondFingerprint);
        }

        /// <summary>
        /// The converse of SameSeedProducesIdenticalGalaxy - different seeds must (overwhelmingly
        /// likely to) produce different galaxies, proving the seed actually has an effect rather
        /// than being accepted and ignored.
        /// </summary>
        /// <summary>
        /// docs/behavior-specs-3/diplomacy-relations.md §1 confirms (via decompile of the
        /// exported client) that a newly created race's relationship toward every other race
        /// initializes to Neutral - Gameinitializer.GenerateEmpires previously set every pair to
        /// Enemy instead, meaning every new game started with all empires already at war with
        /// everyone else.
        /// </summary>
        [Test]
        public void NewGame_InitialRelationsAreNeutral()
        {
            GameSettings.Data.MapHeight = 400;
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.StarDensity = 60;
            GameSettings.Data.StarSeparation = 10;
            GameSettings.Data.StarUniformity = 60;
            GameSettings.Data.GameName = "RelationsDefaultTest";

            string tempFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NovaTest_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(tempFolder);

            // Gameinitializer's private constructor writes this run's temp folder into
            // nova.conf (a real, shared, cross-process file - not scoped to this test process),
            // via Config's ServerFolder/GameSettingsFile keys. Once tempFolder is deleted below,
            // anything that later falls back to those keys (e.g. a real Nova.exe launch trying
            // to locate its settings file) breaks - confirmed live: this exact test corrupted
            // nova.conf earlier this session and broke `Nova.exe --gui -i <intel>` afterward
            // with a hidden "please locate Your Game Name.settings" dialog, until nova.conf was
            // manually repaired. Snapshotting and restoring it here, unconditionally, prevents
            // this test from ever leaking that kind of global side effect again.
            string configFile = Nova.Common.FileSearcher.GetConfigFile();
            bool configExisted = System.IO.File.Exists(configFile);
            byte[] configBackup = configExisted ? System.IO.File.ReadAllBytes(configFile) : null;

            try
            {
                Race raceA = new Race();
                raceA.Name = "NeutralTestRaceA";
                Race raceB = new Race();
                raceB.Name = "NeutralTestRaceB";

                var knownRaces = new Dictionary<string, Race> { { raceA.Name, raceA }, { raceB.Name, raceB } };
                var players = new List<PlayerSettings>
                {
                    new PlayerSettings { PlayerNumber = 1, RaceName = raceA.Name, AiProgram = "Human" },
                    new PlayerSettings { PlayerNumber = 2, RaceName = raceB.Name, AiProgram = "Human" },
                };

                // Drives Gameinitializer's private constructor + GenerateEmpires directly via
                // reflection - the only two steps this test actually needs - rather than the
                // full public Initialize pipeline, which also generates a star map and writes
                // real game files to disk (unnecessary I/O for checking one default value).
                var ctor = typeof(Gameinitializer).GetConstructor(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(string) }, null);
                object game = ctor.Invoke(new object[] { tempFolder });

                var generateEmpires = typeof(Gameinitializer).GetMethod("GenerateEmpires", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                generateEmpires.Invoke(game, new object[] { players, knownRaces });

                ServerData serverState = ((Gameinitializer)game).ServerState;

                foreach (EmpireData empire in serverState.AllEmpires.Values)
                {
                    foreach (EmpireIntel report in empire.EmpireReports.Values)
                    {
                        Assert.AreEqual(PlayerRelation.Neutral, report.Relation,
                            $"Empire {empire.Id}'s initial relation toward empire {report.Id} should be Neutral, not {report.Relation}.");
                    }
                }
            }
            finally
            {
                try
                {
                    if (configExisted)
                    {
                        System.IO.File.WriteAllBytes(configFile, configBackup);
                    }
                    else if (System.IO.File.Exists(configFile))
                    {
                        System.IO.File.Delete(configFile);
                    }
                }
                catch
                {
                    // Best-effort restore - see the comment above on why this matters, but a
                    // failure here shouldn't also fail the assertion this test already made.
                }

                try
                {
                    System.IO.Directory.Delete(tempFolder, true);
                }
                catch
                {
                    // Best-effort cleanup - not the point of the test.
                }
            }
        }

        [Test]
        public void DifferentSeedsProduceDifferentGalaxies()
        {
            GameSettings.Data.MapHeight = 400;
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.StarDensity = 60;
            GameSettings.Data.StarSeparation = 10;
            GameSettings.Data.StarUniformity = 60;

            ServerData first = BuildServerState(4);
            new StarMapinitializer(first, new Random(111)).GenerateStars();

            ServerData second = BuildServerState(4);
            new StarMapinitializer(second, new Random(222)).GenerateStars();

            CollectionAssert.AreNotEqual(Fingerprint(first), Fingerprint(second));
        }

        /// ----------------------------------------------------------------------------
        /// <Summary>
        /// Test rectangular map generation.
        /// 
        /// TODO Exception handling logic (if-statements) in tests is not recommended.
        /// </Summary>
        /// ----------------------------------------------------------------------------
        [Test]
        public void Map800x400Test()
        {
            const int NUM_ATTEMPTS = 1; 

            // Generate the map
            ServerData serverState = new ServerData();
            try
            {
                // some inital data
                

                serverState.AllRaces.Clear();
                Race race = new Race();

                for (int i = 0; i < 7; i++)
                {
                    race.Name = "foo" + i;
                    serverState.AllRaces.Add(race.Name, race);
                    serverState.AllPlayers.Add(new PlayerSettings());
                }      

                for (int attempts = 0; attempts < NUM_ATTEMPTS; ++attempts)
                {
                    serverState.AllStars.Clear();

                    // make a map
                    GameSettings.Data.MapHeight = 800;
                    GameSettings.Data.MapWidth = 400;
                    GameSettings.Data.StarDensity = 60;
                    GameSettings.Data.StarSeparation = 10;
                    GameSettings.Data.StarUniformity = 60;
     
                    StarMapinitializer starMapInitializer = new StarMapinitializer(serverState);
                    
                    starMapInitializer.GenerateStars();
                    starMapInitializer.GeneratePlayerAssets();
                }
            }
            catch
            {
                // fail on any exception
                System.Windows.Forms.MessageBox.Show("Number of stars: " + serverState.AllStars.Count); // keep this line for debugging - Dan 01 Jul 11
                Assert.Fail();
            }
        }

        /// <Summary>
        /// Packet Physics and Interstellar Traveler both start with a second homeworld-tier
        /// planet (docs/behavior-specs/race-traits.md §2) - previously entirely unimplemented
        /// (the only code that ever mentioned it was inside a commented-out switch statement).
        /// Also checks a race with neither trait still gets exactly one planet, and that the
        /// bonus doesn't starve another player of their own home star (it must not draw from
        /// map.Homeworlds, which is sized to exactly the player count).
        /// </Summary>
        [Test]
        public void GeneratePlayerAssets_PacketPhysicsAndInterstellarTraveler_GetSecondPlanet()
        {
            ServerData serverState = new ServerData();

            GameSettings.Data.MapHeight = 400;
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.StarDensity = 60;
            GameSettings.Data.StarSeparation = 10;
            GameSettings.Data.StarUniformity = 60;

            Race itRace = new Race();
            itRace.Name = "ITRace";
            itRace.Traits.SetPrimary("IT");
            serverState.AllRaces.Add(itRace.Name, itRace);

            Race normalRace = new Race();
            normalRace.Name = "NormalRace";
            serverState.AllRaces.Add(normalRace.Name, normalRace);

            // Only used by GenerateStars() to size map.Homeworlds to the player count - see
            // the comment on FindNearestUnownedStar in StarMapInitialiser.cs.
            serverState.AllPlayers.Add(new PlayerSettings());
            serverState.AllPlayers.Add(new PlayerSettings());

            EmpireData itEmpire = new EmpireData();
            itEmpire.Id = 1;
            itEmpire.Race = itRace;
            serverState.AllEmpires[itEmpire.Id] = itEmpire;

            EmpireData normalEmpire = new EmpireData();
            normalEmpire.Id = 2;
            normalEmpire.Race = normalRace;
            serverState.AllEmpires[normalEmpire.Id] = normalEmpire;

            StarMapinitializer starMapInitializer = new StarMapinitializer(serverState);
            starMapInitializer.GenerateStars();
            starMapInitializer.GeneratePlayerAssets();

            int itOwnedStars = 0;
            int normalOwnedStars = 0;
            int fullStarbases = 0;
            int smallStarbases = 0;
            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Owner == itEmpire.Id)
                {
                    itOwnedStars++;

                    // IT's two planets both get a Stargate-equipped starbase (docs/
                    // behavior-specs/race-traits.md §2), but not IDENTICAL ones: the home star
                    // gets its full combat "Starbase" (weapons/shields at full strength) plus the
                    // Gate, the second planet a small, dedicated "Stargate" base (modest
                    // weapons/shields) plus the same Gate - matching the original game's "one big
                    // base, one small one, both gated" split, not two copies of either extreme.
                    Assert.IsNotNull(star.Starbase, $"{star.Name} should have a starbase");
                    ShipDesign design = star.Starbase.Composition.Values.First().Design;
                    design.Update();
                    Assert.IsTrue(design.Summary.Properties.ContainsKey("Gate"), $"{star.Name}'s starbase should have a Stargate");

                    // Both designs should have real weapons/shields, not just the Gate - the
                    // small base's own "some guns and shields, not none" requirement is the part
                    // this test used to get wrong (it had zero of both after the first fix).
                    Assert.IsTrue(design.Weapons.Count > 0, $"{design.Name} should have some weapons");
                    Assert.Greater(design.Shield, 0, $"{design.Name} should have some shield");

                    if (design.Name == "Starbase")
                    {
                        fullStarbases++;
                    }
                    else
                    {
                        smallStarbases++;
                        Assert.AreEqual("Stargate", design.Name);
                    }
                }
                if (star.Owner == normalEmpire.Id)
                {
                    normalOwnedStars++;
                }
            }

            Assert.AreEqual(2, itOwnedStars, "Interstellar Traveler should start with two planets");
            Assert.AreEqual(1, normalOwnedStars, "A race without PP/IT should start with only one planet");
            Assert.AreEqual(1, fullStarbases, "IT's home star should have the full combat starbase");
            Assert.AreEqual(1, smallStarbases, "IT's second planet should have the small Stargate base");
        }

        /// <Summary>
        /// Packet Physics' two planets both get a Mass-Driver-equipped starbase, matching IT's
        /// "one full, one small, both gated" split above - the home star's full combat "Starbase"
        /// Design plus a Mass Driver, the second planet a small, dedicated "Mass Driver Base"
        /// Design (modest weapons/shields, not none) plus its own Mass Driver. Mirrors
        /// GeneratePlayerAssets_PacketPhysicsAndInterstellarTraveler_GetSecondPlanet above but
        /// checks PP specifically.
        /// </Summary>
        [Test]
        public void GeneratePlayerAssets_PacketPhysics_SecondPlanetHasMassDriverStarbase()
        {
            ServerData serverState = new ServerData();

            GameSettings.Data.MapHeight = 400;
            GameSettings.Data.MapWidth = 400;
            GameSettings.Data.StarDensity = 60;
            GameSettings.Data.StarSeparation = 10;
            GameSettings.Data.StarUniformity = 60;

            Race ppRace = new Race();
            ppRace.Name = "PPRace";
            ppRace.Traits.SetPrimary("PP");
            serverState.AllRaces.Add(ppRace.Name, ppRace);

            serverState.AllPlayers.Add(new PlayerSettings());

            EmpireData ppEmpire = new EmpireData();
            ppEmpire.Id = 1;
            ppEmpire.Race = ppRace;
            serverState.AllEmpires[ppEmpire.Id] = ppEmpire;

            StarMapinitializer starMapInitializer = new StarMapinitializer(serverState);
            starMapInitializer.GenerateStars();
            starMapInitializer.GeneratePlayerAssets();

            int starbasesWithMassDriver = 0;
            int ppOwnedStars = 0;
            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Owner != ppEmpire.Id)
                {
                    continue;
                }

                ppOwnedStars++;
                Assert.IsNotNull(star.Starbase, $"{star.Name} should have a starbase");
                ShipDesign design = star.Starbase.Composition.Values.First().Design;
                design.Update();
                Assert.IsTrue(design.Summary.Properties.ContainsKey("Mass Driver"), $"{star.Name}'s starbase should have a Mass Driver");
                Assert.IsTrue(design.Weapons.Count > 0, $"{design.Name} should have some weapons");
                Assert.Greater(design.Shield, 0, $"{design.Name} should have some shield");

                if (design.Name == "Mass Driver Base")
                {
                    starbasesWithMassDriver++;
                }
                else
                {
                    Assert.AreEqual("Starbase", design.Name, "The other planet should have the normal full starbase");
                }
            }

            Assert.AreEqual(2, ppOwnedStars, "Packet Physics should start with two planets");
            Assert.AreEqual(1, starbasesWithMassDriver, "Exactly one of Packet Physics' two starbases should be the small dedicated Mass Driver base");
        }
    }
}

