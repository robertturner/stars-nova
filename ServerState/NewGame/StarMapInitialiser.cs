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

            // Interstellar Traveler's full home starbase also carries a Stargate (100kt/250ly)
            // ALONGSIDE its full weapon/shield loadout, not instead of it - the earlier version
            // of this fix left this Design with no Orbital-or-Electrical component at all,
            // overcorrecting for the original bug (see the second-base comment below) of both
            // planets sharing one Design. Now that each planet has its own Design, equipping this
            // one directly is safe - it's never attached to the second planet anymore.
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

            // Interstellar Traveler and Packet Physics both start with a SECOND, smaller
            // starbase design for their second starting planet (see InitializeHomeStar's own
            // comment on that second planet) - a distinct Design on the cheap "Orbital Fort"
            // hull, matching the original game's "one full starbase, one small one" split: a
            // modest weapon/shield loadout (smaller than the home star's full combat starbase,
            // but real, not empty) plus the same signature component every one of this empire's
            // starbases carries. This used to instead equip the shared "Starbase" Design above,
            // which put the Stargate/Mass Driver on BOTH planets (since every starbase fleet
            // referenced that same Design) and left this second planet with either a full combat
            // starbase or, after the first fix, no weapons/shields at all - see PROJECT-STATUS.md.
            if (empire.Race.HasTrait("IT") || empire.Race.HasTrait("PP"))
            {
                bool isInterstellarTraveler = empire.Race.HasTrait("IT");
                Component secondBaseHull = components.Fetch("Orbital Fort");
                Component orbitalComponent = isInterstellarTraveler
                    ? components.Fetch("Stargate 100/250")
                    : components.Fetch("Mass Driver 5");

                ShipDesign secondBase = new ShipDesign(empire.GetNextDesignKey());
                secondBase.Name = isInterstellarTraveler ? "Stargate" : "Mass Driver Base";
                secondBase.Blueprint = secondBaseHull;
                secondBase.Type = ItemType.Starbase;
                secondBase.Icon = new ShipIcon(secondBaseHull.ImageFile, secondBaseHull.ComponentImage);

                foreach (HullModule module in secondBase.Hull.Modules)
                {
                    if (module.ComponentType == "Weapon")
                    {
                        module.AllocatedComponent = laser;
                        module.ComponentCount = 4;
                    }
                    else if (module.ComponentType == "Shield or Armor")
                    {
                        module.AllocatedComponent = shield;
                        module.ComponentCount = 4;
                    }
                    else if (module.ComponentType == "Orbital or Electrical")
                    {
                        module.AllocatedComponent = orbitalComponent;
                        module.ComponentCount = 1;
                    }
                }

                secondBase.Update();
                empire.Designs[secondBase.Key] = secondBase;
            }
            // Some PRTs start with additional ship types beyond the universal scout/colony-
            // ship/starbase trio - this used to be exactly the dead pseudocode this comment
            // replaces (a `switch` wrapped in a block comment, never compiled or run - every
            // race got the same one scout + one colony ship + one starbase regardless of PRT).
            // Now sourced directly from the official Stars! Player's Guide's "Starting
            // Advantages" section for each Primary Trait (Step 2: Primary Trait, pp 20-3 to
            // 20-11 - https://archive.org/download/manual_Stars/Stars.pdf). Starting RESEARCH
            // levels for these same PRTs are a separate, already-working system - see
            // Gameinitializer.ProcessPrimaryTraits, which several of these designs below rely
            // on already having granted the tech a bonus component needs (e.g. CA's Orbital
            // Adjuster requires Biotechnology 6, which ProcessPrimaryTraits already sets for CA
            // before GeneratePlayerAssets - and therefore this method - ever runs).
            //
            // The scout variations (HE/WM's armed scout, PP's two shielded scouts, JOAT's
            // second plain scout) are built here but actually assigned to a starting fleet in
            // AllocateHomeStarOrbitalInstallations, which needs to pick the right design name
            // per PRT instead of always "Scout".
            if (empire.Race.HasTrait("HE") || empire.Race.HasTrait("WM"))
            {
                // "One armed scout" (HE, p 20-3; WM, p 20-5) - the same Scout hull, with a
                // Laser (needs no tech, same as every other starting weapon in this method) in
                // the otherwise-empty General Purpose slot, which accepts anything except an
                // Engine - see ShipDesignViewModel.IsCompatible's own comment.
                ShipDesign armedScout = new ShipDesign(empire.GetNextDesignKey());
                // A fresh Fetch(), not a reuse of the outer scoutHull - Component.Fetch()
                // deep-clones a hull's Modules list per call (see Hull.Clone()), but Blueprint
                // is a plain reference assignment, so reusing one already-fetched Component
                // across two ShipDesigns would make them share (and clobber) the same
                // HullModule objects. Confirmed live: this was originally shared with the
                // unconditional "Scout" design above, and building this one afterwards
                // silently turned that "Scout" design's own slots into an armed scout too.
                armedScout.Blueprint = components.Fetch("Scout");
                foreach (HullModule module in armedScout.Hull.Modules)
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
                    else if (module.ComponentType == "General Purpose")
                    {
                        module.AllocatedComponent = laser;
                        module.ComponentCount = 1;
                    }
                }
                armedScout.Icon = new ShipIcon(scoutHull.ImageFile, scoutHull.ComponentImage);
                armedScout.Type = ItemType.Ship;
                armedScout.Name = "Armed Scout";
                armedScout.Update();
                empire.Designs[armedScout.Key] = armedScout;
            }

            if (empire.Race.HasTrait("PP"))
            {
                // "Two shielded scouts" (p 20-8) - the same Scout hull again, with a Mole-skin
                // Shield in the General Purpose slot instead of a weapon.
                ShipDesign shieldedScout = new ShipDesign(empire.GetNextDesignKey());
                // A fresh Fetch() - see the identical comment on armedScout.Blueprint above.
                shieldedScout.Blueprint = components.Fetch("Scout");
                foreach (HullModule module in shieldedScout.Hull.Modules)
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
                    else if (module.ComponentType == "General Purpose")
                    {
                        module.AllocatedComponent = shield;
                        module.ComponentCount = 1;
                    }
                }
                shieldedScout.Icon = new ShipIcon(scoutHull.ImageFile, scoutHull.ComponentImage);
                shieldedScout.Type = ItemType.Ship;
                shieldedScout.Name = "Shielded Scout";
                shieldedScout.Update();
                empire.Designs[shieldedScout.Key] = shieldedScout;
            }

            if (empire.Race.HasTrait("CA"))
            {
                // "Every race with the Claim Adjuster trait starts out with one ship outfitted
                // with Orbital Adjusters" (Player's Guide, "Claim Adjusters and Terraforming
                // Other Players' Planets from Orbit", pp 6-20/6-21 and p 20-6) - the Scout hull
                // again, with an Orbital Adjuster (needs Biotechnology 6, which
                // ProcessPrimaryTraits already grants CA) in place of a weapon or shield.
                Component orbitalAdjuster = components.Fetch("Orbital Adjuster");

                ShipDesign adjusterShip = new ShipDesign(empire.GetNextDesignKey());
                // A fresh Fetch() - see the identical comment on armedScout.Blueprint above.
                adjusterShip.Blueprint = components.Fetch("Scout");
                foreach (HullModule module in adjusterShip.Hull.Modules)
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
                    else if (module.ComponentType == "General Purpose")
                    {
                        module.AllocatedComponent = orbitalAdjuster;
                        module.ComponentCount = 1;
                    }
                }
                adjusterShip.Icon = new ShipIcon(scoutHull.ImageFile, scoutHull.ComponentImage);
                adjusterShip.Type = ItemType.Ship;
                adjusterShip.Name = "Orbital Adjuster";
                adjusterShip.Update();
                empire.Designs[adjusterShip.Key] = adjusterShip;
            }

            if (empire.Race.HasTrait("SD"))
            {
                // "Two mine layers (one standard, one speed trap)" (p 20-7) - both on the
                // dedicated Mini Mine Layer hull, its two Mine Layer slots filled to their own
                // max with one mine type each. Speed Trap 20 needs Biotechnology 2 and
                // Propulsion 2 - exactly what ProcessPrimaryTraits already grants SD, and
                // presumably why those two fields (out of six) were chosen for SD's tech bonus
                // in the first place.
                Component mineDispenser = components.Fetch("Mine Dispenser 40");
                Component speedTrapMine = components.Fetch("Speed Trap 20");

                // Each design gets its own Fetch() of the hull - see the comment on
                // armedScout.Blueprint above on why reusing one Component across two
                // ShipDesigns would make them silently share (and clobber) the same
                // HullModule objects.
                ShipDesign standardMineLayer = new ShipDesign(empire.GetNextDesignKey());
                Component mineLayerHull = components.Fetch("Mini Mine Layer");
                standardMineLayer.Blueprint = mineLayerHull;
                foreach (HullModule module in standardMineLayer.Hull.Modules)
                {
                    if (module.ComponentType == "Engine")
                    {
                        module.AllocatedComponent = engine;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType.Contains("Scanner"))
                    {
                        module.AllocatedComponent = scaner;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType == "Mine Layer")
                    {
                        module.AllocatedComponent = mineDispenser;
                        module.ComponentCount = module.ComponentMaximum;
                    }
                }
                standardMineLayer.Icon = new ShipIcon(mineLayerHull.ImageFile, mineLayerHull.ComponentImage);
                standardMineLayer.Type = ItemType.Ship;
                standardMineLayer.Name = "Mine Layer";
                standardMineLayer.Update();
                empire.Designs[standardMineLayer.Key] = standardMineLayer;

                ShipDesign speedTrapLayer = new ShipDesign(empire.GetNextDesignKey());
                Component speedTrapHull = components.Fetch("Mini Mine Layer");
                speedTrapLayer.Blueprint = speedTrapHull;
                foreach (HullModule module in speedTrapLayer.Hull.Modules)
                {
                    if (module.ComponentType == "Engine")
                    {
                        module.AllocatedComponent = engine;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType.Contains("Scanner"))
                    {
                        module.AllocatedComponent = scaner;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType == "Mine Layer")
                    {
                        module.AllocatedComponent = speedTrapMine;
                        module.ComponentCount = module.ComponentMaximum;
                    }
                }
                speedTrapLayer.Icon = new ShipIcon(speedTrapHull.ImageFile, speedTrapHull.ComponentImage);
                speedTrapLayer.Type = ItemType.Ship;
                speedTrapLayer.Name = "Speed Trap";
                speedTrapLayer.Update();
                empire.Designs[speedTrapLayer.Key] = speedTrapLayer;
            }

            if (empire.Race.HasTrait("IT") || empire.Race.HasTrait("JOAT"))
            {
                // "One destroyer" - both Interstellar Traveler (p 20-9) and Jack Of All Trades
                // (p 20-10) get the same basic Destroyer, so it's built once and shared.
                Component destroyerHull = components.Fetch("Destroyer");

                ShipDesign destroyer = new ShipDesign(empire.GetNextDesignKey());
                destroyer.Blueprint = destroyerHull;
                foreach (HullModule module in destroyer.Hull.Modules)
                {
                    if (module.ComponentType == "Engine")
                    {
                        module.AllocatedComponent = engine;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType.Contains("Weapon"))
                    {
                        module.AllocatedComponent = laser;
                        module.ComponentCount = module.ComponentMaximum;
                    }
                    else if (module.ComponentType == "Armor")
                    {
                        module.AllocatedComponent = armor;
                        module.ComponentCount = module.ComponentMaximum;
                    }
                }
                destroyer.Icon = new ShipIcon(destroyerHull.ImageFile, destroyerHull.ComponentImage);
                destroyer.Type = ItemType.Ship;
                destroyer.Name = "Destroyer";
                destroyer.Update();
                empire.Designs[destroyer.Key] = destroyer;
            }

            if (empire.Race.HasTrait("IT"))
            {
                // "One privateer" (p 20-9) - the Privateer hull's own flavour is a "multi-
                // purpose freighter" (confirmed via the Interstellar Traveler strategy-guide
                // appendix cited in PROJECT-STATUS.md), so alongside its shield and scanner it
                // also gets a Laser in one of its two General Purpose slots for self-defense,
                // leaving the other (and the unfillable Base Cargo slot - see
                // ShipDesignViewModel.IsCompatible, nothing in this codebase has Type
                // "Base Cargo") empty.
                Component privateerHull = components.Fetch("Privateer");

                ShipDesign privateer = new ShipDesign(empire.GetNextDesignKey());
                privateer.Blueprint = privateerHull;
                bool weaponPlaced = false;
                foreach (HullModule module in privateer.Hull.Modules)
                {
                    if (module.ComponentType == "Engine")
                    {
                        module.AllocatedComponent = engine;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType.Contains("Scanner"))
                    {
                        module.AllocatedComponent = scaner;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType == "Shield or Armor")
                    {
                        module.AllocatedComponent = shield;
                        module.ComponentCount = module.ComponentMaximum;
                    }
                    else if (module.ComponentType == "General Purpose" && !weaponPlaced)
                    {
                        module.AllocatedComponent = laser;
                        module.ComponentCount = 1;
                        weaponPlaced = true;
                    }
                }
                privateer.Icon = new ShipIcon(privateerHull.ImageFile, privateerHull.ComponentImage);
                privateer.Type = ItemType.Ship;
                privateer.Name = "Privateer";
                privateer.Update();
                empire.Designs[privateer.Key] = privateer;
            }

            if (empire.Race.HasTrait("JOAT"))
            {
                // "One medium freighter" and "one mini miner" (p 20-10) - the "two scouts" and
                // shared Destroyer above cover the rest of JOAT's starting fleet.
                Component freighterHull = components.Fetch("Medium Freighter");

                ShipDesign freighter = new ShipDesign(empire.GetNextDesignKey());
                freighter.Blueprint = freighterHull;
                foreach (HullModule module in freighter.Hull.Modules)
                {
                    if (module.ComponentType == "Engine")
                    {
                        module.AllocatedComponent = engine;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType.Contains("Scanner"))
                    {
                        module.AllocatedComponent = scaner;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType == "Shield or Armor")
                    {
                        module.AllocatedComponent = armor;
                        module.ComponentCount = module.ComponentMaximum;
                    }
                }
                freighter.Icon = new ShipIcon(freighterHull.ImageFile, freighterHull.ComponentImage);
                freighter.Type = ItemType.Ship;
                freighter.Name = "Medium Freighter";
                freighter.Update();
                empire.Designs[freighter.Key] = freighter;

                Component minerHull = components.Fetch("Mini Miner");
                Component miningRobot = components.Fetch("Robo-Midget Miner");

                ShipDesign miner = new ShipDesign(empire.GetNextDesignKey());
                miner.Blueprint = minerHull;
                foreach (HullModule module in miner.Hull.Modules)
                {
                    if (module.ComponentType == "Engine")
                    {
                        module.AllocatedComponent = engine;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType.Contains("Scanner"))
                    {
                        module.AllocatedComponent = scaner;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType == "Mining Robot")
                    {
                        module.AllocatedComponent = miningRobot;
                        module.ComponentCount = module.ComponentMaximum;
                    }
                }
                miner.Icon = new ShipIcon(minerHull.ImageFile, minerHull.ComponentImage);
                miner.Type = ItemType.Ship;
                miner.Name = "Mini Miner";
                miner.Update();
                empire.Designs[miner.Key] = miner;
            }
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
                // AllocateStarbase below) - the small dedicated "Stargate"/"Mass Driver Base"
                // Design from PrepareDesigns, not the full combat starbase the primary home star
                // gets. PP's "(non-tiny universes)" qualifier is still not checked - an open
                // follow-up; see PROJECT-STATUS.md.
                if (empire.Race.HasTrait("PP") || empire.Race.HasTrait("IT"))
                {
                    Star secondStar = FindNearestUnownedStar(star.Position);
                    if (secondStar != null)
                    {
                        AllocateHomeStarResources(secondStar, empire);

                        string secondBaseDesignName = empire.Race.HasTrait("IT") ? "Stargate" : "Mass Driver Base";
                        AllocateStarbase(secondStar, empire, secondBaseDesignName);
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
            ShipDesign colonyShipDesign = FindDesign(empire, "Santa Maria");

            if (empire.Race.Traits.Primary.Code != "HE")
            {
                AddShipFleet(star, empire, colonyShipDesign, colonyShipDesign.Name + " #1");
            }
            else
            {
                for (int i = 1; i <= 3; i++)
                {
                    AddShipFleet(star, empire, colonyShipDesign, string.Format("{0} #{1}", colonyShipDesign.Name, i));
                }
            }

            // Most PRTs start with one plain Scout. Three don't - see PrepareDesigns' own
            // comment on where these alternate designs come from (the official Stars! Player's
            // Guide's "Starting Advantages" per Primary Trait): Hyper Expansion and War Monger
            // get an armed one instead, Packet Physics gets two shielded ones instead, and Jack
            // Of All Trades gets a second plain one alongside its first.
            string primaryCode = empire.Race.Traits.Primary.Code;
            if (primaryCode == "HE" || primaryCode == "WM")
            {
                AddShipFleet(star, empire, FindDesign(empire, "Armed Scout"), "Scout #1");
            }
            else if (primaryCode == "PP")
            {
                AddShipFleet(star, empire, FindDesign(empire, "Shielded Scout"), "Scout #1");
                AddShipFleet(star, empire, FindDesign(empire, "Shielded Scout"), "Scout #2");
            }
            else if (primaryCode == "JOAT")
            {
                ShipDesign scoutDesign = FindDesign(empire, "Scout");
                AddShipFleet(star, empire, scoutDesign, "Scout #1");
                AddShipFleet(star, empire, scoutDesign, "Scout #2");
            }
            else
            {
                AddShipFleet(star, empire, FindDesign(empire, "Scout"), "Scout #1");
            }

            AllocateStarbase(star, empire);
            AllocateBonusStartingShips(star, empire, primaryCode);
        }

        /// <summary>
        /// Extra starting ships some PRTs get beyond the universal scout/colony-ship/starbase
        /// trio and the scout variations above - see PrepareDesigns' own comment on where these
        /// come from (the official Stars! Player's Guide's "Starting Advantages" per Primary
        /// Trait, pp 20-3 to 20-11): Claim Adjuster's Orbital-Adjuster-equipped ship, Space
        /// Demolition's two mine layers, Interstellar Traveler's destroyer and privateer, and
        /// Jack Of All Trades' medium freighter, mini miner and destroyer. Only called for the
        /// primary home star (not IT/Packet Physics' second starting planet, which only gets a
        /// starbase - see InitializeHomeStar), matching the Player's Guide's one-off wording for
        /// each of these ("one ship", "one destroyer", etc, never "one per planet").
        /// </summary>
        private void AllocateBonusStartingShips(Star star, EmpireData empire, string primaryCode)
        {
            if (primaryCode == "CA")
            {
                AddShipFleet(star, empire, FindDesign(empire, "Orbital Adjuster"), "Orbital Adjuster #1");
            }
            else if (primaryCode == "SD")
            {
                AddShipFleet(star, empire, FindDesign(empire, "Mine Layer"), "Mine Layer #1");
                AddShipFleet(star, empire, FindDesign(empire, "Speed Trap"), "Speed Trap #1");
            }
            else if (primaryCode == "IT")
            {
                AddShipFleet(star, empire, FindDesign(empire, "Destroyer"), "Destroyer #1");
                AddShipFleet(star, empire, FindDesign(empire, "Privateer"), "Privateer #1");
            }
            else if (primaryCode == "JOAT")
            {
                AddShipFleet(star, empire, FindDesign(empire, "Medium Freighter"), "Medium Freighter #1");
                AddShipFleet(star, empire, FindDesign(empire, "Mini Miner"), "Mini Miner #1");
                AddShipFleet(star, empire, FindDesign(empire, "Destroyer"), "Destroyer #1");
            }
        }

        private static ShipDesign FindDesign(EmpireData empire, string name)
        {
            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Name == name)
                {
                    return design;
                }
            }

            return null;
        }

        private static void AddShipFleet(Star star, EmpireData empire, ShipDesign design, string fleetName)
        {
            ShipToken token = new ShipToken(design, 1);
            Fleet fleet = new Fleet(token, star, empire.GetNextFleetKey());
            fleet.Name = fleetName;
            empire.AddOrUpdateFleet(fleet);
        }

        /// <summary>
        /// Builds and attaches this empire's starbase to the given star - factored out of
        /// AllocateHomeStarOrbitalInstallations so it can also be called for Interstellar
        /// Traveler/Packet Physics' second starting planet (see InitializeHomeStar), which gets a
        /// starbase but not a fresh colony-ship/scout fleet like a true home star does. That
        /// second planet's starbase is a different, smaller Design than a true home star's (see
        /// PrepareDesigns) - designName lets the caller pick which one to attach.
        /// </summary>
        private void AllocateStarbase(Star star, EmpireData empire, string designName = "Starbase")
        {
            ShipDesign starbaseDesign = null;
            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Name == designName)
                {
                    starbaseDesign = design;
                }
            }

            ShipToken starbase = new ShipToken(starbaseDesign, 1);
            Fleet starbaseFleet = new Fleet(starbase, star, empire.GetNextFleetKey());
            starbaseFleet.Name = star.Name + " Starbase";

            // The Fleet(ShipToken, Star, long) constructor always defaults Type to
            // ItemType.Fleet - unlike Manufacture.CreateShips's own starbase branch, nothing here
            // ever corrected that for a starting starbase, so every new game's initial starbase
            // (every home star, plus IT/Packet Physics' second planet) was permanently mis-typed
            // as a plain Fleet. Harmless on its own (star.Starbase still pointed at the right
            // object), but it meant EmpireData.RemoveOrphanedStarbaseFleets' own Type check could
            // never recognize this one as a starbase once a real replacement superseded it later
            // - confirmed live from a real save where exactly this starting starbase lingered as
            // a permanent stray "fleet in orbit" after being replaced.
            starbaseFleet.Type = ItemType.Starbase;

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
