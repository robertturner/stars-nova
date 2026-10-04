#region Copyright Notice
// ============================================================================
// Copyright (C) 2012 The Stars-Nova Project
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
    /// A decaying mineral concentration left in deep space - the destination of battle salvage
    /// (see BattleEngine.Run) when a battle happens somewhere other than over a planet. Unlike
    /// planet-side salvage (which is added directly to the star's ResourcesOnHand and never
    /// decays), a deep-space concentration loses 10% of each mineral type, or 10kT, whichever is
    /// larger, per year. See docs/behavior-specs-5/combat-resolution.md §7.
    /// </summary>
    [Serializable]
    public class DeepSpaceMinerals
    {
        public NovaPoint Position { get; set; }

        public Resources Minerals { get; set; } = new Resources();

        /// <summary>
        /// The one-year decay grace flag (behavior-specs-10/turn-generation-engine.md §1 step 21
        /// and §3: bit 0x40 of a stationary salvage record, set by the wreckage routine
        /// FUN_10f0_1850 and cleared by step 18's decay pass instead of decaying that year).
        /// Saved so a reload between generations keeps it.
        /// </summary>
        public bool DecayGrace { get; set; }

        /// <summary>True once every mineral type has decayed to zero - ServerData discards a
        /// concentration once it reaches this state, rather than keeping empty entries around
        /// forever.</summary>
        public bool IsEmpty => Minerals.Ironium <= 0 && Minerals.Boranium <= 0 && Minerals.Germanium <= 0 && Minerals.Energy <= 0;

        public DeepSpaceMinerals()
        {
            Position = new NovaPoint();
        }

        public DeepSpaceMinerals(NovaPoint position)
        {
            Position = new NovaPoint(position);
        }

        /// <summary>
        /// Step 18's pass over this resting record (behavior-specs-10/turn-generation-engine.md
        /// §3): while the grace flag is set it is cleared and nothing decays; otherwise one
        /// year's <see cref="Decay"/> applies. Returns true when the record decayed.
        /// </summary>
        public bool DecayOrUseGrace()
        {
            if (DecayGrace)
            {
                DecayGrace = false;
                return false;
            }

            Decay();
            return true;
        }

        /// <summary>
        /// Applies one year's decay: each mineral type loses 10% of its current amount, or
        /// 10kT, whichever is larger, never below zero (behavior-specs-10/turn-generation-
        /// engine.md §3, resting packets; combat-resolution.md §7). The 10% is taken rounded
        /// down (amount x 10 / 100, integer) - the spec does not state the rounding (Ambiguity),
        /// and this matches the in-flight packet decay's MineralPacketRules.DecayLoss reading.
        /// Energy is never left as wreckage (BattleEngine.AddWreckage) but is decayed the same
        /// way for old saves that carried it.
        /// </summary>
        public void Decay()
        {
            Minerals.Ironium = DecayAmount(Minerals.Ironium);
            Minerals.Boranium = DecayAmount(Minerals.Boranium);
            Minerals.Germanium = DecayAmount(Minerals.Germanium);
            Minerals.Energy = DecayAmount(Minerals.Energy);
        }

        private static int DecayAmount(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int decay = Math.Max((int)((long)amount * 10 / 100), 10);
            return Math.Max(0, amount - decay);
        }

        /// <summary>
        /// Load: initializing constructor from an XmlNode representation.
        /// </summary>
        public DeepSpaceMinerals(XmlNode node)
        {
            Position = new NovaPoint();

            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "position":
                            Position = new NovaPoint(subnode);
                            break;
                        case "minerals":
                            Minerals = new Resources(subnode);
                            break;
                        case "decaygrace":
                            DecayGrace = bool.Parse(subnode.FirstChild.Value);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error("Error loading DeepSpaceMinerals : " + e.Message);
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>
        /// Save: Generate an XmlElement representation for saving to file.
        /// </summary>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelDeepSpaceMinerals = xmldoc.CreateElement("DeepSpaceMinerals");

            xmlelDeepSpaceMinerals.AppendChild(Position.ToXml(xmldoc, "Position"));
            xmlelDeepSpaceMinerals.AppendChild(Minerals.ToXml(xmldoc, "Minerals"));
            if (DecayGrace)
            {
                Global.SaveData(xmldoc, xmlelDeepSpaceMinerals, "DecayGrace", "true");
            }

            return xmlelDeepSpaceMinerals;
        }
    }
}
