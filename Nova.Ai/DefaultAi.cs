#region Copyright Notice
// ============================================================================
// Copyright (C) 2009 - 2017 stars-nova
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

namespace Nova.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    public class DefaultAi : AbstractAI
    {
        /// <summary>
        /// Ports ai-opponent-behavior.md section 1 ("Personality dispatch"): a per-player 0-7
        /// code selecting which personality drives this empire's turn. The spec's own Open
        /// Questions admit the true mapping from these numeric codes to any named/selectable
        /// difficulty option was never recovered from the decompile - the specific code values
        /// chosen for <see cref="DisabledPersonality"/>/<see cref="PassivePersonality"/> below,
        /// and the linear tuning-scalar spread across the Standard range, are this rebuild's own
        /// reasonable, explicitly-not-spec-verified assignment, not a confirmed original mapping.
        /// Selected via an optional `-n &lt;0-7&gt;` CLI argument
        /// (CommandArguments.Option.AiPersonality); omitted entirely on an ordinary AI invocation,
        /// which resolves to <see cref="StandardPersonality"/> - so every behavior built in this
        /// rebuild before this mechanic keeps working exactly as before by default.
        /// </summary>
        public const int DisabledPersonality = 0;

        /// <summary>The spec's "near-total no-op" slot - only the shared passes run (production/
        /// research allocation, and mineral-cargo transport, which mechanic 5's
        /// FreighterRoutingSelector already treats as covering starbase mineral-rebalancing too -
        /// see PROJECT-STATUS.md's mechanic-7-of-8 entry); no independent scouting, colonization,
        /// or defense decisions.</summary>
        public const int PassivePersonality = 1;

        public const int MinStandardPersonality = 2;
        public const int MaxStandardPersonality = 7;
        public const int StandardPersonality = 4;

        private Intel turnData;
        private FleetList fuelStations = null;
        private DefaultAIPlanner aiPlan = null;
        private readonly Random random = new Random();
        private int personality = StandardPersonality;

        // Sub AIs to manage planets, fleets and stuff
        private Dictionary<string, DefaultPlanetAI> planetAIs = new Dictionary<string, DefaultPlanetAI>();
        private Dictionary<long, DefaultFleetAI> fleetAIs = new Dictionary<long, DefaultFleetAI>();

        /// <summary>
        /// This empire's owned fleets, reshuffled once per turn (see DoMove) - ports
        /// ai-opponent-behavior.md section 8: the order an AI's fleets receive their per-turn
        /// decisions in is randomized (Fisher-Yates) each turn rather than following stable
        /// list/dictionary order, most visibly mattering when two fleets could otherwise compete
        /// for the same target (e.g. two idle colonizers both eligible for the same planet -
        /// whichever is decided first claims it). HandleScouting/HandleColonizing iterate this
        /// same shuffled list rather than re-querying OwnedFleets directly, so the randomized
        /// order is actually the one those decisions see.
        /// </summary>
        private List<Fleet> shuffledFleets = new List<Fleet>();

        /// <summary>A per-personality tuning value for the Standard range (2-7), spread linearly
        /// across it (1 at <see cref="MinStandardPersonality"/>, up to 6 at
        /// <see cref="MaxStandardPersonality"/>) - the spec describes personalities as differing
        /// only by "tuning constants (probabilities, distance thresholds, aggressiveness
        /// multipliers)", not decision-tree shape. Threaded through so far into
        /// ThreatAssessment.NeedsDefensiveMinelaying's <c>raceTraitValue</c> (see HandleDefense) -
        /// the one place in this rebuild that already had an explicit "unnamed race trait"
        /// placeholder crying out for a real per-player driver. Every other mechanic's own
        /// already-approximated constants are left as-is rather than retrofitting all of them
        /// with personality scaling too, which would be a disproportionate mechanical refactor
        /// for constants that were never spec-verified numbers to begin with.</summary>
        private int PersonalityTraitValue
        {
            get { return TraitValueForPersonality(personality); }
        }

        /// <summary>Pure mapping from a personality code to its tuning-trait value - pulled out
        /// as its own static method (rather than only an instance property) so it's directly
        /// unit-testable without needing a fully-initialized AI/ClientData fixture.</summary>
        public static int TraitValueForPersonality(int personality)
        {
            int clamped = Math.Max(MinStandardPersonality, Math.Min(MaxStandardPersonality, personality));
            return 1 + (clamped - MinStandardPersonality);
        }

        /// <summary>
        /// This is the entry point to the AI proper.
        /// Currently this does not use anything recognized by Computer Science as AI,
        /// just functional programming to complete a list of tasks.
        /// </summary>
        public override void DoMove()
        {
            if (commandArguments != null && commandArguments.Contains(CommandArguments.Option.AiPersonality))
            {
                personality = int.Parse(commandArguments[CommandArguments.Option.AiPersonality], System.Globalization.CultureInfo.InvariantCulture);
            }

            if (personality == DisabledPersonality)
            {
                // "Another slot is entirely disabled: its driver function does nothing at all."
                return;
            }

            aiPlan = new DefaultAIPlanner(clientState);

            // create the helper AIs
            foreach (Star star in clientState.EmpireState.OwnedStars.Values)
            {
                if (star.Owner == clientState.EmpireState.Id)
                {
                    DefaultPlanetAI planetAI = new DefaultPlanetAI(star, clientState, this.aiPlan, random);
                    planetAIs.Add(star.Key, planetAI);
                }
            }

            shuffledFleets = clientState.EmpireState.OwnedFleets.Values
                .Where(fleet => fleet.Owner == clientState.EmpireState.Id)
                .ToList();
            Shuffle(shuffledFleets);

            foreach (Fleet fleet in shuffledFleets)
            {
                aiPlan.CountFleet(fleet);
                DefaultFleetAI fleetAI = new DefaultFleetAI(fleet, clientState, fuelStations);
                fleetAIs.Add(fleet.Id, fleetAI);

                // reset all waypoint orders
                for (int wpIndex = 1; wpIndex < fleet.Waypoints.Count; wpIndex++)
                {
                    WaypointCommand command = new WaypointCommand(CommandMode.Delete, fleet.Key, wpIndex);
                    command.ApplyToState(clientState.EmpireState);
                    clientState.Commands.Push(command);
                }
            }

            turnData = clientState.InputTurn;

            // The two "shared passes" (spec step 5) run for every active personality, Passive
            // included: production/research allocation, and mineral-cargo transport (which
            // mechanic 5's FreighterRoutingSelector already treats as covering starbase mineral-
            // rebalancing too).
            HandleProduction();
            HandleResearch();
            HandleTransports();

            if (personality == PassivePersonality)
            {
                // "One personality slot is a near-total no-op... no independent fleet/planet
                // decisions" - skip scouting, colonization, and defense/minelaying.
                return;
            }

            HandleScouting();
            HandleColonizing();
            HandleDefense();
        }

        /// <summary>
        /// Fisher-Yates shuffle in place, using this AI's own turn-scoped Random - the game's
        /// server-side turn generation has its own optionally-seeded Random (see GameSettings.Seed/
        /// StarMapGenerator), but the AI runs as an entirely separate process (Nova.exe --ai) with
        /// no access to that seed across the process boundary, so this is necessarily its own
        /// independent source of randomness rather than a literally shared one.
        /// </summary>
        private void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// Setup the production queue for the AI.
        /// </summary>
        private void HandleProduction()
        {
            foreach (DefaultPlanetAI ai in planetAIs.Values)
            {
                ai.HandleProduction();
            }
        }

        /// <summary>
        /// Ports ai-opponent-behavior.md section 4 ("Threat assessment and defense/minefield
        /// decisions"): rates the threat near each owned planet and, where the defense-need
        /// evaluator recommends it, orders an idle mine-laying fleet already there to lay mines -
        /// see ThreatAssessment for the rating/evaluator logic and DefaultFleetAI.LayMines for why
        /// this order doesn't yet visibly do anything once issued (a pre-existing, unrelated
        /// engine gap, not something this AI-behavior port introduces or should fix).
        /// </summary>
        private void HandleDefense()
        {
            const double ThreatScanRadius = 200;

            int ownedPlanetCount = clientState.EmpireState.OwnedStars.Values
                .Count(star => star.Owner == clientState.EmpireState.Id);

            int existingMinefieldUnits = turnData.AllMinefields.Values
                .Where(field => field.Owner == clientState.EmpireState.Id)
                .Sum(field => field.NumberOfMines);

            foreach (Star planet in clientState.EmpireState.OwnedStars.Values)
            {
                if (planet.Owner != clientState.EmpireState.Id)
                {
                    continue;
                }

                int threatRating = ThreatAssessment.AggregateThreatRating(
                    planet.Position,
                    clientState.EmpireState.FleetReports.Values,
                    clientState.EmpireState.Id,
                    ThreatScanRadius,
                    random);

                if (!ThreatAssessment.NeedsDefensiveMinelaying(threatRating, ownedPlanetCount, existingMinefieldUnits, random, PersonalityTraitValue))
                {
                    continue;
                }

                Fleet mineLayer = shuffledFleets.FirstOrDefault(fleet =>
                    fleet.NumberOfMines > 0 && PointUtilities.IsNear(planet.Position, fleet.Position));

                if (mineLayer != null)
                {
                    fleetAIs[mineLayer.Id].LayMines();
                }
            }
        }

        private void HandleScouting()
        {
            List<Fleet> scoutFleets = new List<Fleet>();
            foreach (Fleet fleet in shuffledFleets)
            {
                if (fleet.Name.Contains("Scout") == true)
                {
                    scoutFleets.Add(fleet);
                }
            }

            // Find the stars we do not need to scout (eg home world)
            List<StarIntel> excludedStars = new List<StarIntel>();
            foreach (StarIntel report in turnData.EmpireState.StarReports.Values)
            {
                if (report.Year != Global.Unset)
                {
                    excludedStars.Add(report);
                }
            }

            if (scoutFleets.Count > 0)
            {
                foreach (Fleet fleet in scoutFleets)
                {
                    StarIntel starToScout = fleetAIs[fleet.Id].Scout(excludedStars);
                    if (starToScout != null)
                    {
                        excludedStars.Add(starToScout);
                    }
                }
            }
        }
        

        private void HandleColonizing()
        {
            List<Fleet> colonyShipsFleets = new List<Fleet>();
            foreach (Fleet fleet in shuffledFleets)
            {
                if (fleet.CanColonize == true && fleet.Waypoints.Count == 1)
                {
                    colonyShipsFleets.Add(fleet);
                }
            }

            if (colonyShipsFleets.Count == 0)
            {
                return;
            }

            // Distance-band scoring, probabilistic acceptance, "too eager" jitter, late-game
            // distance conservatism, and a reservoir-sampled exploratory fallback - ports
            // ai-opponent-behavior.md section 2 (see ColonizationTargetSelector's own comments
            // for exactly which figures are spec-stated versus a reasonable reconstruction).
            var targetSelector = new ColonizationTargetSelector(clientState, random);

            // Not explicitly covered by section 2 itself (that's section 5's concern for cargo
            // routing), but without this, two colonizers processed in the same turn could
            // independently select and both head for the same still-technically-unowned target,
            // since neither one's pick updates the other's view of StarReports mid-loop.
            var claimedTargets = new HashSet<string>();

            foreach (Fleet colonyFleet in colonyShipsFleets)
            {
                StarIntel target = targetSelector.SelectTarget(colonyFleet, turnNumber);
                if (target == null || !claimedTargets.Add(target.Name))
                {
                    continue;
                }

                // Section 9's two concrete, unambiguous commitment gates - distinct from section
                // 2's target-PICKING above, this decides whether the already-picked fleet/target
                // pair is actually worth committing to. See ColonizerCommitmentAdvisor's own
                // comment for what's deliberately not ported here (the tech-upgrade-ambition
                // ladder and the audit-trail bitmask).
                if (!ColonizerCommitmentAdvisor.IsColonizerCapable(colonyFleet))
                {
                    // Too little cargo capacity to ever found a viable colony - leave the target
                    // unclaimed for a more capable fleet, rather than wasting this one's order.
                    claimedTargets.Remove(target.Name);
                    continue;
                }

                if (!ColonizerCommitmentAdvisor.IsWithinFundingRange(target.Position, colonyFleet.Position))
                {
                    claimedTargets.Remove(target.Name);
                    continue;
                }

                fleetAIs[colonyFleet.Id].Colonise(target);
            }
        }

        /// <summary>
        /// Assigns every idle transport-capable fleet a mineral-delivery run - ports
        /// ai-opponent-behavior.md section 5 ("Automated mineral/cargo transport"). Previously
        /// transports only ever got *built* (see DefaultPlanetAI.BuildTransport) but were never
        /// actually given anywhere to go - a functional gap, not just a fidelity one, since a
        /// transport with no orders just sits at its home planet forever.
        /// </summary>
        private void HandleTransports()
        {
            List<Fleet> idleTransportFleets = shuffledFleets
                .Where(fleet => !fleet.CanColonize && fleet.TotalCargoCapacity > 0 && fleet.Waypoints.Count == 1)
                .ToList();

            if (idleTransportFleets.Count == 0)
            {
                return;
            }

            var routingSelector = new FreighterRoutingSelector(clientState, random);

            // Same reasoning as HandleColonizing's claimedTargets: without this, two transports
            // processed in the same turn could both pick the identical shortfall planet, since
            // neither one's pick updates the other's view of ResourcesOnHand mid-loop - this one
            // *is* explicitly called out by the spec itself ("avoids double-assigning a delivery
            // target another friendly fleet is already servicing that turn").
            var claimedTargets = new HashSet<string>();

            foreach (Fleet transportFleet in idleTransportFleets)
            {
                FreighterRun run = routingSelector.SelectRun(transportFleet, claimedTargets);
                if (run == null || !claimedTargets.Add(run.Target.Name))
                {
                    continue;
                }

                fleetAIs[transportFleet.Id].DeliverCargo(run);
            }
        }


        /// <Summary>
        /// Manage research.
        /// Only changes research field after completing the previous research level.
        /// </Summary>
        private void HandleResearch()
        {
            // Generate a research command to describe the changes.
            ResearchCommand command = new ResearchCommand();
            command.Topics.Zero();
            // Set the percentage of production to dedicate to research
            command.Budget = 0;

            // check if messages contains info about tech advence. Could be more than one, so use a flag to prevent setting the research level multiple times.
            bool hasAdvanced = false;
            foreach (Message msg in clientState.Messages)
            {
                if (!string.IsNullOrEmpty(msg.Type) && msg.Type == "TechAdvance")
////                if (!string.IsNullOrEmpty(msg.Type) && msg.Text.Contains("Your race has advanced to Tech Level") == true)  // can be removed if the previous line works
                {
                    hasAdvanced = true;
                }
            }

            if (hasAdvanced)
            {
                // pick next topic
                int minLevel = int.MaxValue;
                Nova.Common.TechLevel.ResearchField targetResearchField = TechLevel.ResearchField.Weapons; // default to researching weapons

                if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Propulsion] < 3)
                {
                    // Prop 3 - Long Hump 6 - Warp 6 engine (or fuel mizer at Prop 2)
                    targetResearchField = TechLevel.ResearchField.Propulsion;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Electronics] < 1)
                {
                    // Elec 1 - Rhino Scanner - 50 ly scan
                    targetResearchField = TechLevel.ResearchField.Electronics;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Construction] < 3)
                {
                    // Cons 3 - Destroyer & Medium Freighter
                    targetResearchField = TechLevel.ResearchField.Construction;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Electronics] < 5)
                {
                    // Elec 5 - Scanners
                    targetResearchField = TechLevel.ResearchField.Electronics;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Weapons] < 6)
                {
                    // Wep 6 - Beta Torp (@5) and Yakimora Light Phaser
                    targetResearchField = TechLevel.ResearchField.Weapons;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Propulsion] < 7)
                {
                    // Prop 7 - Warp 8 engine
                    targetResearchField = TechLevel.ResearchField.Propulsion;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Construction] < 6)
                {
                    // Cons 6 - Frigate
                    targetResearchField = TechLevel.ResearchField.Construction;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Biotechnology] < 4)
                {
                    // Bio 4 - Unlock terraform and prep for mines
                    targetResearchField = TechLevel.ResearchField.Biotechnology;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Energy] < 3)
                {
                    // Energy 3 - Mines and shields
                    targetResearchField = TechLevel.ResearchField.Energy;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Construction] < 9)
                {
                    // Cons 9 - Cruiser
                    targetResearchField = TechLevel.ResearchField.Construction;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Energy] < 6)
                {
                    // Energy 6 - Shields
                    targetResearchField = TechLevel.ResearchField.Energy;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Weapons] < 12)
                {
                    // Weapons 12 - Jihad Missile
                    targetResearchField = TechLevel.ResearchField.Weapons;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Construction] < 13)
                {
                    // Cons 13 - Battleships
                    targetResearchField = TechLevel.ResearchField.Construction;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Energy] < 11)
                {
                    // Energy 11 - Bear Neutrino at 10, and unlocks Syncro Sapper (need weapons 21)
                    targetResearchField = TechLevel.ResearchField.Energy;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Electronics] < 11)
                {
                    // Elect 11 - Jammer 20 and Super Computer
                    targetResearchField = TechLevel.ResearchField.Electronics;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Propulsion] < 12)
                {
                    // Prop 12 - Warp 10 and Overthruster
                    targetResearchField = TechLevel.ResearchField.Propulsion;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Biotechnology] < 7)
                {
                    // Bio 7 maybe - scanners, Anti-matter generator, smart bombs
                    targetResearchField = TechLevel.ResearchField.Biotechnology;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Weapons] < 24)
                {
                    // Weapons 24 - research all remaining weapons technologies
                    targetResearchField = TechLevel.ResearchField.Weapons;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Construction] < 26)
                {
                    // Cons 26 - Nubian
                    targetResearchField = TechLevel.ResearchField.Construction;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Electronics] < 19)
                {
                    // Elect 19 - Battle nexus
                    targetResearchField = TechLevel.ResearchField.Electronics;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Energy] < 22)
                {
                    // Energy 22 - Complete Phase Shield
                    targetResearchField = TechLevel.ResearchField.Energy;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Propulsion] < 23)
                {
                    // Prop 23 - Trans-Star 10
                    targetResearchField = TechLevel.ResearchField.Propulsion;
                }
                else if (clientState.EmpireState.ResearchLevels[TechLevel.ResearchField.Biotechnology] < 10)
                {
                    // Bio 10 - RNA Scanner
                    targetResearchField = TechLevel.ResearchField.Biotechnology;
                }
                else
                {
                    // research lowest tech field
                    for (TechLevel.ResearchField field = TechLevel.FirstField; field <= TechLevel.LastField; field++)
                    {
                        if (clientState.EmpireState.ResearchLevels[field] < minLevel)
                        {
                            minLevel = clientState.EmpireState.ResearchLevels[field];
                            targetResearchField = field;
                        }
                    }
                }
                command.Topics[targetResearchField] = 1;
            }

            if (command.IsValid(clientState.EmpireState))
            {
                clientState.Commands.Push(command);
                command.ApplyToState(clientState.EmpireState);
            }
        }
    }
}
