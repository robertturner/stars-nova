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
    using Nova.Common.DataStructures;
    
    /// <summary>
    /// Performs Star Colonisation.
    /// </summary>
    public class InvadeTask : IWaypointTask
    {
        private List<Message> messages = new List<Message>();
        
        public List<Message> Messages
        {
            get{ return messages;}
        }
        
        public string Name
        {
            get{return "Invade";}
        }
        
        public InvadeTask()
        {
             
        }
        
        /// <summary>
        /// Load: Read in a ColoniseTask from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionUnit</param>
        public InvadeTask(XmlNode node)
        {
            if (node == null)
            {
                return;
            }    
        }
        
        public bool IsValid(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver)
        {
            Message message = new Message();
            Messages.Add(message);
            
            message.Audience = fleet.Owner;
            message.Text = "Fleet " + fleet.Name + " has waypoint orders to invade ";
            
            // First check that we are actuallly in orbit around a planet.

            if (fleet.InOrbit == null || target == null || !(target is Star))
            {
                message.Text += "but the target is not a planet.";
                return false;
            }

            // and that we have troops.

            if (fleet.Cargo.ColonistsInKilotons == 0)
            {
                message.Text += "but there are no troops on board.";
                return false;
            }
    
            Star star = (Star)target;
            
            // Consider the diplomatic situation
            if (fleet.Owner == star.Owner)
            {
                // already own this planet, so colonists can beam down safely
                star.Colonists += fleet.Cargo.ColonistNumbers;
                fleet.Cargo.ColonistsInKilotons = 0;
                
                message.Text += star.Name + " but it is already ours. Troops have joined the local populace.";
                return false;
            }
            
            // behavior-specs-10/fleet-movement-scanning-cargo.md §4, "Colonist unload outcomes,
            // complete table" (Unload task column, FUN_10b0_3f3a :75200-75218). The tests run in
            // the handler's order: unowned planet (85), then the Alternate Reality trait (86), then
            // the starbase (309). Every refusal leaves the colonists ABOARD. Diplomatic relations
            // are never consulted: unloading onto a friend's planet is an invasion like any other.
            if (star.Owner == Global.Nobody)
            {
                // Message 85: the planet is uninhabited - nobody colonises by unloading.
                message.Text += star.Name + " but it is not colonised. You must send a ship with a colony module and orders to colonise to take this system.";
                return false;
            }

            // Message 86: an Alternate Reality race may not unload colonists onto any planet that
            // is not its own. Refused at the handler, so the colonists stay aboard (only the
            // Transfer-dialog route, which this port does not model, destroys them - message 87).
            // Tested before the starbase.
            if (sender.Race != null && sender.Race.HasTrait("AR"))
            {
                message.Text += star.Name + " but our colonists cannot live on a planet. The colonists remain aboard.";
                return false;
            }

            // Message 309: the planet has a starbase; the colonists stay aboard.
            if (star.Starbase != null)
            {
                message.Text += star.Name + " but the starbase at " + star.Name + " would kill all invading troops. Order has been cancelled.";
                return false;
            }

            // Valid: the invasion itself reports the outcome, so drop the unfinished
            // "has waypoint orders to invade" stub.
            Messages.Remove(message);
            return true;
        }
        
        public bool Perform(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver)
        {
            return Invade(fleet, (Star)target, sender, receiver, fleet.Cargo.ColonistsInKilotons);
        }

        /// <summary>
        /// Lands <paramref name="troopKilotons"/> of the fleet's colonists (clamped to what it
        /// carries) on another race's planet: the invasion of the colonist unload outcome table
        /// (strength 110%, 165% War Monger, against the population, doubled for an Inner Strength
        /// defender). Used by this task and by an Unload cargo task at another race's planet.
        /// </summary>
        public bool Invade(Fleet fleet, Star star, EmpireData sender, EmpireData receiver, int troopKilotons)
        {
            troopKilotons = Math.Max(0, Math.Min(troopKilotons, fleet.Cargo.ColonistsInKilotons));

            // The troops are now committed to take the star or die trying
            int troops = troopKilotons * Global.ColonistsPerKiloton;
            fleet.Cargo.ColonistsInKilotons -= troopKilotons;

            // Set up the message recipients before the star (potentially) changes hands.
            Message wolfMessage = new Message();
            wolfMessage.Audience = fleet.Owner;
            Message lambMessage = new Message();
            lambMessage.Audience = star.Owner;

            // Take into account the Defenses (computed locally, not via Defenses' shared statics,
            // which a UI thread may be overwriting for another planet at the same moment).
            int troopsOnGround = (int)(troops * (1.0 - Defenses.InvasionCoverageOf(star)));

            // Apply defender and attacker bonuses
            double attackerBonus = 1.1;
            if (sender.Race.HasTrait("WM"))
            {
                attackerBonus *= 1.5;
            }

            double defenderBonus = 1.0;
            if (receiver.Race.HasTrait("IS"))
            {
                defenderBonus *= 2.0;
            }

            int defenderStrength = (int)(star.Colonists * defenderBonus);
            int attackerStrength = (int)(troopsOnGround * attackerBonus);
            int survivorStrength = defenderStrength - attackerStrength; // will be negative if attacker wins

            string messageText = fleet.Owner + "'s fleet " + fleet.Name + " attacked " +
                                 star.Name + " with " + troops + " troops. ";

            if (survivorStrength > 0)
            {
                // defenders win
                int remainingDefenders = (int)(survivorStrength / defenderBonus);
                remainingDefenders = Math.Max(remainingDefenders, Global.ColonistsPerKiloton);
                int defendersKilled = star.Colonists - remainingDefenders;
                star.Colonists = remainingDefenders;

                messageText += "The attackers were slain but "
                            + defendersKilled +
                            " colonists were killed in the attack.";

                wolfMessage.Text = messageText;
                Messages.Add(wolfMessage);

                lambMessage.Text = messageText;
                Messages.Add(lambMessage);
            }
            else if (survivorStrength < 0)
            {
                // attacker wins
                star.ManufacturingQueue.Clear();
                int remainingAttackers = (int)(-survivorStrength / attackerBonus);
                remainingAttackers = Math.Max(remainingAttackers, Global.ColonistsPerKiloton);
                int attackersKilled = troops - remainingAttackers;
                star.Colonists = remainingAttackers;
                
                receiver.OwnedStars.Remove(star);
                star.Owner = fleet.Owner;
                // The surviving population is the invader's (as ColonizationResolver does for a
                // colonisation); without this the captured planet kept growing and producing as
                // the defender's race until the next reload relinked ThisRace (found by Nova.Sim).
                star.ThisRace = sender.Race;
                star.EnergyTechLevel = sender.ResearchLevels[TechLevel.ResearchField.Energy];
                sender.OwnedStars.Add(star);

                // The captor's default production template becomes the planet's queue
                // (production-queue.md 10f: "a colonisation or invasion succeeds").
                ProductionTemplateSet.ApplyDefault(star, sender);

                // See ColoniseTask.Perform's own comment - same defensive
                // ContainsKey-or-Add pattern for the equivalent "took a star this empire never
                // had a report for" case, rather than assuming StarReports already has an entry.
                if (sender.StarReports.ContainsKey(star.Key))
                {
                    sender.StarReports[star.Key].Update(star, ScanLevel.Owned, sender.TurnYear);
                }
                else
                {
                    sender.StarReports.Add(star.Key, star.GenerateReport(ScanLevel.Owned, sender.TurnYear));
                }
                
                messageText += "The defenders were slain but "
                            + attackersKilled +
                            " troops were killed in the attack.";

                // Capturing a planet is the shared salvage dispatcher's third source
                // (turn-generation-engine.md §5): the former owner's six tech levels stand in for
                // the design tables.
                SalvageTables tables = new SalvageTables();
                tables.AddTechLevel(receiver.ResearchLevels);
                SalvageResult result = SalvageDispatcher.TryGain(sender, tables, GameRandom.Current);
                if (result != null)
                {
                    messageText += result.PartName != null
                        ? " Capturing the planet has also revealed the plans for the " + result.PartName + "."
                        : " Capturing the planet has also added " + result.BankedResources
                            + " research points to your " + result.Field + " research.";
                }

                wolfMessage.Text = messageText;
                Messages.Add(wolfMessage);

                lambMessage.Text = messageText;
                Messages.Add(lambMessage);
            }
            else
            {
                // no survivors!
                messageText += "Both sides fought to the last and none were left to claim the planet!";

                wolfMessage.Text = messageText;
                Messages.Add(wolfMessage);

                lambMessage.Text = messageText;
                Messages.Add(lambMessage);

                // clear out the colony
                star.ManufacturingQueue.Clear();
                star.Colonists = 0;
                star.Mines = 0;
                star.Factories = 0;
                star.Owner = Global.Nobody;
            }
            
            return true; 
        }
        
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelTask = xmldoc.CreateElement("InvadeTask");
            
            return xmlelTask;
        }
    }
}
