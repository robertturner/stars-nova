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
    using System.Collections.Generic;

    /// <summary>Who owns a map object, from the viewing race's point of view.</summary>
    public enum MapOwnership
    {
        /// <summary>The viewing race.</summary>
        Own,

        /// <summary>Nobody (or not known).</summary>
        Unowned,

        /// <summary>Another race.</summary>
        Other,
    }

    /// <summary>
    /// Map text builders.
    /// - <see cref="Identify"/>: the shared object-identification phrase used by the under-cursor
    ///   tooltip (behavior-specs-10/client-interface.md, "Object-identification text builder"):
    ///   another race's object gets that race's name substituted into an "owned by ___" template;
    ///   an object carrying orbital special equipment picks an alternate template depending on
    ///   whether a qualifying (Stargate) component is present; everything else falls back to a
    ///   plain category-name template; nothing under the cursor reads "Deep Space".
    /// - <see cref="PlanetTooltip"/>: the planet hover tooltip (client-ui-dialog-catalog.md, mode 7):
    ///   the planet's name, an ownership line that differs for own / unowned / another race's (the
    ///   last with a scan-gated extra line), and a habitability percentage line when the data
    ///   allows it.
    /// SPEC GAP: the templates' exact wording is not given anywhere in the specs; every literal
    /// below is a neutral placeholder kept in one place (the constants) for the spec writer to fix.
    /// </summary>
    public static class MapObjectText
    {
        public const string DeepSpaceText = "Deep Space";
        public const string OwnedByTemplate = "{0} owned by {1}";
        public const string StargateTemplate = "{0} (with Stargate)";
        public const string OrbitalTemplate = "{0} (with starbase)";
        public const string OwnedByYouLine = "Owned by you";
        public const string UnownedLine = "Unowned";
        public const string OwnedByOtherLine = "Owned by {0}";
        public const string PopulationLine = "Population: {0:N0}";
        public const string HabitabilityLine = "Habitability: {0}%";

        /// <summary>Plain category names (the fallback template).</summary>
        public static string CategoryName(MapObjectKind kind)
        {
            switch (kind)
            {
                case MapObjectKind.Planet: return "Planet";
                case MapObjectKind.Fleet: return "Fleet";
                case MapObjectKind.Minefield: return "Minefield";
                case MapObjectKind.Wormhole: return "Wormhole";
                case MapObjectKind.Packet: return "Mineral Packet";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// The identification phrase for one object (null kind = nothing under the cursor).
        /// <paramref name="hasOrbitalEquipment"/> is true for a planet whose starbase is known;
        /// <paramref name="hasStargate"/> selects the Stargate alternate of that template.
        /// </summary>
        public static string Identify(MapObjectKind? kind, string name, MapOwnership ownership, string ownerRaceName, bool hasOrbitalEquipment = false, bool hasStargate = false)
        {
            if (kind == null)
            {
                return DeepSpaceText;
            }

            string subject = string.IsNullOrEmpty(name) ? CategoryName(kind.Value) : name;

            if (hasOrbitalEquipment)
            {
                subject = string.Format(hasStargate ? StargateTemplate : OrbitalTemplate, subject);
            }

            if (ownership == MapOwnership.Other && !string.IsNullOrEmpty(ownerRaceName))
            {
                return string.Format(OwnedByTemplate, subject, ownerRaceName);
            }

            return subject;
        }

        /// <summary>
        /// The planet hover tooltip lines. <paramref name="scannedPopulation"/> is the scan-gated
        /// extra line for another race's planet (null when the scan did not reveal it);
        /// <paramref name="habitabilityPercent"/> is null when the environment is unknown.
        /// </summary>
        public static List<string> PlanetTooltip(string name, MapOwnership ownership, string ownerRaceName, int? scannedPopulation, int? habitabilityPercent)
        {
            var lines = new List<string> { name ?? string.Empty };

            switch (ownership)
            {
                case MapOwnership.Own:
                    lines.Add(OwnedByYouLine);
                    break;
                case MapOwnership.Unowned:
                    lines.Add(UnownedLine);
                    break;
                default:
                    lines.Add(string.Format(OwnedByOtherLine, string.IsNullOrEmpty(ownerRaceName) ? "another race" : ownerRaceName));
                    if (scannedPopulation != null)
                    {
                        lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, PopulationLine, scannedPopulation.Value));
                    }

                    break;
            }

            if (habitabilityPercent != null)
            {
                lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, HabitabilityLine, habitabilityPercent.Value));
            }

            return lines;
        }
    }
}
