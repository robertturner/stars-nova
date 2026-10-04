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

            // The wizard's discrete Galaxy Size / Star Density (behavior-specs-10/
            // new-game-setup.md section 3): a square (size + 1) x 400 ly map and a formula star
            // count. Re-applied here so the map size always matches the chosen preset.
            GameSettings settings = GameSettings.Data;
            if (settings.UseGalaxyPresets)
            {
                settings.ApplyGalaxyPreset(settings.GalaxySizeSetting, settings.StarDensitySetting);
            }

            this.map = new StarMapGenerator(
                GameSettings.Data.MapWidth,
                GameSettings.Data.MapHeight,
                GameSettings.Data.StarSeparation,
                GameSettings.Data.StarDensity,
                GameSettings.Data.StarUniformity,
                this.random);

            if (settings.UseGalaxyPresets)
            {
                this.map.TargetStarCount = settings.NumberOfStars;
            }
            this.map.Clumping = settings.GalaxyClumping;
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

                // The following values are percentages of the permissable range of
                // each environment parameter expressed as a percentage.
                // behavior-specs-9/population-growth.md section 2: Gravity and Temperature are
                // 1 + U(0..89) + U(0..9) (a trapezoid over 1-99) and Radiation 1 + U(0..98) (uniform).
                // Rolled before the concentrations because the high-radiation concentration raise
                // (behavior-specs-10/new-game-setup.md section 3) reads the planet's radiation.
                RollEnvironment(star, random);

                // behavior-specs-10/new-game-setup.md section 3: 31-119, then the high-radiation
                // raise, the Accelerated BBS +5 and the low-concentration roll.
                RollMineralConcentrations(star, random);

                // Ordinary planets start with no surface minerals (behavior-specs-11/
                // new-game-setup.md section 3, raw `0x1855` and `0x18db`): the per-planet loop
                // zeroes all three stocks, and the tonnage roll after it indexes only the
                // home-world template (RollHomeSurfaceStock). Only home worlds (template copy)
                // and the Packet Physics / Interstellar Traveler second planets (100-299 kT
                // each) start with any. A fresh Star's stocks are already zero.
                serverState.AllStars[star.Name] = star;
            }
        }

        /// <summary>
        /// Rolls a planet's environment (current and original copies alike).
        /// behavior-specs-9/population-growth.md section 2: Gravity and Temperature are
        /// 1 + U(0..89) + U(0..9) and Radiation 1 + U(0..98).
        /// </summary>
        public static void RollEnvironment(Star star, Random random)
        {
            star.Radiation = 1 + random.Next(0, 99);
            star.Gravity = 1 + random.Next(0, 90) + random.Next(0, 10);
            star.Temperature = 1 + random.Next(0, 90) + random.Next(0, 10);

            star.OriginalRadiation = star.Radiation;
            star.OriginalGravity = star.Gravity;
            star.OriginalTemperature = star.Temperature;
        }

        /// <summary>
        /// Rolls an ordinary planet's three mineral concentrations, in the order of
        /// behavior-specs-10/new-game-setup.md section 3 (raw segment 16 `0x184c`-`0x19b1`):
        /// <list type="number">
        /// <item>two independent 0-44 rolls plus 31 (31-119), per mineral in Ironium, Boranium,
        /// Germanium order;</item>
        /// <item>on a planet whose current radiation is 90 or more, each concentration c gains
        /// half of a random 0 to (98 - c), rounded down;</item>
        /// <item>Accelerated BBS Play adds 5 to each concentration under 40;</item>
        /// <item>one low-concentration roll of 0-26 per planet: 18-26 nothing, 9-17 and 7-8 one
        /// replacement, 3-6 two, 1-2 three, 0 four - each replacement sets a freshly (and
        /// possibly repeatedly) chosen mineral's concentration to a random 1-30.</item>
        /// </list>
        /// The radiation must already be rolled. The template planet the home worlds copy runs
        /// through exactly the same steps. Under "Beginner: Maximum Minerals"
        /// (GameSettings.MaximumMinerals, option bit 0x01, `:50533`-`50586`) every concentration
        /// is a flat 100 and the radiation raise and low-concentration roll are skipped; no
        /// random draws are made.
        /// </summary>
        /// <remarks>
        /// The spec says "a planet at 99 or more gets nothing" for the radiation raise. The
        /// random range 0 to (98 - c) is empty once c reaches 99, so this reads it as "a
        /// concentration of 99 or more is not raised" (per mineral), not as a radiation test.
        /// The order of the two draws inside one replacement (mineral, then value) is not given
        /// by the spec; mineral first is assumed.
        /// </remarks>
        public static void RollMineralConcentrations(Star star, Random random)
        {
            if (GameSettings.Data.MaximumMinerals)
            {
                star.MineralConcentration.Ironium = MaximumMineralsConcentration;
                star.MineralConcentration.Boranium = MaximumMineralsConcentration;
                star.MineralConcentration.Germanium = MaximumMineralsConcentration;
                return;
            }

            star.MineralConcentration.Ironium = random.Next(0, 45) + random.Next(0, 45) + 31;
            star.MineralConcentration.Boranium = random.Next(0, 45) + random.Next(0, 45) + 31;
            star.MineralConcentration.Germanium = random.Next(0, 45) + random.Next(0, 45) + 31;

            if (star.Radiation >= 90)
            {
                star.MineralConcentration.Ironium = RaiseForHighRadiation(star.MineralConcentration.Ironium, random);
                star.MineralConcentration.Boranium = RaiseForHighRadiation(star.MineralConcentration.Boranium, random);
                star.MineralConcentration.Germanium = RaiseForHighRadiation(star.MineralConcentration.Germanium, random);
            }

            if (GameSettings.Data.AcceleratedStart)
            {
                if (star.MineralConcentration.Ironium < 40)
                {
                    star.MineralConcentration.Ironium += 5;
                }
                if (star.MineralConcentration.Boranium < 40)
                {
                    star.MineralConcentration.Boranium += 5;
                }
                if (star.MineralConcentration.Germanium < 40)
                {
                    star.MineralConcentration.Germanium += 5;
                }
            }

            int replacements = LowConcentrationReplacements(random.Next(0, 27));
            for (int i = 0; i < replacements; i++)
            {
                int mineral = random.Next(0, 3);
                int value = random.Next(1, 31);
                switch (mineral)
                {
                    case 0:
                        star.MineralConcentration.Ironium = value;
                        break;
                    case 1:
                        star.MineralConcentration.Boranium = value;
                        break;
                    default:
                        star.MineralConcentration.Germanium = value;
                        break;
                }
            }
        }

        /// <summary>Every concentration under "Beginner: Maximum Minerals" (new-game-setup.md section 1).</summary>
        public const int MaximumMineralsConcentration = 100;

        /// <summary>
        /// How many concentrations the low-concentration roll (0-26) replaces with 1-30 -
        /// behavior-specs-10/new-game-setup.md section 3: 18-26 none, 9-17 one, 7-8 one, 3-6
        /// two, 1-2 three, 0 four.
        /// </summary>
        public static int LowConcentrationReplacements(int roll)
        {
            if (roll >= 18)
            {
                return 0;
            }
            if (roll >= 7)
            {
                return 1;
            }
            if (roll >= 3)
            {
                return 2;
            }
            if (roll >= 1)
            {
                return 3;
            }
            return 4;
        }

        private static int RaiseForHighRadiation(int concentration, Random random)
        {
            if (concentration >= 99)
            {
                return concentration;
            }
            return concentration + (random.Next(0, 99 - concentration) / 2);
        }

        /// <summary>
        /// The template home-world surface stock for one mineral - behavior-specs-10/
        /// new-game-setup.md section 3 (`:50589`-`50619`): a random 0 to 10 x concentration - 1,
        /// plus 10; if that is under 200, plus 155 + a random 0-149; then +25% (rounded down)
        /// under Accelerated BBS Play.
        /// </summary>
        public static int RollHomeSurfaceStock(int concentration, Random random)
        {
            int stock = random.Next(0, 10 * Math.Max(1, concentration)) + 10;
            if (stock < 200)
            {
                stock += 155 + random.Next(0, 150);
            }
            if (GameSettings.Data.AcceleratedStart)
            {
                stock += stock / 4;
            }
            return stock;
        }

        /// <summary>
        /// Creates the galaxy's wormhole pairs - docs/behavior-specs-11/
        /// fleet-movement-scanning-cargo.md "Wormhole lifecycle, complete rule" item 1. None at all
        /// when "No Random Events" is set; otherwise the number of pairs is a uniform draw by
        /// galaxy size (Tiny 0-2, Small 1-3, Medium 1-5, Large 3-6, Huge 4-8). Each end draws its
        /// own base stability 0-2, starts at age 0 with empty masks, and takes a position from the
        /// 100-draw placement rule (<see cref="WormholePlacement"/>). Must run after
        /// GenerateStars() so there are real star positions to keep clear of.
        /// </summary>
        public void GenerateWormholes()
        {
            if (GameSettings.Data.NoRandomEvents)
            {
                return;
            }

            // The galaxy-size index s (0 Tiny .. 4 Huge), the same reading RandomEventsStep uses:
            // clamp(MapWidth / 400 - 1, 0, 4).
            int sizeIndex = Math.Max(0, Math.Min(4, (GameSettings.Data.MapWidth / 400) - 1));
            int minimumPairs = new[] { 0, 1, 1, 3, 4 }[sizeIndex];
            int pairSpan = new[] { 3, 3, 5, 4, 5 }[sizeIndex];
            int pairCount = minimumPairs + random.Next(pairSpan);

            for (int i = 0; i < pairCount; i++)
            {
                Wormhole first = PlaceOneWormhole(Global.Nobody);
                if (first == null)
                {
                    continue; // galaxy too crowded to fit another pair - stop trying for more
                }

                Wormhole second = PlaceOneWormhole(first.Key);
                if (second == null)
                {
                    serverState.AllWormholes.Remove(first.Key);
                    continue;
                }

                first.PairedKey = second.Key;
                second.PairedKey = first.Key;
            }
        }

        private Wormhole PlaceOneWormhole(long partnerKey)
        {
            int baseStability = random.Next(3); // uniform 0-2
            NovaPoint position = WormholePlacement.DrawAnywhere(
                random,
                GameSettings.Data.MapWidth,
                GameSettings.Data.MapHeight,
                serverState.AllStars.Values,
                serverState.AllWormholes.Values,
                serverState.IterateAllFleets(),
                partnerKey,
                Global.Nobody);
            if (position == null)
            {
                return null;
            }

            Wormhole wormhole = new Wormhole();
            wormhole.Key = nextWormholeKey++;
            wormhole.Position = position;
            wormhole.BaseStability = baseStability;
            wormhole.Age = 0;
            serverState.AllWormholes.Add(wormhole.Key, wormhole);
            return wormhole;
        }

        private long nextWormholeKey = 1;


        /// <summary>
        /// Initialize the general game data for each player. E,g, picking a home.
        /// planet, allocating initial resources, etc.
        /// </summary>
        public void GeneratePlayerAssets()
        {
            // One template for the whole game: every home world copies the same surface stocks
            // and (floored) concentrations - behavior-specs-10/new-game-setup.md section 3.
            PrepareResources();

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                string player = empire.Race.Name;

                PrepareDesigns(empire, player);
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

            // Scout pass first, then the colonizer (behavior-specs-10/new-game-setup.md section
            // 5a): a player's design slot 0 - the first ship design created, see SlotZeroShipDesign
            // - is always its first scout, which is the extra ship stationed at the Packet
            // Physics / Interstellar Traveler second home planet.
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
                // unconditional "Scout" design below, and building this one afterwards
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
            empire.Designs[scout.Key] = scout;

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
            empire.Designs[cs.Key] = cs;

            // Starbase designs. Alternate Reality's starbase slot 0 is the bare "Starter Colony"
            // (Orbital Fort hull) a won colonisation installs, and its homeworld's full "Starbase"
            // is slot 1 (behavior-specs-10/new-game-setup.md section 5b, population-growth.md
            // section 3), so the Starter Colony is created first.
            if (empire.Race.HasTrait("AR"))
            {
                StarterColony.EnsureDesign(empire);
            }

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

            // Advanced Remote Mining without Only Basic Remote Mining: the Potato Bug template
            // (Midget Miner hull, Quick Jump 5, two Robo-Midget Miners) - behavior-specs-10/
            // new-game-setup.md section 5a (`:51544`-`51549`) and 5b. Two ships are stationed in
            // AllocateHomeStarOrbitalInstallations.
            if (HasPotatoBugs(empire.Race))
            {
                Component midgetMinerHull = components.Fetch("Midget Miner");
                Component midgetRobot = components.Fetch("Robo-Midget Miner");

                ShipDesign potatoBug = new ShipDesign(empire.GetNextDesignKey());
                potatoBug.Blueprint = midgetMinerHull;
                foreach (HullModule module in potatoBug.Hull.Modules)
                {
                    if (module.ComponentType == "Engine")
                    {
                        module.AllocatedComponent = engine;
                        module.ComponentCount = 1;
                    }
                    else if (module.ComponentType == "Mining Robot")
                    {
                        module.AllocatedComponent = midgetRobot;
                        module.ComponentCount = 2;
                    }
                }
                potatoBug.Icon = new ShipIcon(midgetMinerHull.ImageFile, midgetMinerHull.ComponentImage);
                potatoBug.Type = ItemType.Ship;
                potatoBug.Name = PotatoBugDesignName;
                potatoBug.Update();
                empire.Designs[potatoBug.Key] = potatoBug;
            }

            // Last, the starting tech-upgrade pass over every starting ship design.
            ApplyStartingTechUpgrades(empire);
        }

        /// <summary>The Advanced Remote Mining starting miner's design name (template 15).</summary>
        public const string PotatoBugDesignName = "Potato Bug";

        /// <summary>
        /// A race with Advanced Remote Mining and without Only Basic Remote Mining starts with
        /// two Potato Bugs (behavior-specs-10/new-game-setup.md section 5a).
        /// </summary>
        public static bool HasPotatoBugs(Race race)
        {
            return race.HasTrait("ARM") && !race.HasTrait("OBRM");
        }

        /// <summary>
        /// The starting tech-upgrade candidates of behavior-specs-10/new-game-setup.md section 5b
        /// (`:51551`-`51664`): for each listed starting part, the replacements tried best first.
        /// Parts not listed (Settler's Delight, cloaks, computers, fuel tanks, mine dispensers,
        /// speed traps, Orbital Adjusters, colonisation modules, ...) are never replaced.
        /// </summary>
        private static readonly Dictionary<string, string[]> StartingUpgradeCandidates = BuildStartingUpgradeCandidates();

        private static Dictionary<string, string[]> BuildStartingUpgradeCandidates()
        {
            string[] engines = { "Radiating Hydro-Ram Scoop", "Alpha Drive 8", "Daddy Long Legs 7", "Fuel Mizer", "Long Hump 6" };
            string[] scanners = { "Possum Scanner", "Mole Scanner", "Rhino Scanner" };
            string[] shields = { "Wolverine Diffuse Shield", "Cow-hide Shield" };
            string[] armour = { "Carbonic Armor", "Crobmnium" };
            string[] beams = { "Yakimora Light Phaser", "X-Ray Laser" };
            string[] torpedoes = { "Beta Torpedo" };
            string[] bombs = { "Black Cat Bomb" };
            string[] robots = { "Robo-Miner", "Robo-Midget Miner" };

            return new Dictionary<string, string[]>
            {
                { "Quick Jump 5", engines },
                { "Bat Scanner", scanners },
                { "Rhino Scanner", scanners },
                { "Mole-skin Shield", shields },
                { "Cow-hide Shield", shields },
                { "Tritanium", armour },
                { "Crobmnium", armour },
                { "Laser", beams },
                { "X-Ray Laser", beams },
                { "Alpha Torpedo", torpedoes },
                { "Lady Finger Bomb", bombs },
                { "Robo-Midget Miner", robots },
                { "Robo-Mini Miner", robots },
            };
        }

        /// <summary>
        /// The starting tech-upgrade pass (behavior-specs-10/new-game-setup.md section 5b): every
        /// installed part of every starting ship design that appears in the candidate table is
        /// swapped for the first candidate the empire can build right now (its AvailableComponents:
        /// tech levels and trait gates, one-time gifts excluded); the quantity is kept, and the
        /// part stays when no candidate qualifies. The Radiating Hydro-Ram Scoop is skipped for a
        /// Colony-Ship-hulled design unless the race is radiation-immune or its radiation centre
        /// is above 84.
        /// </summary>
        /// <remarks>
        /// Only Ship designs are walked: the original's pass runs over the per-race ship-design
        /// array, and starbases come from a separate starbase template table. Does nothing when
        /// the empire has no AvailableComponents.
        /// </remarks>
        public static void ApplyStartingTechUpgrades(EmpireData empire)
        {
            if (empire.AvailableComponents == null)
            {
                return;
            }

            AllComponents components = new AllComponents();

            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Type != ItemType.Ship || design.Hull == null)
                {
                    continue;
                }

                bool changed = false;
                foreach (HullModule module in design.Hull.Modules)
                {
                    string candidate = StartingUpgradeFor(module.AllocatedComponent, design, empire);
                    if (candidate != null)
                    {
                        module.AllocatedComponent = components.Fetch(candidate);
                        changed = true;
                    }
                }

                if (changed)
                {
                    design.Update();
                }
            }
        }

        /// <summary>
        /// The replacement the starting tech-upgrade pass picks for one installed part, or null
        /// to keep it.
        /// </summary>
        public static string StartingUpgradeFor(Component installed, ShipDesign design, EmpireData empire)
        {
            if (installed == null || !StartingUpgradeCandidates.TryGetValue(installed.Name, out string[] candidates))
            {
                return null;
            }

            foreach (string candidate in candidates)
            {
                if (candidate == "Radiating Hydro-Ram Scoop" && design.Blueprint != null && design.Blueprint.Name == "Colony Ship"
                    && !empire.Race.RadiationTolerance.Immune && empire.Race.RadiationTolerance.OptimumLevel <= 84)
                {
                    continue;
                }

                if (empire.AvailableComponents.Contains(candidate))
                {
                    return candidate == installed.Name ? null : candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Rolls the home-world template - behavior-specs-10/new-game-setup.md section 3. The
        /// old "100-299" concentration and 300-499 surface rolls never existed: the original
        /// uses its first planet record as a template rolled like any ordinary planet (so its
        /// concentrations go through RollMineralConcentrations, high-radiation raise and
        /// low-concentration roll included), and that template alone gets surface minerals
        /// (RollHomeSurfaceStock). Here the template is a scratch Star that is never placed on
        /// the map, since this port's home stars are separate objects from GenerateStars' planets.
        /// </summary>
        private void PrepareResources()
        {
            Star template = new Star();
            RollEnvironment(template, random);
            RollMineralConcentrations(template, random);

            this.homeStarDefaultMineralConcentration = new Resources(template.MineralConcentration);

            this.homeStarDefaultSurfaceMinerals.Ironium = RollHomeSurfaceStock(template.MineralConcentration.Ironium, random);
            this.homeStarDefaultSurfaceMinerals.Boranium = RollHomeSurfaceStock(template.MineralConcentration.Boranium, random);
            this.homeStarDefaultSurfaceMinerals.Germanium = RollHomeSurfaceStock(template.MineralConcentration.Germanium, random);
        }

        /// <summary>Every home world's concentration floor (25 only in the built-in tutorial
        /// galaxy, which this port does not have) - behavior-specs-10/new-game-setup.md section 3.</summary>
        public const int HomeWorldConcentrationFloor = 30;

        /// <summary>The installations every home world starts with before the leftover-point
        /// bonus - behavior-specs-10/new-game-setup.md section 5b (`:50939`-`50950`).</summary>
        public const int HomeWorldStartingInstallations = 10;

        /// <summary>
        /// Copies the home-world template onto a home star: the template's surface stocks, and
        /// each concentration floored at 30 - behavior-specs-10/new-game-setup.md section 3
        /// (`FUN_1078_1334`, `:50967`-`50987`).
        /// </summary>
        public static void ApplyHomeWorldTemplate(Star star, Resources templateConcentration, Resources templateSurface)
        {
            star.ResourcesOnHand.Ironium = templateSurface.Ironium;
            star.ResourcesOnHand.Boranium = templateSurface.Boranium;
            star.ResourcesOnHand.Germanium = templateSurface.Germanium;

            star.MineralConcentration.Ironium = Math.Max(HomeWorldConcentrationFloor, templateConcentration.Ironium);
            star.MineralConcentration.Boranium = Math.Max(HomeWorldConcentrationFloor, templateConcentration.Boranium);
            star.MineralConcentration.Germanium = Math.Max(HomeWorldConcentrationFloor, templateConcentration.Germanium);
        }

        /// <summary>
        /// Packet Physics / Interstellar Traveler second home planet - behavior-specs-10/
        /// new-game-setup.md, "Starting population, exact order" step 4: the second planet gets
        /// 2/5 of the home planet's population (as it stands after steps 1-3) and the home planet
        /// is then reduced to 4/5 of it, each truncated, in units of 100 colonists.
        /// </summary>
        public static void SplitStartingPopulation(Star home, Star second, Race race, bool expertComputerPlayer = false)
        {
            int units = race.GetStartingPopulation(expertComputerPlayer) / 100;
            second.Colonists = (units * 2 / 5) * 100;
            home.Colonists = (units * 4 / 5) * 100;
        }

        /// <summary>The computer skill tier whose players get the +10% starting population: Expert (PlayerSettings.AiSkill 3, "a skill field above 2").</summary>
        public const int ExpertAiSkill = 3;

        /// <summary>
        /// This empire's slot settings, or null when the slot has none (hand-built states).
        /// </summary>
        private PlayerSettings SettingsOf(EmpireData empire)
        {
            foreach (PlayerSettings settings in serverState.AllPlayers)
            {
                if (settings.PlayerNumber == empire.Id)
                {
                    return settings;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether this empire's slot is a computer player: PlayerSettings.AiProgram is "Human"
        /// for a person (a null/empty value, as left by tests and older callers, is treated as
        /// human).
        /// </summary>
        private bool IsComputerPlayer(EmpireData empire)
        {
            PlayerSettings settings = SettingsOf(empire);
            return settings != null && !string.IsNullOrEmpty(settings.AiProgram) && settings.AiProgram != "Human";
        }

        /// <summary>
        /// Whether this empire's slot is a computer player at the Expert tier - the test of
        /// behavior-specs-11/new-game-setup.md "Starting population, exact order" step 2: the
        /// computer-player bit together with a skill field above 2 (PlayerSettings.AiSkill, -1
        /// when not recorded). A human slot with a skill recorded is still human.
        /// </summary>
        private bool IsExpertComputerPlayer(EmpireData empire)
        {
            PlayerSettings settings = SettingsOf(empire);
            return IsComputerPlayer(empire) && settings.AiSkill >= ExpertAiSkill;
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

                // The home-world flag (mining yield floor of 30, Star.IsHomeWorld).
                star.IsHomeWorld = true;

                star.Position.X = starPosition[0];
                star.Position.Y = starPosition[1];
                
                map.Homeworlds.Remove(starPosition);

                star.Name = nameGenerator.NextStarName;

                bool isComputerPlayer = IsComputerPlayer(empire);
                bool isExpertComputerPlayer = IsExpertComputerPlayer(empire);
                AllocateHomeStarResources(star, empire, isComputerPlayer, isExpertComputerPlayer);
                AllocateHomeStarOrbitalInstallations(star, empire, player);

                serverState.AllStars[star.Name] = star;

                // Packet Physics and Interstellar Traveler both start with a second planet
                // (behavior-specs-11/new-game-setup.md section 3, "The second planet's own
                // state", `:51420`-`51537`), only on a galaxy-size index of at least 1, i.e.
                // not Tiny (section 5a, `:51405`, `:51541`). This deliberately does NOT draw
                // from map.Homeworlds, which StarMapGenerator sizes to exactly numPlayers -
                // taking a second slot here would leave a later player with no home star at all
                // and hit the FatalError below. It takes one of the ordinary stars of
                // GenerateStars() instead, as the original does (see AllocateSecondPlanet).
                if (HasSecondHomePlanet(empire.Race, GameSettings.Data.GalaxySizeIndex))
                {
                    Star secondStar = ChooseSecondPlanet(star.Position, GameSettings.Data.MapWidth);
                    if (secondStar != null)
                    {
                        AllocateSecondPlanet(secondStar, star, empire, isExpertComputerPlayer);
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
        /// Packet Physics and Interstellar Traveler get a second home planet, but only when the
        /// galaxy-size index is at least 1 (not Tiny) - behavior-specs-10/new-game-setup.md
        /// section 5a.
        /// </summary>
        public static bool HasSecondHomePlanet(Race race, int galaxySizeIndex)
        {
            return (race.HasTrait("PP") || race.HasTrait("IT")) && galaxySizeIndex >= 1;
        }

        /// <summary>The second planet's candidate band, as hundredths of the galaxy diameter: 15 to 23 inclusive.</summary>
        public const int SecondPlanetBandInnerPercent = 15;
        public const int SecondPlanetBandOuterPercent = 23;

        /// <summary>
        /// The second planet's choice (behavior-specs-11/new-game-setup.md section 3, "The
        /// second planet's own state", step 1): among the planets still unowned, those at a
        /// squared distance from the home world between (15D / 100)^2 and (23D / 100)^2
        /// inclusive (D the galaxy diameter, divisions truncating) are the candidates and one
        /// is kept uniformly at random; with no candidate the nearest unowned planet is used.
        /// Null only when no planet is unowned at all.
        /// </summary>
        /// <remarks>
        /// D is the map width: the preset diameter when the wizard galaxy is in use, and the
        /// free map's width otherwise (the same reading GameSettings.GalaxySizeIndex uses).
        /// Candidates are taken in the galaxy's own planet order, so the uniform pick is
        /// reproducible from the seed.
        /// </remarks>
        private Star ChooseSecondPlanet(NovaPoint home, int diameter)
        {
            long inner = SecondPlanetBandInnerPercent * diameter / 100;
            long outer = SecondPlanetBandOuterPercent * diameter / 100;
            long innerSquared = inner * inner;
            long outerSquared = outer * outer;

            List<Star> candidates = new List<Star>();
            Star nearest = null;
            long nearestSquared = long.MaxValue;

            foreach (Star candidate in serverState.AllStars.Values)
            {
                if (candidate.Owner != Global.Nobody)
                {
                    continue;
                }

                long dx = candidate.Position.X - home.X;
                long dy = candidate.Position.Y - home.Y;
                long squared = (dx * dx) + (dy * dy);

                if (squared >= innerSquared && squared <= outerSquared)
                {
                    candidates.Add(candidate);
                }

                if (squared < nearestSquared)
                {
                    nearestSquared = squared;
                    nearest = candidate;
                }
            }

            if (candidates.Count > 0)
            {
                return candidates[random.Next(candidates.Count)];
            }

            return nearest;
        }

        /// <summary>The second planet's re-rolls before the home world's environment is copied instead.</summary>
        public const int SecondPlanetEnvironmentRerolls = 100;

        /// <summary>
        /// The second planet's environment (behavior-specs-11/new-game-setup.md section 3, "The
        /// second planet's own state", step 2; race-traits.md section 2a): while the race's
        /// habitability value for the planet (Race.HabPercent) is below 10, all three
        /// environment values (current and original copies) are re-rolled to 2 + a random 0-96,
        /// gravity, temperature then radiation; if a 100th re-roll would be needed, the home
        /// world's three values are copied instead - in that case even a passing 100th roll is
        /// overwritten. A planet that is habitable enough as rolled makes no draw.
        /// </summary>
        /// <remarks>
        /// Read as: up to 100 re-rolls are made; once the 100th has been made the home world's
        /// values are copied whatever it gave. Only the number of draws (replay) would differ
        /// under the other reading (99 re-rolls, then the copy).
        /// </remarks>
        public static void RollSecondPlanetEnvironment(Star second, Star home, Race race, Random random)
        {
            int rerolls = 0;
            while (race.HabPercent(second) < 10 && rerolls < SecondPlanetEnvironmentRerolls)
            {
                second.Gravity = 2 + random.Next(0, 97);
                second.Temperature = 2 + random.Next(0, 97);
                second.Radiation = 2 + random.Next(0, 97);
                rerolls++;
            }

            if (rerolls == SecondPlanetEnvironmentRerolls)
            {
                second.Gravity = home.Gravity;
                second.Temperature = home.Temperature;
                second.Radiation = home.Radiation;
            }

            second.OriginalGravity = second.Gravity;
            second.OriginalTemperature = second.Temperature;
            second.OriginalRadiation = second.Radiation;
        }

        /// <summary>The second planet's installations: 10 mines and 4 factories, defences left at 0.</summary>
        public const int SecondPlanetMines = 10;
        public const int SecondPlanetFactories = 4;

        /// <summary>
        /// The second planet's planetary scanner, "type 0": the first of the planetary scanner
        /// subtypes 0-8 (behavior-specs-11/production-queue.md 10d: Viewer 50, Viewer 90,
        /// Scoper 150, ...). Installed whatever the race can build.
        /// </summary>
        public const string SecondPlanetScanner = "Viewer 50";

        /// <summary>One of the second planet's surface stocks: 100 + a random 0-199 kT, no Accelerated BBS bonus.</summary>
        public static int RollSecondPlanetSurfaceStock(Random random)
        {
            return 100 + random.Next(0, 200);
        }

        /// <summary>
        /// Gives an ordinary star the second planet's own state (behavior-specs-11/
        /// new-game-setup.md section 3, "The second planet's own state", steps 2-7): the
        /// environment re-rolled until habitable (RollSecondPlanetEnvironment); the owner set;
        /// a starbase of the race's starbase design slot 1 (this port's small "Stargate" /
        /// "Mass Driver Base" design, the second starbase design PrepareDesigns creates); the
        /// artifact flag cleared; 10 mines, 4 factories and 0 defences; the planetary scanner
        /// type 0; 2/5 of the home world's starting population (the home world keeping 4/5);
        /// each surface stock 100 + a random 0-199 kT drawn independently (Ironium, Boranium,
        /// Germanium); its own rolled concentrations, with no floor of 30 and no leftover-point
        /// bonus (the home-world template is not copied); not a home world (Star.IsHomeWorld
        /// stays false, so the concentration-30 mining floor does not apply); and one ship of
        /// the race's design slot 0 as a new one-ship fleet.
        /// </summary>
        /// <remarks>
        /// The "three-bit field of the packet-destination word set to 1" has no identified
        /// meaning and no counterpart here (spec gap). Fleet names: this port numbers starting
        /// fleets per design ("Scout #1", "Santa Maria #1"...), not with the original's
        /// per-owner fleet number, so the extra ship is "&lt;design&gt; #&lt;next number of that
        /// design&gt;" - a Packet Physics race's "Shielded Scout #3", an Interstellar Traveler's
        /// "Scout #2".
        /// </remarks>
        private void AllocateSecondPlanet(Star second, Star home, EmpireData empire, bool isExpertComputerPlayer)
        {
            RollSecondPlanetEnvironment(second, home, empire.Race, random);

            second.Owner = empire.Id;
            second.ThisRace = empire.Race;
            second.EnergyTechLevel = empire.ResearchLevels[TechLevel.ResearchField.Energy];
            second.IsHomeWorld = false;
            second.HasArtifact = false;

            second.Mines = SecondPlanetMines;
            second.Factories = SecondPlanetFactories;
            second.Defenses = 0;

            second.ScannerType = SecondPlanetScanner;
            Component scanner = new AllComponents().Fetch(SecondPlanetScanner);
            Scanner scannerProperty = scanner != null && scanner.Properties.ContainsKey("Scanner") ? scanner.Properties["Scanner"] as Scanner : null;
            if (scannerProperty != null)
            {
                // As StarUpdateStep ranges a newly installed planetary scanner.
                second.ScanRange = empire.Race.HasTrait("NAS") ? scannerProperty.NormalScan * 2 : scannerProperty.NormalScan;
            }

            // 2/5 to the second planet, 4/5 left on the home planet.
            SplitStartingPopulation(home, second, empire.Race, isExpertComputerPlayer);

            second.ResourcesOnHand.Ironium = RollSecondPlanetSurfaceStock(random);
            second.ResourcesOnHand.Boranium = RollSecondPlanetSurfaceStock(random);
            second.ResourcesOnHand.Germanium = RollSecondPlanetSurfaceStock(random);

            home.ResourcesOnHand.Energy = home.GetResourceRate();
            second.ResourcesOnHand.Energy = second.GetResourceRate();

            string secondBaseDesignName = empire.Race.HasTrait("IT") ? "Stargate" : "Mass Driver Base";
            AllocateStarbase(second, empire, secondBaseDesignName);

            ShipDesign slotZero = SlotZeroShipDesign(empire);
            if (slotZero != null)
            {
                AddShipFleet(second, empire, slotZero, NextFleetName(empire, slotZero));
            }
        }

        /// <summary>
        /// "&lt;design&gt; #n", n being one more than the number of this empire's fleets led by
        /// that design (the home world's starting fleets of the same design, whatever they were
        /// named: this port names a Packet Physics race's two Shielded Scouts "Scout #1/#2").
        /// </summary>
        private static string NextFleetName(EmpireData empire, ShipDesign design)
        {
            int existing = 0;
            foreach (Fleet fleet in empire.OwnedFleets.Values)
            {
                foreach (ShipToken token in fleet.Composition.Values)
                {
                    if (token.Design != null && token.Design.Key == design.Key)
                    {
                        existing++;
                        break;
                    }
                }
            }

            return design.Name + " #" + (existing + 1);
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

            // Any PRT with Advanced Remote Mining and not Only Basic Remote Mining: two Potato
            // Bugs (behavior-specs-10/new-game-setup.md section 5a, one design-creating call
            // plus one repeat).
            ShipDesign potatoBug = FindDesign(empire, PotatoBugDesignName);
            if (potatoBug != null)
            {
                AddShipFleet(star, empire, potatoBug, PotatoBugDesignName + " #1");
                AddShipFleet(star, empire, potatoBug, PotatoBugDesignName + " #2");
            }
        }

        /// <summary>
        /// The empire's design slot 0: its first-created ship design (starbase designs live in a
        /// separate table in the original). PrepareDesigns creates the scout pass first, so this
        /// is the race's first scout (behavior-specs-10/new-game-setup.md section 5a). Null when
        /// the empire has no ship design.
        /// </summary>
        public static ShipDesign SlotZeroShipDesign(EmpireData empire)
        {
            ShipDesign first = null;
            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Type == ItemType.Ship && (first == null || design.Key < first.Key))
                {
                    first = design;
                }
            }
            return first;
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
        private void AllocateHomeStarResources(Star star, EmpireData empire, bool isComputerPlayer, bool isExpertComputerPlayer)
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

            // behavior-specs-11/new-game-setup.md "Starting population, exact order": an
            // Expert-tier computer player gets +10% (step 2) before Accelerated BBS (step 3).
            star.Colonists = empire.Race.GetStartingPopulation(isExpertComputerPlayer);

            // The shared template's surface stocks and concentrations floored at 30, then 10
            // mines, 10 factories and 10 defences (behavior-specs-10/new-game-setup.md sections
            // 3 and 5b) - defences were previously never set at all.
            ApplyHomeWorldTemplate(star, this.homeStarDefaultMineralConcentration, this.homeStarDefaultSurfaceMinerals);
            star.Mines = HomeWorldStartingInstallations;
            star.Factories = HomeWorldStartingInstallations;
            star.Defenses = HomeWorldStartingInstallations;

            if (empire.Race.HasTrait("AR"))
            {
                // An Alternate Reality home world starts without a planetary scanner (its
                // five-bit scanner field is put back to "none", behavior-specs-10/
                // new-game-setup.md section 5b, `:51666`-`51670`). Its innate scan range is
                // population-derived, as StarUpdateStep recomputes every turn.
                star.ScannerType = "None";
                star.ScanRange = (int)Math.Sqrt(star.Colonists / 10.0);
            }
            else
            {
                star.ScannerType = "Scoper 150"; // TODO (priority 4) get from component list
                star.ScanRange = empire.Race.HasTrait("NAS") ? 100 : 50; // TODO (priority 4) get from component list
            }
            star.DefenseType = "SDI"; // TODO (priority 4) get from component list

            // Leftover points (and, for Alternate Reality, zeroing the installations).
            HomeStarLeftoverpointsAdjuster.Adjust(star, empire.Race, isComputerPlayer);

            star.ResourcesOnHand.Energy = star.GetResourceRate();
        }
    }
}
