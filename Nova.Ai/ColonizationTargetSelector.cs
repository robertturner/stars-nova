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

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The colony-ship target search `FUN_1090_098e` (behavior-specs-10/ai-opponent-behavior.md
    /// §2, "Colonization-target choice (corrected)"): the NEAREST planet, by squared distance,
    /// that is unowned and not already claimed by another own fleet - with no scoring, no
    /// distance bands and no random acceptance. For categories other than 0 and 5 a planet whose
    /// terraformed habitability for the AI is negative is left out too.
    /// </summary>
    /// <remarks>
    /// Claims (§2 detail): for categories 0 and 5 the classification is rebuilt on every search,
    /// and a planet is claimed only when another own fleet's next waypoint is that planet with
    /// the Colonize task. For the other categories it is built once, on the first search of the
    /// run, from every own fleet's next waypoint whatever its task, and the caller then marks
    /// each planet it orders colonized (<see cref="MarkClaimed"/>). The wormhole diversion before
    /// year 120 is not ported. The "random exploratory colonization" paragraph that survives in
    /// §2 is not ported either: no traced code path in the colonization search does it.
    /// </remarks>
    public class ColonizationTargetSelector
    {
        private readonly ClientData clientState;
        private readonly int category;
        private HashSet<string> claimedOnce;

        public ColonizationTargetSelector(ClientData clientState, int category)
        {
            this.clientState = clientState;
            this.category = category;
        }

        /// <summary>
        /// The nearest eligible planet for <paramref name="colonyFleet"/>, or null when there is
        /// none. Ties keep the earlier report.
        /// </summary>
        public StarIntel SelectTarget(Fleet colonyFleet)
        {
            bool rebuildEachCall = !AiCategory.UsesColonyHabitabilityFilter(category);
            HashSet<string> claimed = rebuildEachCall ? ColonizeClaims(colonyFleet) : OnceClaims();

            StarIntel best = null;
            double bestDistance = double.MaxValue;

            foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
            {
                if (report.Owner != Global.Nobody || claimed.Contains(report.Name))
                {
                    continue;
                }

                if (AiCategory.UsesColonyHabitabilityFilter(category) && !PassesHabitabilityFilter(clientState.EmpireState.Race, report))
                {
                    continue;
                }

                double distance = PointUtilities.DistanceSquare(colonyFleet.Position, report.Position);
                if (distance < bestDistance)
                {
                    best = report;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>Marks a planet the caller has just ordered colonized, so later searches in
        /// the same run skip it (the categories whose classification is built once).</summary>
        public void MarkClaimed(StarIntel target)
        {
            if (target != null && claimedOnce != null)
            {
                claimedOnce.Add(target.Name);
            }
        }

        /// <summary>
        /// §2 / §12: non-negative terraformed habitability for the AI (`FUN_1048_47ec`). A report
        /// carries no terraforming history, so the planet is treated as untouched (its current
        /// environment is its original one) and gets the race's full terraform allowance.
        /// </summary>
        public static bool PassesHabitabilityFilter(Race race, StarIntel report)
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

            return race.HabitalValueAfterTerraform(projected) >= 0;
        }

        private IEnumerable<Fleet> OwnFleets()
        {
            return clientState.EmpireState.OwnedFleets.Values
                .Where(fleet => fleet.Owner == clientState.EmpireState.Id);
        }

        /// <summary>Categories 0 and 5: planets another own fleet's next waypoint targets with
        /// the Colonize task.</summary>
        private HashSet<string> ColonizeClaims(Fleet self)
        {
            HashSet<string> claimed = new HashSet<string>();
            foreach (Fleet fleet in OwnFleets())
            {
                Waypoint next = NextWaypoint(fleet);
                if (fleet == self || next == null || !(next.Task is ColoniseTask))
                {
                    continue;
                }

                AddClaim(claimed, next);
            }

            return claimed;
        }

        /// <summary>
        /// The fleet's next waypoint, skipping any waypoint at its current position: the
        /// original loads colonists by an immediate transfer, which Nova expresses as a load
        /// waypoint where the fleet stands, so the real "next waypoint" is the one after it.
        /// </summary>
        private static Waypoint NextWaypoint(Fleet fleet)
        {
            for (int index = 1; index < fleet.Waypoints.Count; index++)
            {
                NovaPoint position = fleet.Waypoints[index].Position;
                bool here = position != null && fleet.Position != null
                    && position.X == fleet.Position.X && position.Y == fleet.Position.Y;
                if (!here)
                {
                    return fleet.Waypoints[index];
                }
            }

            return null;
        }

        /// <summary>Other categories: every own fleet's next-waypoint planet, whatever the task,
        /// captured on the first search of the run.</summary>
        private HashSet<string> OnceClaims()
        {
            if (claimedOnce == null)
            {
                claimedOnce = new HashSet<string>();
                foreach (Fleet fleet in OwnFleets())
                {
                    Waypoint next = NextWaypoint(fleet);
                    if (next != null)
                    {
                        AddClaim(claimedOnce, next);
                    }
                }
            }

            return claimedOnce;
        }

        private void AddClaim(HashSet<string> claimed, Waypoint waypoint)
        {
            foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
            {
                if (report.Name == waypoint.Destination
                    || (waypoint.Position != null && report.Position != null
                        && report.Position.X == waypoint.Position.X && report.Position.Y == waypoint.Position.Y))
                {
                    claimed.Add(report.Name);
                }
            }
        }
    }
}
