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
    using System.Linq;
    using System.Xml;

    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// Fleet class. A fleet is a container for one or more ships (which may be of
    /// different designs). Ship instances do not exist by themselves, they are
    /// always part of a fleet (even if they are the only ship in the fleet).
    /// A fleet may be a starbase.
    /// </summary>
    [Serializable]
    public class Fleet : Mappable
    {   
        /// <summary>
        /// Holds the ship tokens in the format "ShipDesign, Quantity, Damage%".
        /// </summary>
        private Dictionary<long, ShipToken> tokens  = new Dictionary<long, ShipToken>();
        public List<Waypoint> Waypoints   = new List<Waypoint>();

        /// <summary>
        /// The cargo carried by the entire fleet. 
        /// To avoid issues with duplication cargo is tracked at the fleet level only.
        /// </summary>
        public Cargo Cargo = new Cargo(); 
        
        public Mappable InOrbit = null;
        public double Bearing = 0;
        public double Cloaked = 0;
        public double FuelAvailable = 0;
        public double TargetDistance = 100;
        public string BattlePlan = "Default";

        /// <summary>
        /// The Repeat Orders flag (fleet byte 5 bit 0x02, order record type 10): each waypoint the
        /// fleet reaches is re-appended, task included, to the end of its route, so the route
        /// cycles (behavior-specs-10/fleet-movement-scanning-cargo.md §5; see
        /// TurnGenerator.UpdateFleet). A reached fleet-targeted (intercept) leg is never recycled.
        /// </summary>
        public bool RepeatOrders = false;

        public enum TravelStatus 
        { 
            Arrived, InTransit 
        }

        /// <summary>
        /// Return the total normal bombing capability.
        /// </summary>
        public Bomb BombCapability
        {
            get
            {
                Bomb totalBombs = new Bomb();
                foreach (ShipToken token in tokens.Values)
                {
                    Bomb bomb = token.Design.BombCapability * token.Quantity;
                    totalBombs.PopKill += bomb.PopKill;
                    totalBombs.Installations += bomb.Installations;
                    totalBombs.MinimumKill += bomb.MinimumKill;
                }
                return totalBombs;
            }
        }

        /// <summary>
        /// Check if any of the ships has colonization module.
        /// </summary>
        public bool CanColonize
        {
            get
            {
                foreach (ShipToken token in tokens.Values)
                {
                    if (token.Design.CanColonize)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Property to determine if a fleet can re-fuel.
        /// </summary>
        public bool CanRefuel
        {
            get
            {
                foreach (ShipToken token in tokens.Values)
                {
                    if (token.Design.CanRefuel)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// This property is true if the fleet has at least one ship with a scanner.
        /// </summary>
        public bool CanScan
        {
            get
            {
                foreach (ShipToken token in tokens.Values)
                {
                    if (token.Design.CanScan)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Return the composition of a fleet (ship design and number of ships of that
        /// design).
        /// </summary>
        public Dictionary<long, ShipToken> Composition
        {
            get
            {
                return tokens;
            }
        }
        
        
        /// <summary>
        /// Return Free Warp speed for fleet.
        /// </summary>
        public int FreeWarpSpeed
        {
            get
            {
                int speed = 10;
                foreach (ShipToken token in tokens.Values)
                {
                    speed = Math.Min(speed, token.Design.FreeWarpSpeed);
                }

                return speed;
            }
        }

        /// <summary>
        /// Determine if the fleet has bombers.
        /// </summary>
        public bool HasBombers
        {
            get
            {
                bool bombers = false;
                foreach (ShipToken token in tokens.Values)
                {
                    if (token.Design.IsBomber)
                    {
                        bombers = true;
                    }
                }
                return bombers;
            }
        }

        /// <summary>
        /// Choose an image from one of the ships in the fleet.
        /// </summary>
        public ShipIcon Icon
        {
            get
            {
                try
                {
                    ShipToken token = tokens.Values.First();
                    return token.Design.Icon;
                }
                catch
                {
                    Report.Error("Fleet.cs Fleet.Icon (get): unable to get ship image.");
                }
                return null;
            }
        }

        /// <summary>
        /// Report if a fleet is armed.
        /// </summary>
        public bool IsArmed
        {
            get
            {
                foreach (ShipToken token in tokens.Values)
                {
                    if (token.Design.HasWeapons)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Property to determine if a fleet is a starbase.
        /// </summary>
        public bool IsStarbase
        {
            get
            {
                foreach (ShipToken token in tokens.Values)
                {
                    if (token.Design.IsStarbase)
                    {
                        return true;
                    }
                }
                return false;
            }
        }
        
        /// <summary>
        /// Return the mass of a fleet.
        /// </summary>
        public int Mass
        {
            get
            {
                int totalMass = 0;

                foreach (ShipToken token in tokens.Values)
                {
                    totalMass += token.Design.Mass * token.Quantity;
                }
                totalMass += Cargo.Mass;

                return totalMass;
            }
        }

        /// <summary>
        /// Return the number of mines (all three field types together) this fleet lays in a
        /// year while stationary. Zero means no ship carries a mine-laying part (message 191).
        /// </summary>
        public int NumberOfMines
        {
            get
            {
                return MinesPerYear(MinefieldType.Standard) + MinesPerYear(MinefieldType.Heavy) + MinesPerYear(MinefieldType.SpeedBump);
            }
        }

        /// <summary>
        /// The mines of one field type this fleet lays in a full (stationary) year:
        /// behavior-specs-10/turn-generation-engine.md section 3, "Mine laying, exact rule".
        /// The fleet total is the sum of ship count x design figure. A design's figure is the sum
        /// over its parts of quantity x the part's mines per year (Mine Dispensers and the Multi
        /// Contained Munition lay Standard, Heavy Dispensers Heavy, Speed Traps Speed Bump; a
        /// Munition counts as 40 standard mines), doubled on the Mini Mine Layer and Super Mine
        /// Layer hulls (their "Mine Layer Efficiency" property).
        /// </summary>
        public int MinesPerYear(MinefieldType fieldType)
        {
            int total = 0;
            foreach (ShipToken token in tokens.Values)
            {
                total += DesignMinesPerYear(token.Design, fieldType) * token.Quantity;
            }

            return total;
        }

        /// <summary>One ship's yearly mines of the given type (see <see cref="MinesPerYear"/>).</summary>
        public static int DesignMinesPerYear(ShipDesign design, MinefieldType fieldType)
        {
            if (design == null || design.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull"))
            {
                return 0;
            }

            design.Update();

            int figure;
            switch (fieldType)
            {
                case MinefieldType.Heavy:
                    figure = design.HeavyMines.LayerRate;
                    break;
                case MinefieldType.SpeedBump:
                    figure = design.SpeedBumbMines.LayerRate;
                    break;
                default:
                    figure = design.StandardMines.LayerRate;
                    foreach (HullModule module in design.Hull.Modules)
                    {
                        if (module.AllocatedComponent != null && module.AllocatedComponent.Name == MultiContainedMunitionName)
                        {
                            figure += MultiContainedMunitionMines * module.ComponentCount;
                        }
                    }
                    break;
            }

            if (design.Summary.Properties.TryGetValue("Mine Layer Efficiency", out ComponentProperty efficiency)
                && efficiency is DoubleProperty efficiencyValue
                && efficiencyValue.Value > 1)
            {
                figure = (int)(figure * efficiencyValue.Value);
            }

            return figure;
        }

        /// <summary>The Multi Contained Munition counts as this many standard mines per unit.</summary>
        public const int MultiContainedMunitionMines = 40;

        public const string MultiContainedMunitionName = "Multi Contained Munition";

        /// <summary>Total remote-mining capacity (mine-equivalents) this fleet contributes this
        /// turn, capped at Global.MaxRemoteMiningEquivalents per fleet -
        /// docs/behavior-specs-4/population-growth.md: "any additional mining capacity stacked
        /// into the same fleet beyond that produces no extra minerals... splitting the same total
        /// mine-equivalents across more, smaller fleets always mines less in total than
        /// concentrating them."</summary>
        public int MineEquivalents
        {
            get
            {
                int mineEquivalents = 0;

                foreach (ShipToken token in tokens.Values)
                {
                    mineEquivalents += token.Design.MineEquivalents * token.Quantity;
                }

                return Math.Min(mineEquivalents, Global.MaxRemoteMiningEquivalents);
            }
        }


        /// <summary>
        /// Return the penetrating range scan capability of the fleet.
        /// </summary>
        /// <remarks>
        /// This previously carried a FIXME suggesting fleet-wide scan ranges should combine
        /// additively across ships via the documented fourth-root formula (see
        /// ShipDesign.cs / Scanner.cs operator+, and
        /// docs/behavior-specs/fleet-movement-scanning-cargo.md §3). That formula is sourced
        /// only for combining multiple scanner *components on one ship design* — no source found
        /// documents combining scan range *across different ships in a fleet* the same way, so
        /// taking the best single ship's range (as below) is left as-is rather than guessing at
        /// an unsourced fleet-wide combination rule.
        /// </remarks>
        public int PenScanRange
        {
            get
            {
                int penRange = 0;
                
                foreach (ShipToken token in tokens.Values)
                {
                    if (token.Design.ScanRangePenetrating > penRange)
                    {
                        penRange = token.Design.ScanRangePenetrating;
                    }
                }
                return penRange;
            }
        }
        
        /// <summary>
        /// Return the non penetrating range scan capability of the fleet.
        /// See the remarks on PenScanRange above regarding fleet-wide combination.
        /// </summary>
        public int ScanRange
        {
            get
            {
                int scanRange = 0;
                
                foreach (ShipToken token in tokens.Values)
                {
                    if (token.Design.ScanRangeNormal > scanRange)
                    {
                        scanRange = token.Design.ScanRangeNormal;
                    }
                }
                return scanRange;
            }
        }

        /// <summary>
        /// Return the current speed of the fleet.
        /// </summary>
        public int Speed
        {
            get
            {
                Waypoint target = Waypoints[0];
                return target.WarpFactor;
            }

            set
            {
                Waypoint target = Waypoints[0];
                target.WarpFactor = 0;
            }
        }

        /// <summary>
        /// Return the current total amour strength of the fleet.
        /// </summary>
        public double TotalArmorStrength
        {
            get
            {
                return tokens.Values.Sum(token => token.Armor);  // note: token.Armour is a total so no need to * by quantity
            }
        }

        /// <summary>
        /// Find the total cargo capacity of the fleet.
        /// </summary>
        public int TotalCargoCapacity
        {
            get
            {
                return tokens.Values.Sum(token => token.Design.CargoCapacity * token.Quantity);
            }
        }

        /// <summary>
        /// Return the cost of a fleet. 
        /// </summary>
        public Resources TotalCost
        {
            get
            {
                Resources cost = new Resources();

                foreach (ShipToken token in tokens.Values)
                {
                    cost += token.Design.Cost * token.Quantity;
                }

                return cost;
            }
        }

        /// <summary>
        /// Find the total dock capacity of the fleet.
        /// </summary>
        public int TotalDockCapacity
        {
            get
            {
                return tokens.Values.Sum(token => token.Design.DockCapacity);
            }
        }

        /// <summary>
        /// Find the total fuel capacity of all ships in the fleet.
        /// </summary>
        public int TotalFuelCapacity
        {
            get
            {
                return tokens.Values.Sum(token => token.Design.FuelCapacity * token.Quantity);
            }
        }

        /// <summary>
        /// Return the total shield strength of the fleet.
        /// </summary>
        public double TotalShieldStrength
        {
            get
            {
                return tokens.Values.Sum(token => token.Shields);  // note token.Shields is a total so no need to multiply by quantity
            }
        }

        /// <summary>The Pick Pocket Scanner's component name.</summary>
        public const string PickPocketScannerName = "Pick Pocket Scanner";

        /// <summary>The Robber Baron Scanner's component name (components.xml spells it
        /// "Robber Barron Scanner"; both spellings are recognised).</summary>
        public const string RobberBaronScannerName = "Robber Baron Scanner";

        /// <summary>
        /// The cargo-theft scanner ability against fleets: some ship in the fleet carries a Pick
        /// Pocket or Robber Baron Scanner (the scanner-class flag of the fleet's combined scan
        /// result is an OR over its stacks; behavior-specs-10/fleet-movement-scanning-cargo.md §4
        /// "Caps on a load" and "Theft").
        /// </summary>
        public bool CanStealFromFleets
        {
            get { return CarriesComponent(PickPocketScannerName) || CarriesRobberBaron(); }
        }

        /// <summary>
        /// The cargo-theft ability against another race's planet's surface minerals: a Robber
        /// Baron Scanner (the in-game help: the Pick Pocket sees and steals fleet cargo, the
        /// Robber Baron also planets' surface minerals).
        /// </summary>
        public bool CanStealFromPlanets
        {
            get { return CarriesRobberBaron(); }
        }

        private bool CarriesRobberBaron()
        {
            return CarriesComponent(RobberBaronScannerName) || CarriesComponent("Robber Barron Scanner");
        }

        /// <summary>True when some ship design in the fleet mounts the named component.</summary>
        public bool CarriesComponent(string componentName)
        {
            foreach (ShipToken token in tokens.Values)
            {
                ShipDesign design = token.Design;
                if (token.Quantity <= 0 || design?.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull"))
                {
                    continue;
                }

                foreach (HullModule module in design.Hull.Modules)
                {
                    if (module.AllocatedComponent != null && module.ComponentCount > 0
                        && module.AllocatedComponent.Name == componentName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The fuel (mg) the fleet needs for the rest of its route: from its position through
        /// waypoints <paramref name="fromIndex"/> onward, each leg at its own warp at the fleet's
        /// current consumption rate (Load Optimal, §4). Warp 0 and Stargate legs need none.
        /// </summary>
        public int FuelRequiredForRoute(Race race, int fromIndex)
        {
            double need = 0;
            NovaPoint from = Position;

            for (int i = Math.Max(0, fromIndex); i < Waypoints.Count; i++)
            {
                Waypoint leg = Waypoints[i];
                if (leg.Position == null)
                {
                    continue;
                }

                int warp = leg.WarpFactor;
                if (warp > 0 && warp <= 10 && from != null)
                {
                    double distance = PointUtilities.Distance(from, leg.Position);
                    need += FuelConsumption(warp, race) * distance / (warp * warp);
                }

                from = leg.Position;
            }

            return (int)Math.Ceiling(need);
        }


        /// <summary>
        /// Placeholder constructor - Fleet should be replaced by a reference to the fleet with the same Key.
        /// </summary>
        public Fleet(long newKey) 
        { 
            Key = newKey; 
        }

        
        /// <summary>
        /// Fleet construction for unit testing and stack creation during a battle.
        /// </summary>
        /// <param name="name">The fleet name.</param>
        /// <param name="id">The fleet id.</param>
        /// <param name="position">The fleet position.</param>
        public Fleet(string name, ushort owner, uint id, NovaPoint position)
        {
            Name = name;
            Owner = owner;
            Id = id;
            Position = position;
        }
        
        public Fleet(Fleet copy)
            : base(copy)
        {
        }
        
        /// <summary>
        /// Fleet construction based on a ShipToken and some parameters from a star (this is
        /// the usual case for most fleets when a new ship is manufactured at a star).
        /// </summary>
        /// <param name="ship">The ShipToken being constructed.</param>
        /// <param name="star">The star constructing the ship.</param>
        public Fleet(ShipToken token, Star star, long newKey)
        {
            tokens.Add(token.Key, token);

            FuelAvailable = TotalFuelCapacity;
            Type          = ItemType.Fleet;

            // Have one waypoint to reflect the fleet's current position and the
            // planet it is in orbit around.
         
            Waypoint w    = new Waypoint();      
            w.Position    = star.Position;
            w.Destination = star.Name;
            w.WarpFactor  = 0;

            Waypoints.Add(w);

            // Inititialise the fleet elements that come from the star.

            Position     = star.Position;       
            InOrbit      = star;                
            Key          = newKey;    
        }
        
        
        /// <summary>
        /// Fleet construction based on a ship and some parameters from a star (this is
        /// the usual case for most fleets when a new ship is manufactured at a star).
        /// </summary>
        /// <param name="ship">The ship being constructed.</param>
        /// <param name="star">The star constructing the ship.</param>
        public Fleet(ShipDesign design, int quantity, Star star, long newKey) :
            this(new ShipToken(design, quantity), star, newKey)
        {
        }


        public TravelStatus GetTravelStatus()
        {
            Waypoint target = Waypoints[0];
            if (Position == target.Position)
            {
                return TravelStatus.Arrived;
            }
            else
            {
                return TravelStatus.InTransit;
            }
        }

        
        /// <summary>
        /// Move the fleet towards the waypoint at the top of the list. Fuel is consumed
        /// at the rate of the sum of each of the individual ships (i.e. available fuel
        /// is automatically "pooled" between the ships).
        /// </summary>
        /// <param name="availableTime">The portion of a year left for travel.</param>
        /// <param name="race">The race this fleet belongs to.</param>
        /// <returns>A TravelStatus indicating arrival or in-transit.</returns>
        public TravelStatus Move(ref double availableTime, Race race)
        {
            if (GetTravelStatus() == TravelStatus.Arrived)
            {
                return TravelStatus.Arrived;
            }

            Waypoint target = Waypoints[0];

            InOrbit = null;

            double legDistance = PointUtilities.Distance(Position, target.Position);

            int warpFactor = target.WarpFactor;
            int speed = warpFactor * warpFactor;
            double targetTime = legDistance / speed;
            double fuelConsumptionRate = FuelConsumption(warpFactor, race);
            double fuelTime = FuelAvailable / fuelConsumptionRate;
            double travelTime = targetTime;

            // Determine just how long we have available to travel towards the
            // waypoint target. This will be the smaller of target time (the ideal
            // case, we get there) available time (didn't get there but still can
            // move towards there next turn) and fuel time.

            TravelStatus arrived = TravelStatus.Arrived;

            if (travelTime > availableTime)
            {
                travelTime = availableTime;
                arrived = TravelStatus.InTransit;
            }

            if (travelTime >= fuelTime)
            {
                travelTime = fuelTime;
                arrived = TravelStatus.InTransit;
            }
            
            // If we have arrived then the new fleet position is the waypoint
            // target. Otherwise the position is determined by how far we got
            // in the time or fuel available.

            if (arrived == TravelStatus.Arrived)
            {
                Position = target.Position;
                target.WarpFactor = 0;
            }
            else
            {
                double travelled = speed * travelTime;
                Position = PointUtilities.MoveTo(Position, target.Position, travelled);
            }

            // Update the travel time left for this year and the total fuel we
            // now have available.

            availableTime -= travelTime;

            // The usage figure is kept in tenths of a mg and the tenths are rounded up ("adds 9
            // and then divides by 10"), and the result is subtracted from the tank, clamped at
            // zero (behavior-specs-10/fleet-movement-scanning-cargo.md section 2, steps 5 and the
            // caller-side note). Truncating charged 37.8 mg as 37.
            long fuelTenths = (long)Math.Floor((fuelConsumptionRate * travelTime * 10) + 1e-9);
            int fuelUsed = (int)((fuelTenths + 9) / 10);
            FuelAvailable = Math.Max(0, FuelAvailable - fuelUsed);

            double distanceTravelled = speed * travelTime;
            FuelAvailable += FuelGeneration(warpFactor, distanceTravelled);
            if (FuelAvailable > TotalFuelCapacity)
            {
                FuelAvailable = TotalFuelCapacity;
            }

            // Added check if fleet run out of full it's speed will be changed 
            // to free warp speed.
            if (arrived == TravelStatus.InTransit && fuelConsumptionRate > this.FuelAvailable)
            {
                target.WarpFactor = this.FreeWarpSpeed;
            }
            return arrived;
        }

        /// <summary>
        /// Return the fuel consumption (mg per year) of the fleet at the specified
        /// warp factor.
        /// </summary>
        /// <param name="warpFactor">The warp speed of the fleet.</param>
        /// <param name="race">The race this fleet belongs too.</param>
        /// <returns>The rate of fuel consumption in mg / year.</returns>
        public double FuelConsumption(int warpFactor, Race race)
        {
            double fuelConsumption = 0;

            // Work out how full of cargo the fleet is.
            double cargoFullness;
            if (TotalCargoCapacity == 0)
            {
                cargoFullness = 0;
            }
            else
            {
                cargoFullness = ((double)Cargo.Mass) / ((double)TotalCargoCapacity);
            }


            // Each design's figure counts once per ship: the calculator sums "the design's mass
            // times its ship count" (behavior-specs-10/fleet-movement-scanning-cargo.md section 2,
            // step 3). A stack of ten burned the fuel of one.
            foreach (ShipToken token in tokens.Values)
            {
                fuelConsumption += token.Design.FuelConsumption(warpFactor, race, (int)(token.Design.CargoCapacity * cargoFullness)) * token.Quantity;
            }

            return fuelConsumption;
        }

        /// <summary>
        /// Fuel generated this move by ramscoop engines running below their "free" warp
        /// threshold. Per docs/behavior-specs/fleet-movement-scanning-cargo.md §2: 0 above the
        /// free-travel warp, distance-for-distance at exactly the free-travel warp, then 3x/6x/10x
        /// distance for 1/2/3-or-more warp factors below it. This is per engine, so scales with
        /// the number of ships in each token.
        /// </summary>
        /// <remarks>
        /// This is a simplified, rule-based approximation. The spec's own sourced generation
        /// table gives specific mg values per named engine per warp speed rather than a clean
        /// formula, and isn't reproduced here — this formula is the documented general step
        /// pattern, not a verbatim per-engine table.
        ///
        /// Every engine - ramscoop or not - also generates exactly 1mg at warp 1 specifically
        /// (docs/behavior-specs-7/fleet-movement-scanning-cargo.md: "every conventional
        /// (non-scoop) engine generating exactly 1 mg at warp 1 ... the mechanical basis for
        /// treating warp 1 as an always-available, self-sustaining crawl speed"). Without this,
        /// a fleet with only ordinary (non-ramscoop) engines that ran out of fuel had no way to
        /// ever generate more and could never move again, contradicting that guarantee.
        /// </remarks>
        /// <summary>
        /// Recomputes this fleet's effective cloak percentage (<see cref="Cloaked"/>) from its
        /// current composition - a mass-weighted average of each installed design's raw
        /// cloak-rating units (see ShipDesign.Update, which already folds in installed cloak
        /// components and any Super Stealth/Improved Starbases trait baseline), run through
        /// CloakCalculator's single piecewise curve exactly once. behavior-specs-7/
        /// combat-resolution.md §11: "weight = design mass x count of that design in the fleet
        /// ... not a simple sum and not just the single best-cloaked design." Nothing else in
        /// this codebase currently recomputes Cloaked as composition changes, so ScanStep calls
        /// this fresh for every fleet before using it as a scan target, rather than relying on a
        /// cached value that could go stale.
        /// </summary>
        public void RecalculateCloak(Race race)
        {
            double weightedRawUnitsSum = 0;
            double totalWeight = 0;

            foreach (ShipToken token in tokens.Values)
            {
                token.Design.Update(race);

                double rawUnits = 0;
                if (token.Design.Summary.Properties.TryGetValue("Cloak", out ComponentProperty cloak))
                {
                    rawUnits = ((ProbabilityProperty)cloak).Value;
                }

                double weight = token.Design.Mass * token.Quantity;
                weightedRawUnitsSum += rawUnits * weight;
                totalWeight += weight;
            }

            double weightedAverageRawUnits = totalWeight > 0 ? weightedRawUnitsSum / totalWeight : 0;
            Cloaked = CloakCalculator.PercentFromRawUnits(weightedAverageRawUnits);
        }

        /// <summary>
        /// Fuel (mg) this fleet makes per year when it is not refuelling at a friendly starbase: 50
        /// per Anti-matter Generator carried plus 200 per ship of the Fuel Transport and Super-Fuel
        /// Transport hulls (behavior-specs-8/turn-generation-engine.md section 1 step 22). Ramscoop
        /// engines play no part in this pass - their fuel comes from travelling.
        /// </summary>
        public double PassiveFuelGeneration
        {
            get
            {
                double generated = 0;

                foreach (ShipToken token in tokens.Values)
                {
                    generated += token.Design.FuelGenerationPerYear * token.Quantity;

                    if (token.Design.IsFuelTransportHull)
                    {
                        generated += Global.FuelTransportFuelPerShip * token.Quantity;
                    }
                }

                return generated;
            }
        }

        private double FuelGeneration(int warpFactor, double distance)
        {
            double generated = 0;

            foreach (ShipToken token in tokens.Values)
            {
                Engine engine = token.Design.Engine;
                if (engine == null)
                {
                    continue;
                }

                double perEngineFactor;

                if (!engine.RamScoop)
                {
                    perEngineFactor = warpFactor == 1 ? 1 : 0;
                }
                else
                {
                    int belowFreeWarp = engine.FreeWarpSpeed - warpFactor;

                    if (belowFreeWarp < 0)
                    {
                        perEngineFactor = 0;
                    }
                    else if (belowFreeWarp == 0)
                    {
                        perEngineFactor = 1;
                    }
                    else if (belowFreeWarp == 1)
                    {
                        perEngineFactor = 3;
                    }
                    else if (belowFreeWarp == 2)
                    {
                        perEngineFactor = 6;
                    }
                    else
                    {
                        perEngineFactor = 10;
                    }
                }

                generated += perEngineFactor * distance * token.Quantity;
            }

            return generated;
        }

        /// <summary>
        /// Calculate the fuel required for this fleet to reach a given destination.
        /// </summary>
        /// <param name="warpFactor">The warp speed to travel at.</param>
        /// <param name="race">The race operating the fleet.</param>
        /// <param name="dest">The destination as a <see cref="NovaPoint"/>.</param>
        /// <returns>The estimated fuel consumption.</returns>
        /// <remarks>
        /// FIXME (priority 4) - probably has rounding errors.
        /// FIXME (priority 3) - should this account for final year slow down?.
        /// </remarks>
        public int GetFuelRequired(int warpFactor, Race race, NovaPoint dest)
        {
            double fuelConsumption = FuelConsumption(warpFactor, race);
            double time = PointUtilities.DistanceSquare(this.Position, dest) / (warpFactor * warpFactor * warpFactor * warpFactor);
            return (int)(time * fuelConsumption);
        }

        /// <summary>
        /// Load: initializing constructor to load a fleet from an XmlNode (save file).
        /// </summary>
        /// <param name="node">An XmlNode representing the fleet.</param>
        public Fleet(XmlNode node)
            : base(node)
        {
            // Read the node
            XmlNode mainNode = node.FirstChild;
            try
            {
                while (mainNode != null)
                {
                    switch (mainNode.Name.ToLowerInvariant())
                    {
                        case "fleetid":
                            Id = uint.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "cargo":
                            Cargo = new Cargo(mainNode);
                            break;
                        case "inorbit":
                            InOrbit = new Star();
                            InOrbit.Name = mainNode.FirstChild.Value;
                            break;
                        case "bearing":
                            Bearing = double.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "cloaked":
                            Cloaked = double.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "fuelavailable":
                            FuelAvailable = double.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "targetdistance":
                            TargetDistance = double.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "battleplan":
                            BattlePlan = mainNode.FirstChild.Value;
                            break;
                        case "repeatorders":
                            RepeatOrders = bool.Parse(mainNode.FirstChild.Value);
                            break;
                        case "tokens":
                            XmlNode subNode = mainNode.FirstChild;
                            ShipToken token;
                            while (subNode != null)
                            {
                                token = new ShipToken(subNode);
                                tokens.Add(token.Key, token);
                                subNode = subNode.NextSibling;
                            }
                            break;
                        case "waypoint":
                            Waypoint waypoint = new Waypoint(mainNode);
                            Waypoints.Add(waypoint);
                            break;

                        default: break;
                    }


                    mainNode = mainNode.NextSibling;
                }
            }
            catch (Exception e)
            {
                Report.Error("Error loading fleet:" + Environment.NewLine + e.Message);
                throw e;
            }
        }


        /// <summary>
        /// Save: Return an XmlElement representation of the Fleet.
        /// </summary>
        /// <param name="xmldoc">The parent xml document.</param>
        /// <returns>An XmlElement representation of the Fleet.</returns>
        public new XmlElement ToXml(XmlDocument xmldoc, string nodeName = "Fleet")
        {
            XmlElement xmlelFleet = xmldoc.CreateElement(nodeName);

            xmlelFleet.AppendChild(base.ToXml(xmldoc));
            
            if (InOrbit != null)
            {
                Global.SaveData(xmldoc, xmlelFleet, "InOrbit", InOrbit.Name);
            }

            Global.SaveData(xmldoc, xmlelFleet, "Bearing", this.Bearing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            
            if (Cloaked != 0)
            {
                Global.SaveData(xmldoc, xmlelFleet, "Cloaked", this.Cloaked.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            Global.SaveData(xmldoc, xmlelFleet, "FuelAvailable", this.FuelAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelFleet, "FuelCapacity", this.TotalFuelCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelFleet, "TargetDistance", this.TargetDistance.ToString(System.Globalization.CultureInfo.InvariantCulture));
            
            if (Cargo.Mass > 0)
            {
                Global.SaveData(xmldoc, xmlelFleet, "CargoCapacity", this.TotalCargoCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            
            Global.SaveData(xmldoc, xmlelFleet, "BattlePlan", this.BattlePlan);

            if (RepeatOrders)
            {
                Global.SaveData(xmldoc, xmlelFleet, "RepeatOrders", RepeatOrders.ToString());
            }

            xmlelFleet.AppendChild(Cargo.ToXml(xmldoc));

            foreach (Waypoint waypoint in Waypoints)
            {
                xmlelFleet.AppendChild(waypoint.ToXml(xmldoc));
            }

            XmlElement xmlelTokens = xmldoc.CreateElement("Tokens");
            foreach (ShipToken token in tokens.Values)
            {
                xmlelTokens.AppendChild(token.ToXml(xmldoc));
            }            
            xmlelFleet.AppendChild(xmlelTokens);

            return xmlelFleet;
        }
        
        public FleetIntel GenerateReport(ScanLevel scan, int year)
        {
            FleetIntel report = new FleetIntel(this, scan, year);
            
            return report;
        }
    }
}
