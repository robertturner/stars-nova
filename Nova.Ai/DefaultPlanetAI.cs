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
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// A helper object for the default AI for managing planets: the planet-level steps of the
    /// original's AI turn, behavior-specs-10/ai-opponent-behavior.md §1 (Overview) and §6, run
    /// by DefaultAi in this order over the shuffled planet list - the personality's planet pass
    /// (<see cref="BuildShips"/>), the per-planet advisor chain (<see cref="RunAdvisorChain"/>),
    /// the bomber-defence pass (<see cref="RunBomberDefence"/>) and the end-of-pass top-up
    /// (<see cref="RunTopUp"/>).
    /// </summary>
    /// <remarks>
    /// The queue is no longer cleared each turn: the original keeps a planet's queue and
    /// subtracts its full cost from what the advisors may spend (§6, top-up "Surplus").
    /// </remarks>
    public class DefaultPlanetAI
    {
        /// <summary>production-queue.md's per-item quantity cap.</summary>
        private const int MaxItemQuantity = 1023;

        private readonly ClientData clientState;
        private readonly DefaultAIPlanner aiPlan;
        private readonly Random random;
        private readonly int category;

        private readonly Star planet;

        /// <summary>
        /// Initializing constructor.
        /// </summary>
        /// <param name="newStar">The planet the ai is to manage.</param>
        /// <param name="category">The spec category the AI plays (AiCategory).</param>
        public DefaultPlanetAI(Star newStar, ClientData newState, DefaultAIPlanner newAIPlan, Random random, int category = AiCategory.Automitrons)
        {
            planet = newStar;
            clientState = newState;
            aiPlan = newAIPlan;
            this.random = random;
            this.category = category;
        }

        public Star Planet
        {
            get { return planet; }
        }

        /// <summary>The spec category this planet's AI plays.</summary>
        public int Category
        {
            get { return category; }
        }

        /// <summary>The skill field (§1a, 0 Easy .. 3 Expert) the packet advisor reads.</summary>
        public int Skill { get; set; } = AiCategory.StandardSkill;

        /// <summary>The mineral-packet seam (§6 shared packet advisor); queues nothing by
        /// default.</summary>
        public IAiPacketAdvisor PacketAdvisor { get; set; } = NoPacketAdvisor.Instance;

        /// <summary>The empire whose planet this is.</summary>
        public EmpireData Empire
        {
            get { return clientState.EmpireState; }
        }

        private Race Race
        {
            get { return clientState.EmpireState.Race; }
        }

        private List<ProductionOrder> Queue
        {
            get { return planet.ManufacturingQueue.Queue; }
        }

        /// <summary>The planet's population in the original's stored units of 100 colonists
        /// (§13).</summary>
        public int PopulationUnits
        {
            get { return planet.Colonists / 100; }
        }

        /// <summary>
        /// Nova's own generic planet pass, kept only for personality 1, whose planet pass the spec
        /// does not describe (behavior-specs-10/ai-opponent-behavior.md Open Questions: "personality
        /// 0's and 1's planet-pass build rules are covered only at the level of §17's quantity
        /// column"; personality 1's column names no production at all). The other personalities run
        /// <see cref="AiPlanetPasses"/>. One scout, colony ship or transport of each kind not
        /// already queued here, at the bottom of the queue. Colony ships need the once-per-run
        /// colony-ship gate (§4, decided by DefaultAi) and, after year 59, the isolation check
        /// (§2, §6).
        /// </summary>
        public void BuildShips(int yearCounter)
        {
            if (this.planet.GetResourceRate() <= DefaultAIPlanner.LowProduction)
            {
                return;
            }

            if (this.aiPlan.ScoutCount < DefaultAIPlanner.EarlyScouts)
            {
                QueueShip(this.aiPlan.ScoutDesign);
            }

            if (this.aiPlan.ColonyShipGateAllows
                && PlanetAdvisors.PassesIsolationCheck(yearCounter, NearestNonOwnedPlanetDistanceSquared(), random))
            {
                QueueShip(this.aiPlan.ColonizerDesign);
            }

            if (this.aiPlan.TotalTransportKt < this.aiPlan.TransportKtRequired)
            {
                QueueShip(this.aiPlan.TransportDesign);
            }
        }

        /// <summary>
        /// The end-of-pass routine `FUN_1090_59e6` for this planet (§3, §6): first the
        /// starbase-building advisor (<see cref="RunStarbaseBuilding"/>, which has its own gates),
        /// then the per-planet advisor chain: population gate, queue affordability gate (not for
        /// category 5), then the advisors in order, the first that queues ending the chain -
        /// starbase upgrade, packets (<see cref="IAiPacketAdvisor.RunSharedAdvisor"/>), scanner,
        /// Defenses, Terraform. The scanner advisor can never fire. A
        /// planet with the urgent-supply flag (categories 1-3: negative habitability) skips
        /// straight to the Terraform advisor.
        /// </summary>
        /// <remarks>
        /// The original runs the building advisor over every planet before the chain; it neither
        /// draws random numbers nor reads other planets' queues, so running it planet by planet
        /// here decides the same.
        /// </remarks>
        public void RunAdvisorChain()
        {
            StarbaseSlots slots = StarbaseSlots.ForEmpire(clientState.EmpireState);
            bool urgentSupply = FreighterRoutingSelector.HasUrgentSupplyFlag(category, planet, Race);

            RunStarbaseBuilding(slots, urgentSupply);

            if (!PlanetAdvisors.PassesAdvisorPopulationGate(category, PopulationUnits, urgentSupply))
            {
                return;
            }

            if (category != AiCategory.Macinti && !QueueIsAffordable())
            {
                return;
            }

            bool queued = false;
            if (!urgentSupply)
            {
                queued = RunStarbaseUpgrade(slots);
            }

            // §6 the shared packet advisor `FUN_1090_4d10` (returns at once for category 4),
            // through the packet seam (IAiPacketAdvisor; AiPacketAdvisor in a real run).
            if (!queued && !urgentSupply && category != AiCategory.Cybertrons)
            {
                queued = PacketAdvisor.RunSharedAdvisor(this, category, Skill, random);
            }

            if (!queued && !urgentSupply)
            {
                int defenses = PlanetAdvisors.DefensesAdvisor(
                    PopulationUnits,
                    planet.Defenses,
                    planet.GetMaxDefenses(),
                    QueuedQuantity<DefenseProductionUnit>(manualOnly: true),
                    HasManual<DefenseProductionUnit>());

                queued = defenses > 0 && Insert(new ProductionOrder(defenses, new DefenseProductionUnit(Race), false), atTop: false);
            }

            if (!queued && category != AiCategory.Robotoids)
            {
                int terraform = PlanetAdvisors.TerraformAdvisor(
                    category,
                    PopulationUnits,
                    HasManual<TerraformProductionUnit>(),
                    AnyAxisOffIdeal(planet, Race),
                    TerraformHeadroom(planet, Race),
                    QueuedQuantity<TerraformProductionUnit>(manualOnly: false));

                if (terraform > 0)
                {
                    Insert(new ProductionOrder(terraform, new TerraformProductionUnit(Race), false), atTop: false);
                }
            }
        }

        /// <summary>
        /// The starbase-building advisor `FUN_1090_510a` (§3). Categories 0-3: a freighter hub
        /// (from turn 20, §5) with no starbase, or one outside the ten starbase slots, a population
        /// above 79 units and no urgent-supply flag gets one starbase from the default slot D.
        /// Category 4: the planet test `FUN_10a8_2bf8` (no starbase, habitability above 14,
        /// population above 499 units; slot 5 or 6 by mineral concentration). Categories 5 and 7:
        /// nothing. Never when a starbase is already queued. The tutorial cut-off after year 30 has
        /// no Nova counterpart. Queued at the bottom, like the chain's advisors.
        /// </summary>
        public void RunStarbaseBuilding()
        {
            RunStarbaseBuilding(
                StarbaseSlots.ForEmpire(clientState.EmpireState),
                FreighterRoutingSelector.HasUrgentSupplyFlag(category, planet, Race));
        }

        private void RunStarbaseBuilding(StarbaseSlots slots, bool urgentSupply)
        {
            int slot;
            if (category == AiCategory.Cybertrons)
            {
                Resources concentration = planet.MineralConcentration ?? new Resources();
                slot = StarbaseAdvisors.PlanetTestSlot(
                    planet.Starbase != null,
                    Race.HabPercent(planet),
                    PopulationUnits,
                    concentration.Ironium > 15 && concentration.Boranium > 15 && concentration.Germanium > 15,
                    StarbaseQueued(),
                    slots);
            }
            else
            {
                slot = StarbaseAdvisors.HubStarbaseSlot(
                    category,
                    IsFreighterHub(YearCounter),
                    slots.SlotOf(StarbaseDesign()) != StarbaseAdvisors.NoSlot,
                    PopulationUnits,
                    urgentSupply,
                    StarbaseQueued(),
                    slots.DefaultSlot());
            }

            QueueStarbase(slots, slot);
        }

        /// <summary>
        /// The starbase-upgrade advisor `FUN_1090_52da` (§6), the chain's first advisor, on the
        /// <see cref="StarbaseSlots"/> stand-in. Design ages come from the AI's creation-turn
        /// name suffix (<see cref="AiDesignRoles.CreationYear"/>). The other-generation branch may
        /// name an empty slot: the original queues it and the order is refused when it completes;
        /// here nothing is queued and the chain goes on.
        /// </summary>
        private bool RunStarbaseUpgrade(StarbaseSlots slots)
        {
            if (category == AiCategory.Macinti)
            {
                return RunMacintiStarbaseUpgrade(slots);
            }

            if (planet.Starbase == null)
            {
                return false;
            }

            int turnYear = clientState.EmpireState.TurnYear;
            Resources stock = planet.ResourcesOnHand ?? new Resources();
            bool minerals = stock.Ironium >= StarbaseAdvisors.UpgradeMineralFloor
                && stock.Boranium >= StarbaseAdvisors.UpgradeMineralFloor
                && stock.Germanium >= StarbaseAdvisors.UpgradeMineralFloor;

            int slot = StarbaseAdvisors.UpgradeSlot(
                category,
                YearCounter,
                slots.SlotOf(StarbaseDesign()),
                StarbaseQueued(),
                slots,
                turnYear - slots.CreationYear(slots.DefaultSlot()),
                turnYear - slots.CreationYear(slots.HubBase()),
                minerals,
                random);

            return QueueStarbase(slots, slot);
        }

        /// <summary>
        /// Category 5's branch of the starbase-upgrade advisor (§6): the queue test applies only
        /// when the planet has a starbase; the urgency marks come from
        /// <see cref="StarbaseAdvisors.MacintiMarks"/> over the ten-slot stand-in. A planet with no
        /// starbase "works from whatever slot number the planet record holds"; Nova keeps no such
        /// number, so it is read as slot 0 (assumption: the stored nibble of a planet that never
        /// had a starbase). A starbase outside the ten slots gets nothing. An upgrade naming an
        /// empty slot queues nothing (the original's order would be refused on completion).
        /// </summary>
        private bool RunMacintiStarbaseUpgrade(StarbaseSlots slots)
        {
            int slot = 0;
            if (planet.Starbase != null)
            {
                if (StarbaseQueued())
                {
                    return false;
                }

                slot = slots.SlotOf(StarbaseDesign());
                if (slot == StarbaseAdvisors.NoSlot)
                {
                    return false;
                }
            }

            int[] marks = MacintiMarks(slots);
            int percent = StarbaseAdvisors.PopulationPercent(planet.Colonists, planet.CapacityColonists(Race));
            return QueueStarbase(slots, StarbaseAdvisors.MacintiUpgradeSlot(slot, marks, percent, random));
        }

        /// <summary>Category 5's urgency marks this turn, from the slots' designs, their ages,
        /// whether any starbase of each exists, and their chassis.</summary>
        private int[] MacintiMarks(StarbaseSlots slots)
        {
            int count = StarbaseSlots.SlotCount;
            bool[] live = new bool[count];
            int[] ages = new int[count];
            bool[] inUse = new bool[count];
            int[] chassis = new int[count];
            int turnYear = clientState.EmpireState.TurnYear;

            for (int slot = 0; slot < count; slot++)
            {
                ShipDesign design = slots[slot];
                live[slot] = design != null;
                if (design == null)
                {
                    chassis[slot] = -1;
                    continue;
                }

                ages[slot] = turnYear - slots.CreationYear(slot);
                chassis[slot] = StarbaseAdvisors.ChassisIndex(design.Blueprint?.Name);
                inUse[slot] = clientState.EmpireState.OwnedStars.Values.Any(star =>
                    star.Owner == clientState.EmpireState.Id
                    && star.Starbase != null
                    && star.Starbase.Composition != null
                    && star.Starbase.Composition.Values.Any(token => token.Design != null && token.Design.Key == design.Key));
            }

            return StarbaseAdvisors.MacintiMarks(YearCounter, live, ages, inUse, chassis);
        }

        private bool QueueStarbase(StarbaseSlots slots, int slot)
        {
            ShipDesign design = slots[slot];
            if (design == null)
            {
                return false;
            }

            return Insert(new ProductionOrder(1, new ShipProductionUnit(design), false), atTop: false);
        }

        /// <summary>The year counter (0 in the first year).</summary>
        private int YearCounter
        {
            get { return clientState.EmpireState.TurnYear - Global.StartingYear; }
        }

        /// <summary>The design of the planet's starbase, or null.</summary>
        private ShipDesign StarbaseDesign()
        {
            if (planet.Starbase == null || planet.Starbase.Composition == null)
            {
                return null;
            }

            return planet.Starbase.Composition.Values
                .Where(token => token.Design != null)
                .Select(token => token.Design)
                .FirstOrDefault();
        }

        /// <summary>The queue already holds a starbase design.</summary>
        private bool StarbaseQueued()
        {
            return Queue.Any(order => order.Unit is ShipProductionUnit unit
                && clientState.EmpireState.Designs.TryGetValue(unit.DesignKey, out ShipDesign design)
                && AiDesignRoles.IsStarbaseDesign(design));
        }

        /// <summary>
        /// Whether this planet is one of the AI's freighter hubs (§5 "Hubs", rebuilt statelessly
        /// per §16 exactly as FreighterRoutingSelector does): from turn 20, every owned starbase
        /// planet, then every owned planet without one that meets the hub thresholds, category 0
        /// leaving out any within 50 ly of an earlier hub.
        /// </summary>
        private bool IsFreighterHub(int yearCounter)
        {
            if (yearCounter < 20)
            {
                return false;
            }

            if (planet.Starbase != null)
            {
                return true;
            }

            List<Star> owned = clientState.EmpireState.OwnedStars.Values
                .Where(star => star.Owner == clientState.EmpireState.Id)
                .ToList();
            List<Star> hubs = owned.Where(star => star.Starbase != null).ToList();

            foreach (Star star in owned.Where(star => star.Starbase == null))
            {
                if (!FreighterRoutingSelector.QualifiesAsHub(star))
                {
                    continue;
                }

                if (category == AiCategory.Robotoids
                    && hubs.Any(hub => PointUtilities.DistanceSquare(hub.Position, star.Position) <= 50 * 50))
                {
                    continue;
                }

                if (star.Name == planet.Name)
                {
                    return true;
                }

                hubs.Add(star);
            }

            return false;
        }

        /// <summary>
        /// The bomber-defence pass `FUN_1090_420c` (§6): every category, no population gate,
        /// from turn (galaxy-size index + 2) × 10, on a planet a foreign bomber fleet orbits.
        /// Defenses (and the Mineral Alchemy paying for the extra ones) go to the top.
        /// </summary>
        public void RunBomberDefence(int yearCounter, int galaxySizeIndex, bool foreignBomberInOrbit)
        {
            if (!foreignBomberInOrbit || yearCounter < PlanetAdvisors.BomberDefenceFirstTurn(galaxySizeIndex))
            {
                return;
            }

            Resources projected = ProjectedMinerals();
            int cap = planet.GetMaxDefenses() - planet.Defenses - QueuedQuantity<DefenseProductionUnit>(manualOnly: true);

            (int defenses, int alchemy) = PlanetAdvisors.BomberDefence(
                planet.GetResourceRate(),
                projected.Ironium,
                projected.Boranium,
                projected.Germanium,
                cap,
                HasManual<DefenseProductionUnit>(),
                Race.HasTrait("MA"));

            if (defenses > 0)
            {
                Insert(new ProductionOrder(defenses, new DefenseProductionUnit(Race), false), atTop: true);
            }

            if (alchemy > 0)
            {
                Insert(new ProductionOrder(alchemy, new AlchemyProductionUnit(Race), false), atTop: true);
            }
        }

        /// <summary>
        /// The end-of-pass top-up `FUN_10a8_1e8a` (§6): Factories to the bottom, Mines to the top,
        /// Mineral Alchemy late in the game and, for category 5 only, Terraform first, all out of
        /// the planet's surplus after the full cost of its existing queue.
        /// </summary>
        public void RunTopUp(int yearCounter)
        {
            Resources projected = ProjectedMinerals();
            Resources committed = QueueCost();

            int mineCap = planet.GetOperableMines();
            int mineRoom = Math.Max(0, mineCap - Math.Min(planet.Mines, mineCap) - QueuedQuantity<MineProductionUnit>(manualOnly: true));
            int factoryCap = planet.GetOperableFactories();
            int factoryRoom = Math.Max(0, factoryCap - Math.Min(planet.Factories, factoryCap) - QueuedQuantity<FactoryProductionUnit>(manualOnly: true));

            Resources factoryCost = Race.GetFactoryResources();
            int terraformAllowance = Math.Max(0, TerraformHeadroom(planet, Race) - QueuedQuantity<TerraformProductionUnit>(manualOnly: false));

            TopUpResult result = PlanetAdvisors.TopUp(
                category,
                (long)projected.Ironium - committed.Ironium,
                (long)projected.Boranium - committed.Boranium,
                (long)projected.Germanium - committed.Germanium,
                (long)planet.GetResourceRate() - committed.Energy,
                factoryRoom,
                mineRoom,
                factoryCost.Energy,
                factoryCost.Germanium,
                Race.GetMineResources().Energy,
                new AlchemyProductionUnit(Race).Cost.Energy,
                terraformAllowance,
                Queue.Count > 0 && Queue[0].Unit is AlchemyProductionUnit,
                AllTechsAt26(),
                yearCounter);

            if (result.Terraform > 0)
            {
                Insert(new ProductionOrder(result.Terraform, new TerraformProductionUnit(Race), false), atTop: true);
            }

            if (result.Factories > 0)
            {
                Insert(new ProductionOrder(result.Factories, new FactoryProductionUnit(Race), false), atTop: false);
            }

            if (result.Mines > 0)
            {
                Insert(new ProductionOrder(result.Mines, new MineProductionUnit(Race), false), atTop: true);
            }

            if (result.Alchemy > 0)
            {
                Insert(new ProductionOrder(result.Alchemy, new AlchemyProductionUnit(Race), false), atTop: result.AlchemyAtTop);
            }
        }

        /// <summary>
        /// Remaining terraform headroom: the 1% steps still possible on each axis, each limited by
        /// the distance to the race's ideal and by the flat terraform allowance measured from the
        /// original environment. This uses TerraformProductionUnit's own rule as the proxy for
        /// the original's `FUN_1048_537e` (Nova has no tech-gated terraform cap); immune axes are
        /// never terraformed, and 0 is returned whenever TerraformProductionUnit.
        /// SelectAxisToImprove finds nothing to improve.
        /// </summary>
        public static int TerraformHeadroom(Star star, Race race)
        {
            if (TerraformProductionUnit.SelectAxisToImprove(star, race) == null)
            {
                return 0;
            }

            int maxPercent = TerraformProductionUnit.MaxTerraformPercent(race);
            return AxisHeadroom(race.GravityTolerance, star.Gravity, star.OriginalGravity, maxPercent)
                + AxisHeadroom(race.TemperatureTolerance, star.Temperature, star.OriginalTemperature, maxPercent)
                + AxisHeadroom(race.RadiationTolerance, star.Radiation, star.OriginalRadiation, maxPercent);
        }

        /// <summary>
        /// The Terraform advisor's axis test (§6): some axis differs from the race's ideal. An
        /// immune axis always counts as different, as in the original (its stored centre is the
        /// immune marker).
        /// </summary>
        public static bool AnyAxisOffIdeal(Star star, Race race)
        {
            return AxisOff(race.GravityTolerance, star.Gravity)
                || AxisOff(race.TemperatureTolerance, star.Temperature)
                || AxisOff(race.RadiationTolerance, star.Radiation);
        }

        private static bool AxisOff(EnvironmentTolerance tolerance, int value)
        {
            return tolerance.Immune || value != tolerance.OptimumLevel;
        }

        private static int AxisHeadroom(EnvironmentTolerance tolerance, int current, int original, int maxPercent)
        {
            if (tolerance.Immune)
            {
                return 0;
            }

            int distanceToIdeal = Math.Abs(current - tolerance.OptimumLevel);
            int remaining = Math.Max(0, maxPercent - Math.Abs(current - original));
            return Math.Min(distanceToIdeal, remaining);
        }

        /// <summary>Surface stock plus this year's mining, per mineral (§6 "projected
        /// availability").</summary>
        private Resources ProjectedMinerals()
        {
            Resources stock = planet.ResourcesOnHand ?? new Resources();
            Resources concentration = planet.MineralConcentration ?? new Resources();
            return new Resources(
                stock.Ironium + planet.GetMiningRate(concentration.Ironium),
                stock.Boranium + planet.GetMiningRate(concentration.Boranium),
                stock.Germanium + planet.GetMiningRate(concentration.Germanium),
                0);
        }

        /// <summary>The full cost of everything already in the planet's queue
        /// (`FUN_1090_3604`).</summary>
        private Resources QueueCost()
        {
            Resources total = new Resources();
            foreach (ProductionOrder order in Queue)
            {
                total = total + order.NeededResources();
            }

            return total;
        }

        /// <summary>§6 queue-affordability gate: the queue's full cost exceeds the projected stock
        /// of some mineral. Resources are not compared.</summary>
        private bool QueueIsAffordable()
        {
            Resources projected = ProjectedMinerals();
            Resources committed = QueueCost();
            return committed.Ironium <= projected.Ironium
                && committed.Boranium <= projected.Boranium
                && committed.Germanium <= projected.Germanium;
        }

        private bool HasManual<T>()
            where T : IProductionUnit
        {
            return Queue.Any(order => order.Unit is T && !order.IsAutoBuild);
        }

        private int QueuedQuantity<T>(bool manualOnly)
            where T : IProductionUnit
        {
            return Queue
                .Where(order => order.Unit is T && (!manualOnly || !order.IsAutoBuild))
                .Sum(order => order.Quantity);
        }

        private bool AllTechsAt26()
        {
            TechLevel levels = clientState.EmpireState.ResearchLevels;
            for (TechLevel.ResearchField field = TechLevel.FirstField; field <= TechLevel.LastField; field++)
            {
                if (levels[field] < 26)
                {
                    return false;
                }
            }

            return true;
        }

        // ================================================================ planet-pass helpers
        // Used by AiPlanetPasses (the per-personality planet passes of §12).

        /// <summary>The queue holds an order for this design.</summary>
        public bool HasShipOrderFor(ShipDesign design)
        {
            return design != null && Queue.Any(order => order.Unit is ShipProductionUnit unit && unit.DesignKey == design.Key);
        }

        /// <summary>The queue holds an order for a ship (not starbase) design.</summary>
        public bool HasShipDesignQueued()
        {
            return Queue.Any(order => order.Unit is ShipProductionUnit unit
                && clientState.EmpireState.Designs.TryGetValue(unit.DesignKey, out ShipDesign design)
                && !AiDesignRoles.IsStarbaseDesign(design));
        }

        /// <summary>The queue holds a starbase design.</summary>
        public bool HasStarbaseDesignQueued()
        {
            return StarbaseQueued();
        }

        /// <summary>Queues <paramref name="quantity"/> ships of the design at the bottom of the
        /// queue (`FUN_1090_2736` position 1), after switching any order for a design it
        /// supersedes over to it. False when nothing was queued.</summary>
        public bool QueueShips(ShipDesign design, int quantity)
        {
            if (design == null || quantity <= 0)
            {
                return false;
            }

            ReplaceSupersededOrders(design);
            return Insert(new ProductionOrder(quantity, new ShipProductionUnit(design), false), atTop: false);
        }

        /// <summary>Inserts any order at the top or the bottom (`FUN_1090_2736`).</summary>
        public bool QueueItem(ProductionOrder order, bool atTop)
        {
            return Insert(order, atTop);
        }

        /// <summary>The planet's surplus (§6 top-up "Surplus"): surface stock plus this year's
        /// mining per mineral, and this year's resources, minus the full cost of the queue.</summary>
        public Resources Surplus()
        {
            Resources projected = ProjectedMinerals();
            Resources committed = QueueCost();
            return new Resources(
                projected.Ironium - committed.Ironium,
                projected.Boranium - committed.Boranium,
                projected.Germanium - committed.Germanium,
                planet.GetResourceRate() - committed.Energy);
        }

        /// <summary>The queue does not over-commit any mineral or (when asked) resources
        /// (personality 4's planet-pass coverage, §12).</summary>
        public bool QueueNotOverCommitted(bool includeResources)
        {
            Resources surplus = Surplus();
            return surplus.Ironium >= 0 && surplus.Boranium >= 0 && surplus.Germanium >= 0
                && (!includeResources || surplus.Energy >= 0);
        }

        /// <summary>Mine room as in the top-up: the operable-mine cap minus the built mines (at
        /// most the cap) minus the queued manual Mines, floored at 0.</summary>
        public int MineRoom()
        {
            int cap = planet.GetOperableMines();
            return Math.Max(0, cap - Math.Min(planet.Mines, cap) - QueuedQuantity<MineProductionUnit>(manualOnly: true));
        }

        /// <summary>Factory room, computed the same way.</summary>
        public int FactoryRoom()
        {
            int cap = planet.GetOperableFactories();
            return Math.Max(0, cap - Math.Min(planet.Factories, cap) - QueuedQuantity<FactoryProductionUnit>(manualOnly: true));
        }

        /// <summary>The Terraform Environment units the build list still allows: the terraform
        /// headroom minus what is queued.</summary>
        public int TerraformAllowance()
        {
            return Math.Max(0, TerraformHeadroom(planet, Race) - QueuedQuantity<TerraformProductionUnit>(manualOnly: false));
        }

        /// <summary>The planet's starbase slot in the ten-slot stand-in, or NoSlot (no starbase,
        /// or one outside the ten).</summary>
        public int StarbaseSlot(StarbaseSlots slots)
        {
            return slots.SlotOf(StarbaseDesign());
        }

        // ================================================================ packet helpers
        // Used by AiPacketAdvisor (§6 shared packet advisor, §12 personalities 4 and 5).

        /// <summary>Surface stock plus this year's mining, per mineral (§6 "projected").</summary>
        public Resources ProjectedMineralStock()
        {
            return ProjectedMinerals();
        }

        /// <summary>The queue already holds a manual packet item (types 14-17).</summary>
        public bool HasPacketItemQueued()
        {
            return Queue.Any(order => order.Unit is PacketProductionUnit unit && !unit.AutoBuild && !order.IsAutoBuild);
        }

        /// <summary>Queues <paramref name="count"/> manual packets of the mineral (types 14-17)
        /// at the top or the bottom of the queue.</summary>
        public bool QueuePackets(PacketMineral mineral, int count, bool atTop)
        {
            return count > 0 && Insert(new ProductionOrder(count, new PacketProductionUnit(Race, mineral, false), false), atTop);
        }

        /// <summary>Sets the planet's packet destination and chosen speed through an ordinary
        /// PacketDestinationCommand (needs a mass driver and a known destination).</summary>
        public bool SetPacketDestination(string destination, int warp)
        {
            PacketDestinationCommand command = new PacketDestinationCommand(planet.Key, destination, warp);
            if (!command.IsValid(clientState.EmpireState))
            {
                return false;
            }

            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
            return true;
        }

        /// <summary>The queue holds a Genesis Device.</summary>
        public bool HasGenesisDeviceQueued()
        {
            return Queue.Any(order => order.Unit is GenesisDeviceProductionUnit);
        }

        /// <summary>The distance (squared) to the nearest planet the AI does not own, the
        /// isolation check's measure (§2).</summary>
        public double NearestNonOwnedPlanetDistanceSquared()
        {
            double nearest = double.MaxValue;
            foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
            {
                if (report.Owner == clientState.EmpireState.Id || report.Name == planet.Name)
                {
                    continue;
                }

                nearest = Math.Min(nearest, PointUtilities.DistanceSquare(planet.Position, report.Position));
            }

            return nearest;
        }

        /// <summary>Queues one ship of the design at the bottom unless one is already queued
        /// here (the queue now persists between turns). Orders for a design it supersedes are
        /// first switched over to it (<see cref="ReplaceSupersededOrders"/>).</summary>
        private void QueueShip(ShipDesign design)
        {
            if (design == null)
            {
                return;
            }

            ReplaceSupersededOrders(design);

            bool alreadyQueued = Queue.Any(order => order.Unit is ShipProductionUnit unit && unit.DesignKey == design.Key);
            if (!alreadyQueued)
            {
                Insert(new ProductionOrder(1, new ShipProductionUnit(design), false), atTop: false);
            }
        }

        /// <summary>
        /// The planet-queue side of replacing a design (ai-opponent-behavior.md §7 "replaced
        /// immediately", §17): the original writes a new design over its slot and production items
        /// name the slot, so a queued order builds the new design from then on and the old design
        /// is "no longer built". Nova's orders name a design key, so each queued order for a design
        /// <paramref name="current"/> supersedes (<see cref="AiDesignRoles.Supersedes"/>) is
        /// replaced in place by an order for <paramref name="current"/> of the same quantity and
        /// auto-build flag. Unlike the original, the old order's partial progress is not carried
        /// over.
        /// </summary>
        public void ReplaceSupersededOrders(ShipDesign current)
        {
            if (current == null)
            {
                return;
            }

            for (int index = 0; index < Queue.Count; index++)
            {
                ProductionOrder old = Queue[index];
                if (!(old.Unit is ShipProductionUnit unit) || unit.DesignKey == current.Key)
                {
                    continue;
                }

                if (!clientState.EmpireState.Designs.TryGetValue(unit.DesignKey, out ShipDesign queuedDesign)
                    || !AiDesignRoles.Supersedes(current, queuedDesign))
                {
                    continue;
                }

                ProductionOrder replacement = new ProductionOrder(old.Quantity, new ShipProductionUnit(current), old.IsAutoBuild);
                ProductionCommand delete = new ProductionCommand(CommandMode.Delete, old, this.planet.Key, index);
                ProductionCommand add = new ProductionCommand(CommandMode.Add, replacement, this.planet.Key, index);
                if (!delete.IsValid(clientState.EmpireState) || !add.IsValid(clientState.EmpireState))
                {
                    continue;
                }

                delete.ApplyToState(clientState.EmpireState);
                clientState.Commands.Push(delete);
                add.ApplyToState(clientState.EmpireState);
                clientState.Commands.Push(add);
            }
        }

        /// <summary>
        /// Inserts an order at the top (position 0) or the bottom of the queue, as
        /// `FUN_1090_2736` does, unless the queue already holds the 200-item maximum (§6).
        /// </summary>
        private bool Insert(ProductionOrder order, bool atTop)
        {
            if (order.Quantity <= 0 || Queue.Count >= PlanetAdvisors.MaxQueuedItems)
            {
                return false;
            }

            order.Quantity = Math.Min(order.Quantity, MaxItemQuantity);

            int index = atTop ? 0 : Queue.Count;
            ProductionCommand command = new ProductionCommand(CommandMode.Add, order, this.planet.Key, index);
            if (!command.IsValid(clientState.EmpireState))
            {
                return false;
            }

            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
            return true;
        }
    }
}
