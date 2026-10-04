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

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// The two rename surfaces (behavior-specs-10/client-ui-dialog-catalog.md "Rename surfaces"):
    /// both apply "length and field validation" and commit only on accept; the ORDINARY surface
    /// also filters and rewrites its text on every keystroke (<see cref="LiveFilter"/>), the
    /// COMPACT surface validates only once, at accept time (<see cref="ValidateOnAccept"/>), and
    /// words its prompt by whether the target slot was empty (<see cref="CompactPrompt"/>).
    /// SPEC GAP: the length limit, the characters the live filter removes or rewrites, and the
    /// prompt wording are not given. Neutral seams: <see cref="MaxNameLength"/> = 0 (no limit),
    /// the live filter removes control characters only, and the accept-time check refuses an
    /// empty (all-blank) name and, where the caller passes sibling names, a duplicate.
    /// </summary>
    public static class RenameRules
    {
        /// <summary>The longest accepted name; 0 means no limit (SPEC GAP, see the class note).</summary>
        public const int MaxNameLength = 0;

        /// <summary>The ordinary surface's per-keystroke filter.</summary>
        public static string LiveFilter(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder filtered = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsControl(c))
                {
                    filtered.Append(c);
                }
            }

            string result = filtered.ToString();
            if (MaxNameLength > 0 && result.Length > MaxNameLength)
            {
                result = result.Substring(0, MaxNameLength);
            }

            return result;
        }

        /// <summary>The name as it is stored: filtered and trimmed.</summary>
        public static string Normalise(string text)
        {
            return LiveFilter(text).Trim();
        }

        /// <summary>
        /// Accept-time validation, shared by both surfaces. Returns null when the name may be
        /// committed, otherwise the reason it was refused.
        /// </summary>
        public static string ValidateOnAccept(string text, IEnumerable<string> siblingNames = null)
        {
            string name = Normalise(text);
            if (name.Length == 0)
            {
                return "A name is required.";
            }

            if (MaxNameLength > 0 && name.Length > MaxNameLength)
            {
                return "That name is too long.";
            }

            if (siblingNames != null && siblingNames.Any(sibling => string.Equals(sibling, name, StringComparison.Ordinal)))
            {
                return "That name is already in use.";
            }

            return null;
        }

        /// <summary>The compact surface's prompt: naming an empty slot versus renaming a used one.</summary>
        public static string CompactPrompt(bool slotWasEmpty)
        {
            return slotWasEmpty ? "Name this new entry:" : "Rename this entry:";
        }
    }
}
