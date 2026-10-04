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

    /// <summary>Where activating a message takes the player (the Goto target kinds).</summary>
    public enum MessageDestinationKind
    {
        /// <summary>Nothing beyond selecting the message.</summary>
        None,

        /// <summary>The battle's replay (Message.Event is the BattleReport).</summary>
        BattleReplay,

        /// <summary>The named planet's production queue (the second press of a queue notice).</summary>
        ProductionQueue,

        /// <summary>Select the named planet.</summary>
        Planet,

        /// <summary>The Research panel.</summary>
        Research,

        /// <summary>The Technology Browser.</summary>
        TechnologyBrowser,

        /// <summary>The ship and starbase designer.</summary>
        Designer,

        /// <summary>The Score window.</summary>
        Scores,

        /// <summary>The serial-number entry box.</summary>
        SerialNumber,

        /// <summary>A special map object found by its id.</summary>
        MapObject,

        /// <summary>The Battles report.</summary>
        BattlesReport,

        /// <summary>A component reference, opened in the Technology Browser.</summary>
        Component,

        /// <summary>The Player Relations dialog.</summary>
        Relations,

        /// <summary>A map position (the low 14 bits hold a battle number).</summary>
        Location,

        /// <summary>Select a fleet.</summary>
        Fleet,
    }

    /// <summary>A message's destination and, for planet destinations, the planet's name.</summary>
    public sealed class MessageDestination
    {
        public MessageDestination(MessageDestinationKind kind, string planetName = null)
            : this(kind, planetName, null)
        {
        }

        public MessageDestination(MessageDestinationKind kind, string planetName, MessageDestinationKind? secondPressKind)
        {
            Kind = kind;
            PlanetName = planetName;
            SecondPressKind = secondPressKind;
        }

        public MessageDestinationKind Kind { get; }

        public string PlanetName { get; }

        /// <summary>
        /// The destination a second press reaches, or null when the first press is the whole
        /// action. When this is set the button's caption changes from Goto (dynamic string 1357)
        /// to View (string 741) after the first press.
        /// </summary>
        public MessageDestinationKind? SecondPressKind { get; }

        public bool NeedsSecondPress => SecondPressKind.HasValue;
    }

    /// <summary>
    /// Message activation (behavior-specs-11/client-ui-dialog-catalog.md, "Messages", "Goto
    /// targets"). The Goto button never looks at the message type to choose a view, except for the
    /// production-queue case. Whenever the current message changes the subject word is decoded into
    /// a target kind and Goto is enabled when the kind is not "none" (and the type is not filtered
    /// while filtered messages are hidden; that part is the caller's filter). Pressing Goto runs
    /// the kind's action. <b>Second press:</b> after a planet Goto for types 62, 63 (the two
    /// queue-empty notices) and after every location Goto, the caption changes from Goto to View
    /// and a flag is raised; pressing it again opens the planet's production dialog (planet case)
    /// or the Battle VCR (location case). Moving to another message clears the flag.
    /// Nova's messages carry a string type, not the numeric subject word, so the route is keyed on
    /// the string type where the subject would be and on <see cref="Message.Event"/> for the
    /// object. SEAMS for spec-silent details (reported): the numeric subject-word kinds with no
    /// Nova equivalent (serial number, scores, designer, map object, battles report, relations,
    /// component category/subtype, location/battle number) are exposed on
    /// <see cref="MessageDestinationKind"/> but produced only where a Nova message supplies the
    /// data; the location second press (Battle VCR on the battle number) is not reachable because
    /// Nova stores no battle number on a location message.
    /// </summary>
    public static class MessageRouting
    {
        public const string TechAdvanceType = "TechAdvance";

        public const string NewComponentType = "NewComponent";

        /// <summary>The Message.Type of every mineral-packet notice (the server's
        /// PacketLaunch.MessageType: launch, merge, disintegration, capture, bombardment).</summary>
        public const string MineralPacketType = "Mineral Packet";

        /// <summary>The Goto caption before the first press (dynamic string 1357).</summary>
        public const string GotoCaption = "Goto";

        /// <summary>The caption after the first press of a two-press destination (string 741).</summary>
        public const string ViewCaption = "View";

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
                // The first press selects the planet; for the two queue-empty notices the caption
                // then becomes View and a second press opens the production dialog.
                return ProductionNoticeTypes.OpensProductionQueue(message.Type)
                    ? new MessageDestination(MessageDestinationKind.Planet, planet, MessageDestinationKind.ProductionQueue)
                    : new MessageDestination(MessageDestinationKind.Planet, planet);
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

    /// <summary>
    /// The Goto button's two-press state for the current message (behavior-specs-11/
    /// client-ui-dialog-catalog.md "Goto targets", "Second press"). Select a message when the
    /// current one changes (which clears the View flag); <see cref="Press"/> returns the
    /// destination to act on and arms the caption for a second press where the spec calls for one.
    /// </summary>
    public sealed class MessageGoto
    {
        private readonly IEnumerable<string> knownPlanets;

        private Message selected;
        private MessageDestination destination;
        private bool viewArmed;

        public MessageGoto(IEnumerable<string> knownPlanets = null)
        {
            this.knownPlanets = knownPlanets;
        }

        /// <summary>The current message's first-press destination.</summary>
        public MessageDestination Destination => destination ?? MessageRouting.Destination(selected, knownPlanets);

        /// <summary>True when the kind is not "none" (the caller also applies the message filter).</summary>
        public bool CanGoto => Destination.Kind != MessageDestinationKind.None;

        /// <summary>The button caption: Goto (1357), or View (741) once a second press is armed.</summary>
        public string Caption => viewArmed ? MessageRouting.ViewCaption : MessageRouting.GotoCaption;

        /// <summary>True after the first press of a two-press destination.</summary>
        public bool ViewArmed => viewArmed;

        /// <summary>Called when the current message changes; clears the second-press flag.</summary>
        public void Select(Message message)
        {
            if (ReferenceEquals(selected, message))
            {
                return;
            }

            selected = message;
            destination = MessageRouting.Destination(message, knownPlanets);
            viewArmed = false;
        }

        /// <summary>
        /// The destination to act on. The first press of a two-press destination returns the
        /// selection kind and arms View; the second press returns the deeper kind.
        /// </summary>
        public MessageDestination Press()
        {
            MessageDestination current = Destination;
            if (current.SecondPressKind.HasValue)
            {
                if (viewArmed)
                {
                    viewArmed = false;
                    return new MessageDestination(current.SecondPressKind.Value, current.PlanetName);
                }

                viewArmed = true;
                return new MessageDestination(current.Kind, current.PlanetName);
            }

            viewArmed = false;
            return current;
        }
    }
}
