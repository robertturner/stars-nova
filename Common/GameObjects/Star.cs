#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010, 2011 The Stars-Nova Project
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

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// This object represents a Star system, the basic unit of stars-nova settlement/expansion.
    /// </summary>
    [Serializable]
    public class Star : Mappable
    {
        public bool HasFleetsInOrbit;
        public ProductionQueue ManufacturingQueue;
        public Resources MineralConcentration;
        public Resources ResourcesOnHand;

        /// <summary>
        /// kT mined so far toward the next 1-point drop in concentration, per mineral. Tracked
        /// across turns so partial progress isn't lost/re-derived each year (see Mine()).
        /// </summary>
        public Resources MineralMiningProgress;
        public Fleet Starbase;

        /// <summary>
        /// Colonization attempts registered against this star THIS turn (see ColoniseTask),
        /// awaiting resolution once every fleet has had a chance to arrive - behavior-specs-7/
        /// turn-generation-engine.md §4's "simultaneous colonization tie-break" only makes sense
        /// once every contender for this turn is known, not resolved on a first-fleet-wins basis
        /// as each fleet happens to be processed. Transient, per-turn scratch state - not
        /// persisted, always empty again by the time a turn file is written.
        /// </summary>
        public List<ColonizationAttempt> PendingColonizations = new List<ColonizationAttempt>();

        /// <summary>
        /// The number of colonists as reported on a planet. Divide by GlobalDefinitions.ColonistsPerKiloton to convert to cargo units.
        /// </summary>
        public int Colonists;
        private int defenses;
        public bool OnlyLeftover = false;

        public int Factories;
        public int Mines;
        public int ResearchAllocation;

        /// <summary>
        /// A player's home world (planet byte 5 bit 0x4 in the original), set once at game
        /// creation and kept for the life of the game whoever owns the planet. Its own mines (and
        /// any mining of it while an Alternate Reality race owns it) yield as if each concentration
        /// were at least <see cref="HomeWorldMiningConcentrationFloor"/> - behavior-specs-10/
        /// population-growth.md section 5 and new-game-setup.md section 3 (`FUN_1028_3a74`,
        /// `:13544`-`13546`).
        /// </summary>
        public bool IsHomeWorld;

        /// <summary>The concentration a home world's mining yield never falls below (the stored
        /// concentration itself still falls toward 1).</summary>
        public const int HomeWorldMiningConcentrationFloor = 30;

        /// <summary>
        /// Ultimate Recycling's per-planet recycled-resources accumulator d
        /// (behavior-specs-9/production-queue.md 10g, "Resource funding"): a 16-bit word filled
        /// by ScrapTask (ScrapFleetStep, before production) when the planet's OWNER has UR, and
        /// read once by StarUpdateStep's production pass of the SAME turn generation, which
        /// blends it as r + d x r / (d + r) (ScrapTask.BlendRecycledResources). Transient: it
        /// lives for one turn generation only, is zeroed by ScrapFleetStep and after use, and is
        /// never saved (the superseded "DeferredScrapResources" next-turn credit is ignored when
        /// an old file is loaded).
        /// </summary>
        public int RecycledScrapResources;

        /// <summary>
        /// The planet-artifact flag (behavior-specs-10/turn-generation-engine.md §11, planet byte
        /// 0x1a bit 0x40): the next race to take the planet over finds an alien artifact worth a
        /// research bounty, and the flag is cleared (PlanetArtifactStep). Server-side only; saved
        /// only when set.
        /// </summary>
        public bool HasArtifact;

        public int ScanRange;
        public string DefenseType = "None";
        public string ScannerType = "None";

        /// <summary>
        /// The planet's mineral-packet destination (a planet name; null or empty = none), set by
        /// its owner when the starbase has a mass driver (PacketDestinationCommand;
        /// behavior-specs-10/production-queue.md §10a/§10b, fleet-movement-scanning-cargo.md
        /// "planet orders"). A packet order buys nothing without one.
        /// </summary>
        public string PacketDestination;

        /// <summary>
        /// The planet's chosen packet speed (warp; 0 = not chosen). Used when it lies in
        /// 5..(best driver warp + 3), otherwise the launch rating (production-queue.md §10b).
        /// </summary>
        public int PacketWarp;

        // The following values are percentages of the permissable range of each
        // environment parameter (between 0 and 100).

        public int Gravity = 0;
        public int Radiation = 0;
        public int Temperature = 0;
        public int OriginalGravity = 0;
        public int OriginalRadiation = 0;
        public int OriginalTemperature = 0;

        /// <summary>
        /// A reference to the race information for the owner of this star.
        /// This is a convenience for the server. It will be null for races other than the player's race in the client.
        /// </summary>
        public Race ThisRace = null;

        /// <summary>
        /// The owning empire's current Energy tech level, kept in sync alongside ThisRace
        /// wherever that is (re-)linked. Needed for Alternate Reality's distinct resource-rate
        /// and scan-range formulas (see GetResourceRate()), since a Star has no other access to
        /// its owner's live research state. Not itself persisted; recomputed on load like
        /// ThisRace is.
        /// </summary>
        public int EnergyTechLevel = 0;

        private HashSet<IStarObserver> observerList = new HashSet<IStarObserver>();

        /// <summary>
        /// Default constructor.
        /// </summary>
        public Star()
        {
            this.Starbase = null;
            this.ManufacturingQueue = new ProductionQueue();
            this.MineralConcentration = new Resources();
            this.ResourcesOnHand = new Resources();
            this.MineralMiningProgress = new Resources();
            Type = ItemType.Star;
        }

        /// <summary>
        /// Shared operable-count formula for factories and mines. behavior-specs-8/production-
        /// queue.md §3: the race's per-10,000 setting is multiplied by the population (in units of
        /// 100 colonists) and divided by 100 BEFORE flooring - the earlier "whole 10,000-colonist
        /// blocks" reading quantised too coarsely - then floored at 1 and clamped to the separate,
        /// maximum-population-based build cap. Alternate Reality gets 0 from every one of these
        /// routines (it can neither build nor operate mines, factories or defenses).
        /// </summary>
        private int OperableBuildings(int colonists, int perTenThousandSetting)
        {
            if (ThisRace == null || ThisRace.HasTrait("AR") || colonists <= 0)
            {
                // ThisRace == null: see the original note on GetOperableFactories - returning zero
                // for an unowned star is more graceful than the exception it used to raise.
                return 0;
            }

            int populationUnits = colonists / 100;
            int operable = populationUnits * perTenThousandSetting / 100;

            return Math.Min(BuildCap(perTenThousandSetting), Math.Max(1, operable));
        }

        /// <summary>
        /// How many mines or factories a planet may CONTAIN: limited by its MAXIMUM population, not
        /// its current one (behavior-specs-8/production-queue.md §3, "Build cap"), floored at 10.
        /// At 100% habitability and the default setting this is 1,000.
        /// </summary>
        private int BuildCap(int perTenThousandSetting)
        {
            if (ThisRace == null || ThisRace.HasTrait("AR"))
            {
                return 0;
            }

            int maximumPopulationUnits = (int)(CapacityColonists(ThisRace) / 100);
            return Math.Max(10, maximumPopulationUnits * perTenThousandSetting / 100);
        }

        /// <summary>The most factories this planet may contain (see BuildCap).</summary>
        public int GetBuildCapFactories()
        {
            return ThisRace == null ? 0 : BuildCap(ThisRace.OperableFactories);
        }

        /// <summary>The most mines this planet may contain (see BuildCap).</summary>
        public int GetBuildCapMines()
        {
            return ThisRace == null ? 0 : BuildCap(ThisRace.OperableMines);
        }

        /// <summary>
        /// Determine the number of factories that can be operated.
        /// </summary>
        /// <returns>The number of factories that can be operated.</returns>
        public int GetOperableFactories()
        {
            if (ThisRace == null)
            {
                return 0;
            }

            return OperableBuildings(Colonists, ThisRace.OperableFactories);
        }
        
        /// <summary>
        /// Determine the number of factories that can be operated next turn
        /// considering growth.
        /// </summary>
        /// <returns>The number of factories that can be operated next turn.</returns>
        public int GetFutureOperableFactories()
        {
            if (ThisRace == null)
            {
                return 0;
            }
            
            int expectedGrowth = CalculateGrowth(ThisRace);
            
            return OperableBuildings(Colonists + expectedGrowth, ThisRace.OperableFactories);
        }

        /// <summary>
        /// Calculate the number of mines that can be operated.
        /// </summary>
        /// <returns>The number of mines that can be operated.</returns>
        public int GetOperableMines()
        {            
            if (ThisRace == null)
            {
                return 0;
            }

            return OperableBuildings(Colonists, ThisRace.OperableMines);
        }
        
        /// <summary>
        /// Determine the number of mines that can be operated next turn
        /// considering growth.
        /// </summary>
        /// <returns>The number of mines that can be operated next turn.</returns>
        public int GetFutureOperableMines()
        {
            if (ThisRace == null)
            {
                return 0;
            }
            
            int expectedGrowth = CalculateGrowth(ThisRace);
            
            return OperableBuildings(Colonists + expectedGrowth, ThisRace.OperableMines);
        }

        /// <summary>
        /// The most defenses this planet may contain: four times its habitability value in percent,
        /// clamped to 10..100 (0 for Alternate Reality). A planet at 25% habitability or better can
        /// hold the full 100; a poorer one is held to four times its value, never below 10 (which is
        /// also the cap for planets with zero or negative value). behavior-specs-8/production-
        /// queue.md §3.
        /// </summary>
        public int GetMaxDefenses()
        {
            if (ThisRace == null || ThisRace.HasTrait("AR"))
            {
                return 0;
            }

            // The integer -45..100 evaluator itself, not a fraction scaled back up (FUN_1048_4ed0
            // multiplies FUN_1048_490e's result by 4; population-growth.md section 2).
            int habitabilityPercent = ThisRace.HabPercent(this);
            return Math.Max(10, Math.Min(Global.MaxDefenses, 4 * habitabilityPercent));
        }

        /// <summary>
        /// How many defenses the given colonist count can operate: one per 2,500 colonists, rounded
        /// up, at most the planet's defense cap (production-queue.md §3).
        /// </summary>
        private int OperableDefenses(int colonists)
        {
            if (ThisRace == null || colonists <= 0)
            {
                return 0;
            }

            return Math.Min(GetMaxDefenses(), (colonists + 2499) / 2500);
        }

        /// <summary>Defenses the planet's current colonists can operate.</summary>
        public int GetOperableDefenses()
        {
            return OperableDefenses(Colonists);
        }

        /// <summary>Defenses next year's projected population can operate.</summary>
        public int GetFutureOperableDefenses()
        {
            return ThisRace == null ? 0 : OperableDefenses(Colonists + CalculateGrowth(ThisRace));
        }
        
        /// <summary>
        /// The planet reset a completed Genesis Device performs (behavior-specs-8/production-queue.md
        /// section 10c). Except for Alternate Reality owners (who have no mines, factories or
        /// defenses to lose), it zeroes the mine, factory and defense counts and removes the
        /// planetary scanner. For every owner it then zeroes the three mineral stockpiles, re-rolls
        /// the three mineral CONCENTRATIONS (each 25 plus two independent draws of 0-39, so 25-103)
        /// and re-rolls the three ENVIRONMENT values (each 1 plus two independent draws of 0-49, so
        /// 1-99). Population, ownership, resources on hand and any starbase are untouched.
        /// </summary>
        /// <remarks>
        /// The spec says the environment values are re-rolled "current and original" without saying
        /// whether the two copies share one draw; they are set equal here so a reborn planet has no
        /// terraforming history (its full terraform headroom is available again).
        /// </remarks>
        public void ApplyGenesisDevice(Random random)
        {
            if (ThisRace == null || !ThisRace.HasTrait("AR"))
            {
                Factories = 0;
                Mines = 0;
                Defenses = 0;
                ScannerType = "None";
            }

            ResourcesOnHand.Ironium = 0;
            ResourcesOnHand.Boranium = 0;
            ResourcesOnHand.Germanium = 0;

            MineralConcentration.Ironium = 25 + random.Next(0, 40) + random.Next(0, 40);
            MineralConcentration.Boranium = 25 + random.Next(0, 40) + random.Next(0, 40);
            MineralConcentration.Germanium = 25 + random.Next(0, 40) + random.Next(0, 40);

            Radiation = OriginalRadiation = 1 + random.Next(0, 50) + random.Next(0, 50);
            Gravity = OriginalGravity = 1 + random.Next(0, 50) + random.Next(0, 50);
            Temperature = OriginalTemperature = 1 + random.Next(0, 50) + random.Next(0, 50);
        }

        /// <summary>
        /// Calculate the number of factories currently operated.
        /// </summary>
        /// <returns>The number of factories currently in operation.</returns>
        public int GetFactoriesInUse()
        {
            int potentialFactories = GetOperableFactories();
            return Math.Min(Factories, potentialFactories);
        }
        
        /// <summary>
        /// Calculate the number of mines currently operated.
        /// </summary>
        /// <returns>The number of mines currently in operated.</returns>
        public int GetMinesInUse()
        {
            if (ThisRace != null && ThisRace.HasTrait("AR"))
            {
                return AlternateRealityInnateMines(Colonists);
            }

            int potentialMines = GetOperableMines();
            return Math.Min(Mines, potentialMines);
        }

        /// <summary>
        /// Alternate Reality cannot build or operate mines, but its planets still mine: the effective
        /// mine count is floor(sqrt(population in whole units of 100 colonists)), ignoring the built
        /// mines and the race's operable setting - behavior-specs-10/race-traits.md, "Alternate
        /// Reality innate mining, exact" (`FUN_1048_4cce`, the square root taken with truncation).
        /// 2,400 colonists give 4 mines, 57,600 give 24, and under 100 colonists give 0 (there is no
        /// minimum of 1).
        /// </summary>
        private static int AlternateRealityInnateMines(int colonists)
        {
            if (colonists < 100)
            {
                return 0;
            }

            return (int)Math.Sqrt(colonists / 100);
        }

        /// <summary>
        /// The per-10-mines output setting the planet's own mines use: the race's setting, except
        /// that Alternate Reality uses a fixed 10 (race-traits.md, "Alternate Reality innate mining,
        /// exact": "for an Alternate Reality race that setting is replaced by a fixed 10").
        /// </summary>
        private int OwnMinesOutputSetting()
        {
            if (ThisRace == null)
            {
                return 0;
            }

            return ThisRace.HasTrait("AR") ? 10 : ThisRace.MineProductionRate;
        }

        /// <summary>
        /// The concentration the planet's OWN mines yield at: the stored value, but at least 30 on a
        /// home world (population-growth.md section 5: "On a home world ... the yield also uses at
        /// least 30 for the planet's own mines"). The stored concentration keeps falling toward 1.
        /// </summary>
        private int OwnMinesYieldConcentration(int concentration)
        {
            return IsHomeWorld ? Math.Max(concentration, HomeWorldMiningConcentrationFloor) : concentration;
        }

        /// <summary>
        /// The concentration floor an orbiting remote-mining fleet's yield uses at this planet: 30
        /// on a home world owned by an Alternate Reality race ("and for any mining when the owner is
        /// Alternate Reality", population-growth.md section 5; new-game-setup.md section 3: "any
        /// mining of a planet owned by an Alternate Reality race"), otherwise 0 (no floor). Pass
        /// it to <see cref="MineForFleet(int, ref int, ref int, int, int)"/>.
        /// </summary>
        public int RemoteMiningYieldConcentrationFloor()
        {
            return IsHomeWorld && ThisRace != null && ThisRace.HasTrait("AR") ? HomeWorldMiningConcentrationFloor : 0;
        }

        /// <summary>
        /// Raw (unrounded) kT one application of the planet's own mines produces: operating mines x
        /// output setting x concentration / 1,000 (population-growth.md section 5: "mine-equivalents
        /// x concentration x output setting / 1,000"). No per-10-mines truncation: 24 innate mines at
        /// concentration 115 yield 27.6 kT (turn-generation-engine.md section 6 live test).
        /// </summary>
        private double RawOwnMinesYield(int minesInUse, int concentration)
        {
            return (double)minesInUse * OwnMinesOutputSetting() * OwnMinesYieldConcentration(concentration) / 1000.0;
        }
        
        /// <summary>
        /// Calculate the amount of resources currently generated.
        /// </summary>
        /// <returns>The resources generated.</returns>
        public int GetResourceRate()
        {
            if (ThisRace == null || Colonists <= 0)
            {
                return 0;
            }

            if (ThisRace.HasTrait("AR"))
            {
                return GetAlternateRealityResourceRate();
            }

            int factoriesInUse = GetFactoriesInUse();

            // The population part counts the capacity-adjusted population (ResourcePopulationUnits).
            int rate = ThisRace.ColonistsPerResource > 0 ? ResourcePopulationUnits() * 100 / ThisRace.ColonistsPerResource : 0;
            rate += (int)(((double)factoriesInUse / Global.FactoriesPerFactoryProductionUnit) * ThisRace.FactoryProduction);

            // "A result of 0 is raised to 1. This shared ending applies to both branches"
            // (behavior-specs-10/race-traits.md section 5a step 6).
            return Math.Max(1, rate);
        }

        /// <summary>
        /// The population, in units of 100 colonists, the resource routine counts
        /// (behavior-specs-10/race-traits.md section 5a step 3, FUN_1048_4faa, every PRT): above
        /// the planet's capacity C, colonists count at half weight, C + (P - C) / 2, and nothing
        /// counts beyond 2C. A planet whose capacity is 0 (Alternate Reality without a starbase)
        /// counts nobody.
        /// </summary>
        /// <remarks>
        /// Ambiguity: the capacity of a non-AR planet with zero or negative habitability is not
        /// given by the spec (Nova's CapacityColonists uses a flat stand-in there), so the
        /// adjustment is applied only to Alternate Reality planets and planets of positive value.
        /// </remarks>
        private int ResourcePopulationUnits()
        {
            int units = Colonists / 100;
            if (ThisRace.HasTrait("AR") || ThisRace.HabPercent(this) > 0)
            {
                int capacityUnits = (int)(CapacityColonists(ThisRace) / 100);
                if (units > capacityUnits)
                {
                    units = Math.Min(capacityUnits + ((units - capacityUnits) / 2), 2 * capacityUnits);
                }
            }

            return units;
        }

        /// <summary>
        /// Alternate Reality's resources, exactly as behavior-specs-10/race-traits.md section 5a
        /// reads FUN_1048_4faa (live-validated 12 of 12): with P the capacity-adjusted population
        /// in units of 100, c the race's economic field 0 (the colonists-per-resource slot, which
        /// an AR race uses as its unscaled efficiency coefficient, 7-25), E the Energy tech level
        /// floored at 1 and H the planet value in percent floored at 25:
        /// floor(sqrt(P / c x E) x H x 0.1 + 0.999), and at least 1. (This used the factory-output
        /// slot as c, raw colonists, no floors and plain truncation.)
        /// </summary>
        private int GetAlternateRealityResourceRate()
        {
            int coefficient = Math.Max(1, ThisRace.ColonistsPerResource / 100);
            int energy = Math.Max(1, EnergyTechLevel);
            int planetValue = Math.Max(25, ThisRace.HabPercent(this));

            double root = Math.Sqrt((double)ResourcePopulationUnits() / coefficient * energy);
            int rate = (int)((root * planetValue * 0.1) + 0.999);
            return Math.Max(1, rate);
        }
        
        /// <summary>
        /// Calculate the amount of resources generated next turn accounting growth and
        /// factory production.
        /// </summary>
        /// <returns>The resources generated next turn.</returns>
        public int GetFutureResourceRate(int extraFactories)
        {
            if (ThisRace == null || Colonists <= 0)
            {
                return 0;
            }
            
            int potentialFactories = GetFutureOperableFactories();
            int expectedGrowth = CalculateGrowth(ThisRace);
            int factoriesInUse = Math.Min(Factories + extraFactories, potentialFactories);
            
            int rate = (int)(((double)Colonists + expectedGrowth) / ThisRace.ColonistsPerResource);
            rate += (int)(((double)factoriesInUse / Global.FactoriesPerFactoryProductionUnit) * ThisRace.FactoryProduction);
            
            return rate;
        }
        
        /// <summary>
        /// Calculate the amount of kT of minerals that can currently be mined.
        /// </summary>
        /// <returns>The mining rate in kT.</returns>
        public int GetMiningRate(int concentration)
        {
            if (ThisRace == null)
            {
                return 0;
            }
             
            int minesInUse = GetMinesInUse();

            // docs/behavior-specs-4/population-growth.md confirms the exported client applies
            // unbiased stochastic rounding here rather than truncating (a true value of X.37 kT
            // rounds up 37% of the time) - Global.StochasticRound replaces the previous plain
            // (int) truncation, which always rounded down and so systematically under-mined.
            // The mine count is no longer integer-divided by 10 first (which dropped up to 9
            // mines' output) - see RawOwnMinesYield.
            return Global.StochasticRound(RawOwnMinesYield(minesInUse, concentration));
        }
        
        /// <summary>
        /// Calculate the amount of kT of minerals that can be mined considering additional
        /// mines, for example in production ones.
        /// </summary>
        /// <returns>The potential mining rate in kT.</returns>
        public int GetFutureMiningRate(int concentration, int extraMines)
        {  
            if (ThisRace == null)
            {
                return 0;
            }
            
            int potentialMines = GetFutureOperableMines();
            int minesInUse = Math.Min(Mines + extraMines, potentialMines);
            if (ThisRace.HasTrait("AR"))
            {
                minesInUse = AlternateRealityInnateMines(Colonists + CalculateGrowth(ThisRace));
            }
            
            // A preview: truncated rather than stochastically rounded.
            return (int)RawOwnMinesYield(minesInUse, concentration);
        }
        
        /// <summary>
        /// The planet's maximum supportable population, in raw colonists, for the given race.
        /// </summary>
        /// <remarks>
        /// race.MaxPopulation already applies the Hyper Expansion/Jack Of All Trades/Inner
        /// Strength population-cap multipliers (checking trait codes "HE"/"JOAT"/"IS"), so they
        /// are not reapplied here. behavior-specs-7/population-growth.md confirms this nominal
        /// figure (1,000,000 for a standard race) is then scaled by the race's habitability value
        /// for this star - a 50%-habitability world supports only half as many colonists as a
        /// 100%-habitability one, not the same flat maximum.
        /// </remarks>
        public double CapacityColonists(Race race)
        {
            if (race.HasTrait("AR"))
            {
                return AlternateRealityCapacity(race);
            }

            int habPercent = race.HabPercent(this);

            // handle negative (or exactly zero) hab worlds - no positive scale to derive a
            // sensible capacity from, so fall back to a flat ceiling.
            if (habPercent <= 0)
            {
                return 25000.0;
            }

            // Whole-percent habitability (population-growth.md section 2) times the maximum,
            // computed as percent x max / 100 so it stays exact (0.29 x 1,000,000 in floating
            // point is not).
            return habPercent * (double)race.MaxPopulation / 100.0;
        }

        /// <summary>
        /// Alternate Reality's maximum population does not come from habitability at all: it is fixed
        /// by the starbase chassis currently orbiting the planet. behavior-specs-9/population-growth.md
        /// section 3 (`FUN_1048_4a8e`): the table is in units of 100 colonists, so Orbital Fort
        /// 250,000, Space Dock 500,000, Space Station 1,000,000, Ultra Station 2,000,000, Death Star
        /// 3,000,000 colonists (live-confirmed: an AR homeworld on a Space Station grew 25,000 ->
        /// 57,700). A planet with NO starbase (status bit 0x2 = "a starbase orbits this planet") has
        /// capacity 0. The +10% bonus of the lesser-trait bit 9 (Only Basic Remote Mining) applies
        /// last, on top, as it does for every race.
        /// </summary>
        /// <remarks>
        /// A won Alternate Reality colonisation installs the "Starter Colony" starbase (Orbital Fort
        /// hull) so a new colony has capacity from its first turn - see ServerState/StarterColony.cs.
        /// The original divides by zero for an owned AR planet with no starbase at all (not
        /// live-tested); here growth treats a zero capacity as "supports nobody" (Star.Capacity /
        /// CalculateGrowth guard it).
        /// </remarks>
        private double AlternateRealityCapacity(Race race)
        {
            double capacity = 0;

            if (Starbase != null && Starbase.Owner == Owner && Starbase.Composition.Count > 0)
            {
                ShipDesign baseDesign = Starbase.Composition.Values.First().Design;
                string chassis = baseDesign.Blueprint == null ? null : baseDesign.Blueprint.Name;

                switch (chassis)
                {
                    case "Orbital Fort": capacity = 250000; break;
                    case "Space Dock": capacity = 500000; break;
                    case "Space Station": capacity = 1000000; break;
                    case "Ultra Station": capacity = 2000000; break;
                    case "Death Star": capacity = 3000000; break;
                }
            }

            if (race.HasTrait("OBRM"))
            {
                capacity += (int)capacity / 10;
            }

            return capacity;
        }

        /// <summary>
        /// Calculate the utilized capacity (as a percentage).
        /// </summary>
        /// <param name="race"></param>
        /// <returns>Capacity in the range 1 - 100 (%); 100 when the planet can support nobody at all.</returns>
        public int Capacity(Race race)
        {
            double maximum = CapacityColonists(race);
            if (maximum <= 0)
            {
                return Colonists > 0 ? 100 : 0;
            }

            double capacity = (Colonists / maximum) * 100;

            return (int)Math.Ceiling(capacity);
        }
        
        /// <summary>
        /// Calculates the growth for the star.
        /// </summary>
        /// <param name="race"></param>
        /// <returns>The amount of colonists the star will gain on update.</returns>
        /// <remarks>
        /// See Update().
        /// </remarks>
        public int CalculateGrowth(Race race)
        {
            int finalGrowth = GrowthColonists(RawGrowth(race), race.HabValue(this));
            finalGrowth /= 100;
            finalGrowth *= 100;

            return finalGrowth;
        }

        /// <summary>The year's population change in colonists as a real number, before any
        /// rounding - see <see cref="CalculateGrowth"/> and <see cref="GrowthWithCarry"/>.</summary>
        private double RawGrowth(Race race)
        {
            double habitalValue = race.HabValue(this);
            double growthRate = race.GrowthRate;

            if (race.HasTrait("HE"))
            {
                growthRate *= Global.GrowthFactorHyperExpansion;
            }

            double populationGrowth = 0;

            // Where the population stands against the planet's capacity, compared the way the
            // original does (behavior-specs-10/population-growth.md section 3): no crowding at a
            // quarter of capacity or less; above that and below capacity the rate is multiplied by
            // (1000 - the capacity fraction in whole thousandths, rounded down)^2 / 562,500; at or
            // above capacity the over-capacity rule. This used Capacity()'s ceiling-rounded whole
            // percent, which over-crowded (33.46% was taken as 34%, about 3% less growth) and sent
            // a planet within 1% of its capacity to the zero-growth band.
            double maximum = CapacityColonists(race);

            if (habitalValue < 0.0)
            {
                // negative hab planet
                populationGrowth = 0.1 * this.Colonists * habitalValue;
            }
            else if (maximum > 0 && Colonists * 4.0 <= maximum)
            {
                // low pop planet - docs/behavior-specs-4/population-growth.md §3's own formula is
                // an unconditional "capPct <= 0.25" for the no-crowding case (matching
                // starsfaq.com's sourced formula exactly), not "< 0.25" - the previous strict
                // inequality left capacity == 0.25 EXACTLY matching neither this branch nor the
                // next one below, falling all the way through to the "full planet" branch and
                // silently zeroing growth outright. Confirmed as a real, live bug from a reported
                // save: Capacity() rounds up to a whole percentage (Math.Ceiling), so a colony
                // whose true capacity was ~24.4% still rounded to exactly 25 and permanently
                // stopped growing turn after turn.
                populationGrowth = Colonists * growthRate / 100.0 * habitalValue;
            }
            else if (maximum > 0 && Colonists < maximum)
            {
                // early crowding: (1000 - t)^2 / 562,500 is exactly 16/9 x (1 - t/1000)^2.
                long thousandths = (long)(Colonists * 1000.0 / maximum);
                populationGrowth = Colonists * growthRate / 100.0 * habitalValue;
                populationGrowth *= (1000 - thousandths) * (1000 - thousandths) / 562500.0;
            }
            else // capacity >= 1.0: full or over-full planet
            {
                // behavior-specs-7/population-growth.md supersedes the earlier "plateaus at
                // capacity" finding: population from exactly at capacity up to 9 colonists over
                // it still produces flat zero growth, but at capacity+10 or more the planet
                // actually DECLINES, at a rate that ramps smoothly from negligible (just past the
                // threshold) up to a hard-clamped -12%/turn (reached around 4x capacity) - traced
                // directly from the decompiled crowding block, not merely inferred.
                double rawCapacity = CapacityColonists(race);

                // The dead band is 10 population UNITS (1 unit = 100 colonists): zero change up to
                // 999 colonists over capacity, decline from +1,000 (behavior-specs-9/population-
                // growth.md section 3; spec-8's "+10" was read as colonists).
                if (Colonists < rawCapacity + 1000)
                {
                    populationGrowth = 0;
                }
                else
                {
                    // A planet that can support nobody (Alternate Reality without a starbase) is
                    // infinitely over-full: straight to the hard-clamped -12%/turn.
                    int capPct1000 = rawCapacity <= 0 ? int.MaxValue : (int)((Colonists * 1000.0) / rawCapacity);
                    int n = Math.Max(99 - (capPct1000 / 10), -300);
                    populationGrowth = Colonists * n / 2500.0;
                }
            }
            
            // As per vanilla Stars! the minimal colonist growth unit
            // is set as 100 colonists. A planet does not track colonists
            // by the tens. While visually this does not matter much,
            // the compounding effect of growth can make those extra tens of
            // colonists matter in the long run and mismatch the behaviour
            // of Stars! and Nova.
            return populationGrowth;
        }

        /// <summary>
        /// The year's population change in colonists (hundredths of a population unit), before
        /// it is split into whole units and the 0-99 remainder (behavior-specs-10/
        /// population-growth.md section 3): truncated toward zero, and for a negative-
        /// habitability planet at least 1 (the raw decline's floor), so even a tiny decline
        /// eventually takes a unit through the carry.
        /// </summary>
        private static int GrowthColonists(double populationGrowth, double habitalValue)
        {
            int colonists = (int)populationGrowth;
            if (habitalValue < 0.0 && colonists == 0)
            {
                colonists = -1;
            }

            return colonists;
        }

        /// <summary>
        /// The persisted per-planet fractional-carry byte, 0-99 colonists (population-growth.md
        /// section 3, planet offset 0x14): growth adds its remainder below a whole unit of 100
        /// colonists here, carrying a unit into the population at 100; a decline subtracts its
        /// remainder, borrowing a unit when the byte would go negative. Deterministic - no random
        /// rounding is involved.
        /// </summary>
        public int PopulationCarry;

        /// <summary>
        /// This year's population change in colonists including the fractional carry, and the
        /// carry byte it leaves; see <see cref="PopulationCarry"/>. Compute-only.
        /// </summary>
        public int GrowthWithCarry(Race race, out int newCarry)
        {
            double habitalValue = race.HabValue(this);
            int whole = CalculateGrowth(race);
            int exact = GrowthColonists(RawGrowth(race), habitalValue);
            int remainder = Math.Abs(exact) % 100;

            newCarry = PopulationCarry;
            if (exact >= 0)
            {
                newCarry += remainder;
                if (newCarry >= 100)
                {
                    newCarry -= 100;
                    whole += 100;
                }
            }
            else
            {
                newCarry -= remainder;
                if (newCarry < 0)
                {
                    newCarry += 100;
                    whole -= 100;
                }
            }

            return whole;
        }
        
        /// <summary>
        /// Update the population of a star system.
        /// </summary>
        /// <param name="race"></param>
        /// <remarks>
        /// See Update().
        /// </remarks>
        public void UpdatePopulation(Race race)
        {
            // Whole units of 100 change hands; the 0-99 remainder goes through the persisted
            // carry byte (population-growth.md section 3).
            Colonists += GrowthWithCarry(race, out int newCarry);
            PopulationCarry = newCarry;
            if (Colonists <= 0)
            {
                Colonists = 0;
                PopulationCarry = 0;
            }
        }
  
        /// <summary>
        /// Updates the research allocation for the star.
        /// </summary>
        /// <param name="budget">The new budget (0-100).</param>
        public void UpdateResearch(int budget)
        {
            if (OnlyLeftover == false)
            {
                if (budget >= 0 && budget <= 100)
                {
                    this.ResearchAllocation = (this.GetResourceRate() * budget) / 100;
                }
            }
            else
            {
                this.ResearchAllocation = 0;
            }
        }

        /// <summary>
        /// Update the resources available to a star system.
        /// </summary>
        /// <remarks>
        /// See UpdateMinerals().
        /// </remarks>
        public void UpdateResources()
        {
            // A certain number of colonists will generate a resource each year.
            // This has a default of 1000 colonists per resource but can be
            // channged on a per race basis in the Race Designer.

            // In addition, resources are generated by factories that are capable
            // of being manned. Again this is set in the Race Deigner with a
            // default of 1k colonists needed to man each factory. Note that the
            // actual number of existing factories may be less than the number
            // that are capable of being manned.
            // 
            // UPDATE: The Stars! default is 10k per 10 factories. This calculation
            // has been refactored away. -Aeglos
            // UPDATE2: This 

            this.ResourcesOnHand.Energy = this.GetResourceRate();
            this.ResourcesOnHand.Energy -= this.ResearchAllocation;
        }

        /// <summary>
        /// Update the minerals available on a star system.
        /// </summary>
        /// <remarks>
        /// See UpdateResources().
        /// </remarks>
        public void UpdateMinerals()
        {
            this.ResourcesOnHand.Ironium += this.Mine(ref this.MineralConcentration.Ironium, ref this.MineralMiningProgress.Ironium);
            this.ResourcesOnHand.Boranium += this.Mine(ref this.MineralConcentration.Boranium, ref this.MineralMiningProgress.Boranium);
            this.ResourcesOnHand.Germanium += this.Mine(ref this.MineralConcentration.Germanium, ref this.MineralMiningProgress.Germanium);
        }

        /// <summary>
        /// kT of a mineral that must be mined to drop concentration by one point, starting from
        /// <paramref name="concentration"/>. The community-sourced curve this codebase previously
        /// used (starsfaq.com "Mineral Concentration And Mining" by Jason Cawley: 12500/concentration
        /// for concentration >= 27, a flat 462 from 5 to 26, 1000 for the 4-&gt;3 and 3-&gt;2 drops,
        /// and 2000 for the 2-&gt;1 drop) is superseded by behavior-specs-7/population-growth.md's
        /// direct decompile of `FUN_1028_3a74`: the threshold is always `12500 / effectiveConcentration`,
        /// where `effectiveConcentration` is the raw concentration for concentration >= 25, floor-clamped
        /// to 25 for concentration 5-24, and floor-clamped to 10 below 5 - a clean two-breakpoint
        /// stair-step, not the community-documented three-tier/irregular-tail curve. Concentration
        /// never drops below 1 (no cost defined there).
        ///
        /// <paramref name="mineProductionRate"/> is the mining race's Race.MineProductionRate (10
        /// = the baseline 1.0x efficiency - "10 mines produce 10 kT at 100% concentration"). The
        /// same spec confirms efficiency above 1.0 "scales these thresholds up proportionally...
        /// extracting more kT per point of concentration drop, not more points per kT" - i.e. a
        /// more efficient race's mines get more minerals per point of concentration lost, not the
        /// same minerals for a faster-depleting planet. 1250 = 12500/10, so this reduces to the
        /// unscaled formula above for the default 10-rate race.
        /// </summary>
        private static int KtToDropOnePoint(int concentration, int mineProductionRate)
        {
            // Concentrations above 100 are real (comets, 100-299 homeworlds): mining uses the raw value
            // but depletion treats anything >= 101 as 100 (behavior-specs-9/population-growth.md
            // section 5).
            int clamped = Math.Min(concentration, 100);
            int effectiveConcentration = clamped >= 25 ? clamped : (clamped >= 5 ? 25 : 10);
            return (1250 * mineProductionRate) / effectiveConcentration;
        }

        /// <summary>
        /// Mine minerals.
        /// </summary>
        /// <param name="concentration">The mineral concentration in this system, (1.0 = 100%).
        /// Mining alters the concentration of minerals.</param>
        /// <param name="miningProgress">kT mined so far toward the next 1-point drop in
        /// concentration, carried forward across turns.</param>
        /// <returns>The number of minerals mined.</returns>
        /// <remarks>
        /// Mining rate = Number of Mines * Efficiency * Mineral Concentration %.
        ///
        /// Mining efficiency is a race parameter (MineProductionRate per 10 mines)
        /// Concentration is in % and is normalized so that 1.0 = 100%
        ///
        /// Note also that this method does not actually modify the Star's minerals. It
        /// merely returns the amount mined and decreases concentration.
        /// </remarks>
        private int Mine(ref int concentration, ref int miningProgress)
        {
            // As with factories, mines must be manned to be able to produce.
            // Again this is set in the Race Deigner with a default of 1k
            // colonists needed to man each mine and, as with factories, the
            // actual number of existing mines may be less than the number that
            // are capable of being manned.
            //
            // The potential number of mines that might be capable of being used
            // is a race parameter which determines how many mines may be
            // operated by 10K colonists.

            int mined = GetMiningRate(concentration);
            // GetMiningRate already returns 0 when ThisRace is null, so the baseline (10, i.e.
            // unscaled) rate here is only ever multiplied against that 0. An unset/zero
            // MineProductionRate (a Race object that exists but was never loaded from a real
            // race file) falls back the same way - a genuine 0 here would make KtToDropOnePoint
            // return 0, collapsing concentration to 1 in a single application.
            // Alternate Reality's own mines use the fixed setting 10 (OwnMinesOutputSetting).
            int mineProductionRate = OwnMinesOutputSetting() > 0 ? OwnMinesOutputSetting() : 10;
            return ApplyMining(mined, ref concentration, ref miningProgress, mineProductionRate);
        }

        /// <summary>
        /// Applies an already-computed raw mined amount against this mineral's concentration and
        /// carried mining progress, sharing the same depletion curve regardless of whether the
        /// mining came from the planet's own mines (<see cref="Mine"/>) or an orbiting
        /// remote-mining fleet (<see cref="MineForFleet"/>) - docs/behavior-specs-4/
        /// population-growth.md documents both as driven by "a single function" in the original
        /// game, with concentration drawn down between successive applications in the same turn.
        /// <paramref name="mineProductionRate"/> is the MINING race's own efficiency (see
        /// KtToDropOnePoint) - the race actually operating the mines/robots this call represents,
        /// not necessarily the star's owner (a remote-mining fleet may belong to a different empire
        /// than whoever owns, or doesn't own, the star it's mining).
        /// </summary>
        private static int ApplyMining(int mined, ref int concentration, ref int miningProgress, int mineProductionRate)
        {
            // Concentration drops by one point each time the cumulative kT mined toward it
            // (carried across turns) crosses the threshold for the current concentration level.
            miningProgress += mined;
            while (concentration > 1 && miningProgress >= KtToDropOnePoint(concentration, mineProductionRate))
            {
                miningProgress -= KtToDropOnePoint(concentration, mineProductionRate);
                concentration--;
            }

            if (concentration < 1)
            {
                concentration = 1;
            }

            return mined;
        }

        /// <summary>
        /// Mines this mineral on behalf of an orbiting remote-mining fleet (as opposed to the
        /// planet's own mines - see <see cref="Mine"/>), applying the spec-confirmed
        /// <c>mineEquivalents * concentration / 100</c> formula (with the same stochastic
        /// rounding used elsewhere - see Global.StochasticRound) against the SAME concentration/
        /// progress state the planet's own mines share, so multiple mining sources at one star
        /// correctly deplete concentration in sequence rather than each seeing the turn's
        /// starting concentration.
        /// </summary>
        /// <param name="mineEquivalents">The mining fleet's total mine-equivalents (see
        /// Fleet.MineEquivalents), already capped at the per-fleet maximum.</param>
        /// <param name="concentration">The mineral concentration in this system, mutated in
        /// place.</param>
        /// <param name="miningProgress">kT mined so far toward the next 1-point drop in
        /// concentration, mutated in place.</param>
        /// <param name="mineProductionRate">The mining fleet's OWNING race's Race.MineProductionRate
        /// (see KtToDropOnePoint) - a remote-mining fleet's effect on concentration depletion scales
        /// with its own race's mining efficiency, same as a planet's own mines.</param>
        /// <returns>The number of kT of this mineral mined by the fleet this application.</returns>
        public static int MineForFleet(int mineEquivalents, ref int concentration, ref int miningProgress, int mineProductionRate)
        {
            return MineForFleet(mineEquivalents, ref concentration, ref miningProgress, mineProductionRate, 0);
        }

        /// <summary>
        /// As <see cref="MineForFleet(int, ref int, ref int, int)"/>, with the yield using at least
        /// <paramref name="yieldConcentrationFloor"/> (the stored concentration is unaffected): pass
        /// the mined star's <see cref="RemoteMiningYieldConcentrationFloor"/>.
        /// </summary>
        public static int MineForFleet(int mineEquivalents, ref int concentration, ref int miningProgress, int mineProductionRate, int yieldConcentrationFloor)
        {
            int yieldConcentration = Math.Max(concentration, yieldConcentrationFloor);
            int mined = Global.StochasticRound(mineEquivalents * (yieldConcentration / 100.0));
            return ApplyMining(mined, ref concentration, ref miningProgress, mineProductionRate);
        }

        /// <summary>
        /// This star's operational Stargate, if its Starbase has one, or null. Stargates are
        /// only ever built on starbases (an Orbital-class hull item), never on ordinary ships -
        /// see docs/behavior-specs-4/client-interface.md's "Starbase capability indicators" note,
        /// which checks the same "Gate" property for the map's Stargate dot.
        /// </summary>
        public Gate GetStargate()
        {
            return GetStargate(Starbase);
        }

        /// <summary>Same lookup as the instance overload, but usable against a foreign star's
        /// scanned Starbase report (StarIntel.Starbase) too - lets client-side UI (e.g. the
        /// waypoint editor's "use Stargate" default/warning) check gate eligibility without
        /// needing a live, fully-known Star for the far end.</summary>
        public static Gate GetStargate(Fleet starbase)
        {
            ShipToken token = starbase?.Composition.Values.FirstOrDefault();
            if (token == null)
            {
                return null;
            }

            token.Design.Update();
            return token.Design.Summary.Properties.TryGetValue("Gate", out ComponentProperty gate) ? gate as Gate : null;
        }

        public int Defenses
        {
            set
            {
                if (value > Global.MaxDefenses)
                {
                    Report.Debug("Max defenses exceeded.");
                    defenses = Global.MaxDefenses;
                }
                else
                {
                    defenses = value;
                }
            }
            get
            {
                if (defenses <= Global.MaxDefenses)
                {
                    return defenses;
                }
                else
                {
                    Report.Debug("Max defenses exceeded.");
                    return Global.MaxDefenses;
                }
            }
        }

        public void Add(Cargo cargo)
        {
            this.ResourcesOnHand.Ironium += cargo.Ironium;
            this.ResourcesOnHand.Boranium += cargo.Boranium;
            this.ResourcesOnHand.Germanium += cargo.Germanium;
            this.Colonists += cargo.ColonistNumbers;
            this.NotifyObserver();
        }

        public void Remove(Cargo cargo)
        {
            this.ResourcesOnHand.Ironium -= cargo.Ironium;
            this.ResourcesOnHand.Boranium -= cargo.Boranium;
            this.ResourcesOnHand.Germanium -= cargo.Germanium;
            this.Colonists -= cargo.ColonistNumbers;
            this.NotifyObserver();
        }

        public void AddObserver(IStarObserver observer)
        {
            observerList.Add(observer);
        }

        public void RemoveObserver(IStarObserver observer)
        {
            observerList.Remove(observer);
        }

        public void NotifyObserver()
        {
            foreach (IStarObserver observer in observerList)
            {
                observer.Update(this);
            }
        }

        /// <summary>
        /// Override the inherited property Item.Key. Stars use there Name as a unique key as their owner does not affect their unique identity like fleets/ships/minefields.
        /// </summary>
        public new string Key
        {
            get
            {
                return Name;
            }
        }

        /// <summary>
        /// Load: initializing constructor to read in a Star from an XmlNode (from a saved file).
        /// </summary>
        /// <param name="node">An XmlNode representing a Star.</param>
        public Star(XmlNode node)
            : base(node)
        {
            Starbase = null;
            MineralMiningProgress = new Resources();

            XmlNode mainNode = node.FirstChild;

            // Read the node
            while (mainNode != null)
            {
                try
                {
                    switch (mainNode.Name.ToLowerInvariant())
                    {
                        case "hasfleetsinorbit":
                            HasFleetsInOrbit = bool.Parse(mainNode.FirstChild.Value);
                            break;
                        case "productionqueue":
                            ManufacturingQueue = new ProductionQueue(mainNode);
                            break;
                        case "mineralconcentration":
                            MineralConcentration = new Resources(mainNode);
                            break;
                        case "resourcesonhand":
                            ResourcesOnHand = new Resources(mainNode);
                            break;
                        case "mineralminingprogress":
                            MineralMiningProgress = new Resources(mainNode);
                            break;
                        case "colonists":
                            Colonists = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "populationcarry":
                            PopulationCarry = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "defenses":
                            Defenses = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "factories":
                            Factories = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "mines":
                            Mines = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "researchallocation":
                            ResearchAllocation = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "ishomeworld":
                            IsHomeWorld = bool.Parse(mainNode.FirstChild.Value);
                            break;
                        case "hasartifact":
                            HasArtifact = bool.Parse(mainNode.FirstChild.Value);
                            break;
                        case "deferredscrapresources":
                            // Superseded next-turn UR credit (spec-9 replaces it with the
                            // same-generation RecycledScrapResources blend, which is never
                            // saved). Tolerated and discarded so older files still load.
                            break;
                        case "onlyleftover":
                            OnlyLeftover = bool.Parse(mainNode.FirstChild.Value);
                            break;
                        case "scanrange":
                            ScanRange = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "defensetype":
                            DefenseType = mainNode.FirstChild.Value;
                            break;
                        case "scannertype":
                            ScannerType = mainNode.FirstChild.Value;
                            break;
                        case "packetdestination":
                            PacketDestination = mainNode.FirstChild?.Value;
                            break;
                        case "packetwarp":
                            PacketWarp = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "gravity":
                            Gravity = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "radiation":
                            Radiation = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "temperature":
                            Temperature = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "originalgravity":
                            OriginalGravity = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "originalradiation":
                            OriginalRadiation = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "originaltemperature":
                            OriginalTemperature = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        // These are placeholder objects that will be linked to the real objects once 
                        // loading from the file is complete (as they may not exist yet, and cannot be 
                        // referenced from here). The node will only hold enough information to identify 
                        // the referenced object. 

                        // ThisRace will point to the Race that owns the Star, 
                        // for now create a placeholder Race and load its Name
                        case "thisrace":
                            ThisRace = new Race();
                            ThisRace.Name = mainNode.FirstChild.Value;
                            break;

                        // Starbase will point to the Fleet that is this planet's starbase (if any), 
                        // for now create a placeholder Fleet and load its FleetID
                        case "starbase":
                            Starbase = new Fleet(long.Parse(mainNode.FirstChild.Value, NumberStyles.HexNumber));
                            break;

                        default:
                            break;
                    }
                }
                catch (Exception e)
                {
                    // Non-fatal - see Waypoint.cs's own comment for the live-reproduced crash
                    // this "one bad field exits the whole app" pattern caused.
                    Report.Error(e.Message + "\n Details: \n" + e.ToString());
                }
                mainNode = mainNode.NextSibling;
            }
        }

        /// <summary>
        /// Create an XmlElement representation of the star for saving.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representation of the star.</returns>
        public new XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelStar = xmldoc.CreateElement("Star");

            // include inherited Item properties
            xmlelStar.AppendChild(base.ToXml(xmldoc));

            xmlelStar.AppendChild(ManufacturingQueue.ToXml(xmldoc));

            xmlelStar.AppendChild(MineralConcentration.ToXml(xmldoc, "MineralConcentration"));

            xmlelStar.AppendChild(ResourcesOnHand.ToXml(xmldoc, "ResourcesOnHand"));

            xmlelStar.AppendChild(MineralMiningProgress.ToXml(xmldoc, "MineralMiningProgress"));

            Global.SaveData(xmldoc, xmlelStar, "HasFleetsInOrbit", HasFleetsInOrbit.ToString());
  
            // Starbase and ThisRace are stored as references only (just the name is saved).
            if (Starbase != null)
            {
                Global.SaveData(xmldoc, xmlelStar, "Starbase", Starbase.Key.ToString("X"));
            }
            
            if (ThisRace != null)
            {
                Global.SaveData(xmldoc, xmlelStar, "ThisRace", ThisRace.Name);
            }

            if (Colonists != 0)
            {
                Global.SaveData(xmldoc, xmlelStar, "Colonists", Colonists.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (PopulationCarry != 0)
            {
                Global.SaveData(xmldoc, xmlelStar, "PopulationCarry", PopulationCarry.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            if (Defenses != 0)
            {
                Global.SaveData(xmldoc, xmlelStar, "Defenses", Defenses.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            if (Factories != 0)
            {
                Global.SaveData(xmldoc, xmlelStar, "Factories", Factories.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            if (Mines != 0)
            { 
                Global.SaveData(xmldoc, xmlelStar, "Mines", Mines.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            if (ResearchAllocation != 0)
            {
                Global.SaveData(xmldoc, xmlelStar, "ResearchAllocation", ResearchAllocation.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (IsHomeWorld)
            {
                Global.SaveData(xmldoc, xmlelStar, "IsHomeWorld", IsHomeWorld.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (HasArtifact)
            {
                Global.SaveData(xmldoc, xmlelStar, "HasArtifact", HasArtifact.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            Global.SaveData(xmldoc, xmlelStar, "OnlyLeftover", OnlyLeftover.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        
            if (ScanRange != 0)
            {
                Global.SaveData(xmldoc, xmlelStar, "ScanRange", ScanRange.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            Global.SaveData(xmldoc, xmlelStar, "DefenseType", DefenseType);
            Global.SaveData(xmldoc, xmlelStar, "ScannerType", ScannerType);

            if (!string.IsNullOrEmpty(PacketDestination))
            {
                Global.SaveData(xmldoc, xmlelStar, "PacketDestination", PacketDestination);
            }

            if (PacketWarp != 0)
            {
                Global.SaveData(xmldoc, xmlelStar, "PacketWarp", PacketWarp.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            Global.SaveData(xmldoc, xmlelStar, "Gravity", Gravity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelStar, "Radiation", Radiation.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelStar, "Temperature", Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelStar, "OriginalGravity", OriginalGravity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelStar, "OriginalRadiation", OriginalRadiation.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelStar, "OriginalTemperature", OriginalTemperature.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return xmlelStar;
        }

        public override string ToString()
        {
            return "Star: " + Name;
        }
        
        public StarIntel GenerateReport(ScanLevel scan, int year)
        {
            StarIntel report = new StarIntel(this, scan, year);
            
            return report;
        }
    }
}
