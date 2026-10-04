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

namespace Nova.Common.Waypoints
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    using Nova.Common.Combat;
    using Nova.Common.Components;

    /// <summary>
    /// The Transfer Fleet waypoint task (task nibble 9): gives the fleet to another player
    /// (behavior-specs-10/fleet-movement-scanning-cargo.md §5, task table row 9). It does nothing
    /// on arrival; the transfer itself runs in the task pass's final mode, after movement and the
    /// battle (Nova.Server.TurnSteps.TransferFleetStep calls <see cref="Transfer"/>), so a fleet
    /// destroyed in battle gives nothing. The task stays on the fleet's current waypoint, holding
    /// the fleet there, until that pass settles it.
    /// </summary>
    public class TransferFleetTask : IWaypointTask
    {
        private List<Message> messages = new List<Message>();

        public TransferFleetTask()
        {
            RecipientId = Global.Nobody;
        }

        public TransferFleetTask(ushort recipientId)
        {
            RecipientId = recipientId;
        }

        public TransferFleetTask(TransferFleetTask copy)
        {
            RecipientId = copy.RecipientId;
        }

        /// <summary>
        /// Load: Read in a TransferFleetTask from an XmlNode representation.
        /// </summary>
        public TransferFleetTask(XmlNode node)
            : this()
        {
            if (node == null)
            {
                return;
            }

            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    if (subnode.Name.ToLowerInvariant() == "recipient")
                    {
                        RecipientId = ushort.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception e)
                {
                    Report.Error(e.Message);
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>The empire id of the player who is to receive the fleet.</summary>
        public ushort RecipientId { get; set; }

        public List<Message> Messages
        {
            get { return messages; }
        }

        public string Name
        {
            get { return "Transfer Fleet"; }
        }

        public bool IsValid(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver = null)
        {
            return true;
        }

        public bool Perform(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver = null)
        {
            // No arrival action: the transfer runs in the final task pass.
            return true;
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelTask = xmldoc.CreateElement("TransferFleetTask");
            Global.SaveData(xmldoc, xmlelTask, "Recipient", RecipientId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return xmlelTask;
        }

        /// <summary>
        /// The Transfer Fleet handler (behavior-specs-11/fleet-movement-scanning-cargo.md §5,
        /// "Transfer Fleet (9), complete rule"; `stars.exe.export.c:76035-76176`). The checks run
        /// in order: the recipient must be a live race other than the giver (328), must not be a
        /// computer player and must not rate the giver as Enemy (332), the fleet must carry no
        /// colonists (329; minerals and fuel do not block), and every design the recipient lacks
        /// must fit a free slot (330 to the giver, 331 to the recipient), as must the recipient's
        /// 512-fleet cap. On success a new fleet owned by the recipient appears at the same
        /// position with the same ships (damage state carried), minerals and fuel; a design the
        /// recipient lacked is copied across with its failed-legality flag set; the source fleet is
        /// removed; 333 goes to the giver and 334 to the recipient. Every outcome settles the task.
        /// </summary>
        /// <param name="fleet">The fleet being given away.</param>
        /// <param name="sender">The fleet's owner.</param>
        /// <param name="recipient">The receiving empire, or null if the stored id does not resolve.</param>
        /// <param name="recipientIsComputer">True when the recipient slot is a computer player.</param>
        /// <returns>The recipient's new fleet (already added to its OwnedFleets), or null when the
        /// transfer was refused.</returns>
        public Fleet Transfer(Fleet fleet, EmpireData sender, EmpireData recipient, bool recipientIsComputer = false)
        {
            if (recipient == null || recipient.Id == sender.Id || recipient.Eliminated || fleet.IsStarbase)
            {
                // Message 328.
                Tell(sender.Id, "Fleet " + fleet.Name + " could not be transferred: the chosen recipient cannot receive it.");
                return null;
            }

            // Message 332: a computer recipient, or one whose own opinion rates the giver as Enemy.
            if (recipientIsComputer || RatesEnemy(recipient, sender.Id))
            {
                Tell(sender.Id, "Fleet " + fleet.Name + " could not be transferred to " + recipient.Race.PluralName
                    + ": that player cannot receive gifts from you.");
                return null;
            }

            // Message 329: only colonists block; minerals and fuel go with the ships.
            if (fleet.Cargo.ColonistsInKilotons > 0)
            {
                Tell(sender.Id, "Fleet " + fleet.Name + " could not be transferred because it still carries colonists.");
                return null;
            }

            // Messages 330/331: every design the recipient lacks needs a free slot of its kind.
            int newShipDesigns = 0;
            int newStarbaseDesigns = 0;
            HashSet<string> counted = new HashSet<string>();
            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (FindEquivalent(recipient, token.Design) != null)
                {
                    continue;
                }

                string signature = Signature(token.Design);
                if (signature != null && !counted.Add(signature))
                {
                    continue;
                }

                if (token.Design.Type == ItemType.Starbase)
                {
                    newStarbaseDesigns++;
                }
                else
                {
                    newShipDesigns++;
                }
            }

            if (CountDesigns(recipient, ItemType.Ship) + newShipDesigns > Global.MaxDesignsAmount
                || CountDesigns(recipient, ItemType.Starbase) + newStarbaseDesigns > Global.MaxStarbaseDesignsAmount)
            {
                Tell(sender.Id, "Fleet " + fleet.Name + " could not be transferred to " + recipient.Race.PluralName
                    + " because they have no free design slot for one of its ships.");
                Tell(recipient.Id, "The " + sender.Race.PluralName + " tried to give you a fleet, but you have no free design slot for it.");
                return null;
            }

            int recipientFleets = recipient.OwnedFleets.Values.Count(owned => !owned.IsStarbase);
            if (recipientFleets >= Global.MaxFleetAmount)
            {
                // Messages 330 and 331.
                Tell(sender.Id, "Fleet " + fleet.Name + " could not be transferred to " + recipient.Race.PluralName
                    + " because they already have the maximum of " + Global.MaxFleetAmount + " fleets.");
                Tell(recipient.Id, "The " + sender.Race.PluralName + " tried to give you a fleet, but you already have the maximum of "
                    + Global.MaxFleetAmount + " fleets.");
                return null;
            }

            Fleet gift = new Fleet(recipient.GetNextFleetKey());
            gift.Type = ItemType.Fleet;
            gift.Position = fleet.Position;
            gift.InOrbit = fleet.InOrbit;
            gift.Cargo = new Cargo(fleet.Cargo);

            ShipDesign firstDesign = null;
            foreach (ShipToken token in fleet.Composition.Values)
            {
                ShipDesign design = DesignFor(recipient, token.Design);
                firstDesign = firstDesign ?? design;

                ShipToken copy = new ShipToken(design, token.Quantity, token.Armor);
                DamageWord.Store(copy, DamageWord.For(token));
                gift.Composition.Add(copy.Key, copy);
            }

            gift.FuelAvailable = Math.Min(fleet.FuelAvailable, gift.TotalFuelCapacity);
            gift.Waypoints.Add(new Waypoint
            {
                Position = fleet.Position,
                Destination = fleet.InOrbit != null ? fleet.InOrbit.Name : "Space at " + fleet.Position,
                WarpFactor = 0,
            });
            gift.Name = (firstDesign != null ? firstDesign.Name : "Fleet") + " #" + gift.Id;

            recipient.AddOrUpdateFleet(gift);
            sender.RemoveFleet(fleet);

            // Messages 333 and 334.
            Tell(sender.Id, "Fleet " + fleet.Name + " has been transferred to " + recipient.Race.PluralName + ".");
            Tell(recipient.Id, "The " + sender.Race.PluralName + " have given your race the fleet " + gift.Name + ".");

            return gift;
        }

        /// <summary>True when the recipient's own relation toward the giver is Enemy.</summary>
        private static bool RatesEnemy(EmpireData recipient, ushort giverId)
        {
            return recipient.EmpireReports != null
                && recipient.EmpireReports.TryGetValue(giverId, out EmpireIntel intel)
                && intel.Relation == PlayerRelation.Enemy;
        }

        /// <summary>How many of the empire's designs are of the given kind.</summary>
        private static int CountDesigns(EmpireData empire, ItemType kind)
        {
            return empire.Designs.Values.Count(design => (design.Type == ItemType.Starbase) == (kind == ItemType.Starbase));
        }

        /// <summary>
        /// The recipient's design for a gifted ship: an identical design it already has (same hull
        /// and the same parts in the same slots), otherwise a copy of the giver's design under a new
        /// key of the recipient's, with the failed-legality flag set (the +0x7c bit 0x80 the
        /// transfer handler sets on a copied design).
        /// </summary>
        private static ShipDesign DesignFor(EmpireData recipient, ShipDesign design)
        {
            ShipDesign match = FindEquivalent(recipient, design);
            if (match != null)
            {
                return match;
            }

            ShipDesign copy = new ShipDesign(recipient.GetNextDesignKey());
            copy.Name = design.Name;
            copy.Type = design.Type;
            copy.Icon = design.Icon != null ? (ShipIcon)design.Icon.Clone() : null;
            copy.Blueprint = design.Blueprint != null ? new Component(design.Blueprint) : null;
            copy.FailedLegality = true;
            if (copy.Blueprint != null && copy.Blueprint.Properties.ContainsKey("Hull"))
            {
                copy.Update(recipient.Race, recipient.ResearchLevels);
            }

            recipient.Designs[copy.Key] = copy;
            return copy;
        }

        /// <summary>The recipient's identical design, or null when it has none.</summary>
        private static ShipDesign FindEquivalent(EmpireData recipient, ShipDesign design)
        {
            string signature = Signature(design);
            if (signature == null)
            {
                return null;
            }

            return recipient.Designs.Values.FirstOrDefault(own => Signature(own) == signature);
        }

        private static string Signature(ShipDesign design)
        {
            if (design?.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull"))
            {
                return null;
            }

            return design.Blueprint.Name + ":" + string.Join(",", design.Hull.Modules.Select(module =>
                (module.AllocatedComponent == null ? string.Empty : module.AllocatedComponent.Name) + "x" + module.ComponentCount));
        }

        private void Tell(ushort audience, string text)
        {
            Message message = new Message();
            message.Audience = audience;
            message.Text = text;
            Messages.Add(message);
        }
    }
}
