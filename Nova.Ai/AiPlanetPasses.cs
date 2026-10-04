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

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// What the per-personality planet passes read in one AI run (behavior-specs-10/
    /// ai-opponent-behavior.md §12): the role designs (§17, AiDesignPlanner.CurrentDesigns), the
    /// ships in existence per design (design +0x83, read once at the start of the run, so ships
    /// queued this turn do not count), the role counts of the AI's fleets, the colony-ship gate's
    /// verdict and fleet count F (§4), the hub count (§5), and the run's counters.
    /// </summary>
    public sealed class AiPlanetPassContext
    {
        private readonly Dictionary<long, int> shipsByDesign = new Dictionary<long, int>();
        private readonly Dictionary<AiFleetRole, int> roleShips = new Dictionary<AiFleetRole, int>();
        private int? habitableTargets;

        public AiPlanetPassContext(ClientData clientState, int category, int skill, Random random, DefaultAIPlanner planner, AiFleetContext fleets)
        {
            ClientState = clientState;
            Category = category;
            Skill = skill;
            Random = random;
            Planner = planner;
            Fleets = fleets;
            RoleDesigns = AiDesignPlanner.CurrentDesigns(category, Empire.Designs.Values);
            OwnPlanetCount = Empire.OwnedStars.Values.Count(star => star.Owner == Empire.Id);

            foreach (Fleet fleet in Empire.OwnedFleets.Values.Where(f => f.Owner == Empire.Id && f.Composition != null))
            {
                foreach (ShipToken token in fleet.Composition.Values.Where(t => t != null && t.Design != null && t.Quantity > 0))
                {
                    shipsByDesign.TryGetValue(token.Design.Key, out int count);
                    shipsByDesign[token.Design.Key] = count + token.Quantity;
                }

                if (fleet.IsStarbase)
                {
                    continue;
                }

                Dictionary<AiFleetRole, int> roles = AiFleetRoles.CountRoles(fleet.Composition.Values, category);
                foreach (KeyValuePair<AiFleetRole, int> role in roles)
                {
                    roleShips.TryGetValue(role.Key, out int count);
                    roleShips[role.Key] = count + role.Value;
                }

                if (category == AiCategory.Cybertrons)
                {
                    CountCybertronFleet(fleet, roles);
                }
            }

            GenesisBudget = Math.Min(10, OwnPlanetCount / 20);
        }

        public ClientData ClientState { get; }

        public EmpireData Empire
        {
            get { return ClientState.EmpireState; }
        }

        public int Category { get; }

        public int Skill { get; }

        public Random Random { get; }

        public DefaultAIPlanner Planner { get; }

        /// <summary>The fleet passes' view (at-or-bound-for tests); may be null in tests that
        /// need no fleet counts.</summary>
        public AiFleetContext Fleets { get; }

        /// <summary>The year counter: 0 in the first year.</summary>
        public int Year
        {
            get { return Empire.TurnYear - Global.StartingYear; }
        }

        /// <summary>Each role tag's current design.</summary>
        public Dictionary<string, ShipDesign> RoleDesigns { get; }

        /// <summary>The colony-ship gate's verdict this run (§4).</summary>
        public bool ColonyGateAllows { get; set; } = true;

        /// <summary>F, the colony-fleet count of the gate (§4 step 4), raised by one for each
        /// colony ship personality 5 queues this turn.</summary>
        public int ColonyFleetCount { get; set; }

        /// <summary>The AI's freighter hub count (§5, rebuilt statelessly per §16).</summary>
        public int HubCount { get; set; }

        /// <summary>The mineral-packet seam.</summary>
        public IAiPacketAdvisor Packets { get; set; } = NoPacketAdvisor.Instance;

        /// <summary>The planets the AI owns ("its planet count", read as the AI's own planets
        /// wherever §12 says "planets").</summary>
        public int OwnPlanetCount { get; }

        // ------------------------------------------------------------- per-run counters

        /// <summary>Personality 3 queues its colony ship at the first eligible planet only.</summary>
        public bool RototillColonyQueued { get; set; }

        /// <summary>Personality 4: own warship fleets (slots 4-13) at or bound for a planet; each
        /// battle-group order adds one.</summary>
        public int CybertronBoundWarshipFleets { get; set; }

        /// <summary>Personality 4: own warship fleets not bound for a planet; each hunter order
        /// adds one.</summary>
        public int CybertronUnboundWarshipFleets { get; set; }

        /// <summary>Personality 4: guard fleets (slots 14-15); each guard order adds one.</summary>
        public int CybertronGuardFleets { get; set; }

        /// <summary>Personality 5: haulers queued this turn, added to those in existence.</summary>
        public int MacintiHaulersQueued { get; set; }

        /// <summary>Personality 5: Genesis Devices still allowed this turn, min(10, planets ÷ 20).</summary>
        public int GenesisBudget { get; set; }

        // ------------------------------------------------------------- lookups

        public int ShipsInExistence(ShipDesign design)
        {
            return design != null && shipsByDesign.TryGetValue(design.Key, out int count) ? count : 0;
        }

        /// <summary>Ships of a fleet role in the AI's own (non-starbase) fleets.</summary>
        public int RoleShips(AiFleetRole role)
        {
            return roleShips.TryGetValue(role, out int count) ? count : 0;
        }

        /// <summary>A role tag's current design, or null (the original: slot empty or deleted).</summary>
        public ShipDesign Role(string tag)
        {
            return tag != null && RoleDesigns.TryGetValue(tag, out ShipDesign design) ? design : null;
        }

        /// <summary>The newest (creation year, then key) current design among the tags.</summary>
        public ShipDesign Newest(params string[] tags)
        {
            return tags.Select(Role)
                .Where(design => design != null)
                .OrderBy(design => AiDesignPlanner.CreationYear(design, Global.StartingYear))
                .ThenBy(design => design.Key)
                .LastOrDefault();
        }

        /// <summary>
        /// The habitable-target count of personalities 2 and 3 (§12, personality 2 "Building"):
        /// unowned planets with a positive terraformed habitability for the AI (`FUN_1048_47ec`)
        /// whose report-detail byte exceeds 2. Nova's reports have no detail byte; a report that
        /// carries the planet's environment stands in for it.
        /// </summary>
        public int HabitableUnownedTargets()
        {
            if (!habitableTargets.HasValue)
            {
                habitableTargets = Empire.StarReports.Values.Count(report =>
                    report.Owner == Global.Nobody
                    && report.Gravity != Global.Unset
                    && report.Temperature != Global.Unset
                    && report.Radiation != Global.Unset
                    && AiPlanetPassRules.TerraformedHabValue(Empire.Race, report) > 0);
            }

            return habitableTargets.Value;
        }

        /// <summary>
        /// Personality 4's warship counts (first fleet pass, §12): a fleet with ships in slots
        /// 4-13 (hunters and battle groups: the hunter, strike and bomber roles) counts as bound
        /// when it is at or bound for a planet, else not bound; a guard fleet is one with ships of
        /// a slot-14/15 (planetary guard) design. The retirement-loop quirk is not reproduced.
        /// </summary>
        private void CountCybertronFleet(Fleet fleet, Dictionary<AiFleetRole, int> roles)
        {
            int warships = AiFleetRoles.Count(roles, AiFleetRole.Hunter)
                + AiFleetRoles.Count(roles, AiFleetRole.StrikeLight)
                + AiFleetRoles.Count(roles, AiFleetRole.StrikeHeavy)
                + AiFleetRoles.Count(roles, AiFleetRole.Bomber);
            if (warships > 0)
            {
                bool bound = Fleets != null && Fleets.AtOrBoundFor(fleet) != null;
                if (bound)
                {
                    CybertronBoundWarshipFleets++;
                }
                else
                {
                    CybertronUnboundWarshipFleets++;
                }
            }

            if (fleet.Composition.Values.Any(token => token.Quantity > 0 && AiPlanetPassRules.RoleKind(token.Design, Category) == AiDesignRoleKind.PlanetaryGuard))
            {
                CybertronGuardFleets++;
            }
        }
    }

    /// <summary>What personality 4's build chooser `FUN_10a8_30c4` orders (§12).</summary>
    public enum CybertronBuild
    {
        None,
        BattleGroup,
        Guard,
        Hunter
    }

    /// <summary>
    /// The decision rules of the per-personality planet passes (behavior-specs-10/
    /// ai-opponent-behavior.md §12), free of game objects so each can be tested on its own.
    /// Population figures are in stored units of 100 colonists (§13).
    /// </summary>
    public static class AiPlanetPassRules
    {
        /// <summary>Seam (spec gap): personality 0's strike batch size. §12 says only "strike and
        /// bomber batches"; the neutral default is one ship.</summary>
        public const int RobotoidStrikeBatch = 1;

        /// <summary>Seam (spec gap): personality 0's bomber batch size (see above).</summary>
        public const int RobotoidBomberBatch = 1;

        /// <summary>Seam (spec gap): personality 0's line-warship order size (§12 names "line
        /// warships" with no quantity).</summary>
        public const int RobotoidLineBatch = 1;

        /// <summary>Personalities 0, 4 and 5 queue minelayers "four at a time" (§12).</summary>
        public const int MinelayerBatch = 4;

        /// <summary>Seam (spec gap): personality 5's warship order size (§12 says only
        /// "Warships: from the slot ranges above").</summary>
        public const int MacintiWarshipBatch = 1;

        /// <summary>
        /// Personality 2's freighters (§12): one when Pr &gt; 4 and the AI has fewer freighter
        /// ships than the larger of a tenth of its planet count and twice its hub count, or fewer
        /// than 10/7 of that on a 1-in-4 roll.
        /// </summary>
        public static bool AutomitronWantsFreighter(int propulsionLevel, int freighterShips, int ownPlanets, int hubs, Random random)
        {
            if (propulsionLevel <= 4)
            {
                return false;
            }

            int quota = Math.Max(ownPlanets / 10, 2 * hubs);
            if (freighterShips < quota)
            {
                return true;
            }

            // "fewer than 10/7 of that", compared exactly: ships × 7 < quota × 10.
            return (long)freighterShips * 7 < (long)quota * 10 && random.Next(4) == 0;
        }

        /// <summary>
        /// Personality 2's minelayers (§12): three, one time in three, when the first own fleet at
        /// the planet holds fewer than 10 of them (17 on a 1-in-8 roll) and a 1-in-(2n + 1) roll
        /// succeeds. n is read as the minelayers that fleet holds (ambiguity: §12 does not name
        /// n). The rolls are drawn in that order. Returns the quantity (3 or 0).
        /// </summary>
        public static int AutomitronMinelayers(int minelayersInFirstFleet, Random random)
        {
            if (random.Next(3) != 0)
            {
                return 0;
            }

            int limit = random.Next(8) == 0 ? 17 : 10;
            int n = Math.Max(0, minelayersInFirstFleet);
            if (n >= limit)
            {
                return 0;
            }

            return random.Next((2 * n) + 1) == 0 ? 3 : 0;
        }

        /// <summary>Personality 2's caps for the slot-11/12 and slot-9/10 ships: planets ÷ 12 + 8
        /// and planets ÷ 24 + 4.</summary>
        public static int AutomitronLineCap(int ownPlanets)
        {
            return (ownPlanets / 12) + 8;
        }

        public static int AutomitronGarrisonCap(int ownPlanets)
        {
            return (ownPlanets / 24) + 4;
        }

        /// <summary>
        /// Personality 2's slot-9/10 (and 11/12) builds: up to five of the newest design, one at a
        /// time while the surplus stays non-negative (every mineral and resources, after the ship's
        /// cost), as long as fewer than <paramref name="cap"/> of it exist (counting those just
        /// added). Returns the quantity.
        /// </summary>
        public static int AutomitronCappedBuild(int existing, int cap, Resources surplus, Resources unitCost)
        {
            int quantity = 0;
            Resources left = new Resources(surplus);
            while (quantity < 5 && existing + quantity < cap)
            {
                Resources after = left - unitCost;
                if (after.Ironium < 0 || after.Boranium < 0 || after.Germanium < 0 || after.Energy < 0)
                {
                    break;
                }

                left = after;
                quantity++;
            }

            return quantity;
        }

        /// <summary>
        /// Personality 3's colony ship (§12): while slot-1 ships in existence plus one are fewer
        /// than the habitable unowned planets, "or none exist" (read as: no slot-1 ship exists).
        /// </summary>
        public static bool RototillWantsColonyShip(int colonyShipsInExistence, int habitableTargets)
        {
            return colonyShipsInExistence == 0 || colonyShipsInExistence + 1 < habitableTargets;
        }

        /// <summary>
        /// Personality 4's Terraform (§12): (resource surplus ÷ 70) + 1 units when the planet's
        /// habitability value is below 10, or one unit when it is below the terraformable
        /// potential and the surplus exceeds 70.
        /// </summary>
        public static int CybertronTerraform(int habitability, int terraformablePotential, long resourceSurplus)
        {
            if (resourceSurplus < 0)
            {
                return 0;
            }

            if (habitability < 10)
            {
                return (int)Math.Min(int.MaxValue, (resourceSurplus / 70) + 1);
            }

            return habitability < terraformablePotential && resourceSurplus > 70 ? 1 : 0;
        }

        /// <summary>The growth product of personalities 4 and 5: stored population × the race's
        /// growth rate in percent, the rate doubled for Hyper Expansion.</summary>
        public static long GrowthProduct(int populationUnits, double growthRatePercent, bool hyperExpansion)
        {
            long rate = (long)growthRatePercent * (hyperExpansion ? 2 : 1);
            return populationUnits * rate;
        }

        /// <summary>
        /// Personality 4's colony ships (§12, colony-ship alternation): one when bit 3 of the
        /// planet's packet word is clear or the growth product exceeds 5,500, a second when the
        /// product exceeds 15,000 before year 100; both need fewer than 40 ships of the slot-1
        /// design and the isolation check. (Bit 0 "no colony target here" always passes, §12.)
        /// </summary>
        public static int CybertronColonyShips(bool alternationBitSet, long growthProduct, int slotOneShips, bool isolationPasses, int year)
        {
            if (alternationBitSet && growthProduct <= 5500)
            {
                return 0;
            }

            if (slotOneShips >= 40 || !isolationPasses)
            {
                return 0;
            }

            return growthProduct > 15000 && year < 100 ? 2 : 1;
        }

        /// <summary>
        /// Stateless stand-in for bit 3 of personality 4's packet word (§16: set on a turn when a
        /// colony ship is queued, cleared on a turn when none is, "so a planet normally builds one
        /// every other turn"): Nova's AI keeps no memory between turns, so the bit is read as set
        /// on odd years. This keeps the every-other-turn rhythm but not the exact phase.
        /// </summary>
        public static bool CybertronAlternationBitSet(int year)
        {
            return (year % 2) == 1;
        }

        /// <summary>
        /// Personality 4's build chooser `FUN_10a8_30c4` (§12): at most once per planet per turn,
        /// with probability 10%, or 40% when the room for both mines and factories is at least
        /// 100. A battle group is offered while no more than 250 warship fleets are bound for a
        /// planet; the guard design while fewer than 40 guard fleets exist and a chance roll
        /// succeeds (50% with another player's planet within 300 ly, else 10%; the "guard weak" bit
        /// never reaches the pass); a hunter while no more than 120 warship fleets are unbound. One
        /// 0-99 roll r: a group when r &gt; 50 and one is offered, else a guard when r &gt; 25 and
        /// one is offered, else a hunter. Rolls: the chance, the guard offer, then r.
        /// </summary>
        public static CybertronBuild CybertronChooser(
            bool roomAtLeast100,
            bool groupAvailable,
            int boundWarshipFleets,
            bool guardDesign,
            int guardFleets,
            bool foreignPlanetWithin300,
            bool hunterDesign,
            int unboundWarshipFleets,
            Random random)
        {
            int chance = roomAtLeast100 ? 40 : 10;
            if (random.Next(100) >= chance)
            {
                return CybertronBuild.None;
            }

            bool groupOffered = groupAvailable && boundWarshipFleets <= 250;
            bool guardOffered = guardDesign && guardFleets < 40 && random.Next(100) < (foreignPlanetWithin300 ? 50 : 10);
            bool hunterOffered = hunterDesign && unboundWarshipFleets <= 120;

            int r = random.Next(100);
            if (r > 50 && groupOffered)
            {
                return CybertronBuild.BattleGroup;
            }

            if (r > 25 && guardOffered)
            {
                return CybertronBuild.Guard;
            }

            return hunterOffered ? CybertronBuild.Hunter : CybertronBuild.None;
        }

        /// <summary>
        /// Personality 5's colony-line skip (§12): after year 120 always once F exceeds 49; also
        /// when the gate refuses, when F exceeds 100, or when F exceeds 40 after year 120, each of
        /// these overridden 8% of the time (a 0-99 roll of 7 or less; read as one roll per
        /// failing test).
        /// </summary>
        public static bool MacintiColonyLineSkipped(int year, int colonyFleets, bool gateAllows, Random random)
        {
            if (year > 120 && colonyFleets > 49)
            {
                return true;
            }

            if (!gateAllows && random.Next(100) > 7)
            {
                return true;
            }

            if (colonyFleets > 100 && random.Next(100) > 7)
            {
                return true;
            }

            return year > 120 && colonyFleets > 40 && random.Next(100) > 7;
        }

        /// <summary>
        /// Personality 5's extra colony ships (§12): from year 5, a second when the growth product
        /// exceeds 2,300, the planet's annual resources exceed 35 and skill is 1 or more; a third
        /// when the product exceeds 3,600, resources exceed 50 and skill is 2 or more.
        /// </summary>
        public static int MacintiExtraColonyShips(int year, long growthProduct, int resources, int skill)
        {
            if (year < 5)
            {
                return 0;
            }

            int extra = 0;
            if (growthProduct > 2300 && resources > 35 && skill >= 1)
            {
                extra++;
            }

            if (growthProduct > 3600 && resources > 50 && skill >= 2)
            {
                extra++;
            }

            return extra;
        }

        /// <summary>
        /// Personality 5's Genesis selection test (§12, `:68039`-`68070`): stored population over
        /// 10,000 units; no Genesis Device queued; the number of leading minerals (Ironium,
        /// Boranium, Germanium order) with at least 2,000 kT is not exactly 2; and the "poor
        /// concentrations" roll on the concentration sum c: below 15 always, 15-29 a 2-in-3 roll
        /// then a 4-in-5 roll, 30-59 the 4-in-5 roll alone, 60 or more never.
        /// </summary>
        public static bool MacintiGenesisCandidate(int populationUnits, bool genesisQueued, Resources stock, int concentrationSum, Random random)
        {
            if (populationUnits <= 10000 || genesisQueued)
            {
                return false;
            }

            int leading = 0;
            if (stock.Ironium >= 2000)
            {
                leading++;
                if (stock.Boranium >= 2000)
                {
                    leading++;
                    if (stock.Germanium >= 2000)
                    {
                        leading++;
                    }
                }
            }

            if (leading == 2)
            {
                return false;
            }

            if (concentrationSum < 15)
            {
                return true;
            }

            if (concentrationSum < 30)
            {
                return random.Next(3) < 2 || random.Next(5) < 4;
            }

            if (concentrationSum < 60)
            {
                return random.Next(5) < 4;
            }

            return false;
        }

        /// <summary>The terraformed habitability (`FUN_1048_47ec`, -1..+1) of a reported planet,
        /// treated as untouched, as ColonizationTargetSelector.PassesHabitabilityFilter does.</summary>
        public static double TerraformedHabValue(Race race, StarIntel report)
        {
            Star projected = new Star
            {
                Gravity = report.Gravity,
                Temperature = report.Temperature,
                Radiation = report.Radiation,
                OriginalGravity = report.Gravity,
                OriginalTemperature = report.Temperature,
                OriginalRadiation = report.Radiation,
            };

            return race.HabitalValueAfterTerraform(projected);
        }

        /// <summary>The §17 role kind of an AI-built (tagged) design, or null.</summary>
        public static AiDesignRoleKind? RoleKind(ShipDesign design, int category)
        {
            string tag = AiDesignRoleTag.TagOf(design);
            if (tag == null)
            {
                return null;
            }

            AiDesignRole role = AiDesignRoleTable.ForCategory(category).FirstOrDefault(r => r.Tag == tag);
            return role?.Kind;
        }
    }

    /// <summary>
    /// The per-personality planet passes (behavior-specs-10/ai-opponent-behavior.md §12), run by
    /// DefaultAi over the shuffled planet list after the fleet passes. Each queues the
    /// personality's role designs (§17: AiPlanetPassContext.Role) at the bottom of the queue.
    /// Personality 1's pass is not described by the spec, so it keeps Nova's generic pass
    /// (DefaultPlanetAI.BuildShips). Ship orders go only to planets with a starbase
    /// (production-queue.md: "A ship design (slot 0-15) requires a starbase on the planet").
    /// </summary>
    public static class AiPlanetPasses
    {
        private static readonly string[] RobotoidStrikeTags = { "strike1-a", "strike1-b", "strike1-c", "strike1-d", "strike2-a", "strike2-b" };
        private static readonly string[] MacintiWarshipTags = { "strike1-a", "strike1-b", "strike1-c", "strike2-a", "strike2-b", "strike2-c", "bomber-a", "bomber-b" };

        public static void Run(DefaultPlanetAI planetAI, AiPlanetPassContext context)
        {
            switch (context.Category)
            {
                case AiCategory.Robotoids:
                    Robotoids(planetAI, context);
                    break;
                case AiCategory.Turindrones:
                    planetAI.BuildShips(context.Year);
                    break;
                case AiCategory.Automitrons:
                    Automitrons(planetAI, context);
                    break;
                case AiCategory.Rototills:
                    Rototills(planetAI, context);
                    break;
                case AiCategory.Cybertrons:
                    Cybertrons(planetAI, context);
                    break;
                case AiCategory.Macinti:
                    Macinti(planetAI, context);
                    break;
            }
        }

        /// <summary>
        /// Personality 0 (§12 summary only: "the planet pass queues freighters, colony ships (gate
        /// and isolation check), minelayers four at a time, strike and bomber batches, and line
        /// warships"; the Open Questions say its build rules are "covered only at the level of
        /// §17's quantity column"). Every gate the spec omits uses one neutral convention: a role
        /// is queued at a starbase planet only while the planet's queue holds no order for that
        /// role's current design. Freighters keep Nova's transport quota as the stand-in quota.
        /// The isolation check always passes for personality 0: only personality 5's driver sets
        /// the scratch flags it reads (§2).
        /// </summary>
        private static void Robotoids(DefaultPlanetAI planetAI, AiPlanetPassContext context)
        {
            if (planetAI.Planet.Starbase == null)
            {
                return;
            }

            if (context.Planner.TotalTransportKt < context.Planner.TransportKtRequired)
            {
                QueueUnlessQueued(planetAI, context.Planner.TransportDesign, 1);
            }

            if (context.ColonyGateAllows)
            {
                QueueUnlessQueued(planetAI, context.Planner.ColonizerDesign, 1);
            }

            QueueUnlessQueued(planetAI, context.Role("minelayer"), AiPlanetPassRules.MinelayerBatch);
            QueueUnlessQueued(planetAI, context.Newest(RobotoidStrikeTags), AiPlanetPassRules.RobotoidStrikeBatch);
            QueueUnlessQueued(planetAI, context.Newest("bomber-a", "bomber-b"), AiPlanetPassRules.RobotoidBomberBatch);
            QueueUnlessQueued(planetAI, context.Newest("line-a", "line-b"), AiPlanetPassRules.RobotoidLineBatch);
        }

        /// <summary>
        /// Personality 2 (§12 `:66185`-`66392`): own planets that have a starbase, a stored
        /// population over 1,499 units and no ship design already queued. Freighters by quota;
        /// one colony ship after year 10 when no ship of the slot-1 design exists and some
        /// unowned planet is habitable; three minelayers by the roll rule; four bombers into an
        /// existing, strong-enough group at the planet; slot-9/10 garrison ships up to the cap.
        /// Personality 2's refresh never fills slots 11-12, so that line has no design. Counts
        /// are taken at the start of the run (several planets may each queue one in a turn).
        /// </summary>
        private static void Automitrons(DefaultPlanetAI planetAI, AiPlanetPassContext context)
        {
            Star planet = planetAI.Planet;
            if (planet.Starbase == null || planetAI.PopulationUnits <= 1499 || planetAI.HasShipDesignQueued())
            {
                return;
            }

            Random random = context.Random;
            ShipDesign freighter = context.Newest("freighter-a", "freighter-b");
            if (freighter != null && AiPlanetPassRules.AutomitronWantsFreighter(
                context.Empire.ResearchLevels[TechLevel.ResearchField.Propulsion],
                context.RoleShips(AiFleetRole.Freighter),
                context.OwnPlanetCount,
                context.HubCount,
                random))
            {
                planetAI.QueueShips(freighter, 1);
            }

            ShipDesign colonizer = context.Planner.ColonizerDesign;
            if (context.Year > 10 && colonizer != null && context.ShipsInExistence(colonizer) == 0 && context.HabitableUnownedTargets() > 0)
            {
                planetAI.QueueShips(colonizer, 1);
            }

            ShipDesign minelayer = context.Role("minelayer");
            if (minelayer != null)
            {
                Fleet first = FirstOwnFleetAt(planet, context);
                int layers = first == null ? 0 : AiFleetRoles.Count(AiFleetRoles.CountRoles(first.Composition.Values, context.Category), AiFleetRole.Minelayer);
                planetAI.QueueShips(minelayer, AiPlanetPassRules.AutomitronMinelayers(layers, random));
            }

            ShipDesign bomber = context.Newest("bomber-a", "bomber-b");
            if (bomber != null && StrongBomberGroupAt(planet, context))
            {
                planetAI.QueueShips(bomber, 4);
            }

            ShipDesign garrison = context.Role("garrison");
            if (garrison != null)
            {
                int quantity = AiPlanetPassRules.AutomitronCappedBuild(
                    context.ShipsInExistence(garrison),
                    AiPlanetPassRules.AutomitronGarrisonCap(context.OwnPlanetCount),
                    planetAI.Surplus(),
                    garrison.Cost);
                planetAI.QueueShips(garrison, quantity);
            }
        }

        /// <summary>
        /// Personality 3 (§12): own planets with a starbase, a stored population over 999 units
        /// and no starbase design queued. In year 0 two slot-0 ships; afterwards one slot-1 colony
        /// ship per turn, at the first eligible planet only.
        /// </summary>
        private static void Rototills(DefaultPlanetAI planetAI, AiPlanetPassContext context)
        {
            if (planetAI.Planet.Starbase == null || planetAI.PopulationUnits <= 999 || planetAI.HasStarbaseDesignQueued())
            {
                return;
            }

            if (context.Year == 0)
            {
                planetAI.QueueShips(context.Role("explorer"), 2);
                return;
            }

            if (context.RototillColonyQueued)
            {
                return;
            }

            context.RototillColonyQueued = true;
            ShipDesign colonizer = context.Planner.ColonizerDesign;
            if (colonizer != null && AiPlanetPassRules.RototillWantsColonyShip(context.ShipsInExistence(colonizer), context.HabitableUnownedTargets()))
            {
                planetAI.QueueShips(colonizer, 1);
            }
        }

        /// <summary>
        /// Personality 4 (§12 `:69935`-`70092`): every owned planet whose queue does not already
        /// over-commit a mineral or resources. Terraform; then, at starbase planets that are not
        /// packet hubs (starbase slots other than 1, 3, 6 and 8), colony ships (alternation, under
        /// 40 slot-1 ships; the isolation check always passes for personality 4, §2), one hauler
        /// and four minelayers one time in four; then the warship build chooser.
        /// </summary>
        /// <remarks>
        /// Readings (listed in the report): Terraform goes to the bottom of the queue and is capped
        /// by what the build list still allows; the build chooser runs only at starbase planets.
        /// </remarks>
        private static void Cybertrons(DefaultPlanetAI planetAI, AiPlanetPassContext context)
        {
            Star planet = planetAI.Planet;
            Race race = context.Empire.Race;
            Random random = context.Random;
            if (!planetAI.QueueNotOverCommitted(includeResources: true))
            {
                return;
            }

            int habitability = race.HabPercent(planet);
            int potential = (int)Math.Round(race.HabitalValueAfterTerraform(planet) * 100);
            int terraform = Math.Min(
                AiPlanetPassRules.CybertronTerraform(habitability, potential, planetAI.Surplus().Energy),
                planetAI.TerraformAllowance());
            if (terraform > 0)
            {
                planetAI.QueueItem(new ProductionOrder(terraform, new TerraformProductionUnit(race), false), atTop: false);
            }

            if (planet.Starbase == null)
            {
                return;
            }

            StarbaseSlots slots = StarbaseSlots.ForEmpire(context.Empire);
            if (!StarbaseAdvisors.IsHubSlot(planetAI.StarbaseSlot(slots)))
            {
                ShipDesign colonizer = context.Planner.ColonizerDesign;
                if (colonizer != null)
                {
                    int colonyShips = AiPlanetPassRules.CybertronColonyShips(
                        AiPlanetPassRules.CybertronAlternationBitSet(context.Year),
                        AiPlanetPassRules.GrowthProduct(planetAI.PopulationUnits, race.GrowthRate, race.HasTrait("HE")),
                        context.ShipsInExistence(colonizer),
                        isolationPasses: true,
                        context.Year);
                    planetAI.QueueShips(colonizer, colonyShips);
                }

                ShipDesign hauler = context.Newest("hauler-a", "hauler-b");
                if (context.Role("hauler-a") != null
                    && planetAI.PopulationUnits > 2000
                    && context.RoleShips(AiFleetRole.Freighter) < 50
                    && CybertronHaulerRouter.HasSmallOwnPlanetWithin400(planet.Position, planet.Name, context.Empire))
                {
                    planetAI.QueueShips(hauler, 1);
                }

                ShipDesign minelayer = context.Role("minelayer");
                if (minelayer != null && random.Next(4) == 0)
                {
                    planetAI.QueueShips(minelayer, AiPlanetPassRules.MinelayerBatch);
                }

                // The warship chooser runs only at starbase planets outside the packet-hub slots
                // (1, 3, 6 and 8); a hub starbase never builds warships from it
                // (ai-opponent-behavior.md §12, personality 4).
                CybertronChooser(planetAI, context);
            }
        }

        /// <summary>Runs personality 4's build chooser for one planet and queues its order.</summary>
        private static void CybertronChooser(DefaultPlanetAI planetAI, AiPlanetPassContext context)
        {
            Random random = context.Random;
            ShipDesign groupOne = context.Role("group1-a");
            ShipDesign groupTwo = context.Role("group2-a");
            string group = null;
            if (groupOne != null || groupTwo != null)
            {
                ShipDesign newer = context.Newest("group1-a", "group2-a");
                group = newer == groupOne ? "group1" : "group2";
            }

            ShipDesign guard = context.Newest("guard-a", "guard-b");
            ShipDesign hunter = context.Newest("hunter-a", "hunter-b");

            CybertronBuild build = AiPlanetPassRules.CybertronChooser(
                planetAI.MineRoom() >= 100 && planetAI.FactoryRoom() >= 100,
                group != null,
                context.CybertronBoundWarshipFleets,
                guard != null,
                context.CybertronGuardFleets,
                ForeignPlanetWithin(planetAI.Planet.Position, 300, context.Empire),
                hunter != null,
                context.CybertronUnboundWarshipFleets,
                random);

            switch (build)
            {
                case CybertronBuild.BattleGroup:
                    planetAI.QueueShips(context.Role(group + "-a"), 2);
                    planetAI.QueueShips(context.Role(group + "-b"), 2);
                    if (random.Next(100) < 75)
                    {
                        planetAI.QueueShips(context.Role(group + "-c"), 1);
                    }

                    if (random.Next(100) < 50)
                    {
                        planetAI.QueueShips(context.Role(group + "-d"), 1);
                    }

                    context.CybertronBoundWarshipFleets++;
                    break;

                case CybertronBuild.Guard:
                    planetAI.QueueShips(guard, 1);
                    context.CybertronGuardFleets++;
                    break;

                case CybertronBuild.Hunter:
                    planetAI.QueueShips(hunter, 1);
                    context.CybertronUnboundWarshipFleets++;
                    break;
            }
        }

        /// <summary>
        /// Personality 5 (§12 `:67836`-`68157`): starbase planets with a stored population of 200
        /// units or more, except a starbase in slot 0, and one in slot 1 only before year 26.
        /// Colony ships through the gate with its 8% override, the 30 kT mineral stop and the
        /// isolation check; haulers one time in three up to min(64, planets ÷ 4); miners one time
        /// in two; minelayers four at a time; warships; the packet seam; Genesis Devices.
        /// </summary>
        /// <remarks>
        /// Spec gaps and readings (listed in the report): minelayers and warships name no gate,
        /// so they use personality 0's convention (only while the planet's queue holds no order of
        /// that design) and <see cref="AiPlanetPassRules.MacintiWarshipBatch"/>; the hauler cap
        /// counts hauler ships in existence plus those queued this turn; the 30 kT stop and the
        /// "before year 5 nothing else" stop apply only when the colony line was not skipped;
        /// Terraform after a Genesis Device is capped by what the build list allows.
        /// </remarks>
        private static void Macinti(DefaultPlanetAI planetAI, AiPlanetPassContext context)
        {
            Star planet = planetAI.Planet;
            Race race = context.Empire.Race;
            Random random = context.Random;
            if (planet.Starbase == null || planetAI.PopulationUnits < 200)
            {
                return;
            }

            int slot = planetAI.StarbaseSlot(StarbaseSlots.ForEmpire(context.Empire));
            if (slot == 0 || (slot == 1 && context.Year >= 26))
            {
                return;
            }

            if (!AiPlanetPassRules.MacintiColonyLineSkipped(context.Year, context.ColonyFleetCount, context.ColonyGateAllows, random))
            {
                Resources stock = planet.ResourcesOnHand ?? new Resources();
                if (stock.Ironium < 30 || stock.Boranium < 30 || stock.Germanium < 30)
                {
                    return;
                }

                if (PlanetAdvisors.PassesIsolationCheck(context.Year, planetAI.NearestNonOwnedPlanetDistanceSquared(), random))
                {
                    long product = AiPlanetPassRules.GrowthProduct(planetAI.PopulationUnits, race.GrowthRate, race.HasTrait("HE"));
                    int quantity = 1 + AiPlanetPassRules.MacintiExtraColonyShips(context.Year, product, planet.GetResourceRate(), context.Skill);
                    if (planetAI.QueueShips(context.Planner.ColonizerDesign, quantity))
                    {
                        context.ColonyFleetCount += quantity;
                    }

                    if (context.Year < 5)
                    {
                        return;
                    }
                }
            }

            ShipDesign hauler = context.Newest("hauler-a", "hauler-b");
            if (hauler != null && random.Next(3) == 0
                && context.RoleShips(AiFleetRole.Freighter) + context.MacintiHaulersQueued < Math.Min(64, context.OwnPlanetCount / 4))
            {
                if (planetAI.QueueShips(hauler, 1))
                {
                    context.MacintiHaulersQueued++;
                }
            }

            ShipDesign miner = context.Newest("miner-a", "miner-b");
            if (miner != null && random.Next(2) == 0)
            {
                planetAI.QueueShips(miner, 1);
            }

            QueueUnlessQueued(planetAI, context.Role("minelayer"), AiPlanetPassRules.MinelayerBatch);
            QueueUnlessQueued(planetAI, context.Newest(MacintiWarshipTags), AiPlanetPassRules.MacintiWarshipBatch);
            QueueUnlessQueued(planetAI, context.Newest("line-a", "line-b"), AiPlanetPassRules.MacintiWarshipBatch);

            context.Packets.RunMacintiPlanetPassPackets(planetAI, context.Year, random);

            MacintiGenesis(planetAI, context);
        }

        /// <summary>Personality 5's Genesis Devices (§12): after year 120, once the race could
        /// build a Neutron Shield, up to min(10, planets ÷ 20) planets per turn; each gets one
        /// Genesis Device and then 75 Terraform units at the bottom of the queue.</summary>
        private static void MacintiGenesis(DefaultPlanetAI planetAI, AiPlanetPassContext context)
        {
            if (context.Year <= 120 || context.GenesisBudget <= 0 || !context.Empire.AvailableComponents.ContainsKey("Neutron Shield"))
            {
                return;
            }

            Star planet = planetAI.Planet;
            Resources stock = planet.ResourcesOnHand ?? new Resources();
            Resources concentration = planet.MineralConcentration ?? new Resources();
            int concentrationSum = concentration.Ironium + concentration.Boranium + concentration.Germanium;
            if (!AiPlanetPassRules.MacintiGenesisCandidate(planetAI.PopulationUnits, planetAI.HasGenesisDeviceQueued(), stock, concentrationSum, context.Random))
            {
                return;
            }

            planetAI.QueueItem(new ProductionOrder(1, new GenesisDeviceProductionUnit(context.Empire), false), atTop: false);
            int terraform = Math.Min(75, planetAI.TerraformAllowance());
            if (terraform > 0)
            {
                planetAI.QueueItem(new ProductionOrder(terraform, new TerraformProductionUnit(context.Empire.Race), false), atTop: false);
            }

            context.GenesisBudget--;
        }

        /// <summary>Queues the design unless the planet's queue already holds an order for it (the
        /// neutral convention for the spec's unspecified build gates).</summary>
        private static void QueueUnlessQueued(DefaultPlanetAI planetAI, ShipDesign design, int quantity)
        {
            if (design != null && !planetAI.HasShipOrderFor(design))
            {
                planetAI.QueueShips(design, quantity);
            }
        }

        /// <summary>The first own (non-starbase) fleet in table order orbiting the planet.</summary>
        private static Fleet FirstOwnFleetAt(Star planet, AiPlanetPassContext context)
        {
            return context.Empire.OwnedFleets.Values.FirstOrDefault(fleet =>
                fleet.Owner == context.Empire.Id && !fleet.IsStarbase && fleet.InOrbit != null && fleet.InOrbit.Name == planet.Name);
        }

        /// <summary>
        /// Personality 2's bomber condition: some own fleet at the planet passes the strength
        /// test `FUN_1098_0124` (slots 11-12 plus twice slots 9-10 against K, base 3) and already
        /// holds at least Q bombers (base 3).
        /// </summary>
        private static bool StrongBomberGroupAt(Star planet, AiPlanetPassContext context)
        {
            int k = PlanetAttackHandler.StrengthK(context.Year, PlanetAttackHandler.StrengthBase(context.Category));
            int q = PlanetAttackHandler.BomberQuotaQ(context.Year, PlanetAttackHandler.BomberQuotaBase(context.Category));
            foreach (Fleet fleet in context.Empire.OwnedFleets.Values)
            {
                if (fleet.Owner != context.Empire.Id || fleet.IsStarbase || fleet.InOrbit == null || fleet.InOrbit.Name != planet.Name)
                {
                    continue;
                }

                Dictionary<AiFleetRole, int> roles = AiFleetRoles.CountRoles(fleet.Composition.Values, context.Category);
                int strength = AiFleetRoles.Count(roles, AiFleetRole.StrikeLight) + (2 * AiFleetRoles.Count(roles, AiFleetRole.StrikeHeavy));
                if (strength >= k && AiFleetRoles.Count(roles, AiFleetRole.Bomber) >= q)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Another player's planet lies within the radius (squared distance at most
        /// radius²).</summary>
        private static bool ForeignPlanetWithin(NovaPoint from, int radius, EmpireData empire)
        {
            return empire.StarReports.Values.Any(report =>
                report.Owner != empire.Id
                && report.Owner != Global.Nobody
                && report.Position != null
                && PointUtilities.DistanceSquare(from, report.Position) <= (double)radius * radius);
        }
    }
}
