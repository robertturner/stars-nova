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

    /// <summary>
    /// The message viewer's per-type filter (behavior-specs-10/client-ui-dialog-catalog.md,
    /// Messages, "the 392-bit bitmap is a per-message-type filter"):
    /// - a set bit means "filtered"; the tick/cross toggle flips the filter for the current
    ///   message's type, and sibling types always change together;
    /// - the filter is cleared to nothing-filtered when a game is opened, then restored from the
    ///   player's own file, so it persists per player across turns;
    /// - a magnifier appears only when some type present this turn is filtered, and switches
    ///   between "hide filtered messages" (default) and "show filtered messages";
    /// - in hide mode Next/Previous skip filtered messages, in show mode they step through all.
    ///
    /// The original's filter is indexed by a 9-bit numeric message type. Nova's Message.Type is a
    /// free-form string, and far coarser (one "Stargate" type covers what the original splits
    /// into a dozen ids), so the filter here is keyed on a group name: <see cref="GroupOf"/> maps
    /// each Nova type string onto the spec's sibling groups (only the battle-report run 145-168
    /// has more than one Nova type string today; every other type is its own group of one, as in
    /// the spec). Pure logic with no UI dependency, so it is unit-testable and shared by the
    /// desktop and Android Messages panels.
    /// </summary>
    public sealed class MessageFilter
    {
        /// <summary>The group name used for a message with no Type at all.</summary>
        public const string GeneralGroup = "General";

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

        /// <summary>
        /// "Show filtered messages" mode (the magnifier); false is the default "hide filtered".
        /// </summary>
        public bool ShowFiltered { get; set; }

        /// <summary>The filtered groups, for persistence and display.</summary>
        public IReadOnlyCollection<string> FilteredGroups => filteredGroups;

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

        /// <summary>Index of the next visible message after <paramref name="current"/>, or -1.</summary>
        public int Next(IReadOnlyList<string> types, int current)
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

        /// <summary>Index of the previous visible message before <paramref name="current"/>, or -1.</summary>
        public int Previous(IReadOnlyList<string> types, int current)
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

        /// <summary>
        /// The first message shown when the viewer opens: the first visible one, or -1.
        /// </summary>
        public int First(IReadOnlyList<string> types)
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
        public int ToggleShowFiltered(IReadOnlyList<string> types, int current)
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

            return ShowFiltered && current < types.Count ? current : -1;
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
}
