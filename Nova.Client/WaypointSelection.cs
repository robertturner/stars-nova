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
    /// <summary>
    /// Which waypoint of a fleet's route stays selected after a waypoint is deleted
    /// (behavior-specs-11/save-turn-file-format.md and client-interface.md command table, ids
    /// 103/104 and the Delete key: the selected waypoint is deleted and the selection stays on the
    /// previous (103) or the following (104) waypoint).
    /// Waypoint indices are Fleet.Waypoints indices; index 0 is the fleet's own position. Nova's
    /// order editors never show waypoint 0 as a selectable row, but the spec is explicit that
    /// deleting the first leg leaves waypoint 0 - the fleet itself - current, not the following
    /// waypoint. This returns 0 in that case; a view model that cannot show a row for it treats
    /// 0 as "the fleet itself is the subject". -1 means no waypoint selected.
    /// </summary>
    public static class WaypointSelection
    {
        /// <summary>
        /// The selection after deleting waypoint <paramref name="deletedIndex"/>.
        /// </summary>
        /// <param name="selectedIndex">The selection before the delete (-1 for none).</param>
        /// <param name="deletedIndex">The waypoint that was deleted (1 or more).</param>
        /// <param name="countAfterDelete">Fleet.Waypoints.Count after the delete (including index 0).</param>
        /// <param name="keepPrevious">True for the "previous" variant (the Delete key), false for "following".</param>
        public static int AfterDelete(int selectedIndex, int deletedIndex, int countAfterDelete, bool keepPrevious)
        {
            if (deletedIndex < 1)
            {
                return selectedIndex;
            }

            if (selectedIndex != deletedIndex)
            {
                // Another waypoint was deleted: the selected one keeps its identity, shifting down
                // by one if it came after the deleted one.
                if (selectedIndex > deletedIndex)
                {
                    selectedIndex--;
                }

                return selectedIndex >= 0 && selectedIndex < countAfterDelete ? selectedIndex : -1;
            }

            int previous = deletedIndex - 1; // 0 is the fleet itself (waypoint 0)
            int following = deletedIndex; // the next waypoint has moved down into this slot
            bool hasPrevious = previous >= 0;
            bool hasFollowing = following < countAfterDelete;

            if (keepPrevious)
            {
                return hasPrevious ? previous : (hasFollowing ? following : -1);
            }

            return hasFollowing ? following : (hasPrevious ? previous : -1);
        }
    }
}
