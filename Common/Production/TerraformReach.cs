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

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Xml;

    using Nova.Common.Components;

    /// <summary>
    /// How far, per environment axis, the owner's terraforming technology can move a planet from
    /// its ORIGINAL value (behavior-specs-10/production-queue.md 10k item 3, FUN_1048_53e2): the
    /// best all-axis ("Total") terraform component the owner can build sets a common reach, and a
    /// better single-axis component (Gravity / Temp / Radiation) extends its own axis. No
    /// terraforming component at all means no reach (headroom 0).
    /// </summary>
    [Serializable]
    public sealed class TerraformReach
    {
        /// <summary>Axis indexes in the original's order (ties go to the earlier axis).</summary>
        public const int GravityAxis = 0;
        public const int TemperatureAxis = 1;
        public const int RadiationAxis = 2;
        public const int AxisCount = 3;

        private readonly int[] reach = new int[AxisCount];

        public TerraformReach(int gravity, int temperature, int radiation)
        {
            reach[GravityAxis] = Math.Max(0, gravity);
            reach[TemperatureAxis] = Math.Max(0, temperature);
            reach[RadiationAxis] = Math.Max(0, radiation);
        }

        public int Gravity => reach[GravityAxis];

        public int Temperature => reach[TemperatureAxis];

        public int Radiation => reach[RadiationAxis];

        /// <summary>The reach on one axis (0 gravity, 1 temperature, 2 radiation).</summary>
        public int this[int axis] => reach[axis];

        /// <summary>
        /// The reach an empire's buildable terraform components give (production-queue.md 10k
        /// item 3). Components are recognised by name ("Total ±N", "Gravity ±N", "Temp ±N",
        /// "Radiation ±N", as in components.xml and the component-stats table).
        /// </summary>
        public static TerraformReach FromComponents(IEnumerable<Component> components)
        {
            int total = 0;
            int[] single = new int[AxisCount];

            if (components != null)
            {
                foreach (Component component in components)
                {
                    if (component == null || component.Type != ItemType.Terraforming)
                    {
                        continue;
                    }

                    if (!TryParse(component.Name, out int axis, out int amount))
                    {
                        continue;
                    }

                    if (axis < 0)
                    {
                        total = Math.Max(total, amount);
                    }
                    else
                    {
                        single[axis] = Math.Max(single[axis], amount);
                    }
                }
            }

            return new TerraformReach(
                Math.Max(total, single[GravityAxis]),
                Math.Max(total, single[TemperatureAxis]),
                Math.Max(total, single[RadiationAxis]));
        }

        /// <summary>The reach of <paramref name="empire"/>'s available components.</summary>
        public static TerraformReach For(EmpireData empire)
        {
            if (empire == null || empire.AvailableComponents == null)
            {
                return new TerraformReach(0, 0, 0);
            }

            return FromComponents(empire.AvailableComponents.Values);
        }

        /// <summary>
        /// The fallback when the owner's technology is not known (a unit queued on the client and
        /// never yet stamped by the server's production step, or a test without an empire): the
        /// project's earlier flat allowance, 15 per axis, or 30 with Total Terraforming.
        /// </summary>
        public static TerraformReach Legacy(Race race)
        {
            int flat = race != null && race.HasTrait("TT") ? 30 : 15;
            return new TerraformReach(flat, flat, flat);
        }

        /// <summary>
        /// Reads a terraform component's name: the axis (-1 for an all-axis "Total" part) and the
        /// number after the "±".
        /// </summary>
        public static bool TryParse(string name, out int axis, out int amount)
        {
            axis = -1;
            amount = 0;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            int sign = name.IndexOf('±');
            if (sign < 0)
            {
                return false;
            }

            string digits = name.Substring(sign + 1).Trim();
            int end = 0;
            while (end < digits.Length && char.IsDigit(digits[end]))
            {
                end++;
            }

            if (end == 0 || !int.TryParse(digits.Substring(0, end), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount))
            {
                return false;
            }

            string prefix = name.Substring(0, sign).Trim().ToLowerInvariant();
            if (prefix.StartsWith("total", StringComparison.Ordinal))
            {
                axis = -1;
            }
            else if (prefix.StartsWith("grav", StringComparison.Ordinal))
            {
                axis = GravityAxis;
            }
            else if (prefix.StartsWith("temp", StringComparison.Ordinal))
            {
                axis = TemperatureAxis;
            }
            else if (prefix.StartsWith("rad", StringComparison.Ordinal))
            {
                axis = RadiationAxis;
            }
            else
            {
                return false;
            }

            return true;
        }

        /// <summary>Load from XML.</summary>
        public TerraformReach(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                if (subnode.FirstChild != null
                    && int.TryParse(subnode.FirstChild.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "gravity": reach[GravityAxis] = Math.Max(0, value); break;
                        case "temperature": reach[TemperatureAxis] = Math.Max(0, value); break;
                        case "radiation": reach[RadiationAxis] = Math.Max(0, value); break;
                    }
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>Save to XML.</summary>
        public XmlElement ToXml(XmlDocument xmldoc, string nodeName)
        {
            XmlElement element = xmldoc.CreateElement(nodeName);
            Global.SaveData(xmldoc, element, "Gravity", Gravity.ToString(CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, element, "Temperature", Temperature.ToString(CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, element, "Radiation", Radiation.ToString(CultureInfo.InvariantCulture));
            return element;
        }
    }
}
