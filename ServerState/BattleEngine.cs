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
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Combat;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Deal with combat between races.
    /// </summary>
    public class BattleEngine
    {
        // The injected test random, or null: then every battle location draws from its own
        // stream of the game seed (ServerData.CreateRandom("Battle"), taken afresh per location
        // in Run), so combat is repeatable and one battle's draws never shift another's.
        private Random random;
        private readonly bool randomInjected;

        private Random Rng
        {
            get { return random ?? (random = serverState != null ? serverState.CreateRandom("Battle") : new Random()); }
        }

        private readonly int movementPhasesPerRound = 3;
        private readonly int maxBattleRounds = 16;

        /// <summary>
        /// Squares of movement a Disengage-style tactic must accumulate to successfully flee the
        /// battle board. See docs/behavior-specs-5/combat-resolution.md §3, §7.
        /// </summary>
        private const double DisengageRetreatDistance = 7;
        // The above table as a 2d lookup. Note round 8 moved to the first postion as we use battleRound % 8.
        private readonly int[,] movementTable = new int[,] 
        {
            {
                0, 1, 0, 1, 0, 1, 0, 1
            },
            {
                1, 1, 1, 0, 1, 1, 1, 0
            },
            {
                1, 1, 1, 1, 1, 1, 1, 1
            },
            {
                1, 2, 1, 1, 1, 2, 1, 1
            },
            {
                1, 2, 1, 2, 1, 2, 1, 2
            },
            {
                2, 2, 2, 1, 2, 2, 2, 1
            },
            {
                2, 2, 2, 2, 2, 2, 2, 2
            },
            {
                2, 3, 2, 2, 2, 3, 2, 2
            },
            {
                2, 3, 2, 3, 2, 3, 2, 3
            }
        };


        private ServerData serverState;
        private BattleReport battle;

        /// <summary>
        /// Used to generate fleet id numbers for battle stacks.
        /// </summary>
        private uint stackId;

        private int battleRound = 0;

        /// <summary>
        /// The stacks of the battle DoBattle is currently running - needed at shot time by the
        /// weapons that pick targets of their own (gatling "hits-all" beams, beam overflow).
        /// </summary>
        private List<Stack> currentBattleStacks = new List<Stack>();

        /// <summary>
        /// Running total of the salvage (already scaled, see RecordWreck) of ships destroyed in
        /// the battle currently being processed, reset per battle location and deposited once the
        /// battle at that location ends. See behavior-specs-10/combat-resolution.md §7.
        /// </summary>
        private Resources totalSalvage = new Resources();

        /// <summary>
        /// True once at least one ship has been destroyed at the battle location being processed:
        /// the wreckage routine is only invoked then, so a battle with no losses creates no object
        /// (and the "all-zero amounts invent salvage" guard applies only to a real wreck).
        /// </summary>
        private bool wrecksThisLocation;

        /// <summary>
        /// The planet the battle being processed is over (null in deep space or when DoBattle is
        /// driven directly) - decides the salvage fraction and where dumped cargo goes.
        /// </summary>
        private Star battleStar;

        /// <summary>
        /// Keys of the fleets that jettisoned their minerals at this battle's token setup
        /// (battle plan dump option) - each of their cargo-capable tokens moves 1 slower.
        /// </summary>
        private readonly HashSet<long> dumpedFleets = new HashSet<long>();

        /// <summary>
        /// Each token's battle-movement value 0-8, worked out once per battle at token setup
        /// (ShipDesign.BattleMovementFor) - it moves (v + 2) quarter-squares a round.
        /// </summary>
        private readonly Dictionary<Stack, int> tokenMovement = new Dictionary<Stack, int>();

        /// <summary>
        /// The highest required tech level seen across every design destroyed this battle, per
        /// owning race - reset per battle location alongside totalSalvage. Feeds
        /// GrantBattleTechGains once the battle ends: "any race that had at least one ship
        /// survive the battle... becomes eligible for a chance to gain partial tech levels based
        /// on the enemy tech present in ships destroyed during the fight" - see
        /// docs/behavior-specs-5/combat-resolution.md §7 Aftermath and §9's tech-gain
        /// orchestration note.
        /// </summary>
        private Dictionary<int, TechLevel> destroyedTechByOwner = new Dictionary<int, TechLevel>();

        /// <summary>
        /// Creates a new battle engine.
        /// </summary>
        /// <param name="serverState">
        /// A <see cref="ServerState"/> which holds the state of the game.
        /// </param>
        /// <param name="battleReport">
        /// A <see cref="BattleReport"/> onto which to write the battle results.
        /// </param>
        public BattleEngine(ServerData serverState, BattleReport battleReport)
        {
            this.serverState = serverState;
            this.battle = battleReport;
        }

        /// <summary>
        /// As <see cref="BattleEngine(ServerData, BattleReport)"/>, with an explicit random
        /// source for the missile hit rolls and other draws (injectable for tests).
        /// </summary>
        public BattleEngine(ServerData serverState, BattleReport battleReport, Random random)
            : this(serverState, battleReport)
        {
            this.random = random;
            this.randomInjected = random != null;
        }

        /// <summary>
        /// Deal with any fleet battles. How the battle engine in Stars! works is
        /// documented in the Stars! FAQ (a copy is included in the documentation).
        /// </summary>
        public void Run()
        {
            // Determine the positions of any potential battles. For a battle to
            // take place 2 or more fleets must be at the same location.

            List<List<Fleet>> potentialBattles = DetermineCoLocatedFleets();

            // If there are no co-located fleets then there are no fleets at all
            // so there is nothing more to do so we can give up here.

            if (potentialBattles.Count == 0)
            {
                return;
            }

            // Eliminate potential battle locations where there is only one race
            // present.

            List<List<Fleet>> engagements = EliminateSingleRaces(potentialBattles);

            // Again this could result in an empty array. If so, give up here.

            if (engagements.Count == 0)
            {
                return;
            }

            // Tracks, per empire, how many of this turn's battles it actually took part in
            // (i.e. reached DoBattle - not merely co-located with an entirely uninvolved
            // fleet) - fed into a "You had N battles this turn" summary message once every
            // location has been processed. Recorded here (in Run(), which sees every
            // location) rather than in ReportBattle() itself, since that method only sees
            // one battle at a time.
            Dictionary<int, int> battleCountByEmpire = new Dictionary<int, int>();

            // Messages generated below (per-battle "there was a battle at X" reports) are
            // appended to serverState.AllMessages as each location is processed; the summary
            // message(s) are inserted here, at the position before any of them, once the
            // final counts are known - see the loop's end.
            int messagesInsertionIndex = serverState.AllMessages.Count;

            // We now have a list of every collection of fleets of more than one
            // race at the same location. Run through each possible combat zone,
            // build the fleet stacks and invoke the battle at each location
            // between any enemies.

            foreach (List<Fleet> coLocatedFleets in engagements)
            {
                // A fleet with no attack order against anyone else here, that nobody else here
                // has orders to attack, sits the battle out entirely: it becomes no token, keeps
                // its cargo, sees no designs, gets no report and forfeits the battle's tech-gain
                // rolls (behavior-specs-10/combat-resolution.md §1; §5 dump cargo: "a fleet of a
                // race not drawn into the fight ... keeps its cargo").
                List<Fleet> battlingFleets = FleetsDrawnIntoBattle(coLocatedFleets);
                if (battlingFleets.Count == 0)
                {
                    continue;
                }

                List<Stack> battlingStacks = ApplyTokenCap(GenerateStacks(battlingFleets));

                // If no targets get selected (for whatever reason - e.g. every race present
                // has no hostile orders toward the others, per docs/behavior-specs-5/
                // combat-resolution.md §1's "sits the battle out entirely") then there is no
                // battle HERE, but other locations later in `engagements` may still be real
                // battles - skip only this one. This was previously a `return`, which silently
                // abandoned every remaining battle location in the whole turn the instant the
                // FIRST one turned out to be a non-battle (e.g. two merely-co-located neutral
                // fleets) - a real, severe bug: any genuine battle listed after a benign
                // co-location in `engagements` would never run at all, with nothing to indicate
                // why.
                if (SelectTargets(battlingStacks) == 0)
                {
                    continue;
                }

                // This location's own seeded stream (see the random field).
                if (!randomInjected)
                {
                    random = serverState.CreateRandom("Battle");
                }

                // Each battle location gets its own fresh report. `battle` was previously
                // reused/mutated in place across every location in this same Run() call (and
                // across every location that ever runs for the whole lifetime of this
                // BattleEngine instance, since TurnGenerator constructs one BattleEngine up
                // front and calls Run() once per turn) - meaning every Message.Event and every
                // EmpireData.BattleReports entry for EVERY battle this turn ended up pointing
                // at the SAME object, which the next location's processing then overwrote in
                // place. In a turn with two or more battles, every "tap to replay" would have
                // shown only whichever battle happened to run last. Year is also stamped here
                // (previously never set at all, always 0) so Key (Year+Location) stays unique
                // across turns, not just within one.
                battle = new BattleReport();
                battle.Year = serverState.TurnYear;

                stackId = 0;
                Fleet sample = battlingFleets.First() as Fleet;

                if (sample.InOrbit != null)
                {
                    battle.Location = sample.InOrbit.Name;
                }
                else
                {
                    battle.Location = "coordinates " + sample.Position.ToString();
                }

                PositionStacks(battlingStacks);

                // Copy the full list of stacks into the battle report. We need a
                // full list to start with as the list in the battle engine will
                // get depleted during the battle and may not (and most likely will
                // not) be fully populated by the time we Serialize the
                // report. Ensure we take a copy at this point as the "real" stack
                // will mutate as processing proceeds and even ships may vanish.
                                
                foreach (Stack stack in battlingStacks)
                {
                    battle.Stacks[stack.Key] = new Stack(stack);
                }

                totalSalvage = new Resources();
                wrecksThisLocation = false;
                destroyedTechByOwner = new Dictionary<int, TechLevel>();

                battleStar = sample.InOrbit as Star;
                if (battleStar == null && sample.InOrbit != null)
                {
                    serverState.AllStars.TryGetValue(sample.InOrbit.Name, out battleStar);
                }

                // Token setup, before round 1: fleets whose plan has the dump option jettison
                // their minerals (behavior-specs-10/combat-resolution.md §5).
                DumpCargo(battlingStacks);

                DoBattle(battlingStacks);

                // Salvage (§7 correction, FUN_10f0_50e0): already worked out per kill by
                // RecordWreck - ships lost x mineral cost / 3 per stack plus the destroyed ships'
                // cargo share, scaled 8/10 over a planet with a starbase, 5/10 over one without
                // and 3/4 in deep space. Over a planet it goes to the surface stockpile whoever
                // owns it; in deep space it becomes (or adds to) the decaying wreckage at this
                // position, 30,000 kT per object with overflow into further objects at the same
                // spot (AddWreckage).
                Resources salvage = totalSalvage;
                if (battleStar != null)
                {
                    battleStar.ResourcesOnHand += salvage;
                }
                else if (wrecksThisLocation)
                {
                    // The wreckage routine is called only when a ship actually died; with none it
                    // never ran, so a bloodless deep-space battle leaves no object.
                    AddWreckage(sample.Position, salvage);
                }

                battleStar = null;
                tokenMovement.Clear();
                dumpedFleets.Clear();

                GrantBattleTechGains(battlingStacks);
                GrantOneTimeSpecialComponent(battlingStacks);

                ReportBattle();

                foreach (int empireId in battle.Losses.Keys)
                {
                    battleCountByEmpire.TryGetValue(empireId, out int count);
                    battleCountByEmpire[empireId] = count + 1;
                }
            }

            ReportBattleCounts(battleCountByEmpire, messagesInsertionIndex);
        }

        /// <summary>
        /// Adds one "You had N battles this turn" message per empire that fought at least one
        /// battle this turn, inserted ahead of this turn's own per-battle "There was a battle
        /// at X" messages (see Run()'s messagesInsertionIndex) so it reads as a lead-in summary
        /// first, matching the original game's own turn-report ordering, rather than trailing
        /// after the detail messages it's meant to summarize.
        /// </summary>
        private void ReportBattleCounts(Dictionary<int, int> battleCountByEmpire, int messagesInsertionIndex)
        {
            foreach (KeyValuePair<int, int> entry in battleCountByEmpire)
            {
                string battleWord = entry.Value == 1 ? "battle" : "battles";
                Message summary = new Message(entry.Key, "You had " + entry.Value + " " + battleWord + " this turn.", "BattleSummary", null);
                serverState.AllMessages.Insert(messagesInsertionIndex, summary);
            }
        }

        /// <summary>
        /// Determine the positions of any potential battles where the number of fleets
        /// is more than one (this scan could be more efficient but this is easier to
        /// read). 
        /// </summary>
        /// <returns>A list of all lists of co-located fleets.</returns>
        public List<List<Fleet>> DetermineCoLocatedFleets()
        {
            List<List<Fleet>> allColocatedFleets = new List<List<Fleet>>();
            Dictionary<long, bool> fleetDone = new Dictionary<long, bool>();
            
            foreach (Fleet fleetA in serverState.IterateAllFleets())
            {
                if (fleetDone.ContainsKey(fleetA.Key))
                {
                    continue;
                }

                List<Fleet> coLocatedFleets = new List<Fleet>();

                foreach (Fleet fleetB in serverState.IterateAllFleets())
                {
                    if (fleetB.Position != fleetA.Position)
                    {
                        continue;
                    }

                    coLocatedFleets.Add(fleetB);
                    fleetDone[fleetB.Key] = true;
                }

                if (coLocatedFleets.Count > 1)
                {
                    allColocatedFleets.Add(coLocatedFleets);
                }
            }
            
            return allColocatedFleets;
        }

        /// <summary>
        /// Eliminate single race groupings. Note that we know there must be at least
        /// two fleets when we determined co-located fleets earlier.
        /// </summary>
        /// <param name="fleetPositions">A list of all lists of co-located fleets.</param>
        /// <returns>The positions of all potential battles.</returns>
        public List<List<Fleet>> EliminateSingleRaces(List<List<Fleet>> allColocatedFleets)
        {
            List<List<Fleet>> allEngagements = new List<List<Fleet>>();

            foreach (List<Fleet> coLocatedFleets in allColocatedFleets)
            {
                Dictionary<int, bool> empires = new Dictionary<int, bool>();

                foreach (Fleet fleet in coLocatedFleets)
                {
                    empires[fleet.Owner] = true;
                }

                if (empires.Count > 1)
                {
                    allEngagements.Add(coLocatedFleets);
                }
            }
            return allEngagements;
        }

        /// <summary>
        /// The co-located fleets that take part in a battle here: those whose battle plan
        /// attacks some other race present, and those of a race some other fleet present has
        /// orders to attack ("if any other race present has hostile orders against you ... you
        /// are automatically a legitimate target for them regardless of your own orders"). The
        /// rest sit the battle out entirely (behavior-specs-10/combat-resolution.md §1).
        /// </summary>
        public List<Fleet> FleetsDrawnIntoBattle(List<Fleet> coLocatedFleets)
        {
            return coLocatedFleets.Where(fleet => coLocatedFleets.Any(other => other.Owner != fleet.Owner
                && (IsLegitimateTarget(serverState, fleet.Owner, fleet.BattlePlan, other.Owner)
                    || IsLegitimateTarget(serverState, other.Owner, other.BattlePlan, fleet.Owner)))).ToList();
        }

        /// <summary>
        /// Extract a list of stacks from a fleet; each ship design present on
        /// the fleet will form a distinct stack. If multiple fleets are present there may be multiple
        /// stacks of the same design present at the battle. (Or if stack ship limits are exceeded
        /// by a single fleet).
        /// </summary>
        /// <param name="fleet">The <see cref="Fleet"/> to be converted to stacks.</param>
        /// <returns>A list of stacks extracted from the fleet.</returns>
        public List<Stack> BuildFleetStacks(Fleet fleet)
        {
            List<Stack> stackList = new List<Stack>();
            
            Stack newStack = null;
            
            foreach (ShipToken token in fleet.Composition.Values)
            {             
                newStack = new Stack(fleet, stackId, token);
                
                stackList.Add(newStack);
                
                stackId++;                
            }

            // Note that each of this Stacks has it's Key UNIQUE within this battle (separate from the
            // Fleet's key),
            // consisting of Owner + stackId. The token inside is still Keyed by design.Key.
            return stackList;
        }

        /// <summary>
        /// Run through all of the fleets in an engagement and convert them to stacks of the
        /// same ship design and battle plan. We will return a complete list of all stacks at
        /// this engagement location.
        /// </summary>
        /// <param name="coLocatedFleets">A list of fleets at the given location.</param>
        /// <returns>A list of Fleets representing all stack in the engagement (1 fleet per unique stack).</returns>
        public List<Stack> GenerateStacks(List<Fleet> coLocatedFleets)
        {
            List<Stack> battlingStacks = new List<Stack>();

            foreach (Fleet fleet in coLocatedFleets)
            {
                List<Stack> fleetStacks = BuildFleetStacks(fleet);

                foreach (Stack stack in fleetStacks)
                {
                    battlingStacks.Add(stack);
                }
            }

            return battlingStacks;
        }

        /// <summary>
        /// Enforces the hard cap of 256 tokens per battle. Slots are allocated fairly, split
        /// evenly per race, with unused shares redistributed to races that need more; within a
        /// race, tokens from the fleets with the highest fleet ID numbers are dropped first. See
        /// docs/behavior-specs/combat-resolution.md §2.
        /// </summary>
        public List<Stack> ApplyTokenCap(List<Stack> battlingStacks)
        {
            const int maxTokens = 256;

            if (battlingStacks.Count <= maxTokens)
            {
                return battlingStacks;
            }

            Dictionary<int, List<Stack>> byRace = new Dictionary<int, List<Stack>>();
            foreach (Stack stack in battlingStacks)
            {
                if (!byRace.ContainsKey(stack.Owner))
                {
                    byRace[stack.Owner] = new List<Stack>();
                }

                byRace[stack.Owner].Add(stack);
            }

            // Keep the lowest fleet IDs (highest IDs are dropped first) within each race.
            foreach (List<Stack> raceStacks in byRace.Values)
            {
                raceStacks.Sort((a, b) => a.ParentKey.CompareTo(b.ParentKey));
            }

            Dictionary<int, int> allocation = new Dictionary<int, int>();
            foreach (int owner in byRace.Keys)
            {
                allocation[owner] = 0;
            }

            int slotsRemaining = maxTokens;
            List<int> owners = new List<int>(byRace.Keys);

            // Repeatedly split whatever's left evenly among races that can still use more,
            // so an even split with leftovers gets redistributed rather than wasted.
            while (slotsRemaining > 0)
            {
                List<int> wanting = owners.FindAll(o => allocation[o] < byRace[o].Count);
                if (wanting.Count == 0)
                {
                    break;
                }

                int share = Math.Max(1, slotsRemaining / wanting.Count);
                bool anyGiven = false;

                foreach (int owner in wanting)
                {
                    if (slotsRemaining <= 0)
                    {
                        break;
                    }

                    int give = Math.Min(share, Math.Min(slotsRemaining, byRace[owner].Count - allocation[owner]));
                    if (give > 0)
                    {
                        allocation[owner] += give;
                        slotsRemaining -= give;
                        anyGiven = true;
                    }
                }

                if (!anyGiven)
                {
                    break;
                }
            }

            List<Stack> capped = new List<Stack>();
            foreach (int owner in byRace.Keys)
            {
                capped.AddRange(byRace[owner].GetRange(0, allocation[owner]));
            }

            return capped;
        }

        /// <summary>
        /// Set the initial position of all of the stacks.
        /// </summary>
        /// <param name="battlingStacks">All stacks in this battle.</param>
        public void PositionStacks(List<Stack> battlingStacks)
        {
            Dictionary<int, int> empires = new Dictionary<int, int>();
            Dictionary<int, NovaPoint> racePositions = new Dictionary<int, NovaPoint>();

            foreach (Stack stack in battlingStacks)
            {
                empires[stack.Owner] = stack.Owner;
            }

            SpaceAllocator spaceAllocator = new SpaceAllocator(empires.Count, Rng);

            // Ensure that we allocate enough space so that all race stacks are
            // out of weapons range (scaled).

            int spaceSize = spaceAllocator.GridAxisCount * Global.MaxWeaponRange;

            // spaceAllocator.AllocateSpace(spaceSize);
            spaceAllocator.AllocateSpace(10); // Set to the standard Stars! battle board size - Dan 26 Jun 11
            battle.SpaceSize = spaceSize;

            // Now allocate a position for each race in the centre of one of the
            // allocated spacial chunks.

            foreach (int empireId in empires.Values)
            {
                NovaRect newPosition = spaceAllocator.GetBox();
                NovaPoint position = new NovaPoint();

                position.X = newPosition.X + (newPosition.Width / 2);
                position.Y = newPosition.Y + (newPosition.Height / 2);

                racePositions[empireId] = position;
                battle.Losses[empireId] = 0;
            }

            // Place all stacks belonging to the same race at the same position.

            foreach (Stack stack in battlingStacks)
            {
                stack.Position = racePositions[stack.Owner];
            }
            
            // Update the known designs of enemy ships.
            foreach (int empireId in empires.Values)
            {
                foreach (Stack stack in battlingStacks)
                {
                    if (stack.Owner != empireId)
                    {
                        foreach (ShipToken token in stack.Composition.Values)
                        {
                            if (serverState.AllEmpires[empireId].EmpireReports[stack.Owner].Designs.ContainsKey(token.Design.Key))
                            {
                                serverState.AllEmpires[empireId].EmpireReports[stack.Owner].Designs[token.Design.Key] = token.Design;
                            }
                            else
                            {
                                serverState.AllEmpires[empireId].EmpireReports[stack.Owner].Designs.Add(token.Design.Key, token.Design);
                            }
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Deal with a battle. This function will execute until all target fleets are
        /// destroyed or a pre-set maximum time has elapsed.   
        /// </summary>
        /// <param name="battlingStacks">All stacks in this battle.</param>
        public void DoBattle(List<Stack> battlingStacks)
        {
            currentBattleStacks = battlingStacks;

            // "Every battle starts each token at full shields; only armor damage persists
            // between battles" - behavior-specs-9/combat-resolution.md §6.
            foreach (Stack stack in battlingStacks)
            {
                if (stack.Token != null)
                {
                    stack.Token.Shields = (double)stack.Token.Design.Shield * stack.Token.Quantity;
                }
            }

            SetUpTokenMovement(battlingStacks);

            battleRound = 1;
            for (battleRound = 1; battleRound <= maxBattleRounds; ++battleRound)
            {
                // Regenerating Shields act at the START of every round after the first (rounds
                // 2-16) - behavior-specs-9/combat-resolution.md §6, race-traits.md §3 (RS).
                if (battleRound > 1)
                {
                    ApplyRegeneratingShields(battlingStacks);
                }

                if (SelectTargets(battlingStacks) == 0)
                {
                    // no more targets
                    break;
                }

                MoveStacks(battlingStacks);

                List<WeaponDetails> allAttacks = GenerateAttacks(battlingStacks);

                foreach (WeaponDetails attack in allAttacks)
                {
                    ProcessAttack(attack);
                }
            }
        }

        /// <summary>
        /// Regenerating Shields (RS), run by DoBattle at the start of rounds 2-16: every surviving
        /// token of an RS race whose shields are above zero regains one tenth of its full
        /// per-ship shield value, rounded down, for each of its ships, capped at full. "Full"
        /// is ShipDesign.Shield, which already includes the trait's 40% bonus (ShipDesign.Update).
        /// A token whose shields have reached zero gets nothing back for the rest of that battle
        /// - behavior-specs-9/combat-resolution.md §6 and race-traits.md §3 (RS, bit 13).
        /// ShipToken.Shields is a TOTAL across every ship in the token, hence the Quantity
        /// scaling below.
        /// </summary>
        private void ApplyRegeneratingShields(List<Stack> battlingStacks)
        {
            foreach (Stack stack in battlingStacks)
            {
                if (stack.Token == null || stack.IsDestroyed || stack.HasRetreated)
                {
                    continue;
                }

                if (!serverState.AllEmpires[stack.Owner].Race.HasTrait("RS"))
                {
                    continue;
                }

                int fullPerShip = stack.Token.Design.Shield;
                if (fullPerShip <= 0 || stack.Token.Shields <= 0)
                {
                    // No shields to regenerate, or knocked to zero: never regenerates again
                    // this battle (shields only ever rise through this method).
                    continue;
                }

                double maxShields = (double)fullPerShip * stack.Token.Quantity;
                double regeneration = (double)(fullPerShip / 10) * stack.Token.Quantity;
                stack.Token.Shields = Math.Min(maxShields, stack.Token.Shields + regeneration);
            }
        }

        /// <summary>
        /// Select targets (if any). Targets are set on a stack-by-stack basis. A Battle Plan's
        /// Primary Target type is searched first; only if no enemy stack matches that type does
        /// the Secondary Target type get considered. A target type not listed as either is never
        /// selected, even though that ignored stack may still be shooting back independently
        /// (AreEnemies/target selection is evaluated separately per wolf). See
        /// docs/behavior-specs-5/combat-resolution.md §4.
        /// </summary>
        /// <param name="battlingStacks">All stacks in this battle.</param>
        /// <returns>The number of targeted stacks.</returns>
        public int SelectTargets(List<Stack> battlingStacks)
        {
            int numberOfTargets = 0;

            foreach (Stack wolf in battlingStacks)
            {
                wolf.Target = null;

                // A retreated stack has left the battle - it can no longer pick a target
                // (and, per the check in SelectMostAttractiveTarget below, can no longer be
                // picked as one either). See docs/behavior-specs/combat-resolution.md §3, §7.
                // A destroyed token picks nothing either: otherwise a dead token kept "targeting"
                // (and moving) and a battle whose last enemy had died ran on to the 16-round cap
                // instead of ending at once (§7, "last race standing").
                if (wolf.IsArmed == false || wolf.HasRetreated || wolf.IsDestroyed)
                {
                    continue;
                }

                BattlePlan battlePlan = GetBattlePlan(wolf);

                wolf.Target = SelectMostAttractiveTarget(wolf, battlingStacks, battlePlan.PrimaryTarget)
                    ?? SelectMostAttractiveTarget(wolf, battlingStacks, battlePlan.SecondaryTarget);

                if (wolf.Target != null)
                {
                    numberOfTargets++;
                }
            }

            return numberOfTargets;
        }

        /// <summary>
        /// The single most attractive enemy stack matching a given target-type string, or null
        /// if no enemy stack of that type is present. Used twice per wolf by SelectTargets - once
        /// for the primary target type, and (only if that finds nothing) once for the secondary.
        /// </summary>
        private Stack SelectMostAttractiveTarget(Stack wolf, List<Stack> battlingStacks, string targetType)
        {
            Stack best = null;
            double maxAttractiveness = 0;

            foreach (Stack lamb in battlingStacks)
            {
                if (lamb.HasRetreated || !AreEnemies(wolf, lamb) || !MatchesTargetType(lamb, targetType))
                {
                    continue;
                }

                // Strictly greater: a score of 0 is never picked, and a tie keeps the earlier
                // token in battle order (behavior-specs-9/combat-resolution.md §4).
                double attractiveness = GetAttractiveness(wolf, lamb);
                if (attractiveness > maxAttractiveness)
                {
                    best = lamb;
                    maxAttractiveness = attractiveness;
                }
            }

            return best;
        }

        /// <summary>
        /// Classifies a stack against one of the seven Battle Plan target-type strings
        /// (BattlePlan.TargetOptions, plus "None") - the 7-way classifier confirmed by
        /// inspection of the exported client, docs/behavior-specs-5/combat-resolution.md §4:
        /// "Any" always matches; "Starbase" is a dedicated flag; "Armed Ships"/"Unarmed Ships"
        /// are exact opposites of each other; "Bombers" is satisfied regardless of armed state,
        /// so an unarmed bomber matches both Bombers and Unarmed Ships simultaneously, matching
        /// the documented shared-category overlap.
        ///
        /// "Freighters" and "Fuel Transports" are a best-effort approximation from cargo/fuel
        /// capacity rather than an exact match: unlike the original game, ShipDesign has no
        /// stored per-hull "role" category to switch on (Nova hulls are described purely by
        /// their component stats), so a hull with no weapons, no bomb bay, and real non-fuel
        /// cargo capacity is treated as a Freighter, and one with no weapons, no bomb bay, no
        /// cargo capacity but real fuel capacity (confirmed against the real "Fuel Transport"/
        /// "Super-Fuel Transport" hull stats: BaseCargo 0, FuelCapacity 750+) is treated as a
        /// Fuel Transport. Both therefore also match "Unarmed Ships", which is broader overlap
        /// than the original's exact-category scheme but is the best available without hull-role
        /// data.
        /// </summary>
        private static bool MatchesTargetType(Stack target, string targetType)
        {
            if (target?.Token?.Design == null)
            {
                return false;
            }

            ShipDesign design = target.Token.Design;
            bool armed = design.Weapons.Count > 0;

            switch (targetType)
            {
                case "Any":
                    return true;
                case "Starbase":
                    return design.IsStarbase;
                case "Armed Ships":
                    return armed;
                case "Unarmed Ships":
                    return !armed;
                case "Bombers":
                    return design.IsBomber;
                case "Freighters":
                    return !armed && !design.IsBomber && design.CargoCapacity > 0;
                case "Fuel Transports":
                    return !armed && !design.IsBomber && design.CargoCapacity == 0 && design.FuelCapacity > 0;
                default:
                    // "None", or an unrecognized value - never matches. See docs/behavior-specs-5/
                    // combat-resolution.md §4: "target types not listed as either primary or
                    // secondary are never fired upon by that token".
                    return false;
            }
        }

        /// <summary>
        /// Determine how attractive a target stack is to attack, from a specific attacking
        /// stack's point of view (the formula depends on the attacker's weapon type).
        /// Attractiveness = Cost / APN, where Cost is the target ship design's Boranium +
        /// resource cost (ironium/germanium excluded) and APN ("Attack Power Needed") is
        /// weapon-type-specific — roughly how much punishment the target can soak up against
        /// that weapon type. A lower APN relative to cost means a "softer" target and higher
        /// attractiveness. Credited to community researcher Art Lathrop's testing; see
        /// docs/behavior-specs/combat-resolution.md §4.
        /// </summary>
        /// <remarks>
        /// Uses the wolf's first weapon as a representative weapon for this score — the real
        /// game evaluates attractiveness per shot against whichever weapon is actually firing,
        /// which isn't modeled here since Nova tracks one target per stack, not per weapon slot
        /// (the weapon-specific overload below is used where a particular slot re-aims, e.g.
        /// beam overflow).
        /// </remarks>
        public double GetAttractiveness(Stack wolf, Stack target)
        {
            if (target == null || target.IsDestroyed)
            {
                return 0;
            }

            if (wolf.Token.Design.Weapons.Count == 0)
            {
                double fallbackCost = target.Token.Design.Cost.Boranium + target.Token.Design.Cost.Energy;
                // Fleet.IsArmed (which gates whether SelectTargets calls this at all) relies on
                // ShipDesign.HasWeapons, which — a pre-existing Nova quirk — is always true
                // (it checks the Weapons list for null, but it's initialized to an empty list,
                // never null). So a stack can reach here with no actual weapon to derive APN
                // from. Fall back to a simple cost-vs-defense ratio rather than reporting zero
                // attractiveness, which would make such a stack unable to ever pick a target.
                double fallbackDefense = target.Token.Armor + target.Token.Shields;
                return fallbackDefense > 0 ? fallbackCost / fallbackDefense : double.MaxValue;
            }

            return GetAttractiveness(wolf, target, wolf.Token.Design.Weapons[0]);
        }

        /// <summary>
        /// As <see cref="GetAttractiveness(Stack, Stack)"/>, scored for one specific firing
        /// weapon. 0 means "never pick this target".
        /// </summary>
        public double GetAttractiveness(Stack wolf, Stack target, Weapon weapon)
        {
            if (target == null || target.IsDestroyed)
            {
                return 0;
            }

            // behavior-specs-10/combat-resolution.md §4 (FUN_10f0_4326 :101708-101723): the cost
            // is the whole token's - (resources + boranium) x ships - scaled x100 when below
            // 100,000 and otherwise fixed at 10,000,000; armor and shields are whole-token
            // figures too (remaining armor at least 1, the pooled shields). Range only decides
            // eligibility (no range term). Every score is at least 1, except that a sapper skips
            // a shieldless token (0, never picked).
            ShipDesign design = target.Token.Design;
            int ships = Math.Max(1, target.Token.Quantity);
            long cost = ((long)design.Cost.Energy + design.Cost.Boranium) * ships;
            long scaledCost = cost < 100000 ? cost * 100 : 10000000L;
            long armor = Math.Max(1L, (long)Math.Floor(target.Token.Armor));
            long shields = Math.Max(0L, (long)Math.Floor(target.Token.Shields));

            if (!weapon.IsMissile)
            {
                // A beam first multiplies the scaled cost by the target's deflector percentage
                // when that is below 100. Standard beam: 100 x that / (armor + shields + 1);
                // sapper: 100 x that / shields, rounded up.
                int deflectorPercent = design.BeamDeflectorPercent;
                if (deflectorPercent < 100)
                {
                    scaledCost = scaledCost * deflectorPercent / 100;
                }

                if (weapon.Group == WeaponType.shieldSapper)
                {
                    if (shields <= 0)
                    {
                        return 0;
                    }

                    return Math.Max(1L, ((100 * scaledCost) + shields - 1) / shields);
                }

                return Math.Max(1L, 100 * scaledCost / (armor + shields + 1));
            }

            // Torpedo or missile: the scaled cost (no deflector term, no extra x100) over the APN.
            double accuracy = CalculateWeaponAccuracy(wolf, weapon, target) / 100.0;
            if (accuracy <= 0)
            {
                return 1;
            }

            int weaponTypeFactor = (weapon.Group == WeaponType.missile) ? 2 : 1;
            double apn;
            if (shields >= armor)
            {
                apn = (armor * 2) / accuracy;
            }
            else
            {
                apn = ((shields * 2) / accuracy) + ((armor - shields) / (accuracy * weaponTypeFactor));
            }

            if (apn <= 0)
            {
                return scaledCost;
            }

            return Math.Max(1.0, Math.Floor(scaledCost / apn));
        }

        /// <summary>
        /// Determine if one stack is a potential target of the other. This depends not
        /// just on the relation (friend, enemy, etc.) but also on the battle plan of
        /// the "wolf" stack (e.g. attack everyone, attack enemies, etc.).
        /// </summary>
        /// <param name="wolf">Potential attacker.</param>
        /// <param name="lamb">Potential target.</param>
        /// <returns>True if lamb is a valid target for wolf.</returns>
        public bool AreEnemies(Fleet wolf, Fleet lamb)
        {
            return IsLegitimateTarget(serverState, wolf.Owner, wolf.BattlePlan, lamb.Owner);
        }

        /// <summary>
        /// The shared "legitimate enemies" check behind both ship-vs-ship targeting
        /// (AreEnemies above) and planetary bombing (Bombing.Bomb) - the same Battle Plan
        /// Attack/TargetId settings drive both, per docs/behavior-specs-5/combat-resolution.md
        /// §1/§4 and §9's "checking each fleet's stored hostility disposition (via the same
        /// per-race-pair relationship lookup used for the 'legitimate enemies' bitmask)". Static
        /// (rather than an instance method only BattleEngine can call) specifically so Bombing -
        /// a separate class with no BattleEngine reference - can reuse the exact same rule
        /// instead of its own narrower hardcoded Enemy-relation-only check.
        /// </summary>
        /// <param name="serverState">Server state, for empire/battle-plan/relationship lookups.</param>
        /// <param name="attackerOwner">The potential attacker's owning empire.</param>
        /// <param name="attackerBattlePlanName">The potential attacker's Battle Plan key.</param>
        /// <param name="targetOwner">The potential target's owning empire.</param>
        /// <returns>True if targetOwner is a legitimate target for attackerOwner.</returns>
        public static bool IsLegitimateTarget(ServerData serverState, ushort attackerOwner, string attackerBattlePlanName, ushort targetOwner)
        {
            if (attackerOwner == targetOwner)
            {
                return false;
            }

            EmpireData attackerData = serverState.AllEmpires[attackerOwner];

            // The relation consulted is the attacker's OWN opinion of the target owner (0
            // Neutral, 1 Friend, 2 Enemy); the target's opinion is never read
            // (behavior-specs-10/turn-generation-engine.md §4, "Who bombs whom").
            PlayerRelation targetRelation = attackerData.EmpireReports.TryGetValue(targetOwner, out EmpireIntel intel)
                ? intel.Relation
                : PlayerRelation.Neutral;

            if (!attackerData.BattlePlans.TryGetValue(attackerBattlePlanName ?? string.Empty, out BattlePlan battlePlan))
            {
                return false;
            }

            // Attack Who: Nobody (0) never; Enemies (1) only an Enemy opinion; Neutrals and
            // Enemies (2) unless Friend; Everyone (3) always, Friends included; 4 and up name one
            // specific player. TargetId is consulted ONLY for that last setting - previously a
            // stale TargetId matched before the Attack value was checked, so a "None" plan still
            // fought (and bombed) that player ("Nobody never bombs", spec-10 coverage combat
            // row 2 / turn row 19).
            switch (battlePlan.Attack)
            {
                case "None":
                    return false;
                case "Everyone":
                    return true;
                case "Enemies":
                    return targetRelation == PlayerRelation.Enemy;
                case "Enemies and Neutrals":
                    return targetRelation != PlayerRelation.Friend;
                default:
                    // Any other value is the specific-player setting (e.g.
                    // SpecificPlayerAttack): only TargetId's planets and fleets.
                    return battlePlan.TargetId == targetOwner;
            }
        }

        /// <summary>
        /// The Attack Who value that selects the single player held in BattlePlan.TargetId (any
        /// value other than the four named settings is treated the same way).
        /// </summary>
        public const string SpecificPlayerAttack = "Specific Player";

         /// <summary>
        /// Move stacks towards their targets (if any). Record each movement in the
        /// battle report.
        /// </summary>
        /// <param name="battlingStacks">All stacks in the battle.</param>
        public void MoveStacks(List<Stack> battlingStacks)
        {
            // Movement in Squares per Round
            //                  Round
            // Movement  1  2  3  4  5  6  7  8
            // 1/2       1  0  1  0  1  0  1  0
            // 3/4       1  1  0  1  1  1  0  1
            // 1         1  1  1  1  1  1  1  1
            // 1 1/4     2  1  1  1  2  1  1  1
            // 1 1/2     2  1  2  1  2  1  2  1
            // 1 3/4     2  2  1  2  2  2  1  2
            // 2         2  2  2  2  2  2  2  2
            // 2 1/4     3  2  2  2  3  2  2  2
            // 2 1/2     3  2  3  2  3  2  3  2
            // repeats for rounds 9 - 16

            // In Stars! each round breaks movement into 3 phases.
            // Phase 1: All stacks that can move 3 squares this round get to move 1 square.
            // Phase 2: All stacks that can move 2 or more squares this round get to move 1 square.
            // Phase 3: All stacks that can move this round get to move 1 square.
            // TODO (priority 3) - verify that a ship should be able to move 1 square per phase if it has 3 move points, or is it limited to 1 per turn?
            // The movement order is computed once per round (each token's r applies to all three
            // of the round's steps).
            List<Stack> movementOrder = MovementOrder(battlingStacks, Rng, MovementWeight);
            for (var phase = 1; phase <= movementPhasesPerRound; phase++)
            {
                foreach (Stack stack in movementOrder)
                {
                    // A stack that has already accumulated 7 squares under a Disengage-style
                    // tactic has successfully fled the battle - it moves no further (and, per
                    // the checks added to SelectTargets/GenerateAttacks/ProcessAttack, neither
                    // fires nor can be fired upon for the remainder of the fight). See
                    // docs/behavior-specs/combat-resolution.md §3, §7.
                    if (stack.HasRetreated)
                    {
                        continue;
                    }

                    if (stack.Target != null & !stack.IsStarbase)
                    {
                        NovaPoint from = stack.Position;
                        NovaPoint to = ChooseMoveTarget(stack, battlingStacks);

                        // The table's rows are the battle-movement values 0-8 (1/2 to 2 1/2
                        // squares, (v + 2) quarter-squares a round) - behavior-specs-10/
                        // combat-resolution.md §5.
                        int movesThisRound = movementTable[TokenMovement(stack), battleRound % 8];

                        bool moveThisPhase = true;
                        switch (phase)
                        {
                            case 1:
                                {
                                    moveThisPhase = movesThisRound == 3;
                                    break;
                                }
                            case 2:
                                {
                                    moveThisPhase = movesThisRound >= 2;
                                    break;
                                }
                            case 3:
                                {
                                    moveThisPhase = movesThisRound >= 1;
                                    break;
                                }
                        }

                        // stack can move only after accumulating at least 1 move point, and after doing so expends that 1 move point
                        if (moveThisPhase)
                        {
                            stack.Position = PointUtilities.BattleMoveTo(from, to);

                            if (IsDisengaging(stack))
                            {
                                // Squares of movement on the battle grid, whose distance is the
                                // larger of the column and row differences (combat-resolution.md
                                // §6): a diagonal step is one square, not 1.41 (it was Euclidean).
                                stack.DisengageDistanceAccumulated += Math.Max(Math.Abs(stack.Position.X - from.X), Math.Abs(stack.Position.Y - from.Y));

                                if (stack.DisengageDistanceAccumulated >= DisengageRetreatDistance)
                                {
                                    stack.HasRetreated = true;

                                    string fleetName = serverState.AllEmpires[stack.Owner].OwnedFleets.TryGetValue(stack.ParentKey, out Fleet ownedFleet)
                                        ? ownedFleet.Name
                                        : stack.Name;

                                    Message message = new Message();
                                    message.Audience = stack.Owner;
                                    message.Text = "Fleet " + fleetName + " has successfully disengaged from battle at "
                                        + battle.Location + ".";
                                    message.Type = "Battle";
                                    serverState.AllMessages.Add(message);
                                }
                            }

                            // Update the battle report with these movements.
                            BattleStepMovement report = new BattleStepMovement();
                            report.StackKey = stack.Key;
                            report.Position = stack.Position;
                            battle.Steps.Add(report);
                        }
                    }
                    // TODO (priority 7) - shouldn't stacks without targets flee the battle if their strategy says to do so? they're sitting ducks now!
                }
            }
        }

        /// <summary>
        /// The effective movement weight E of a token (combat-resolution.md section 5, "Movement
        /// order within a step", FUN_10f0_5950): W + 2 x (r - 7) x W / 100, the division
        /// truncating toward zero, where W is the per-ship weight and r the token's 0-14 random
        /// term for the round.
        /// </summary>
        public static int EffectiveMovementWeight(double weight, int randomTerm)
        {
            int w = (int)Math.Max(0.0, weight);
            return w + (2 * (randomTerm - 7) * w) / 100;
        }

        /// <summary>
        /// The order in which tokens take a movement step (combat-resolution.md section 5,
        /// "Movement order within a step", FUN_10f0_5950): each token draws its 0-14 random term
        /// and sorts by its effective weight E, heaviest first, ties in token-array order (the
        /// battle-start shuffle). Computed once per round and reused for that round's three steps,
        /// so all three share the same r. The default weight is the stack's own mass; the engine
        /// passes <see cref="MovementWeight"/>.
        /// </summary>
        public static List<Stack> MovementOrder(IEnumerable<Stack> stacks, Random random, Func<Stack, double> weight = null)
        {
            Func<Stack, double> weigh = weight ?? (s => s.Mass);
            List<(Stack Stack, int Effective)> order = new List<(Stack, int)>();
            foreach (Stack stack in stacks)
            {
                int randomTerm = random != null ? random.Next(15) : 7;
                order.Add((stack, EffectiveMovementWeight(weigh(stack), randomTerm)));
            }

            // OrderByDescending is stable, so equal E keeps the token-array order.
            return order.OrderByDescending(entry => entry.Effective).Select(entry => entry.Stack).ToList();
        }

        /// <summary>
        /// A token's per-ship battle weight W for the movement order: the mass of one ship of its
        /// design plus that ship's share of its fleet's cargo (<see cref="CargoShare"/>, the same
        /// share the battle-movement value uses). It is a per-ship figure, not the stack's total
        /// mass (combat-resolution.md section 5).
        /// </summary>
        private double MovementWeight(Stack stack)
        {
            ShipToken token = stack.Token;
            if (token?.Design == null)
            {
                return 0;
            }

            return token.Design.Mass + CargoShare(stack);
        }

        /// <summary>
        /// The stack's Battle Plan, looked up from its owning empire.
        /// </summary>
        private BattlePlan GetBattlePlan(Stack stack)
        {
            return serverState.AllEmpires[stack.Owner].BattlePlans[stack.BattlePlan];
        }

        /// <summary>
        /// The real fleet a stack was built from (a Stack is a copy that shares only the token),
        /// or null when it is not in its owner's fleet list (e.g. a test-built stack).
        /// </summary>
        private Fleet ParentFleet(Stack stack)
        {
            if (serverState.AllEmpires.TryGetValue(stack.Owner, out EmpireData empire)
                && empire.OwnedFleets.TryGetValue(stack.ParentKey, out Fleet fleet))
            {
                return fleet;
            }

            return null;
        }

        /// <summary>
        /// A token's battle-movement value 0-8 (see <see cref="SetUpTokenMovement"/>); the design
        /// card value for a stack that was never set up.
        /// </summary>
        private int TokenMovement(Stack stack)
        {
            if (tokenMovement.TryGetValue(stack, out int movement))
            {
                return movement;
            }

            return stack.Token?.Design == null ? 0 : stack.Token.Design.BattleMovement;
        }

        /// <summary>
        /// Token setup's battle movement (behavior-specs-10/combat-resolution.md §5,
        /// FUN_10f0_2184 via FUN_10f0_2cca): each token's value from its design, its owner's War
        /// Monger bonus, the dump penalty and its share of its fleet's cargo, worked out after any
        /// dump (so dumped minerals no longer count; colonists still do).
        /// </summary>
        private void SetUpTokenMovement(List<Stack> battlingStacks)
        {
            tokenMovement.Clear();

            foreach (Stack stack in battlingStacks)
            {
                if (stack.Token?.Design == null)
                {
                    continue;
                }

                ShipDesign design = stack.Token.Design;
                bool warMonger = serverState.AllEmpires.TryGetValue(stack.Owner, out EmpireData empire)
                    && empire.Race != null && empire.Race.HasTrait("WM");

                tokenMovement[stack] = design.BattleMovementFor(CargoShare(stack), dumpedFleets.Contains(stack.ParentKey), warMonger);
            }
        }

        /// <summary>
        /// One ship's share of its fleet's cargo mass: cargo x the design's cargo capacity / the
        /// fleet's total cargo capacity (e.g. 400 kT over two 210 kT freighters is 200 kT each).
        /// </summary>
        private int CargoShare(Stack stack)
        {
            Fleet fleet = ParentFleet(stack);
            int capacity = stack.Token.Design.CargoCapacity;
            if (fleet == null || capacity <= 0 || fleet.Cargo.Mass <= 0)
            {
                return 0;
            }

            long fleetCapacity = 0;
            foreach (ShipToken token in fleet.Composition.Values)
            {
                fleetCapacity += (long)token.Design.CargoCapacity * token.Quantity;
            }

            return fleetCapacity <= 0 ? 0 : (int)((long)fleet.Cargo.Mass * capacity / fleetCapacity);
        }

        /// <summary>
        /// Dumping cargo in battle (behavior-specs-10/combat-resolution.md §5, FUN_10f0_1754):
        /// once, at token setup, every fleet drawn into the battle whose battle plan has the dump
        /// option and that carries some ironium, boranium or germanium empties all three holds -
        /// onto the orbited planet's surface whoever owns it, or into wreckage at the fleet's
        /// position in deep space. Colonists are never dumped. No message is sent.
        /// </summary>
        private void DumpCargo(List<Stack> battlingStacks)
        {
            dumpedFleets.Clear();
            HashSet<long> seen = new HashSet<long>();

            foreach (Stack stack in battlingStacks)
            {
                if (!seen.Add(stack.ParentKey))
                {
                    continue;
                }

                Fleet fleet = ParentFleet(stack);
                if (fleet == null || !GetBattlePlan(stack).DumpCargo)
                {
                    continue;
                }

                Resources minerals = new Resources(fleet.Cargo.Ironium, fleet.Cargo.Boranium, fleet.Cargo.Germanium, 0);
                if (minerals.Ironium + minerals.Boranium + minerals.Germanium <= 0)
                {
                    continue;
                }

                fleet.Cargo.Ironium = 0;
                fleet.Cargo.Boranium = 0;
                fleet.Cargo.Germanium = 0;

                if (battleStar != null)
                {
                    battleStar.ResourcesOnHand += minerals;
                }
                else
                {
                    AddWreckage(fleet.Position, minerals);
                }

                dumpedFleets.Add(stack.ParentKey);
            }
        }

        /// <summary>Most minerals (kT, all three summed) one wreckage object holds.</summary>
        public const int MaxWreckageKilotons = 30000;

        /// <summary>Adds minerals to the deep-space wreckage at a position.</summary>
        private void AddWreckage(NovaPoint position, Resources minerals)
        {
            AddWreckage(serverState, position, minerals);
        }

        /// <summary>
        /// The wreckage routine (FUN_10f0_1850; behavior-specs-10/combat-resolution.md §5 dump
        /// cargo, §7): minerals left in deep space become wreckage objects at the position. Each
        /// object holds at most 30,000 kT and the rest overflows into further objects at the same
        /// spot (keyed "position", "position#1", "position#2"...); existing objects there are
        /// topped up first, filling ironium, then boranium, then germanium. If the position
        /// coincides with a planet's map position no object is made and the minerals are lost.
        /// Energy is not a mineral and is never left as wreckage.
        /// </summary>
        public static void AddWreckage(ServerData serverState, NovaPoint position, Resources minerals)
        {
            AddWreckage(serverState, position, minerals, forceNew: false);
        }

        /// <summary>
        /// As <see cref="AddWreckage(ServerData, NovaPoint, Resources)"/>, with
        /// <paramref name="forceNew"/> making the first object at the position a brand-new one
        /// rather than a top-up of whatever is already there (Scrap Fleet always creates a new
        /// object; the battle, cargo-dump and minefield callers top up).
        /// </summary>
        public static void AddWreckage(ServerData serverState, NovaPoint position, Resources minerals, bool forceNew)
        {
            if (serverState == null || position == null || minerals == null)
            {
                return;
            }

            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Position != null && star.Position.X == position.X && star.Position.Y == position.Y)
                {
                    return;
                }
            }

            int[] left = { Math.Max(0, minerals.Ironium), Math.Max(0, minerals.Boranium), Math.Max(0, minerals.Germanium) };
            if (left[0] + left[1] + left[2] <= 0)
            {
                // All-zero amounts invent salvage: 0-9 kT of each mineral at random, redrawn until
                // the total is positive (combat-resolution.md §7 "Guards").
                do
                {
                    left[0] = GameRandom.Current.Next(0, 10);
                    left[1] = GameRandom.Current.Next(0, 10);
                    left[2] = GameRandom.Current.Next(0, 10);
                }
                while (left[0] + left[1] + left[2] <= 0);
            }

            string baseKey = position.ToHashString();
            int slot = 0;
            if (forceNew)
            {
                while (serverState.AllDeepSpaceMinerals.ContainsKey(slot == 0 ? baseKey : baseKey + "#" + slot))
                {
                    slot++;
                }
            }

            // The one-year grace mark is set on the FIRST object that receives minerals (newly
            // created or topped up); an overflow object created inside the loop is not marked
            // (combat-resolution.md §7 "Grace mark").
            bool gracePending = true;
            for (; left[0] + left[1] + left[2] > 0; slot++)
            {
                string key = slot == 0 ? baseKey : baseKey + "#" + slot;
                if (!serverState.AllDeepSpaceMinerals.TryGetValue(key, out DeepSpaceMinerals wreckage))
                {
                    wreckage = new DeepSpaceMinerals(position);
                    serverState.AllDeepSpaceMinerals[key] = wreckage;
                }

                Resources held = wreckage.Minerals;
                int room = MaxWreckageKilotons - (Math.Max(0, held.Ironium) + Math.Max(0, held.Boranium) + Math.Max(0, held.Germanium));
                int[] add = new int[3];
                for (int mineral = 0; mineral < 3 && room > 0; mineral++)
                {
                    add[mineral] = Math.Min(room, left[mineral]);
                    left[mineral] -= add[mineral];
                    room -= add[mineral];
                }

                if (add[0] + add[1] + add[2] > 0)
                {
                    if (gracePending)
                    {
                        wreckage.DecayGrace = true;
                        gracePending = false;
                    }

                    wreckage.Minerals = held + new Resources(add[0], add[1], add[2], 0);
                }
            }
        }

        /// <summary>
        /// The wreck routine (behavior-specs-10/combat-resolution.md §7 correction,
        /// FUN_10f0_50e0), called each time ships of a token die and BEFORE they are removed:
        /// per mineral, ships lost x the design's mineral cost / 3 (truncated), plus the
        /// destroyed ships' share of the fleet's mineral cargo (all of it when the whole fleet
        /// dies, which is taken off the fleet), then x 8/10 over a planet with a starbase, x 5/10
        /// over one without, or minus a quarter (rounded down) in deep space. Energy (resources)
        /// is not salvaged.
        /// </summary>
        private void RecordWreck(Stack target, int shipsLost)
        {
            if (shipsLost <= 0 || target.Token?.Design == null)
            {
                return;
            }

            wrecksThisLocation = true;

            // A Bleeding Edge owner's costs are recomputed at current tech with the doubling
            // switched off (combat-resolution.md §7, ship-design-and-components.md §8: the
            // FUN_10f0_50e0 trait-12 recompute serves wreckage valuation).
            Resources cost = target.Token.Design.CostWithoutBleedingEdgeDoubling;
            long ironium = (long)shipsLost * cost.Ironium / 3;
            long boranium = (long)shipsLost * cost.Boranium / 3;
            long germanium = (long)shipsLost * cost.Germanium / 3;

            Fleet fleet = ParentFleet(target);
            if (fleet != null && fleet.Cargo.Ironium + fleet.Cargo.Boranium + fleet.Cargo.Germanium > 0)
            {
                long fleetShips = 0;
                long fleetCapacity = 0;
                foreach (ShipToken token in fleet.Composition.Values)
                {
                    fleetShips += token.Quantity;
                    fleetCapacity += (long)token.Design.CargoCapacity * token.Quantity;
                }

                long lostCapacity = (long)shipsLost * target.Token.Design.CargoCapacity;
                bool wholeFleet = shipsLost >= fleetShips;

                int ironiumShare = wholeFleet ? fleet.Cargo.Ironium : CargoPart(fleet.Cargo.Ironium, lostCapacity, fleetCapacity);
                int boraniumShare = wholeFleet ? fleet.Cargo.Boranium : CargoPart(fleet.Cargo.Boranium, lostCapacity, fleetCapacity);
                int germaniumShare = wholeFleet ? fleet.Cargo.Germanium : CargoPart(fleet.Cargo.Germanium, lostCapacity, fleetCapacity);

                fleet.Cargo.Ironium -= ironiumShare;
                fleet.Cargo.Boranium -= boraniumShare;
                fleet.Cargo.Germanium -= germaniumShare;

                ironium += ironiumShare;
                boranium += boraniumShare;
                germanium += germaniumShare;
            }

            totalSalvage += new Resources(
                (int)ScaleSalvage(ironium),
                (int)ScaleSalvage(boranium),
                (int)ScaleSalvage(germanium),
                0);
        }

        private static int CargoPart(int amount, long lostCapacity, long fleetCapacity)
        {
            return fleetCapacity <= 0 ? 0 : (int)(amount * lostCapacity / fleetCapacity);
        }

        /// <summary>The salvage location scaling of <see cref="RecordWreck"/>.</summary>
        private long ScaleSalvage(long amount)
        {
            if (battleStar != null)
            {
                return amount * (battleStar.Starbase != null ? 8 : 5) / 10;
            }

            return amount - (amount / 4);
        }

        /// <summary>
        /// Whether a stack is currently running away under a Disengage-style tactic (either
        /// plain Disengage, or Disengage if Challenged after it has taken its first hit).
        /// </summary>
        private bool IsDisengaging(Stack stack)
        {
            string tactic = GetBattlePlan(stack).Tactic;
            return tactic == "Disengage" || (tactic == "Disengage if Challenged" && stack.HasTakenDamage);
        }

        /// <summary>
        /// True if any enemy stack currently has a weapon in range of this stack's position.
        /// </summary>
        private bool IsThreatened(Stack self, List<Stack> battlingStacks)
        {
            return FindNearestThreat(self, battlingStacks) != null;
        }

        /// <summary>
        /// The closest enemy stack that has a weapon able to reach this stack's current
        /// position, or null if none does.
        /// </summary>
        private Stack FindNearestThreat(Stack self, List<Stack> battlingStacks)
        {
            Stack nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (Stack other in battlingStacks)
            {
                if (other.IsDestroyed || other.HasRetreated || !AreEnemies(other, self) || other.Token.Design.Weapons.Count == 0)
                {
                    continue;
                }

                // Grid (Chebyshev) distance against the enemy's stored range - the movement AI
                // deliberately ignores a starbase's +1 reach (combat-resolution.md §3 quirk).
                double distance = GridDistance(self, other);
                int maxEnemyRange = 0;
                foreach (Weapon weapon in other.Token.Design.Weapons)
                {
                    maxEnemyRange = Math.Max(maxEnemyRange, weapon.Range);
                }

                if (distance <= maxEnemyRange && distance < nearestDistance)
                {
                    nearest = other;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Picks the square a stack should move toward this phase, based on its Battle Plan's
        /// tactic. See docs/behavior-specs/combat-resolution.md §3 for the six documented
        /// tactics.
        /// </summary>
        /// <remarks>
        /// Simplifications from the documented behavior: "Maximise Net Damage" and "Maximise
        /// Damage Ratio" are both treated the same as "Maximise Damage" here (close to point-blank
        /// for beam ships; the finer distinctions between optimizing net damage ratio vs. using
        /// only the single longest-ranged weapon aren't modeled). "Minimise Damage to Self"'s
        /// "close in without moving toward the enemy" nuance when unthreatened is approximated as
        /// simply closing toward the target. Random movement (used when Disengage can't increase
        /// or hold distance, or has no enemy in range) is approximated as holding position rather
        /// than picking a random square, since Nova's board-boundary handling for random moves
        /// wasn't established.
        /// </remarks>
        private NovaPoint ChooseMoveTarget(Stack stack, List<Stack> battlingStacks)
        {
            string tactic = GetBattlePlan(stack).Tactic;
            bool disengaging = IsDisengaging(stack);
            bool minimisingSelfDamage = tactic == "Minimise Damage to Self" && IsThreatened(stack, battlingStacks);

            if (disengaging || minimisingSelfDamage)
            {
                Stack threat = FindNearestThreat(stack, battlingStacks);
                if (threat == null)
                {
                    return stack.Position; // no enemy in range: "move randomly" approximated as holding
                }

                int dx = Math.Sign(stack.Position.X - threat.Position.X);
                int dy = Math.Sign(stack.Position.Y - threat.Position.Y);

                if (dx == 0 && dy == 0)
                {
                    return stack.Position; // can't increase distance from directly on top of the threat: hold
                }

                return new NovaPoint(stack.Position.X + dx, stack.Position.Y + dy);
            }

            // Maximise Damage / Maximise Net Damage / Maximise Damage Ratio (and Minimise Damage
            // to Self when not currently threatened): close on the current target.
            return stack.Target.Position;
        }

        /// <summary>
        /// Fire weapons at selected targets.
        /// </summary>
        /// <param name="battlingStacks">All stacks in the battle.</param>
        private List<WeaponDetails> GenerateAttacks(List<Stack> battlingStacks)
        {
            // First, identify all of the weapons and their characteristics for
            // every ship stack present at the battle and who they are pointed at.

            List<WeaponDetails> allAttacks = new List<WeaponDetails>();

            foreach (Stack stack in battlingStacks)
            {
                if ( ! stack.IsDestroyed && !stack.HasRetreated)
                {
                    // generate an attack for each weapon slot in the Design (all ships in the Token fire weapons in the same slot at the same time)
                    foreach (Weapon weaponSystem in stack.Token.Design.Weapons)
                    {
                        WeaponDetails weapon = new WeaponDetails();

                        weapon.SourceStack = stack;
                        weapon.TargetStack = stack.Target;
                        weapon.Weapon = weaponSystem;

                        allAttacks.Add(weapon);
                    }
                }
            }
            
            // Sort the weapon list according to weapon system initiative.
            allAttacks.Sort();
            
            return allAttacks;
        }

        /// <summary>
        /// Attempt an attack.
        /// </summary>
        /// <param name="allAttacks">A list of WeaponDetails representing a round of attacks.</param>
        private bool ProcessAttack(WeaponDetails attack)
        {
            // Gatling ("hits-all") beams pick their own targets: every eligible enemy in reach.
            if (attack.Weapon.Group == WeaponType.gatlingGun)
            {
                return FireGatling(attack.SourceStack, attack.Weapon);
            }

            // First, check that the target stack we originally identified has not
            // been destroyed (actually, the stack still exists at this point but
            // it may have no ship tokens left). In which case, don't bother trying to
            // fire this weapon system (we'll wait until the next battle clock
            // "tick" and re-target then).    
            if (attack.TargetStack == null || attack.TargetStack.IsDestroyed || attack.TargetStack.HasRetreated)
            {
                return false;
            }

            if (attack.SourceStack == null || attack.SourceStack.IsDestroyed)
            {
                // Report.Error("attacking stack no longer exists");
                return false;
            }

            // If the target stack is not within the reach of this weapon system
            // then there is no point in trying to fire it. Reach is the stored range (+1 for a
            // starbase firer) measured as Chebyshev grid distance - behavior-specs-9/
            // combat-resolution.md §6 (previously Euclidean, so a range-1 weapon could not hit
            // a diagonally adjacent square).
            if (GridDistance(attack.SourceStack, attack.TargetStack) > WeaponReach(attack.SourceStack, attack.Weapon))
            {
                return false;
            }

            // Target is valid; execute attack. 
            ExecuteAttack(attack);
            
            return true;
        }

        /// <summary>
        /// DischargeWeapon. We know the weapon and we know the target stack so attack.
        /// </summary>
        /// <param name="ship">The firing stack.</param>
        /// <param name="details">The weapon being fired.</param>
        /// <param name="target">The target stack.</param>
        private void ExecuteAttack(WeaponDetails attack)
        {
            // the two stacks involved in the attack          
            Stack attacker = attack.SourceStack;
            Stack target = attack.TargetStack;
            
            // Report on the targeting.
            BattleStepTarget report = new BattleStepTarget();
            report.StackKey = attack.SourceStack.Key;
            report.TargetKey = attack.TargetStack.Key;
            battle.Steps.Add(report);

            // Identify the attack parameters that have to take into account
            // factors other than the base values (e.g. jammers, capacitors, etc.)
            double hitPower = CalculateWeaponPower(attacker, attack.Weapon, target);
            double accuracy = CalculateWeaponAccuracy(attacker, attack.Weapon, target);

            if (attack.Weapon.IsMissile)
            {
                FireMissile(attacker, target, hitPower, accuracy, attack.Weapon);
                DestroyIfDead(attacker, target);
            }
            else
            {
                // Removes every token it destroys itself (a beam can overflow onto further
                // targets).
                FireBeam(attacker, target, hitPower, attack.Weapon);
            }
        }

        /// <summary>
        /// If the target has no ships or no armor left, remove it from the battle (see
        /// DestroyStack). If we still have some Armor then the stack hasn't been destroyed yet.
        /// </summary>
        private void DestroyIfDead(Stack attacker, Stack target)
        {
            if (target.Token != null && (target.Token.Quantity <= 0 || target.Token.Armor <= 0))
            {
                DestroyStack(attacker, target);
            }
        }

        /// <summary>
        /// All Defenses are gone. Remove the stack from the battle (which
        /// exists only during the battle) and, more importantly, remove the
        /// token from its "real" fleet. Also, generate a "destroy" event to
        /// update the battle visualization display.
        /// </summary>
        /// <param name="target"></param>
        private void DestroyStack(Stack attacker, Stack target)
        {
            // report the losses
            // Ships destroyed by earlier shots this battle (whole-ship kills within a shot that
            // didn't finish off the whole token) were already counted in DamageArmor as they
            // happened; only the remaining stragglers are counted here, to avoid double-counting.
            if (target.Token.Quantity > 0)
            {
                battle.Losses[target.Owner] = battle.Losses[target.Owner] + target.Token.Quantity;

                // Destroyed ships leave salvage (RecordWreck), deposited at the end of the
                // battle - behavior-specs-10/combat-resolution.md §7.
                RecordWreck(target, target.Token.Quantity);
                RecordDestroyedTech(target);
            }

            // for the battle viewer / report
            BattleStepDestroy destroy = new BattleStepDestroy();
            destroy.StackKey = target.Key;
            battle.Steps.Add(destroy);

            // remove the Token from the Fleet, if it exists
            if (serverState.AllEmpires[target.Owner].OwnedFleets.ContainsKey(target.ParentKey))
            {
                serverState.AllEmpires[target.Owner].OwnedFleets[target.ParentKey].Composition.Remove(target.Token.Key); // remove the token from the fleet

                // remove the fleet if no more tokens
                if (serverState.AllEmpires[target.Owner].OwnedFleets[target.ParentKey].Composition.Count == 0) 
                {
                    serverState.AllEmpires[target.Owner].OwnedFleets.Remove(target.ParentKey);
                    serverState.AllEmpires[target.Owner].FleetReports.Remove(target.ParentKey);
                    serverState.AllEmpires[attacker.Owner].FleetReports.Remove(target.ParentKey);  // added in Rev# 872
                }
            }

            // remove the token from the Stack (do this last so target.Token remains valid above)
            target.Composition.Remove(target.Key);
        }

        /// <summary>
        /// Do beam weapon damage (standard or sapper beam), including overflow onto further
        /// targets - behavior-specs-9/combat-resolution.md §6, "Applying the damage" and
        /// "Overflow". Removes from the battle every token it destroys.
        /// </summary>
        /// <param name="attacker">Token firing the beam.</param>
        /// <param name="target">Weapon target.</param>
        /// <param name="hitPower">Damage done by the weapon at this target, already through
        /// capacitors, this target's deflectors and the range falloff (CalculateWeaponPower).</param>
        /// <param name="weapon">The firing weapon slot.</param>
        private void FireBeam(Stack attacker, Stack target, double hitPower, Weapon weapon)
        {
            HashSet<Stack> alreadyHit = new HashSet<Stack>();

            while (target != null && hitPower > 0)
            {
                alreadyHit.Add(target);

                // Captured up front: DestroyStack may remove the token below.
                bool targetIsStarbase = target.IsStarbase;
                int targetDeflectorPercent = target.Token.Design.BeamDeflectorPercent;
                int targetDistance = GridDistance(attacker, target);
                double remainder;

                if (weapon.Group == WeaponType.shieldSapper)
                {
                    // A sapper only ever drains shields: it never touches armor and has no
                    // effect at all on a token with no shields.
                    if (target.Token.Shields <= 0)
                    {
                        return;
                    }

                    remainder = DamageShields(attacker, target, hitPower);
                    if (target.Token.Shields > 0)
                    {
                        return;
                    }
                }
                else
                {
                    // First we have to take down the shields of the target ship. If there is any
                    // power left over it carries forward to attack Armor. If all we have done is
                    // weaken the shields then that is the end of this shot.
                    remainder = DamageShields(attacker, target, hitPower);
                    if (target.Token.Shields > 0 || remainder <= 0)
                    {
                        return;
                    }

                    remainder = DamageArmor(attacker, target, remainder);

                    bool destroyed = target.Token.Quantity <= 0 || target.Token.Armor <= 0;
                    DestroyIfDead(attacker, target);
                    if (!destroyed)
                    {
                        return;
                    }
                }

                // Overflow: a standard beam that destroyed the whole token, or a sapper that
                // stripped its last shield point, carries the unused remainder on. It is turned
                // back into undeflected, unattenuated terms, then re-aimed at the next most
                // attractive eligible target in reach, where that target's deflectors and
                // distance apply afresh. A shot that hits a starbase never overflows.
                if (targetIsStarbase || remainder <= 0)
                {
                    return;
                }

                long undeflected = (long)remainder * 100 / Math.Max(1, targetDeflectorPercent);
                undeflected = undeflected * 100 / Math.Max(1, 100 - BeamFalloffPercent(weapon, targetDistance));

                Stack next = SelectTargetInReach(attacker, weapon, alreadyHit);
                if (next == null)
                {
                    return;
                }

                BattleStepTarget report = new BattleStepTarget();
                report.StackKey = attacker.Key;
                report.TargetKey = next.Key;
                battle.Steps.Add(report);

                hitPower = ApplyBeamDeflectorAndFalloff(undeflected, attacker, weapon, next);
                target = next;
            }
        }

        /// <summary>
        /// A gatling ("hits-all") beam slot: one shot hits every enemy token in reach whose type
        /// matches the firer's primary or its secondary target type, each hit taking the full
        /// slot damage times the capacitor percentage times that target's own deflector
        /// percentage - no range falloff, nothing carried over between targets
        /// (behavior-specs-9/combat-resolution.md §6, "Hits-all beams").
        /// </summary>
        /// <returns>True if at least one token was hit.</returns>
        private bool FireGatling(Stack attacker, Weapon weapon)
        {
            if (attacker == null || attacker.IsDestroyed || attacker.HasRetreated)
            {
                return false;
            }

            BattlePlan battlePlan = GetBattlePlan(attacker);
            long slotDamage = BeamSlotDamage(attacker, weapon);
            int reach = WeaponReach(attacker, weapon);
            bool firedAny = false;

            foreach (Stack lamb in currentBattleStacks.ToList())
            {
                if (lamb == attacker || lamb.IsDestroyed || lamb.HasRetreated || !AreEnemies(attacker, lamb))
                {
                    continue;
                }

                if (!MatchesTargetType(lamb, battlePlan.PrimaryTarget) && !MatchesTargetType(lamb, battlePlan.SecondaryTarget))
                {
                    continue;
                }

                if (GridDistance(attacker, lamb) > reach)
                {
                    continue;
                }

                BattleStepTarget report = new BattleStepTarget();
                report.StackKey = attacker.Key;
                report.TargetKey = lamb.Key;
                battle.Steps.Add(report);

                long damage = slotDamage * lamb.Token.Design.BeamDeflectorPercent / 100;

                double remainder = DamageShields(attacker, lamb, damage);
                if (lamb.Token.Shields <= 0 && remainder > 0)
                {
                    DamageArmor(attacker, lamb, remainder);
                    DestroyIfDead(attacker, lamb);
                }

                firedAny = true;
            }

            return firedAny;
        }

        /// <summary>
        /// The next target a beam's overflow re-aims at: the most attractive (for this weapon)
        /// enemy token in reach not yet hit by this shot, primary-type targets first and
        /// secondary-type ones only when no primary is in reach; ties go to the earlier token in
        /// battle order (behavior-specs-9/combat-resolution.md §4, §6).
        /// </summary>
        private Stack SelectTargetInReach(Stack attacker, Weapon weapon, HashSet<Stack> exclude)
        {
            BattlePlan battlePlan = GetBattlePlan(attacker);
            int reach = WeaponReach(attacker, weapon);

            foreach (string targetType in new[] { battlePlan.PrimaryTarget, battlePlan.SecondaryTarget })
            {
                Stack best = null;
                double bestScore = 0;

                foreach (Stack lamb in currentBattleStacks)
                {
                    if (exclude.Contains(lamb) || lamb.IsDestroyed || lamb.HasRetreated || !AreEnemies(attacker, lamb)
                        || !MatchesTargetType(lamb, targetType) || GridDistance(attacker, lamb) > reach)
                    {
                        continue;
                    }

                    double score = GetAttractiveness(attacker, lamb, weapon);
                    if (score > bestScore)
                    {
                        best = lamb;
                        bestScore = score;
                    }
                }

                if (best != null)
                {
                    return best;
                }
            }

            return null;
        }

        /// <summary>
        /// Fire a missile weapon system. Each individual missile/torpedo in the shot is resolved
        /// as an independent hit/miss check against accuracy, not one all-or-nothing roll for
        /// the whole slot - see behavior-specs-9/combat-resolution.md §6 and its Worked
        /// Example 2 (a 3-torpedo salvo landing a mixed 2-hit/1-miss result). hitPower is the
        /// shot's total damage (WeaponsInSlot * ShipsInToken * WeaponDamagePerHit, per
        /// CalculateWeaponPower); dividing by the individual missile count (weapon.Count *
        /// ShipsInToken) recovers each missile's own damage. The rolled hits and misses are then
        /// applied to the token in two aggregated calls, as the original does.
        /// </summary>
        /// <param name="attacker">Token firing the missile.</param>
        /// <param name="target">Missile weapon target.</param>
        /// <param name="hitPower">Total damage the shot can do, across every missile fired.</param>
        /// <param name="accuracy">Missile accuracy, 0-100.</param>
        private void FireMissile(Stack attacker, Stack target, double hitPower, double accuracy, Weapon weapon)
        {
            int unallotted = Math.Max(1, weapon.Count * attacker.Token.Quantity);
            long baseDamagePerMissile = (long)hitPower / unallotted;
            HashSet<Stack> alreadyAllotted = new HashSet<Stack>();
            bool firstTarget = true;

            // Allotment, exact (behavior-specs-10/combat-resolution.md §6, FUN_10f0_4326
            // :101938-102025): each time the salvo picks a target with M missiles unallotted, the
            // hits H are rolled for all M; the token takes the smallest n (from its ship count
            // up to M) whose shield chips and hits would destroy it, or all M; the remaining
            // missiles roll afresh against the next most attractive target in reach.
            while (unallotted > 0 && target != null && !target.IsDestroyed)
            {
                alreadyAllotted.Add(target);

                if (!firstTarget)
                {
                    BattleStepTarget report = new BattleStepTarget();
                    report.StackKey = attacker.Key;
                    report.TargetKey = target.Key;
                    battle.Steps.Add(report);
                }

                firstTarget = false;

                int allotted = AllotMissiles(attacker, target, weapon, unallotted, baseDamagePerMissile);
                DestroyIfDead(attacker, target);
                unallotted -= allotted;

                if (unallotted <= 0)
                {
                    break;
                }

                target = SelectTargetInReach(attacker, weapon, alreadyAllotted);
            }
        }

        /// <summary>
        /// One allotment of a missile salvo against one token (§6, "Allotment arithmetic,
        /// exact"), applied as two aggregated damage calls: first the misses' combined chip,
        /// misses x D / 8 truncated, to shields only; then the hits, hits x D / 2 (truncated)
        /// offered to the shields and whatever they do not absorb plus a second hits x D / 2 to
        /// armor - so an odd hits x D loses one point - with a kill limit of the n missiles
        /// allotted.
        /// </summary>
        /// <returns>The number of missiles allotted to this token, n.</returns>
        private int AllotMissiles(Stack attacker, Stack target, Weapon weapon, int unallotted, long baseDamagePerMissile)
        {
            int missiles = unallotted;

            // (1) The hits for all M missiles, against this target's accuracy.
            int hitChance = (int)CalculateWeaponAccuracy(attacker, weapon, target);
            int hits = RollMissileHits(missiles, hitChance);

            // (2) Per-missile damage, doubled for a capital missile when this target's shield
            // pool is already zero at this moment (decided before any missile resolves - shields
            // knocked out during the allotment do not double it).
            long damage = baseDamagePerMissile;
            if (weapon.Group == WeaponType.missile && target.Token.Shields <= 0)
            {
                damage *= 2;
            }

            // (3) Whole-token shield pool and remaining armor.
            long shields = Math.Max(0L, (long)Math.Floor(target.Token.Shields));
            long armor = Math.Max(0L, (long)Math.Floor(target.Token.Armor));
            int ships = target.Token.Quantity;

            // (4) / (5) The n-search.
            int allotted = missiles;
            int allottedHits = hits;
            if (!(ships >= missiles || (long)hits * damage <= armor))
            {
                for (int n = ships; n <= missiles; n++)
                {
                    int nHits = (int)(((long)hits * n + missiles - 1) / missiles);
                    int nMisses = n - nHits;
                    long shieldsLeft = Math.Max(0L, shields - (nMisses * damage / 8));
                    long half = nHits * damage / 2;
                    long armorEstimate = half + Math.Max(0L, half - shieldsLeft);
                    if (armorEstimate >= armor)
                    {
                        allotted = n;
                        allottedHits = nHits;
                        break;
                    }
                }
            }

            int allottedMisses = allotted - allottedHits;

            // (6) Misses first, then the hits.
            long chip = allottedMisses * damage / 8;
            if (chip > 0 && target.Token.Shields > 0)
            {
                DamageShields(attacker, target, chip);
            }

            if (allottedHits > 0)
            {
                long half = allottedHits * damage / 2;
                double armorDamage = DamageShields(attacker, target, half) + half;
                if (armorDamage > 0)
                {
                    DamageArmor(attacker, target, armorDamage, allotted);
                }
            }

            return allotted;
        }

        /// <summary>
        /// The salvo's hit count (§6 "Per-shot resolution"): at 100% or more every missile hits
        /// with no draw; for 1-200 missiles each draws 0-99 and hits when the draw is below the
        /// accuracy; at 201 or more the hits are accuracy x missiles / 100, truncated.
        /// </summary>
        private int RollMissileHits(int missiles, int hitChance)
        {
            if (hitChance >= 100)
            {
                return missiles;
            }

            if (missiles > 200)
            {
                return (int)((long)hitChance * missiles / 100);
            }

            int hits = 0;
            for (int i = 0; i < missiles; i++)
            {
                if (Rng.Next(0, 100) < hitChance)
                {
                    hits++;
                }
            }

            return hits;
        }

        /// <summary>
        /// Attack the shields.
        /// </summary>
        /// <param name="attacker">Token firing a weapon.</param>
        /// <param name="target">Ship being fired on.</param>
        /// <param name="hitPower">Damage output of the weapon.</param>
        /// <returns>Residual damage after shields or zero.</returns>
        private double DamageShields(Stack attacker, Stack target, double hitPower)
        {
            if (hitPower > 0)
            {
                target.HasTakenDamage = true;
            }

            if (target.Token.Shields <= 0)
            {
                return hitPower;
            }

            double initialShields = target.Token.Shields;
            target.Token.Shields -= hitPower;

            if (target.Token.Shields < 0)
            {
                target.Token.Shields = 0;
            }

            // Calculate remianing weapon power, after damaging shields (if any)
            double remainingPower = Math.Max(0, hitPower - initialShields);

            // "If the damage is less than the pool, the remaining pool is divided evenly among
            // the ships, rounding down" - behavior-specs-9/combat-resolution.md §6. Shields are
            // stored per ship in the original and pooled over the token's ships here.
            if (target.Token.Shields > 0 && target.Token.Quantity > 0)
            {
                target.Token.Shields = Math.Floor(target.Token.Shields / target.Token.Quantity) * target.Token.Quantity;
            }

            double damageDone = initialShields - target.Token.Shields;

            BattleStepWeapons battleStepReport = new BattleStepWeapons();
            battleStepReport.Damage = damageDone;
            battleStepReport.Targeting = BattleStepWeapons.TokenDefence.Shields;
            battleStepReport.WeaponTarget.StackKey = attacker.Key; 
            battleStepReport.WeaponTarget.TargetKey = target.Key;

            battle.Steps.Add(battleStepReport);

            return remainingPower;
        }

        /// <summary>
        /// Attack the Armor.
        /// </summary>
        /// <param name="attacker">Token making the attack.</param>
        /// <param name="target">Target being fired on.</param>
        /// <param name="hitPower">Weapon damage.</param>
        /// <param name="killLimit">Most ships this damage may destroy - the missile salvo's
        /// "one missile, one kill" limit (behavior-specs-9/combat-resolution.md §6); beams have
        /// none. Damage beyond the limit is discarded, not spread over the survivors.</param>
        /// <returns>The damage left over after destroying the whole token (0 if any ship
        /// survives) - what a beam may overflow onto its next target.</returns>
        private double DamageArmor(Stack attacker, Stack target, double hitPower, int killLimit = int.MaxValue)
        {
            // The exact damage-word routine (behavior-specs-10/combat-resolution.md §8,
            // FUN_10f0_52c4): unpack the token's word (a percentage of its ships each carrying
            // the same damage, in 1/500 of the design's armor), kill the damaged ships first
            // (each needing only its remaining armor), then undamaged ones at full armor, within
            // the kill limit (damage beyond it is discarded), and repack the survivors - any
            // leftover damage is pooled with what the damaged survivors carry, shared over all of
            // them and rounded up to the next 1/500. See Nova.Common.Combat.DamageWord.
            long incoming = Math.Max(0L, (long)Math.Floor(hitPower));
            int armorPerShip = target.Token.Design.Armor;
            DamageWord word = DamageWord.For(target.Token);
            DamageWord.DamageResult outcome = word.Apply(incoming, target.Token.Quantity, armorPerShip, killLimit);

            double leftover = 0;
            int shipsDestroyed = outcome.ShipsKilled;
            if (shipsDestroyed > 0)
            {
                // Salvage is worked out before the ships leave the token, so their cargo share is
                // still measured against the whole fleet (§7).
                RecordWreck(target, shipsDestroyed);
                target.Token.Quantity -= shipsDestroyed;
                battle.Losses[target.Owner] = battle.Losses[target.Owner] + shipsDestroyed;
                RecordDestroyedTech(target);
            }

            if (target.Token.Quantity <= 0)
            {
                target.Token.Armor = 0;
                leftover = outcome.Leftover;
            }
            else
            {
                DamageWord.Store(target.Token, outcome.Word);
            }

            double appliedDamage = incoming - outcome.Discarded - (long)leftover;

            BattleStepWeapons battleStepReport = new BattleStepWeapons();
            battleStepReport.Damage = appliedDamage;
            battleStepReport.Targeting = BattleStepWeapons.TokenDefence.Armor;
            battleStepReport.WeaponTarget.StackKey = attacker.Key;
            battleStepReport.WeaponTarget.TargetKey = target.Key;
            battle.Steps.Add(battleStepReport);

            return leftover;
        }

        /// <summary>
        /// Calculate weapon power. For a standard or sapper beam this is the exact order of
        /// operations of behavior-specs-9/combat-resolution.md §6 ("Beam shot, exact order of
        /// operations"): weapons in slot x ships in token x damage per weapon, then x the firer's
        /// capacitor percentage, then x the target's deflector percentage, then x (100 - falloff)
        /// / 100, every step truncating. Missile/torpedo power is the unmodified base power -
        /// their hit/miss and shield/armor split is handled separately in FireMissile.
        /// </summary>
        /// <param name="attacker">Firing stack.</param>
        /// <param name="weapon">Firing weapon.</param>
        /// <param name="target">Stack being fired on.</param>
        /// <returns>Damage weapon is able to do.</returns>
        private double CalculateWeaponPower(Stack attacker, Weapon weapon, Stack target)
        {
            if (weapon.IsMissile)
            {
                // ShotDamage = WeaponsInSlot * ShipsInToken * WeaponDamagePerHit. weapon.Power
                // already folds in WeaponsInSlot (identical components in one hull slot are
                // summed when the ship design is built).
                return (double)weapon.Power * attacker.Token.Quantity;
            }

            return ApplyBeamDeflectorAndFalloff(BeamSlotDamage(attacker, weapon), attacker, weapon, target);
        }

        /// <summary>
        /// A beam slot's damage before anything target-specific: weapons in slot x ships in
        /// token x damage per weapon, x the firer's capacitor percentage (truncating).
        /// </summary>
        private static long BeamSlotDamage(Stack attacker, Weapon weapon)
        {
            long damage = (long)weapon.Power * attacker.Token.Quantity;
            return damage * attacker.Token.Design.CapacitorPercent / 100;
        }

        /// <summary>
        /// Applies the target's deflector percentage, then the range falloff, each step
        /// truncating (behavior-specs-9/combat-resolution.md §6).
        /// </summary>
        private static long ApplyBeamDeflectorAndFalloff(long damage, Stack attacker, Weapon weapon, Stack target)
        {
            damage = damage * target.Token.Design.BeamDeflectorPercent / 100;
            int falloff = BeamFalloffPercent(weapon, GridDistance(attacker, target));
            return damage * (100 - falloff) / 100;
        }

        /// <summary>
        /// Beam range falloff percentage: the whole-number part of 10 x distance / the weapon's
        /// STORED range - so a range-3 beam loses 0/3/6/10% at distances 0-3. A starbase's bonus
        /// square of reach is not in the divisor (its range-1 beam at distance 2 loses 20%), and a
        /// range-0 beam never loses anything (behavior-specs-9/combat-resolution.md §6).
        /// </summary>
        private static int BeamFalloffPercent(Weapon weapon, int distance)
        {
            if (weapon.Range <= 0)
            {
                return 0;
            }

            return Math.Min(100, (10 * distance) / weapon.Range);
        }

        /// <summary>
        /// How far a weapon slot reaches: its stored range, plus one square when the firer is a
        /// starbase (behavior-specs-9/combat-resolution.md §3, §6).
        /// </summary>
        private static int WeaponReach(Stack attacker, Weapon weapon)
        {
            return weapon.Range + (attacker.IsStarbase ? 1 : 0);
        }

        /// <summary>
        /// Battle-board distance between two stacks: the larger of the column and row
        /// differences (Chebyshev distance), used for both weapon reach and beam falloff -
        /// behavior-specs-9/combat-resolution.md §6.
        /// </summary>
        private static int GridDistance(Stack from, Stack to)
        {
            return Math.Max(Math.Abs(from.Position.X - to.Position.X), Math.Abs(from.Position.Y - to.Position.Y));
        }

        /// <summary>
        /// Calculate weapon accuracy. Beam weapons always hit (accuracy is only meaningful for
        /// missiles/torpedoes). For missiles, the chance to hit is based on the base accuracy,
        /// the firing ship's computers, and the target's jammers.
        /// </summary>
        /// <remarks>
        /// The confirmed formula of behavior-specs-9/combat-resolution.md §6 (evidence
        /// FUN_10f0_41ca, cross-checked against all three published figures): the target's jam
        /// percentage and the firer's computer bonus first cancel subtractively. If jam is left
        /// over (residual &gt;= 0), accuracy = base x (100 - residual) / 100; if computer bonus
        /// is left over, accuracy = 100 - (100 - leftover) x (100 - base) / 100. Integer
        /// arithmetic, clamped to 1..100. Base 20 with a Battle Super Computer (30) gives 44,
        /// against a Jammer 20 alone 16, and both together 28.
        /// </remarks>
        /// <param name="attacker">Attacking stack.</param>
        /// <param name="weapon">Firing weapon.</param>
        /// <param name="target">Stack being fired on.</param>
        /// <returns>Chance that weapon will hit, 1-100.</returns>
        private double CalculateWeaponAccuracy(Stack attacker, Weapon weapon, Stack target)
        {
            if (!weapon.IsMissile)
            {
                return weapon.Accuracy;
            }

            // Both stored as whole-number percentages (bytes) in the original. The design
            // aggregates are doubles built with Math.Pow, so e.g. one Jammer 20 sums to
            // 19.999999999999996 - truncate only after absorbing that floating-point noise.
            int baseAccuracy = weapon.Accuracy;
            int jam = (int)Math.Floor(target.Token.Design.Jammer + 1e-6);
            int computer = (int)Math.Floor(attacker.Token.Design.ComputerAccuracy + 1e-6);
            int residual = jam - computer;

            int accuracy;
            if (residual >= 0)
            {
                accuracy = baseAccuracy * (100 - residual) / 100;
            }
            else
            {
                int leftover = -residual;
                accuracy = 100 - ((100 - leftover) * (100 - baseAccuracy) / 100);
            }

            return Math.Max(1, Math.Min(100, accuracy));
        }

        /// <summary>
        /// Records that a design belonging to <paramref name="target"/>'s owner was just
        /// destroyed, tracking the highest required tech level seen per field across every
        /// destroyed design this battle - the "source" profile GrantBattleTechGains later rolls
        /// surviving races against. Reuses TechTrading.HighestRequiredTech (already used the same
        /// way for scrapping/invasion) since a Stack is itself a Fleet with the destroyed token
        /// still present in its Composition at the point both call sites invoke this.
        /// </summary>
        private void RecordDestroyedTech(Stack target)
        {
            TechLevel destroyedTech = TechTrading.HighestRequiredTech(target);

            if (!destroyedTechByOwner.TryGetValue(target.Owner, out TechLevel highest))
            {
                destroyedTechByOwner[target.Owner] = destroyedTech;
                return;
            }

            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (destroyedTech[field] > highest[field])
                {
                    highest[field] = destroyedTech[field];
                }
            }
        }

        /// <summary>
        /// Gives every race with at least one ship that survived this battle (present and
        /// un-destroyed, or successfully retreated) a chance to gain a tech level from the
        /// pooled highest tech level of every OTHER race's ships destroyed here - "any race that
        /// had at least one ship survive the battle... becomes eligible for a chance to gain
        /// partial tech levels based on the enemy tech present in ships destroyed during the
        /// fight" (docs/behavior-specs-5/combat-resolution.md §7 Aftermath). This is a distinct
        /// subsystem from combat resolution itself (§9's own framing) reusing the tech-trading
        /// mechanic already wired into ScrapTask/InvadeTask (TechTrading.AttemptTechGain) - the
        /// original's exact 13-way/6-way tech-bonus category tables were never traced with
        /// enough confidence to reproduce (see the doc's own admission in §9), so this grants
        /// against Nova's existing 6 research fields instead of attempting to fabricate those
        /// categories.
        /// </summary>
        private void GrantBattleTechGains(List<Stack> battlingStacks)
        {
            if (destroyedTechByOwner.Count == 0)
            {
                return; // nobody died - nothing to learn from.
            }

            HashSet<int> survivingRaces = new HashSet<int>(
                battlingStacks.Where(stack => !stack.IsDestroyed || stack.HasRetreated).Select(stack => (int)stack.Owner));

            foreach (int race in survivingRaces)
            {
                TechLevel pooledEnemyTech = new TechLevel(0);
                bool anyEnemyTech = false;

                foreach (KeyValuePair<int, TechLevel> entry in destroyedTechByOwner)
                {
                    if (entry.Key == race)
                    {
                        continue;
                    }

                    anyEnemyTech = true;
                    foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
                    {
                        if (entry.Value[field] > pooledEnemyTech[field])
                        {
                            pooledEnemyTech[field] = entry.Value[field];
                        }
                    }
                }

                if (!anyEnemyTech)
                {
                    continue;
                }

                EmpireData empire = serverState.AllEmpires[race];
                TechLevel.ResearchField? learned = TechTrading.AttemptTechGain(empire, pooledEnemyTech);
                if (learned != null)
                {
                    Message message = new Message();
                    message.Audience = race;
                    message.Text = "Studying enemy wreckage from the battle at " + battle.Location
                        + " has taught your scientists Tech Level " + empire.ResearchLevels[learned.Value]
                        + " in the " + learned.Value + " field.";
                    message.Type = "Battle";
                    serverState.AllMessages.Add(message);
                }
            }
        }

        /// <summary>
        /// Gives every race with at least one ship that survived this battle a chance to be
        /// awarded one of the 12 special components behavior-specs-7/ship-design-and-components.md
        /// §14a confirms are gated by a one-time, per-race, per-component random grant - a
        /// separate mechanism from <see cref="GrantBattleTechGains"/>'s enemy-wreckage tech study
        /// above, and from the original game's own PRT/LRT component-availability gating.
        ///
        /// The spec traces the real mechanism as: on a successful roll (roughly 50/50), pick one
        /// of 13 candidate slots; 12 of them name a specific component (see
        /// <see cref="SpecialComponentGrants"/>), and if that component hasn't already been
        /// granted to this race, a second weighted roll decides whether it's actually awarded now.
        /// This is a disclosed simplification of that chain: the "13th slot" (a bare tech-level
        /// bump with no component, per the spec's own hedged "most plausibly" framing) and the
        /// second roll's exact weighting are both left unimplemented, since neither is concretely
        /// quantified by the spec - here, a single roughly-50% roll picks uniformly at random from
        /// whichever of the 12 named components this race hasn't already been granted, so the
        /// same "never grant twice, roughly even odds per qualifying battle" shape holds without
        /// fabricating specific unconfirmed numbers.
        /// </summary>
        public void GrantOneTimeSpecialComponent(List<Stack> battlingStacks)
        {
            HashSet<int> survivingRaces = new HashSet<int>(
                battlingStacks.Where(stack => !stack.IsDestroyed || stack.HasRetreated).Select(stack => (int)stack.Owner));

            foreach (int race in survivingRaces)
            {
                if (Rng.Next(100) <= 49)
                {
                    continue; // roughly even odds of no reward at all this battle.
                }

                // Salvage never feeds bits 8 (Mini Morph), 10 (Genesis Device) or 12: only the
                // Mystery Trader reaches them (behavior-specs-10/turn-generation-engine.md §5,
                // ship-design-and-components.md §14a), so the draw is over the other ten.
                EmpireData empire = serverState.AllEmpires[race];
                List<string> stillUngranted = SpecialComponentGrants.SalvageableComponents
                    .Where(name => !empire.GrantedSpecialComponents.Contains(name))
                    .ToList();

                if (stillUngranted.Count == 0)
                {
                    continue; // this race has already been awarded all ten salvageable parts.
                }

                string granted = stillUngranted[Rng.Next(stillUngranted.Count)];
                empire.GrantedSpecialComponents.Add(granted);

                // Tech level may already exceed this component's requirement (it just wasn't
                // available to build until now) - if so, make it buildable immediately rather
                // than waiting for a future tech-level-up that may never come. StarUpdateStep.
                // TechLevelUp handles the more common case of the grant preceding the tech level.
                Component component = new AllComponents().Fetch(granted);
                if (component != null && empire.ResearchLevels >= component.RequiredTech)
                {
                    empire.AvailableComponents.Add(component);
                }

                Message message = new Message
                {
                    Audience = race,
                    Text = "Victory at " + battle.Location + " has earned your race the plans for a " + granted + "!",
                    Type = "Battle",
                };
                serverState.AllMessages.Add(message);
            }
        }

        /// <summary>
        /// Report the battle and losses to each player.
        /// </summary>
        private void ReportBattle()
        {
            foreach (int empire in battle.Losses.Keys)
            {
                Message message = new Message(
                    empire,
                    "There was a battle at " + battle.Location + "\r\n",
                    "BattleReport",
                    battle);

                if (battle.Losses[empire] == 0)
                {
                    message.Text += "None of your ships were destroyed";
                }
                else
                {
                    message.Text += battle.Losses[empire].ToString(System.Globalization.CultureInfo.InvariantCulture) +
                       " of your ships were destroyed";
                }

                serverState.AllMessages.Add(message);
                
                serverState.AllEmpires[empire].BattleReports.Add(battle);
            }
        }
    }
}

