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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The stale-slot sweep `FUN_1090_5d68`, the splitter `FUN_1090_5bda` and personality 0's
    /// obsolete-fleet rule (behavior-specs-10/ai-opponent-behavior.md §10 and §16 item 3, §12
    /// personality 0 fleet pass 3). The flags are recomputed every turn from the current year and
    /// each design's creation year and number in existence; nothing persists.
    /// </summary>
    /// <remarks>
    /// Stand-ins: Nova designs have no creation-year field, so the year is
    /// AiDesignRoles.CreationYear (the " T&lt;year&gt;" name suffix, else the game's start).
    /// Each driver passes its own slot range and threshold; only personality 4's thresholds are
    /// given, so they are used for every personality, and the "slot range" is the warship roles
    /// (strike, bomber, line, hunter) - never colony, freighter, scout, minelayer or miner
    /// designs, whose replacement Nova's planet pass depends on. A design with no ships is
    /// deleted only when no production queue holds it (deleting a queued design would strand
    /// the order in Nova).
    /// </remarks>
    public static class StaleDesignSweep
    {
        /// <summary>The splitter runs while the player owns fewer than 501 fleets.</summary>
        public const int FleetLimit = 501;

        /// <summary>The age (years) beyond which a design is stale: 50 before year 120, 70 before
        /// 200, 100 before 400, 300 after (personality 4's figures).</summary>
        public static int Threshold(int year)
        {
            if (year < 120)
            {
                return 50;
            }

            if (year < 200)
            {
                return 70;
            }

            return year < 400 ? 100 : 300;
        }

        /// <summary>Whether a design takes part in the sweep (the warship "slot ranges").</summary>
        public static bool InSweptRange(ShipDesign design, int category)
        {
            switch (AiFleetRoles.Classify(design, category))
            {
                case AiFleetRole.StrikeLight:
                case AiFleetRole.StrikeHeavy:
                case AiFleetRole.Bomber:
                case AiFleetRole.LineWarship:
                case AiFleetRole.Hunter:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The sweep: every swept design older than the threshold is flagged when ships of it
        /// exist, otherwise listed in <paramref name="toDelete"/> (when no queue holds it).
        /// </summary>
        public static HashSet<long> Sweep(EmpireData empire, int category, out List<ShipDesign> toDelete)
        {
            int year = empire.TurnYear - Global.StartingYear;
            int threshold = Threshold(year);
            HashSet<long> stale = new HashSet<long>();
            toDelete = new List<ShipDesign>();

            Dictionary<long, int> inExistence = new Dictionary<long, int>();
            foreach (Fleet fleet in empire.OwnedFleets.Values.Where(f => f.Owner == empire.Id))
            {
                foreach (ShipToken token in fleet.Composition.Values.Where(t => t.Quantity > 0 && t.Design != null))
                {
                    inExistence.TryGetValue(token.Design.Key, out int count);
                    inExistence[token.Design.Key] = count + token.Quantity;
                }
            }

            HashSet<long> queued = new HashSet<long>();
            foreach (Star star in empire.OwnedStars.Values)
            {
                foreach (ProductionOrder order in star.ManufacturingQueue?.Queue ?? new List<ProductionOrder>())
                {
                    if (order.Unit is ShipProductionUnit unit)
                    {
                        queued.Add(unit.DesignKey);
                    }
                }
            }

            foreach (ShipDesign design in empire.Designs.Values.ToList())
            {
                if (design.IsStarbase || !InSweptRange(design, category))
                {
                    continue;
                }

                int age = empire.TurnYear - AiDesignRoles.CreationYear(design);
                if (age <= threshold)
                {
                    continue;
                }

                if (inExistence.ContainsKey(design.Key))
                {
                    stale.Add(design.Key);
                }
                else if (!queued.Contains(design.Key))
                {
                    toDelete.Add(design);
                }
            }

            return stale;
        }

        /// <summary>A fleet that mixes flagged and unflagged designs (the splitter's input).</summary>
        public static bool IsMixed(Fleet fleet, HashSet<long> stale)
        {
            List<ShipToken> occupied = fleet.Composition.Values.Where(t => t.Quantity > 0 && t.Design != null).ToList();
            return occupied.Any(t => stale.Contains(t.Design.Key)) && occupied.Any(t => !stale.Contains(t.Design.Key));
        }

        /// <summary>A fleet made up only of flagged designs (fleet pass 3's obsolete fleet).</summary>
        public static bool IsObsolete(Fleet fleet, HashSet<long> stale)
        {
            List<ShipToken> occupied = fleet.Composition.Values.Where(t => t.Quantity > 0 && t.Design != null).ToList();
            return occupied.Count > 0 && occupied.All(t => stale.Contains(t.Design.Key));
        }

        /// <summary>
        /// Personality 0's obsolete-fleet rule: scrapped at once at an own planet with a
        /// starbase, or one time in five at another own planet; otherwise sent to the nearest own
        /// starbase planet, unless already in deep space bound for a planet.
        /// </summary>
        public static FleetOrder ObsoleteFleetOrder(Fleet fleet, AiFleetContext context)
        {
            StarIntel here = context.CurrentPlanet(fleet);
            if (context.IsOwn(here))
            {
                if (context.HasStarbase(here) || context.Random.Next(5) == 0)
                {
                    return new FleetOrder { Scrap = true, Reason = "obsolete: scrapped" };
                }
            }
            else if (here == null && context.BoundFor(fleet) != null)
            {
                return FleetOrder.KeepOrders("obsolete: in deep space bound for a planet");
            }

            StarIntel home = context.NearestOwnStarbasePlanet(fleet.Position);
            if (home == null || (here != null && here.Name == home.Name))
            {
                return FleetOrder.KeepOrders("obsolete: no starbase planet to go to");
            }

            return FleetOrder.MoveTo(home, "obsolete: sent to the nearest own starbase planet");
        }
    }
}
