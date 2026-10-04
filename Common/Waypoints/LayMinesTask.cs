using System.Linq;
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
    /// The Lay Mine Field waypoint task (task code 6).
    /// </summary>
    public class LayMinesTask : IWaypointTask
    {
        private List<Message> messages = new List<Message>();
        
        public List<Message> Messages
        {
            get{ return messages;}
        } 
        
        public string Name
        {
            get{return "Lay Mines";}
        }
        
        /// <summary>
        /// The duration value that is never decremented, so the order runs indefinitely
        /// (behavior-specs-10/turn-generation-engine.md section 3, "Cancellation and duration";
        /// fleet-movement-scanning-cargo.md section 5: a new Lay Mine Field order gets duration 5).
        /// </summary>
        public const int Indefinitely = 5;

        /// <summary>
        /// The order's duration counter, read by the server before each year's laying: 0 means
        /// this is the last year (the task is then cleared), <see cref="Indefinitely"/> is never
        /// decremented, any other value is decremented by one.
        /// </summary>
        public int Duration = Indefinitely;

        public LayMinesTask()
        {

        }

        /// <summary>
        /// Load: Read in a LayMinesTask from an XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a LayMinesTask</param>
        public LayMinesTask(XmlNode node)
        {
            if (node == null)
            {
                return;
            }

            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                if (subnode.Name.ToLowerInvariant() == "duration" && subnode.FirstChild != null)
                {
                    Duration = int.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                }

                subnode = subnode.NextSibling;
            }
        }
        
        public bool IsValid(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver)
        {
            Message message = new Message();
            Messages.Add(message);            
            message.Audience = fleet.Owner;
            
            
            if (fleet.NumberOfMines == 0)
            {
                message.Text = fleet.Name + " attempted to lay mines. The order has been canceled because no ship in the fleet has a mine laying pod.";
                return false;
            }
            
            Messages.Clear();
            return true;           
        }
        
        /// <summary>
        /// Nothing happens on arrival: the yearly laying (amounts, duration counter, field merge)
        /// is done after movement by ServerState/LayMines.cs, which can reach the minefield table.
        /// </summary>
        public bool Perform(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver)
        {
            return true;
        }
        
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelTask = xmldoc.CreateElement("LayMinesTask");
            Global.SaveData(xmldoc, xmlelTask, "Duration", Duration.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return xmlelTask;
        }
    }
}
