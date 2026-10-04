#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009-2012 The Stars-Nova Project
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

namespace Nova.Common.Waypoints
{
    using System;
    using System.Collections.Generic;
    using System.Xml;

    using Nova.Common;

    /// <summary>
    /// Performs Star Colonisation.
    /// </summary>
    public class ColoniseTask : IWaypointTask
    {
        private List<Message> messages = new List<Message>();

        public List<Message> Messages
        {
            get
            {
                return messages;
            }
        }

        public string Name
        {
            get
            {
                return "Colonise";
            }
        }

        public ColoniseTask()
        {
        }

        /// <summary>
        /// Load: Read in a ColoniseTask from and XmlNode representation.
        /// </summary>
        /// <param name="node">An <see cref="XmlNode"/> containing a representation of a <see cref="ProductionUnit"/>.</param>
        public ColoniseTask(XmlNode node)
        {
            if (node == null)
            {
                return;
            }
        }

        /// <summary>
        /// The Colonize cancellations (behavior-specs-9/fleet-movement-scanning-cargo.md §5,
        /// "Colonize resolution, fleet side", step 1), each ending the order with no other
        /// effect: the fleet is not at a planet (message 81); the planet has ANY owner, the
        /// fleet's own race included (message 82); the fleet carries no colonists (message 83 -
        /// one unit, 100 colonists, is enough); no ship carries a Colonization Module or an
        /// Orbital Construction Module (message 84; both carry the "Colonizer" property).
        /// </summary>
        public bool IsValid(Fleet fleet, Mappable target, EmpireData sender, EmpireData reciever)
        {
            Message message = new Message();
            Messages.Add(message);

            message.Audience = fleet.Owner;
            message.Text = fleet.Name + " attempted to colonise ";

            if (fleet.InOrbit == null || target == null || !(target is Star))
            {
                message.Text += "something that is not a star.";
                return false;
            }

            Star star = (Star)target;
            message.Text += target.Name;

            if (star.Owner != Global.Nobody)
            {
                message.Text += " but it is already inhabited.";
                return false;
            }

            if (fleet.Cargo.ColonistsInKilotons == 0)
            {
                message.Text += " but no colonists were on board.";
                return false;
            }

            if (fleet.CanColonize == false)
            {
                message.Text += " but no ships with colonization module were present.";
                return false;
            }

            Messages.Clear();
            return true;
        }

        /// <summary>
        /// Dismantles the whole colonizing fleet into the planet and registers its colonists as
        /// a pending landing (behavior-specs-9/fleet-movement-scanning-cargo.md §5, "Colonize
        /// resolution, fleet side", steps 2-4). This happens for EVERY colonizing fleet at the
        /// task pass, contest losers included:
        ///
        /// - Minerals, credited at once and ADDED to the planet's surface stockpile: per mineral
        ///   floor(3S/4) of the cost of every ship in the fleet (escorts and freighters
        ///   included), plus the fleet's whole cargo of it. The fleet's fuel and the ships'
        ///   resource cost are lost; Ultimate Recycling plays no part (its test is on the Scrap
        ///   path only). A loser's minerals stay on the planet for the winner.
        /// - Colonists go into the pending-landing ledger (Star.PendingColonizations), not onto
        ///   the planet: ServerState/ColonizationResolver.cs settles the landing once every
        ///   contender is known (Common can't reference ServerState). Every simultaneous attempt
        ///   at the same star sees the same "still unowned" state via <see cref="IsValid"/>,
        ///   since nothing here changes the planet's owner.
        /// - The fleet is removed (its composition is cleared; the empty fleet is cleaned up).
        /// </summary>
        public bool Perform(Fleet fleet, Mappable target, EmpireData sender, EmpireData reciever)
        {
            Star star = target as Star;

            star.PendingColonizations.Add(new ColonizationAttempt(fleet, sender));

            Resources deposited = ScrapTask.SalvageMinerals(fleet, 3, 4);
            star.ResourcesOnHand.Ironium += deposited.Ironium;
            star.ResourcesOnHand.Boranium += deposited.Boranium;
            star.ResourcesOnHand.Germanium += deposited.Germanium;

            // Message 89, the same notice a scrapped fleet gives, quoting the total kilotons
            // deposited (cargo included).
            Messages.Add(new Message
            {
                Audience = fleet.Owner,
                Text = fleet.Name + " has been dismantled at " + star.Name + " to found a colony, depositing "
                    + deposited.Mass.ToString(System.Globalization.CultureInfo.InvariantCulture) + "kT of minerals."
            });

            fleet.Cargo.Clear();
            fleet.FuelAvailable = 0;
            fleet.Composition.Clear();

            return true;
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelTask = xmldoc.CreateElement("ColoniseTask");

            return xmlelTask;
        }
    }
}
