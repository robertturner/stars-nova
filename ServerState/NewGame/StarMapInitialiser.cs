#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
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

namespace Nova.Server.NewGame
{
    using System;
    using System.Collections.Generic;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// This object contains static methods to initialize the star map. 
    /// Note that StarsMapGenerator handles positioning of the stars.
    /// </summary>
    public class StarMapinitializer
    {
        private ServerData serverState;
        private StarMapGenerator map;
        private readonly Random random;
        private NameGenerator nameGenerator;
        private Resources homeStarDefaultMineralConcentration = new Resources();
        private Resources homeStarDefaultSurfaceMinerals = new Resources();

        /// <param name="random">
        /// The Random to draw every map/mineral/homeworld-slot value from, and to construct the
        /// shared NameGenerator with. Optional (defaults to a freshly-seeded one) so existing
        /// callers are unaffected; pass in the same seeded Random Gameinitializer uses elsewhere
        /// to make a whole game's generation reproducible from one seed - see GameSettings.Seed.
        /// </param>
        public StarMapinitializer(ServerData serverState, Random random = null)
        {
            this.serverState = serverState;
            this.random = random ?? new Random();
            this.nameGenerator = new NameGenerator(this.random);
            this.map = new StarMapGenerator(
                GameSettings.Data.MapWidth,
                GameSettings.Data.MapHeight,
                GameSettings.Data.StarSeparation,
                GameSettings.Data.StarDensity,
                GameSettings.Data.StarUniformity,
                this.random);
        }


        /// <summary>
        /// Generate all the stars. We have two helper classes to assist in doing this:
        /// a name generator to allocate star names and a space generator to ensure
        /// stars have a reasonable separation. Beyond that, all we need to do is
        /// allocate some random mineral concentrations.
        /// </summary>
        /// <remarks>
        /// FIXME (priority 3) This method is public so that it can be called from the test fixture in NewGameTest.cs.
        /// That is the only method outside this object that should call this method.
        /// </remarks>
        public void GenerateStars()
        {
            map.Generate(serverState.AllPlayers.Count);

            foreach (int[] starPosition in map.Stars)
            {
                Star star = new Star();

                star.Position.X = starPosition[0];
                star.Position.Y = starPosition[1];

                star.Name = nameGenerator.NextStarName;

                star.MineralConcentration.Boranium = random.Next(1, 99);
                star.MineralConcentration.Ironium = random.Next(1, 99);
                star.MineralConcentration.Germanium = random.Next(1, 99);

                // The following values are percentages of the permissable range of
                // each environment parameter expressed as a percentage.
                star.Radiation = random.Next(1, 99);
                star.Gravity = random.Next(1, 99);
                star.Temperature = random.Next(1, 99);
                
                star.OriginalRadiation = star.Radiation;
                star.OriginalGravity = star.Gravity;
                star.OriginalTemperature = star.Temperature;

                serverState.AllStars[star.Name] = star;
            }
        }

        /// <summary>
        /// Places a handful of Wormhole pairs around the galaxy, each end far enough from any
        /// star (a real gravity well) and from every other special object - docs/behavior-specs-4/
        /// fleet-movement-scanning-cargo.md's "Wormholes" section. Must run after GenerateStars()
        /// so there are real star positions to keep clear of.
        ///
        /// Two simplifications from that section, both disclosed there and in Wormhole.cs's own
        /// comment: this uses a plain minimum-distance check rather than the spec's own
        /// unquantified "four squared-distance tiers" placement scoring, and the pair COUNT itself
        /// (one pair per ~20 stars, minimum 1) is an invented, reasonable density - the spec
        /// doesn't state how many wormholes a galaxy should generate with.
        /// </summary>
        public void GenerateWormholes()
        {
            const double minimumDistanceFromAnyObject = 30.0;
            const int maxPlacementAttempts = 200;

            int pairCount = Math.Max(1, serverState.AllStars.Count / 20);

            for (int i = 0; i < pairCount; i++)
            {
                Wormhole first = PlaceOneWormhole(minimumDistanceFromAnyObject, maxPlacementAttempts);
                if (first == null)
                {
                    continue; // galaxy too crowded to fit another pair - stop trying for more
                }

                Wormhole second = PlaceOneWormhole(minimumDistanceFromAnyObject, maxPlacementAttempts);
                if (second == null)
                {
                    serverState.AllWormholes.Remove(first.Key);
                    continue;
                }

                first.PairedKey = second.Key;
                second.PairedKey = first.Key;
            }
        }

