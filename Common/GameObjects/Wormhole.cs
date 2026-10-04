#region Copyright Notice
// ============================================================================
// Copyright (C) 2009, 2010 stars-nova
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

#region Module Description
// ===========================================================================
// A naturally-occurring, free, uncapped-mass shortcut between two points in
// deep space - docs/behavior-specs-4/fleet-movement-scanning-cargo.md's
// "Wormholes" section. Always exists in a pair (two Wormhole objects, each
// pointing at the other via PairedKey); a fleet reaching either end
// transits to the other the same year it arrives.
//
// Placement uses a plain minimum-distance check rather than the spec's
// "four squared-distance tiers" scoring, since no concrete distances survive
// for those tiers.
//
// Detection follows behavior-specs-10/fleet-movement-scanning-cargo.md §3:
// a flat radius test plus a 0-99 roll against the 75% cloak until the empire
// has discovered the wormhole once (ScanStep.DetectWormholes,
// ScannerRules.DetectsWormhole); each empire's sightings are kept as
// WormholeIntel in EmpireData.WormholeReports, which also carries the
// stability tier seen and whether that race has used the wormhole
// (UsedBy here; the per-race bitmask the AI's diversion scoring reads,
// ai-opponent-behavior.md §12).
//
// There is no "heavy-mineral-cargo wormhole transit" effect: the spec
// re-attributes that routine to the Mystery Trader encounter
// (turn-generation-engine.md §5a).
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    [Serializable]
    public class Wormhole : Mappable
    {
        /// <summary>The other end of this wormhole pair's Key. 0 (Global.Nobody) if the pair link
        /// hasn't been resolved yet (should never persist that way past generation).</summary>
        public long PairedKey;

        /// <summary>
        /// The end's base stability, uniform 0-2, drawn once at creation (bits 0-1 of the status
        /// word). It is kept across jumps (behavior-specs-11/fleet-movement-scanning-cargo.md
        /// "Wormhole lifecycle, complete rule", item 4).
        /// </summary>
        public int BaseStability;

        /// <summary>
        /// The end's age in years (bits 2-11 of the status word, wrapping at 1,024); 0 at
        /// creation, reset to 0 by a jump and otherwise +1 every generation.
        /// </summary>
        public int Age;

        /// <summary>
        /// 0 ("Rock Solid," lasting 30+ years) to 6 ("Very Unstable," relocating within about 5
        /// years) - docs/behavior-specs-11/fleet-movement-scanning-cargo.md's "Wormhole
        /// lifecycle, complete rule": base + (age / 5, truncated) - 2, clamped to 0-6. The
        /// original stores only the base and the age; the tier is derived from them, so setting
        /// this directly (legacy saves, tests) decomposes to base 0 + an age that yields the tier.
        /// </summary>
        public int StabilityTier
        {
            get { return Math.Max(0, Math.Min(6, BaseStability + (Age / 5) - 2)); }
            set
            {
                BaseStability = 0;
                Age = 5 * (Math.Max(0, Math.Min(6, value)) + 2);
            }
        }

        /// <summary>
        /// The races (empire ids) that have used this wormhole end: the wormhole's per-race
        /// bitmask (word +10) that the AI's diversion scoring tests as "known"
        /// (behavior-specs-10/ai-opponent-behavior.md §12, personality 0 colony ships step 3:
        /// "A wormhole this race has not used scores 90 ... else 50; a known one scores
        /// 70 - 10 x its stability tier"). The spec does not say which code sets the bit; this
        /// port sets it for both ends when one of the race's fleets transits the pair
        /// (TurnGenerator.TryWormholeTransit).
        /// </summary>
        public HashSet<int> UsedBy = new HashSet<int>();

        /// <summary>
        /// The races (empire ids) that currently have this wormhole end *located*: the original's
        /// located mask (word +8). Set when one of the race's scanners detects the end, and by a
        /// transit on the exit end only; cleared for all races when that end jumps
        /// (behavior-specs-11/fleet-movement-scanning-cargo.md §3). A located end is seen at the
        /// full normal scanner range; an unlocated one only at r/4 or within penetrating range.
        /// </summary>
        public HashSet<int> Located = new HashSet<int>();

        public Wormhole()
        {
        }

        /// <summary>True when the race (empire id) currently has this end located.</summary>
        public bool IsLocatedBy(int empireId)
        {
            return Located != null && Located.Contains(empireId);
        }

        /// <summary>True when the race (empire id) has used this wormhole end.</summary>
        public bool IsUsedBy(int empireId)
        {
            return UsedBy.Contains(empireId);
        }

        /// <summary>
        /// Save: Generate an XmlElement representation of the Wormhole for saving to file.
        /// </summary>
        public new XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelWormhole = xmldoc.CreateElement("Wormhole");

            xmlelWormhole.AppendChild(base.ToXml(xmldoc));

            Global.SaveData(xmldoc, xmlelWormhole, "PairedKey", PairedKey.ToString("X"));
            Global.SaveData(xmldoc, xmlelWormhole, "BaseStability", BaseStability.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelWormhole, "Age", Age.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (UsedBy.Count > 0)
            {
                Global.SaveData(xmldoc, xmlelWormhole, "UsedBy", string.Join(",", UsedBy.OrderBy(id => id).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            }

            if (Located != null && Located.Count > 0)
            {
                Global.SaveData(xmldoc, xmlelWormhole, "Located", string.Join(",", Located.OrderBy(id => id).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            }

            return xmlelWormhole;
        }

        /// <summary>
        /// Load: initializing Constructor from an xml node.
        /// </summary>
        public Wormhole(XmlNode node)
            : base(node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "pairedkey":
                            PairedKey = long.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "basestability":
                            BaseStability = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "age":
                            Age = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        // Legacy saves stored the derived tier alone; treat it as the tier at age 0.
                        case "stabilitytier":
                            StabilityTier = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "usedby":
                            foreach (string id in ((XmlText)subnode.FirstChild).Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                UsedBy.Add(int.Parse(id.Trim(), System.Globalization.CultureInfo.InvariantCulture));
                            }

                            break;

                        case "located":
                            foreach (string id in ((XmlText)subnode.FirstChild).Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                Located.Add(int.Parse(id.Trim(), System.Globalization.CultureInfo.InvariantCulture));
                            }

                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error("Error loading Wormhole : " + e.Message);
                }
                subnode = subnode.NextSibling;
            }
        }
    }
}
