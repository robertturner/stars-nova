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

namespace Nova.Common.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Xml;

    /// <summary>
    /// Sets or clears the owner's "detonate" flag on one of its minefields
    /// (<see cref="Minefield.Detonate"/>). Sources:
    /// - turn-generation-engine.md section 3: "Minefields are edited via the same order-queuing
    ///   mechanism used everywhere else - a minefield-window command issues a turn order that is
    ///   applied during this pass, rather than mutating the minefield immediately";
    /// - save-turn-file-format.md, order opcode 0x23: resolves the minefield by id, "requires the
    ///   acting race to own it" and toggles a single bit;
    /// - race-traits.md section 2, Space Demolition: "can remotely detonate its own standard
    ///   minefields".
    /// While the flag is set the yearly minefield pass damages every fleet inside the field and
    /// raises its decay by 25 points (fleet-movement-scanning-cargo.md section 5 "Detonation";
    /// MinefieldDecayStep).
    ///
    /// A minefield lives in ServerData, not EmpireData, so <see cref="ApplyToState"/> (the shared
    /// ICommand path) only checks the empire side; the server applies the flag through
    /// <see cref="ApplyToMinefields"/> (TurnGenerator.ParseCommands) and the client through the
    /// same method on its own copy of the field (ClientData.InputTurn.AllMinefields).
    /// AMBIGUITY: the race-traits wording "standard minefields" is read as MinefieldType.Standard
    /// only; the turn-generation and order-file passages do not restate the type or trait gate.
    /// </summary>
    public class DetonateCommand : ICommand
    {
        /// <summary>The minefield's Key (owner in the key's empire bits).</summary>
        public long MinefieldKey { get; set; }

        /// <summary>The new value of the field's detonate flag.</summary>
        public bool Detonate { get; set; }

        public DetonateCommand(long minefieldKey, bool detonate)
        {
            MinefieldKey = minefieldKey;
            Detonate = detonate;
        }

        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        public DetonateCommand(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;

            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "minefieldkey":
                        MinefieldKey = long.Parse(subnode.FirstChild.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        break;

                    case "detonate":
                        Detonate = bool.Parse(subnode.FirstChild.Value);
                        break;
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>
        /// True for the Space Demolition owner of the minefield (the key's empire bits name the
        /// owner, as for every Item).
        /// </summary>
        public bool IsValid(EmpireData empire)
        {
            if (empire == null || empire.Race == null)
            {
                return false;
            }

            return MinefieldKey.Owner() == empire.Id && CanDetonate(empire.Race);
        }

        /// <summary>Nothing on the empire changes; see <see cref="ApplyToMinefields"/>.</summary>
        public void ApplyToState(EmpireData empire)
        {
        }

        /// <summary>
        /// Sets the flag on the named field when it exists, is owned by the issuing empire and is
        /// a standard field. Returns true when the field was changed.
        /// </summary>
        public bool ApplyToMinefields(IDictionary<long, Minefield> minefields, EmpireData empire)
        {
            if (minefields == null || !IsValid(empire))
            {
                return false;
            }

            if (!minefields.TryGetValue(MinefieldKey, out Minefield field) || !CanDetonate(field, empire))
            {
                return false;
            }

            field.Detonate = Detonate;
            return true;
        }

        /// <summary>The race may order detonations at all (Space Demolition).</summary>
        public static bool CanDetonate(Race race)
        {
            return race != null && race.HasTrait("SD");
        }

        /// <summary>The empire may set or clear this field's detonate flag: its own standard
        /// field, and the empire is Space Demolition.</summary>
        public static bool CanDetonate(Minefield field, EmpireData empire)
        {
            return field != null
                && empire != null
                && field.Owner == empire.Id
                && field.FieldType == MinefieldType.Standard
                && CanDetonate(empire.Race);
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "Detonate");
            Global.SaveData(xmldoc, xmlelCom, "MinefieldKey", MinefieldKey.ToString("X", CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelCom, "Detonate", Detonate.ToString(CultureInfo.InvariantCulture));
            return xmlelCom;
        }
    }
}
