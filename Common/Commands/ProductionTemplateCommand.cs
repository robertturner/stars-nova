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

namespace Nova.Common.Commands
{
    using System;
    using System.Globalization;
    using System.Xml;

    /// <summary>
    /// Edits the empire's production templates (EmpireData.ProductionTemplates;
    /// production-queue.md sections 9 and 10f): either stores one slot's whole content (name, up to
    /// 12 auto-build entries, the leftover flag), or chooses which slot is the default template
    /// copied into new and captured colonies (or none). The templates must reach the server,
    /// because the default-template copy runs there during colonisation and invasion.
    /// </summary>
    public class ProductionTemplateCommand : ICommand
    {
        /// <summary>Stores <paramref name="template"/> into <paramref name="slot"/> (0-3).</summary>
        public ProductionTemplateCommand(int slot, ProductionTemplate template)
        {
            Slot = slot;
            Template = template?.Clone() ?? new ProductionTemplate();
        }

        /// <summary>Chooses the default slot (0-3, or ProductionTemplateSet.NoDefault).</summary>
        public ProductionTemplateCommand(int defaultSlot)
        {
            Slot = -1;
            DefaultSlot = defaultSlot;
        }

        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        public ProductionTemplateCommand(XmlNode node)
        {
            Slot = -1;
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "slot":
                        Slot = int.Parse(subnode.FirstChild.Value, CultureInfo.InvariantCulture);
                        break;

                    case "defaultslot":
                        DefaultSlot = int.Parse(subnode.FirstChild.Value, CultureInfo.InvariantCulture);
                        break;

                    case "template":
                        Template = ProductionTemplate.FromXml(subnode);
                        break;
                }

                subnode = subnode.NextSibling;
            }
        }

        /// <summary>The slot whose content <see cref="Template"/> replaces, or -1.</summary>
        public int Slot { get; private set; }

        /// <summary>The new slot content (when <see cref="Slot"/> is 0-3).</summary>
        public ProductionTemplate Template { get; private set; }

        /// <summary>The new default slot, or null to leave it alone.</summary>
        public int? DefaultSlot { get; private set; }

        public bool IsValid(EmpireData empire)
        {
            if (empire == null || empire.ProductionTemplates == null)
            {
                return false;
            }

            bool setsSlot = Slot >= 0;
            if (setsSlot && (Slot >= ProductionTemplateSet.SlotCount || Template == null
                || Template.Entries.Count > ProductionTemplate.MaxEntries))
            {
                return false;
            }

            if (DefaultSlot.HasValue
                && DefaultSlot.Value != ProductionTemplateSet.NoDefault
                && (DefaultSlot.Value < 0 || DefaultSlot.Value >= ProductionTemplateSet.SlotCount))
            {
                return false;
            }

            return setsSlot || DefaultSlot.HasValue;
        }

        public void ApplyToState(EmpireData empire)
        {
            if (Slot >= 0 && Slot < ProductionTemplateSet.SlotCount && Template != null)
            {
                empire.ProductionTemplates.SetSlot(Slot, Template);
            }

            if (DefaultSlot.HasValue)
            {
                empire.ProductionTemplates.DefaultSlot = DefaultSlot.Value;
            }
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "ProductionTemplate");
            if (Slot >= 0 && Template != null)
            {
                Global.SaveData(xmldoc, xmlelCom, "Slot", Slot.ToString(CultureInfo.InvariantCulture));
                xmlelCom.AppendChild(Template.ToXml(xmldoc, Slot));
            }

            if (DefaultSlot.HasValue)
            {
                Global.SaveData(xmldoc, xmlelCom, "DefaultSlot", DefaultSlot.Value.ToString(CultureInfo.InvariantCulture));
            }

            return xmlelCom;
        }
    }
}
