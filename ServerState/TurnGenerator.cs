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

        // A test override for every draw this class makes (StargateJumpTest sets it by
        // reflection). Null in play: each consumer then takes its own stream derived from the
        // game seed (ServerData.CreateRandom) - the empire shuffle, the pursuit re-lock, and one
        // stream per fleet for its movement rolls (movementRandom, set by ProcessFleet) - so a
        // generation is repeatable from the seed and one fleet's rolls never shift another's.
        private Random rand;
        private Random movementRandom;

        private Random MovementRandom
        {
            get { return movementRandom ?? rand ?? (movementRandom = serverState.CreateRandom("FleetMovement")); }
        }

        // Fleets that "saw action" during this turn's movement (a completed Stargate jump sets the
        // flag, behavior-specs-9/fleet-movement-scanning-cargo.md §5 "Side effect"): they get no
        // repair this generation (turn-generation-engine.md §11). A minefield hit sets it too.
        // Consumed by RepairStep (step 25, after the battle); cleared before each movement pass.
        private readonly HashSet<long> fleetsThatSawAction = new HashSet<long>();

        // Fleets whose position changed during this turn's movement: they repair at the "moved"
        // rate (turn-generation-engine.md section 11). Consumed by RepairStep; cleared with the above.
        private readonly HashSet<long> fleetsThatMovedThisYear = new HashSet<long>();

        // Repair pass: step 25 of behavior-specs-10/turn-generation-engine.md section 1, after the
        // post-movement stage (battle 23, bombardment 23b, Mystery Trader 23c) and before the year
        // counter (33) and the design pass (36, key 90). (Key 26, not 25: keys 24/25 are the
        // Transfer Fleet task mode 23h and mine sweeping 24, which both precede repair.)
        private const int REPAIRSTEP = 26;

        // Mine sweeping (step 24: after Transfer Fleet, key 24, just before repair) and fleet
        // terraforming (step 27, after repair and Claim Adjuster drift; key 28):
        // behavior-specs-10/turn-generation-engine.md §1, §11.
        private const int MINESWEEPSTEP = 25;
        private const int FLEETTERRAFORMSTEP = 28;

        // Radiating Hydro-Ram Scoop colonist losses: with the post-movement steps (keys below
        // BOMBINGSTEP run right after minefield decay), ahead of Inner Strength breeding (9).
        private const int RAMSCOOPRADIATIONSTEP = 8;

        // Mass-packet decay in flight: step 18 with minefield decay (keys below BOMBINGSTEP run
        // right after MinefieldDecayStep: 7, after the resting-packet decay at 6 and before step
        // 19's breeding at 9). The
        // year-end half step of packets launched this turn: step 21, after the production hub
        // (20, keys 11-15, including the random events at its end) and before the battle.
        // behavior-specs-10/turn-generation-engine.md §1 steps 18 and 21.
        private const int PACKETDECAYSTEP = 7;
        private const int PACKETHALFSTEP = 16;

        // Used to order turn steps.
        private const int FIRSTSTEP = 00;
        // Runs before STARSTEP so a planet's own mines (only ever processed for owned, colonized
        // stars) see this turn's already-depleted concentration if a remote-mining fleet also
        // worked the same star - docs/behavior-specs-4/population-growth.md describes multiple
        // mining sources at one star as strictly sequential, though doesn't mandate which comes
        // first; this is a disclosed, reasonable ordering choice, not a spec requirement.
        private const int REMOTEMININGSTEP = 11;

        private const int STARSTEP = 12;

        // Random-event wrapper (comet, environment shift, mineral deposit): runs at the end of the
        // production hub (behavior-specs-9/turn-generation-engine.md §1 step 20, §5a), so after the
        // planets' own production and before the battle and the year increment.
        private const int RANDOMEVENTSSTEP = 15;

        // Inner Strength shipboard breeding: step 19 of behavior-specs-9/turn-generation-engine.md
        // section 1, after movement (16) and minefield decay (18), before the production hub (20).
        // Keys below BOMBINGSTEP run (in key order) right after MinefieldDecayStep, so 9 puts it
        // ahead of remote mining and the planets' own production.
        private const int COLONISTBREEDINGSTEP = 9;

        // Overgating (behavior-specs-9/fleet-movement-scanning-cargo.md §5 "Overgating,
        // code-confirmed"): a gate whose range is "any" (stored here as SafeRange <= 0) is treated
        // as an 8,000 ly gate. The old community-fitted mass vanish curve (A = 68) and the
        // placeholder Interstellar Traveler vanish scale are withdrawn by that section.
        private const int OVERGATE_ANY_RANGE_LIGHT_YEARS = 8000;
        private const int BOMBINGSTEP = 19;

        // Wormhole relocation: the special-object pass mode 1 (step 21 of behavior-specs-10/
        // turn-generation-engine.md §1), with the fresh-packet half step (16), after the
        // production hub and BEFORE the battle (23). Keys below BOMBINGSTEP run pre-battle.
        // (17, not 18: TurnOrderTest probes key 18 as "just before bombing".)
        private const int WORMHOLEDRIFTSTEP = 17;

        // Resting-packet (salvage) decay with its one-year grace flag: step 18 (FUN_10b8_433a,
        // with packet and minefield decay), so key 6, just before the in-flight packet decay
        // (7) and before the production hub and the battle (turn-generation-engine.md §1, §3).
        private const int DEEPSPACEMINERALDECAYSTEP = 6;

        // Mystery Trader fleet encounters: the post-movement stage right after the battle pass and
        // orbital bombardment (behavior-specs-10/turn-generation-engine.md §1 step 23c, §5a).
        private const int MYSTERYTRADERSTEP = 23;

        // Transfer Fleet: the waypoint-task pass's final mode, after the battle (step 23h of
        // behavior-specs-10/turn-generation-engine.md section 1), before repair (25).
        private const int TRANSFERFLEETSTEP = 24;

        // The planet-artifact bounty of colonisation/invasion resolution (step 23f) and the
        // post-battle research buy loop on banked pools (23g): after the colonisation pass and
        // bombing (19), before the Mystery Trader (23).
        private const int PLANETARTIFACTSTEP = 20;
        private const int RESEARCHBUYLOOPSTEP = 21;

        // Design legality pass (behavior-specs-10 turn-generation step 36, fleet-movement-
        // scanning-cargo.md §5 Scrap Fleet "The flag"): late in the generation, before the scan/
        // output step, so the flag fixes Scrap/Colonize valuation for the NEXT generation.
        // (90 rather than 60: TurnOrderTest probes key 60 as "after every simulation step".)
        private const int DESIGNLEGALITYSTEP = 90;
        private const int SCANSTEP = 99;

        // Patrol scan and fleet-target revalidation: step 39, right after each player's
        // visibility pass (behavior-specs-10/fleet-movement-scanning-cargo.md §5, Patrol item 0).
        // (101, not 100: TurnOrderTest probes key 100 as "after the scan step".)
        private const int PATROLSTEP = 101;
        
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
        private FleetPursuit fleetPursuit;
        
        /// <summary>
        /// Construct a turn processor. 
        /// </summary>
        public TurnGenerator(ServerData serverState)
        {
            this.serverState = serverState;            
            turnSteps = new SortedList<int, ITurnStep>();
            rand = null;
            
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
            fleetPursuit = new FleetPursuit(this.serverState);
            
            turnSteps.Add(SCANSTEP, new ScanStep());
            turnSteps.Add(BOMBINGSTEP, new BombingStep());
            turnSteps.Add(STARSTEP, new StarUpdateStep());
            turnSteps.Add(RANDOMEVENTSSTEP, new RandomEventsStep());
            turnSteps.Add(REMOTEMININGSTEP, new RemoteMiningStep());
            turnSteps.Add(WORMHOLEDRIFTSTEP, new WormholeDriftStep());
            turnSteps.Add(DEEPSPACEMINERALDECAYSTEP, new DeepSpaceMineralDecayStep());
            turnSteps.Add(MYSTERYTRADERSTEP, new MysteryTraderStep());
            turnSteps.Add(COLONISTBREEDINGSTEP, new ColonistBreedingStep());
            turnSteps.Add(DESIGNLEGALITYSTEP, new DesignLegalityStep());
            turnSteps.Add(REPAIRSTEP, new RepairStep(fleetsThatMovedThisYear, fleetsThatSawAction));
            turnSteps.Add(PATROLSTEP, new PatrolStep());
            turnSteps.Add(TRANSFERFLEETSTEP, new TransferFleetStep());
            turnSteps.Add(MINESWEEPSTEP, new MineSweepStep());
            turnSteps.Add(FLEETTERRAFORMSTEP, new FleetTerraformStep());
            turnSteps.Add(RAMSCOOPRADIATIONSTEP, new RamScoopRadiationStep());
            turnSteps.Add(PACKETDECAYSTEP, new PacketDecayStep());
            turnSteps.Add(PACKETHALFSTEP, new PacketMovementStep(true));
            turnSteps.Add(PLANETARTIFACTSTEP, new PlanetArtifactStep());
            turnSteps.Add(RESEARCHBUYLOOPSTEP, new ResearchBuyLoopStep());
        }
        
        /// <summary>
        /// Generate a new turn by reading in the player turn files to update the master
        /// copy of stars, ships, etc. Then do the processing required to take in the
        /// passage of one year of time and, finally, write out the new turn file.
        /// </summary>
        public void Generate()
        {
            // Determinism: every draw of this generation comes from a stream derived from the
            // game seed, this (pre-increment) year and the consumer's key (ServerData.CreateRandom),
            // never from process state - so the same game gives the same result in any process,
            // and a saved and reloaded game continues exactly as one that kept running. Code with
            // no stream of its own (stochastic rounding, tech trading, ...) draws from the ambient
            // stream (GameRandom.Current), narrowed below per step and per fleet.
            // The game's own settings, not whatever the process-wide GameSettings.Data holds.
            using IDisposable gameSettings = serverState.UseSettings();

            serverState.BeginRandomTurn();
            using IDisposable generationRandom = GameRandom.Use(serverState.CreateRandom("Generation"));

            BackupTurn();

            // behavior-specs-7/ai-opponent-behavior.md §8 and turn-generation-engine.md §1 both
            // confirm the original game reshuffles player processing order (Fisher-Yates) once per
            // turn, immediately before per-player turn-generation dispatch, rather than always
            // processing empires in the same fixed order - see ServerData.ShuffledEmpireOrder's own
            // comment for where this codebase's step-pipeline architecture actually consumes it.
            serverState.ShuffledEmpireOrder = serverState.ComputeShuffledEmpireOrder(rand ?? serverState.CreateRandom("EmpireOrder"));

            // For now, just copy the command stacks right away.
            // TODO (priority 6): Integrity check the new turn before
            // updating the state (cheats, errors).
            ReadOrders();

            // for all commands of all empires: command.ApplyToState(empire);
            // for WaypointCommand: Add Waypoints to Fleets.
            ParseCommands();

            // Step 8: design normalisation (behavior-specs-10/turn-generation-engine.md §1).
            new DesignNormalisationStep().Process(serverState);

            // Step 10: re-lock fleet-targeted waypoints whose target vanished or moved
            // (behavior-specs-10/fleet-movement-scanning-cargo.md §5, Pursuit).
            fleetPursuit.ReLockTargets(rand ?? serverState.CreateRandom("PursuitReLock"));

            // Steps 9 and 11: follow orders - mark and propagate (FollowOrderStep).
            new FollowOrderStep(fleetPursuit.FollowMarked).Process(serverState);

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
            using (GameRandom.Use(serverState.CreateRandom("ScrapFleet")))
            {
                new ScrapFleetStep().Process(serverState);
            }

            // Step 12e: the research buy loop on banked pools only (no income); step 13: the
            // race-definition integrity check, after the pre-movement stage, before movement.
            new ResearchBuyLoopStep().Process(serverState);
            new RaceIntegrityStep().Process(serverState);

            // remove battle from old turns
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                empire.BattleReports.Clear();
            }

            // Fleet movement, waypoint tasks, then production - and only THEN combat.
            // behavior-specs-8/turn-generation-engine.md section 1 rebuilds the master routine's
            // order from its real call sequence and corrects the old 23-phase list that put combat
            // at phase 9, before economy and movement: the battle pass is step 23, AFTER movement
            // (16) and the production hub (20), so a fleet arriving at a planet fights the same
            // turn, ships completed this turn can be fought at their build location, and a fleet
            // destroyed in battle is gone before colonisation/invasion resolution (23f) sees it.
            // An earlier revision of this method moved combat BEFORE movement on the strength of
            // that superseded list.
            serverState.CleanupFleets();

            // Mystery Trader movement: the special-object pass, mode 0, runs before fleets move
            // (behavior-specs-10/turn-generation-engine.md §1 step 15, §5a).
            new MysteryTraderMovementStep().Process(serverState);

            // Mass packets take their full step (and may arrive and impact) in the same pass,
            // before fleets move (step 15).
            new PacketMovementStep(false).Process(serverState);

            HashSet<long> minefieldsBeforeMovement = new HashSet<long>(serverState.AllMinefields.Keys);

            fleetsThatSawAction.Clear();
            fleetsThatMovedThisYear.Clear();

            foreach (Fleet fleet in serverState.IterateAllFleetsInShuffledOrder())
            {
                ProcessFleet(fleet); // ToDo: don't scrap fleets here at waypoint 1
            }

            // The end-of-movement refresh: fleet-targeted waypoints take their targets' new
            // positions, and only then is arrival at such a waypoint tested (Pursuit).
            fleetPursuit.RefreshAndResolveArrivals();
            serverState.CleanupFleets();

            // Minefield decay follows fleet movement and precedes production (step 18 of the
            // reconciled order). Only fields that already existed decay: a field laid during this
            // turn's movement/tasks is laid after the decay pass in the original (Lay Mine Field is
            // a post-battle task).
            // A fleet caught by a detonating field counts as having seen action (no repair).
            new MinefieldDecayStep(fleetsThatSawAction).Process(serverState, minefieldsBeforeMovement, rand ?? serverState.CreateRandom("MinefieldDecay"));

            // Production (remote mining, then the planets' own mining/growth/queues).
            RunTurnSteps(step => step < BOMBINGSTEP);

            // Combat resolves here, reading the post-movement, post-production galaxy. Combat
            // groups fleets purely by fleet.Position.
            using (GameRandom.Use(serverState.CreateRandom("BattleAmbient")))
            {
                battleEngine.Run();
            }

            serverState.CleanupFleets();

            // Every fleet has now had its chance to arrive and register a colonization attempt
            // (ColoniseTask.Perform), and any that survived the battle can now resolve - the real
            // routine resolves colonisation and invasion once more after the battle pass (step
            // 23f). Resolve simultaneous-arrival contests here; a winning attempt clears its
            // fleet's composition, so a second cleanup removes that now-empty fleet the same way
            // the pre-fix immediate-Perform() path did.
            ColonizationResolver.ResolvePendingColonizations(serverState);
            serverState.CleanupFleets();

            RunTurnSteps(step => step >= BOMBINGSTEP && step < SCANSTEP);

            // Victory evaluation runs after the year counter increments (step 33 then 35) and after
            // all simulation - VictoryCheck.Victor() computes gameTime from serverState.TurnYear,
            // so it must see the incremented value, and it must see the year's final state.
            serverState.TurnYear++;

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                empire.TurnYear = serverState.TurnYear;
                empire.TurnSubmitted = false;
            }

            victoryCheck.Victor();

            // The per-turn score record, kept for the most recent 100 turns
            // (save-turn-file-format.md section 3).
            serverState.ScoreHistory.Record(serverState.TurnYear, scores.GetScores());

            // Scanning/intel generation is the output step (the real routine scans while it writes
            // each player's turn file, step 39), so it runs last.
            RunTurnSteps(step => step >= SCANSTEP);
            
            WriteIntel();

            // remove old messages, do this last so that the 1st turn intro message is not removed before it is delivered.
            serverState.AllMessages = new List<Message>();

            CleanupOrders();

            // Leave the state exactly as a save and reload would (collection order, transient
            // counters), so a server kept in memory continues identically to a reloaded one.
            serverState.CompactCollections();
        }
        

        /// <summary>
        /// Runs the registered turn steps whose order key satisfies the filter, in key order.
        /// </summary>
        private void RunTurnSteps(Func<int, bool> filter)
        {
            foreach (KeyValuePair<int, ITurnStep> turnStep in turnSteps)
            {
                if (filter(turnStep.Key))
                {
                    // Each step's ambient stream is its own (keyed by its order key), so code
                    // that draws from GameRandom.Current in one step never shifts another.
                    using (GameRandom.Use(serverState.CreateRandom("StepAmbient", turnStep.Key)))
                    {
                        turnStep.Value.Process(serverState);
                    }
                }
            }
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

                            // A minefield lives in ServerData, not EmpireData (DetonateCommand).
                            (command as DetonateCommand)?.ApplyToMinefields(serverState.AllMinefields, empire);
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
                if (fi.Name.ToLowerInvariant().EndsWith(Global.OrdersExtension))
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

            // This fleet's own streams, keyed by its fleet key: its movement rolls (Cheap Engines,
            // Warp 10, overgating) and the ambient stream its waypoint tasks draw from (merging,
            // tech trading...). Another fleet's draws never shift these.
            movementRandom = rand ?? serverState.CreateRandom("FleetMovement", fleet.Key);
            using IDisposable fleetTaskRandom = GameRandom.Use(rand != null ? null : serverState.CreateRandom("FleetTasks", fleet.Key));

            int xBeforeMove = fleet.Position.X;
            int yBeforeMove = fleet.Position.Y;

            bool destroyed = UpdateFleet(fleet);

            if (destroyed == true)
            {
                fleetsThatSawAction.Remove(fleet.Key);
                return true;
            }

            bool movedThisYear = fleet.Position.X != xBeforeMove || fleet.Position.Y != yBeforeMove;
            if (movedThisYear)
            {
                fleetsThatMovedThisYear.Add(fleet.Key);
            }

            // Lay Mine Field runs after movement and depends on whether the fleet moved
            // (behavior-specs-10/turn-generation-engine.md section 3, "Mine laying, exact rule").
            layMines.Process(fleet, movedThisYear);

            // refuel (and shield recharge); repair itself is RepairStep, after the battle
            RegenerateFleet(fleet, movedThisYear);

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
        /// Refuel, and recharge shields (shields are not persisted, so every token starts the
        /// year's battle with full shields).
        /// </summary>
        /// <param name="fleet">The fleet.</param>
        /// <param name="movedThisYear">Unused here since repair moved to RepairStep (kept for the
        /// existing reflection-based tests).</param>
        /// <remarks>
        /// To refuel a ship must be in orbit of a planet with a starbase with a dock capacity > 0.
        /// Repair is no longer done here: it is the separate RepairStep (step 25 of
        /// behavior-specs-10/turn-generation-engine.md section 1), after the battle, so a fleet is
        /// not healed before it fights and a fleet that fought is skipped.
        /// </remarks>
        private void RegenerateFleet(Fleet fleet, bool movedThisYear)
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

            // refuel: a fleet at a friendly starbase that can dock ships is refuelled to full; every
            // other fleet makes its own fuel - 50 mg per Anti-matter Generator carried and 200 mg per
            // Fuel Transport / Super-Fuel Transport ship - up to its capacity (behavior-specs-8/
            // turn-generation-engine.md section 1 step 22; ramscoop engines play no part in it).
            if (star != null && star.Owner == fleet.Owner /* TODO (priority 6) or friendly*/ && star.Starbase != null && star.Starbase.CanRefuel)
            {
                fleet.FuelAvailable = fleet.TotalFuelCapacity;
            }
            else if (!fleet.IsStarbase)
            {
                fleet.FuelAvailable = Math.Min(fleet.TotalFuelCapacity, fleet.FuelAvailable + fleet.PassiveFuelGeneration);
            }

            foreach (ShipToken token in fleet.Composition.Values)
            {
                token.Shields = token.Design.Shield * token.Quantity; // note: token.Sheild is for all ships in the token
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

            // Alternate Reality warp-acceleration casualties (message 193) are decided once per
            // fleet per turn, as the fleet sets out - see IsSettingOutUnderAlternateReality. They
            // are applied at the first ordinary engine move below, so a Cheap Engines balk (which
            // cancels the move) also cancels them.
            bool alternateRealityLossPending = IsSettingOutUnderAlternateReality(fleet, race);

            bool firstIteration = true;

            while (fleet.Waypoints.Count > 0)
            {
                Waypoint waypointZero = fleet.Waypoints[0];
                NovaPoint positionBeforeMove = fleet.Position;
                int legWarp = waypointZero.WarpFactor;

                // The fleet's own current-position waypoint (the original's waypoint 0) - consumed
                // for free at the start of the turn, never "reached" (so never recycled by
                // Repeat Orders, and never a pursuit leg).
                bool isCurrentPositionWaypoint = firstIteration && waypointZero.Position == fleet.Position;
                firstIteration = false;
                Waypoint recycledWaypoint = null;

                Fleet.TravelStatus fleetMoveResult;

                // -------------------
                // Move
                // -------------------

                // Stargates let an eligible fleet skip warp travel (and this waypoint's minefield
                // check - see below) entirely, arriving the same turn regardless of remaining
                // fuel - but only when this leg was actually ordered at the dedicated "use
                // Stargate" speed (Global.StargateWarpFactor). An ordinary warp speed (1-10)
                // ignores any Stargate that happens to exist and travels normally instead,
                // exactly as if no gate were there. See docs/behavior-specs-4/
                // fleet-movement-scanning-cargo.md §5 "Stargates" and TryStargateJump's own
                // comment.
                if (TryStargateJump(fleet, waypointZero, race, out bool gateDestroyed, out bool gateArrived))
                {
                    if (gateDestroyed)
                    {
                        return true;
                    }

                    // Ordered to gate, but this leg isn't eligible this turn (no Stargate at one
                    // end, ineligible cargo, or beyond the 5x overgating cap) - the original
                    // game's "use Stargate" speed simply fails rather than falling back to
                    // ordinary warp travel: the fleet stays exactly where it is and the order
                    // stays queued to retry next turn.
                    fleetMoveResult = gateArrived ? Fleet.TravelStatus.Arrived : Fleet.TravelStatus.InTransit;
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
                    if (waypointZero.WarpFactor > 6 && race.Traits.Contains("CE") && MovementRandom.Next(10) == 1)
                    {
                        // Engines fail
                        Message message = new Message();
                        message.Audience = fleet.Owner;
                        message.Text = "Fleet " + fleet.Name + "'s engines failed to start. Fleet has not moved this turn.";
                        message.Type = "Cheap Engines";
                        serverState.AllMessages.Add(message);
                        fleetMoveResult = Fleet.TravelStatus.InTransit;
                    }
                    else
                    {
                        if (alternateRealityLossPending)
                        {
                            ApplyAlternateRealityWarpLoss(fleet);
                        }

                        fleetMoveResult = fleet.Move(ref availableTime, race);
                    }

                    // Checked once per turn only (the original's pass-0 test).
                    alternateRealityLossPending = false;

                    // Minefields along this year's actual path (behavior-specs-10/
                    // fleet-movement-scanning-cargo.md section 5): a hit stops the fleet at the
                    // hit point and counts as having seen action (no repair this year).
                    MinefieldHit minefieldHit = checkForMinefields.Check(fleet, positionBeforeMove);

                    if (minefieldHit == MinefieldHit.Destroyed)
                    {
                        return true;
                    }

                    if (minefieldHit == MinefieldHit.Hit)
                    {
                        fleetsThatSawAction.Add(fleet.Key);
                        if (fleetMoveResult == Fleet.TravelStatus.Arrived && fleet.Position != waypointZero.Position)
                        {
                            // Stopped short of the waypoint: resume the leg next year.
                            fleetMoveResult = Fleet.TravelStatus.InTransit;
                            waypointZero.WarpFactor = legWarp;
                        }
                    }
                }

                // Pursuit: a leg aimed at a fleet is never arrived at during movement. The fleet
                // stops on the stored point; the end-of-movement refresh (FleetPursuit) then moves
                // the point to where the target ended up and only then tests arrival
                // (behavior-specs-10/fleet-movement-scanning-cargo.md §5, Pursuit).
                if (fleetMoveResult == Fleet.TravelStatus.Arrived && !isCurrentPositionWaypoint && waypointZero.IsFleetTarget)
                {
                    fleetMoveResult = Fleet.TravelStatus.InTransit;
                    waypointZero.WarpFactor = legWarp;
                }

                if (fleetMoveResult == Fleet.TravelStatus.InTransit)
                {
                    currentPosition.Position = fleet.Position;
                    // Patrol stays on the current-position waypoint while the fleet travels on
                    // (its scan runs from waypoint 0 every turn - PatrolStep).
                    currentPosition.Task = currentPosition.Task is PatrolTask ? currentPosition.Task : new NoTask();
                    // A fleet that never actually left orbit this iteration (Cheap Engines
                    // failing to start, or a Stargate order that turned out to be ineligible)
                    // still has fleet.InOrbit set to where it's really sitting - label the
                    // resume placeholder with that star's name instead of a "Space at" position,
                    // both for an accurate display and so TryStargateJump's own "is this really
                    // a new order, or just the current-position placeholder" self-check
                    // recognizes it correctly next turn. A genuine mid-flight stop already has
                    // Fleet.Move's own InOrbit = null from the moment it left, so this still
                    // falls back to the position-based label exactly as before for that case.
                    currentPosition.Destination = fleet.InOrbit != null ? fleet.InOrbit.Name : "Space at " + fleet.Position;
                    // The label decides the target kind too (Waypoint.MakeFixedPoint's rule): a
                    // placeholder carried over from a star kept TargetKind Planet while naming
                    // "Space at (x, y)" (found by Nova.Sim, SIM-6). The current-position waypoint
                    // is never a pursuit leg, so it aims at no fleet.
                    currentPosition.TargetKind = fleet.InOrbit is Star ? WaypointTargetKind.Planet : WaypointTargetKind.DeepSpace;
                    currentPosition.TargetFleetKey = Global.None;
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

                    // Repeat Orders: a reached waypoint is re-appended, with its task and its leg
                    // warp, to the end of the route (fleet byte 5 bit 0x02; behavior-specs-10 §5).
                    // Not the current-position waypoint, not a fleet-targeted (intercept) leg, and
                    // not when it was the last waypoint left (nothing to cycle through).
                    if (fleet.RepeatOrders && !isCurrentPositionWaypoint && !waypointZero.IsFleetTarget && fleet.Waypoints.Count > 1)
                    {
                        recycledWaypoint = waypointZero.CloneWithTask();
                        recycledWaypoint.WarpFactor = legWarp;
                    }

                    bool taskValid = waypointZero.Task.IsValid(fleet, target, sender, reciever);
                    if (taskValid)
                    {
                        waypointZero.Task.Perform(fleet, target, sender, reciever); // ToDo: scrapping fleet may be performed as waypoint 1 task here which is not correct.
                    }

                    serverState.AllMessages.AddRange(waypointZero.Task.Messages);

                    // Task is done, clear it - except Lay Mine Field, which stays on the
                    // current waypoint (holding the fleet there) until its duration runs out;
                    // the yearly laying itself is LayMines.Process, called after movement.
                    // Patrol likewise stays: it is not an arrival action (PatrolStep).
                    // An unfinished Transport task (CargoTask.Pending) and a Transfer Fleet task
                    // (settled after the battle by TransferFleetStep) stay too, holding the fleet.
                    if (!(taskValid && waypointZero.Task is LayMinesTask) && !(waypointZero.Task is PatrolTask)
                        && !(taskValid && waypointZero.Task is CargoTask pendingCargo && pendingCargo.Pending)
                        && !(waypointZero.Task is TransferFleetTask))
                    {
                        waypointZero.Task = new NoTask();
                    }

                    /*if (thisWaypoint.Task != WaypointTask.LayMines)
                    {
                        thisWaypoint.Task =  WaypointTask.None;
                    }*/
                }

                currentPosition = fleet.Waypoints[0];
                fleet.Waypoints.RemoveAt(0);

                if (recycledWaypoint != null)
                {
                    fleet.Waypoints.Add(recycledWaypoint);

                    // Reaching a recycled waypoint always ends the turn's movement, even a
                    // zero-distance one, so a cycling route can never loop within one turn.
                    break;
                }

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
                // A fleet whose current waypoint carries Lay Mine Field does not move on
                // (turn-generation-engine.md section 3, "Mine laying, exact rule").
                if (positionBeforeMove != fleet.Position || currentPosition.Task is LayMinesTask
                    || (currentPosition.Task is CargoTask heldCargo && heldCargo.Pending)
                    || currentPosition.Task is TransferFleetTask)
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
                    if (MovementRandom.Next(10) != 0)
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
        /// The gate for Alternate Reality warp-acceleration casualties (behavior-specs-9/
        /// fleet-movement-scanning-cargo.md §1, message 193): an AR fleet that is actually setting
        /// out this turn - at least two waypoints, the first waypoint's task neither Transport nor
        /// Lay Mines, and a leg warp that is non-zero and not the "use Stargate" sentinel (Stargate
        /// legs are exempt). Fuel is not consulted. The Cheap Engines balk is handled by the caller.
        /// </summary>
        private static bool IsSettingOutUnderAlternateReality(Fleet fleet, Race race)
        {
            if (race == null || !race.HasTrait("AR") || fleet.Waypoints.Count < 2)
            {
                return false;
            }

            IWaypointTask firstTask = fleet.Waypoints[0].Task;
            if (firstTask is CargoTask || firstTask is LayMinesTask)
            {
                return false;
            }

            int legWarp = fleet.Waypoints[1].WarpFactor;
            return legWarp > 0 && legWarp != Global.StargateWarpFactor;
        }

        /// <summary>
        /// A setting-out Alternate Reality fleet carrying C &gt; 10 units of colonists (1 unit = 1 kT
        /// = 100 colonists) loses floor((C + 11) x 3 / 100) units, at any warp. A loss of 0 (loads
        /// of 11-22 units) posts no message. behavior-specs-9/fleet-movement-scanning-cargo.md §1.
        /// </summary>
        private void ApplyAlternateRealityWarpLoss(Fleet fleet)
        {
            int colonistUnits = fleet.Cargo.ColonistsInKilotons;
            if (colonistUnits <= 10)
            {
                return;
            }

            int lostUnits = (colonistUnits + 11) * 3 / 100;
            if (lostUnits <= 0)
            {
                return;
            }

            fleet.Cargo.ColonistsInKilotons = colonistUnits - lostUnits;

            Message message = new Message();
            message.Audience = fleet.Owner;
            message.Text = "Due to the rigors of warp acceleration, " + (lostUnits * Global.ColonistsPerKiloton)
                + " of your colonists on " + fleet.Name + " have died.";
            message.Type = "Warp Acceleration";
            serverState.AllMessages.Add(message);
        }

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

                // Jump freeze: other players' pursuers stop at the mouth it entered.
                fleetPursuit.FreezePursuers(fleet, fleet.Position);

                // The race has now used this wormhole (both ends): the per-race bit the AI's
                // diversion scoring reads as "known" (ai-opponent-behavior.md §12).
                wormhole.UsedBy.Add(fleet.Owner);
                pairedEnd.UsedBy.Add(fleet.Owner);

                // A copy: WormholeDriftStep moves wormhole.Position in place, which would
                // otherwise drag the fleet along with the mouth it came out of.
                fleet.Position = new NovaPoint(pairedEnd.Position);
                fleet.InOrbit = null;

                Message message = new Message();
                message.Audience = fleet.Owner;
                message.Text = "Fleet " + fleet.Name + " has transited a Wormhole.";
                message.Type = "Wormhole";
                serverState.AllMessages.Add(message);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts a Stargate jump for this waypoint - but only when it was explicitly ORDERED
        /// at the dedicated "use Stargate" speed (WarpFactor == Global.StargateWarpFactor, one
        /// beyond the highest real warp speed). An ordinary warp speed (1-10) never gates, even
        /// when both ends have an operational Stargate - exactly as if the gate didn't exist,
        /// matching the original game's ability to select a non-stargated speed instead.
        ///
        /// When the Stargate speed IS ordered, this either succeeds (arriving this same turn,
        /// possibly damaged by overgating - up to 5x a gate's rated range/mass, checking distance
        /// only against the SENDING gate but mass against BOTH gates) or fails outright if the
        /// route isn't gateable at all (no Stargate at one end, ineligible cargo, or beyond the
        /// 5x cap) - in which case the fleet simply stays where it is rather than falling back to
        /// ordinary warp travel, matching the original game's own "it simply failed" behavior for
        /// this speed selection. See docs/behavior-specs-4/fleet-movement-scanning-cargo.md §5
        /// "Stargates".
        /// </summary>
        /// <param name="destroyed">True if every ship in the fleet was lost attempting the jump.</param>
        /// <param name="arrived">True if the fleet actually reached its destination this turn
        /// (safely or damaged); false if the Stargate speed was ordered but the route wasn't
        /// eligible, so the fleet stayed put instead.</param>
        /// <returns>True if this leg was ordered at the Stargate speed at all (whether it
        /// arrived, was damaged, was destroyed, or simply failed) - false only for an ordinary
        /// warp speed, so the fleet falls through to normal warp movement for this waypoint.</returns>
        private bool TryStargateJump(Fleet fleet, Waypoint waypointZero, Race race, out bool destroyed, out bool arrived)
        {
            destroyed = false;
            arrived = false;

            // A fleet in deep space has no departure gate, but may still jump on its own Jump
            // Gates (behavior-specs-10 §5 "Fleet-carried gates"); origin is then null.
            Star origin = fleet.InOrbit as Star;
            if (origin == null)
            {
                // Deep-space counterpart of the idle-placeholder guard below: the resume
                // placeholder sits exactly at the fleet's own position.
                if (waypointZero.WarpFactor != Global.StargateWarpFactor
                    || waypointZero.Position == fleet.Position)
                {
                    return false;
                }

                return TryStargateJumpFrom(null, fleet, waypointZero, race, out destroyed, out arrived);
            }

            // Waypoint 0 is always the fleet's current position (see Fleet's own constructor
            // comment) - for a fleet with no real travel order queued, that waypoint's
            // Destination is just its own star's name, and its WarpFactor is only ever a copy of
            // whatever the fleet's last REAL order happened to be (see UpdateFleet's own
            // placeholder-building code), not a new order at all. Without this check, an idle
            // fleet sitting at a gate-equipped star whose last real order was a Stargate jump
            // would spuriously re-attempt (and "gate" to itself, or "fail") one every single turn
            // it does nothing at all. This went unnoticed until a real, working Stargate first
            // existed anywhere (see StarMapInitialiser.PrepareDesigns' IT/PP equip logic) -
            // previously no idle fleet could ever reach this code path with a live gate to use.
            if (waypointZero.Destination == origin.Name)
            {
                return false;
            }

            if (waypointZero.WarpFactor != Global.StargateWarpFactor)
            {
                // An ordinary warp speed was ordered for this leg - ignore any Stargate that
                // happens to exist and fall through to normal warp travel, exactly as if no gate
                // were here. This is the "you could select non-stargated by selecting a different
                // speed" half of the original game's "use Stargate" speed option.
                return false;
            }

            return TryStargateJumpFrom(origin, fleet, waypointZero, race, out destroyed, out arrived);
        }

        /// <summary>
        /// The gate checks, pre-jump cargo dump and jump itself for a fleet that has been ordered
        /// to gate (behavior-specs-10/fleet-movement-scanning-cargo.md §5 "Overgating, code-
        /// confirmed": "Fleet-carried gates" and "Pre-jump cargo dump"; §4 "Cargo via
        /// Stargates"). The checks run in the original's order:
        ///  1. Departure. A planet's gate must belong to the fleet's race or to a race whose
        ///     relation toward the fleet's race is Friend (else message 230). With no departure
        ///     gate (no gate at the planet, or deep space) every occupied design must carry a Jump
        ///     Gate (else 222); the destination gate's ratings then stand in for both ends.
        ///  2. Destination: a planet (else 327) with a gate (else 226) belonging to the fleet's
        ///     race or a friend (else 229).
        ///  3. Range / mass refusals (227/228).
        ///  4. Only for a jump from a planet's gate by a race that is not Interstellar Traveler:
        ///     colonists aboard at a planet not the fleet owner's refuse the jump (350, nothing
        ///     unloaded); otherwise all minerals and colonists go onto the departure planet in
        ///     full (no loss, no cap; fuel stays aboard), messages 236/237/238, copied to a friend
        ///     owner. Interstellar Traveler fleets and fleets on their own Jump Gates keep cargo.
        /// The original dumps BEFORE step 3, so a refused overgate has still unloaded; this port
        /// follows the spec's reimplementation recommendation and refuses first.
        /// </summary>
        private bool TryStargateJumpFrom(Star origin, Fleet fleet, Waypoint waypointZero, Race race, out bool destroyed, out bool arrived)
        {
            destroyed = false;
            arrived = false;

            // A Gate's SafeRange/SafeHullMass of 0 or less (stored as -1 by real "Any" mass/range
            // components - e.g. "Gate 100/Any" or "Gate Any/Any" in components.xml) means NO
            // limit on that dimension, not "this isn't really a gate" - matching the exact same
            // convention ShipDesignViewModel's own component-description text already uses
            // ("Safe for any hull mass"/"Unlimited range" for <= 0). Only a null Gate means there
            // truly is no Stargate here at all.
            Gate sendGate = origin?.GetStargate();
            bool usingOwnJumpGates = false;
            if (sendGate != null)
            {
                if (!IsSelfOrFriend(origin.Owner, fleet.Owner))
                {
                    // Message 230.
                    EmitStargateFailureMessage(fleet, "the Stargate at " + origin.Name + " does not belong to us or a friend");
                    return true;
                }
            }
            else if (FleetCarriesJumpGates(fleet))
            {
                usingOwnJumpGates = true;
            }
            else
            {
                // Message 222.
                EmitStargateFailureMessage(fleet, (origin != null ? origin.Name : "deep space")
                    + " has no operational Stargate and the fleet carries no Jump Gates");
                return true;
            }

            if (!serverState.AllStars.TryGetValue(waypointZero.Destination, out Star destination))
            {
                // Message 327.
                EmitStargateFailureMessage(fleet, "its destination is not a planet");
                return true;
            }

            Gate receiveGate = destination.GetStargate();
            if (receiveGate == null)
            {
                // Message 226.
                EmitStargateFailureMessage(fleet, destination.Name + " has no operational Stargate");
                return true;
            }

            if (!IsSelfOrFriend(destination.Owner, fleet.Owner))
            {
                // Message 229.
                EmitStargateFailureMessage(fleet, "the Stargate at " + destination.Name + " does not belong to us or a friend");
                return true;
            }

            if (usingOwnJumpGates)
            {
                // "The destination gate's ratings then stand in for both ends."
                sendGate = receiveGate;
            }

            bool isInterstellarTraveler = race.Traits.Contains("IT");

            // behavior-specs-9/fleet-movement-scanning-cargo.md §5 "Overgating, code-confirmed":
            // the distance is truncated to whole light-years; an "any" range counts as 8,000 ly and
            // an "any" mass limit (0 here) is never exceeded. Range is tested against the sending
            // gate only, mass (each design's EMPTY mass - Design.Mass never includes cargo or fuel,
            // for any race) against both. More than 5x a limit refuses the jump before anything
            // happens (messages 227/228); exactly 5x is allowed but is certain destruction.
            int distance = (int)PointUtilities.Distance(fleet.Position, destination.Position);
            int rangeLimit = sendGate.SafeRange > 0 ? (int)sendGate.SafeRange : OVERGATE_ANY_RANGE_LIGHT_YEARS;
            int sendMassLimit = sendGate.SafeHullMass > 0 ? (int)sendGate.SafeHullMass : 0;
            int receiveMassLimit = receiveGate.SafeHullMass > 0 ? (int)receiveGate.SafeHullMass : 0;

            if (distance > 5 * rangeLimit)
            {
                // Message 227.
                EmitStargateFailureMessage(fleet, "the distance is more than five times the gate's range");
                return true;
            }

            foreach (ShipToken sizeCheckToken in fleet.Composition.Values)
            {
                sizeCheckToken.Design.Update();
                int designMass = sizeCheckToken.Design.Mass;
                if ((sendMassLimit > 0 && designMass > 5 * sendMassLimit)
                    || (receiveMassLimit > 0 && designMass > 5 * receiveMassLimit))
                {
                    // Message 228. One ship is too big for this gate pair at any price - the whole
                    // fleet doesn't gate this turn, rather than leaving some ships behind.
                    EmitStargateFailureMessage(fleet, "at least one ship is more than five times a gate's mass rating");
                    return true;
                }
            }

            // Pre-jump cargo dump: only from a planet's gate, only for a race that is not
            // Interstellar Traveler.
            if (!usingOwnJumpGates && origin != null && !isInterstellarTraveler)
            {
                if (!DumpCargoBeforeJump(fleet, origin))
                {
                    return true;
                }
            }

            // Past this point the fleet definitely gates - either safely, damaged, or destroyed.
            arrived = true;
            List<long> destroyedTokenKeys = new List<long>();

            int shipsBeforeJump = 0;
            foreach (ShipToken countToken in fleet.Composition.Values)
            {
                shipsBeforeJump += countToken.Quantity;
            }

            int shipsLost = 0;

            foreach (KeyValuePair<long, ShipToken> entry in fleet.Composition)
            {
                ShipToken token = entry.Value;
                int damagePercent = OvergateDamagePercent(distance, rangeLimit, token.Design.Mass, sendMassLimit, receiveMassLimit);

                if (damagePercent <= 0)
                {
                    continue;
                }

                // A stack at 100% is lost whole.
                if (damagePercent >= 100)
                {
                    shipsLost += token.Quantity;
                    destroyedTokenKeys.Add(entry.Key);
                    continue;
                }

                // Otherwise each ship vanishes independently with probability floor(damage / 3)
                // percent - one roll out of 100 per ship, for range and mass overgating alike.
                // Interstellar Traveler ships never vanish (the roll is skipped); their damage is
                // unchanged.
                int originalQuantity = token.Quantity;
                int survivors = originalQuantity;
                if (!isInterstellarTraveler)
                {
                    int vanishChancePercent = damagePercent / 3;
                    for (int i = 0; i < originalQuantity; i++)
                    {
                        if (MovementRandom.Next(100) < vanishChancePercent)
                        {
                            survivors--;
                        }
                    }
                }

                if (survivors <= 0)
                {
                    shipsLost += originalQuantity;
                    destroyedTokenKeys.Add(entry.Key);
                    continue;
                }

                // Each survivor takes damage percent of the design's armor (truncated, at least 1
                // point). The stack's old damage - including that of the ships that just vanished,
                // which the original keeps in the shared spread - stays with the survivors, so a
                // ship never survives past 100% cumulative damage. (This port keeps one aggregate
                // armor figure per token, so the old damage is treated as spread over the stack.)
                // Regression note: a live Scout was once left at Armor 0 forever after overgating;
                // exhausted armor is fatal here exactly as in battle and minefield damage.
                int designArmor = token.Design.Armor;
                int newDamagePerShip = Math.Max(1, designArmor * damagePercent / 100);
                double oldDamage = Math.Max(0, ((double)designArmor * originalQuantity) - token.Armor);
                double totalDamage = oldDamage + ((double)newDamagePerShip * survivors);
                double survivorsFullArmor = (double)designArmor * survivors;

                if (totalDamage >= survivorsFullArmor)
                {
                    shipsLost += originalQuantity;
                    destroyedTokenKeys.Add(entry.Key);
                    continue;
                }

                shipsLost += originalQuantity - survivors;
                token.Quantity = survivors;
                token.Armor = survivorsFullArmor - totalDamage;
            }

            foreach (long key in destroyedTokenKeys)
            {
                fleet.Composition.Remove(key);
            }

            // Messages: none unless ships were lost; a wiped-out fleet gets 231 and is removed;
            // otherwise the loss is compared with the pre-jump ship count: below a quarter
            // (rounded down) 232, above half (rounded down) 234, else 233. (The original's
            // stack-count quirk that can remove a fleet with survivors is not replicated.)
            if (fleet.Composition.Count == 0)
            {
                Message wipedOut = new Message();
                wipedOut.Audience = fleet.Owner;
                wipedOut.Text = "All " + shipsLost + " ships of fleet " + fleet.Name
                    + " were torn apart attempting to overgate to " + destination.Name + ". The fleet has been lost.";
                wipedOut.Type = "Stargate";
                serverState.AllMessages.Add(wipedOut);

                destroyed = true;
                return true;
            }

            if (shipsLost > 0)
            {
                string severity;
                if (shipsLost < shipsBeforeJump / 4)
                {
                    severity = "a few of its ships";
                }
                else if (shipsLost > shipsBeforeJump / 2)
                {
                    severity = "most of its ships";
                }
                else
                {
                    severity = "a significant part of its ships";
                }

                Message lossMessage = new Message();
                lossMessage.Audience = fleet.Owner;
                lossMessage.Text = "Fleet " + fleet.Name + " lost " + severity + " (" + shipsLost + " of " + shipsBeforeJump
                    + ") to the stresses of overgating to " + destination.Name + ".";
                lossMessage.Type = "Stargate";
                serverState.AllMessages.Add(lossMessage);
            }

            // A completed jump sets the fleet's "saw action this year" flag: no repair this
            // generation (fleet-movement-scanning-cargo.md §5 "Side effect").
            fleetsThatSawAction.Add(fleet.Key);

            // Jump freeze: other players' pursuers stop at the point it left.
            fleetPursuit.FreezePursuers(fleet, fleet.Position);

            fleet.Position = destination.Position;
            fleet.InOrbit = destination;

            Message arrivalMessage = new Message();
            arrivalMessage.Audience = fleet.Owner;
            arrivalMessage.Text = "Fleet " + fleet.Name + " has arrived at " + destination.Name + " via Stargate.";
            arrivalMessage.Type = "Stargate";
            serverState.AllMessages.Add(arrivalMessage);

            return true;
        }

        /// <summary>
        /// Overgating damage percentage for one design (behavior-specs-9/
        /// fleet-movement-scanning-cargo.md §5 "Overgating, code-confirmed"). Each exceeded limit
        /// L gives a survival factor in ten-thousandths, 2,500 x (5L - x) / L truncated (that is,
        /// (5L - x) / (4L)); the range factor comes first and each exceeded mass factor multiplies
        /// the running factor, dividing by 10,000 and truncating. A factor below 1 means 100%.
        /// Damage = (10,000 - running factor) / 100, truncated; 0 within every limit.
        /// </summary>
        /// <param name="sendMassLimit">0 for an "any" mass gate (never exceeded).</param>
        /// <param name="receiveMassLimit">0 for an "any" mass gate (never exceeded).</param>
        private static int OvergateDamagePercent(int distance, int rangeLimit, int mass, int sendMassLimit, int receiveMassLimit)
        {
            long running = 10000;

            if (distance > rangeLimit)
            {
                long rangeFactor = OvergateSurvivalFactor(rangeLimit, distance);
                if (rangeFactor < 1)
                {
                    return 100;
                }
                running = rangeFactor;
            }

            foreach (int massLimit in new[] { sendMassLimit, receiveMassLimit })
            {
                if (massLimit > 0 && mass > massLimit)
                {
                    long massFactor = OvergateSurvivalFactor(massLimit, mass);
                    if (massFactor < 1)
                    {
                        return 100;
                    }
                    running = running * massFactor / 10000;
                }
            }

            return (int)((10000 - running) / 100);
        }

        /// <summary>2,500 x (5L - x) / L, truncated: the (5L - x) / (4L) survival factor in
        /// ten-thousandths.</summary>
        private static long OvergateSurvivalFactor(long limit, long x)
        {
            return 2500 * ((5 * limit) - x) / limit;
        }

        /// <summary>
        /// True when a gate belonging to <paramref name="gateOwner"/> may be used by a fleet of
        /// <paramref name="fleetOwner"/>: it is the fleet's own race, or the gate owner rates the
        /// fleet's race as Friend (its own EmpireReports entry - the relation "toward the fleet's
        /// race", behavior-specs-10 §5 "Pre-jump cargo dump"). An unowned gate is never usable.
        /// </summary>
        private bool IsSelfOrFriend(ushort gateOwner, ushort fleetOwner)
        {
            if (gateOwner == fleetOwner)
            {
                return true;
            }

            if (gateOwner == Global.Nobody)
            {
                return false;
            }

            return serverState.AllEmpires.TryGetValue(gateOwner, out EmpireData ownerEmpire)
                && ownerEmpire.EmpireReports.TryGetValue(fleetOwner, out EmpireIntel intel)
                && intel.Relation == PlayerRelation.Friend;
        }

        /// <summary>
        /// Fleet-carried gates (behavior-specs-10 §5): the jump can be made with no departure gate
        /// only if every occupied design in the fleet carries a Jump Gate (Mechanical idx 9).
        /// </summary>
        private static bool FleetCarriesJumpGates(Fleet fleet)
        {
            if (fleet.Composition.Count == 0)
            {
                return false;
            }

            foreach (ShipToken token in fleet.Composition.Values)
            {
                bool carriesOne = false;
                Hull hull = token.Design?.Blueprint != null && token.Design.Blueprint.Properties.ContainsKey("Hull")
                    ? token.Design.Hull
                    : null;
                if (hull?.Modules != null)
                {
                    foreach (HullModule module in hull.Modules)
                    {
                        if (module.AllocatedComponent != null && module.ComponentCount > 0
                            && module.AllocatedComponent.Name == "Jump Gate")
                        {
                            carriesOne = true;
                            break;
                        }
                    }
                }

                if (!carriesOne)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The pre-jump cargo dump (behavior-specs-10 §5, FUN_10b0_1f8c): colonists aboard at a
        /// departure planet that is not the fleet owner's (a friend's included) refuse the jump
        /// with nothing unloaded (message 350). Otherwise all three minerals and all colonists are
        /// added in full to the departure planet's surface stock and population (no loss, no cap,
        /// no habitability test) and the holds are zeroed; fuel stays aboard. Messages 236
        /// (minerals only), 237 (colonists only) or 238 (both) go to the fleet's owner and, when
        /// the planet belongs to a friend, to that friend too.
        /// </summary>
        /// <returns>False when the jump is refused (message 350).</returns>
        private bool DumpCargoBeforeJump(Fleet fleet, Star origin)
        {
            int colonistKilotons = fleet.Cargo.ColonistsInKilotons;
            int ironium = fleet.Cargo.Ironium;
            int boranium = fleet.Cargo.Boranium;
            int germanium = fleet.Cargo.Germanium;
            bool hasMinerals = ironium > 0 || boranium > 0 || germanium > 0;
            bool hasColonists = colonistKilotons > 0;

            if (!hasMinerals && !hasColonists)
            {
                return true;
            }

            if (hasColonists && origin.Owner != fleet.Owner)
            {
                // Message 350.
                EmitStargateFailureMessage(fleet, "it has colonists aboard and we do not control " + origin.Name);
                return false;
            }

            int colonists = fleet.Cargo.ColonistNumbers;
            origin.ResourcesOnHand.Ironium += ironium;
            origin.ResourcesOnHand.Boranium += boranium;
            origin.ResourcesOnHand.Germanium += germanium;
            if (hasColonists)
            {
                origin.Colonists += colonists;
            }

            fleet.Cargo.Ironium = 0;
            fleet.Cargo.Boranium = 0;
            fleet.Cargo.Germanium = 0;
            fleet.Cargo.ColonistsInKilotons = 0;

            string what;
            if (hasMinerals && hasColonists)
            {
                what = (ironium + boranium + germanium) + "kT of minerals and " + colonists + " colonists"; // 238
            }
            else if (hasMinerals)
            {
                what = (ironium + boranium + germanium) + "kT of minerals"; // 236
            }
            else
            {
                what = colonists + " colonists"; // 237
            }

            string text = "Fleet " + fleet.Name + " has unloaded " + what + " onto " + origin.Name
                + " before using the Stargate.";

            serverState.AllMessages.Add(new Message { Audience = fleet.Owner, Text = text, Type = "Stargate" });
            if (origin.Owner != fleet.Owner && origin.Owner != Global.Nobody)
            {
                serverState.AllMessages.Add(new Message { Audience = origin.Owner, Text = text, Type = "Stargate" });
            }

            return true;
        }

        /// <summary>Reports why an explicitly-ordered Stargate jump (WarpFactor ==
        /// Global.StargateWarpFactor) couldn't be attempted at all - see TryStargateJump's own
        /// "ineligible" return points.</summary>
        private void EmitStargateFailureMessage(Fleet fleet, string reason)
        {
            Message message = new Message();
            message.Audience = fleet.Owner;
            message.Text = "Fleet " + fleet.Name + " could not use the Stargate this turn (" + reason + ") and remains where it is.";
            message.Type = "Stargate";
            serverState.AllMessages.Add(message);
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