        private Wormhole PlaceOneWormhole(double minimumDistanceFromAnyObject, int maxAttempts)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                NovaPoint candidate = new NovaPoint(random.Next(0, GameSettings.Data.MapWidth), random.Next(0, GameSettings.Data.MapHeight));

                bool tooClose = false;
                foreach (Star star in serverState.AllStars.Values)
                {
                    if (PointUtilities.Distance(candidate, star.Position) < minimumDistanceFromAnyObject)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    foreach (Wormhole existing in serverState.AllWormholes.Values)
                    {
                        if (PointUtilities.Distance(candidate, existing.Position) < minimumDistanceFromAnyObject)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                }

                if (tooClose)
                {
                    continue;
                }

                Wormhole wormhole = new Wormhole();
                wormhole.Key = nextWormholeKey++;
                wormhole.Position = candidate;
                wormhole.StabilityTier = random.Next(7); // 0 (Rock Solid) - 6 (Very Unstable)
                serverState.AllWormholes.Add(wormhole.Key, wormhole);
                return wormhole;
            }

            return null;
        }

        private long nextWormholeKey = 1;


        /// <summary>
        /// Initialize the general game data for each player. E,g, picking a home.
        /// planet, allocating initial resources, etc.
        /// </summary>
        public void GeneratePlayerAssets()
        {
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                string player = empire.Race.Name;
                
                PrepareDesigns(empire, player);
                PrepareResources();
                InitializeHomeStar(empire, player);
            }

            Nova.Common.Message welcome = new Nova.Common.Message();
            welcome.Text = "Your race is ready to explore the universe.";
            welcome.Audience = Global.Everyone;

