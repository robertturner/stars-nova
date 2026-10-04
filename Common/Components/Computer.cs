#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010 stars-nova
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

#region Module Description
// ===========================================================================
// This class defines a computer property.
// ===========================================================================
#endregion

namespace Nova.Common.Components
{
    using System;
    using System.Xml;

    [Serializable]
    public class Computer : ComponentProperty
    {
        public int Initiative = 0;   
        public double Accuracy = 0;

        #region Construction

        /// <summary>
        /// Initializes a new instance of the Computer class.
        /// </summary>
        public Computer() 
        { 
        }


        /// <summary>
        /// Initializes a new instance of the Computer class.
        /// Copy constructor.
        /// </summary>
        /// <param name="existing">An existing <see cref="Computer"/>.</param>
        public Computer(Computer existing)
        {
            this.Initiative = existing.Initiative;
            this.Accuracy = existing.Accuracy;
        }

        #endregion

        #region Interface ICloneable

        /// ----------------------------------------------------------------------------
        /// <summary>
        /// Implement the ICloneable interface so properties can be cloned.
        /// </summary>
        /// <returns>A clone of this object.</returns>
        /// ----------------------------------------------------------------------------
        public override object Clone()
        {
            return new Computer(this);
        }

        #endregion

        #region Operators

        /// <summary>
        /// Polymorphic addition of properties.
        /// </summary>
        /// <param name="op2"></param>
        public override void Add(ComponentProperty op2)
        {
            Computer temp = this + (Computer)op2;
            Initiative = temp.Initiative;
            Accuracy = temp.Accuracy;
        }

        /// <summary>
        /// Polymorphic multiplication of properties.
        /// </summary>
        /// <param name="scalar"></param>
        public override void Scale(int scalar)
        {
            Computer temp = this * scalar;
            Initiative = temp.Initiative;
            Accuracy = temp.Accuracy;
        }

        /// ----------------------------------------------------------------------------
        /// <summary>
        /// Provide a way to add properties in the ship design.
        /// </summary>
        /// <param name="op1">LHS operand.</param>
        /// <param name="op2">RHS operand.</param>
        /// <returns>Sum of the properties.</returns>
        /// ----------------------------------------------------------------------------
        public static Computer operator +(Computer op1, Computer op2)
        {
            Computer sum = new Computer(op1);
            sum.Initiative = op1.Initiative + op2.Initiative;
            // Sum of two independant probabilities: (1 - ( (1-Accuracy1 ) * (1-Accuracy2) )
            // Using 100.0 as Accuracy is on a 1 to 100 (%) scale not 0 to 1 (normalised) scale.
            // This is the diminishing-returns stacking behavior-specs-9/combat-resolution.md §7a
            // output (b) gives for the computer accuracy bonus: each computer closes its own
            // rate's share of the remaining gap to 100, no explicit cap.
            // behavior-specs-10 §7a: each step is bonus + (100 - bonus) x rate / 100,
            // truncated - integer arithmetic (ShipDesign.ComputerAccuracy walks the parts unit by
            // unit, which is the exact battle value; this summary is for display).
            int first = (int)Math.Floor(op1.Accuracy + 1e-6);
            int second = (int)Math.Floor(op2.Accuracy + 1e-6);
            sum.Accuracy = first + ((100 - first) * second / 100);

            // Previously returned op1, discarding the sum just built - so computers in two
            // different hull slots never stacked at all (neither accuracy nor initiative); only
            // the first slot's computer counted (behavior-specs-9 coverage, combat row 30).
            return sum;
        }


        /// ----------------------------------------------------------------------------
        /// <summary>
        /// Operator* to scale (multiply) properties in the ship design.
        /// </summary>
        /// <param name="op1">Property to scale.</param>
        /// <param name="scalar">Number of instances of this property.</param>
        /// <returns>A single property that represents all these instances.</returns>
        /// ----------------------------------------------------------------------------
        public static Computer operator *(Computer op1, int scalar)
        {
            Computer sum = new Computer(op1);
            sum.Initiative = op1.Initiative * scalar;
            // Diminishing returns per unit, truncating at every step (behavior-specs-10/
            // combat-resolution.md §7a): one Battle Computer 20, two 36, three 48 (not 48.8).
            int rate = (int)Math.Floor(op1.Accuracy + 1e-6);
            int bonus = 0;
            for (int i = 0; i < scalar; i++)
            {
                bonus += (100 - bonus) * rate / 100;
            }

            sum.Accuracy = bonus;
            return sum;
        }

        #endregion

        #region Load Save Xml

        /// ----------------------------------------------------------------------------
        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        /// <param name="node">An <see cref="XmlNode"/> within 
        /// a Nova component definition file (xml document).
        /// </param>
        /// ----------------------------------------------------------------------------
        public Computer(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    if (subnode.Name.ToLowerInvariant() == "initiative")
                    {
                        Initiative = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    if (subnode.Name.ToLowerInvariant() == "accuracy")
                    {
                        Accuracy = double.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    // ignore incomplete or unset values
                }
                subnode = subnode.NextSibling;
            }
        }


        /// ----------------------------------------------------------------------------
        /// <summary>
        /// Save: Serialize this property to an <see cref="XmlElement"/>.
        /// </summary>
        /// <param name="xmldoc">The parent <see cref="XmlDocument"/>.</param>
        /// <returns>An <see cref="XmlElement"/> representation of the Property.</returns>
        /// ----------------------------------------------------------------------------
        public override XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelProperty = xmldoc.CreateElement("Property");

            // Initiative
            XmlElement xmlelInitiative = xmldoc.CreateElement("Initiative");
            XmlText xmltxtInitiative = xmldoc.CreateTextNode(this.Initiative.ToString(System.Globalization.CultureInfo.InvariantCulture));
            xmlelInitiative.AppendChild(xmltxtInitiative);
            xmlelProperty.AppendChild(xmlelInitiative);
            // Accuracy
            XmlElement xmlelAccuracy = xmldoc.CreateElement("Accuracy");
            XmlText xmltxtAccuracy = xmldoc.CreateTextNode(this.Accuracy.ToString(System.Globalization.CultureInfo.InvariantCulture));
            xmlelAccuracy.AppendChild(xmltxtAccuracy);
            xmlelProperty.AppendChild(xmlelAccuracy);

            return xmlelProperty;
        }

        #endregion
    }
}

