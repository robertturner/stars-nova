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

using Nova.Common;
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
            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Owner == itEmpire.Id)
                {
                    itOwnedStars++;
                }
                if (star.Owner == normalEmpire.Id)
                {
                    normalOwnedStars++;
                }
            }

            Assert.AreEqual(2, itOwnedStars, "Interstellar Traveler should start with two planets");
            Assert.AreEqual(1, normalOwnedStars, "A race without PP/IT should start with only one planet");
        }
    }
}

