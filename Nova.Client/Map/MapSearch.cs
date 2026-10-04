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

namespace Nova.Client.Map
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>How a Find query matched.</summary>
    public enum FindMatch
    {
        None,
        PlanetExact,
        PlanetPrefix,
        FleetByNumber,
        FleetByName,
    }

    /// <summary>The single result of a Find query (null <see cref="Entry"/> = "no result").</summary>
    public sealed class FindResult
    {
        public static readonly FindResult NotFound = new FindResult(null, FindMatch.None);

        public FindResult(MapObjectEntry entry, FindMatch match)
        {
            Entry = entry;
            Match = match;
        }

        public MapObjectEntry Entry { get; }

        public FindMatch Match { get; }

        public bool Found => Entry != null;
    }

    /// <summary>
    /// The Find dialog's matching rule (behavior-specs-11/client-ui-dialog-catalog.md "Search and
    /// record browser", "Find Planet or Fleet"; commands 4200/4201, template 4202). Comparisons
    /// ignore the case of the letters A-Z only (whole strings and prefixes); there is no trimming
    /// of the query and no substring search. The order of the tests on OK:
    /// <list type="number">
    /// <item>Every planet of the universe, in planet-number order, is compared with the query as a
    /// whole name. The first exact match wins at once. While no exact match has been seen, the
    /// first planet whose name begins with the query is remembered.</item>
    /// <item>Fleet number. The query is read as a fleet number: an optional leading word for
    /// "fleet" (dynamic string 1256, compared over its own length, after which six characters are
    /// skipped), spaces, an optional number sign, spaces, then a decimal number starting with a
    /// digit 1-9. If the number ends before reaching 513 and the viewer owns a fleet with that
    /// number, that fleet wins. Only the viewer's own fleets can be found by number.</item>
    /// <item>Every fleet the viewer knows (own and foreign, in the client's fleet-list order) is
    /// compared as a whole with its displayed name. The first exact match wins. Fleets are never
    /// matched by prefix.</item>
    /// <item>The remembered prefix-matching planet, if any.</item>
    /// <item>Otherwise no result (the caller shows the stop-icon message, string 526).</item>
    /// </list>
    /// An empty query therefore finds planet 1 (every name starts with the empty string). A planet
    /// always beats a fleet with the same exact name, a fleet number beats a fleet name, and an
    /// exact fleet name beats a planet prefix.
    /// SEAMS for spec-silent details (reported): the caller supplies the objects with planets in
    /// planet-number order and fleets in fleet-list order (the map model's own order); the fleet's
    /// per-owner number and "is the viewer's own fleet" are supplied by the caller. Dynamic string
    /// 1256 is taken to be the word "Fleet" (the spec's own gloss).
    /// </summary>
    public static class MapSearch
    {
        /// <summary>The leading word of a fleet-number query (dynamic string 1256 seam).</summary>
        public const string FleetWord = "Fleet";

        /// <summary>Characters skipped after that word (the spec's "six characters are skipped").</summary>
        public const int FleetWordSkip = 6;

        /// <summary>Fleet numbers are searchable below this value (the spec's "before reaching 513").</summary>
        public const int FleetNumberLimit = 513;

        /// <summary>The Find field's edit limit (the spec's "limited to 39 characters").</summary>
        public const int QueryMaxLength = 39;

        /// <summary>
        /// Runs the five-step rule over <paramref name="objects"/>.
        /// </summary>
        /// <param name="objects">The objects the player knows: planets in planet-number order and
        /// fleets in fleet-list order.</param>
        /// <param name="query">The query text, compared without trimming.</param>
        /// <param name="fleetNumber">The fleet's per-owner number (used by step 2). Null disables
        /// the fleet-number step.</param>
        /// <param name="isOwnFleet">Whether the entry is one of the viewer's own fleets (used by
        /// step 2). Null treats every fleet as own.</param>
        public static FindResult Find(
            IEnumerable<MapObjectEntry> objects,
            string query,
            Func<MapObjectEntry, int> fleetNumber = null,
            Func<MapObjectEntry, bool> isOwnFleet = null)
        {
            string text = query ?? string.Empty;
            List<MapObjectEntry> all = (objects ?? Enumerable.Empty<MapObjectEntry>()).Where(entry => entry != null).ToList();
            List<MapObjectEntry> planets = all.Where(entry => entry.Kind == MapObjectKind.Planet).ToList();
            List<MapObjectEntry> fleets = all.Where(entry => entry.Kind == MapObjectKind.Fleet).ToList();

            // 1. Planets, in order: first exact match wins; remember the first prefix match.
            MapObjectEntry prefixPlanet = null;
            foreach (MapObjectEntry planet in planets)
            {
                if (EqualsAz(planet.Name, text))
                {
                    return new FindResult(planet, FindMatch.PlanetExact);
                }

                if (prefixPlanet == null && StartsAz(planet.Name, text))
                {
                    prefixPlanet = planet;
                }
            }

            // 2. The viewer's own fleet by number.
            if (fleetNumber != null && TryParseFleetNumber(text, out int number))
            {
                foreach (MapObjectEntry fleet in fleets)
                {
                    if ((isOwnFleet == null || isOwnFleet(fleet)) && fleetNumber(fleet) == number)
                    {
                        return new FindResult(fleet, FindMatch.FleetByNumber);
                    }
                }
            }

            // 3. Any known fleet by exact displayed name (never by prefix).
            foreach (MapObjectEntry fleet in fleets)
            {
                if (EqualsAz(fleet.Name, text))
                {
                    return new FindResult(fleet, FindMatch.FleetByName);
                }
            }

            // 4. The remembered prefix-matching planet.
            if (prefixPlanet != null)
            {
                return new FindResult(prefixPlanet, FindMatch.PlanetPrefix);
            }

            // 5. No result.
            return FindResult.NotFound;
        }

        /// <summary>The single result to focus, or null for "no result".</summary>
        public static MapObjectEntry FindFirst(
            IEnumerable<MapObjectEntry> objects,
            string query,
            Func<MapObjectEntry, int> fleetNumber = null,
            Func<MapObjectEntry, bool> isOwnFleet = null)
        {
            return Find(objects, query, fleetNumber, isOwnFleet).Entry;
        }

        /// <summary>
        /// Reads the query as a fleet number (the spec's step 2): an optional leading "Fleet"
        /// word (after which six characters are skipped), spaces, an optional "#", spaces, then a
        /// decimal number starting with 1-9 that must end the query and stay below 513.
        /// </summary>
        public static bool TryParseFleetNumber(string query, out int number)
        {
            number = 0;
            string text = query ?? string.Empty;
            int i = 0;

            if (text.Length >= FleetWord.Length && StartsAz(text, FleetWord))
            {
                i = FleetWordSkip;
            }

            while (i < text.Length && text[i] == ' ')
            {
                i++;
            }

            if (i < text.Length && text[i] == '#')
            {
                i++;
            }

            while (i < text.Length && text[i] == ' ')
            {
                i++;
            }

            if (i >= text.Length || text[i] < '1' || text[i] > '9')
            {
                return false;
            }

            int value = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                value = (value * 10) + (text[i] - '0');
                i++;
            }

            if (i != text.Length || value >= FleetNumberLimit)
            {
                return false;
            }

            number = value;
            return true;
        }

        /// <summary>Whole-string comparison ignoring the case of A-Z only.</summary>
        public static bool EqualsAz(string left, string right)
        {
            string a = left ?? string.Empty;
            string b = right ?? string.Empty;
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (FoldAz(a[i]) != FoldAz(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Prefix test ignoring the case of A-Z only.</summary>
        public static bool StartsAz(string text, string prefix)
        {
            string a = text ?? string.Empty;
            string b = prefix ?? string.Empty;
            if (b.Length > a.Length)
            {
                return false;
            }

            for (int i = 0; i < b.Length; i++)
            {
                if (FoldAz(a[i]) != FoldAz(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static char FoldAz(char c)
        {
            return c >= 'a' && c <= 'z' ? (char)(c - 32) : c;
        }
    }
}
