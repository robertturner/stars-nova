#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010, 2011, 2012 The Stars-Nova Project
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

namespace Nova.Common.Components
{
    using System;
    using System.Collections.Generic;
    using System.Xml;

    /// <summary>
    /// This module defines the potential design of a ship. Details of the actual
    /// design are only available once the hull modules have been populated.
    /// </summary>
    [Serializable]
    public class ShipDesign : Item
    {
        // This is the component that contains the Hull property, to which all other ships components attach.
        public Component Blueprint 
        {
            get; 
            set;
        }

        // Note there are get properties for: Armor, Shield, FuelCapcity, CargoCapacity, etc

        // The Summary is a 'super' component with properties representing the sum of all 
        // components added to the ship. 
        public Component Summary = new Component();

        // The following items can't be fully sumarised, as their properties can't be simply added.
        // For example each weapon stack will fire separately at its own initiative.
        public List<Weapon> Weapons = new List<Weapon>();

        // The bombing capability of a ship can be summarised by the sum of its 
        // Conventional bombs and the sum of its smart bombs.
        public Bomb ConventionalBombs = new Bomb(0, 0, 0, false);
        public Bomb SmartBombs = new Bomb(0, 0, 0, true);

        // Mine layers which create different types of minefields.
        // Note we assume that there will be three types of minefields: standard, heavy and speed bump
        // and they can be distinguised by the % chance of collision. (0.3, 1.0 and 3.5 respectivly).
        public MineLayer StandardMines = new MineLayer();
        public MineLayer HeavyMines = new MineLayer();
        public MineLayer SpeedBumbMines = new MineLayer();

        /// <summary>
        /// The image assigned to this ship design, which may be different from the default hull module component image. 
        /// The ship image shall be selectable when the ship is designed.
        /// </summary>
        public ShipIcon Icon = null;

        /// <summary>
        /// The "failed legality" flag (design record +0x7c bit 0x80, behavior-specs-10/
        /// ship-design-and-components.md §16; fleet-movement-scanning-cargo.md §5 Scrap Fleet):
        /// set by the end-of-generation DesignLegalityStep on a non-starbase design whose hull or
        /// any part is not fully available to its owner now. Never cleared on the record (only a
        /// designer copy starts without it, so the copy constructor does not carry it). Scrap
        /// Fleet and Colonize credit a flagged stack at a quarter of its cost. Persisted in the
        /// design XML.
        /// </summary>
        public bool FailedLegality { get; set; }

        /// <summary>
        // Returns the Hull directly for easy Module access.
        /// </summary>
        public Hull Hull
        {
            get
            {
                return Blueprint.Properties["Hull"] as Hull;
            }
        }
        
        /// <summary>
        /// Returns the total Mass (No cargo) of this design.
        /// </summary>
        public int Mass
        {
            get
            {
                return Summary.Mass;   
            }
        }
        
        /// <summary>
        /// Returns the total Cost of this design.
        /// </summary>
        public Resources Cost
        {
            get
            {
                return Summary.Cost;
            }
        }
        
