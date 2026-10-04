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

namespace Nova.Common.DataStructures
{
    using System;
    using System.Text;

    /// <summary>
    /// The game's file kinds that the client-private backslash markers name
    /// (behavior-specs-10/dynamic-string-table.md §5.2).
    /// </summary>
    public enum GameFileKind
    {
        /// <summary>The game / current file.</summary>
        Game,

        /// <summary>The turn file.</summary>
        Turn,

        /// <summary>The player log (orders) file.</summary>
        PlayerLog,

        /// <summary>The host file.</summary>
        Host,

        /// <summary>The universe-definition file.</summary>
        Universe,

        /// <summary>The history file.</summary>
        History,
    }

    /// <summary>
    /// The second substitution convention of the decoded string templates
    /// (behavior-specs-10/dynamic-string-table.md §5.2): a backslash followed by a single lowercase
    /// letter, used in the file-related messages, where the letter selects which of the game's
    /// file kinds to name. The string lookup itself substitutes nothing; a wrapper expands the
    /// markers to real file names - this is that wrapper, as a pure function.
    /// </summary>
    /// <remarks>
    /// SPEC GAP (reported): §5.2 lists the six file kinds the observed markers cover but not which
    /// letter selects which kind, and the string data file (dynamic-strings.txt) is not in the
    /// repo, so the letter-to-kind mapping is not built in: the caller supplies a resolver from
    /// the marker letter to the text to insert (for example by way of its own letter table and
    /// <see cref="GameFileKind"/>). Rules implemented: only a backslash followed by a lowercase
    /// ASCII letter is a marker; the resolver returning null leaves the marker as written; any
    /// other backslash (before a non-letter, an uppercase letter, or at the end) is copied
    /// unchanged. The printf-style specifiers (%d, %s, ...) are the other convention and are not
    /// touched here.
    /// </remarks>
    public static class BackslashTemplate
    {
        /// <summary>True when <paramref name="c"/> can follow a backslash to form a marker.</summary>
        public static bool IsMarkerLetter(char c)
        {
            return c >= 'a' && c <= 'z';
        }

        /// <summary>
        /// Expands every backslash-letter marker in <paramref name="template"/> through
        /// <paramref name="resolve"/> (marker letter to replacement text; null keeps the marker).
        /// </summary>
        public static string Expand(string template, Func<char, string> resolve)
        {
            if (string.IsNullOrEmpty(template))
            {
                return template;
            }

            if (resolve == null)
            {
                throw new ArgumentNullException(nameof(resolve));
            }

            StringBuilder result = new StringBuilder(template.Length);
            for (int i = 0; i < template.Length; i++)
            {
                char c = template[i];
                if (c == '\\' && i + 1 < template.Length && IsMarkerLetter(template[i + 1]))
                {
                    string replacement = resolve(template[i + 1]);
                    if (replacement != null)
                    {
                        result.Append(replacement);
                        i++;
                        continue;
                    }
                }

                result.Append(c);
            }

            return result.ToString();
        }

        /// <summary>The marker letters present in <paramref name="template"/>, in order of appearance.</summary>
        public static string MarkerLetters(string template)
        {
            StringBuilder letters = new StringBuilder();
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            for (int i = 0; i + 1 < template.Length; i++)
            {
                if (template[i] == '\\' && IsMarkerLetter(template[i + 1]))
                {
                    letters.Append(template[i + 1]);
                    i++;
                }
            }

            return letters.ToString();
        }
    }
}
