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
    using System.Globalization;

    using Nova.Common;

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
    /// The inputs the mode-7 planet population popup needs
    /// (behavior-specs-11/client-ui-dialog-catalog.md, "Mode 7", and
    /// behavior-specs-11/client-interface.md, "Object identification text" which points at it).
    /// Population-like figures are in the original's stored units of 100 colonists, as the popup
    /// prints them: the stored figure followed by two zeros. <see cref="DefenceNibble"/> is -1
    /// when the viewer has no defence reading at all, so no third sentence is emitted (0 is the
    /// real "no planetary defences" value).
    /// </summary>
    public sealed class PlanetPopupFacts
    {
        /// <summary>The planet's name (the bold-font run of the popup).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Who owns the planet, from the viewer's point of view.</summary>
        public MapOwnership Ownership { get; set; }

        /// <summary>The viewer's report level for the planet (the original's planet byte 4).</summary>
        public int ReportLevel { get; set; }

        /// <summary>The viewer's own planet's population, in 100-colonist units.</summary>
        public int Population { get; set; }

        /// <summary>Another race's scanned population estimate (the 12-bit field times 4).</summary>
        public int PopulationEstimate { get; set; }

        /// <summary>The viewer's habitability value (the integer -45..100 evaluator).</summary>
        public int Habitability { get; set; }

        /// <summary>The viewer's maximum population for the planet, in 100-colonist units; below 1
        /// the habitability sentence is left out.</summary>
        public int MaxPopulation { get; set; }

        /// <summary>The coming year's growth, in 100-colonist units.</summary>
        public int Growth { get; set; }

        /// <summary>The report's defence nibble (top 4 bits of planet word +0x12), or -1 unknown.</summary>
        public int DefenceNibble { get; set; } = -1;
    }

    /// <summary>
    /// Map text builders.
    /// - The per-kind object-name builders and their dispatcher <see cref="Identify"/>
    ///   (behavior-specs-11/client-interface.md, "Object identification text: the object-name
    ///   builders and the map status strip"): each builder writes one line. A planet is its name
    ///   ("orbiting" plus the name for an orbiting reference); a fleet is an owner prefix, a body
    ///   (the player's given name, else the main design cut to 28 characters with a plus sign when
    ///   the fleet holds more than one design) and, only for an unnamed fleet, " #N"; a minefield is
    ///   the owner prefix, its type word and the minefield noun; a packet is the owner prefix and
    ///   the mineral-packet noun (or the salvage word); a wormhole/Mystery Trader/other mystery
    ///   object is the matching noun; and no object is the deep-space caption, or "space" with the
    ///   two coordinates. There is no "owned by" template and no Stargate-dependent alternate.
    /// - <see cref="PopulationPopup"/>: popup mode 7 (client-ui-dialog-catalog.md, "Mode 7"), up to
    ///   three word-wrapped sentences assembled from the numbered dynamic-string fragments.
    /// SPEC GAP: the fragment texts are not given anywhere in the specs (the dynamic string table
    /// is not recovered), so every literal below is a project-chosen placeholder kept in one place
    /// (the constants) for the spec writer to replace. The sentence layout, order, gating and
    /// number formatting are the spec's.
    /// </summary>
    public static class MapObjectText
    {
        // ---- per-kind object-name placeholders ----

        public const string DeepSpaceText = "Deep Space";
        public const string SpaceText = "space";
        public const string OrbitingPrefix = "orbiting ";
        public const string FleetNoun = "Fleet";
        public const string MinefieldNoun = "Minefield";
        public const string MineralPacketNoun = "Mineral Packet";
        public const string SalvageNoun = "Salvage";
        public const string WormholeNoun = "Wormhole";
        public const string MysteryTraderNoun = "Mystery Trader";
        public const string MysteryObjectNoun = "Mystery Object";

        /// <summary>Placeholder for the owner prefix when the race has no name (the original uses
        /// the player-number caption of dynamic string 1374 with a possessive ending).</summary>
        public const string UnnamedRaceText = "another race";

        /// <summary>The longest main-design run kept in an unnamed fleet's line.</summary>
        public const int FleetDesignLimit = 28;

        // ---- popup mode 7 fragment placeholders (numbers are the dynamic-string ids) ----

        public const string PopOwnPrefix580 = "Your population on ";
        public const string PopCount591 = " is ";
        public const string PopNobody584 = " has no population.";
        public const string PopEnemy581 = "Enemy planet ";
        public const string PopEstimate582 = ", estimated population ";
        public const string PopUnknown583 = ", population unknown.";
        public const string PopLoss585 = " is losing ";
        public const string PopLossTail590 = " per year";
        public const string PopLossOwn586 = " of your colonists.";
        public const string PopLossOther587 = " of any colonists that settle there.";
        public const string PopMaxOwn588 = " can support ";
        public const string PopMaxTail576 = " of your colonists.";
        public const string PopMaxOther579 = "Colonizing ";
        public const string PopMaxOther578 = " would allow ";
        public const string PopMaxOther577 = "as many as ";
        public const string PopGrow530 = "The population on ";
        public const string PopGrow531 = " will grow by ";
        public const string PopGrowTotalJoin = " to ";
        public const string PopGrowNone532 = " will not grow next year.";
        public const string PopDefenceNone483 = " has no planetary defences.";
        public const string PopDefence484 = " has planetary defences with ";
        public const string PopDefenceCoverageSuffix = "% coverage.";

        /// <summary>Plain category names (the fallback template).</summary>
        public static string CategoryName(MapObjectKind kind)
        {
            switch (kind)
            {
                case MapObjectKind.Planet: return "Planet";
                case MapObjectKind.Fleet: return FleetNoun;
                case MapObjectKind.Minefield: return MinefieldNoun;
                case MapObjectKind.Wormhole: return WormholeNoun;
                case MapObjectKind.Packet: return MineralPacketNoun;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// The owner prefix used by the fleet and special-object builders: empty for the viewer's
        /// own objects and for unowned ones, otherwise the owner's singular race name and one space.
        /// </summary>
        public static string OwnerPrefix(MapOwnership ownership, string ownerRaceName)
        {
            if (ownership != MapOwnership.Other)
            {
                return string.Empty;
            }

            string race = string.IsNullOrEmpty(ownerRaceName) ? UnnamedRaceText : ownerRaceName;
            return race + " ";
        }

        /// <summary>A planet is its name; an orbiting reference is string 869, "orbiting" + name.</summary>
        public static string PlanetName(string name, bool orbiting = false)
        {
            string value = name ?? string.Empty;
            return orbiting ? OrbitingPrefix + value : value;
        }

        /// <summary>
        /// A fleet line. A player-named fleet is the prefix plus that name and no number. Otherwise
        /// it is the prefix, the main design (cut to 28 characters, plus a plus sign when the fleet
        /// holds more than one design, or the generic fleet noun when the design is unknown), a
        /// space, a number sign and the per-owner fleet number.
        /// </summary>
        public static string FleetName(MapOwnership ownership, string ownerRaceName, string givenName, string mainDesignName, int fleetNumber, bool multipleDesigns)
        {
            string prefix = OwnerPrefix(ownership, ownerRaceName);
            if (!string.IsNullOrEmpty(givenName))
            {
                return prefix + givenName;
            }

            string body = string.IsNullOrEmpty(mainDesignName)
                ? FleetNoun
                : Truncate(mainDesignName, FleetDesignLimit);
            if (multipleDesigns)
            {
                body += "+";
            }

            return prefix + body + " #" + fleetNumber.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A minefield is the owner prefix, its type word and the minefield noun.</summary>
        public static string MinefieldName(MapOwnership ownership, string ownerRaceName, MinefieldType type)
        {
            return OwnerPrefix(ownership, ownerRaceName) + MinefieldTypeWord(type) + " " + MinefieldNoun;
        }

        /// <summary>A packet is the owner prefix and the mineral-packet noun, or the salvage word
        /// when it is not moving.</summary>
        public static string PacketName(MapOwnership ownership, string ownerRaceName, bool salvage)
        {
            return OwnerPrefix(ownership, ownerRaceName) + (salvage ? SalvageNoun : MineralPacketNoun);
        }

        /// <summary>A wormhole is its noun.</summary>
        public static string WormholeName()
        {
            return WormholeNoun;
        }

        /// <summary>A Mystery Trader is its noun.</summary>
        public static string MysteryTraderName()
        {
            return MysteryTraderNoun;
        }

        /// <summary>Any other special object is the generic mystery-object noun.</summary>
        public static string MysteryObjectName()
        {
            return MysteryObjectNoun;
        }

        /// <summary>No object: the deep-space caption when there is no position, otherwise "space"
        /// with the two coordinates.</summary>
        public static string DeepSpaceName(int? x, int? y)
        {
            if (x == null || y == null)
            {
                return DeepSpaceText;
            }

            return SpaceText + " (" + x.Value.ToString(CultureInfo.InvariantCulture)
                + ", " + y.Value.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// The identification phrase for one object (null kind = nothing under the cursor), picking
        /// the per-kind builder. Where a caller already has a display name but not the builder's own
        /// ingredients (minefields and packets), the name is used after the owner prefix.
        /// </summary>
        public static string Identify(MapObjectKind? kind, string name, MapOwnership ownership, string ownerRaceName)
        {
            if (kind == null)
            {
                return DeepSpaceText;
            }

            switch (kind.Value)
            {
                case MapObjectKind.Planet:
                    return PlanetName(name);

                case MapObjectKind.Fleet:
                    return string.IsNullOrEmpty(name)
                        ? FleetName(ownership, ownerRaceName, null, null, 0, false)
                        : FleetName(ownership, ownerRaceName, name, null, 0, false);

                case MapObjectKind.Minefield:
                    return string.IsNullOrEmpty(name)
                        ? MinefieldName(ownership, ownerRaceName, MinefieldType.Standard)
                        : OwnerPrefix(ownership, ownerRaceName) + name;

                case MapObjectKind.Wormhole:
                    return WormholeName();

                case MapObjectKind.Packet:
                    return string.IsNullOrEmpty(name)
                        ? PacketName(ownership, ownerRaceName, false)
                        : OwnerPrefix(ownership, ownerRaceName) + name;

                default:
                    return name ?? string.Empty;
            }
        }

        /// <summary>
        /// The mode-7 population popup: up to three sentences.
        /// 1. By owner: the viewer's own (580, name, 591, population, full stop), an unowned planet
        ///    (name, 584), or another player's (581, name, then 582 and the report estimate at
        ///    report level 3+, else 583).
        /// 2. Habitability, only at report level 3+: a negative v gives the yearly loss (585, |v|/10
        ///    with one decimal and a percent sign, 590, and 586/587); a non-negative v with a positive
        ///    C gives the maximum population (588/576 own, or 579/578/577/576 other).
        /// 3. Own planet with v at least 0 and population below C: 530, name and either 531 with the
        ///    growth and the new total or 532. Another player's: name and 483 (no defences) or 484
        ///    with the coverage. Unowned planets get no third sentence.
        /// </summary>
        public static List<string> PopulationPopup(PlanetPopupFacts facts)
        {
            var sentences = new List<string>();
            string name = facts.Name ?? string.Empty;

            switch (facts.Ownership)
            {
                case MapOwnership.Own:
                    sentences.Add(PopOwnPrefix580 + name + PopCount591 + FormatPopulation(facts.Population) + ".");
                    break;

                case MapOwnership.Unowned:
                    sentences.Add(name + PopNobody584);
                    break;

                default:
                    sentences.Add(PopEnemy581 + name + (facts.ReportLevel >= 3
                        ? PopEstimate582 + FormatPopulation(facts.PopulationEstimate) + "."
                        : PopUnknown583));
                    break;
            }

            if (facts.ReportLevel >= 3)
            {
                if (facts.Habitability < 0)
                {
                    string loss = (Math.Abs(facts.Habitability) / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                    sentences.Add(name + PopLoss585 + loss + PopLossTail590
                        + (facts.Ownership == MapOwnership.Own ? PopLossOwn586 : PopLossOther587));
                }
                else if (facts.MaxPopulation >= 1)
                {
                    sentences.Add(facts.Ownership == MapOwnership.Own
                        ? name + PopMaxOwn588 + FormatPopulation(facts.MaxPopulation) + PopMaxTail576
                        : PopMaxOther579 + name + PopMaxOther578 + PopMaxOther577 + FormatPopulation(facts.MaxPopulation) + PopMaxTail576);
                }
            }

            if (facts.Ownership == MapOwnership.Own)
            {
                if (facts.Habitability >= 0 && facts.Population < facts.MaxPopulation)
                {
                    bool growing = facts.Habitability > 0 && facts.Growth > 0;
                    sentences.Add(PopGrow530 + name + (growing
                        ? PopGrow531 + FormatPopulation(facts.Growth) + PopGrowTotalJoin + FormatPopulation(facts.Population + facts.Growth) + "."
                        : PopGrowNone532));
                }
            }
            else if (facts.Ownership == MapOwnership.Other && facts.DefenceNibble >= 0)
            {
                sentences.Add(facts.DefenceNibble == 0
                    ? name + PopDefenceNone483
                    : name + PopDefence484 + (facts.DefenceNibble * 6 + 3).ToString(CultureInfo.InvariantCulture) + PopDefenceCoverageSuffix);
            }

            return sentences;
        }

        /// <summary>
        /// The original's population formatting: the stored figure (units of 100 colonists) followed
        /// by two zeros, with a zero population as a single zero.
        /// </summary>
        public static string FormatPopulation(int figure)
        {
            return figure == 0 ? "0" : figure.ToString(CultureInfo.InvariantCulture) + "00";
        }

        /// <summary>The minefield type's word (standard, heavy or speed bump).</summary>
        public static string MinefieldTypeWord(MinefieldType type)
        {
            switch (type)
            {
                case MinefieldType.Heavy: return "heavy";
                case MinefieldType.SpeedBump: return "speed bump";
                default: return "standard";
            }
        }

        private static string Truncate(string value, int length)
        {
            return value.Length <= length ? value : value.Substring(0, length);
        }
    }
}
