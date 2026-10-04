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

namespace Nova.Common
{
    using System;
    using System.Xml;

    using Nova.Common.DataStructures;

    /// <summary>
    /// An empire's record of one wormhole end it has detected: where it was last seen and in
    /// which year, the stability tier seen then, and whether this race has used it. Having a
    /// record also means the wormhole has been discovered once, so it is no longer 75% cloaked
    /// to this empire (behavior-specs-10/fleet-movement-scanning-cargo.md sections 3 and 5).
    /// Written by the server's ScanStep.
    /// </summary>
    [Serializable]
    public class WormholeIntel : Mappable
    {
        /// <summary>The key of the wormhole's other end (the pair link).</summary>
        public long PairedKey;

        /// <summary>The year the wormhole was last detected at <see cref="Mappable.Position"/>.</summary>
        public int Year;

        /// <summary>The stability tier, 0 ("Rock Solid") to 6 ("Very Unstable"), when last
        /// detected (fleet-movement-scanning-cargo.md, "Wormhole placement and stability").</summary>
        public int StabilityTier;

        /// <summary>
        /// This race has used the wormhole: its bit in the wormhole's per-race bitmask
        /// (Wormhole.UsedBy). The AI's diversion scoring calls such a wormhole "known" and scores
        /// it 70 - 10 x <see cref="StabilityTier"/> (ai-opponent-behavior.md §12). The race's own
        /// knowledge, so ScanStep refreshes it every year whether or not the end is in range.
        /// </summary>
        public bool UsedByUs;

        public WormholeIntel()
        {
        }

        public WormholeIntel(Wormhole wormhole, int year)
        {
            Key = wormhole.Key;
            Name = wormhole.Name;
            Position = new NovaPoint(wormhole.Position);
            PairedKey = wormhole.PairedKey;
            Year = year;
            StabilityTier = wormhole.StabilityTier;
        }

        /// <summary>Refreshes the record from a fresh detection.</summary>
        public void Update(Wormhole wormhole, int year)
        {
            Position = new NovaPoint(wormhole.Position);
            PairedKey = wormhole.PairedKey;
            Year = year;
            StabilityTier = wormhole.StabilityTier;
        }

        public new XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelIntel = xmldoc.CreateElement("WormholeIntel");

            xmlelIntel.AppendChild(base.ToXml(xmldoc));

            Global.SaveData(xmldoc, xmlelIntel, "PairedKey", PairedKey.ToString("X"));
            Global.SaveData(xmldoc, xmlelIntel, "Year", Year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelIntel, "StabilityTier", StabilityTier.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (UsedByUs)
            {
                Global.SaveData(xmldoc, xmlelIntel, "UsedByUs", "true");
            }

            return xmlelIntel;
        }

        public WormholeIntel(XmlNode node)
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

                        case "year":
                            Year = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "stabilitytier":
                            StabilityTier = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "usedbyus":
                            UsedByUs = bool.Parse(((XmlText)subnode.FirstChild).Value);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error("Error loading WormholeIntel : " + e.Message);
                }

                subnode = subnode.NextSibling;
            }
        }
    }
}
