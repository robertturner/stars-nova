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

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The seam for the AI's mineral-packet rules (behavior-specs-10/ai-opponent-behavior.md).
    /// The three call sites (DefaultAi.PacketAdvisor):
    /// <list type="bullet">
    /// <item>§6 the shared per-planet packet advisor `FUN_1090_4d10`, the second advisor of the
    /// end-of-pass chain (after the starbase upgrade, before Defenses). It never runs for
    /// personality 4 (the caller does not call it then).</item>
    /// <item>§12 personality 4's own packet routine `FUN_10a8_123e` (hub supply, packet attack,
    /// border fallback), run after the end-of-pass routine and before the top-up, over the
    /// starbase planets in the shuffled planet order, with the per-planet packet word of §16.</item>
    /// <item>§12 personality 5's planet-pass bulk transfer (after year 120, one time in four,
    /// warp-10 drivers, own targets within about 160 ly).</item>
    /// </list>
    /// <see cref="AiPacketAdvisor"/> implements them; <see cref="NoPacketAdvisor"/> queues nothing.
    /// </summary>
    public interface IAiPacketAdvisor
    {
        /// <summary>§6 `FUN_1090_4d10` for one planet; true when it queued something (which
        /// ends that planet's advisor chain).</summary>
        bool RunSharedAdvisor(DefaultPlanetAI planet, int category, int skill, Random random);

        /// <summary>§12 personality 4's packet routine `FUN_10a8_123e`.</summary>
        void RunCybertronPackets(IReadOnlyList<DefaultPlanetAI> shuffledPlanets, int skill, int galaxySizeIndex, Random random);

        /// <summary>§12 personality 5's planet-pass packets, for one covered planet.</summary>
        void RunMacintiPlanetPassPackets(DefaultPlanetAI planet, int year, Random random);
    }

    /// <summary>A packet seam that queues nothing and draws no random numbers.</summary>
    public sealed class NoPacketAdvisor : IAiPacketAdvisor
    {
        public static readonly NoPacketAdvisor Instance = new NoPacketAdvisor();

        public bool RunSharedAdvisor(DefaultPlanetAI planet, int category, int skill, Random random)
        {
            return false;
        }

        public void RunCybertronPackets(IReadOnlyList<DefaultPlanetAI> shuffledPlanets, int skill, int galaxySizeIndex, Random random)
        {
        }

        public void RunMacintiPlanetPassPackets(DefaultPlanetAI planet, int year, Random random)
        {
        }
    }

    /// <summary>
    /// Personality 4's packet words (§16 category-4 bookkeeping), one per planet name. The
    /// original keeps them in the AI memory block of the history file. Nova's AI process keeps
    /// nothing between turns, so DefaultAi uses <see cref="FromPacketsInFlight"/> every run
    /// (§16's stateless fallback); a caller that can persist an instance across runs gets the
    /// original's behaviour exactly (call <see cref="BeginRun"/> at the start of each run).
    /// </summary>
    public sealed class CybertronPacketMemory
    {
        private readonly Dictionary<string, int> words = new Dictionary<string, int>();

        public int Word(string planet)
        {
            return planet != null && words.TryGetValue(planet, out int word) ? word : 0;
        }

        public void SetWord(string planet, int word)
        {
            if (planet != null)
            {
                words[planet] = word & 0xff;
            }
        }

        public int CoolDown(string planet)
        {
            return CybertronPacketWord.CoolDown(Word(planet));
        }

        public void SetCoolDown(string planet, int coolDown)
        {
            SetWord(planet, CybertronPacketWord.WithCoolDown(Word(planet), coolDown));
        }

        /// <summary>The start of a category-4 run: every nonzero cool-down drops by 1
        /// (`:69590`-`69591`).</summary>
        public void BeginRun()
        {
            foreach (string planet in words.Keys.ToList())
            {
                int coolDown = CybertronPacketWord.CoolDown(words[planet]);
                if (coolDown > 0)
                {
                    words[planet] = CybertronPacketWord.WithCoolDown(words[planet], coolDown - 1);
                }
            }
        }

        /// <summary>
        /// §16's stateless fallback: a block as freshly zeroed (direction index 0, no rest or
        /// follow-up flags), except that every planet not owned by the AI that one of its packets
        /// in flight is bound for is treated as cooling down ("treat any planet that received
        /// packets within the last three turns as ineligible (derivable from packets in
        /// flight)"). Own planets get no cool-down: hub supply never sets one.
        /// </summary>
        public static CybertronPacketMemory FromPacketsInFlight(EmpireData empire)
        {
            CybertronPacketMemory memory = new CybertronPacketMemory();
            if (empire?.MineralPacketReports == null)
            {
                return memory;
            }

            foreach (MineralPacket packet in empire.MineralPacketReports.Values)
            {
                if (packet == null || packet.Owner != empire.Id || string.IsNullOrEmpty(packet.TargetName)
                    || empire.OwnedStars.Contains(packet.TargetName))
                {
                    continue;
                }

                memory.SetCoolDown(packet.TargetName, 1);
            }

            return memory;
        }
    }

    /// <summary>
    /// The AI's mineral-packet rules (behavior-specs-10/ai-opponent-behavior.md §6, §12, §16),
    /// issued through ordinary player orders: PacketProductionUnit items (types 14-17) and a
    /// PacketDestinationCommand for the planet's packet destination and chosen speed.
    /// </summary>
    /// <remarks>
    /// Stand-ins, as elsewhere in Nova.Ai: Nova's planet reports carry no 4-bit coarse defence
    /// value, so c = 0; no report tells whether an owner is Alternate Reality or Packet
    /// Physics, so those exclusions always pass; the nearest-object finder (`FUN_1038_2988`, class
    /// mask 0x21) is the nearest planet report; the reported population figure is the report's
    /// colonists ÷ 400 (§13 note). The §6 target range table is a SPEC GAP
    /// (<see cref="AiPacketRules.SharedRangeSquaredByRating"/>, no limit by default).
    /// </remarks>
    public sealed class AiPacketAdvisor : IAiPacketAdvisor
    {
        /// <summary>Nova map coordinates start at 0; the original's frame subtracts 1,000 from
        /// its map coordinates (§12 chooser "Frame"), which start at 1,000.</summary>
        public const int MapOrigin = 0;

        /// <summary>A persisted personality-4 memory, or null for §16's stateless fallback
        /// rebuilt each run.</summary>
        public CybertronPacketMemory Memory { get; set; }

        // ================================================================ §6 shared advisor

        /// <summary>
        /// `FUN_1090_4d10` (§6): skill 2+, no packet item queued, projected I + B + G above
        /// 3,000 kT, best mass-driver warp 10+, then a 1-in-4 roll; a random target (reservoir
        /// sampling) among other players' planets with a report at most two years old that are
        /// weak enough, within the range set by the doubled-driver flag (84 ly, 225 ly doubled,
        /// tripled when a surface mineral exceeds 12,500 kT); the destination and its speed field
        /// are set (warp 13) and the packets go to the bottom of the queue. The advisor counts as
        /// acted once a target is drawn. Never for personality 4.
        /// </summary>
        public bool RunSharedAdvisor(DefaultPlanetAI planet, int category, int skill, Random random)
        {
            if (planet == null || category == AiCategory.Cybertrons)
            {
                return false;
            }

            Star star = planet.Planet;
            Resources projected = planet.ProjectedMineralStock();
            int bestDriverWarp = MineralPacketRules.BestDriverWarp(star.Starbase);
            if (!AiPacketRules.SharedAdvisorGates(skill, planet.HasPacketItemQueued(), projected, bestDriverWarp))
            {
                return false;
            }

            if (random.Next(AiPacketRules.SharedRollSides) != 0)
            {
                return false;
            }

            // The range is indexed by the doubled-driver flag (best warp in two starbase slots),
            // and the 12,500 kT rich test reads the planet's surface stock, not the projected one.
            bool doubled = MineralPacketRules.LaunchRating(star.Starbase) > bestDriverWarp;
            Resources surface = star.ResourcesOnHand;
            bool rich = surface != null
                && (surface.Ironium > AiPacketRules.SharedRichMineralAbove
                    || surface.Boranium > AiPacketRules.SharedRichMineralAbove
                    || surface.Germanium > AiPacketRules.SharedRichMineralAbove);
            double range = AiPacketRules.SharedRangeSquared(doubled, rich);

            EmpireData empire = planet.Empire;
            StarIntel target = null;
            int candidates = 0;
            foreach (StarIntel report in empire.StarReports.Values)
            {
                if (report?.Position == null || report.Owner == empire.Id || report.Owner == Global.Nobody)
                {
                    continue;
                }

                if (empire.TurnYear - report.Year > AiPacketRules.SharedReportMaxAge
                    || !AiPacketRules.SharedTargetWeakEnough(0, AiFleetContext.ReportedFigure(report))
                    || PointUtilities.DistanceSquare(star.Position, report.Position) > range)
                {
                    continue;
                }

                candidates++;
                if (random.Next(candidates) == 0)
                {
                    target = report;
                }
            }

            // The destination's speed field is set to warp 13 whatever the driver (the gate has
            // already required a best driver of warp 10 or more, so 13 is legal).
            if (target == null || !planet.SetPacketDestination(target.Name, AiPacketRules.SharedPacketWarp))
            {
                return false;
            }

            foreach (AiPacketOrder order in AiPacketRules.SharedOrders(projected, random))
            {
                planet.QueuePackets(order.Mineral, order.Count, atTop: false);
            }

            // The advisor has acted once a target is drawn, whether or not the mineral rules
            // queued anything: the destination is set even then, and the chain stops.
            return true;
        }

        // ================================================================ §12 personality 5

        /// <summary>
        /// Personality 5's planet-pass packets (§12): after year 120, one time in four, a planet
        /// whose best driver is warp 10+ and whose stored population exceeds 10,000 units ships a
        /// fifth (at most 20,000 kT) of a mineral it holds above 5,000 kT, as amount ÷ 100
        /// packets, to the own planet with a warp-10+ driver within about 160 ly that holds least
        /// of that mineral, which must hold less than the amount. Readings (see report): the
        /// roll is drawn before the planet tests; the packets go to the bottom of the queue; the
        /// chosen speed is left as it was.
        /// </summary>
        public void RunMacintiPlanetPassPackets(DefaultPlanetAI planet, int year, Random random)
        {
            if (planet == null || year <= AiPacketRules.MacintiAfterYear)
            {
                return;
            }

            if (random.Next(AiPacketRules.MacintiRollSides) != 0)
            {
                return;
            }

            Star star = planet.Planet;
            if (MineralPacketRules.BestDriverWarp(star.Starbase) < AiPacketRules.MacintiMinimumDriverWarp
                || planet.PopulationUnits <= AiPacketRules.MacintiPopulationAbove)
            {
                return;
            }

            if (!AiPacketRules.MacintiShipment(star.ResourcesOnHand ?? new Resources(), out PacketMineral mineral, out int amount))
            {
                return;
            }

            Star target = null;
            int least = int.MaxValue;
            foreach (Star other in planet.Empire.OwnedStars.Values)
            {
                if (other == null || other.Name == star.Name || other.Owner != planet.Empire.Id || other.Position == null
                    || MineralPacketRules.BestDriverWarp(other.Starbase) < AiPacketRules.MacintiMinimumDriverWarp
                    || PointUtilities.DistanceSquare(star.Position, other.Position) > AiPacketRules.MacintiRangeSquared)
                {
                    continue;
                }

                int held = Held(other.ResourcesOnHand, mineral);
                if (held < least)
                {
                    least = held;
                    target = other;
                }
            }

            if (target == null || least >= amount || !planet.SetPacketDestination(target.Name, star.PacketWarp))
            {
                return;
            }

            planet.QueuePackets(mineral, AiPacketRules.MacintiPacketCount(amount), atTop: false);
        }

        // ================================================================ §12 personality 4

        /// <summary>
        /// `FUN_10a8_123e` (§12 personality 4, *Packets*): each owned starbase planet in the
        /// shuffled order tries hub supply, the packet attack and the border fallback, all
        /// packets at the top of the queue. Reading (see report): a planet whose starbase has no
        /// mass driver is skipped - its packet orders would be deleted on purchase (message 297)
        /// and its destination cannot be set.
        /// </summary>
        public void RunCybertronPackets(IReadOnlyList<DefaultPlanetAI> shuffledPlanets, int skill, int galaxySizeIndex, Random random)
        {
            if (shuffledPlanets == null || shuffledPlanets.Count == 0)
            {
                return;
            }

            EmpireData empire = shuffledPlanets[0].Empire;
            CybertronPacketMemory memory = Memory;
            if (memory == null)
            {
                memory = CybertronPacketMemory.FromPacketsInFlight(empire);
            }
            else
            {
                memory.BeginRun();
            }

            CybertronRun run = new CybertronRun(empire, skill, galaxySizeIndex, random, memory);
            foreach (DefaultPlanetAI planet in shuffledPlanets)
            {
                run.Visit(planet);
            }
        }

        private static int Held(Resources stock, PacketMineral mineral)
        {
            stock = stock ?? new Resources();
            switch (mineral)
            {
                case PacketMineral.Ironium: return stock.Ironium;
                case PacketMineral.Boranium: return stock.Boranium;
                default: return stock.Germanium;
            }
        }

        /// <summary>One category-4 run's working state: the shortage flags rebuilt this turn and
        /// the chooser's last returned planet (never reset per planet, §12 *Quirk*).</summary>
        private sealed class CybertronRun
        {
            private static readonly PacketMineral[] Minerals = { PacketMineral.Ironium, PacketMineral.Boranium, PacketMineral.Germanium };

            private readonly EmpireData empire;
            private readonly int skill;
            private readonly int galaxySizeIndex;
            private readonly Random random;
            private readonly CybertronPacketMemory memory;
            private readonly StarbaseSlots slots;
            private readonly Dictionary<string, bool[]> shortage = new Dictionary<string, bool[]>();
            private readonly HashSet<string> hubs = new HashSet<string>();
            private string lastChooserTarget;

            public CybertronRun(EmpireData empire, int skill, int galaxySizeIndex, Random random, CybertronPacketMemory memory)
            {
                this.empire = empire;
                this.skill = skill;
                this.galaxySizeIndex = galaxySizeIndex;
                this.random = random;
                this.memory = memory;
                slots = StarbaseSlots.ForEmpire(empire);

                // The shortage flags (`:69594`-`69627`): a packet hub is short of a mineral below
                // 10 kT, any other starbase planet below 1,000 kT.
                foreach (Star star in empire.OwnedStars.Values)
                {
                    if (star == null || star.Owner != empire.Id || star.Starbase == null)
                    {
                        continue;
                    }

                    bool hub = AiPacketRules.IsPacketHubSlot(slots.SlotOf(StarbaseDesignOf(star)));
                    if (hub)
                    {
                        hubs.Add(star.Name);
                    }

                    Resources stock = star.ResourcesOnHand ?? new Resources();
                    int below = AiPacketRules.ShortBelow(hub);
                    shortage[star.Name] = new[] { stock.Ironium < below, stock.Boranium < below, stock.Germanium < below };
                }
            }

            public void Visit(DefaultPlanetAI planet)
            {
                Star star = planet.Planet;
                if (star?.Starbase == null || MineralPacketRules.LaunchRating(star.Starbase) <= 0)
                {
                    return;
                }

                bool followUp = CybertronPacketWord.Has(memory.Word(star.Name), CybertronPacketWord.FollowUpBit);
                if (!followUp)
                {
                    if (HubSupply(planet, out bool finished) || finished)
                    {
                        return;
                    }

                    if (Attack(planet))
                    {
                        return;
                    }
                }

                Fallback(planet);
            }

            /// <summary>Branch 1. True when it set the destination; <paramref name="finished"/>
            /// when the planet is done for the turn without loading.</summary>
            private bool HubSupply(DefaultPlanetAI planet, out bool finished)
            {
                finished = false;
                Star star = planet.Planet;
                Resources stock = star.ResourcesOnHand ?? new Resources();
                int[] held = { stock.Ironium, stock.Boranium, stock.Germanium };
                if (!hubs.Contains(star.Name) || !held.Any(amount => amount > AiPacketRules.HubSourceAbove))
                {
                    return false;
                }

                int n = AiPacketRules.HubPacketBudget(star.GetResourceRate());
                if (n < AiPacketRules.HubMinimumPackets)
                {
                    return false;
                }

                int rating = MineralPacketRules.LaunchRating(star.Starbase);
                Star target = null;
                int speed = 0;
                double nearest = double.MaxValue;
                foreach (Star other in empire.OwnedStars.Values)
                {
                    if (other == null || other.Name == star.Name || other.Starbase == null || other.Position == null
                        || !shortage.TryGetValue(other.Name, out bool[] flags) || memory.CoolDown(other.Name) != 0)
                    {
                        continue;
                    }

                    bool needed = false;
                    for (int mineral = 0; mineral < 3; mineral++)
                    {
                        needed |= flags[mineral] && held[mineral] > AiPacketRules.HubSourceAbove;
                    }

                    int s = Math.Min(rating, MineralPacketRules.LaunchRating(other.Starbase));
                    double distance = Math.Sqrt(PointUtilities.DistanceSquare(star.Position, other.Position));
                    if (!needed || distance > AiPacketRules.HubRangeYears * s * s || distance >= nearest)
                    {
                        continue;
                    }

                    nearest = distance;
                    target = other;
                    speed = s;
                }

                if (target == null)
                {
                    return false;
                }

                bool[] targetFlags = shortage[target.Name];
                bool loaded = false;
                for (int mineral = 0; mineral < 3; mineral++)
                {
                    if (!targetFlags[mineral] || held[mineral] <= AiPacketRules.HubSourceAbove)
                    {
                        continue;
                    }

                    int count = Math.Min(n, AiPacketRules.HubMaxPacketsPerMineral);
                    if (count > 0 && planet.QueuePackets(Minerals[mineral], count, atTop: true))
                    {
                        n -= count;
                        targetFlags[mineral] = false;
                        loaded = true;
                    }
                }

                if (!loaded)
                {
                    finished = true;
                    return false;
                }

                // Speed s, the smaller launch rating (no overspeed, no decay); no cool-down.
                planet.SetPacketDestination(target.Name, speed);
                return true;
            }

            /// <summary>Branch 2, the packet attack. True when it fired.</summary>
            private bool Attack(DefaultPlanetAI planet)
            {
                if (skill < 1 || (skill == 1 && random.Next(AiPacketRules.SkillOneRollSides) != 0))
                {
                    return false;
                }

                Star star = planet.Planet;
                Resources surplus = planet.Surplus();
                int resources = planet.ProjectedResources();
                long surplusS = AiPacketRules.AttackSurplus(surplus);
                long budget = AiPacketRules.AttackBudget(surplusS, resources);
                if (surplusS <= AiPacketRules.AttackSurplusAtMost)
                {
                    return false;
                }

                int best = MineralPacketRules.BestDriverWarp(star.Starbase);
                bool doubled = MineralPacketRules.LaunchRating(star.Starbase) > best;
                int s = best + 3;
                double q = AiPacketRules.SurvivingShare(doubled);

                StarIntel target = null;
                int targetMass = 0;
                double targetDistance = 0;
                double nearest = double.MaxValue;
                foreach (StarIntel report in empire.StarReports.Values)
                {
                    if (report?.Position == null || report.Owner == empire.Id || report.Owner == Global.Nobody
                        || memory.CoolDown(report.Name) != 0)
                    {
                        continue;
                    }

                    int r = MineralPacketRules.LaunchRating(report.Starbase);
                    int f = AiFleetContext.ReportedFigure(report);
                    double distance = Math.Sqrt(PointUtilities.DistanceSquare(star.Position, report.Position));
                    if (r == s || f == 0 || distance > AiPacketRules.AttackRangeYears * s * s || distance >= nearest)
                    {
                        continue;
                    }

                    int mass = AiPacketRules.MassNeeded(f, r, s, 0);
                    if (mass > AiPacketRules.BudgetAfterDecay(budget, q, distance, s))
                    {
                        continue;
                    }

                    nearest = distance;
                    target = report;
                    targetMass = mass;
                    targetDistance = distance;
                }

                if (target == null)
                {
                    return false;
                }

                long amount = AiPacketRules.AmountSent(surplusS, targetMass, AiPacketRules.InverseSurvivingShare(doubled), targetDistance, s);
                int[] counts = AiPacketRules.SplitIntoPackets(amount, surplus);
                for (int mineral = 0; mineral < 3; mineral++)
                {
                    if (counts[mineral] > 0)
                    {
                        planet.QueuePackets(Minerals[mineral], counts[mineral], atTop: true);
                    }
                }

                planet.SetPacketDestination(target.Name, s);
                memory.SetCoolDown(target.Name, AiPacketRules.CoolDownTurns);

                int word = memory.Word(star.Name);
                word = CybertronPacketWord.With(word, CybertronPacketWord.RestBit, false);
                word = CybertronPacketWord.With(word, CybertronPacketWord.FollowUpBit, targetDistance > s * s);
                memory.SetWord(star.Name, word);
                return true;
            }

            /// <summary>Branch 3, the fallback and follow-up.</summary>
            private void Fallback(DefaultPlanetAI planet)
            {
                Star star = planet.Planet;
                int word = memory.Word(star.Name);
                bool rest = CybertronPacketWord.Has(word, CybertronPacketWord.RestBit);
                bool followUp = CybertronPacketWord.Has(word, CybertronPacketWord.FollowUpBit);

                if (rest && !followUp)
                {
                    memory.SetWord(star.Name, CybertronPacketWord.With(word, CybertronPacketWord.RestBit, false));
                    return;
                }

                string target;
                string coolDownPlanet;
                if (!followUp)
                {
                    int index = AiPacketRules.NextDirectionIndex(random.Next(7), CybertronPacketWord.DirectionIndex(word));
                    word = CybertronPacketWord.WithDirectionIndex(word, index);
                    memory.SetWord(star.Name, word);

                    lastChooserTarget = Choose(star, index);
                    target = lastChooserTarget;
                    if (target == null)
                    {
                        return;
                    }

                    coolDownPlanet = target;
                }
                else
                {
                    // Follow-up: the destination stays last turn's attack target, but the
                    // cool-down test and mark use the chooser's last return value in this run
                    // (§12 *Quirk*); with none yet, no cool-down is tested or marked (reading).
                    target = star.PacketDestination;
                    coolDownPlanet = lastChooserTarget;
                }

                bool queued = false;
                if (coolDownPlanet == null || memory.CoolDown(coolDownPlanet) == 0)
                {
                    queued = QueueLargestSurplusPacket(planet);
                }

                word = memory.Word(star.Name);
                if (queued)
                {
                    if (coolDownPlanet != null)
                    {
                        memory.SetCoolDown(coolDownPlanet, AiPacketRules.CoolDownTurns);
                        word = memory.Word(star.Name);
                    }

                    word = CybertronPacketWord.With(word, CybertronPacketWord.RestBit, !followUp);
                }
                else
                {
                    word = CybertronPacketWord.With(word, CybertronPacketWord.RestBit, false);
                }

                word = CybertronPacketWord.With(word, CybertronPacketWord.FollowUpBit, false);
                memory.SetWord(star.Name, word);

                // The speed becomes s (best driver warp + 3).
                if (!string.IsNullOrEmpty(target))
                {
                    planet.SetPacketDestination(target, MineralPacketRules.BestDriverWarp(star.Starbase) + 3);
                }
            }

            /// <summary>`FUN_10a8_1d76`: one packet of the mineral with the largest surplus (ties
            /// to the earlier mineral), which must exceed 169 kT, at the top of the queue.</summary>
            private static bool QueueLargestSurplusPacket(DefaultPlanetAI planet)
            {
                Resources surplus = planet.Surplus();
                int[] left = { surplus.Ironium, surplus.Boranium, surplus.Germanium };
                int best = 0;
                for (int index = 1; index < 3; index++)
                {
                    if (left[index] > left[best])
                    {
                        best = index;
                    }
                }

                return left[best] > AiPacketRules.FallbackSurplusAbove && planet.QueuePackets(Minerals[best], 1, atTop: true);
            }

            /// <summary>The border chooser `FUN_10a8_1aec` (§12): the planet name, or null.</summary>
            private string Choose(Star star, int index)
            {
                int side = AiPacketRules.FrameSide(galaxySizeIndex);
                int x = Math.Max(0, Math.Min(side, star.Position.X - MapOrigin));
                int y = Math.Max(0, Math.Min(side, star.Position.Y - MapOrigin));
                int s = MineralPacketRules.LaunchRating(star.Starbase) + 3;

                AiPacketRules.BorderPoint(index, x, y, side, out int px, out int py);
                int range = AiPacketRules.SlideDrawRange(side);
                int j = (range > 0 ? random.Next(range) : 0) - AiPacketRules.SlideOffset(side);
                AiPacketRules.Slide(j, side, ref px, ref py);
                AiPacketRules.StepInward(random.Next(s * s), side, ref px, ref py);

                NovaPoint point = new NovaPoint(px + MapOrigin, py + MapOrigin);
                StarIntel nearest = null;
                double nearestDistance = double.MaxValue;
                foreach (StarIntel report in empire.StarReports.Values)
                {
                    if (report?.Position == null)
                    {
                        continue;
                    }

                    double distance = PointUtilities.DistanceSquare(point, report.Position);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = report;
                    }
                }

                if (nearest == null || nearest.Owner == empire.Id
                    || !AiPacketRules.ChooserDistancePasses(PointUtilities.DistanceSquare(star.Position, nearest.Position), s))
                {
                    return null;
                }

                return nearest.Name;
            }

            private static Nova.Common.Components.ShipDesign StarbaseDesignOf(Star star)
            {
                return star.Starbase?.Composition?.Values
                    .Where(token => token.Design != null)
                    .Select(token => token.Design)
                    .FirstOrDefault();
            }
        }
    }
}
