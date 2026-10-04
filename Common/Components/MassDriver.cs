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
// This class defines a property to represent a Mass Driver.
// A mass driver has a maximum warp value (int), but mass drivers do not
// add linearly. The design's launch rating is the best driver's warp, plus one
// if that best rating appears in two different slots (behavior-specs-10/
// production-queue.md section 10b, FUN_1048_5138). Several drivers stacked in
// ONE slot do not add anything.
// ===========================================================================
#endregion

namespace Nova.Common.Components
{
    using System;
    using System.Xml;

    [Serializable]
    public class MassDriver : ComponentProperty
    {
        /// <summary>
        /// The summarised launch rating: for a single component (or one slot) the driver's warp;
        /// for a design summary the best warp, plus one when that best warp is in two or more
        /// different slots.
        /// </summary>
        public int Value = 0;

        // The best single-driver warp seen and how many different slots carry it. These let a
        // design summary combine slots in any order without double counting the +1 bonus
        // (e.g. slots 7, 7, 8 -> 8, not 9). Not serialised: a loaded property is one driver/slot.
        private int bestWarp = 0;
        private int slotsAtBest = 0;

        #region Construction

        /// <summary>
        /// Default constructor.
        /// </summary>
        public MassDriver() 
        { 
        }

        /// <summary>
        /// Copy constructor.
        /// </summary>
        /// <param name="existing"></param>
        public MassDriver(MassDriver existing)
        {
            this.Value = existing.Value;
            this.bestWarp = existing.BestWarp;
            this.slotsAtBest = existing.SlotsAtBest;
        }

        /// <summary>
        /// Initializing constructor.
        /// </summary>
        /// <param name="existing">The initial mass driver value.</param>
        public MassDriver(int existing)
        {
            this.Value = existing;
        }

        /// <summary>The best single-driver warp combined into this property.</summary>
        public int BestWarp
        {
            get { return slotsAtBest > 0 ? bestWarp : Value; }
        }

        /// <summary>How many different slots carry <see cref="BestWarp"/> (1 for a single driver/slot).</summary>
        public int SlotsAtBest
        {
            get { return slotsAtBest > 0 ? slotsAtBest : (Value > 0 ? 1 : 0); }
        }

        private static MassDriver FromSlots(int best, int slots)
        {
            MassDriver result = new MassDriver(slots >= 2 ? best + 1 : best);
            result.bestWarp = best;
            result.slotsAtBest = slots;
            return result;
        }

        #endregion

        #region Interface ICloneable

        /// <summary>
        /// Implement the ICloneable interface so properties can be cloned.
        /// </summary>
        /// <returns></returns>
        public override object Clone()
        {
            return new MassDriver(this);
        }

        #endregion

        #region Operators

        /// <summary>
        /// Polymorphic addition of properties.
        /// </summary>
        /// <param name="op2"></param>
        public override void Add(ComponentProperty op2)
        {
            MassDriver sum = this + (MassDriver)op2;
            Value = sum.Value;
            bestWarp = sum.bestWarp;
            slotsAtBest = sum.slotsAtBest;
        }

        /// <summary>
        /// Polymorphic multiplication of properties.
        /// </summary>
        /// <param name="scalar"></param>
        public override void Scale(int scalar)
        {
            // Unchanged: a slot's rating does not depend on how many drivers it holds.
        }

        /// <summary>
        /// Provide a way to add properties in the ship design.
        /// </summary>
        /// <param name="op1">LHS operand.</param>
        /// <param name="op2">RHS operand.</param>
        /// <returns>
        /// A <see cref="MassDriver"/> representing the combination of two (sets of) slots:
        /// the best warp, plus one when that best warp is in two or more different slots.
        /// </returns>
        public static MassDriver operator +(MassDriver op1, MassDriver op2)
        {
            int best1 = op1.BestWarp;
            int best2 = op2.BestWarp;
            if (best1 <= 0 && best2 <= 0)
            {
                return new MassDriver(0);
            }

            if (best1 == best2)
            {
                return FromSlots(best1, op1.SlotsAtBest + op2.SlotsAtBest);
            }

            return best1 > best2 ? FromSlots(best1, op1.SlotsAtBest) : FromSlots(best2, op2.SlotsAtBest);
        }

        /// <summary>
        /// Operator* to scale (multiply) properties in the ship design.
        /// Mass Driver doesn't scale: several drivers in ONE slot count as that slot's single
        /// rating (production-queue.md section 10b - the +1 is only for two different slots).
        /// </summary>
        /// <param name="op1">The <see cref="MassDriver"/> to scale.</param>
        /// <param name="scalar">The number of mass drivers in the stack (slot).</param>
        /// <returns>A copy of the mass driver: the slot's rating is unchanged.</returns>
        public static MassDriver operator *(MassDriver op1, int scalar)
        {
            return new MassDriver(op1);
        }

        #endregion

        #region Load Save Xml

        /// <summary>
        /// Load from XML: initializing constructor from an XML node.
        /// </summary>
        /// <param name="node">An <see cref="XmlNode"/> within 
        /// a Nova component definition file (xml document).
        /// </param>
        public MassDriver(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    if (subnode.Name.ToLowerInvariant() == "value")
                    {
                        Value = int.Parse(((XmlText)subnode.FirstChild).Value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    // ignore incomplete or unset values
                }
                subnode = subnode.NextSibling;
            }
        }

        /// <summary>
        /// Save: Serialize this property to an <see cref="XmlElement"/>.
        /// </summary>
        /// <param name="xmldoc">The parent <see cref="XmlDocument"/>.</param>
        /// <returns>An <see cref="XmlElement"/> representation of the Property.</returns>
        public override XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelProperty = xmldoc.CreateElement("Property");

            // store the value
            XmlElement xmlelValue = xmldoc.CreateElement("Value");
            XmlText xmltxtValue = xmldoc.CreateTextNode(this.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            xmlelValue.AppendChild(xmltxtValue);
            xmlelProperty.AppendChild(xmlelValue);

            return xmlelProperty;
        }

        #endregion
    }
}

