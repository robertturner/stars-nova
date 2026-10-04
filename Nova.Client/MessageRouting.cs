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

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>Where activating a message takes the player.</summary>
    public enum MessageDestinationKind
    {
        /// <summary>Nothing beyond selecting the message.</summary>
        None,

        /// <summary>The battle's replay (Message.Event is the BattleReport).</summary>
        BattleReplay,

        /// <summary>The named planet's production queue (messages 62/63, and 175-180).</summary>
        ProductionQueue,

        /// <summary>Select the named planet.</summary>
        Planet,

        /// <summary>The Research panel.</summary>
        Research,

        /// <summary>The Technology Browser.</summary>
        TechnologyBrowser,
    }

    /// <summary>A message's destination and, for planet destinations, the planet's name.</summary>
    public sealed class MessageDestination
    {
        public MessageDestination(MessageDestinationKind kind, string planetName = null)
        {
            Kind = kind;
            PlanetName = planetName;
        }

        public MessageDestinationKind Kind { get; }

        public string PlanetName { get; }
    }

    /// <summary>
    /// Message activation (client-ui-dialog-catalog.md "Message-click routing": types 62, 63 and
    /// 175-180 open the production queue of the planet the message names instead of only
    /// selecting it; client-interface.md "Senders other than the menu": the Goto action opens the
    /// Research (126) or Technology Browser (256) view for the kinds that point at those views,
    /// and a planet subject selects the planet). Nova message types are strings, so the numeric
    /// types are matched through <see cref="ProductionNoticeTypes"/> (62/63; 175-180 are never
    /// posted, production-queue.md section 8) and the existing "TechAdvance"/"NewComponent".
    /// AMBIGUITY: which numeric message kinds post 126 vs 256 is not tabulated; a tech-level
    /// notice is read as Research and a new-component notice as the Technology Browser.
    /// </summary>
    public static class MessageRouting
    {
        public const string TechAdvanceType = "TechAdvance";

        public const string NewComponentType = "NewComponent";

        /// <summary>The Message.Type of every mineral-packet notice (the server's
        /// PacketLaunch.MessageType: launch, merge, disintegration, capture, bombardment).</summary>
        public const string MineralPacketType = "Mineral Packet";

        public static MessageDestination Destination(Message message)
        {
            return Destination(message, null);
        }

        /// <summary>
        /// As <see cref="Destination(Message)"/>; <paramref name="knownPlanets"/> (the planets the
        /// player knows of) lets a mineral-packet notice select the planet it is about. Every
        /// packet notice is about one planet - the launching planet (launched, enlarged,
        /// disintegrated, no target) or the receiving one (caught, bombarded, struck) - and names
        /// it first, so the subject is the known planet named earliest in the text (the longest
        /// name wins at the same position). A packet notice whose Event already carries the
        /// planet's name uses that instead.
        /// </summary>
        public static MessageDestination Destination(Message message, IEnumerable<string> knownPlanets)
        {
            if (message == null)
            {
                return new MessageDestination(MessageDestinationKind.None);
            }

            if (message.Event is BattleReport)
            {
                return new MessageDestination(MessageDestinationKind.BattleReplay);
            }

            string planet = message.Event as string;
            if (ProductionNoticeTypes.IsProductionNotice(message.Type) && !string.IsNullOrEmpty(planet))
            {
                return new MessageDestination(
                    ProductionNoticeTypes.OpensProductionQueue(message.Type) ? MessageDestinationKind.ProductionQueue : MessageDestinationKind.Planet,
                    planet);
            }

            if (message.Type == MineralPacketType)
            {
                string subject = !string.IsNullOrEmpty(planet) ? planet : FirstNamedPlanet(message.Text, knownPlanets);
                return string.IsNullOrEmpty(subject)
                    ? new MessageDestination(MessageDestinationKind.None)
                    : new MessageDestination(MessageDestinationKind.Planet, subject);
            }

            switch (message.Type)
            {
                case TechAdvanceType:
                    return new MessageDestination(MessageDestinationKind.Research);
                case NewComponentType:
                    return new MessageDestination(MessageDestinationKind.TechnologyBrowser);
                default:
                    return new MessageDestination(MessageDestinationKind.None);
            }
        }

        /// <summary>The known planet whose name appears earliest in <paramref name="text"/> as a
        /// whole word (the longest name at the same position), or null.</summary>
        public static string FirstNamedPlanet(string text, IEnumerable<string> knownPlanets)
        {
            if (string.IsNullOrEmpty(text) || knownPlanets == null)
            {
                return null;
            }

            string best = null;
            int bestIndex = int.MaxValue;
            foreach (string name in knownPlanets)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                int index = IndexOfWord(text, name);
                if (index < 0)
                {
                    continue;
                }

                if (index < bestIndex || (index == bestIndex && name.Length > best.Length))
                {
                    best = name;
                    bestIndex = index;
                }
            }

            return best;
        }

        private static int IndexOfWord(string text, string word)
        {
            int from = 0;
            while (from <= text.Length - word.Length)
            {
                int index = text.IndexOf(word, from, StringComparison.Ordinal);
                if (index < 0)
                {
                    return -1;
                }

                bool startOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
                int end = index + word.Length;
                bool endOk = end == text.Length || !char.IsLetterOrDigit(text[end]);
                if (startOk && endOk)
                {
                    return index;
                }

                from = index + 1;
            }

            return -1;
        }
    }
}
