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

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>The kind of one available production-catalog row, for tests and captioning.</summary>
    public enum ProductionCatalogKind
    {
        ShipDesign,
        StarbaseDesign,
        GenesisDevice,
        ManualPacket,
        Factory,
        Mine,
        Defense,
        Alchemy,
        Scanner,
        TerraformEnvironment,
        AutoTerraform,
        AutoPacket,
    }

    /// <summary>
    /// One row of the planet production dialog's "available to build" list
    /// (behavior-specs-11/production-queue.md 10l). The unit is a price display; the dialog builds
    /// a fresh unit per added order. <see cref="PresetQuantity"/> is what the quantity field is
    /// pre-filled with when the row is selected (1 for unlimited rows), and
    /// <see cref="MaxQuantity"/> is the most one order of this row may add.
    /// </summary>
    public sealed class ProductionCatalogEntry
    {
        public ProductionCatalogEntry(
            ProductionCatalogKind kind,
            IProductionUnit unit,
            long designKey = Global.None,
            string displayName = null,
            int presetQuantity = 1,
            int maxQuantity = ProductionCatalog.LineQuantityCap,
            bool isTerraform = false,
            bool autoOnly = false,
            bool manualOnly = false,
            bool isScanner = false)
        {
            Kind = kind;
            Unit = unit;
            DesignKey = designKey;
            DisplayName = displayName;
            PresetQuantity = Math.Max(1, presetQuantity);
            MaxQuantity = Math.Max(1, maxQuantity);
            IsTerraform = isTerraform;
            AutoOnly = autoOnly;
            ManualOnly = manualOnly;
            IsScanner = isScanner;
        }

        public ProductionCatalogKind Kind { get; }

        public IProductionUnit Unit { get; }

        /// <summary>The design key for a ship/starbase row, otherwise <see cref="Global.None"/>.</summary>
        public long DesignKey { get; }

        /// <summary>A caption override, or null to use the unit's own name.</summary>
        public string DisplayName { get; }

        public string Name
        {
            get { return string.IsNullOrEmpty(DisplayName) ? Unit.Name : DisplayName; }
        }

        /// <summary>The quantity the Add field is pre-filled with when this row is selected.</summary>
        public int PresetQuantity { get; }

        /// <summary>The most one order of this row may add.</summary>
        public int MaxQuantity { get; }

        public bool IsTerraform { get; }

        /// <summary>Only addable as an auto-build order.</summary>
        public bool AutoOnly { get; }

        /// <summary>Only addable as a manual order.</summary>
        public bool ManualOnly { get; }

        public bool IsScanner { get; }
    }

    /// <summary>
    /// The queue entries a catalog rebuild drops, because the dialog no longer offers them
    /// (production-queue.md 10l: "every queue entry that has no matching catalog item is dropped
    /// from the dialog's working copy").
    /// </summary>
    public sealed class ProductionCatalogQueueFilter
    {
        public ProductionCatalogQueueFilter(IReadOnlyList<ProductionOrder> dropped)
        {
            Dropped = dropped ?? Array.Empty<ProductionOrder>();
        }

        public IReadOnlyList<ProductionOrder> Dropped { get; }
    }

    /// <summary>
    /// The production dialog's available-item list, kept free of UI types so it is unit tested
    /// (behavior-specs-11/production-queue.md 10l, <c>FUN_10d0_010c</c>). The rows are offered in
    /// the spec's order:
    /// <list type="number">
    /// <item>ship designs, only while the starbase has a dock (capacity above 0) and the design is
    ///   no larger than it;</item>
    /// <item>starbase designs, except the installed one;</item>
    /// <item>the Genesis Device, when the race may build it;</item>
    /// <item>the four manual packet items, only while the starbase carries a mass driver;</item>
    /// <item>manual Factory / Mine / Defenses, never for Alternate Reality, only while the build
    ///   room is above 0, pre-filled with the room (at most 1,020);</item>
    /// <item>Mineral Alchemy, always;</item>
    /// <item>the "best planetary scanner" item (type 27), one unit, only while the planet has no
    ///   scanner and the race is not Alternate Reality;</item>
    /// <item>Terraform Environment, only with headroom, pre-filled with it;</item>
    /// <item>the auto-build entries - the auto Min / Max Terraform one (not for Claim Adjusters)
    ///   and the auto Mineral Packets one (always, even with no mass driver and no destination).
    ///   The port exposes auto-build for the installations and alchemy through the dialog's
    ///   auto-build checkbox rather than separate rows (a deviation noted in the coverage).</item>
    /// </list>
    /// </summary>
    public static class ProductionCatalog
    {
        /// <summary>The queue line's ten-bit quantity ceiling (section 10, record layout).</summary>
        public const int LineQuantityCap = 1023;

        /// <summary>The most a manual Factory or Mine order may add (10l item 5, "at most 1,020").</summary>
        public const int ManualInstallationQuantityCap = 1020;

        /// <summary>The caption of the combined auto Min / Max Terraform row.</summary>
        public const string AutoTerraformCaption = "Min / Max Terraform (Auto Build)";

        /// <summary>The catalog rows offered for <paramref name="star"/> and <paramref name="empire"/>.</summary>
        public static IReadOnlyList<ProductionCatalogEntry> Build(Star star, EmpireData empire)
        {
            var items = new List<ProductionCatalogEntry>();
            if (star == null || empire == null)
            {
                return items;
            }

            Race race = empire.Race;

            // 1-2: ship and starbase designs.
            Fleet starbase = star.Starbase;
            int dockCapacity = starbase?.TotalDockCapacity ?? 0;
            long installedStarbaseDesign = Global.None;
            if (starbase != null && starbase.Composition.Count > 0)
            {
                installedStarbaseDesign = starbase.Composition.Values.First().Design.Id;
            }

            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Id == installedStarbaseDesign)
                {
                    continue;
                }

                if (!design.IsStarbase && dockCapacity < design.Mass)
                {
                    continue;
                }

                items.Add(new ProductionCatalogEntry(
                    design.IsStarbase ? ProductionCatalogKind.StarbaseDesign : ProductionCatalogKind.ShipDesign,
                    new ShipProductionUnit(design),
                    designKey: design.Id));
            }

            // 3: the Genesis Device, when the empire may build it.
            if (empire.AvailableComponents != null && empire.AvailableComponents.ContainsKey("Genesis Device"))
            {
                items.Add(new ProductionCatalogEntry(
                    ProductionCatalogKind.GenesisDevice, new GenesisDeviceProductionUnit(empire)));
            }

            // 4: the four manual packet items, only with a mass driver.
            foreach (PacketProductionUnit packet in PacketOrders.CatalogItems(star, race))
            {
                if (packet.AutoBuild)
                {
                    continue;
                }

                items.Add(new ProductionCatalogEntry(
                    ProductionCatalogKind.ManualPacket,
                    packet,
                    manualOnly: true));
            }

            // 5: manual Factory / Mine / Defenses, never for Alternate Reality, only with room.
            if (race == null || !race.HasTrait("AR"))
            {
                AddInstallation(items, ProductionCatalogKind.Factory, new FactoryProductionUnit(race), star);
                AddInstallation(items, ProductionCatalogKind.Mine, new MineProductionUnit(race), star);
                AddInstallation(items, ProductionCatalogKind.Defense, new DefenseProductionUnit(race), star);
            }

            // 6: Mineral Alchemy, always.
            items.Add(new ProductionCatalogEntry(ProductionCatalogKind.Alchemy, new AlchemyProductionUnit(race)));

            // 7: the best planetary scanner, one unit, only with no scanner and not AR.
            if (!ScannerProductionUnit.HasScanner(star) && (race == null || !race.HasTrait("AR")))
            {
                items.Add(new ProductionCatalogEntry(
                    ProductionCatalogKind.Scanner,
                    new ScannerProductionUnit(),
                    maxQuantity: 1,
                    manualOnly: true,
                    isScanner: true));
            }

            // 8: Terraform Environment, only with headroom, pre-filled with it.
            int headroom = TerraformProductionUnit.Headroom(star, race, TerraformReach.For(empire));
            if (headroom > 0)
            {
                items.Add(new ProductionCatalogEntry(
                    ProductionCatalogKind.TerraformEnvironment,
                    new TerraformProductionUnit(race),
                    displayName: ProductionCaptions.TerraformEnvironment,
                    presetQuantity: headroom,
                    maxQuantity: headroom,
                    isTerraform: true));
            }

            // 9: auto-build rows. The auto packet is always offered (10l item 9 and 10k item 4).
            foreach (PacketProductionUnit packet in PacketOrders.CatalogItems(star, race))
            {
                if (!packet.AutoBuild)
                {
                    continue;
                }

                items.Add(new ProductionCatalogEntry(
                    ProductionCatalogKind.AutoPacket,
                    packet,
                    displayName: packet.Name + " (Auto Build)",
                    autoOnly: true));
            }

            // The auto Min / Max Terraform row, always except for Claim Adjusters (10l item 9 and
            // the 10f exclusions: types 4-5 are skipped for Claim Adjuster).
            if (race == null || !race.HasTrait("CA"))
            {
                items.Add(new ProductionCatalogEntry(
                    ProductionCatalogKind.AutoTerraform,
                    new TerraformProductionUnit(race, false),
                    displayName: AutoTerraformCaption,
                    isTerraform: true,
                    autoOnly: true));
            }

            return items;
        }

        private static void AddInstallation(
            List<ProductionCatalogEntry> items, ProductionCatalogKind kind, IProductionUnit unit, Star star)
        {
            int? room = unit.RoomForManualOrder(star);
            if (!room.HasValue)
            {
                return;
            }

            int allowed = Math.Min(room.Value, ManualInstallationQuantityCap);
            if (allowed <= 0)
            {
                return;
            }

            items.Add(new ProductionCatalogEntry(
                kind, unit, presetQuantity: allowed, maxQuantity: allowed));
        }

        /// <summary>
        /// The queued orders the catalog no longer offers (10l: an entry whose catalog item is
        /// gone is dropped from the dialog's working copy): a manual packet on a planet that lost
        /// its mass driver, a scanner order on a planet that has one, a Factory / Mine / Defenses
        /// order for Alternate Reality or a planet at its build cap, a Genesis Device no longer
        /// available, a Terraform Environment order with no headroom, or a ship/starbase design no
        /// longer in the empire's design list.
        /// </summary>
        public static ProductionCatalogQueueFilter FilterQueue(
            Star star, EmpireData empire, IReadOnlyList<ProductionCatalogEntry> catalog)
        {
            var dropped = new List<ProductionOrder>();
            if (star?.ManufacturingQueue == null || empire == null || catalog == null)
            {
                return new ProductionCatalogQueueFilter(dropped);
            }

            Race race = empire.Race;
            foreach (ProductionOrder order in star.ManufacturingQueue.Queue)
            {
                if (!HasCatalogItem(order, catalog, race))
                {
                    dropped.Add(order);
                }
            }

            return new ProductionCatalogQueueFilter(dropped);
        }

        /// <summary>
        /// True when the catalog still offers the queued order. An auto-build Factory / Mine /
        /// Defenses / Mineral Alchemy order is always offered (for a non-Alternate-Reality race,
        /// for the three installations) even when the manual row has disappeared at the build cap,
        /// because this port folds those auto types into the manual rows plus the dialog's
        /// auto-build checkbox rather than giving each its own row.
        /// </summary>
        private static bool HasCatalogItem(ProductionOrder order, IReadOnlyList<ProductionCatalogEntry> catalog, Race race)
        {
            if (Match(order, catalog) != null)
            {
                return true;
            }

            if (!order.IsAutoBuild)
            {
                return false;
            }

            switch (order.Unit)
            {
                case FactoryProductionUnit _:
                case MineProductionUnit _:
                case DefenseProductionUnit _:
                    return race == null || !race.HasTrait("AR");

                case AlchemyProductionUnit _:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>The catalog row a queued order belongs to, or null when the catalog offers no
        /// such item. An auto-build installation/alchemy order matches the manual row (this port's
        /// auto-build is the dialog's checkbox, not a separate catalog row).</summary>
        public static ProductionCatalogEntry Match(ProductionOrder order, IReadOnlyList<ProductionCatalogEntry> catalog)
        {
            if (order?.Unit == null || catalog == null)
            {
                return null;
            }

            foreach (ProductionCatalogEntry entry in catalog)
            {
                if (Matches(order, entry))
                {
                    return entry;
                }
            }

            return null;
        }

        private static bool Matches(ProductionOrder order, ProductionCatalogEntry entry)
        {
            switch (order.Unit)
            {
                case ShipProductionUnit ship:
                    return (entry.Kind == ProductionCatalogKind.ShipDesign || entry.Kind == ProductionCatalogKind.StarbaseDesign)
                        && entry.DesignKey == ship.DesignKey;

                case FactoryProductionUnit _:
                    return entry.Kind == ProductionCatalogKind.Factory;

                case MineProductionUnit _:
                    return entry.Kind == ProductionCatalogKind.Mine;

                case DefenseProductionUnit _:
                    return entry.Kind == ProductionCatalogKind.Defense;

                case AlchemyProductionUnit _:
                    return entry.Kind == ProductionCatalogKind.Alchemy;

                case ScannerProductionUnit _:
                    return entry.Kind == ProductionCatalogKind.Scanner;

                case GenesisDeviceProductionUnit _:
                    return entry.Kind == ProductionCatalogKind.GenesisDevice;

                case PacketProductionUnit _:
                    return order.IsAutoBuild
                        ? entry.Kind == ProductionCatalogKind.AutoPacket
                        : entry.Kind == ProductionCatalogKind.ManualPacket;

                case TerraformProductionUnit _:
                    return order.IsAutoBuild
                        ? entry.Kind == ProductionCatalogKind.AutoTerraform
                        : entry.Kind == ProductionCatalogKind.TerraformEnvironment;

                default:
                    return false;
            }
        }
    }
}
