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
// This port deliberately simplifies two things the spec itself flags as
// either unquantified or genuinely unresolved (see that section and
// turn-generation-engine.md's "Fleet-linked habitat-tolerance drift" note):
// - Placement uses a plain minimum-distance check rather than the spec's own
//   "four squared-distance tiers" scoring, since no concrete distances survive
//   for those tiers.
// - Detection is a simple "visible once a fleet is within range" model
//   rather than the spec's per-turn probabilistic (0-99-vs-cloak%) roll,
//   since building a full parallel per-empire Intel/report pipeline for one
//   object type - mirroring Minefield's, which this class otherwise mirrors
//   structurally - is a disproportionate side-investment for this pass. The
//   heavy-mineral-cargo transit side effect (relocation for gas-tolerant
//   races, habitat-tolerance drift for ordinary ones) is left unimplemented
//   entirely for the same reason: several of the traits/thresholds it
//   depends on are explicitly unidentified even in the source material.
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
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

        public Wormhole()
        {
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
                    switch (subnode.Name.ToLower())
                    {
                        case "pairedkey":
                            PairedKey = long.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "stabilitytier":
                            StabilityTier = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
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
