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
    using System.Xml;

    using Nova.Common;

    /// <summary>
    /// The Patrol waypoint task (task nibble 7). It does nothing on arrival: it is a
    /// radius-gated automatic redirect run by the host when each player's turn file is written
    /// (behavior-specs-10/fleet-movement-scanning-cargo.md §5, Patrol "Complete rule"; the scan
    /// itself is Nova.Server.TurnSteps.PatrolStep). Its two settings are the speed (0 = the fleet's
    /// efficient warp) and the range index (range = (index + 1) x 50 ly; 10 means 10,000 ly).
    /// </summary>
    public class PatrolTask : IWaypointTask
    {
        /// <summary>The range index that means "any distance" (10,000 ly).</summary>
        public const int UnlimitedRangeIndex = 10;

        /// <summary>The range, in light years, of <see cref="UnlimitedRangeIndex"/>.</summary>
        public const int UnlimitedRange = 10000;

        private List<Message> messages = new List<Message>();

        public PatrolTask()
        {
        }

        public PatrolTask(PatrolTask copy)
        {
            Speed = copy.Speed;
            RangeIndex = copy.RangeIndex;
        }

        /// <summary>
        /// Load: Read in a PatrolTask from an XmlNode representation.
        /// </summary>
        public PatrolTask(XmlNode node)
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
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "speed":
                            Speed = int.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "rangeindex":
                            RangeIndex = int.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error(e.Message);
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>The intercept speed: 0 means the fleet's efficient warp.</summary>
        public int Speed { get; set; }

        /// <summary>The stored range value, 0-10 (see <see cref="RangeInLightYears"/>).</summary>
        public int RangeIndex { get; set; }

        /// <summary>
        /// (stored value + 1) x 50 ly, so 0-9 give 50-500 ly and 10 gives 10,000 ly.
        /// </summary>
        public int RangeInLightYears
        {
            get
            {
                if (RangeIndex >= UnlimitedRangeIndex)
                {
                    return UnlimitedRange;
                }

                return (Math.Max(0, RangeIndex) + 1) * 50;
            }
        }

        public List<Message> Messages
        {
            get { return messages; }
        }

        public string Name
        {
            get { return "Patrol"; }
        }

        public bool IsValid(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver = null)
        {
            return true;
        }

        public bool Perform(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver = null)
        {
            // No arrival action: the patrol scan runs in the step-39 pass.
            return true;
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelTask = xmldoc.CreateElement("PatrolTask");
            Global.SaveData(xmldoc, xmlelTask, "Speed", Speed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelTask, "RangeIndex", RangeIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return xmlelTask;
        }
    }
}