            serverState.AllMessages.Add(welcome);
        }
  
        
        /// <summary>
        /// Initialize some starting designs.
        /// </summary>
        /// <param name="race">The <see cref="Race"/> of the player being initialized.</param>
        /// <param name="player">The player being initialized.</param>
        private void PrepareDesigns(EmpireData empire, string player)
        {
            // Read components data and create some basic stuff
            AllComponents components = new AllComponents();
            
            Component colonyShipHull = null, scoutHull = null;            
            Component colonizer = null;
            Component scaner = components.Fetch("Bat Scanner");
            Component armor = components.Fetch("Tritanium");
            Component shield = components.Fetch("Mole-skin Shield");
            Component laser = components.Fetch("Laser");
            Component torpedo = components.Fetch("Alpha Torpedo");

            Component starbaseHull = components.Fetch("Space Station");
            Component engine = components.Fetch("Quick Jump 5");
            Component colonyShipEngine = null;

            if (empire.Race.Traits.Primary.Code != "HE")
            {
                colonyShipHull = components.Fetch("Colony Ship");
            }
            else
            {
                colonyShipEngine = components.Fetch("Settler's Delight");
                colonyShipHull = components.Fetch("Mini-Colony Ship");
            }
            
            scoutHull = components.Fetch("Scout");

            if (empire.Race.HasTrait("AR") == true)
            {
                colonizer = components.Fetch("Orbital Construction Module");
            }
            else
            {
                colonizer = components.Fetch("Colonization Module");
            }


            if (colonyShipEngine == null)
            {
                colonyShipEngine = engine;
            }

            ShipDesign cs = new ShipDesign(empire.GetNextDesignKey());
            cs.Blueprint = colonyShipHull;
            foreach (HullModule module in cs.Hull.Modules)
            {
                if (module.ComponentType == "Engine")
                {
                    module.AllocatedComponent = colonyShipEngine;
                    module.ComponentCount = 1;
                }
                else if (module.ComponentType == "Mechanical")
                {
                    module.AllocatedComponent = colonizer;
                    module.ComponentCount = 1;
                }
            }
            cs.Icon = new ShipIcon(colonyShipHull.ImageFile, colonyShipHull.ComponentImage);

            cs.Type = ItemType.Ship;
            cs.Name = "Santa Maria";
            cs.Update();

            ShipDesign scout = new ShipDesign(empire.GetNextDesignKey());
            scout.Blueprint = scoutHull;
            foreach (HullModule module in scout.Hull.Modules)
            {
                if (module.ComponentType == "Engine")
                {
                    module.AllocatedComponent = engine;
                    module.ComponentCount = 1;
                }
                else if (module.ComponentType == "Scanner")
                {
                    module.AllocatedComponent = scaner;
                    module.ComponentCount = 1;
                }
            }
            scout.Icon = new ShipIcon(scoutHull.ImageFile, scoutHull.ComponentImage);

            scout.Type = ItemType.Ship;
            scout.Name = "Scout";
            scout.Update();

            ShipDesign starbase = new ShipDesign(empire.GetNextDesignKey());
            starbase.Name = "Starbase";
            starbase.Blueprint = starbaseHull;
            starbase.Type = ItemType.Starbase;
            starbase.Icon = new ShipIcon(starbaseHull.ImageFile, starbaseHull.ComponentImage);
            bool weaponSwitcher = false; // start with laser
            bool armorSwitcher = false; // start with armor
            foreach (HullModule module in starbase.Hull.Modules)
            {
                if (module.ComponentType == "Weapon")
                {
                    if (weaponSwitcher == false)
                    {
                        module.AllocatedComponent = laser;
                    }
                    else
                    {
                        module.AllocatedComponent = torpedo;
                    }
                    weaponSwitcher = !weaponSwitcher;
                    module.ComponentCount = 8;
                }
                if (module.ComponentType == "Shield")
                {
                    module.AllocatedComponent = shield;
                    module.ComponentCount = 8;
                }
                if (module.ComponentType == "Shield or Armor")
                {
                    if (armorSwitcher == false)
                    {
                        module.AllocatedComponent = armor;
                    }
                    else
                    {
                        module.AllocatedComponent = shield;
                    }
                    module.ComponentCount = 8;
                    armorSwitcher = !armorSwitcher;
                }
            }

            // Interstellar Traveler and Packet Physics both start with a starbase equipped for
            // their signature mechanic - see ProcessPrimaryTraits' own comments ("2 planets with
            // 100/250 stargates" for IT; the Energy=24 starting research level exists specifically
            // so Mass Driver 5's tech requirement is already met for PP). Every starbase this
            // empire ever starts with (both InitializeHomeStar's primary home star and its PP/IT
            // second planet, added below) shares this one Design, so equipping it once here here
            // covers both. The Space Station hull's two "Orbital or Electrical" slots (cells 11
            // and 13) are otherwise left empty by the loop above.
            if (empire.Race.HasTrait("IT") || empire.Race.HasTrait("PP"))
            {
                Component orbitalComponent = empire.Race.HasTrait("IT")
                    ? components.Fetch("Stargate 100/250")
                    : components.Fetch("Mass Driver 5");

                foreach (HullModule module in starbase.Hull.Modules)
                {
                    if (module.ComponentType == "Orbital or Electrical" && module.AllocatedComponent == null)
                    {
                        module.AllocatedComponent = orbitalComponent;
                        module.ComponentCount = 1;
                        break;
                    }
                }
            }

            starbase.Update();

            empire.Designs[starbase.Key] = starbase;
            empire.Designs[cs.Key] = cs;
            empire.Designs[scout.Key] = scout;
            /*
            switch (race.Traits.Primary.Code)
            {
                case "HE":
                    // Start with one armed scout + 3 mini-colony ships
                case "SS":
                    // Start with one scout + one colony ship.
                case "WM":
                    // Start with one armed scout + one colony ship.
                    break;

                case "CA":
                    // Start with an orbital terraforming ship
                    break;

                case "IS":
                    // Start with one scout and one colony ship
                    break;

                case "SD":
                    // Start with one scout, one colony ship, Two mine layers (one standard, one speed trap)
                    break;

                case "PP":
                    empireData.ResearchLevel[TechLevel.ResearchField.Energy] = 4;
                    // Two shielded scouts, one colony ship, two starting planets in a non-tiny universe
                    break;

                case "IT":
                    empireData.ResearchLevel[TechLevel.ResearchField.Propulsion] = 5;
                    empireData.ResearchLevel[TechLevel.ResearchField.Construction] = 5;
                    // one scout, one colony ship, one destroyer, one privateer, 2 planets with 100/250 stargates (in non-tiny universe)
                    break;

                case "AR":
                    empireData.ResearchLevel[TechLevel.ResearchField.Energy] = 1;

                    // starts with one scout, one orbital construction colony ship
                    break;

                case "JOAT":
                    // two scouts, one colony ship, one medium freighter, one mini miner, one destroyer
                    break;
            */
        }

        private void PrepareResources()
        {
            this.homeStarDefaultSurfaceMinerals.Boranium = random.Next(300, 500);
            this.homeStarDefaultSurfaceMinerals.Ironium = random.Next(300, 500);
            this.homeStarDefaultSurfaceMinerals.Germanium = random.Next(300, 500);

            // docs/behavior-specs-3/new-game-setup.md §3 confirms (via decompile of the exported
            // client) home-world mineral concentrations randomize to 100-299 inclusive, not the
            // previous 50-99 - Random.Next's upper bound is exclusive, hence 300 here.
            this.homeStarDefaultMineralConcentration.Boranium = random.Next(100, 300);
            this.homeStarDefaultMineralConcentration.Ironium = random.Next(100, 300);
            this.homeStarDefaultMineralConcentration.Germanium = random.Next(100, 300);
        }
        
        /// <summary>
        /// Allocate a "home" star system for each player giving it some colonists and
        /// initial resources. We use the space allocator helper class to ensure that
        /// the home systems for each race are not too close together.
        /// </summary>
        /// <param name="race"><see cref="Race"/> to be positioned.</param>
        /// <param name="spaceAllocator">The <see cref="SpaceAllocator"/> being used to allocate positions.</param>
        private void InitializeHomeStar(EmpireData empire, string player)
        {
            if (map.Homeworlds.Count > 0)
            {
                int[] starPosition = map.Homeworlds[random.Next(map.Homeworlds.Count)];
                
                Star star = new Star();
                star.Owner = empire.Id;

                star.Position.X = starPosition[0];
                star.Position.Y = starPosition[1];
                
                map.Homeworlds.Remove(starPosition);

                star.Name = nameGenerator.NextStarName;

                AllocateHomeStarResources(star, empire);
                AllocateHomeStarOrbitalInstallations(star, empire, player);

                serverState.AllStars[star.Name] = star;

                // Packet Physics and Interstellar Traveler both start with a second
                // homeworld-tier planet - see docs/behavior-specs/race-traits.md §2 ("PP:
                // Starts with a second homeworld-tier planet"; "IT: Starts with two
                // Stargate-equipped planets"). This deliberately does NOT draw from
                // map.Homeworlds, which StarMapGenerator sizes to exactly numPlayers - taking a
                // second slot here would leave a later player with no home star at all and hit
                // the FatalError below. Instead it grants the nearest currently-unowned regular
                // star generated by GenerateStars(), converted to the same habitability/
                // population/resources treatment as a real homeworld, plus a starbase (see
                // AllocateStarbase below) - equipped with a Stargate for IT, or a Mass Driver for
                // PP, per PrepareDesigns' own comment. PP's "(non-tiny universes)" qualifier is
                // still not checked - an open follow-up; see PROJECT-STATUS.md.
                if (empire.Race.HasTrait("PP") || empire.Race.HasTrait("IT"))
                {
                    Star secondStar = FindNearestUnownedStar(star.Position);
                    if (secondStar != null)
                    {
                        AllocateHomeStarResources(secondStar, empire);

                        // Both PRTs' second planet gets a starbase too (see PrepareDesigns' own
                        // comment for how that shared Design is equipped for each) - just not a
                        // second full colony-ship/scout fleet, which real Stars! doesn't grant
                        // here either.
                        AllocateStarbase(secondStar, empire);
                    }
                }

                return;
            }
            else
            {
                Report.FatalError("Could not allocate home star");
            }
        }

        /// <summary>
        /// Find the closest star to the given position that isn't owned by any empire yet.
        /// Used to grant Packet Physics/Interstellar Traveler their second starting planet
        /// without disturbing the reserved map.Homeworlds allocation (see InitializeHomeStar).
        /// </summary>
        private Star FindNearestUnownedStar(NovaPoint position)
        {
            Star nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (Star candidate in serverState.AllStars.Values)
            {
                if (candidate.Owner != 0)
                {
                    continue;
                }

                double distance = PointUtilities.Distance(position, candidate.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = candidate;
                }
            }

            return nearest;
        }
  
        
        /// <summary>
        /// Allocate an initial set of resources to a player's "home" star system. for
        /// each player giving it some colonists and initial resources.         
        /// </summary>
        /// <param name="star"></param>
        /// <param name="race"></param>
        private void AllocateHomeStarOrbitalInstallations(Star star, EmpireData empire, string player)
        {
            ShipDesign colonyShipDesign = null;
            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Name == "Santa Maria")
                {
                    colonyShipDesign = design;    
                }
            }
            
            if (empire.Race.Traits.Primary.Code != "HE")
            {
                ShipToken cs = new ShipToken(colonyShipDesign, 1);
                Fleet fleet1 = new Fleet(cs, star, empire.GetNextFleetKey());                               
                fleet1.Name = colonyShipDesign.Name + " #1";
                empire.AddOrUpdateFleet(fleet1);
            }
            else
            {
                for (int i = 1; i <= 3; i++)
                {
                    ShipToken cs = new ShipToken(colonyShipDesign, 1);
                    Fleet fleet = new Fleet(cs, star, empire.GetNextFleetKey());                    
                    fleet.Name = string.Format("{0} #{1}", colonyShipDesign.Name, i);                    
                    empire.AddOrUpdateFleet(fleet);
                }
            }
   
            ShipDesign scoutDesign = null;
            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Name == "Scout")
                {
                    scoutDesign = design;    
                }
            }
            
            ShipToken scout = new ShipToken(scoutDesign, 1);
            Fleet scoutFleet = new Fleet(scout, star, empire.GetNextFleetKey());
            scoutFleet.Name = "Scout #1";
            empire.AddOrUpdateFleet(scoutFleet);

            AllocateStarbase(star, empire);
        }

        /// <summary>
        /// Builds and attaches this empire's starbase to the given star - factored out of
        /// AllocateHomeStarOrbitalInstallations so it can also be called for Interstellar
        /// Traveler/Packet Physics' second starting planet (see InitializeHomeStar), which gets a
        /// starbase but not a fresh colony-ship/scout fleet like a true home star does.
        /// </summary>
        private void AllocateStarbase(Star star, EmpireData empire)
        {
            ShipDesign starbaseDesign = null;
            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Name == "Starbase")
                {
                    starbaseDesign = design;
                }
            }

            ShipToken starbase = new ShipToken(starbaseDesign, 1);
            Fleet starbaseFleet = new Fleet(starbase, star, empire.GetNextFleetKey());
            starbaseFleet.Name = star.Name + " Starbase";
            star.Starbase = starbaseFleet;
            empire.AddOrUpdateFleet(starbaseFleet);
        }


        /// <summary>
        /// Allocate an initial set of resources to a player's "home" star system. for
        /// each player giving it some colonists and initial resources. 
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void AllocateHomeStarResources(Star star, EmpireData empire)
        {
            // Set the owner of the home star in order to obtain proper
            // starting resources.
            star.Owner = empire.Id;
            star.ThisRace = empire.Race;
            star.EnergyTechLevel = empire.ResearchLevels[TechLevel.ResearchField.Energy];

            // Set the habital values for this star to the optimum for each race.
            // This should allTurnedIn in a planet value of 100% for this race's home
            // world.            
            star.Radiation = empire.Race.RadiationTolerance.OptimumLevel;
            star.Temperature = empire.Race.TemperatureTolerance.OptimumLevel;
            star.Gravity = empire.Race.GravityTolerance.OptimumLevel;
            
            star.OriginalRadiation = star.Radiation;
            star.OriginalGravity = star.Gravity;
            star.OriginalTemperature = star.Temperature;
            
            star.Colonists = empire.Race.GetStartingPopulation();

            star.ResourcesOnHand.Boranium = this.homeStarDefaultSurfaceMinerals.Boranium; // ToDo: leftover advantage points
            star.ResourcesOnHand.Ironium = this.homeStarDefaultSurfaceMinerals.Ironium;
            star.ResourcesOnHand.Germanium = this.homeStarDefaultSurfaceMinerals.Germanium;
            star.Mines = 10;
            star.Factories = 10;
            star.ResourcesOnHand.Energy = star.GetResourceRate();

            star.MineralConcentration.Boranium = this.homeStarDefaultMineralConcentration.Boranium;
            star.MineralConcentration.Ironium = this.homeStarDefaultMineralConcentration.Ironium;
            star.MineralConcentration.Germanium = this.homeStarDefaultMineralConcentration.Germanium;

            star.ScannerType = "Scoper 150"; // TODO (priority 4) get from component list
            star.DefenseType = "SDI"; // TODO (priority 4) get from component list
            star.ScanRange = empire.Race.HasTrait("NAS") ? 100 : 50; // TODO (priority 4) get from component list

            HomeStarLeftoverpointsAdjuster.Adjust(star, empire.Race);
        }
    }
}
