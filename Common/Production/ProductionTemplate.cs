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
    using System.Linq;
    using System.Xml;

    /// <summary>
    /// The auto-build item types a production template may hold - the original's six-bit type
    /// field (production-queue.md section 10 catalog, types 0-6; captions are dynamic strings
    /// 126 + type, section 6 "Captions").
    /// </summary>
    public enum TemplateItemType
    {
        Mines = 0,
        Factories = 1,
        Defenses = 2,
        Alchemy = 3,
        MinTerraform = 4,
        MaxTerraform = 5,
        MineralPackets = 6,
    }

    /// <summary>
    /// One template line: an auto-build type and its "up to N" figure (ten-bit quantity,
    /// production-queue.md 10f and the queue-record layout of section 10).
    /// </summary>
    public sealed class ProductionTemplateEntry
    {
        /// <summary>The ten-bit quantity field's ceiling (section 10, record layout).</summary>
        public const int MaxQuantity = 1023;

        public ProductionTemplateEntry(TemplateItemType type, int quantity)
        {
            Type = type;
            Quantity = Math.Max(1, Math.Min(MaxQuantity, quantity));
        }

        public TemplateItemType Type { get; }

        /// <summary>The per-turn "up to N" figure, 1..1023.</summary>
        public int Quantity { get; }

        /// <summary>The catalog caption of the type (dynamic strings 126-132).</summary>
        public string Caption
        {
            get { return CaptionOf(Type); }
        }

        public static string CaptionOf(TemplateItemType type)
        {
            switch (type)
            {
                case TemplateItemType.Mines: return "Mines";
                case TemplateItemType.Factories: return "Factories";
                case TemplateItemType.Defenses: return "Defenses";
                case TemplateItemType.Alchemy: return "Alchemy";
                case TemplateItemType.MinTerraform: return "Min Terraform";
                case TemplateItemType.MaxTerraform: return "Max Terraform";
                default: return "Mineral Packets";
            }
        }

        /// <summary>
        /// The race-specific exclusions of the default-template copy (production-queue.md 10f):
        /// Alternate Reality skips types 0-2 (Mines, Factories, Defenses), Claim Adjuster skips
        /// types 4 and 5 (Min and Max Terraform).
        /// </summary>
        public static bool IsExcludedFor(TemplateItemType type, Race race)
        {
            if (race == null)
            {
                return false;
            }

            if (race.HasTrait("AR") && (type == TemplateItemType.Mines || type == TemplateItemType.Factories || type == TemplateItemType.Defenses))
            {
                return true;
            }

            return race.HasTrait("CA") && (type == TemplateItemType.MinTerraform || type == TemplateItemType.MaxTerraform);
        }

        /// <summary>The auto-build queue order this line stands for, priced for the race.</summary>
        public ProductionOrder ToOrder(Race race)
        {
            IProductionUnit unit;
            switch (Type)
            {
                case TemplateItemType.Mines: unit = new MineProductionUnit(race); break;
                case TemplateItemType.Factories: unit = new FactoryProductionUnit(race); break;
                case TemplateItemType.Defenses: unit = new DefenseProductionUnit(race); break;
                case TemplateItemType.Alchemy: unit = new AlchemyProductionUnit(race); break;
                case TemplateItemType.MinTerraform: unit = new TerraformProductionUnit(race, true); break;
                case TemplateItemType.MaxTerraform: unit = new TerraformProductionUnit(race, false); break;
                default: unit = new PacketProductionUnit(race, PacketMineral.Mixed, true); break;
            }

            return new ProductionOrder(Quantity, unit, true);
        }

        /// <summary>
        /// The template type of an auto-build queue order, or null when the order is not one a
        /// template can hold (manual orders, ships, Genesis Device).
        /// </summary>
        public static TemplateItemType? TypeOf(ProductionOrder order)
        {
            if (order == null || !order.IsAutoBuild || order.Unit == null)
            {
                return null;
            }

            switch (order.Unit)
            {
                case MineProductionUnit _: return TemplateItemType.Mines;
                case FactoryProductionUnit _: return TemplateItemType.Factories;
                case DefenseProductionUnit _: return TemplateItemType.Defenses;
                case AlchemyProductionUnit _: return TemplateItemType.Alchemy;
                case TerraformProductionUnit terraform: return terraform.MinimumOnly ? TemplateItemType.MinTerraform : TemplateItemType.MaxTerraform;
                case PacketProductionUnit packet: return packet.AutoBuild ? TemplateItemType.MineralPackets : (TemplateItemType?)null;
                default: return null;
            }
        }
    }

    /// <summary>
    /// One saved production template (production-queue.md section 9 and 10f;
    /// client-ui-dialog-catalog.md "Production queue editor"): a name, up to 12 auto-build
    /// entries and the "contribute only leftover resources to research" flag.
    /// </summary>
    public sealed class ProductionTemplate
    {
        /// <summary>Entries per slot (section 9, "each of the 4 slots holds up to 12").</summary>
        public const int MaxEntries = 12;

        public string Name = string.Empty;

        public List<ProductionTemplateEntry> Entries = new List<ProductionTemplateEntry>();

        /// <summary>The stored "contribute only leftover resources to research" bit.</summary>
        public bool OnlyLeftover;

        public bool IsEmpty
        {
            get { return Entries.Count == 0; }
        }

        /// <summary>Appends an entry; false (nothing added) when the template is full.</summary>
        public bool TryAdd(ProductionTemplateEntry entry)
        {
            if (entry == null || Entries.Count >= MaxEntries)
            {
                return false;
            }

            Entries.Add(entry);
            return true;
        }

        /// <summary>A copy of this template under another name.</summary>
        public ProductionTemplate WithName(string name)
        {
            ProductionTemplate copy = Clone();
            copy.Name = name ?? string.Empty;
            return copy;
        }

        public ProductionTemplate Clone()
        {
            return new ProductionTemplate
            {
                Name = Name,
                Entries = new List<ProductionTemplateEntry>(Entries),
                OnlyLeftover = OnlyLeftover,
            };
        }

        /// <summary>
        /// The queue orders this template gives a planet of <paramref name="race"/>, in order,
        /// with the race exclusions of 10f applied.
        /// </summary>
        public List<ProductionOrder> OrdersFor(Race race)
        {
            return Entries
                .Where(entry => !ProductionTemplateEntry.IsExcludedFor(entry.Type, race))
                .Select(entry => entry.ToOrder(race))
                .ToList();
        }

        /// <summary>
        /// A template holding the auto-build entries of a planet's queue, in order (manual
        /// orders and ships are left out; at most 12), and the planet's leftover flag.
        /// </summary>
        public static ProductionTemplate FromQueue(string name, Star star)
        {
            ProductionTemplate template = new ProductionTemplate { Name = name ?? string.Empty };
            if (star == null)
            {
                return template;
            }

            template.OnlyLeftover = star.OnlyLeftover;
            foreach (ProductionOrder order in star.ManufacturingQueue.Queue)
            {
                TemplateItemType? type = ProductionTemplateEntry.TypeOf(order);
                if (type.HasValue && !template.TryAdd(new ProductionTemplateEntry(type.Value, order.Quantity)))
                {
                    break;
                }
            }

            return template;
        }

        /// <summary>
        /// The Player's Guide's illustrative default template (production-queue.md section 9
        /// table): eight auto-build entries. Offered as a starting point in the template
        /// manager; nothing applies it unasked.
        /// </summary>
        public static ProductionTemplate ManualExample()
        {
            ProductionTemplate template = new ProductionTemplate { Name = "Player's Guide example" };
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.MinTerraform, 10));
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Factories, 10));
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Mines, 10));
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Defenses, 2));
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Factories, 25));
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Mines, 25));
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.MaxTerraform, 10));
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Defenses, 5));
            return template;
        }

        public XmlElement ToXml(XmlDocument xmldoc, int slot)
        {
            XmlElement xmlelTemplate = xmldoc.CreateElement("Template");
            xmlelTemplate.SetAttribute("Slot", slot.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(Name))
            {
                Global.SaveData(xmldoc, xmlelTemplate, "Name", Name);
            }

            if (OnlyLeftover)
            {
                Global.SaveData(xmldoc, xmlelTemplate, "OnlyLeftover", "true");
            }

            foreach (ProductionTemplateEntry entry in Entries)
            {
                XmlElement xmlelEntry = xmldoc.CreateElement("Entry");
                xmlelEntry.SetAttribute("Type", ((int)entry.Type).ToString(CultureInfo.InvariantCulture));
                xmlelEntry.SetAttribute("Quantity", entry.Quantity.ToString(CultureInfo.InvariantCulture));
                xmlelTemplate.AppendChild(xmlelEntry);
            }

            return xmlelTemplate;
        }

        public static ProductionTemplate FromXml(XmlNode node)
        {
            ProductionTemplate template = new ProductionTemplate();
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "name":
                        template.Name = subnode.FirstChild?.Value ?? string.Empty;
                        break;

                    case "onlyleftover":
                        template.OnlyLeftover = subnode.FirstChild != null && bool.Parse(subnode.FirstChild.Value);
                        break;

                    case "entry":
                        int type = int.Parse(subnode.Attributes["Type"].Value, CultureInfo.InvariantCulture);
                        int quantity = int.Parse(subnode.Attributes["Quantity"].Value, CultureInfo.InvariantCulture);
                        if (Enum.IsDefined(typeof(TemplateItemType), type))
                        {
                            template.TryAdd(new ProductionTemplateEntry((TemplateItemType)type, quantity));
                        }

                        break;
                }

                subnode = subnode.NextSibling;
            }

            return template;
        }
    }

    /// <summary>
    /// An empire's four template slots and which one is the default template that is copied
    /// into every newly colonised or captured planet's queue (production-queue.md section 9:
    /// "a 4-slot manager"; 10f: "the new owner's stored default production template is copied
    /// into the planet's queue ... and the leftover flag is copied from the template").
    /// AMBIGUITY: the specs do not say whether the default template is one of the four slots or a
    /// separate record, nor how it is chosen. Here it is one of the slots, chosen by the player
    /// (<see cref="DefaultSlot"/>, or none); slot 1 by default. SPEC GAP: the slots' initial
    /// contents for a new empire are not specified; they start empty (copying an empty default
    /// leaves the new queue empty and clears the leftover flag).
    /// </summary>
    public sealed class ProductionTemplateSet
    {
        public const int SlotCount = 4;

        /// <summary><see cref="DefaultSlot"/> value for "no default template".</summary>
        public const int NoDefault = -1;

        private readonly ProductionTemplate[] slots = new ProductionTemplate[SlotCount];

        public ProductionTemplateSet()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                slots[i] = new ProductionTemplate();
            }
        }

        /// <summary>The slot index applied to new colonies, or <see cref="NoDefault"/>.</summary>
        public int DefaultSlot { get; set; } = 0;

        public IReadOnlyList<ProductionTemplate> Slots
        {
            get { return slots; }
        }

        public ProductionTemplate this[int slot]
        {
            get { return slots[slot]; }
        }

        /// <summary>The default template, or null when none is chosen.</summary>
        public ProductionTemplate Default
        {
            get { return DefaultSlot >= 0 && DefaultSlot < SlotCount ? slots[DefaultSlot] : null; }
        }

        /// <summary>Replaces one slot's content (a copy is stored).</summary>
        public void SetSlot(int slot, ProductionTemplate template)
        {
            if (slot < 0 || slot >= SlotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            slots[slot] = template == null ? new ProductionTemplate() : template.Clone();
        }

        /// <summary>
        /// Replaces the planet's queue with the template's orders (race exclusions applied) and
        /// copies its leftover flag - the copy 10f describes for the default template.
        /// </summary>
        public static void Apply(ProductionTemplate template, Star star, Race race)
        {
            if (template == null || star == null)
            {
                return;
            }

            if (star.ManufacturingQueue == null)
            {
                star.ManufacturingQueue = new ProductionQueue();
            }

            star.ManufacturingQueue.Clear();
            star.ManufacturingQueue.Queue.AddRange(template.OrdersFor(race));
            star.OnlyLeftover = template.OnlyLeftover;
        }

        /// <summary>
        /// The colonisation / invasion hook (production-queue.md 10f; turn-generation-engine.md
        /// section 11): copies the new owner's default template into the planet's queue. Does
        /// nothing when the owner has no default template chosen.
        /// </summary>
        public static void ApplyDefault(Star star, EmpireData newOwner)
        {
            if (star == null || newOwner == null || newOwner.ProductionTemplates == null)
            {
                return;
            }

            ProductionTemplate template = newOwner.ProductionTemplates.Default;
            if (template != null)
            {
                Apply(template, star, newOwner.Race);
            }
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelSet = xmldoc.CreateElement("ProductionTemplates");
            xmlelSet.SetAttribute("Default", DefaultSlot.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < SlotCount; i++)
            {
                if (!slots[i].IsEmpty || !string.IsNullOrEmpty(slots[i].Name) || slots[i].OnlyLeftover)
                {
                    xmlelSet.AppendChild(slots[i].ToXml(xmldoc, i));
                }
            }

            return xmlelSet;
        }

        public static ProductionTemplateSet FromXml(XmlNode node)
        {
            ProductionTemplateSet set = new ProductionTemplateSet();
            XmlAttribute defaultAttribute = node.Attributes?["Default"];
            if (defaultAttribute != null)
            {
                int value = int.Parse(defaultAttribute.Value, CultureInfo.InvariantCulture);
                set.DefaultSlot = value >= 0 && value < SlotCount ? value : NoDefault;
            }

            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                if (subnode.Name.Equals("Template", StringComparison.OrdinalIgnoreCase))
                {
                    int slot = int.Parse(subnode.Attributes["Slot"].Value, CultureInfo.InvariantCulture);
                    if (slot >= 0 && slot < SlotCount)
                    {
                        set.slots[slot] = ProductionTemplate.FromXml(subnode);
                    }
                }

                subnode = subnode.NextSibling;
            }

            return set;
        }
    }
}
