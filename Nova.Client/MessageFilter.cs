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
    using System.IO;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The message viewer's per-type filter (behavior-specs-11/client-ui-dialog-catalog.md,
    /// Messages):
    /// - a set bit means "filtered"; the tick/cross toggle flips the filter for the current
    ///   message's type, and sibling types always change together;
    /// - the filter is cleared to nothing-filtered when a game is opened, then restored from the
    ///   player's own file, so it persists per player across turns;
    /// - a magnifier appears only when some type present this turn is filtered, and switches
    ///   between "hide filtered messages" (default) and "show filtered messages";
    /// - in hide mode Next/Previous skip filtered messages, in show mode they step through all.
    ///
    /// The original's filter is a 392-bit set indexed by the 9-bit numeric message type
    /// (0-386). The spec gives the complete numeric type-to-group map in
    /// <see cref="GroupKey(int)"/>: flipping any member of a multi-type group sets every member.
    /// Nova's Message.Type is a free-form string and far coarser (one "Stargate" type covers what
    /// the original splits into a dozen ids), so the string path (<see cref="GroupOf(string)"/>)
    /// is the one the desktop and Android panels use, with the battle-report run 145-168 the only
    /// sibling group Nova's vocabulary currently distinguishes; every other Nova type is its own
    /// group of one. The numeric path is the spec-faithful core, exercised by tests, and becomes
    /// end-to-end meaningful only once a message carries a numeric type (a Message change outside
    /// this file, reported as a seam).
    /// </summary>
    public sealed class MessageFilter
    {
        /// <summary>The group name used for a message with no Type at all.</summary>
        public const string GeneralGroup = "General";

        /// <summary>The number of numeric message types in the original (0-386).</summary>
        public const int TypeCount = 387;

        /// <summary>
        /// Nova message-type strings that belong to the same spec sibling group. A type not listed
        /// is its own group (named after itself).
        /// Spec runs/pairs and their Nova equivalents:
        ///   145-168 (all battle-report summaries): "Battle", "BattleReport", "BattleSummary".
        /// The other spec groups (43-46 and 121/122 cargo seen at a location, 96-100 and 106-110
        /// bombing results, 47/48 and 53-58 production results, 66-77 cargo-transfer outcomes) have
        /// no separately typed Nova messages: bombing is one "Bombing" type for both sides, and
        /// production/cargo notices are untyped, so they need no mapping entry.
        /// </summary>
        private static readonly Dictionary<string, string> SiblingGroups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Battle"] = "Battle",
            ["BattleReport"] = "Battle",
            ["BattleSummary"] = "Battle",
        };

        private readonly HashSet<string> filteredGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<int> filteredTypeGroups = new HashSet<int>();

        /// <summary>
        /// "Show filtered messages" mode (the magnifier); false is the default "hide filtered".
        /// </summary>
        public bool ShowFiltered { get; set; }

        /// <summary>The filtered groups, for persistence and display.</summary>
        public IReadOnlyCollection<string> FilteredGroups => filteredGroups;

        /// <summary>The filtered numeric-type groups (the spec's 392-bit set, group keys).</summary>
        public IReadOnlyCollection<int> FilteredTypeGroups => filteredTypeGroups;

        /// <summary>The filter group a message type belongs to.</summary>
        public static string GroupOf(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                return GeneralGroup;
            }

            string trimmed = type.Trim();
            return SiblingGroups.TryGetValue(trimmed, out string group) ? group : trimmed;
        }

        /// <summary>
        /// The spec's complete numeric type-to-group map (client-ui-dialog-catalog.md, Messages,
        /// "Complete type-to-group map"): group key = the lowest type in the group; every one of
        /// the 387 types belongs to exactly one group. Multi-type groups are {43-46}, {47,48},
        /// {53,54}, {55,56}, {57,58}, {66,67}, {68,69}, {70,71}, {72,73}, {74,75}, {76,77},
        /// {96-100}, {106-110}, {121,122} and {145-168}; every other type is its own group of one.
        /// The types the spec calls out as deliberately *not* grouped (49-52, 101-105) return
        /// themselves, as does every other singleton.
        /// </summary>
        public static int GroupKey(int type)
        {
            if (type >= 43 && type <= 46)
            {
                return 43;
            }

            if (type >= 96 && type <= 100)
            {
                return 96;
            }

            if (type >= 106 && type <= 110)
            {
                return 106;
            }

            if (type >= 145 && type <= 168)
            {
                return 145;
            }

            switch (type)
            {
                case 47:
                case 48:
                    return 47;
                case 53:
                case 54:
                    return 53;
                case 55:
                case 56:
                    return 55;
                case 57:
                case 58:
                    return 57;
                case 66:
                case 67:
                    return 66;
                case 68:
                case 69:
                    return 68;
                case 70:
                case 71:
                    return 70;
                case 72:
                case 73:
                    return 72;
                case 74:
                case 75:
                    return 74;
                case 76:
                case 77:
                    return 76;
                case 121:
                case 122:
                    return 121;
                default:
                    return type;
            }
        }

        /// <summary>True when numeric-type messages of this type are filtered.</summary>
        public bool IsFiltered(int type)
        {
            return filteredTypeGroups.Contains(GroupKey(type));
        }

        /// <summary>True when a numeric-type message is shown by Next/Previous.</summary>
        public bool IsVisible(int type)
        {
            return ShowFiltered || !IsFiltered(type);
        }

        /// <summary>The tick/cross for a numeric type: flips its whole sibling group.</summary>
        public void Toggle(int type)
        {
            int group = GroupKey(type);
            if (!filteredTypeGroups.Remove(group))
            {
                filteredTypeGroups.Add(group);
            }
        }

        /// <summary>True when messages of this type are filtered.</summary>
        public bool IsFiltered(string type)
        {
            return filteredGroups.Contains(GroupOf(type));
        }

        /// <summary>
        /// True when a message of this type is shown by Next/Previous: always in show mode,
        /// otherwise only when its type is not filtered.
        /// </summary>
        public bool IsVisible(string type)
        {
            return ShowFiltered || !IsFiltered(type);
        }

        /// <summary>The tick/cross: flips the filter for this type's whole sibling group.</summary>
        public void Toggle(string type)
        {
            string group = GroupOf(type);
            if (!filteredGroups.Remove(group))
            {
                filteredGroups.Add(group);
            }
        }

        /// <summary>Clears every filter (the state on game open, before restoring).</summary>
        public void ClearAll()
        {
            filteredGroups.Clear();
            filteredTypeGroups.Clear();
            ShowFiltered = false;
        }

        /// <summary>
        /// The magnifier is offered only when some type present this turn is filtered (the
        /// original's companion "present this turn" bitmap).
        /// </summary>
        public bool IsMagnifierVisible(IEnumerable<string> typesPresentThisTurn)
        {
            return typesPresentThisTurn.Any(IsFiltered);
        }

        /// <summary>The magnifier for numeric types present this turn.</summary>
        public bool IsMagnifierVisible(IEnumerable<int> typesPresentThisTurn)
        {
            return typesPresentThisTurn.Any(IsFiltered);
        }

        /// <summary>Index of the next visible message after <paramref name="current"/>, or -1.</summary>
        /// <param name="include">An optional view scope (for example the selected category): an
        /// index is only reachable while this returns true.</param>
        public int Next(IReadOnlyList<string> types, int current, Func<int, bool> include = null)
        {
            for (int i = Math.Max(current + 1, 0); i < types.Count; i++)
            {
                if ((include == null || include(i)) && IsVisible(types[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Index of the previous visible message before <paramref name="current"/>, or -1.</summary>
        public int Previous(IReadOnlyList<string> types, int current, Func<int, bool> include = null)
        {
            for (int i = Math.Min(current - 1, types.Count - 1); i >= 0; i--)
            {
                if ((include == null || include(i)) && IsVisible(types[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The first message shown when the viewer opens: the first visible one, or -1.
        /// </summary>
        public int First(IReadOnlyList<string> types, Func<int, bool> include = null)
        {
            return Next(types, -1, include);
        }

        /// <summary>Next visible numeric type after <paramref name="current"/>, or -1.</summary>
        public int Next(IReadOnlyList<int> types, int current)
        {
            for (int i = Math.Max(current + 1, 0); i < types.Count; i++)
            {
                if (IsVisible(types[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Previous visible numeric type before <paramref name="current"/>, or -1.</summary>
        public int Previous(IReadOnlyList<int> types, int current)
        {
            for (int i = Math.Min(current - 1, types.Count - 1); i >= 0; i--)
            {
                if (IsVisible(types[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The first visible numeric type, or -1.</summary>
        public int First(IReadOnlyList<int> types)
        {
            return Next(types, -1);
        }

        /// <summary>
        /// The magnifier click / minus key: switches hide/show mode and returns the new selection.
        /// The current message stays selected if its type is already filtered (switching to show
        /// mode) or unfiltered (switching back); otherwise the selection moves to the next such
        /// message, or to the previous one if none follows. If there is no such message at all the
        /// selection stays put in show mode (every message is visible there) and is cleared (-1)
        /// in hide mode.
        /// </summary>
        public int ToggleShowFiltered(IReadOnlyList<string> types, int current, Func<int, bool> include = null)
        {
            ShowFiltered = !ShowFiltered;
            bool wantFiltered = ShowFiltered;

            bool InScope(int index) => include == null || include(index);
            bool Matches(int index) => InScope(index) && IsFiltered(types[index]) == wantFiltered;

            if (current >= 0 && current < types.Count && Matches(current))
            {
                return current;
            }

            for (int i = Math.Max(current + 1, 0); i < types.Count; i++)
            {
                if (Matches(i))
                {
                    return i;
                }
            }

            for (int i = Math.Min(current - 1, types.Count - 1); i >= 0; i--)
            {
                if (Matches(i))
                {
                    return i;
                }
            }

            return ShowFiltered && current >= 0 && current < types.Count && InScope(current) ? current : -1;
        }

        /// <summary>
        /// The magnifier click / minus key for numeric types: switches hide/show mode and returns
        /// the new selection (same rule as the string overload).
        /// </summary>
        public int ToggleShowFiltered(IReadOnlyList<int> types, int current)
        {
            ShowFiltered = !ShowFiltered;
            bool wantFiltered = ShowFiltered;

            bool Matches(int index) => IsFiltered(types[index]) == wantFiltered;

            if (current >= 0 && current < types.Count && Matches(current))
            {
                return current;
            }

            for (int i = Math.Max(current + 1, 0); i < types.Count; i++)
            {
                if (Matches(i))
                {
                    return i;
                }
            }

            for (int i = Math.Min(current - 1, types.Count - 1); i >= 0; i--)
            {
                if (Matches(i))
                {
                    return i;
                }
            }

            return ShowFiltered && current >= 0 && current < types.Count ? current : -1;
        }

        /// <summary>
        /// The player's persisted filter file, next to their other client files in the game folder
        /// (the original keeps it in the player's history file, record type 33).
        /// </summary>
        public static string FilePath(string gameFolder, string raceName)
        {
            return Path.Combine(gameFolder ?? "", (raceName ?? "player") + ".messagefilter");
        }

        /// <summary>
        /// Game open: start all-clear, then restore the player's saved filter if there is one.
        /// A missing or unreadable file just leaves the filter clear.
        /// </summary>
        public static MessageFilter Load(string path)
        {
            MessageFilter filter = new MessageFilter();
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            filter.filteredGroups.Add(line.Trim());
                        }
                    }
                }
            }
            catch (IOException)
            {
                filter.ClearAll();
            }
            catch (UnauthorizedAccessException)
            {
                filter.ClearAll();
            }

            return filter;
        }

        /// <summary>Saves the filtered groups (best effort; the mode is not persisted).</summary>
        public void Save(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                File.WriteAllLines(path, filteredGroups.OrderBy(group => group, StringComparer.OrdinalIgnoreCase));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// The message view's four category-selection controls (client-ui-dialog-catalog.md,
    /// Messages: "It includes four category-selection controls"; "The four category controls at
    /// the top of the pane select the message list view, not the filter").
    ///
    /// <b>SEAM / SPEC GAP.</b> The spec confirms there are exactly four controls and that they
    /// scope the message list rather than the per-type filter, and that changing a category
    /// refreshes the current-message area - but it does not name the four categories or define
    /// each one's membership. <see cref="MessageCategories.Includes(MessageCategory,
    /// MessageDestinationKind)"/> is therefore a named stand-in that scopes on the message's
    /// already-spec-defined Goto target kind: All (everything), Planets (Planet or
    /// ProductionQueue), Fleets (Fleet or BattleReplay) and Other (everything else). Reported as a
    /// SPEC GAP; the membership is not asserted from the spec by the tests.
    /// </summary>
    public enum MessageCategory
    {
        /// <summary>Every message (the default view).</summary>
        All,

        /// <summary>Messages whose Goto target is a planet or its production queue.</summary>
        Planets,

        /// <summary>Messages whose Goto target is a fleet or a battle replay.</summary>
        Fleets,

        /// <summary>Every message that is not a planet or fleet message.</summary>
        Other,
    }

    /// <summary>The four message-view categories and their stand-in membership rule.</summary>
    public static class MessageCategories
    {
        /// <summary>The four controls, in display order (All first, the default).</summary>
        public static IReadOnlyList<MessageCategory> All { get; } = new[]
        {
            MessageCategory.All,
            MessageCategory.Planets,
            MessageCategory.Fleets,
            MessageCategory.Other,
        };

        /// <summary>The control's caption.</summary>
        public static string Name(MessageCategory category)
        {
            switch (category)
            {
                case MessageCategory.Planets:
                    return "Planets";
                case MessageCategory.Fleets:
                    return "Fleets";
                case MessageCategory.Other:
                    return "Other";
                default:
                    return "All";
            }
        }

        /// <summary>
        /// True when a message with this destination kind is listed under the category. See the
        /// <see cref="MessageCategory"/> remark: this mapping is the named stand-in for the spec's
        /// unnamed four categories.
        /// </summary>
        public static bool Includes(MessageCategory category, MessageDestinationKind kind)
        {
            if (category == MessageCategory.All)
            {
                return true;
            }

            bool planet = kind == MessageDestinationKind.Planet
                || kind == MessageDestinationKind.ProductionQueue;
            bool fleet = kind == MessageDestinationKind.Fleet
                || kind == MessageDestinationKind.BattleReplay;

            switch (category)
            {
                case MessageCategory.Planets:
                    return planet;
                case MessageCategory.Fleets:
                    return fleet;
                default:
                    return !planet && !fleet;
            }
        }

        /// <summary>True when this message is listed under the category.</summary>
        public static bool Includes(MessageCategory category, Message message, IEnumerable<string> knownPlanets = null)
        {
            return Includes(category, MessageRouting.Destination(message, knownPlanets).Kind);
        }
    }
}
