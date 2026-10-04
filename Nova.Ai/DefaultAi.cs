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
        /// Nova's own personality codes, selected via an optional `-n &lt;0-7&gt;` CLI argument
        /// (CommandArguments.Option.AiPersonality) and resolving to
        /// <see cref="StandardPersonality"/> when omitted. They predate the spec's category table
        /// (behavior-specs-10/ai-opponent-behavior.md §1/§1a); <see cref="AiCategory.ForPersonality"/>
        /// maps them onto it: this disabled code is the spec's category 6 (no driver at all).
        /// </summary>
        public const int DisabledPersonality = 0;

        /// <summary>The spec's category 7, the economy-only driver: research choice, the end-of-pass
        /// routine (Defenses and Terraform advisors, bomber defence) and the top-up, but no fleet
        /// orders and no planet pass (§12).</summary>
        public const int PassivePersonality = 1;

        /// <summary>Nova codes 2-7 are the spec's categories 0-5 in order.</summary>
        public const int MinStandardPersonality = 2;
        public const int MaxStandardPersonality = 7;

        /// <summary>The default: category 2, Automitrons (see AiCategory's remarks for why).</summary>
        public const int StandardPersonality = 4;

        /// <summary>§4 step 3/4 and §11: hulls 14 and 15.</summary>
        private static readonly HashSet<string> ColonyHulls = new HashSet<string> { "Mini-Colony Ship", "Colony Ship" };

        /// <summary>§6 bomber-defence flag: hulls 16-19.</summary>
        private static readonly HashSet<string> BomberHulls = new HashSet<string> { "Mini Bomber", "B-17 Bomber", "Stealth Bomber", "B-52 Bomber" };

        private Intel turnData;
        private FleetList fuelStations = null;
        private DefaultAIPlanner aiPlan = null;
        // Null until DoMove, unless a test injected one (AiRandom): DoMove then derives this
        // turn's Random from the empire's seed and the year (TurnRandom), so an AI game is
        // repeatable from the game seed.
        private Random random;
        private bool randomInjected;
        private int personality = StandardPersonality;
        private AiFleetContext fleetContext;

        /// <summary>Fleets that already got their order this run from an earlier fleet pass.</summary>
        private readonly HashSet<long> handledFleets = new HashSet<long>();

        /// <summary>Fleets the warship passes (hunters, planet attack, bombers) own this run; their
        /// orders persist between turns instead of being reset (the original's fleets keep their
        /// waypoints, and the hunter and attack handler read them).</summary>
        private readonly HashSet<long> warshipFleets = new HashSet<long>();

        /// <summary>Stars already being explored this run (see HandleScouting).</summary>
        private List<StarIntel> excludedStars = new List<StarIntel>();

        /// <summary>The run's Random (a seam for tests).</summary>
        protected Random AiRandom
        {
            get { return random; }
            set
            {
                random = value ?? new Random();
                randomInjected = true;
            }
        }

        /// <summary>
        /// The AI's Random for one turn: derived from the empire's own seed
        /// (EmpireData.RandomSeed, set from the game seed at creation), its id and the turn year,
        /// so the same game always plays the same AI moves and a reloaded game continues
        /// identically. An empire without a seed (hand-built data, older saves) gets an unseeded
        /// Random, as before.
        /// </summary>
        public static Random TurnRandom(EmpireData empire)
        {
            if (empire == null || empire.RandomSeed == 0)
            {
                return new Random();
            }

            return GameRandom.Create(empire.RandomSeed, empire.TurnYear, "DefaultAi", empire.Id);
        }

        /// <summary>The turn's ambient stream (see DoMove), separate from <see cref="TurnRandom"/>
        /// so rules-code draws never shift the AI's own; null without a seed.</summary>
        private static Random TurnRandomAmbient(EmpireData empire)
        {
            if (empire == null || empire.RandomSeed == 0)
            {
                return null;
            }

            return GameRandom.Create(empire.RandomSeed, empire.TurnYear, "DefaultAiAmbient", empire.Id);
        }
        /// <summary>The mineral-packet seam (IAiPacketAdvisor): AiPacketAdvisor (§6, §12) by
        /// default; a test can supply another (null means NoPacketAdvisor).</summary>
        protected IAiPacketAdvisor PacketAdvisor
        {
            get { return packetAdvisor; }
            set { packetAdvisor = value ?? NoPacketAdvisor.Instance; }
        }

        private IAiPacketAdvisor packetAdvisor = new AiPacketAdvisor();
        private int category = AiCategory.Automitrons;
        private int skill = AiCategory.StandardSkill;
        private int yearCounter;

        // Sub AIs to manage planets, fleets and stuff
        private Dictionary<string, DefaultPlanetAI> planetAIs = new Dictionary<string, DefaultPlanetAI>();
        private Dictionary<long, DefaultFleetAI> fleetAIs = new Dictionary<long, DefaultFleetAI>();

        /// <summary>
        /// This empire's owned fleets in plain table order. behavior-specs-10/ai-opponent-
        /// behavior.md §8 retracts the per-turn fleet shuffle (the traced Fisher-Yates belongs
        /// to the battle-token table) and tells a clean-room implementation to "walk fleets in
        /// plain array order".
        /// </summary>
        private List<Fleet> ownFleets = new List<Fleet>();

        /// <summary>
        /// This empire's own DefaultPlanetAI helpers, reshuffled once per turn (see DoMove) -
        /// ports ai-opponent-behavior.md §8's confirmed per-turn Fisher-Yates shuffle of the AI's
        /// planet-processing order (`FUN_1090_60de`), which the advisors and the top-up walk.
        /// </summary>
        private List<DefaultPlanetAI> shuffledPlanetAIs = new List<DefaultPlanetAI>();

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

            category = AiCategory.ForPersonality(personality);
            if (category == AiCategory.NoDriver)
            {
                // §1: case 6 "has no code at all - the dispatcher silently does nothing for it".
                return;
            }

            if (!randomInjected || random == null)
            {
                random = TurnRandom(clientState.EmpireState);
            }

            // Shared rules code the AI calls that draws from the ambient stream (e.g. the
            // stochastic rounding of a mining projection) draws from this turn's own stream
            // too, never from process state.
            using IDisposable ambientRandom = GameRandom.Use(randomInjected ? null : TurnRandomAmbient(clientState.EmpireState));

            yearCounter = clientState.EmpireState.TurnYear - Global.StartingYear;
            aiPlan = new DefaultAIPlanner(clientState, category);

            // §1a: the skill field is the template tier when the race is one of the §14 AI
            // templates, else Standard (Nova has no skill picker).
            skill = AiRaceTemplates.TryIdentify(clientState.EmpireState.Race, out int unusedArchetype, out int templateTier) ? templateTier : AiCategory.StandardSkill;

            // create the helper AIs
            foreach (Star star in clientState.EmpireState.OwnedStars.Values)
            {
                if (star.Owner == clientState.EmpireState.Id)
                {
                    DefaultPlanetAI planetAI = new DefaultPlanetAI(star, clientState, this.aiPlan, random, category);
                    planetAI.Skill = skill;
                    planetAI.PacketAdvisor = packetAdvisor;
                    planetAIs.Add(star.Key, planetAI);
                }
            }

            // §8: the owned-planet order is shuffled once per run (unlike the fleet order).
            shuffledPlanetAIs = planetAIs.Values.ToList();
            Shuffle(shuffledPlanetAIs);

            ownFleets = clientState.EmpireState.OwnedFleets.Values
                .Where(fleet => fleet.Owner == clientState.EmpireState.Id)
                .ToList();

            fleetContext = new AiFleetContext(clientState, category, skill, random, ownFleets);
            handledFleets.Clear();
            warshipFleets.Clear();

            foreach (Fleet fleet in ownFleets)
            {
                aiPlan.CountFleet(fleet);
                DefaultFleetAI fleetAI = new DefaultFleetAI(fleet, clientState, fuelStations);
                fleetAIs.Add(fleet.Id, fleetAI);

                if (!fleet.IsStarbase && IsWarshipDispatched(fleet))
                {
                    warshipFleets.Add(fleet.Id);
                }

                // Reset the waypoint orders of the fleets Nova re-plans from scratch every turn.
                // Category 7 issues no fleet orders at all (§12), colony fleets keep theirs (the
                // §12 colony rules act on idle fleets and on fleets "at, or bound for" a planet),
                // and so do the warship passes' fleets and personality 4's haulers (its router
                // acts on idle haulers only).
                if (category != AiCategory.EconomyOnly && !fleet.CanColonize && !warshipFleets.Contains(fleet.Id) && !IsCybertronHauler(fleet))
                {
                    fleetAI.CutRoute();
                }
            }

            turnData = clientState.InputTurn;

            // §1 skeleton: research choice; fleet decisions; planet decisions; then the shared
            // end-of-pass routine and the production top-up, which every driver runs.
            HandleResearch();

            if (category != AiCategory.EconomyOnly)
            {
                // §12: category 7 issues no fleet orders and has no planet pass.
                HashSet<long> staleDesigns = HandleStaleDesigns();

                // §14/§17: the slot-0 scout and slot-1 colonizer the roles start from, then
                // refresh this personality's role designs (the design-builder seam).
                EnsureStartingDesigns();
                EnsureDesigns();
                HandleInvasionsUnderWay();
                HandleTransports();
                HandleCybertronHaulers();
                HandleScouting();
                HandleColonizing();
                HandleMinelayers();
                HandleWarships();
                HandleSplitter(staleDesigns);

                // §4: the colony-ship gate is evaluated once per run, before the planet pass,
                // and its yes/no applies to every planet that turn.
                aiPlan.ColonyShipGateAllows = EvaluateColonyShipGate(out int colonyFleets);

                // §12: the personality's planet pass over the shuffled planets.
                AiPlanetPassContext passContext = new AiPlanetPassContext(clientState, category, skill, random, aiPlan, fleetContext)
                {
                    ColonyGateAllows = aiPlan.ColonyShipGateAllows,
                    ColonyFleetCount = colonyFleets,
                    HubCount = category <= AiCategory.Rototills ? new FreighterRoutingSelector(clientState, category, yearCounter).HubCount : 0,
                    Packets = packetAdvisor,
                };
                foreach (DefaultPlanetAI ai in shuffledPlanetAIs)
                {
                    AiPlanetPasses.Run(ai, passContext);
                }
            }

            HandleEndOfPass();
        }

        /// <summary>
        /// The design-builder seam (behavior-specs-10/ai-opponent-behavior.md §15 templates, §17
        /// role tags): AiDesignPlanner decides which role designs this personality builds this
        /// turn and each is added through an ordinary DesignCommand. The skill comes from the
        /// race when it is one of the §14 AI templates, else Standard. Nothing here queues the new
        /// designs; the planet pass picks a role's design through DefaultAIPlanner (scout,
        /// colonizer and transport), which reads AiDesignPlanner.CurrentDesigns.
        /// </summary>
        private void EnsureDesigns()
        {
            AiDesignPlanInput input = AiDesignPlanInput.From(clientState.EmpireState, category, skill);
            foreach (PlannedDesign planned in new AiDesignPlanner(random).Plan(input))
            {
                AddDesign(planned.Design);
            }
        }

        /// <summary>
        /// The AI's starting designs (§14: slot 0 is always a scout, slot 1 always a colonizer):
        /// Nova's new-game setup already gives every empire a Scout and a Santa Maria, so this
        /// only adds the §14 scout or colonizer when the empire has no design of that kind (judged
        /// by content, AiStartingDesigns.Classify), through an ordinary DesignCommand. The
        /// archetype and tier come from the race when it is one of the §14 templates, else from
        /// the category with the Standard tier.
        /// </summary>
        private void EnsureStartingDesigns()
        {
            int archetype = category;
            int tier = AiRaceTemplates.Standard;
            if (AiRaceTemplates.TryIdentify(clientState.EmpireState.Race, out int identified, out int identifiedTier))
            {
                archetype = identified;
                tier = identifiedTier;
            }

            EmpireData empire = clientState.EmpireState;
            foreach (ShipDesign design in AiStartingDesigns.BuildMissing(
                empire.Designs.Values,
                empire.AvailableComponents,
                archetype,
                tier,
                empire.ResearchLevels[TechLevel.ResearchField.Energy],
                empire.GetNextDesignKey))
            {
                AddDesign(design);
            }
        }

        private void AddDesign(ShipDesign design)
        {
            DesignCommand command = new DesignCommand(CommandMode.Add, design);
            if (command.IsValid(clientState.EmpireState))
            {
                clientState.Commands.Push(command);
                command.ApplyToState(clientState.EmpireState);
            }
        }

        /// <summary>
        /// Fisher-Yates shuffle in place, using this AI's own turn-scoped Random. The AI may run
        /// as a separate process (Nova.exe --ai) with no access to the server's streams, so its
        /// Random is derived from the empire's own seed in its turn file (see TurnRandom) - still
        /// repeatable from the game seed, but independent of the server's draws.
        /// </summary>
        private void Shuffle<T>(IList<T> list)
        {
            // The forward variant of ai-opponent-behavior.md §8 (FUN_1090_60de): for each index
            // from 0 up to count - 2, a bounded random offset into the remaining unshuffled
            // suffix picks the element swapped to the front.
            for (int i = 0; i < list.Count - 1; i++)
            {
                int j = i + random.Next(list.Count - i);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// The end-of-pass routine `FUN_1090_59e6` and the shared production top-up
        /// `FUN_10a8_1e2a` (behavior-specs-10/ai-opponent-behavior.md §6): the per-planet
        /// advisor chain over every planet, then the bomber-defence pass, then the top-up, each
        /// in the shuffled planet order.
        /// </summary>
        private void HandleEndOfPass()
        {
            foreach (DefaultPlanetAI ai in shuffledPlanetAIs)
            {
                ai.RunAdvisorChain();
            }

            int sizeIndex = GalaxySizeIndex();
            HashSet<string> bomberTargets = PlanetsWithForeignBombersInOrbit();
            foreach (DefaultPlanetAI ai in shuffledPlanetAIs)
            {
                ai.RunBomberDefence(yearCounter, sizeIndex, bomberTargets.Contains(ai.Planet.Name));
            }

            // §12 personality 4's packet routine `FUN_10a8_123e` runs after the end-of-pass
            // routine and before the top-up, through the packet seam.
            if (category == AiCategory.Cybertrons)
            {
                packetAdvisor.RunCybertronPackets(shuffledPlanetAIs, skill, sizeIndex, random);
            }

            foreach (DefaultPlanetAI ai in shuffledPlanetAIs)
            {
                ai.RunTopUp(yearCounter);
            }
        }

        /// <summary>
        /// The galaxy-size index (0 Tiny .. 4 Huge) from the map width. GameSettings is not
        /// always loaded in the AI process, so the width is also estimated from the furthest
        /// known star, rounded to the nearest 400 ly.
        /// </summary>
        private int GalaxySizeIndex()
        {
            int width = GameSettings.Data.MapWidth;
            foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
            {
                if (report.Position == null)
                {
                    continue;
                }

                int furthest = (int)Math.Max(report.Position.X, report.Position.Y);
                width = Math.Max(width, ((furthest + 200) / 400) * 400);
            }

            return PlanetAdvisors.GalaxySizeIndex(width);
        }

        /// <summary>§6: own planets a foreign fleet orbits that contains a design built on a
        /// bomber hull (`FUN_1090_40d6`).</summary>
        private HashSet<string> PlanetsWithForeignBombersInOrbit()
        {
            HashSet<string> flagged = new HashSet<string>();
            foreach (FleetIntel report in clientState.EmpireState.FleetReports.Values)
            {
                if (report.Owner == clientState.EmpireState.Id || !report.InOrbit || report.Composition == null || report.Position == null)
                {
                    continue;
                }

                if (!report.Composition.Values.Any(token => token.Quantity > 0 && HasHull(token.Design, BomberHulls)))
                {
                    continue;
                }

                foreach (DefaultPlanetAI ai in shuffledPlanetAIs)
                {
                    NovaPoint position = ai.Planet.Position;
                    if (position != null && position.X == report.Position.X && position.Y == report.Position.Y)
                    {
                        flagged.Add(ai.Planet.Name);
                    }
                }
            }

            return flagged;
        }

        private static bool HasHull(ShipDesign design, HashSet<string> hullNames)
        {
            return design != null && design.Blueprint != null && design.Blueprint.Name != null && hullNames.Contains(design.Blueprint.Name);
        }

        /// <summary>
        /// The colony-ship gate `FUN_1090_2baa` (§4) with this AI's own figures: its colony
        /// designs (hulls 14/15), its colony fleets F, the planets known to be owned by anyone P,
        /// the galaxy's planet count, the galaxy-size index and the skill. F is handed back
        /// (personality 5's planet pass reads it); it is computed whichever step decides.
        /// </summary>
        private bool EvaluateColonyShipGate(out int colonyFleets)
        {
            bool hasColonyDesign = clientState.EmpireState.Designs.Values.Any(design => HasHull(design, ColonyHulls));

            colonyFleets = ownFleets.Count(fleet =>
                fleet.Composition.Values.Any(token => token.Quantity > 0 && HasHull(token.Design, ColonyHulls)));

            HashSet<string> ownedPlanets = new HashSet<string>(
                clientState.EmpireState.StarReports.Values
                    .Where(report => report.Owner != Global.Nobody)
                    .Select(report => report.Name));
            foreach (Star star in clientState.EmpireState.OwnedStars.Values)
            {
                if (star.Owner == clientState.EmpireState.Id)
                {
                    ownedPlanets.Add(star.Name);
                }
            }

            return PlanetAdvisors.ColonyShipGate(
                skill,
                yearCounter,
                hasColonyDesign,
                colonyFleets,
                GalaxySizeIndex(),
                ownedPlanets.Count,
                clientState.EmpireState.StarReports.Count,
                random);
        }

        /// <summary>
        /// §12's minelayer branch (personality 0, also 4 and 5): from year 41 an own fleet that
        /// can lay mines and has no further waypoints either wanders, when it has more than 6
        /// minelayers, one time in five to a random planet within 105 ly (`FUN_1090_3af4`: a
        /// uniform pick, redrawn up to twice if it has a starbase), or, if it has no task, gets
        /// Lay Mines. Replaces the retracted §4 threat-driven minelaying.
        /// </summary>
        private void HandleMinelayers()
        {
            if (yearCounter < 41)
            {
                return;
            }

            foreach (Fleet fleet in ownFleets)
            {
                if (IsTaken(fleet) || fleet.IsStarbase || fleet.Waypoints.Count != 1 || fleet.NumberOfMines <= 0)
                {
                    continue;
                }

                int layers = fleet.Composition.Values
                    .Where(token => token.Design != null
                        && (token.Design.StandardMines.LayerRate + token.Design.HeavyMines.LayerRate + token.Design.SpeedBumbMines.LayerRate) > 0)
                    .Sum(token => token.Quantity);

                if (layers > 6 && random.Next(5) == 0)
                {
                    StarIntel destination = RandomPlanetWithin(fleet.Position, 105);
                    if (destination != null)
                    {
                        fleetAIs[fleet.Id].MoveTo(destination, Math.Max(1, EfficientWarp.ForFleet(fleet, true)));
                    }

                    continue;
                }

                if (fleet.Waypoints[0].Task == null || fleet.Waypoints[0].Task is NoTask)
                {
                    fleetAIs[fleet.Id].LayMines();
                }
            }
        }

        /// <summary>`FUN_1090_3af4`: a uniform pick among the known planets within the radius,
        /// redrawn up to twice if the pick has a starbase.</summary>
        private StarIntel RandomPlanetWithin(NovaPoint position, double radius)
        {
            List<StarIntel> candidates = clientState.EmpireState.StarReports.Values
                .Where(report => PointUtilities.DistanceSquare(position, report.Position) <= radius * radius)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            StarIntel pick = candidates[random.Next(candidates.Count)];
            for (int redraw = 0; redraw < 2 && pick.Starbase != null; redraw++)
            {
                pick = candidates[random.Next(candidates.Count)];
            }

            return pick;
        }

        private void HandleScouting()
        {
            List<Fleet> scoutFleets = new List<Fleet>();
            foreach (Fleet fleet in ownFleets)
            {
                if (!IsTaken(fleet) && fleet.Name.Contains("Scout") == true)
                {
                    scoutFleets.Add(fleet);
                }
            }

            // Find the stars we do not need to scout (eg home world)
            excludedStars = new List<StarIntel>();
            EmpireData scoutingView = turnData?.EmpireState ?? clientState.EmpireState;
            foreach (StarIntel report in scoutingView.StarReports.Values)
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

        /// <summary>
        /// Idle colony fleets (§2 and §12, "Colony ships"): each runs the colonization search
        /// (ColonizationTargetSelector: the nearest unowned, unclaimed planet, habitability-
        /// filtered for categories other than 0 and 5), loads the personality's colonists (§13)
        /// if it stands on its own planet, and gets Colonize. A fleet with no target is scrapped
        /// where it stands for categories 0 and 5 and otherwise tries again next turn. An empty
        /// fleet that cannot load colonists where it is heads for the nearest own starbase planet
        /// instead (a simplified form of personality 2's empty-fleet rule).
        /// </summary>
        private void HandleColonizing()
        {
            ColonizationTargetSelector targetSelector = new ColonizationTargetSelector(clientState, category);
            List<WormholeSighting> wormholes = KnownWormholes().ToList();

            foreach (Fleet colonyFleet in ownFleets)
            {
                // Colony fleets keep their orders between turns; an idle one has no next leg.
                if (IsTaken(colonyFleet) || !colonyFleet.CanColonize || AiFleetContext.NextWaypoint(colonyFleet) != null)
                {
                    continue;
                }

                if (category >= AiCategory.Turindrones && category <= AiCategory.Rototills)
                {
                    // §12 personalities 1-3 (row 58).
                    FleetOrder order = ColonyFleetRules.IdleColonyFleet(colonyFleet, fleetContext, targetSelector, wormholes);
                    if (order != null)
                    {
                        fleetAIs[colonyFleet.Id].Apply(order);
                    }

                    continue;
                }

                Star standingOn = colonyFleet.InOrbit as Star;
                int populationUnits = standingOn != null ? standingOn.Colonists / 100 : 0;
                int loadUnits = AiCategory.ColonistLoadUnits(category, populationUnits);
                StarIntel target = targetSelector.SelectTarget(colonyFleet);

                // §12 personality 0 step 3 (also personality 5): before year 120 a fleet at an own
                // planet may divert to a wormhole (`FUN_1090_0c4c`).
                WormholeSighting wormhole = null;
                bool atOwnPlanet = standingOn != null && standingOn.Owner == clientState.EmpireState.Id;
                if ((category == AiCategory.Robotoids || category == AiCategory.Macinti) && atOwnPlanet && yearCounter < WormholeDiversion.LastYear)
                {
                    wormhole = WormholeDiversion.Choose(colonyFleet.Position, target?.Position, wormholes, random);
                }

                if (target == null && wormhole == null)
                {
                    if (AiCategory.ScrapsTargetlessColonyFleet(category) && colonyFleet.InOrbit is Star)
                    {
                        fleetAIs[colonyFleet.Id].Apply(new FleetOrder { Scrap = true, Reason = "colony: no target, scrapped" });
                    }

                    continue;
                }

                int loadKt = DefaultFleetAI.ColonistsToLoadKt(colonyFleet, loadUnits);
                if (colonyFleet.Cargo.ColonistsInKilotons <= 0 && loadKt <= 0)
                {
                    SendHomeToLoad(colonyFleet);
                    continue;
                }

                if (wormhole != null)
                {
                    fleetAIs[colonyFleet.Id].Apply(new FleetOrder { LoadColonistsKt = loadKt, Destination = wormhole, Reason = "colony: wormhole diversion" });
                    continue;
                }

                fleetAIs[colonyFleet.Id].Apply(new FleetOrder
                {
                    LoadColonistsKt = loadKt,
                    Destination = target,
                    DestinationTask = new ColoniseTask(),
                    Reason = "colony: colonize",
                });
                targetSelector.MarkClaimed(target);
            }
        }

        /// <summary>
        /// The wormholes the colony ships may divert to (§12, `FUN_1090_0c4c`): the wormhole ends
        /// this empire has detected (EmpireData.WormholeReports, written by the server's scan
        /// step), see WormholeDiversion.Sightings. A test can supply others.
        /// </summary>
        protected virtual IEnumerable<WormholeSighting> KnownWormholes()
        {
            return WormholeDiversion.Sightings(clientState.EmpireState);
        }

        /// <summary>An empty colony fleet with no colonists to load where it stands flies to the
        /// nearest own starbase planet, unless it is already at one (where it waits).</summary>
        private void SendHomeToLoad(Fleet colonyFleet)
        {
            Star home = clientState.EmpireState.OwnedStars.Values
                .Where(star => star.Owner == clientState.EmpireState.Id && star.Starbase != null && star.Colonists > 0)
                .OrderBy(star => PointUtilities.DistanceSquare(star.Position, colonyFleet.Position))
                .FirstOrDefault();

            if (home == null || (colonyFleet.InOrbit != null && colonyFleet.InOrbit.Name == home.Name))
            {
                return;
            }

            fleetAIs[colonyFleet.Id].Apply(FleetOrder.MoveTo(home, "colony: empty, flies home to load"));
        }

        /// <summary>A fleet another pass already owns this run.</summary>
        private bool IsTaken(Fleet fleet)
        {
            return handledFleets.Contains(fleet.Id) || warshipFleets.Contains(fleet.Id);
        }

        /// <summary>
        /// Whether the warship passes of §12 own this fleet (its orders then persist): the
        /// planet-attack handler's strike and bomber fleets and the hunters of each personality,
        /// by the role stand-in of AiFleetRoles.
        /// </summary>
        private bool IsWarshipDispatched(Fleet fleet)
        {
            Dictionary<AiFleetRole, int> roles = AiFleetRoles.CountRoles(fleet.Composition.Values, category);
            int strike = AiFleetRoles.Count(roles, AiFleetRole.StrikeLight) + AiFleetRoles.Count(roles, AiFleetRole.StrikeHeavy) + AiFleetRoles.Count(roles, AiFleetRole.Bomber);
            int hunters = AiFleetRoles.Count(roles, AiFleetRole.Hunter);
            bool combat = FleetRolePredicates.IsCombatFleet(fleet.Composition.Values);

            switch (category)
            {
                case AiCategory.Robotoids:
                    return strike > 0 || combat;
                case AiCategory.Cybertrons:
                    return hunters > 0 || strike > 0;
                case AiCategory.Macinti:
                    return strike > 0 || (combat && AiFleetRoles.Count(roles, AiFleetRole.LineWarship) > 0);
                case AiCategory.Turindrones:
                case AiCategory.Automitrons:
                case AiCategory.Rototills:
                    return AiFleetRoles.Count(roles, AiFleetRole.Bomber) > 0 || hunters > 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The stale-slot sweep (§10, §16 item 3; personalities 0, 1, 2 and 4): stale designs with
        /// no ships are deleted, the rest flagged for this turn; personalities 0 and 4 then apply
        /// fleet pass 3's obsolete-fleet rule. Returns the flags for the splitter.
        /// </summary>
        private HashSet<long> HandleStaleDesigns()
        {
            if (category > AiCategory.Automitrons && category != AiCategory.Cybertrons)
            {
                return new HashSet<long>();
            }

            HashSet<long> stale = StaleDesignSweep.Sweep(clientState.EmpireState, category, out List<ShipDesign> toDelete);
            foreach (ShipDesign design in toDelete)
            {
                DesignCommand command = new DesignCommand(CommandMode.Delete, design.Key);
                if (command.IsValid(clientState.EmpireState))
                {
                    command.ApplyToState(clientState.EmpireState);
                    clientState.Commands.Push(command);
                }
            }

            if (category == AiCategory.Robotoids || category == AiCategory.Cybertrons)
            {
                foreach (Fleet fleet in ownFleets)
                {
                    if (!fleet.IsStarbase && StaleDesignSweep.IsObsolete(fleet, stale))
                    {
                        handledFleets.Add(fleet.Id);
                        fleetAIs[fleet.Id].Apply(StaleDesignSweep.ObsoleteFleetOrder(fleet, fleetContext));
                    }
                }
            }

            return stale;
        }

        /// <summary>
        /// The splitter `FUN_1090_5bda` (§16 item 3; personalities 0, 1 and 2, and 4 from year
        /// 81): every fleet that mixes flagged and unflagged designs has its flagged stacks moved
        /// into a new fleet, while the player owns fewer than 501 fleets.
        /// </summary>
        private void HandleSplitter(HashSet<long> stale)
        {
            if (stale.Count == 0 || (category == AiCategory.Cybertrons && yearCounter < 81))
            {
                return;
            }

            int fleetCount = ownFleets.Count;
            foreach (Fleet fleet in ownFleets)
            {
                if (fleetCount >= StaleDesignSweep.FleetLimit)
                {
                    break;
                }

                if (fleet.IsStarbase || !StaleDesignSweep.IsMixed(fleet, stale))
                {
                    continue;
                }

                List<long> moved = fleet.Composition.Values
                    .Where(token => token.Quantity > 0 && token.Design != null && stale.Contains(token.Design.Key))
                    .Select(token => token.Design.Key)
                    .ToList();
                fleetAIs[fleet.Id].SplitOff(moved);
                fleetCount++;
            }
        }

        /// <summary>
        /// The first fleet pass of personalities 1-3 (§12; row 58): colony fleets and haulers at,
        /// or bound for, another player's planet land their colonists there or have their route
        /// cut (ColonyFleetRules.InvasionUnderWay).
        /// </summary>
        private void HandleInvasionsUnderWay()
        {
            if (category < AiCategory.Turindrones || category > AiCategory.Rototills)
            {
                return;
            }

            foreach (Fleet fleet in ownFleets)
            {
                if (IsTaken(fleet) || fleet.IsStarbase)
                {
                    continue;
                }

                FleetOrder order = ColonyFleetRules.InvasionUnderWay(fleet, fleetContext);
                if (order != null)
                {
                    handledFleets.Add(fleet.Id);
                    fleetAIs[fleet.Id].Apply(order);
                }
            }
        }

        /// <summary>
        /// The warship branches of §12 for each personality, in fleet table order:
        /// <list type="bullet">
        /// <item>0: strike and bomber fleets go to the planet-attack handler; other combat fleets
        /// not already chasing a fleet hunt (`FUN_1090_1438`; the rendezvous tried first is not
        /// ported).</item>
        /// <item>4: a fleet with hunter ships hunts once twice their count reaches H, or when it
        /// already has a route, else waits; battle groups go to the attack copy.</item>
        /// <item>5: strike and bomber fleets go to the attack copy; line warships not chasing a
        /// fleet hunt.</item>
        /// <item>1-3: bombers follow personality 2's bomber rule (the inline target search);
        /// hunters use the variant `FUN_1090_3c80`.</item>
        /// </list>
        /// Before the pass, personalities 0 and 4 mark the planets their warships are at or
        /// heading for as already targeted.
        /// </summary>
        private void HandleWarships()
        {
            List<Fleet> combatFleets = ownFleets
                .Where(fleet => !fleet.IsStarbase && FleetRolePredicates.IsCombatFleet(fleet.Composition.Values))
                .ToList();

            if (category == AiCategory.Robotoids || category == AiCategory.Cybertrons)
            {
                foreach (Fleet fleet in ownFleets.Where(f => warshipFleets.Contains(f.Id)))
                {
                    Dictionary<AiFleetRole, int> roles = AiFleetRoles.CountRoles(fleet.Composition.Values, category);
                    int marking = AiFleetRoles.Count(roles, AiFleetRole.StrikeLight) + AiFleetRoles.Count(roles, AiFleetRole.StrikeHeavy)
                        + (category == AiCategory.Cybertrons ? AiFleetRoles.Count(roles, AiFleetRole.Bomber) + AiFleetRoles.Count(roles, AiFleetRole.Hunter) : 0);
                    StarIntel planet = fleetContext.AtOrBoundFor(fleet);
                    if (marking > 0 && planet != null && PlanetAttackHandler.AttackValue(planet, fleetContext) > 0)
                    {
                        fleetContext.TargetedPlanets.Add(planet.Name);
                    }
                }
            }

            foreach (Fleet fleet in ownFleets)
            {
                if (handledFleets.Contains(fleet.Id) || !warshipFleets.Contains(fleet.Id))
                {
                    continue;
                }

                Dictionary<AiFleetRole, int> roles = AiFleetRoles.CountRoles(fleet.Composition.Values, category);
                int strike = AiFleetRoles.Count(roles, AiFleetRole.StrikeLight) + AiFleetRoles.Count(roles, AiFleetRole.StrikeHeavy) + AiFleetRoles.Count(roles, AiFleetRole.Bomber);
                int hunters = AiFleetRoles.Count(roles, AiFleetRole.Hunter);
                bool combat = combatFleets.Contains(fleet);
                FleetOrder order = null;

                switch (category)
                {
                    case AiCategory.Robotoids:
                        if (strike > 0)
                        {
                            order = PlanetAttackHandler.Handle(fleet, fleetContext, copy: false);
                        }
                        else if (combat && !AiFleetContext.IsChasingFleet(fleet))
                        {
                            order = EnemyFleetHunter.Hunt(fleet, fleetContext, combatFleets, variant: false);
                        }

                        break;

                    case AiCategory.Cybertrons:
                        if (hunters > 0)
                        {
                            if (2 * hunters >= HunterThreshold(yearCounter) || AiFleetContext.NextWaypoint(fleet) != null)
                            {
                                order = EnemyFleetHunter.Hunt(fleet, fleetContext, combatFleets, variant: false);
                            }
                        }
                        else if (strike > 0)
                        {
                            order = PlanetAttackHandler.Handle(fleet, fleetContext, copy: true);
                        }

                        break;

                    case AiCategory.Macinti:
                        if (strike > 0)
                        {
                            order = PlanetAttackHandler.Handle(fleet, fleetContext, copy: true);
                        }
                        else if (!AiFleetContext.IsChasingFleet(fleet))
                        {
                            order = EnemyFleetHunter.Hunt(fleet, fleetContext, combatFleets, variant: false);
                        }

                        break;

                    default:
                        if (AiFleetRoles.Count(roles, AiFleetRole.Bomber) > 0)
                        {
                            order = PlanetAttackHandler.BomberRule(fleet, fleetContext);
                        }
                        else if (hunters > 0)
                        {
                            order = EnemyFleetHunter.Hunt(fleet, fleetContext, combatFleets, variant: true);
                        }

                        break;
                }

                Execute(fleet, order);
            }
        }

        /// <summary>
        /// Personality 4's hunter launch figure H (§12): 1 up to year 50, then
        /// (year − 50) ÷ 10 + 1, plus ((year − 100) ÷ 10) × (year ÷ 100) after year 100.
        /// </summary>
        public static int HunterThreshold(int year)
        {
            if (year <= 50)
            {
                return 1;
            }

            int h = ((year - 50) / 10) + 1;
            if (year > 100)
            {
                h += ((year - 100) / 10) * (year / 100);
            }

            return h;
        }

        /// <summary>Applies an order, sending an exploring fleet on Nova's scout logic (the
        /// stand-in for the exploration pick `FUN_1090_0df2`) and falling back when there is
        /// nothing left to explore.</summary>
        private void Execute(Fleet fleet, FleetOrder order)
        {
            if (order == null)
            {
                return;
            }

            DefaultFleetAI fleetAI = fleetAIs[fleet.Id];
            if (!order.Explore)
            {
                fleetAI.Apply(order);
                return;
            }

            fleetAI.CutRoute();
            StarIntel explored = fleetAI.Scout(excludedStars);
            if (explored != null)
            {
                excludedStars.Add(explored);
                return;
            }

            if (fleet.Waypoints.Count == 1 && order.ExploreFallback != null)
            {
                fleetAI.Apply(order.ExploreFallback());
            }
        }

        /// <summary>
        /// Routes every idle freighter through the shared router (behavior-specs-10/ai-opponent-
        /// behavior.md §5, `FUN_1090_19c8`). Only categories 0-3 call it; personalities 4, 5 and
        /// 7 never do.
        /// </summary>
        private void HandleTransports()
        {
            if (category > AiCategory.Rototills)
            {
                return;
            }

            FreighterRoutingSelector routingSelector = new FreighterRoutingSelector(clientState, category, yearCounter);

            foreach (Fleet transportFleet in ownFleets)
            {
                if (IsTaken(transportFleet) || transportFleet.IsStarbase || transportFleet.CanColonize || transportFleet.TotalCargoCapacity <= 0 || transportFleet.Waypoints.Count != 1)
                {
                    continue;
                }

                FreighterRun run = routingSelector.SelectRun(transportFleet);
                if (run != null)
                {
                    fleetAIs[transportFleet.Id].DeliverCargo(run);
                }
            }
        }

        /// <summary>
        /// Personality 4's haulers (§12, personality 4 "Haulers"): each idle fleet with hauler
        /// ships and no ships in slots 4-13 is routed by its own colonist router
        /// (<see cref="CybertronHaulerRouter"/>, `FUN_10a8_23d0`); personality 4 never uses the
        /// shared freighter router.
        /// </summary>
        private void HandleCybertronHaulers()
        {
            if (category != AiCategory.Cybertrons)
            {
                return;
            }

            foreach (Fleet fleet in ownFleets)
            {
                if (IsTaken(fleet) || !IsCybertronHauler(fleet) || AiFleetContext.NextWaypoint(fleet) != null)
                {
                    continue;
                }

                FleetOrder order = CybertronHaulerRouter.Route(fleet, fleetContext);
                if (order != null)
                {
                    handledFleets.Add(fleet.Id);
                    fleetAIs[fleet.Id].Apply(order);
                }
            }
        }

        /// <summary>A personality-4 hauler fleet: hauler ships (slots 2-3; the §11 hauler hulls
        /// for an untagged design) and no ships in slots 4-13 (hunters and battle groups).</summary>
        private bool IsCybertronHauler(Fleet fleet)
        {
            if (category != AiCategory.Cybertrons || fleet.IsStarbase || fleet.CanColonize)
            {
                return false;
            }

            Dictionary<AiFleetRole, int> roles = AiFleetRoles.CountRoles(fleet.Composition.Values, category);
            return AiFleetRoles.Count(roles, AiFleetRole.Freighter) > 0
                && AiFleetRoles.Count(roles, AiFleetRole.Hunter) == 0
                && AiFleetRoles.Count(roles, AiFleetRole.StrikeLight) == 0
                && AiFleetRoles.Count(roles, AiFleetRole.StrikeHeavy) == 0
                && AiFleetRoles.Count(roles, AiFleetRole.Bomber) == 0;
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
