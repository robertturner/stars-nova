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
    using System.Xml;

    using Nova.Common.DataStructures;
    
    /// <summary>
    /// Waypoints have a position (i.e. where to go), a destination description
    /// (e.g. a star name), a speed to go there and a task to do on arrival (e.g. 
    /// colonise).
    /// </summary>
    public class Waypoint
    {
        public NovaPoint Position 
        {
            get; 
            set;
        }

        public int WarpFactor 
        {
            get; 
            set;
        }

        public IWaypointTask Task 
        {
            get; 
            set;
        }

        public string Destination 
        {
            get; 
            set;
        }

        
        /// <summary>
        /// Default constructor.
        /// </summary>
        public Waypoint()
        {
            WarpFactor = 6;
            Task = new NoTask();
        }
        
        /// <summary>
        /// Copies everything about another Waypoint, except the Task.
        /// Used for editing purposes.
        /// </summary>
        /// <param name="other">Waypoint to semi clone.</param>
        public Waypoint(Waypoint other)
        {
            Position = other.Position;
            WarpFactor = other.WarpFactor;
            Destination = other.Destination;
        }
        

        /// <summary>
        /// Load from XML: initializing constructor from an XML node.
        /// </summary>
        /// <param name="node">A node is a "Waypoint" node Nova save file (xml document).
        /// </param>
        public Waypoint(XmlNode node)
        {
            XmlNode mainNode = node.FirstChild;
            while (mainNode != null)
            {
                try
                {
                    switch (mainNode.Name.ToLower())
                    {
                        case "destination":
                            Destination = mainNode.FirstChild.Value;
                            break;

                        case "warpfactor":
                            WarpFactor = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "position":
                            Position = new NovaPoint(mainNode);
                            break;

                        default:
                            LoadTask(mainNode.Name.ToString(), mainNode);
                            break;
                    }
                }
                catch (Exception e)
                {
                    // Non-fatal - confirmed live as a real, repeated crash: Report.FatalError
                    // calls Environment.Exit, so one malformed field in one waypoint (e.g. an
                    // empty <Destination/> with no text child, from an .orders/.cstate write
                    // that was itself interrupted by an earlier crash) killed the whole app on
                    // every subsequent load, forever - a single bad field is never worth losing
                    // the entire game session over. Skip just this field and keep going, matching
                    // this file's own EmpireData/FleetIntel-style tolerance for a save that's
                    // slightly damaged in one place.
                    Report.Error(e.Message + "\n Details: \n" + e.ToString());
                }

                mainNode = mainNode.NextSibling;
            }

            // The parameterless constructor defaults this to NoTask() before anything runs;
            // this XML constructor only ever sets it via LoadTask, so if every task-shaped child
            // node failed to load (or none was present at all - also possible on a damaged save),
            // Task would otherwise stay null and crash the many callers that assume every
            // Waypoint has one (Perform, Name, ToXml, ...).
            Task ??= new NoTask();
        }

        public IWaypointTask LoadTask(string taskName, XmlNode node)
        {
            if (!taskName.Contains("Task"))
            {
                taskName += "Task";
            }
            
            taskName.Replace(" ", "");
            
            switch (taskName.ToLower())
            {
                case "cargotask":
                    Task = new CargoTask(node);
                    break;
                case "colonisetask":
                    Task = new ColoniseTask(node);
                    break;
                case "invadetask":
                    Task = new InvadeTask(node);
                    break;
                case "layminestask":
                    Task = new LayMinesTask(node);
                    break;
                case "scraptask":
                    Task = new ScrapTask(node);
                    break;
                case "splitmergetask":
                    Task = new SplitMergeTask(node);
                    break;
                default:
                    Task = new NoTask();
                    break;
            }
            
            return Task;
        }
        

        /// <summary>
        /// Save: Serialize this Waypoint to an <see cref="XmlElement"/>.
        /// </summary>
        /// <param name="xmldoc">The parent <see cref="XmlDocument"/>.</param>
        /// <returns>An <see cref="XmlElement"/> representation of the Waypoint.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelWaypoint = xmldoc.CreateElement("Waypoint");

            Global.SaveData(xmldoc, xmlelWaypoint, "Destination", Destination);
            
            if (Position != null)
            {
                xmlelWaypoint.AppendChild(Position.ToXml(xmldoc, "Position"));
            }
            
            Global.SaveData(xmldoc, xmlelWaypoint, "WarpFactor", WarpFactor.ToString(System.Globalization.CultureInfo.InvariantCulture));
            xmlelWaypoint.AppendChild(Task.ToXml(xmldoc));

            return xmlelWaypoint;
        }        
    }
}
