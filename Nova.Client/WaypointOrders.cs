#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;

    using Nova.Common;
    using Nova.Common.Waypoints;

    /// <summary>
    /// The UI rules behind the fleet-orders editor's waypoint tasks and targets, kept here (not in
    /// Nova.Avalonia) so they are unit-testable:
    /// - a waypoint placed on a fleet (own or a foreign fleet report) aims at that fleet, i.e.
    ///   pursues it (behavior-specs-10/fleet-movement-scanning-cargo.md section 5, "Pursuit");
    /// - the task picker's names and the tasks they build, including Patrol with its speed
    ///   (0 = the fleet's efficient warp) and range index ((index + 1) x 50 ly; 10 = 10,000 ly).
    /// </summary>
    public static class WaypointOrders
    {
        /// <summary>The task picker's option for <see cref="PatrolTask"/> (also its Name).</summary>
        public const string PatrolOption = "Patrol";

        /// <summary>The highest stored Patrol range index (10,000 ly).</summary>
        public const int MaxPatrolRangeIndex = PatrolTask.UnlimitedRangeIndex;

        /// <summary>The highest Patrol speed setting (warp 10); 0 means automatic.</summary>
        public const int MaxPatrolSpeed = 10;

        /// <summary>
        /// The waypoint task picker's options, in display order. Every one but "Merge With Fleet"
        /// (which needs a target fleet, resolved by the caller) is built by <see cref="BuildTask"/>.
        /// </summary>
        public static IReadOnlyList<string> TaskOptions { get; } =
            new[] { "None", "Colonise", "Scrap", "Lay Mines", "Invade", PatrolOption, "Merge With Fleet" };

        /// <summary>Labels for Patrol range indices 0-10, index-aligned.</summary>
        public static IReadOnlyList<string> PatrolRangeLabels { get; } = BuildRangeLabels();

        /// <summary>Labels for Patrol speed settings 0-10, index-aligned.</summary>
        public static IReadOnlyList<string> PatrolSpeedLabels { get; } = BuildSpeedLabels();

        /// <summary>"50 ly" .. "500 ly" for indices 0-9, "10,000 ly" for 10 (out-of-range indices clamp).</summary>
        public static string PatrolRangeLabel(int rangeIndex)
        {
            PatrolTask probe = new PatrolTask { RangeIndex = ClampRangeIndex(rangeIndex) };
            return probe.RangeInLightYears.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " ly";
        }

        /// <summary>"Automatic" for 0 (the fleet's efficient warp), otherwise "Warp N".</summary>
        public static string PatrolSpeedLabel(int speed)
        {
            int clamped = ClampPatrolSpeed(speed);
            return clamped == 0 ? "Automatic" : "Warp " + clamped.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static int ClampRangeIndex(int rangeIndex)
        {
            return Math.Max(0, Math.Min(MaxPatrolRangeIndex, rangeIndex));
        }

        public static int ClampPatrolSpeed(int speed)
        {
            return Math.Max(0, Math.Min(MaxPatrolSpeed, speed));
        }

        /// <summary>A Patrol order with the given (clamped) settings.</summary>
        public static PatrolTask BuildPatrol(int speed, int rangeIndex)
        {
            return new PatrolTask { Speed = ClampPatrolSpeed(speed), RangeIndex = ClampRangeIndex(rangeIndex) };
        }

        /// <summary>
        /// The task for a picker option. Patrol takes <paramref name="patrolSpeed"/> and
        /// <paramref name="patrolRangeIndex"/>; anything unrecognised (including "None" and
        /// "Merge With Fleet", which the caller builds itself) gives <see cref="NoTask"/>.
        /// </summary>
        public static IWaypointTask BuildTask(string option, int patrolSpeed = 0, int patrolRangeIndex = 0)
        {
            switch (option)
            {
                case "Colonise":
                    return new ColoniseTask();
                case "Scrap":
                    return new ScrapTask();
                case "Lay Mines":
                    return new LayMinesTask();
                case "Invade":
                    return new InvadeTask();
                case PatrolOption:
                    return BuildPatrol(patrolSpeed, patrolRangeIndex);
                default:
                    return new NoTask();
            }
        }

        /// <summary>
        /// The fleet a map tap on <paramref name="tapped"/> should make a waypoint pursue, or null
        /// when the waypoint should stay a fixed point: only a fleet (own) or fleet report
        /// (foreign) qualifies, never a starbase (it cannot move, and the host's pursuit re-lock
        /// skips starbases) and never the fleet whose orders are being edited.
        /// </summary>
        public static Mappable PursuitTargetOf(Mappable tapped, long orderingFleetKey)
        {
            switch (tapped)
            {
                case Fleet fleet when !fleet.IsStarbase && fleet.Key != orderingFleetKey:
                    return fleet;
                case FleetIntel report when !report.IsStarbase && report.Key != orderingFleetKey:
                    return report;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Aims <paramref name="waypoint"/> at the tapped fleet when <see cref="PursuitTargetOf"/>
        /// says it qualifies (<see cref="Waypoint.AimAtFleet"/>); returns whether it did. A target
        /// with no name keeps the "Space at" label, since an empty Destination does not survive a
        /// save/load round trip.
        /// </summary>
        public static bool AimAtTappedFleet(Waypoint waypoint, Mappable tapped, long orderingFleetKey)
        {
            Mappable target = PursuitTargetOf(tapped, orderingFleetKey);
            if (waypoint == null || target == null)
            {
                return false;
            }

            waypoint.AimAtFleet(target);
            if (string.IsNullOrEmpty(waypoint.Destination))
            {
                waypoint.Destination = "Space at " + waypoint.Position;
            }

            return true;
        }

        /// <summary>
        /// The route list's task text: the task's name, plus a Patrol order's speed and range,
        /// e.g. "Patrol (Automatic, 50 ly)".
        /// </summary>
        public static string TaskDisplay(IWaypointTask task)
        {
            if (task == null)
            {
                return "None";
            }

            if (task is PatrolTask patrol)
            {
                return patrol.Name + " (" + PatrolSpeedLabel(patrol.Speed) + ", " + PatrolRangeLabel(patrol.RangeIndex) + ")";
            }

            return task.Name;
        }

        /// <summary>
        /// A short note on what the waypoint is aimed at for the route list: "chasing &lt;name&gt;"
        /// for a fleet pursuit (Destination is the target fleet's name, kept current by the
        /// host's end-of-movement refresh), otherwise empty.
        /// </summary>
        public static string TargetNote(Waypoint waypoint)
        {
            if (waypoint == null || !waypoint.IsFleetTarget)
            {
                return string.Empty;
            }

            return "chasing " + waypoint.Destination;
        }

        private static IReadOnlyList<string> BuildRangeLabels()
        {
            string[] labels = new string[MaxPatrolRangeIndex + 1];
            for (int i = 0; i <= MaxPatrolRangeIndex; i++)
            {
                labels[i] = PatrolRangeLabel(i);
            }

            return labels;
        }

        private static IReadOnlyList<string> BuildSpeedLabels()
        {
            string[] labels = new string[MaxPatrolSpeed + 1];
            for (int i = 0; i <= MaxPatrolSpeed; i++)
            {
                labels[i] = PatrolSpeedLabel(i);
            }

            return labels;
        }
    }
}
