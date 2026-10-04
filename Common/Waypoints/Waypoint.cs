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
    /// What a waypoint is aimed at: the target-kind nibble of the original 18-byte waypoint
    /// record (behavior-specs-10/fleet-movement-scanning-cargo.md §5, "Waypoint record": 1
    /// planet, 2 fleet, 4 deep space, 8 special object). Unspecified is this port's legacy
    /// value for waypoints written before the field existed; they behave as fixed points
    /// (planet or deep space, decided by <see cref="Waypoint.Destination"/> as before).
    /// </summary>
    public enum WaypointTargetKind
    {
        Unspecified = 0,
        Planet = 1,
        Fleet = 2,
        DeepSpace = 4,
        SpecialObject = 8
    }

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

        /// <summary>
        /// The kind of object this waypoint is aimed at. A waypoint aimed at a fleet
        /// (<see cref="WaypointTargetKind.Fleet"/>) is a pursuit: the host re-aims it at the
        /// target's new position after every generation's movement (see
        /// <see cref="TargetFleetKey"/> and behavior-specs-10 §5, "Pursuit").
        /// </summary>
        public WaypointTargetKind TargetKind
        {
            get;
            set;
        }

        /// <summary>
        /// The Fleet.Key of the target fleet when <see cref="TargetKind"/> is Fleet, otherwise
        /// <see cref="Global.None"/>.
        /// </summary>
        public long TargetFleetKey
        {
            get;
            set;
        } = Global.None;

        /// <summary>
        /// The jump freeze mark (bit 0x20 of the target-kind byte): set on every other player's
        /// waypoint aimed at a fleet that jumped through a stargate or entered a wormhole this
        /// generation, so the end-of-movement refresh leaves the waypoint at the point the target
        /// left. Cleared by the step-39 revalidation and by any order that replaces the waypoint.
        /// </summary>
        public bool PursuitFrozen
        {
            get;
            set;
        }

        /// <summary>True when this waypoint pursues another fleet.</summary>
        public bool IsFleetTarget
        {
            get { return TargetKind == WaypointTargetKind.Fleet && TargetFleetKey != Global.None; }
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
            TargetKind = other.TargetKind;
            TargetFleetKey = other.TargetFleetKey;
            PursuitFrozen = other.PursuitFrozen;
        }

        /// <summary>
        /// A full copy of this waypoint including an independent copy of its task (used by
        /// Repeat Orders, which re-appends a reached waypoint with its task, and by Patrol, which
        /// copies its order onto other waypoints).
        /// </summary>
        public Waypoint CloneWithTask()
        {
            Waypoint copy = new Waypoint(this);
            copy.Task = CloneTask(Task);
            return copy;
        }

        /// <summary>
        /// An independent copy of a waypoint task: through the copy constructors where they exist,
        /// otherwise through the task's own XML form (every task class loads from what it saves).
        /// </summary>
        public static IWaypointTask CloneTask(IWaypointTask task)
        {
            switch (task)
            {
                case null:
                    return new NoTask();
                case CargoTask cargo:
                    return new CargoTask(cargo);
                case SplitMergeTask splitMerge:
                    return new SplitMergeTask(splitMerge);
                case PatrolTask patrol:
                    return new PatrolTask(patrol);
                case TransferFleetTask transfer:
                    return new TransferFleetTask(transfer);
                case NoTask _:
                    return new NoTask();
            }

            XmlDocument xmldoc = new XmlDocument();
            XmlElement element = task.ToXml(xmldoc);
            Waypoint loader = new Waypoint();
            return loader.LoadTask(element.Name, element);
        }

        /// <summary>
        /// Aims this waypoint at a fleet (target kind 2): the fleet's key and its position at the
        /// moment of the order (behavior-specs-10 §5, "Pursuit").
        /// </summary>
        public void AimAtFleet(Mappable targetFleet)
        {
            TargetKind = WaypointTargetKind.Fleet;
            TargetFleetKey = targetFleet.Key;
            Position = targetFleet.Position;
            Destination = targetFleet.Name;
            PursuitFrozen = false;
        }

        /// <summary>
        /// Turns a fleet-targeted waypoint into a fixed point: the given star when there is one,
        /// otherwise deep space at the waypoint's current position.
        /// </summary>
        public void MakeFixedPoint(Star star)
        {
            TargetFleetKey = Global.None;
            PursuitFrozen = false;

            if (star != null)
            {
                TargetKind = WaypointTargetKind.Planet;
                Position = star.Position;
                Destination = star.Name;
            }
            else
            {
                TargetKind = WaypointTargetKind.DeepSpace;
                Destination = "Space at " + Position;
            }
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
                    switch (mainNode.Name.ToLowerInvariant())
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

                        case "targetkind":
                            TargetKind = (WaypointTargetKind)Enum.Parse(typeof(WaypointTargetKind), mainNode.FirstChild.Value);
                            break;

                        case "targetfleetkey":
                            TargetFleetKey = long.Parse(mainNode.FirstChild.Value, System.Globalization.NumberStyles.HexNumber);
                            break;

                        case "pursuitfrozen":
                            PursuitFrozen = bool.Parse(mainNode.FirstChild.Value);
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
            
            switch (taskName.ToLowerInvariant())
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
                case "patroltask":
                    Task = new PatrolTask(node);
                    break;
                case "transferfleettask":
                    Task = new TransferFleetTask(node);
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

            if (TargetKind != WaypointTargetKind.Unspecified)
            {
                Global.SaveData(xmldoc, xmlelWaypoint, "TargetKind", TargetKind.ToString());
            }

            if (TargetFleetKey != Global.None)
            {
                Global.SaveData(xmldoc, xmlelWaypoint, "TargetFleetKey", TargetFleetKey.ToString("X"));
            }

            if (PursuitFrozen)
            {
                Global.SaveData(xmldoc, xmlelWaypoint, "PursuitFrozen", PursuitFrozen.ToString());
            }

            xmlelWaypoint.AppendChild(Task.ToXml(xmldoc));

            return xmlelWaypoint;
        }        
    }
}
