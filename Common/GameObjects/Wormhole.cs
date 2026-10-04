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

        /// <summary>0 ("Rock Solid," lasting 30+ years) to 6 ("Very Unstable," relocating within
        /// about 5 years) - docs/behavior-specs-4/fleet-movement-scanning-cargo.md's confirmed
        /// 7-tier (0-6) stability scale.</summary>
        public int StabilityTier;

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

        public Wormhole()
        {
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
            Global.SaveData(xmldoc, xmlelWormhole, "StabilityTier", StabilityTier.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (UsedBy.Count > 0)
            {
                Global.SaveData(xmldoc, xmlelWormhole, "UsedBy", string.Join(",", UsedBy.OrderBy(id => id).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
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

                        case "stabilitytier":
                            StabilityTier = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "usedby":
                            foreach (string id in ((XmlText)subnode.FirstChild).Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                UsedBy.Add(int.Parse(id.Trim(), System.Globalization.CultureInfo.InvariantCulture));
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
