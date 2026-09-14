#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Server
{
    using System;    
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Class to process a new turn.
    /// </summary>
    public class TurnGenerator
    {
        private ServerData serverState;        
        private SortedList<int, ITurnStep> turnSteps;
        private Random rand;
        
        // Used to order turn steps.
        private const int FIRSTSTEP = 00;
        // Runs before STARSTEP so a planet's own mines (only ever processed for owned, colonized
        // stars) see this turn's already-depleted concentration if a remote-mining fleet also
        // worked the same star - docs/behavior-specs-4/population-growth.md describes multiple
        // mining sources at one star as strictly sequential, though doesn't mandate which comes
        // first; this is a disclosed, reasonable ordering choice, not a spec requirement.
        private const int REMOTEMININGSTEP = 11;
        private const int STARSTEP = 12;

        // Stargate overgating "vanish chance" constants - docs/behavior-specs-4/
        // fleet-movement-scanning-cargo.md §5 labels these a "community-fitted approximation,"
        // lower-confidence than the (independently sourced) damage-percentage formulas they're
        // used alongside. MASS_VANISH_FITTED_CONSTANT is the spec's own "fitted constant A ≈ 68".
        // INTERSTELLAR_TRAVELER_VANISH_SCALE represents Interstellar Traveler's "reduced (but not
        // quantified) chance of losing overgated ships" - 0.5 is a placeholder, not a sourced
        // number, chosen only because the spec confirms the reduction exists without giving a
        // figure.
        private const double MASS_VANISH_FITTED_CONSTANT = 68;
        private const double INTERSTELLAR_TRAVELER_VANISH_SCALE = 0.5;
        private const int BOMBINGSTEP = 19;
        private const int WORMHOLEDRIFTSTEP = 20;
        private const int SCANSTEP = 99;
        
        // TODO: (priority 5) refactor all these into ITurnStep(s).
        private OrderReader orderReader;
        private IntelWriter intelWriter;
        private BattleEngine battleEngine;
        private Bombing bombing;
        private CheckForMinefields checkForMinefields;
        private LayMines layMines;
        private Manufacture manufacture;
        private Scores scores;
        private VictoryCheck victoryCheck;
        
        /// <summary>
        /// Construct a turn processor. 
        /// </summary>
        public TurnGenerator(ServerData serverState)
        {
            this.serverState = serverState;            
            turnSteps = new SortedList<int, ITurnStep>();
            rand = new Random();
            
            // Now that there is a state, comopose the turn processor.
            // TODO ??? (priority 4): Use dependency injection for this? It would
            // generate a HUGE constructor call... a factory to
            // abstract it perhaps? -Aeglos
            orderReader = new OrderReader(this.serverState);
            battleEngine = new BattleEngine(this.serverState, new BattleReport());
            bombing = new Bombing(this.serverState);
            checkForMinefields = new CheckForMinefields(this.serverState);
            layMines = new LayMines(this.serverState);
            manufacture = new Manufacture(this.serverState);
            scores = new Scores(this.serverState);
            intelWriter = new IntelWriter(this.serverState, this.scores);
            victoryCheck = new VictoryCheck(this.serverState, this.scores);
            
            turnSteps.Add(SCANSTEP, new ScanStep());
            turnSteps.Add(BOMBINGSTEP, new BombingStep());
            turnSteps.Add(STARSTEP, new StarUpdateStep());
            turnSteps.Add(REMOTEMININGSTEP, new RemoteMiningStep());
            turnSteps.Add(WORMHOLEDRIFTSTEP, new WormholeDriftStep());
        }
        
        /// <summary>
        /// Generate a new turn by reading in the player turn files to update the master
        /// copy of stars, ships, etc. Then do the processing required to take in the
        /// passage of one year of time and, finally, write out the new turn file.
        /// </summary>
        public void Generate()
        {
            BackupTurn();

            // For now, just copy the command stacks right away.
            // TODO (priority 6): Integrity check the new turn before
            // updating the state (cheats, errors).
            ReadOrders();

            // for all commands of all empires: command.ApplyToState(empire);
            // for WaypointCommand: Add Waypoints to Fleets.
            ParseCommands();

            // Only one traded tech level (scrapping/battle/invasion) is allowed per empire per
            // turn — see docs/behavior-specs/research-tech-tree.md §6.
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                empire.TechGainedThisTurn = false;
            }

            // Do all fleet movement and actions 
            // TODO (priority 4) - split this up into waypoint zero and waypoint 1 actions

            // ToDo: Step 1 --> Scrap Fleet if waypoint 0 order; here, and only here.
            // ToDo: ScrapFleetStep / foreach ITurnStep for waypoint 0. Own TurnStep-List for Waypoint 0?
            new ScrapFleetStep().Process(serverState);

            // remove battle from old turns
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                empire.BattleReports.Clear();
            }

            // Combat resolves BEFORE fleet movement/waypoint-task execution this turn -
            // docs/behavior-specs-4/turn-generation-engine.md's phase order puts combat detection
            // /resolution at phase 9, well before the economic/waypoint-task pass (11) and fleet
            // movement execution (13). Previously this ran AFTER the movement loop below, so a
            // fleet that should have been destroyed in battle could still execute its orders (move,
            // colonize, invade, etc.) that same turn. Combat groups fleets purely by fleet.Position
            // (see BattleEngine.Run), a value that already carries over correctly from the end of
            // the PREVIOUS turn's movement, so nothing here depends on this turn's movement having
            // run first.
            battleEngine.Run();

            serverState.CleanupFleets();

            foreach (Fleet fleet in serverState.IterateAllFleets())
            {
                ProcessFleet(fleet); // ToDo: don't scrap fleets here at waypoint 1
            }
            serverState.CleanupFleets();

            victoryCheck.Victor();

            serverState.TurnYear++;
            
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                empire.TurnYear = serverState.TurnYear;
                empire.TurnSubmitted = false;
            }
                       
            foreach (ITurnStep turnStep in turnSteps.Values)
            {
                turnStep.Process(serverState);    
            }
            
            WriteIntel();

            // remove old messages, do this last so that the 1st turn intro message is not removed before it is delivered.
            serverState.AllMessages = new List<Message>();
            
            CleanupOrders();
        }
        

        protected virtual void WriteIntel()
        {
            intelWriter.WriteIntel();
        }

        protected virtual void ReadOrders()
        {
            orderReader.ReadOrders();
        }

        /// <summary>
        /// Validates and applies all commands sent by the clients and read for this turn.
        /// </summary>
        protected virtual void ParseCommands()
        {
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                if (serverState.AllCommands.ContainsKey(empire.Id))
                {                
                    while (serverState.AllCommands[empire.Id].Count > 0)
                    {
                        ICommand command = serverState.AllCommands[empire.Id].Pop();
                        
                        if (command.IsValid(empire))
                        {
                            command.ApplyToState(empire);
                        }
                        else
                        {
                            // TODO (priority 4) - Flag invalid orders to all players. Preferably give some indication of why it is invalid for debugging custom clients/AIs.
                        }
                    }
                }
                
                foreach (Star star in empire.OwnedStars.Values)
                {
                    serverState.AllStars[star.Key] = star;
                }
            }
        }
        
        /// <summary>
        /// Delete order files, done after turn generation.
        /// </summary>
        protected virtual void CleanupOrders()
        {
            // Delete orders on turn generation.
            // Copy each file into it�s new directory.
            DirectoryInfo source = new DirectoryInfo(serverState.GameFolder);
            foreach (FileInfo fi in source.GetFiles())
            {
                if (fi.Name.ToLower().EndsWith(Global.OrdersExtension))
                {
                    File.Delete(fi.FullName);
                }
            }
        }

        /// <summary>
        /// Copy all turn files to a sub-directory prior to generating the new turn.
        /// </summary>
        protected virtual void BackupTurn()
        {
            // TODO (priority 3) - Add a setting to control the number of backups.
            int currentTurn = serverState.TurnYear;
            string gameFolder = serverState.GameFolder;


            try
            {
                string backupFolder = Path.Combine(gameFolder, currentTurn.ToString());
                DirectoryInfo source = new DirectoryInfo(gameFolder);
                DirectoryInfo target = new DirectoryInfo(backupFolder);

                // Check if the target directory exists, if not, create it.
                if (Directory.Exists(target.FullName) == false)
                {
                    Directory.CreateDirectory(target.FullName);
                }

                // Copy each file into it�s new directory.
                foreach (FileInfo fi in source.GetFiles())
                {
                    fi.CopyTo(Path.Combine(target.ToString(), fi.Name), true);
                }
            }
            catch (Exception e)
            {
                Report.Error("There was a problem backing up the game files: " + Environment.NewLine + e.Message);
            }
        }


        /// <summary>
        /// Process the elapse of one year (turn) for a fleet.
        /// </summary>
        /// <param name="fleet">The fleet to process a turn for.</param>
        /// <returns>True if the fleet was destroyed.</returns>
        private bool ProcessFleet(Fleet fleet)
        {
            if (fleet == null)
            {
                return true;
            }
            
            bool destroyed = UpdateFleet(fleet);
            
            if (destroyed == true)
            {
                return true;
            }

            // refuel/repair
            RegenerateFleet(fleet);

            // Check for no fuel.

            if (fleet.FuelAvailable == 0 && !fleet.IsStarbase)
            {
                Message message = new Message();
                message.Audience = fleet.Owner;
                message.Text = fleet.Name + " has ran out of fuel.";
                serverState.AllMessages.Add(message);
            }

            return false;
        }

        /// <summary>
        /// Refuel and Repair.
        /// </summary>
        /// <param name="fleet"></param>
        /// <remarks>
        /// To refuel a ship must be in orbit of a planet with a starbase with a dock capacity > 0.
        /// Repair is:
        /// 0% while bombing (or orbiting an enemy planet with attack orders).
        /// 1% moving through space
        /// 2% stopped in space
        /// 3% orbiting, but not bombing an enemy planet
        /// 5% orbiting own planet without a starbase.
        /// 8% orbiting own planet with starbase but 0 dock.
        /// 20 orbiting own planet with dock.
        /// +repair% if stopped or orbiting.
        /// TODO (priority 3) - A starbase is not counted towards repairs if it is under attack. 
        /// TODO (priority 3) - reference where these rules are from.
        /// </remarks>
        private void RegenerateFleet(Fleet fleet)
        {
            if (fleet == null)
            {
                return;
            }
            
            Star star = null;
            
            if (fleet.InOrbit != null)
            {
                star = serverState.AllStars[fleet.InOrbit.Name];
            }

            // refuel
            if (star != null && star.Owner == fleet.Owner /* TODO (priority 6) or friendly*/ && star.Starbase != null && star.Starbase.CanRefuel)
            {
                fleet.FuelAvailable = fleet.TotalFuelCapacity;
            }

            // repair, TODO (priority 3) skip if fleet has no damage, if that is more efficient 

            int repairRate = 0;
            if (star != null)
            {
                if (star.Owner == fleet.Owner /* TODO (priority 6) or friend */)
                {
                    if (star.Starbase != null /* TODO (priority 6) and not under attack */)
                    {
                        if (star.Starbase.CanRefuel)
                        {
                            // orbiting own planet with dock.
                            repairRate = 20;
                        }
                        else
                        {
                            // orbiting own planet with starbase but 0 dock.
                            repairRate = 8;
                        }
                    }
                    else
                    {
                        // friendly planet, no base
                        repairRate = 5;
                    }
                }
                else
                {
                    // TODO (priority 6) 0% if bombing
                    // orbiting, but not bombing an enemy planet
                    repairRate = 3;
                }
            }
            else
            {
                // TODO (priority 4) - check if a stopped fleet has 1 or 0 waypoints
                if (fleet.Waypoints.Count == 0)
                {
                    // stopped in space
                    repairRate = 2;
                }
                else
                {
                    // moving through space
                    repairRate = 1;
                }
            }

            // repair ships/tokens
            foreach (ShipToken token in fleet.Composition.Values)
            {
                token.Shields = token.Design.Shield * token.Quantity; // note: token.Sheild is for all ships in the token
                if (repairRate > 0)
                {
                    // note: token.Armor is for all ships in the token
                    int repairAmount = Math.Max(token.Design.Armor * token.Quantity * repairRate / 100, 1);
                    token.Armor += repairAmount;
                    token.Armor = Math.Min(token.Armor, token.Design.Armor * token.Quantity);
                }
            }
        }

        /// <summary>
        /// Update the status of a fleet moving through waypoints and performing any
        /// specified waypoint tasks.
        /// </summary>
        /// <param name="fleet"></param>
        /// <returns>Always returns false.</returns>
        private bool UpdateFleet(Fleet fleet)
        {
            Race race = serverState.AllEmpires[fleet.Owner].Race;

            Waypoint currentPosition = new Waypoint();
            double availableTime = 1.0;

            while (fleet.Waypoints.Count > 0)
            {
                Waypoint waypointZero = fleet.Waypoints[0];
                NovaPoint positionBeforeMove = fleet.Position;

                Fleet.TravelStatus fleetMoveResult;

                // -------------------
                // Move
                // -------------------

                // Stargates let an eligible fleet skip warp travel (and this waypoint's minefield
                // check - see below) entirely, arriving the same turn regardless of ordered warp
                // speed or remaining fuel. See docs/behavior-specs-4/fleet-movement-scanning-cargo.md
                // §5 "Stargates" and TryStargateJump's own comment.
                if (TryStargateJump(fleet, waypointZero, race, out bool gateDestroyed))
                {
                    if (gateDestroyed)
                    {
                        return true;
                    }

                    fleetMoveResult = Fleet.TravelStatus.Arrived;
                }
                else
                {
                    // Warp 10 can be ordered on any ship, but unless its engine is specifically
                    // rated safe at that speed (Engine.FastestSafeSpeed == 10), each individual ship
                    // faces a 10% chance per year of being destroyed, rolled independently per ship
                    // (not per fleet). See docs/behavior-specs/fleet-movement-scanning-cargo.md §1.
                    if (waypointZero.WarpFactor == 10 && CheckWarp10Destruction(fleet))
                    {
                        return true;
                    }

                    // Check for Cheap Engines failing to start
                    if (waypointZero.WarpFactor > 6 && race.Traits.Contains("CE") && rand.Next(10) == 1)
                    {
                        // Engines fail
                        Message message = new Message();
                        message.Audience = fleet.Owner;
                        message.Text = "Fleet " + fleet.Name + "'s engines failed to start. Fleet has not moved this turn.";
                        message.Type = "Cheap Engines";
                        message.Event = this;
                        serverState.AllMessages.Add(message);
                        fleetMoveResult = Fleet.TravelStatus.InTransit;
                    }
                    else
                    {
                         fleetMoveResult = fleet.Move(ref availableTime, race);
                    }

                    bool destroyed = checkForMinefields.Check(fleet);

                    if (destroyed == true)
                    {
                        return true;
                    }
                }

                if (fleetMoveResult == Fleet.TravelStatus.InTransit)
                {
                    currentPosition.Position = fleet.Position;
                    currentPosition.Task =  new NoTask();
                    currentPosition.Destination = "Space at " + fleet.Position;
                    currentPosition.WarpFactor = waypointZero.WarpFactor;
                    break;
                }
                else
                {
                    // Arrived

                    // Wormholes are a free, uncapped-mass shortcut a fleet falls into simply by
                    // arriving at either opening - "a fleet given a wormhole as a waypoint enters
                    // and exits the same year it reaches the opening" (docs/behavior-specs-4/
                    // fleet-movement-scanning-cargo.md's "Wormholes" section). Unlike Stargates,
                    // there's no player-facing "target this wormhole" order in this port (no UI
                    // for it exists), so this matches purely on the fleet's arrival POSITION -
                    // which is how a plain waypoint set to an empty-space point (as opposed to a
                    // named star) already works everywhere else in this codebase.
                    TryWormholeTransit(fleet);

                    EmpireData sender = serverState.AllEmpires[fleet.Owner];
                    EmpireData reciever = null;
                    Star target = null;

                    serverState.AllStars.TryGetValue(waypointZero.Destination, out target);

                    if (target != null)
                    {
                        fleet.InOrbit = target;
                        serverState.AllEmpires.TryGetValue(target.Owner, out reciever);
                    }
                    
                    // -------------------------
                    // Waypoint 1 Tasks
                    // -------------------------

                    if (waypointZero.Task.IsValid(fleet, target, sender, reciever))
                    {
                        waypointZero.Task.Perform(fleet, target, sender, reciever); // ToDo: scrapping fleet may be performed as waypoint 1 task here which is not correct.

                        // LayMinesTask.Perform() can't reach ServerData.AllMinefields itself (see
                        // LayMines.cs's own comment) - this is the other half of that task,
                        // dispatched here alongside every other waypoint task's real effect.
                        if (waypointZero.Task is LayMinesTask)
                        {
                            layMines.Lay(fleet);
                        }
                    }

                    serverState.AllMessages.AddRange(waypointZero.Task.Messages);
                    
                    // Task is done, clear it.
                    waypointZero.Task = new NoTask();

                    /*if (thisWaypoint.Task != WaypointTask.LayMines)
                    {
                        thisWaypoint.Task =  WaypointTask.None;
                    }*/
                }

                currentPosition = fleet.Waypoints[0];
                fleet.Waypoints.RemoveAt(0);

                // Arriving at a waypoint uses up the rest of that turn's movement, even if
                // there's leftover time/fuel budget that could reach a subsequent waypoint too -
                // a fleet only resumes toward its next waypoint on the FOLLOWING turn. See
                // docs/behavior-specs/fleet-movement-scanning-cargo.md §5 ("... executes that
                // waypoint's task once it actually arrives, then proceeds to the following
                // waypoint the next turn"). Without this, a fast/short-hopping fleet could fly
                // through several waypoints - and the stars at them - in a single turn, e.g. a
                // scout arriving at a planet and immediately continuing past it before anything
                // (a scan report, an "explored" flag) ever registered the visit.
                //
                // The exception is the zero-distance "resume from here" placeholder waypoint
                // this same loop re-inserts above whenever a fleet is left InTransit (its
                // Position is set to wherever the fleet actually stopped, so re-approaching it
                // next turn covers no real distance). Consuming that placeholder must stay free/
                // instant, or a fleet already InTransit would need two turns to make any further
                // progress at all - one to consume the placeholder, another to actually move.
                if (positionBeforeMove != fleet.Position)
                {
                    break;
                }
            }

            fleet.Waypoints.Insert(0, currentPosition);
            serverState.SetFleetOrbit(fleet);

            if (fleet.Waypoints.Count > 1)
            {
                Waypoint nextWaypoint = fleet.Waypoints[1];

                double dx = fleet.Position.X - nextWaypoint.Position.X;
                double dy = fleet.Position.Y - nextWaypoint.Position.Y;
                fleet.Bearing = ((Math.Atan2(dy, dx) * 180) / Math.PI) + 90;
            }

            // ??? (priority 4) - why does this always return false.
            return false;
        }

        /// <summary>
        /// Rolls the Warp 10 destruction risk for each ship in the fleet that isn't warp-10-safe,
        /// removing any ships destroyed. See docs/behavior-specs/fleet-movement-scanning-cargo.md §1.
        /// </summary>
        /// <returns>True if the whole fleet was destroyed.</returns>
        private bool CheckWarp10Destruction(Fleet fleet)
        {
            List<long> destroyedTokenKeys = new List<long>();

            foreach (KeyValuePair<long, ShipToken> entry in fleet.Composition)
            {
                ShipToken token = entry.Value;
                Engine engine = token.Design.Engine;

                if (engine == null || engine.FastestSafeSpeed >= 10)
                {
                    continue;
                }

                int survivors = 0;
                for (int i = 0; i < token.Quantity; i++)
                {
                    if (rand.Next(10) != 0)
                    {
                        survivors++;
                    }
                }

                if (survivors < token.Quantity)
                {
                    Message message = new Message();
                    message.Audience = fleet.Owner;
                    message.Text = (token.Quantity - survivors) + " of your " + token.Design.Name
                        + " in fleet " + fleet.Name + " were destroyed attempting Warp 10 travel.";
                    message.Type = "Warp 10";
                    message.Event = this;
                    serverState.AllMessages.Add(message);
                }

                if (survivors <= 0)
                {
                    destroyedTokenKeys.Add(entry.Key);
                }
                else
                {
                    token.Quantity = survivors;
                }
            }

            foreach (long key in destroyedTokenKeys)
            {
                fleet.Composition.Remove(key);
            }

            return fleet.Composition.Count == 0;
        }

        /// <summary>
        /// Attempts a Stargate jump for this waypoint - skipping ordinary warp travel entirely
        /// when the fleet is docked at a gate-equipped star, its next destination also has an
        /// operational gate, and its cargo is eligible (fuel only, unless the race is
        /// Interstellar Traveler). See docs/behavior-specs-4/fleet-movement-scanning-cargo.md §5
        /// "Stargates".
        ///
        /// Distance is checked only against the SENDING gate's rated range; mass is checked
        /// against BOTH gates (the spec: "mass checks additionally require the receiving gate's
        /// rating too"). A gate can be pushed up to 5x over its rated range or mass and still
        /// sometimes succeed - "overgating" - which always damages the ship, and beyond 100%
        /// cumulative damage the ship does not survive. Beyond the 5x cap the gate simply refuses
        /// the jump outright (this method returns false, and the fleet falls through to ordinary
        /// warp movement for this waypoint instead, exactly as if no gate existed).
        /// </summary>
        /// <param name="destroyed">True if every ship in the fleet was lost attempting the jump.</param>
        /// <returns>True if a Stargate jump was attempted this waypoint (whether it succeeded,
        /// damaged the fleet, or destroyed it outright) - false if the fleet isn't eligible to
        /// gate here at all.</returns>
        /// <summary>
        /// If the fleet has just arrived at (or near) a Wormhole opening, immediately relocates
        /// it to the paired opening - see the call site's own comment for why this is
        /// position-matched rather than an explicit order like a Stargate jump.
        /// </summary>
        /// <returns>True if a transit occurred.</returns>
        private bool TryWormholeTransit(Fleet fleet)
        {
            foreach (Wormhole wormhole in serverState.AllWormholes.Values)
            {
                if (!PointUtilities.IsNear(fleet.Position, wormhole.Position))
                {
                    continue;
                }

                if (!serverState.AllWormholes.TryGetValue(wormhole.PairedKey, out Wormhole pairedEnd))
                {
                    return false; // an unpaired wormhole - shouldn't happen from generation, but don't act on it
                }

                fleet.Position = pairedEnd.Position;
                fleet.InOrbit = null;

                Message message = new Message();
                message.Audience = fleet.Owner;
                message.Text = "Fleet " + fleet.Name + " has transited a Wormhole.";
                message.Type = "Wormhole";
                message.Event = this;
                serverState.AllMessages.Add(message);
                return true;
            }

            return false;
        }

        private bool TryStargateJump(Fleet fleet, Waypoint waypointZero, Race race, out bool destroyed)
        {
            destroyed = false;

            if (!(fleet.InOrbit is Star origin))
            {
                return false;
            }

            // Waypoint 0 is always the fleet's current position (see Fleet's own constructor
            // comment) - for a fleet with no real travel order queued, that waypoint's
            // Destination is just its own star's name. Without this check, any idle fleet
            // sitting at a gate-equipped star would "gate" to itself every single turn it does
            // nothing at all, taking real mass/range jump damage (and possibly being destroyed)
            // for a jump it never ordered. This went unnoticed until a real, working Stargate
            // first existed anywhere (see StarMapInitialiser.PrepareDesigns' IT/PP equip logic) -
            // previously no idle fleet could ever reach this code path with a live gate to use.
            if (waypointZero.Destination == origin.Name)
            {
                return false;
            }

            Gate sendGate = origin.GetStargate();
            if (sendGate == null || sendGate.SafeRange <= 0 || sendGate.SafeHullMass <= 0)
            {
                return false;
            }

            if (!serverState.AllStars.TryGetValue(waypointZero.Destination, out Star destination))
            {
                return false;
            }

            Gate receiveGate = destination.GetStargate();
            if (receiveGate == null || receiveGate.SafeHullMass <= 0)
            {
                return false;
            }

            // Cargo via Stargates: a gating fleet must be carrying only fuel, except Interstellar
            // Traveler races, who may gate with mineral/colonist cargo aboard (Design.Mass never
            // includes current cargo anyway, so no separate mass-check exclusion is needed here).
            bool carryingMineralOrColonistCargo = fleet.Cargo.Ironium > 0 || fleet.Cargo.Boranium > 0
                || fleet.Cargo.Germanium > 0 || fleet.Cargo.ColonistsInKilotons > 0;
            bool isInterstellarTraveler = race.Traits.Contains("IT");
            if (carryingMineralOrColonistCargo && !isInterstellarTraveler)
            {
                return false;
            }

            double distance = PointUtilities.Distance(origin.Position, destination.Position);
            if (distance > sendGate.SafeRange * 5)
            {
                return false;
            }

            foreach (ShipToken sizeCheckToken in fleet.Composition.Values)
            {
                sizeCheckToken.Design.Update();
                if (sizeCheckToken.Design.Mass > sendGate.SafeHullMass * 5
                    || sizeCheckToken.Design.Mass > receiveGate.SafeHullMass * 5)
                {
                    // One ship is too big for this gate pair at any price - the whole fleet
                    // doesn't gate this turn, rather than leaving some ships behind.
                    return false;
                }
            }

            // Past this point the fleet definitely gates - either safely, damaged, or destroyed.
            List<long> destroyedTokenKeys = new List<long>();

            foreach (KeyValuePair<long, ShipToken> entry in fleet.Composition)
            {
                ShipToken token = entry.Value;
                double shipMass = token.Design.Mass;

                double rangeDamagePercent = Math.Max(0, 100.0 * (distance - sendGate.SafeRange) / (4 * sendGate.SafeRange));
                double massDamagePercent = Math.Max(0, 100.0 * (1 -
                    ((5 * sendGate.SafeHullMass - shipMass) / (4 * sendGate.SafeHullMass)) *
                    ((5 * receiveGate.SafeHullMass - shipMass) / (4 * receiveGate.SafeHullMass))));
                double combinedDamagePercent = massDamagePercent + ((100 - massDamagePercent) * rangeDamagePercent / 100.0);

                if (combinedDamagePercent >= 100)
                {
                    destroyedTokenKeys.Add(entry.Key);

                    Message allLost = new Message();
                    allLost.Audience = fleet.Owner;
                    allLost.Text = "All of your " + token.Design.Name + " in fleet " + fleet.Name
                        + " were lost attempting to overgate.";
                    allLost.Type = "Stargate";
                    allLost.Event = this;
                    serverState.AllMessages.Add(allLost);
                    continue;
                }

                if (combinedDamagePercent <= 0)
                {
                    continue;
                }

                double totalArmor = token.Design.Armor * token.Quantity;
                token.Armor = Math.Max(0, token.Armor - (totalArmor * combinedDamagePercent / 100.0));

                double massVanishChance = massDamagePercent > 0
                    ? ((100 - MASS_VANISH_FITTED_CONSTANT) * Math.Pow((5 * sendGate.SafeHullMass) - shipMass, 2)
                        / Math.Pow(4 * sendGate.SafeHullMass, 2)) + MASS_VANISH_FITTED_CONSTANT
                    : 0;
                double rangeVanishChance = rangeDamagePercent / 3.0;
                if (isInterstellarTraveler)
                {
                    massVanishChance *= INTERSTELLAR_TRAVELER_VANISH_SCALE;
                    rangeVanishChance *= INTERSTELLAR_TRAVELER_VANISH_SCALE;
                }

                // Rolled once per ship in the token, matching CheckWarp10Destruction's own
                // per-ship (not per-fleet) pattern above.
                int survivors = 0;
                for (int i = 0; i < token.Quantity; i++)
                {
                    if (rand.NextDouble() * 100 >= massVanishChance && rand.NextDouble() * 100 >= rangeVanishChance)
                    {
                        survivors++;
                    }
                }

                if (survivors < token.Quantity)
                {
                    Message message = new Message();
                    message.Audience = fleet.Owner;
                    message.Text = (token.Quantity - survivors) + " of your " + token.Design.Name
                        + " in fleet " + fleet.Name + " were lost attempting to overgate.";
                    message.Type = "Stargate";
                    message.Event = this;
                    serverState.AllMessages.Add(message);
                }

                if (survivors <= 0)
                {
                    destroyedTokenKeys.Add(entry.Key);
                }
                else
                {
                    token.Quantity = survivors;
                }
            }

            foreach (long key in destroyedTokenKeys)
            {
                fleet.Composition.Remove(key);
            }

            if (fleet.Composition.Count == 0)
            {
                destroyed = true;
                return true;
            }

            fleet.Position = destination.Position;
            fleet.InOrbit = destination;

            Message arrivalMessage = new Message();
            arrivalMessage.Audience = fleet.Owner;
            arrivalMessage.Text = "Fleet " + fleet.Name + " has arrived at " + destination.Name + " via Stargate.";
            arrivalMessage.Type = "Stargate";
            arrivalMessage.Event = this;
            serverState.AllMessages.Add(arrivalMessage);

            return true;
        }

        /// <summary>
        /// This is a utility function. Sets intel for the first turn.
        /// </summary>
        public void AssembleEmpireData()
        {
            // Generates initial reports.
            ITurnStep firstStep = new FirstStep();
            firstStep.Process(serverState);
            ITurnStep scanStep = new ScanStep();
            scanStep.Process(serverState);
        }
    }
}
