#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
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
// Definition of a Minefield. Note that it over-rides the Key method to provide
// a simple incrementing number each time a new minefield is created. This
// ensures that each minefield can be used in hash tables without having to
// specify a "name" for the minefield.
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    /// <summary>
    /// The three minefield types (the field record's type byte at +0xc: 0 Standard, 1 Heavy,
    /// 2 Speed Bump). behavior-specs-10/fleet-movement-scanning-cargo.md section 5, "Minefield
    /// rules, code-confirmed".
    /// </summary>
    public enum MinefieldType
    {
        Standard = 0,
        Heavy = 1,
        SpeedBump = 2
    }

    [Serializable]
    public class Minefield : Mappable
    {
        /// <summary>Safe warp per type, before the racial allowance (raw table +0xeb2): 4/6/5.</summary>
        public static readonly int[] SafeWarpByType = { 4, 6, 5 };

        /// <summary>Hit chance per light-year per warp above safe, out of 1,000 (+0xeb8): 3/10/35.</summary>
        public static readonly int[] HitRatePerMilleByType = { 3, 10, 35 };

        /// <summary>Damage per ship, ordinary fleet (+0xea6): 100/500/0.</summary>
        public static readonly int[] DamagePerShipByType = { 100, 500, 0 };

        /// <summary>Damage per ship, "scoop" fleet (an engine burning no fuel at warp 4): 125/600/0.</summary>
        public static readonly int[] ScoopDamagePerShipByType = { 125, 600, 0 };

        /// <summary>Fleet minimum damage, ordinary fleet (+0xe9a): 500/2000/0.</summary>
        public static readonly int[] FleetMinimumByType = { 500, 2000, 0 };

        /// <summary>Fleet minimum damage, scoop fleet: 600/2500/0.</summary>
        public static readonly int[] ScoopFleetMinimumByType = { 600, 2500, 0 };

        public int NumberOfMines;
        public int SafeSpeed = 4;

        /// <summary>
        /// The field's type; Standard unless saved otherwise. Named FieldType because the
        /// inherited Item.Type is the object's ItemType.
        /// </summary>
        public MinefieldType FieldType = MinefieldType.Standard;

        /// <summary>
        /// The owner's "detonate" flag (behavior-specs-10/turn-generation-engine.md §3, step 18;
        /// fleet-movement-scanning-cargo.md §5 "Detonation"): while it is set, the yearly
        /// minefield pass damages every fleet inside the field once (no roll, no stop) and the
        /// field's decay rate rises by 25 percentage points. Space Demolition's ability; the
        /// order that sets it is not part of this class.
        /// </summary>
        public bool Detonate;

        /// <summary>
        /// The per-race *known* mask (the original's word +10): empire ids whose scanners have
        /// detected this field or whose fleet has struck it. It is never cleared, so a known field
        /// only needs the full normal range to be seen again, where an unknown one needs the
        /// quarter range or a penetrating scanner (behavior-specs-11/fleet-movement-scanning-
        /// cargo.md §3, "Minefield detection, complete rule"). The owner always sees its own field
        /// and is not listed. The per-generation *seen* mask is
        /// <see cref="EmpireData.VisibleMinefields"/>, recomputed by ScanStep each year.
        /// </summary>
        public HashSet<int> Known = new HashSet<int>();

        private static int keyId; // TODO (priority 5) Minefield key will be shared amonst all minefields. Lacks a non-static unique id.

        /// <summary>
        /// Default constructor.
        /// </summary>
        public Minefield()
        {
            keyId++;
        }

        /// <summary>
        /// Determine the spatial radius of a Minefield. 
        /// </summary>
        public int Radius
        {
            get
            {
                return (int)Math.Sqrt(NumberOfMines);
            }
        }

        /// <summary>True when the empire owns the field or has ever known it (<see cref="Known"/>).</summary>
        public bool IsKnownTo(int empireId)
        {
            return empireId == Owner || (Known != null && Known.Contains(empireId));
        }

        /// <summary>Marks the field known to a race: a scan detection, a mine hit or a sweep. The
        /// owner needs no mark (it always sees its own field).</summary>
        public void MarkKnown(int empireId)
        {
            if (empireId != Owner)
            {
                (Known ??= new HashSet<int>()).Add(empireId);
            }
        }

        /// <summary>
        /// Generate an XmlElement representation of the Minefield for saving to file.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representing the Minefield.</returns>
        public new XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelMinefield = xmldoc.CreateElement("Minefield");

            xmlelMinefield.AppendChild(base.ToXml(xmldoc));

            Global.SaveData(xmldoc, xmlelMinefield, "NumberOfMines", NumberOfMines.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelMinefield, "SafeSpeed", SafeSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelMinefield, "MinefieldType", FieldType.ToString());
            // "keyId" is no longer written: it was a process-wide static counter of every Minefield
            // ever constructed (never read by game logic), so the saved text depended on process
            // history rather than on the game - fatal to repeatable saves. Old files that still
            // carry it load as before.
            if (Detonate)
            {
                Global.SaveData(xmldoc, xmlelMinefield, "Detonate", "true");
            }

            if (Known != null && Known.Count > 0)
            {
                Global.SaveData(xmldoc, xmlelMinefield, "Known", string.Join(",", Known.OrderBy(id => id)));
            }

            return xmlelMinefield;
        }

        /// <summary>
        /// Load: initializing Constructor from an xml node.
        /// </summary>
        /// <param name="node">A <see cref="Minefield"/> node Nova save file (xml document).</param>
        public Minefield(XmlNode node)
            : base(node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "numberofmines":
                            NumberOfMines = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "safespeed":
                            SafeSpeed = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "minefieldtype":
                            FieldType = (MinefieldType)Enum.Parse(typeof(MinefieldType), ((XmlText)subnode.FirstChild).Value, true);
                            break;

                        case "keyid":
                            keyId = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "detonate":
                            Detonate = bool.Parse(((XmlText)subnode.FirstChild).Value);
                            break;

                        case "known":
                        case "visibleto": // old saves wrote the persistent mask under this name
                            foreach (string id in ((XmlText)subnode.FirstChild).Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                Known.Add(int.Parse(id, System.Globalization.CultureInfo.InvariantCulture));
                            }

                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error("Error loading Minefield : " + e.Message);
                }
                subnode = subnode.NextSibling;
            }   
        }
    }
}