        /// <summary>
        /// Get the total shield value of this ShipDesign.
        /// </summary>
        public int Shield
        {
            get
            {
                if (Summary.Properties.ContainsKey("Shield"))
                {
                    return ((IntegerProperty)Summary.Properties["Shield"]).Value;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Get the total Armor value of this ShipDesign.
        /// </summary>
        /// <remarks>
        /// Confirmed live as a real, reported bug: a freshly-built ship showed 100% damaged the
        /// very turn it was constructed. Root cause - unlike every sibling summary-derived
        /// property here (FuelCapacity, CargoCapacity, Weapons, IsBomber, IsStarbase, ...), this
        /// getter never called Update() itself; it only ever worked by accident, piggy-backing on
        /// some OTHER property access (e.g. Cost, read by ShipProductionUnit's own constructor)
        /// having already populated Summary first on the SAME design object. ShipToken's
        /// constructor (Armor = newDesign.Armor * quantity) reads this property to seed a newly
        /// built ship's starting armor - if Design.Armor is the FIRST summary-derived property
        /// ever touched on this particular ShipDesign instance (e.g. right after a fresh turn's
        /// state load, before anything else happens to touch this design), Summary was still
        /// empty and this returned 0 - a brand new ship built with zero armor instead of full.
        /// </remarks>
        public int Armor
        {
            get
            {
                if (Summary.Properties.Count == 0)
                {
                    Update();
                }

                if (Summary.Properties.ContainsKey("Armor"))
                {
                    return ((IntegerProperty)Summary.Properties["Armor"]).Value;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Get the power rating of this ship - stub: TODO (priority 6).
        /// </summary>
        public int PowerRating
        {
            get
            {
                Update(); 
                return 0;
            }
        }
        
        /// <summary>
        /// Fuel (mg) the design's own components generate per year while it is not refuelling at a
        /// friendly starbase - 50 for each Anti-matter Generator it carries (the Fuel property's
        /// Generation summed over the design). behavior-specs-8/turn-generation-engine.md section 1
        /// step 22.
        /// </summary>
        public int FuelGenerationPerYear
        {
            get
            {
                if (Summary.Properties.Count == 0)
                {
                    Update();
                }
                if (Summary.Properties.ContainsKey("Fuel"))
                {
                    return ((Fuel)Summary.Properties["Fuel"]).Generation;
                }
                return 0;
            }
        }

        /// <summary>
        /// True for the Fuel Transport and Super-Fuel Transport hulls, which make 200 mg of fuel per
        /// ship per year (turn-generation-engine.md section 1 step 22).
        /// </summary>
        public bool IsFuelTransportHull
        {
            get
            {
                return Blueprint != null
                    && (Blueprint.Name == "Fuel Transport" || Blueprint.Name == "Super-Fuel Transport");
            }
        }

        /// <summary>
        /// Get the total FuelCapacity of this ShipDesign.
        /// </summary>
        public int FuelCapacity
        {
            get
            {
                if (Summary.Properties.Count == 0)
                {
                    Update();
                }
                if (Summary.Properties.ContainsKey("Fuel"))
                {
                    return ((Fuel)Summary.Properties["Fuel"]).Capacity;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Get the total cargo capacity of this design.
        /// </summary>
        public int CargoCapacity
        {
            get
            {
                if (Summary.Properties.ContainsKey("Cargo"))
                {
                    return ((IntegerProperty)Summary.Properties["Cargo"]).Value;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Get the dock capacity of this ShipDesign (0 if none).
        /// </summary>
        public int DockCapacity
        {
            get
            {
                if (Blueprint.Properties.ContainsKey("Hull"))
                {
                    return ((Hull)Blueprint.Properties["Hull"]).DockCapacity;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// True if the ship design includes a scanner.
        /// </summary>
        public bool CanScan
        {
            get
            {
                if (Summary.Properties.ContainsKey("Scanner"))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Get the normal scanner capability of this ShipDesign (0 if none).
        /// </summary>
        public int NormalScan
        {
            get
            {
                if (Summary.Properties.ContainsKey("Scanner"))
                {
                    return ((Scanner)Summary.Properties["Scanner"]).NormalScan;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Get the penetrating scanner ability of this ShipDesign (0 if none).
        /// </summary>
        public int PenetratingScan
        {
            get
            {
                if (Summary.Properties.ContainsKey("Scanner"))
                {
                    return ((Scanner)Summary.Properties["Scanner"]).PenetratingScan;
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Get the engine component fitted to this ShipDesign (null if none).
        /// </summary>
        public Engine Engine
        {
            get
            {
                if (Summary.Properties.ContainsKey("Engine"))
                {
                    return (Engine)Summary.Properties["Engine"];
                }
                else
                {
                    return null;
                }
            }
        }
        
        /// <summary>
        /// Get the highest speed the ship can travel for 0 fuel.
        /// </summary>
        public int FreeWarpSpeed
        {
            get
            {
                return Engine.FreeWarpSpeed; 
            }
        }

        /// <summary>
        /// Get this design's battle speed in squares per round, as the design card shows it (0.0
        /// for a starbase, which never moves): (v + 2) quarter-squares for the design's
        /// battle-movement value v (<see cref="BattleMovement"/>), i.e. 1/2 to 2 1/2.
        /// </summary>
        public double BattleSpeed
        {
            get
            {
                if (IsStarbase)
                {
                    return 0.0;
                }

                return (BattleMovement + 2) / 4.0;
            }
        }

        /// <summary>
        /// The five engines with a battle-movement base of 10 regardless of their fuel table
        /// (behavior-specs-10/combat-resolution.md §5). "Interspace 10" is this data's spelling
        /// of Interspace-10.
        /// </summary>
        private static readonly HashSet<string> BattleBaseTenEngines = new HashSet<string>
        {
            "Interspace 10", "Interspace-10", "Enigma Pulsar", "Trans-Star 10", "Trans-Galactic Mizer Scoop", "Galaxy Scoop",
        };

        /// <summary>
        /// The design-card battle-movement value 0-8 (FUN_10f0_2184 called without a fleet): no
        /// War Monger bonus and no cargo terms. See <see cref="BattleMovementFor"/>.
        /// </summary>
        public int BattleMovement
        {
            get
            {
                return BattleMovementFor(0, false, false);
            }
        }

        /// <summary>
        /// A battle token's movement value v, 0-8 - behavior-specs-10/combat-resolution.md §5
        /// (FUN_10f0_2184, all integer and truncating): a base of 10 for Interspace-10, Enigma
        /// Pulsar, Trans-Star 10, Trans-Galactic Mizer Scoop and Galaxy Scoop, otherwise the
        /// highest warp from 9 down whose fuel-use entry is below 121; +1 per Maneuvering Jet, +2
        /// per Overthruster (their "Battle Movement" 1/4 and 1/2 squares) and +1 per Multi
        /// Function Pod; + half, rounded up, of the Enigma Pulsar plus Alien Miner count; -4; +2
        /// for War Monger; -1 when the design has cargo space and its fleet dumped its minerals;
        /// - (mass + the design's share of the fleet's cargo) / 70 / engine count; clamped 0-8.
        /// The token moves (v + 2) quarter-squares per round.
        /// </summary>
        /// <param name="cargoShareKt">This design's per-ship share of the fleet's cargo mass, in
        /// kT, worked out after any dump.</param>
        /// <param name="cargoDumped">The fleet jettisoned minerals under its battle plan's dump
        /// option at battle start.</param>
        /// <param name="warMonger">The owner is War Monger (PRT 2).</param>
        public int BattleMovementFor(int cargoShareKt, bool cargoDumped, bool warMonger)
        {
            if (Summary.Properties.Count == 0)
            {
                Update();
            }

            if (Blueprint == null || !Blueprint.Properties.ContainsKey("Hull") || IsStarbase)
            {
                return 0;
            }

            int value = 0;
            int engines = 0;
            int pulsarsAndMiners = 0;
            int pods = 0;

            foreach (HullModule module in Hull.Modules)
            {
                Component part = module.AllocatedComponent;
                if (part == null || module.ComponentCount <= 0)
                {
                    continue;
                }

                if (engines == 0 && part.Properties.TryGetValue("Engine", out ComponentProperty engineProperty) && engineProperty is Engine engine)
                {
                    engines = module.ComponentCount;
                    value += BattleMovementBase(part.Name, engine);
                }

                if (part.Name == "Enigma Pulsar" || part.Name == "Alien Miner")
                {
                    pulsarsAndMiners += module.ComponentCount;
                }

                if (part.Name == "Multi Function Pod")
                {
                    pods += module.ComponentCount;
                }
            }

            // Maneuvering Jets (1/4 square = 1 point) and Overthrusters (1/2 = 2) carry their
            // contribution as "Battle Movement"; the summary also holds War Monger's +1/2 when the
            // design was updated for a WM race, which is taken back out here and re-applied from
            // the warMonger argument (the design card omits it).
            if (Summary.Properties.TryGetValue("Battle Movement", out ComponentProperty movement) && movement is DoubleProperty movementValue)
            {
                int points = (int)Math.Round(movementValue.Value * 4.0);
                if (raceForCostModifiers != null && raceForCostModifiers.HasTrait("WM"))
                {
                    points -= 2;
                }

                value += points;
            }

            value += pods;
            value += (pulsarsAndMiners + 1) / 2;
            value -= 4;

            if (warMonger)
            {
                value += 2;
            }

            if (cargoDumped && CargoCapacity > 0)
            {
                value -= 1;
            }

            value -= (Mass + Math.Max(0, cargoShareKt)) / 70 / Math.Max(1, engines);

            return Math.Max(0, Math.Min(8, value));
        }

        /// <summary>
        /// The battle-movement base: 10 for the five named engines, otherwise the highest warp
        /// from 9 down at which the engine's fuel-use entry is below 121 (FuelConsumption[w - 1]
        /// holds warp w's entry), 0 if none.
        /// </summary>
        private static int BattleMovementBase(string engineName, Engine engine)
        {
            if (engineName != null && BattleBaseTenEngines.Contains(engineName))
            {
                return 10;
            }

            for (int warp = 9; warp >= 1; warp--)
            {
                if (warp - 1 < engine.FuelConsumption.Length && engine.FuelConsumption[warp - 1] < 121)
                {
                    return warp;
                }
            }

            return 0;
        }

        /// <summary>
        /// Get the total beam deflection capability: the percentage of incoming beam damage
        /// removed (0 with no deflectors, 10 / 19 / 28 for 1 / 2 / 3), i.e. 100 minus
        /// <see cref="BeamDeflectorPercent"/>.
        /// </summary>
        /// <remarks>
        /// Previously read the key "Deflector", which nothing ever wrote - components.xml gives
        /// the Beam Deflector a property of type "Beam Deflector", which SumProperty also had no
        /// case for - so beam deflectors never had any effect in combat (behavior-specs-9
        /// coverage, combat row 16).
        /// </remarks>
        public double BeamDeflectors
        {
            get
            {
                return 100 - BeamDeflectorPercent;
            }
        }

        /// <summary>
        /// The target-side beam deflector percentage: the share of beam damage that still gets
        /// through, 100 with no deflectors. behavior-specs-9/combat-resolution.md §6: it starts
        /// at 100 and is multiplied by 0.9 per Beam Deflector, truncating at each step on a 1000
        /// scale, so 1, 2 and 3 deflectors give 90, 81 and 72 (not 72.9). Used both for beam
        /// damage and for the beam target score (§4).
        /// </summary>
        public int BeamDeflectorPercent
        {
            get
            {
                if (Summary.Properties.Count == 0)
                {
                    Update();
                }

                return beamDeflectorPerMille / 10;
            }
        }

        /// <summary>
        /// The firer-side beam capacitor percentage, 100 with no capacitors.
        /// behavior-specs-9/combat-resolution.md §6/§8: it starts at 100 and is multiplied by
        /// (100 + rate)/100 per capacitor (Energy Capacitor 10, Flux Capacitor 20, §10d), capped
        /// at 255. Integer arithmetic, truncating at each step (the stored value is a byte; the
        /// spec gives no separate rounding rule for this one).
        /// </summary>
        public int CapacitorPercent
        {
            get
            {
                if (Summary.Properties.Count == 0)
                {
                    Update();
                }

                return capacitorPercent;
            }
        }

        /// <summary>
        /// Running per-mille share of beam damage let through by this design's deflectors, see
        /// <see cref="BeamDeflectorPercent"/>. Reset by Update, accumulated by SumProperty.
        /// </summary>
        private int beamDeflectorPerMille = 1000;

        /// <summary>
        /// Running capacitor percentage, see <see cref="CapacitorPercent"/>. Reset by Update,
        /// accumulated by SumProperty.
        /// </summary>
        private int capacitorPercent = 100;

        /// <summary>
        /// Running sums of every fitted scanner's range to the fourth power (normal and
        /// penetrating), so the design's range is taken once, as sqrt(sqrt(sum of range^4)) over
        /// all its scanners (ship-design-and-components.md section 15c, FUN_1038_337e). Combining
        /// pairwise through Scanner.operator+ truncated after every pair and lost up to a light
        /// year per extra scanner. Reset by Update, accumulated by SumProperty.
        /// </summary>
        private double scannerNormalFourthPowers;
        private double scannerPenetratingFourthPowers;

        /// <summary>The ceiling of a design's combined jamming percentage (ship-design-and-components.md §7).</summary>
        public const double MaximumJammer = 95;

        /// <summary>
        /// Get the total jamming capability (combined percentage reduction in an attacker's
        /// missile/torpedo accuracy). Units compound multiplicatively (each keeps 100 - p percent
        /// of what is left) and the design's figure is capped at <see cref="MaximumJammer"/>
        /// (behavior-specs-10/ship-design-and-components.md §7, the base-10000 accumulator stored
        /// as a 0-95 byte). Langston Shell (95% per unit, i.e. 5% jamming) and Mega Poly Shell
        /// (80%, i.e. 20%) feed it through their Jammer properties in components.xml.
        /// </summary>
        public double Jammer
        {
            get
            {
                if (Summary.Properties.ContainsKey("Jammer"))
                {
                    return Math.Min(MaximumJammer, ((ProbabilityProperty)Summary.Properties["Jammer"]).Value);
                }
                else
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Get the total accuracy bonus from fitted computers.
        /// </summary>
        /// <remarks>
        /// behavior-specs-10/combat-resolution.md §7a (FUN_10f0_264c output (b), token +0xc):
        /// walking the installed parts in slot order, each unit of a part with a Computer
        /// accuracy rate (Battle Computer 20, Super Computer 30, Nexus 50, Multi Contained
        /// Munition a flat 10) applies bonus = bonus + (100 - bonus) x rate / 100, truncated at
        /// every step, with no cap - so three Battle Computers give 48, not 48.8.
        /// </remarks>
        public double ComputerAccuracy
        {
            get
            {
                if (Blueprint == null || !Blueprint.Properties.ContainsKey("Hull"))
                {
                    return 0;
                }

                int bonus = 0;
                foreach (HullModule module in Hull.Modules)
                {
                    if (module.AllocatedComponent != null
                        && module.AllocatedComponent.Properties.TryGetValue("Computer", out ComponentProperty property)
                        && property is Computer computer)
                    {
                        int rate = (int)Math.Floor(computer.Accuracy + 1e-6);
                        for (int i = 0; i < module.ComponentCount; i++)
                        {
                            bonus += (100 - bonus) * rate / 100;
                        }
                    }
                }

                return bonus;
            }
        }

        /// <summary>
        /// Get a count of the number of engines. Assumes there is only one engine stack.
        /// </summary>
        public int Number_of_Engines
        {
            get
            {
                if (Blueprint.Properties.ContainsKey("Hull"))
                {
                    foreach (HullModule module in Hull.Modules)
                    {
                        if (module.AllocatedComponent != null && module.AllocatedComponent.Type == ItemType.Engine)
                        {
                            return module.ComponentCount;
                        }
                    }
                }
                return 0;
            }
        }

        /// <summary>
        /// Determine if this is a starbase hull.
        /// </summary>
        public bool IsStarbase
        {
            get
            {
                if (Blueprint.Properties.ContainsKey("Hull"))
                {
                    return Hull.IsStarbase;
                }
                // It doesn't even have a Hull!
                Report.Error("ShipDesign.IsStarbase called on a design with no hull.");
                return false;
            }
        }

        /// <summary>
        /// Get if this is a starbase that can provide unlimited fuel.
        /// TODO (priority 4) - Fuel transports can refuel too. So can ships with an anti-mater generator (IT component?).
        /// </summary>
        public bool CanRefuel
        {
            get
            {
                if (Blueprint.Properties.ContainsKey("Hull"))
                {
                    return Hull.CanRefuel;
                }
                // It doesn't even have a Hull!
                Report.Error("ShipDesign.CanRefuel called on a design with no hull.");
                return false;
            }
        }

        /// <summary>
        /// The cap on both a token's initiative and a weapon slot's firing bracket (§5).
        /// </summary>
        public const int MaxInitiative = 63;

        /// <summary>
        /// Get the initiative of the ShipDesign, including computers but not weapon initiative:
        /// the hull's initiative plus 1/2/3 per Battle Computer/Super Computer/Nexus, capped at
        /// 63 (behavior-specs-10/combat-resolution.md §5, token byte +6).
        /// </summary>
        public int Initiative
        {
            get
            {
                int initiative = 0;
                if (Blueprint.Properties.ContainsKey("Hull"))
                {
                    initiative += ((Hull)Blueprint.Properties["Hull"]).BattleInitiative;
                }
                if (Summary.Properties.ContainsKey("Computer"))
                {
                    initiative += ((Computer)Summary.Properties["Computer"]).Initiative;
                }
                return Math.Min(MaxInitiative, initiative);
            }
        }        
        
        /// <summary>
        /// Get total bomb capability. 
        /// </summary>
        /// <remarks>
        /// TODO (priority 6) Whatever code uses this seems to be ignoring smart bombs.
        /// </remarks>
        public Bomb BombCapability
        {
            get
            {
                Update();
                return ConventionalBombs;
            }
        }
                
        /// <summary>
        /// Get if the ship is a bomber.
        /// </summary>
        public bool IsBomber
        {
            get
            {
                Update();
                if (ConventionalBombs.PopKill == 0 && SmartBombs.PopKill == 0)
                {
                    return false;
                }
                return true;
            }
        }
        
        /// <summary>
        /// Get total mine laying capacity for this ship.
        /// </summary>
        /// <remarks>
        /// TODO (priority 6) Client code must handle heavy and speed trap mines too.
        /// </remarks>
        public int MineCount
        {
            get
            {
                Update();
                return StandardMines.LayerRate;
            }
        }

        /// <summary>Total remote-mining capacity (mine-equivalents) this design contributes per
        /// ship - docs/behavior-specs-4/population-growth.md's `mineEquivalents` term, summed
        /// from every installed "Mining Robot" component. The per-fleet 4,000 cap described
        /// there applies at the Fleet level (Fleet.MineEquivalents), not here.</summary>
        public int MineEquivalents
        {
            get
            {
                Update();
                return Summary.Properties.ContainsKey("Mining Robot")
                    ? ((IntegerProperty)Summary.Properties["Mining Robot"]).Value
                    : 0;
            }
        }

        /// <summary>
        /// Get if this ship has weapons.
        /// </summary>
        public bool HasWeapons
        {
            get
            {
                Update();
                if (Weapons == null)
                {
                    return false;
                }
                return true;
            }
        }
        
        /// <summary>
        /// The range of the ship's normal scanners.
        /// </summary>
        public int ScanRangeNormal
        {
            get
            {
                Update();
                return NormalScan;
            }
        }

        /// <summary>
        /// The range of the ship's penetrating scanners.
        /// </summary>
        public int ScanRangePenetrating
        {
            get
            {
                Update();
                return PenetratingScan;
            }
        }
        
        /// <summary>
        /// Checks if ship can colonize.
        /// </summary>
        public bool CanColonize
        {
            get
            {
                return Summary.Properties.ContainsKey("Colonizer");
            }
        }
        
        /// <summary>
        /// Parametric Constructors. Stores just the
        /// Design Key for later lookup.
        /// </summary>
        /// <param name="designkey"></param>
        public ShipDesign(long designkey)
            : base(designkey)
        {
            Key = designkey;
        }

        /// <summary>
        /// Copy Constructor.
        /// </summary>
        /// <param name="copy">ShipDesign to copy.</param>
        public ShipDesign(ShipDesign copy)
            : base(copy)
        {
            Icon = (ShipIcon)copy.Icon.Clone();
            Blueprint = new Component(copy.Blueprint);
            Update();
        }

        /// <summary>
        /// The race last passed to <see cref="Update(Race)"/>, remembered so that the many
        /// internal callers of the parameterless <see cref="Update()"/> (every property getter
        /// that lazily recomputes Summary) don't silently discard race-based cost modifiers
        /// applied by an earlier Update(race) call.
        /// </summary>
        private Race raceForCostModifiers;

        /// <summary>
        /// The empire's current tech levels last passed to <see cref="Update(Race, TechLevel)"/>,
        /// remembered the same way <see cref="raceForCostModifiers"/> is - needed for
        /// Miniaturization/Bleeding Edge Technology's cost adjustment, which (unlike every other
        /// cost modifier here) depends on the empire's CURRENT tech level, not just its race.
        /// </summary>
        private TechLevel currentTechLevelsForCostModifiers;

        /// <summary>Backing field for <see cref="CostWithoutBleedingEdgeDoubling"/>, summed by
        /// Update alongside Summary.Cost.</summary>
        private Resources costWithoutBleedingEdgeDoubling;

        /// <summary>
        /// The design's cost (miniaturized, with every trait adjustment) but WITHOUT Bleeding Edge
        /// Technology's doubling - the figure the Scrap Fleet recovery and the computer player's
        /// cost estimator use: the original raises a suppress flag for exactly those two
        /// computations, so scrap credits and AI estimates are never doubled
        /// (behavior-specs-10/race-traits.md section 7, step 7). Equal to <see cref="Cost"/> for a
        /// non-BET race.
        /// </summary>
        public Resources CostWithoutBleedingEdgeDoubling
        {
            get
            {
                return costWithoutBleedingEdgeDoubling ?? Summary.Cost;
            }
        }

        /// <summary>
        /// The ship design object has all information that could be found from a scan
        /// of the the ship hull modules. However scanning these for a particular piece
        /// of information is inefficient. This method reorganizes the information
        /// to save other routines from having to do this.
        /// </summary>
        public void Update()
        {
            Update(raceForCostModifiers, currentTechLevelsForCostModifiers);
        }

        /// <summary>
        /// As <see cref="Update()"/>, but also applies the owning race's weapon/starbase-cost
        /// PRT modifiers (War Monger: weapons 25% cheaper; Inner Strength: weapons 25% more
        /// expensive; Improved Starbases/Alternate Reality: starbases 20% cheaper) — see
        /// docs/behavior-specs/race-traits.md §2-3. Pass null to skip these (e.g. for an enemy
        /// design scanned from another empire, whose race traits aren't reliably known). The
        /// race passed here is remembered for subsequent parameterless Update() calls.
        /// </summary>
        public void Update(Race race)
        {
            Update(race, currentTechLevelsForCostModifiers);
        }

        /// <summary>
        /// As <see cref="Update(Race)"/>, but also applies Miniaturization/Bleeding Edge
        /// Technology's per-component cost adjustment, which needs the empire's CURRENT tech
        /// levels (not just its race) - see the per-component loop below. Pass null for
        /// <paramref name="currentTechLevels"/> to skip this specific adjustment (e.g. for an
        /// enemy design, whose empire's live tech levels aren't tracked here) while still applying
        /// every other race-based modifier <see cref="Update(Race)"/> does.
        /// </summary>
        public void Update(Race race, TechLevel currentTechLevels)
        {
            raceForCostModifiers = race;
            currentTechLevelsForCostModifiers = currentTechLevels;

            if (Blueprint == null)
            {
                return; // not much of a ship yet
            }

            if ( ! Blueprint.Properties.ContainsKey("Hull"))
            {
                return; // still not much of a ship.
            }

            // Start by copying the basic properties of the hull
            Summary = new Component(Blueprint);

            // The hull (or starbase chassis) is priced through the same item-cost routine as
            // every part, so it too shrinks with tech (behavior-specs-10/race-traits.md section 7
            // step 2: FUN_1038_2df8 prices the hull with a zero category word, which passes both
            // exclusion tests).
            Summary.Cost = AdjustedItemCost(Blueprint, race, currentTechLevels, out Resources hullCostUndoubled);
            costWithoutBleedingEdgeDoubling = hullCostUndoubled;

            // Battle-token percentages accumulated per component by SumProperty (see
            // BeamDeflectorPercent / CapacitorPercent) - reset here for the same reason as the
            // Weapons/Bombs/MineLayer accumulators below.
            beamDeflectorPerMille = 1000;
            capacitorPercent = 100;
            scannerNormalFourthPowers = 0;
            scannerPenetratingFourthPowers = 0;

            // Regenerating Shields' armor penalty, accumulated per slot in the module loop
            // below - see the RS block at the end of this method.
            int regeneratingShieldsArmorPenalty = 0;
            bool regeneratingShields = race != null && race.HasTrait("RS");

            // These six fields aren't part of Summary (see their own declaring comment - "can't
            // be fully sumarised, as their properties can't be simply added"), so unlike Summary
            // they were never reset here - each call accumulated more onto whatever a PREVIOUS
            // call had already summed, growing without bound the more often any property that
            // triggers Update() (MineCount, HasWeapons, etc.) was read. Real bug, since these
            // getters call Update() on every single access, not just once per design.
            Weapons.Clear();
            ConventionalBombs = new Bomb(0, 0, 0, false);
            SmartBombs = new Bomb(0, 0, 0, true);

            // Each accumulator needs its OWN bucket's HitChance from the start, not
            // MineLayer's default constructor value (0.3, i.e. "Standard") - the SumProperty
            // switch below only ever adds a real mine-layer component into an accumulator
            // whose HitChance already matches (MineLayer.operator+ rejects a mismatch and
            // silently leaves the accumulator unchanged, logging an error instead). Starting
            // HeavyMines/SpeedBumbMines at the Standard default meant the very FIRST heavy or
            // speed-trap mine layer component ever added to any design's accumulator would
            // always mismatch and get rejected, so a design's laying rate for either type could
            // never become nonzero - StandardMines only ever happened to work because its own
            // real HitChance (0.3) coincidentally equals MineLayer's unrelated class default.
            // Confirmed live: a Speed Trap 20-equipped design reported a laying rate of 0.
            StandardMines = new MineLayer { HitChance = MineLayer.StandardHitChance };
            HeavyMines = new MineLayer { HitChance = MineLayer.HeavyHitChance };
            SpeedBumbMines = new MineLayer { HitChance = MineLayer.SpeedTrapHitChance };

            // Add those properties which are included with the hull

            IntegerProperty armor = new IntegerProperty(Hull.ArmorStrength);
            Summary.Properties.Add("Armor", armor);
            IntegerProperty cargo = new IntegerProperty(Hull.BaseCargo);
            Summary.Properties.Add("Cargo", cargo);

            // The hull's own tank is always the base fuel capacity (behavior-specs-10/
            // fleet-movement-scanning-cargo.md section 4: Fuel Transport 750 mg, Super-Fuel Xport
            // 2250 mg). The two fuel-transport hulls also carry a Fuel property of their own
            // (capacity 0, generation 200) that was copied into Summary with the blueprint and so
            // replaced the tank (0 mg), then was summed again (400 mg a year). Their 200 mg a year
            // per ship is credited by Fleet.PassiveFuelGeneration (IsFuelTransportHull), so the
            // hull's Fuel property is not summed here.
            Summary.Properties["Fuel"] = new Fuel(Hull.FuelCapacity, 0);

            // Check any non Hull properties of the ShipHull
            foreach (string key in Blueprint.Properties.Keys)
            {
                if (key != "Hull" && key != "Fuel")
                {
                SumProperty(Blueprint.Properties[key], key, 1);
                }
            }

            // Then add all of the components fitted to the hull modules.
            foreach (HullModule module in Hull.Modules)
            {
                if (module.AllocatedComponent != null)
                {
                    // Sumarise the mass & cost. A slot holding N of a part (a Battleship's six lasers)
                    // contributes N times the part's mass and cost; only armor, fuel, weapons and the
                    // like were scaled by the count before, so a fully loaded ship cost and weighed
                    // the same as one carrying a single of each part.
                    int slotCount = Math.Max(1, module.ComponentCount);
                    Summary.Mass += module.AllocatedComponent.Mass * slotCount;

                    // The shared item-cost routine (FUN_1050_7d44): miniaturization first, then
                    // the trait adjustments on the discounted figure, then BET doubling last -
                    // see AdjustedItemCost.
                    Resources componentCost = AdjustedItemCost(module.AllocatedComponent, race, currentTechLevels, out Resources componentCostUndoubled);

                    for (int unit = 0; unit < slotCount; unit++)
                    {
                        Summary.Cost += componentCost;
                        costWithoutBleedingEdgeDoubling += componentCostUndoubled;
                    }

                    // Regenerating Shields halves only the armor contributed by Armor-category
                    // parts, rounded down per slot (that slot's count times the part's armor,
                    // halved). Hull base armor and the armor of non-Armor-category parts that
                    // carry some (Croby Sharmor, Langston Shell, Multi Cargo Pod) are not halved
                    // - behavior-specs-9/race-traits.md §3 (RS, bit 13) and combat-resolution.md
                    // §6.
                    if (regeneratingShields
                        && module.AllocatedComponent.Type == ItemType.Armor
                        && module.AllocatedComponent.Properties.TryGetValue("Armor", out ComponentProperty partArmor)
                        && partArmor is IntegerProperty partArmorValue)
                    {
                        int slotArmor = partArmorValue.Value * module.ComponentCount;
                        regeneratingShieldsArmorPenalty += slotArmor - (slotArmor / 2);
                    }

                    // Summarise the properties
                    foreach (string key in module.AllocatedComponent.Properties.Keys)
                    {
                        SumProperty(module.AllocatedComponent.Properties[key], key, module.ComponentCount);
                    }
                }
            }

            // Improved Starbases and Alternate Reality both give starbases a 20% cost discount;
            // the two don't stack. See docs/behavior-specs/race-traits.md §2-3.
            if (race != null && Hull.IsStarbase && (race.HasTrait("ISB") || race.HasTrait("AR")))
            {
                Summary.Cost = Summary.Cost * 0.8;
                costWithoutBleedingEdgeDoubling = costWithoutBleedingEdgeDoubling * 0.8;
            }

            // The design's scanner range: the fourth root of the sum of every scanner's range to
            // the fourth power, taken once over all of them (see scannerNormalFourthPowers).
            if (Summary.Properties.TryGetValue("Scanner", out ComponentProperty combinedScanner) && combinedScanner is Scanner combined)
            {
                combined.NormalScan = (int)Math.Pow(scannerNormalFourthPowers, 0.25);
                combined.PenetratingScan = (int)Math.Pow(scannerPenetratingFourthPowers, 0.25);
            }

            // No Advanced Scanners doubles conventional scanner range (in exchange for losing
            // access to penetrating-scanner components entirely, which is handled separately by
            // race/component restrictions, not here). See
            // docs/behavior-specs/fleet-movement-scanning-cargo.md §3.
            if (race != null && race.HasTrait("NAS") && Summary.Properties.ContainsKey("Scanner"))
            {
                Scanner scanner = Summary.Properties["Scanner"] as Scanner;
                if (scanner != null)
                {
                    scanner.NormalScan *= 2;
                }
            }

            // Super Stealth folds a flat 300 raw cloak-rating baseline into every design
            // "regardless of whether that design carries a cloaking device at all" - confirmed
            // by decompile (behavior-specs-7/combat-resolution.md §11), and independently
            // cross-checked against the community-known "SS races carry an inherent 75% cloak"
            // fact: 300 raw units lands exactly on this codebase's CloakCalculator curve's
            // 75%-breakpoint. This is raw units, not a percentage - see AddRawCloakUnits and
            // Fleet.RecalculateCloak for where the curve actually gets applied (once, at the
            // fleet level, after combining every design's raw rating).
            if (race != null && race.HasTrait("SS"))
            {
                AddRawCloakUnits(300);
            }

            // Improved Starbases' "inherent 20% cloak" is only ever sourced as plain trait text
            // in these specs, never decompiled/confirmed as a raw baseline the way SS's 300 is
            // (see race-traits.md's own hedge, cross-referenced against combat-resolution.md §11
            // making no mention of ISB at all). 40 raw units is back-solved from this codebase's
            // own curve to reproduce that one known data point (floor(40/2)=20) purely so ISB
            // composes through the same raw-unit-then-curve mechanism as everything else, rather
            // than being a disconnected special case - not a decompiled figure like SS's.
            if (race != null && race.HasTrait("ISB") && Hull.IsStarbase)
            {
                AddRawCloakUnits(40);
            }

            // War Monger gets a flat +0.5 ("half-square") battle-movement bonus, added to the
            // same "Battle Movement" aggregate a design's own Overthruster/Maneuvering Jet
            // components contribute to (see the generic-summable case in SumProperty) -
            // confirmed by decompile (behavior-specs-7/race-traits.md §2/§3a) and independently
            // corroborated by the live Race Wizard's recovered description text. Starbases never
            // move in battle (see BattleSpeed's own IsStarbase short-circuit), so this is skipped
            // for them.
            if (race != null && race.HasTrait("WM") && !Hull.IsStarbase)
            {
                if (Summary.Properties.TryGetValue("Battle Movement", out ComponentProperty existingMovement))
                {
                    ((DoubleProperty)existingMovement).Value += 0.5;
                }
                else
                {
                    Summary.Properties.Add("Battle Movement", new DoubleProperty(0.5));
                }
            }

            // Regenerating Shields: shields are 40% stronger than listed, armor from Armor-category
            // parts is worth only 50% - behavior-specs-9/race-traits.md §3 (RS, bit 13), traced in
            // full. Applied here at design level so every consumer of ShipDesign.Shield/Armor
            // (ShipToken's constructor, damage/attractiveness math, etc.) sees the adjusted
            // values with nothing further to change downstream; the per-round regeneration is
            // BattleEngine.ApplyRegeneratingShields.
            if (regeneratingShields)
            {
                // Per-ship shield total + floor(2/5 of it), capped at 65,535 (the total already
                // includes Fielded Kelarium's / Mega Poly Shell's shield points). Integer form
                // rather than "* 1.4", which can land a hair under the whole number.
                if (Summary.Properties.TryGetValue("Shield", out ComponentProperty shieldProperty))
                {
                    IntegerProperty shield = (IntegerProperty)shieldProperty;
                    shield.Value = Math.Min(65535, shield.Value + ((2 * shield.Value) / 5));
                }

                // Only the Armor-category slots' halving, accumulated per slot above - NOT the
                // whole armor total (the old "* 0.5" here also halved Hull.ArmorStrength).
                if (regeneratingShieldsArmorPenalty > 0
                    && Summary.Properties.TryGetValue("Armor", out ComponentProperty armorProperty))
                {
                    IntegerProperty armorValue = (IntegerProperty)armorProperty;
                    armorValue.Value = Math.Max(0, armorValue.Value - regeneratingShieldsArmorPenalty);
                }
            }
        }

        /// <summary>
        /// Adds raw cloak-rating units directly to this design's aggregate Cloak property,
        /// bypassing SumProperty/ProbabilityProperty's own independent-probability combination
        /// (which is the wrong rule for cloak - see CloakCalculator's own comment). Used for
        /// trait-intrinsic baselines (Super Stealth, Improved Starbases) that apply regardless of
        /// which cloak components (if any) are actually installed.
        /// </summary>
        private void AddRawCloakUnits(double rawUnits)
        {
            if (Summary.Properties.TryGetValue("Cloak", out ComponentProperty existingCloak))
            {
                ((ProbabilityProperty)existingCloak).Value += rawUnits;
            }
            else
            {
                Summary.Properties.Add("Cloak", new ProbabilityProperty(rawUnits));
            }
        }

        /// <summary>
        /// Miniaturization followed by Bleeding Edge Technology's doubling for a single item with
        /// no trait adjustments in between (e.g. the Genesis Device, which the discount includes) -
        /// see the private overload below for the exact rule (behavior-specs-10/race-traits.md
        /// section 7). Hulls and parts in a design go through <see cref="AdjustedItemCost"/>
        /// instead, which interleaves the WM/IS/IT/CE adjustments between the two steps.
        /// </summary>
        public static Resources ApplyMiniaturizationAndBleedingEdge(Resources cost, TechLevel requiredTech, Race race, TechLevel currentTechLevels)
        {
            return ApplyMiniaturizationAndBleedingEdge(cost, requiredTech, race, currentTechLevels, ItemType.None, true);
        }

        /// <summary>
        /// The whole shared item-cost routine FUN_1050_7d44 for one hull or part, in its exact
        /// order (behavior-specs-10/race-traits.md section 7): (2) miniaturization, (3) Stargates
        /// -25% for Interstellar Traveler, (4) beams/torpedoes -25% for War Monger or +25% for
        /// Inner Strength, (6) engines -50% for Cheap Engines - each acting on the DISCOUNTED
        /// figure - and (7) Bleeding Edge Technology's doubling after everything else.
        /// <paramref name="withoutBleedingEdgeDoubling"/> receives the same cost with step 7
        /// suppressed: the figure the Scrap Fleet recovery and the computer player's estimator
        /// see (they raise the suppress flag DS 0x078e bit 0x08 for their own computations).
        /// A null race skips everything; a null <paramref name="currentTechLevels"/> skips only
        /// miniaturization and BET (e.g. an enemy design whose tech levels aren't tracked).
        /// </summary>
        private static Resources AdjustedItemCost(Component item, Race race, TechLevel currentTechLevels, out Resources withoutBleedingEdgeDoubling)
        {
            Resources cost = new Resources(item.Cost);
            if (race == null)
            {
                withoutBleedingEdgeDoubling = cost;
                return cost;
            }

            bool applyTechMechanics = currentTechLevels != null && item.RequiredTech != null;
            if (applyTechMechanics)
            {
                cost = ApplyMiniaturizationAndBleedingEdge(cost, item.RequiredTech, race, currentTechLevels, item.Type, false);
            }

            // Step 4 covers component categories 0x10/0x20/0x40: beams, torpedoes AND bombs
            // (behavior-specs-10/race-traits.md section 7, "Beam/Torpedo/Bomb +-25%").
            bool isWeapon = item.Type == ItemType.BeamWeapons || item.Type == ItemType.Torpedoes || item.Type == ItemType.Bomb;
            if (isWeapon)
            {
                if (race.HasTrait("WM"))
                {
                    cost = cost * 0.75;
                }
                else if (race.HasTrait("IS"))
                {
                    cost = cost * 1.25;
                }
            }

            // Interstellar Traveler: Stargates cost 25% less to build (component category 0x0200,
            // Stargate subtypes only, not Mass Drivers).
            if (race.HasTrait("IT") && item.Properties.ContainsKey("Gate"))
            {
                cost = cost * 0.75;
            }

            // Cheap Engines: engines cost 50% less to build (component category 1, the whole
            // category).
            if (race.HasTrait("CE") && item.Properties.ContainsKey("Engine"))
            {
                cost = cost * 0.5;
            }

            withoutBleedingEdgeDoubling = cost;

            if (applyTechMechanics && BleedingEdgeDoubles(item.RequiredTech, race, currentTechLevels, item.Type))
            {
                cost = cost * 2;
            }

            return cost;
        }

        /// <summary>
        /// Miniaturization (step 2 of FUN_1050_7d44), exactly as behavior-specs-10/race-traits.md
        /// section 7 reads it from the raw bytes, optionally followed by Bleeding Edge Technology's
        /// doubling (step 7) when <paramref name="includeBleedingEdgeDoubling"/> is set (the public
        /// overload, for an item with no trait adjustments to interleave):
        /// - Every item is discounted except Terraforming and the planetary scanners and defences;
        ///   hulls, starbase chassis and the Genesis Device are included.
        /// - The margin is the smallest (race level - requirement) over the fields with a non-zero
        ///   requirement; an item with no requirement at all uses the race's LOWEST tech level.
        /// - A margin of 0 or less gives no discount. Otherwise it is capped at 19 and becomes 4%
        ///   per level capped at 75% (5% per level capped at 80% with Bleeding Edge Technology).
        /// - Each NON-ZERO cost (resources and the three minerals) loses MulDiv(cost, pct, 100),
        ///   which rounds to the nearest unit (halves away from zero), and a cost that would reach
        ///   0 is set to 1. Example: 20 resources 6 levels ahead loses MulDiv(20, 24, 100) = 5.
        /// </summary>
        private static Resources ApplyMiniaturizationAndBleedingEdge(Resources cost, TechLevel requiredTech, Race race, TechLevel currentTechLevels, ItemType type, bool includeBleedingEdgeDoubling)
        {
            Resources result = new Resources(cost);
            if (IsExcludedFromMiniaturization(type))
            {
                return result;
            }

            int percent = MiniaturizationPercent(requiredTech, race, currentTechLevels);
            if (percent > 0)
            {
                result.Ironium = MiniaturizeField(result.Ironium, percent);
                result.Boranium = MiniaturizeField(result.Boranium, percent);
                result.Germanium = MiniaturizeField(result.Germanium, percent);
                result.Energy = MiniaturizeField(result.Energy, percent);
            }

            if (includeBleedingEdgeDoubling && BleedingEdgeDoubles(requiredTech, race, currentTechLevels, type))
            {
                result = result * 2;
            }

            return result;
        }

        /// <summary>Terraforming (category 0x2000) and the planetary scanners and defences
        /// (category 0x8000 subtypes 0-13) skip miniaturization. The original then tests its BET
        /// doubling against an uninitialised margin for them; following the spec's
        /// reimplementation note they are exempt from the doubling too.</summary>
        private static bool IsExcludedFromMiniaturization(ItemType type)
        {
            return type == ItemType.Terraforming || type == ItemType.PlanetaryInstallations || type == ItemType.Defense;
        }

        /// <summary>The smallest (level - requirement) over the item's non-zero requirements, or
        /// the race's lowest tech level when it has none; <paramref name="hasRequirement"/> says
        /// which.</summary>
        private static int TechMargin(TechLevel requiredTech, TechLevel currentTechLevels, out bool hasRequirement)
        {
            int margin = int.MaxValue;
            int lowestLevel = int.MaxValue;
            hasRequirement = false;

            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                int level = currentTechLevels[field];
                lowestLevel = Math.Min(lowestLevel, level);

                int required = requiredTech[field];
                if (required <= 0)
                {
                    continue;
                }

                hasRequirement = true;
                margin = Math.Min(margin, level - required);
            }

            return hasRequirement ? margin : lowestLevel;
        }

        private static int MiniaturizationPercent(TechLevel requiredTech, Race race, TechLevel currentTechLevels)
        {
            int margin = TechMargin(requiredTech, currentTechLevels, out bool _);
            if (margin <= 0)
            {
                return 0;
            }

            bool bet = race.HasTrait("BET");
            int levels = Math.Min(margin, 19);
            return Math.Min(bet ? 80 : 75, levels * (bet ? 5 : 4));
        }

        /// <summary>One cost field less MulDiv(value, percent, 100) - Windows MulDiv rounds to the
        /// nearest whole unit, halves away from zero - but never below 1 for a field that was
        /// non-zero. A zero field stays zero.</summary>
        private static int MiniaturizeField(int value, int percent)
        {
            if (value == 0)
            {
                return 0;
            }

            long product = (long)value * percent;
            long discount = product >= 0 ? (product + 50) / 100 : -((-product + 50) / 100);
            int result = (int)(value - discount);
            return result <= 0 ? 1 : result;
        }

        /// <summary>
        /// Bleeding Edge Technology's doubling (step 7 of FUN_1050_7d44, exact condition from
        /// behavior-specs-10/race-traits.md section 7): the race has BET, the item has at least
        /// one non-zero requirement, and the margin is 0 or less (in some required field the race
        /// is not yet above the requirement). It is not a "first build" rule - research alone
        /// clears it. Never applied to the Scrap Fleet recovery or computer-player estimates
        /// (see <see cref="CostWithoutBleedingEdgeDoubling"/>).
        /// </summary>
        private static bool BleedingEdgeDoubles(TechLevel requiredTech, Race race, TechLevel currentTechLevels, ItemType type)
        {
            if (!race.HasTrait("BET") || IsExcludedFromMiniaturization(type))
            {
                return false;
            }

            int margin = TechMargin(requiredTech, currentTechLevels, out bool hasRequirement);
            return hasRequirement && margin <= 0;
        }

        /// <summary>
        /// Add a property to the ShipDesign.Summary.
        /// </summary>
        /// <param name="property">
        /// The property to be added to the ShipDesign.Summary.
        /// </param><param name="type">
        /// The type of the property: one of Component.propertyKeys, normally 
        /// the key used to obtain it from a Properties dictionary.
        /// </param>
        private void SumProperty(ComponentProperty property, string type, int componentCount)
        {
            // Capacitors also feed the battle engine's integer capacitor percentage, compounding
            // (100 + rate)/100 per capacitor, truncating, capped at 255 (behavior-specs-9/
            // combat-resolution.md §6) - in addition to the generic Summary sum below, which the
            // design screens display.
            if (type == "Scanner" && property is Scanner fittedScanner)
            {
                scannerNormalFourthPowers += componentCount * Math.Pow(fittedScanner.NormalScan, 4);
                scannerPenetratingFourthPowers += componentCount * Math.Pow(fittedScanner.PenetratingScan, 4);
            }

            if (type == "Capacitor" && property is CapacitorProperty capacitor)
            {
                for (int i = 0; i < componentCount; i++)
                {
                    capacitorPercent = Math.Min(255, capacitorPercent * (100 + (int)capacitor.Value) / 100);
                }
            }

            switch (type)
            {
                // Beam deflectors: x (100 - rate)/100 per deflector (rate 10, so x0.9), on a
                // 1000 scale truncating at each step, so 1/2/3 deflectors let 90/81/72% through
                // (behavior-specs-9/combat-resolution.md §6). Not ProbabilityProperty's own
                // combination, which gives 72.9 at three. The Summary entry holds the deflected
                // share (100 - BeamDeflectorPercent) for display.
                case "Beam Deflector":
                    {
                        int rate = (int)((ProbabilityProperty)property).Value;
                        for (int i = 0; i < componentCount; i++)
                        {
                            beamDeflectorPerMille = beamDeflectorPerMille * (100 - rate) / 100;
                        }

                        double deflected = 100 - (beamDeflectorPerMille / 10);
                        if (Summary.Properties.TryGetValue("Beam Deflector", out ComponentProperty existingDeflector))
                        {
                            ((ProbabilityProperty)existingDeflector).Value = deflected;
                        }
                        else
                        {
                            Summary.Properties.Add("Beam Deflector", new ProbabilityProperty(deflected));
                        }
                    }
                    break;

                // properties that can be summed up to a single property
                case "Armor":
                case "Battle Movement":
                case "Capacitor":
                case "Cargo":
                case "Computer":
                case "Defense":
                case "Driver":
                case "Fuel":
                case "Jammer":
                case "Mass Driver":
                case "Mining Robot":
                case "Movement":
                case "Orbital Adjuster":
                case "Radiation":
                case "Robot":
                case "Scanner":
                case "Shield":
                case "Terraforming":
                    if (Summary.Properties.ContainsKey(type))
                    {
                        ComponentProperty toAdd = property.Clone() as ComponentProperty; // create a copy so scaling doesn't mess it up.
                        toAdd.Scale(componentCount);
                        Summary.Properties[type].Add(toAdd);
                    }
                    else
                    {
                        ComponentProperty toAdd = property.Clone() as ComponentProperty; // create a copy so scaling doesn't mess it up.
                        toAdd.Scale(componentCount);
                        Summary.Properties.Add(type, toAdd);
                    }
                    break;

                // sum up the components in the slot, but keep a separate entry for 'different components'<-- has different meaning for each of these
                case "Bomb":
                    Bomb bomb = property as Bomb;
                    if (bomb.IsSmart)
                    {
                        SmartBombs += bomb * componentCount;
                    }
                    else
                    {
                        ConventionalBombs += bomb * componentCount;
                    }
                    break;
                case "Mine Layer":
                    MineLayer layer = property as MineLayer;
                    if (layer.HitChance == MineLayer.HeavyHitChance)
                    {
                        HeavyMines += layer * componentCount;
                    }
                    else if (layer.HitChance == MineLayer.SpeedTrapHitChance)
                    {
                        SpeedBumbMines += layer * componentCount;
                    }
                    else
                    {
                        StandardMines += layer * componentCount;
                    }
                    break;

                case "Weapon":
                    Weapon weapon = property as Weapon;
                    Weapons.Add(weapon * componentCount);
                    break;

                // Cloak is raw cloak-rating units to be run through CloakCalculator's piecewise
                // curve once, at the fleet level, after combining every design's raw rating (and
                // any race-trait baseline - see AddRawCloakUnits) - a plain arithmetic sum, not
                // ProbabilityProperty's own independent-probability combination rule, which is
                // wrong for this stat (behavior-specs-7/combat-resolution.md §11).
                case "Cloak":
                    AddRawCloakUnits(((ProbabilityProperty)property).Value * componentCount);
                    break;

                // A plain count of installed detectors (not a percentage to combine), consumed
                // by ScanStep's counter-cloak multiplier table (0-17 detectors -> 100%-81%, see
                // behavior-specs-7/combat-resolution.md §11). Reuses ProbabilityProperty purely
                // as a bare numeric container here, for the same reason Cloak bypasses its
                // combination rule above.
                case "Tachyon Detector":
                    {
                        double detectorCount = componentCount;
                        if (Summary.Properties.TryGetValue("Tachyon Detector", out ComponentProperty existingDetector))
                        {
                            ((ProbabilityProperty)existingDetector).Value += detectorCount;
                        }
                        else
                        {
                            Summary.Properties.Add("Tachyon Detector", new ProbabilityProperty(detectorCount));
                        }
                    }
                    break;

                // keep one of each type only - TODO (priority 2) keep the right one
                case "Colonizer":
                case "Engine":
                case "Gate":
                case "Hull":
                case "Mine Layer Efficiency":
                    if (Summary.Properties.ContainsKey(type))
                    {
                        break;
                    }
                    else
                    {
                        Summary.Properties.Add(type, property);
                    }
                    break;

                // Ignore in this context
                case "Hull Affinity":
                case "Transport Ships Only":
                    break;
            }
        }
        
        
        /// <summary>
        /// Calculate fuel consumption.
        /// </summary>
        /// <param name="warp">The speed the ship is travelling.</param>
        /// <param name="race">The race the ship belongs too.</param>
        /// <param name="cargoMass">The mass of any cargo carried (ship mass will be added automatically).</param>
        /// <returns>The ship fuel consumption rate in mg per year.</returns>
        /// <remarks>
        /// Ship_fuel_usage = ship_mass x efficiency x distance / 200
        ///
        /// As distance = speed * time, and we are setting time to 1 year, then we can
        /// just drop speed into the above equation and end up with mg per year. 
        ///
        /// If the secondary racial trait "improved fuel efficiency" is set then
        /// fuel consumption is 15% less than advertised.
        /// </remarks>
        public double FuelConsumption(int warp, Race race, int cargoMass)
        {
            if (warp == 0)
            {
                return 0;
            }
            if (Engine == null)
            {
                return 0; // may be a star base
            }

            int tableValue = Engine.FuelConsumption[warp - 1];

            // Improved Fuel Efficiency is applied to the per-warp table value itself: it "is
            // reduced by 15% of itself, rounded down (so the kept value is the table value minus
            // the truncated 15% share)" (behavior-specs-10/fleet-movement-scanning-cargo.md
            // section 2, step 2). Multiplying the finished figure by 0.85 kept the fraction
            // (Fuel Mizer at warp 8: 235 becomes 200, not 199.75).
            if (race.HasTrait("IFE"))
            {
                tableValue -= tableValue * 15 / 100;
            }

            double efficiency = tableValue / 100.0;
            double speed = warp * warp;

            return (Mass + cargoMass) * efficiency * speed / 200.0;
        }
        
        
        /// <summary>
        /// Removes all allocated components on this design,
        /// but keeps the Hull.
        /// </summary>
        public void ClearAllocated()
        {
            foreach (HullModule module in Hull.Modules)
            {
                module.Empty();
            }
        }
        
        
        /// <summary>
        /// Generate an XmlElement representation of the ShipDesign for saving to file.
        /// Note this uses the minimal approach of storing the ship hull object 
        /// (and recursing through all components). All figured values will need to be 
        /// recalculated on loading.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representing the ShipDesign.</returns>
        public new XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelShipDesign = xmldoc.CreateElement("ShipDesign");
            xmlelShipDesign.AppendChild(base.ToXml(xmldoc));
            Global.SaveData(xmldoc, xmlelShipDesign, "Icon", Icon.Source);
            if (FailedLegality)
            {
                Global.SaveData(xmldoc, xmlelShipDesign, "FailedLegality", "true");
            }
            xmlelShipDesign.AppendChild(Blueprint.ToXml(xmldoc));
            return xmlelShipDesign;
        }

        /// <summary>
        /// Load: initializing Constructor from an xml node.
        /// </summary>
        /// <param name="node">A "ShipDesign" node Nova save file (xml document).</param>
        public ShipDesign(XmlNode node)
            : base(node)
        {
            XmlNode mainNode = node.FirstChild;
            while (mainNode != null)
            {
                try
                {
                    switch (mainNode.Name.ToLowerInvariant())
                    {
                        case "component":
                            Blueprint = new Component(mainNode);
                            break;
                        case "icon":
                            string iconSource = mainNode.FirstChild.Value;
                            Icon = AllShipIcons.Data.GetIconBySource(iconSource);
                            break;
                        case "failedlegality":
                            FailedLegality = bool.Parse(mainNode.FirstChild.Value);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error("Error loading Ship Design : " + e.Message);
                }
                mainNode = mainNode.NextSibling;
            }
        }
    }
}

