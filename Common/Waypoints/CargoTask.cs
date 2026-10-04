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

    public enum CargoMode
    {
        Load = 0,
        Unload
    }   
    
    /// <summary>
    /// Performs Star Colonization.
    /// </summary>
    public class CargoTask : IWaypointTask
    {        
        private List<Message> messages = new List<Message>();
        
        /// <inheritdoc />
        public List<Message> Messages
        {
            get { return messages; }
        }
        
        /// <inheritdoc />
        public string Name
        {
            get
            {
                if (Instructions != null)
                {
                    return "Transport";
                }

                if (Mode == CargoMode.Load)
                { 
                    return "Load Cargo"; 
                }
                else 
                { 
                    return "Unload Cargo"; 
                }
            }
        }
        
        /// <summary>
        /// Cargo object representing the amount to Load or Unload.
        /// </summary>
        public Cargo Amount { get; set; }
        
        /// <summary>
        /// Load or Unload cargo. Mixed operations are represented by more than one Task.
        /// </summary>
        public CargoMode Mode { get; set; }

        /// <summary>
        /// The per-slot Transport instructions (ironium, boranium, germanium, colonists, fuel;
        /// see <see cref="TransportHandler"/>), or null for the legacy fixed-amount
        /// <see cref="Mode"/>/<see cref="Amount"/> form. When set, Mode and Amount are ignored.
        /// </summary>
        public CargoInstruction[] Instructions { get; set; }

        /// <summary>
        /// True after a Perform that left the task unfinished (a Wait for N% not reached, or a Set
        /// amount to N the source could not cover): the waypoint keeps the task and the fleet stays
        /// put, trying again next turn. Not saved; recomputed by every Perform.
        /// </summary>
        public bool Pending { get; private set; }


        /// <summary>
        /// Default Constructor.
        /// </summary>
        public CargoTask()
        {
            Amount = new Cargo();
            Mode = CargoMode.Unload;
        }

        /// <summary>
        /// A Transport task with per-slot instructions, indexed by <see cref="CargoSlot"/>
        /// (missing or null entries mean no instruction for that slot).
        /// </summary>
        public CargoTask(params CargoInstruction[] instructions)
            : this()
        {
            Instructions = TransportHandler.Copy(instructions ?? new CargoInstruction[0]);
        }


        /// <summary>
        /// Copy Constructor.
        /// </summary>
        /// <param name="other">CargoTask to copy.</param>
        public CargoTask(CargoTask copy)
        {
            Amount = new Cargo(copy.Amount);
            Mode = copy.Mode;
            Instructions = TransportHandler.Copy(copy.Instructions);
        }
        
        
        /// <summary>
        /// Load: Read an object of this class from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of this object.</param>
        public CargoTask(XmlNode node)
        {
            if (node == null)
            {
                return;
            }
            
            XmlNode mainNode = node.FirstChild;
            while (mainNode != null)
            {
                try
                {
                    switch (mainNode.Name.ToLowerInvariant())
                    {                            
                        case "cargo":
                            Amount = new Cargo(mainNode);
                            break;
                        case "mode":
                            Mode = (CargoMode)Enum.Parse(typeof(CargoMode), mainNode.FirstChild.Value);
                            break;
                        case "instruction":
                            if (Instructions == null)
                            {
                                Instructions = TransportHandler.Copy(new CargoInstruction[0]);
                            }

                            TransportHandler.LoadInstruction(mainNode, Instructions);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error(e.Message);
                }
                mainNode = mainNode.NextSibling;
            }
        }
        
        
        /// <inheritdoc />
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelTask = xmldoc.CreateElement("CargoTask");
            Global.SaveData(xmldoc, xmlelTask, "Mode", Mode.ToString());
            xmlelTask.AppendChild(Amount.ToXml(xmldoc));

            if (Instructions != null)
            {
                for (int i = 0; i < Instructions.Length; i++)
                {
                    xmlelTask.AppendChild(TransportHandler.ToXml(xmldoc, Instructions[i] ?? new CargoInstruction(), (CargoSlot)i));
                }
            }

            return xmlelTask;
        }

        
        /// <inheritdoc />
        public bool IsValid(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver)
        {
            Pending = false;

            if (Instructions != null)
            {
                // A pending task is performed again every turn: report each turn's outcome once.
                Messages.Clear();

                // A Transport task works against the planet the fleet orbits or another fleet at
                // the same spot; loads from deep space are blocked and nothing can be unloaded
                // into it here (§4).
                bool atPlanet = target is Star && fleet.InOrbit != null;
                bool atFleet = target is Fleet other && other.Key != fleet.Key && other.Position == fleet.Position;
                if (!atPlanet && !atFleet)
                {
                    Message message = new Message();
                    message.Audience = fleet.Owner;
                    message.Text = "Fleet " + fleet.Name + " has transport orders but is not at a planet or a fleet.";
                    Messages.Add(message);
                    return false;
                }

                // Loads from another race are judged slot by slot in the handler (theft).
                return true;
            }

            if (fleet.InOrbit == null || target == null || !(target is Star))
            {
                Message message = new Message();            
                message.Audience = fleet.Owner;
                message.Text = "Fleet " + fleet.Name + " attempted to unload cargo while not in orbit.";
                Messages.Add(message);
                return false;
            }
            
            Star star = target as Star;

            // behavior-specs-10/fleet-movement-scanning-cargo.md §4, "Caps on a load": taking
            // cargo from another race's planet works only with the cargo-theft scanner ability
            // ("Theft": a Robber Baron Scanner takes a planet's surface minerals; colonists are
            // never taken), otherwise such a load is blocked (the owner is told and the task
            // ends). An unowned planet is not "another race's" and may be loaded from.
            // Unloads are always valid here: minerals may go onto any planet, and colonists bound
            // for a planet that is not ours are judged in Unload (the colonist outcome table) -
            // only a colonist unload ever reaches the invasion code.
            // With theft the load goes ahead and Load skips the colonist slot, as the Transport
            // handler judges theft slot by slot (TransportHandler.MayTakeFrom).
            bool theft = fleet.CanStealFromPlanets;
            if (Mode == CargoMode.Load && star.Owner != fleet.Owner && star.Owner != Global.Nobody && !theft)
            {
                Message message = new Message();
                message.Audience = fleet.Owner;
                message.Text = "Fleet " + fleet.Name + " could not load cargo from " + star.Name + " because it belongs to another race.";
                Messages.Add(message);
                return false;
            }

            return true;
        }
        
        
        /// <inheritdoc />
        public bool Perform(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver)
        {
            if (Instructions != null)
            {
                bool complete = TransportHandler.Run(Instructions, fleet, target, sender, receiver, Messages);
                Pending = !complete;
                return complete;
            }

            switch (Mode)
            {
                case CargoMode.Load:
                    return Load(fleet, target, sender, receiver);
                    
                case CargoMode.Unload:
                    return Unload(fleet, target, sender, receiver);
            }
            
            return false;
        }

        
        /// <summary>
        /// Performs concrete unloading.
        /// </summary>
        private bool Unload(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver)
        {
            Star star = target as Star;

            // "Load or unload exactly N: N. An unload never exceeds what the fleet carries"
            // (fleet-movement-scanning-cargo.md §4, "Amounts"): each slot is cut to the cargo
            // aboard, and a negative ordered amount moves nothing.
            Cargo unload = new Cargo();
            foreach (CargoSlot slot in CargoSlots)
            {
                SetSlot(unload, slot, Math.Min(Math.Max(0, GetSlot(Amount, slot)), Math.Max(0, GetSlot(fleet.Cargo, slot))));
            }

            if (star.Owner == fleet.Owner || unload.ColonistsInKilotons <= 0)
            {
                // Own planet: an ordinary transfer, colonists added to the population at once.
                // Minerals alone may be unloaded onto any planet (a foreign one receives them as
                // a gift); the handler's only race test is on colonists.
                Message message = new Message();
                message.Text = "Fleet " + fleet.Name + " has unloaded its cargo at " + star.Name + ".";
                Messages.Add(message);

                star.Add(unload);
                fleet.Cargo.Remove(unload);

                return true;
            }

            // Colonists onto a planet that is not ours (behavior-specs-10 §4, "Colonist unload
            // outcomes, complete table", Unload task column). Slots are handled in the order
            // ironium, boranium, germanium, colonists, so the minerals are unloaded first.
            Cargo minerals = new Cargo();
            minerals.Ironium = unload.Ironium;
            minerals.Boranium = unload.Boranium;
            minerals.Germanium = unload.Germanium;
            if (minerals.Mass > 0)
            {
                star.Add(minerals);
                fleet.Cargo.Remove(minerals);

                Message mineralMessage = new Message();
                mineralMessage.Audience = fleet.Owner;
                mineralMessage.Text = "Fleet " + fleet.Name + " has unloaded its minerals at " + star.Name + ".";
                Messages.Add(mineralMessage);
            }

            // Then the colonists: unowned planet refused (85), Alternate Reality refused (86),
            // a starbase refused (309) - all leaving the colonists aboard - otherwise an invasion
            // (relations are never consulted).
            InvadeTask invade = new InvadeTask();
            bool invaded = false;
            if (invade.IsValid(fleet, star, sender, receiver))
            {
                invaded = invade.Invade(fleet, star, sender, receiver, unload.ColonistsInKilotons);
            }

            Messages.AddRange(invade.Messages);

            return invaded;
        }
        
        
        /// <summary>
        /// Performs concrete loading.
        /// </summary>
        private bool Load(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver)
        {
            Star star = target as Star;

            // fleet-movement-scanning-cargo.md §4, "Caps on a load": in slot order (ironium,
            // boranium, germanium, colonists) each amount is cut to the hold's free space - which
            // is recomputed after every slot, so earlier slots get first claim - and then to what
            // the planet holds, so the source never goes below zero. A slot the fleet may not
            // take from this planet (colonists from another race, even with theft) is skipped.
            // "A Load exactly N that finds less loads what there is and counts as done."
            foreach (CargoSlot slot in CargoSlots)
            {
                int want = Math.Max(0, GetSlot(Amount, slot));
                if (want == 0 || !TransportHandler.MayTakeFrom(fleet, star, slot))
                {
                    continue;
                }

                int got = Math.Min(want, Math.Min(TransportHandler.FreeSpace(fleet, slot), TransportHandler.SourceAmount(star, slot)));
                if (got <= 0)
                {
                    continue;
                }

                Cargo moved = new Cargo();
                SetSlot(moved, slot, got);
                fleet.Cargo.Add(moved);
                star.Remove(moved);
            }

            Message message = new Message();
            message.Text = "Fleet " + fleet.Name + " has loaded cargo from " + star.Name + ".";
            Messages.Add(message);

            return true;
        }

        /// <summary>The four cargo slots of the fixed-amount form, in the handler's slot order.</summary>
        private static readonly CargoSlot[] CargoSlots = { CargoSlot.Ironium, CargoSlot.Boranium, CargoSlot.Germanium, CargoSlot.Colonists };

        private static int GetSlot(Cargo cargo, CargoSlot slot)
        {
            switch (slot)
            {
                case CargoSlot.Ironium:
                    return cargo.Ironium;
                case CargoSlot.Boranium:
                    return cargo.Boranium;
                case CargoSlot.Germanium:
                    return cargo.Germanium;
                case CargoSlot.Colonists:
                    return cargo.ColonistsInKilotons;
                default:
                    return 0;
            }
        }

        private static void SetSlot(Cargo cargo, CargoSlot slot, int amount)
        {
            switch (slot)
            {
                case CargoSlot.Ironium:
                    cargo.Ironium = amount;
                    break;
                case CargoSlot.Boranium:
                    cargo.Boranium = amount;
                    break;
                case CargoSlot.Germanium:
                    cargo.Germanium = amount;
                    break;
                case CargoSlot.Colonists:
                    cargo.ColonistsInKilotons = amount;
                    break;
            }
        }
    }
}
