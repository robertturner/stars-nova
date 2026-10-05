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
    using System.Globalization;

    /// <summary>
    /// The race-name/summary string builder (behavior-specs-10/client-ui-dialog-catalog.md "Title
    /// and status area": a race's display name from its singular and plural names, parameterised
    /// by singular vs plural, an optional "a"/"an" article "chosen by the name's leading sound",
    /// an optional possessive/adjective suffix, and - when no plural name was set - an automatic
    /// English plural: append "s" to "the singular name with trailing spaces removed", "unless the
    /// name already ends in "s" or in "se"" (race-designer-ui-and-availability.md "Identity and
    /// archetype stage", "Derived plural"). That ending set is exactly "s" and "se", not every
    /// sibilant + "e".
    /// SPEC GAP: the suffix used for those two endings, and the possessive/adjective suffix's
    /// text, are not given. Stand-ins: <see cref="AlternatePluralSuffix"/> (empty: such a name is
    /// used unchanged), <see cref="PossessiveSuffix"/> ("'s"), <see cref="Sibilants"/> ("s").
    /// Ambiguity: "leading sound" is read as the first letter being a vowel (a, e, i, o, u).
    /// </summary>
    public static class RaceNameText
    {
        /// <summary>SPEC GAP seam: the plural ending for a name already ending in "s" or "se".</summary>
        public const string AlternatePluralSuffix = "";

        /// <summary>SPEC GAP seam: the possessive/adjective ending.</summary>
        public const string PossessiveSuffix = "'s";

        /// <summary>
        /// SPEC GAP seam: the "X" that makes "-Xe" a no-s ending. The spec names exactly one such
        /// pattern, "se" (race-designer-ui-and-availability.md "Identity and archetype stage",
        /// "Derived plural"), so this holds only "s"; do not broaden it to every sibilant + "e".
        /// </summary>
        public const string Sibilants = "s";

        /// <summary>The plural form: the stored plural name, else the automatic plural.</summary>
        public static string Plural(string singular, string plural)
        {
            if (!string.IsNullOrWhiteSpace(plural))
            {
                return plural.Trim();
            }

            // "the singular name with trailing spaces removed and an 's' appended"
            string name = (singular ?? string.Empty).TrimEnd();
            if (name.Length == 0)
            {
                return name;
            }

            return name + (TakesAlternateSuffix(name) ? AlternatePluralSuffix : "s");
        }

        /// <summary>Builds the display form.</summary>
        public static string Build(string singular, string plural, bool usePlural, bool withArticle, bool possessive)
        {
            string name = usePlural ? Plural(singular, plural) : (singular ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return name;
            }

            if (possessive)
            {
                name += PossessiveSuffix;
            }

            if (withArticle)
            {
                name = (StartsWithVowel(name) ? "an " : "a ") + name;
            }

            return name;
        }

        /// <summary>
        /// The main window title: application name, then the game and the player's race (plural)
        /// and year when a game is open. SPEC GAP: the title's wording is not given; Nova's own.
        /// </summary>
        public static string WindowTitle(string applicationName, string gameName, string raceSingular, string racePlural, int year)
        {
            string race = Plural(raceSingular, racePlural);
            if (string.IsNullOrEmpty(race))
            {
                return applicationName;
            }

            string game = string.IsNullOrWhiteSpace(gameName) ? string.Empty : gameName.Trim() + " - ";
            return string.Format(CultureInfo.InvariantCulture, "{0} - {1}{2}, {3}", applicationName, game, race, year);
        }

        private static bool TakesAlternateSuffix(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith("s", StringComparison.Ordinal))
            {
                return true;
            }

            return lower.Length >= 2 && lower[lower.Length - 1] == 'e' && Sibilants.IndexOf(lower[lower.Length - 2]) >= 0;
        }

        private static bool StartsWithVowel(string name)
        {
            return "aeiouAEIOU".IndexOf(name[0]) >= 0;
        }
    }
}
