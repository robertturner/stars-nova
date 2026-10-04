#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

namespace Nova.Client.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;

    /// <summary>Why a recent-file entry cannot be opened.</summary>
    public enum RecentFileProblem
    {
        None,

        /// <summary>The path has no file extension.</summary>
        NoExtension,

        /// <summary>The file no longer exists.</summary>
        Missing,
    }

    /// <summary>
    /// The File menu's Recent Files list (behavior-specs-10/client-interface.md "Application
    /// shell": a 9-slot list that reuses the ordinary Open load pathway; command table ids
    /// 4300-4308: "loads that slot's file through the same path as Open; a missing file or one
    /// without an extension gives an error box"; menu bar: the items are "captioned with an
    /// underlined digit and the path" and sit between the two adjacent separators).
    /// Persisted in the client's config file under <see cref="PreferenceKey"/>, paths separated
    /// by <see cref="Separator"/>.
    /// SPEC GAP / Ambiguity: the spec does not say how the list is ordered or when an entry is
    /// added. Reading used: most recently opened first; opening a file already in the list moves
    /// it to the top (no duplicates, compared case-insensitively); a tenth file drops the oldest.
    /// A failed open keeps the entry (the spec only says an error box is shown).
    /// </summary>
    public sealed class RecentFiles
    {
        public const string PreferenceKey = "RecentFiles";

        /// <summary>Number of slots (spec: 9, menu ids 4300-4308).</summary>
        public const int Capacity = 9;

        /// <summary>Stored-value separator (a character no Windows path can contain).</summary>
        public const char Separator = '|';

        private readonly List<string> entries = new List<string>();

        public IReadOnlyList<string> Entries => entries;

        public static RecentFiles Parse(string stored)
        {
            RecentFiles list = new RecentFiles();
            if (string.IsNullOrWhiteSpace(stored))
            {
                return list;
            }

            foreach (string part in stored.Split(Separator))
            {
                string path = part.Trim();
                if (path.Length > 0 && list.IndexOf(path) < 0 && list.entries.Count < Capacity)
                {
                    list.entries.Add(path);
                }
            }

            return list;
        }

        public string Format()
        {
            return string.Join(Separator.ToString(), entries);
        }

        /// <summary>Records a file as just opened: it moves to (or enters at) the top.</summary>
        public void Add(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            path = path.Trim();
            int existing = IndexOf(path);
            if (existing >= 0)
            {
                entries.RemoveAt(existing);
            }

            entries.Insert(0, path);
            if (entries.Count > Capacity)
            {
                entries.RemoveRange(Capacity, entries.Count - Capacity);
            }
        }

        public bool Remove(string path)
        {
            int index = IndexOf(path);
            if (index < 0)
            {
                return false;
            }

            entries.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// The menu caption of slot <paramref name="index"/> (0-8): the slot digit 1-9 as the
        /// access key, then the path. Uses the Avalonia access-key marker "_" before the digit and
        /// doubles any "_" in the path so it is shown literally.
        /// </summary>
        public static string MenuCaption(int index, string path)
        {
            string digit = (index + 1).ToString(CultureInfo.InvariantCulture);
            return "_" + digit + " " + (path ?? string.Empty).Replace("_", "__");
        }

        /// <summary>The spec's two open-time failures: no extension, then a missing file.</summary>
        public static RecentFileProblem Check(string path, Func<string, bool> fileExists)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(Path.GetExtension(path)))
            {
                return RecentFileProblem.NoExtension;
            }

            if (fileExists != null && !fileExists(path))
            {
                return RecentFileProblem.Missing;
            }

            return RecentFileProblem.None;
        }

        private int IndexOf(string path)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i], path, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
