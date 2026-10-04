#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
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

namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    /// <summary>
    /// Save/load conformance: every persisted object type is built fully populated, written with
    /// the project's own ToXml, read back with its own XmlNode loader (from the saved TEXT, as a
    /// real save file is), written again, and the two texts must be identical - a field that is
    /// silently dropped, defaulted or re-formatted by a load shows up as a difference. Key field
    /// values are spot-checked as well. The whole server state file goes through the real
    /// ServerData.Save / Restore path.
    ///
    /// Not persisted by design (not asserted): CargoTask.Pending (recomputed by every Perform in
    /// the same generation that reads it), ShipToken.Shields (recharged before every battle),
    /// Star.RecycledScrapResources / PendingColonizations (per-generation scratch), the
    /// NoProductionUnit (the legacy WinForms queue header row; it never enters a saved queue and
    /// its ToXml returns null).
    /// </summary>
    [TestFixture]
    public class SaveFormatCoverageTest
    {
        // ------------------------------------------------------------------ helpers

        /// <summary>Writes an element and returns the saved text (the element's outer XML).</summary>
        private static string Write(Func<XmlDocument, XmlElement> write)
        {
            XmlDocument xmldoc = new XmlDocument();
            XmlElement root = xmldoc.CreateElement("Root");
            xmldoc.AppendChild(root);
            root.AppendChild(write(xmldoc));
            return root.InnerXml;
        }

        /// <summary>
        /// Save, load from the saved text, save again: the two texts must be identical. Returns
        /// the loaded object for spot checks.
        /// </summary>
        private static T RoundTrip<T>(Func<XmlDocument, XmlElement> save, Func<XmlNode, T> load, Func<T, XmlDocument, XmlElement> resave)
        {
            string first = Write(save);

            XmlDocument reread = new XmlDocument();
            reread.LoadXml("<Root>" + first + "</Root>");
            T loaded = load(reread.DocumentElement.FirstChild);

            string second = Write(xmldoc => resave(loaded, xmldoc));
            AssertSameText(first, second);
            return loaded;
        }

        private static void AssertSameText(string first, string second)
        {
            if (first == second)
            {
                return;
            }

            int at = 0;
            while (at < first.Length && at < second.Length && first[at] == second[at])
            {
                at++;
            }

            int from = Math.Max(0, at - 120);
            Assert.Fail("The re-saved text differs from the first save at offset " + at + ":" + Environment.NewLine
                + "first : ..." + first.Substring(from, Math.Min(first.Length - from, 240)) + Environment.NewLine
                + "second: ..." + second.Substring(from, Math.Min(second.Length - from, 240)));
        }

        private static Race MakeRace(string name, string primary)
        {
            Race race = new Race { Name = name, PluralName = name + "s" };
            race.Traits.SetPrimary(primary);
            race.Traits.Add("IFE");
            race.GrowthRate = 15;
            race.FactoryBuildCost = 10;
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.OperableFactories = 10;
            race.MineBuildCost = 5;
            race.MineProductionRate = 10;
            race.OperableMines = 10;
            race.ResearchCosts = new TechLevel(100);
            race.Icon.Source = name + "0001.png";
            return race;
        }

        private static EmpireData MakeEmpire(ushort id, Race race)
        {
            EmpireData empire = new EmpireData { Id = id, Race = race };
            empire.TurnYear = 2105;
            return empire;
        }

        private static ShipDesign MakeDesign(EmpireData empire, string name, int fuel, int cargo)
        {
            Component blueprint = new Component { Name = name + " Hull", Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), BaseCargo = cargo, FuelCapacity = fuel, ArmorStrength = 50 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(empire.GetNextDesignKey()) { Name = name, Blueprint = blueprint };
            design.Icon = AllShipIcons.Data.IconList[0];
            design.Update();
            empire.Designs[design.Key] = design;
            return design;
        }

        private static Star MakeStar(string name, int x, int y)
        {
            return new Star
            {
                Name = name,
                Position = new NovaPoint(x, y),
                MineralConcentration = new Resources(40, 50, 60, 0),
                Gravity = 45,
                Radiation = 55,
                Temperature = 65,
                OriginalGravity = 40,
                OriginalRadiation = 50,
                OriginalTemperature = 60,
            };
        }

        private static Star MakeOwnedStar(EmpireData owner, string name, int x, int y)
        {
            Star star = MakeStar(name, x, y);
            star.Owner = owner.Id;
            star.ThisRace = owner.Race;
            star.Colonists = 123400;
            star.PopulationCarry = 37;
            star.Factories = 101;
            star.Mines = 77;
            star.Defenses = 12;
            star.ResearchAllocation = 15;
            star.IsHomeWorld = true;
            star.OnlyLeftover = true;
            star.ScanRange = 150;
            star.DefenseType = "SDI";
            star.ScannerType = "Viewer 50";
            star.PacketDestination = "Far Away";
            star.PacketWarp = 8;
            star.HasFleetsInOrbit = true;
            star.ResourcesOnHand = new Resources(11, 22, 33, 44);
            star.MineralMiningProgress = new Resources(3, 4, 5, 0);
            owner.OwnedStars.Add(star);
            return star;
        }

        /// <summary>One order of every production unit type, auto and manual, some part-paid.</summary>
        private static void FillQueue(Star star, EmpireData owner, ShipDesign ship, ShipDesign starbase)
        {
            Race race = owner.Race;
            List<ProductionOrder> queue = star.ManufacturingQueue.Queue;
            queue.Add(new ProductionOrder(10, new FactoryProductionUnit(race), true));
            queue.Add(new ProductionOrder(5, new MineProductionUnit(race), false));
            queue.Add(new ProductionOrder(3, new DefenseProductionUnit(race), true));

            AlchemyProductionUnit manualAlchemy = new AlchemyProductionUnit(race);
            Star wallet = new Star { ResourcesOnHand = new Resources(0, 0, 0, 40) };
            manualAlchemy.Construct(wallet); // banks 40 of 100: remaining 60
            queue.Add(new ProductionOrder(2, manualAlchemy, false));
            queue.Add(new ProductionOrder(1000, new AlchemyProductionUnit(race), true));

            queue.Add(new ProductionOrder(4, new TerraformProductionUnit(race, true) { Reach = new TerraformReach(3, 5, 7) }, true));
            queue.Add(new ProductionOrder(6, new TerraformProductionUnit(race, false), true));
            queue.Add(new ProductionOrder(1, new TerraformProductionUnit(race), false));
            queue.Add(new ProductionOrder(2, new ShipProductionUnit(ship), false));
            queue.Add(new ProductionOrder(1, new ShipProductionUnit(starbase), false));
            queue.Add(new ProductionOrder(7, new PacketProductionUnit(race, PacketMineral.Boranium, false), false));
            queue.Add(new ProductionOrder(9, new PacketProductionUnit(race, PacketMineral.Mixed, true), true));

            GenesisDeviceProductionUnit genesis = new GenesisDeviceProductionUnit();
            Star genesisWallet = new Star { ResourcesOnHand = new Resources(0, 0, 0, 1200) };
            genesis.Construct(genesisWallet); // 3,800 of 5,000 still to pay
            queue.Add(new ProductionOrder(1, genesis, false));
        }

        /// <summary>A waypoint of every task type (both CargoTask forms, both SplitMergeTask forms).</summary>
        private static List<Waypoint> EveryTaskWaypoint(ShipDesign design, long pursuedFleetKey)
        {
            List<IWaypointTask> tasks = new List<IWaypointTask>();
            tasks.Add(new NoTask());

            CargoTask legacyLoad = new CargoTask { Mode = CargoMode.Load };
            legacyLoad.Amount.Ironium = 10;
            legacyLoad.Amount.Boranium = 20;
            legacyLoad.Amount.Germanium = 30;
            legacyLoad.Amount.ColonistsInKilotons = 40;
            tasks.Add(legacyLoad);

            tasks.Add(new CargoTask(
                new CargoInstruction(CargoAction.LoadAll),
                new CargoInstruction(CargoAction.UnloadExactly, 25),
                new CargoInstruction(CargoAction.FillToPercent, 60),
                new CargoInstruction(CargoAction.WaitForPercent, 80),
                new CargoInstruction(CargoAction.LoadExactly, 4095)));

            tasks.Add(new ColoniseTask());
            tasks.Add(new InvadeTask());
            tasks.Add(new LayMinesTask { Duration = 3 });
            tasks.Add(new ScrapTask());
            tasks.Add(new SplitMergeTask(new Dictionary<long, ShipToken>(), new Dictionary<long, ShipToken>(), 0x200000007L));

            ShipToken left = new ShipToken(design, 2) { Armor = 75 };
            ShipToken right = new ShipToken(design, 3);
            tasks.Add(new SplitMergeTask(
                new Dictionary<long, ShipToken> { { left.Key, left } },
                new Dictionary<long, ShipToken> { { right.Key, right } }));

            tasks.Add(new PatrolTask { Speed = 7, RangeIndex = 4 });
            tasks.Add(new TransferFleetTask(3));

            List<Waypoint> waypoints = new List<Waypoint>();
            int i = 0;
            foreach (IWaypointTask task in tasks)
            {
                Waypoint waypoint = new Waypoint
                {
                    Position = new NovaPoint(100 + i, 200 + i),
                    Destination = "Point " + i,
                    WarpFactor = i % 12,
                    TargetKind = i % 2 == 0 ? WaypointTargetKind.Planet : WaypointTargetKind.DeepSpace,
                    Task = task,
                };
                waypoints.Add(waypoint);
                i++;
            }

            // A pursuit: target kind, key and the jump freeze mark.
            Waypoint pursuit = new Waypoint
            {
                Position = new NovaPoint(300, 301),
                Destination = "Chased",
                WarpFactor = 9,
                TargetKind = WaypointTargetKind.Fleet,
                TargetFleetKey = pursuedFleetKey,
                PursuitFrozen = true,
                Task = new NoTask(),
            };
            waypoints.Add(pursuit);
            return waypoints;
        }

        /// <summary>A fleet with damage, cargo, Repeat Orders, a non-default battle plan and a
        /// waypoint of every task type.</summary>
        private static Fleet MakeFleet(EmpireData owner, ShipDesign design, Star orbit, long pursuedFleetKey)
        {
            ShipToken token = new ShipToken(design, 4);
            token.Armor = 150;
            token.PackedDamage = 0x1A32;
            Fleet fleet = new Fleet(token, orbit, owner.GetNextFleetKey());
            fleet.Name = "Everything Fleet";
            fleet.Type = ItemType.Fleet;
            fleet.Bearing = 12.25;
            fleet.Cloaked = 35.5;
            fleet.FuelAvailable = 123.75;
            fleet.TargetDistance = 3.5;
            fleet.BattlePlan = "Hunter";
            fleet.RepeatOrders = true;
            fleet.Cargo.Ironium = 5;
            fleet.Cargo.Boranium = 6;
            fleet.Cargo.Germanium = 7;
            fleet.Cargo.ColonistsInKilotons = 8;
            fleet.Waypoints.AddRange(EveryTaskWaypoint(design, pursuedFleetKey));
            owner.AddOrUpdateFleet(fleet);
            return fleet;
        }

        // ------------------------------------------------------------------ individual objects

        [Test]
        public void Waypoint_OfEveryTaskType_RoundTripsIdentically()
        {
            EmpireData empire = MakeEmpire(1, MakeRace("Wayfarer", "JOAT"));
            ShipDesign design = MakeDesign(empire, "Scout", 100, 0);

            foreach (Waypoint waypoint in EveryTaskWaypoint(design, 0x200000003L))
            {
                Waypoint loaded = RoundTrip(waypoint.ToXml, node => new Waypoint(node), (w, doc) => w.ToXml(doc));

                Assert.AreEqual(waypoint.Task.GetType(), loaded.Task.GetType(), waypoint.Destination);
                Assert.AreEqual(waypoint.Destination, loaded.Destination);
                Assert.AreEqual(waypoint.WarpFactor, loaded.WarpFactor);
                Assert.AreEqual(waypoint.TargetKind, loaded.TargetKind);
                Assert.AreEqual(waypoint.Position, loaded.Position);
            }
        }

        [Test]
        public void Waypoint_TaskFields_SurviveTheRoundTrip()
        {
            EmpireData empire = MakeEmpire(1, MakeRace("Wayfarer", "JOAT"));
            ShipDesign design = MakeDesign(empire, "Scout", 100, 0);
            List<Waypoint> loaded = EveryTaskWaypoint(design, 0x200000003L)
                .Select(w => RoundTrip(w.ToXml, node => new Waypoint(node), (x, doc) => x.ToXml(doc)))
                .ToList();

            CargoTask legacy = (CargoTask)loaded[1].Task;
            Assert.IsNull(legacy.Instructions);
            Assert.AreEqual(CargoMode.Load, legacy.Mode);
            Assert.AreEqual(40, legacy.Amount.ColonistsInKilotons);

            CargoTask transport = (CargoTask)loaded[2].Task;
            Assert.AreEqual(CargoAction.LoadAll, transport.Instructions[(int)CargoSlot.Ironium].Action);
            Assert.AreEqual(CargoAction.UnloadExactly, transport.Instructions[(int)CargoSlot.Boranium].Action);
            Assert.AreEqual(25, transport.Instructions[(int)CargoSlot.Boranium].Amount);
            Assert.AreEqual(CargoAction.WaitForPercent, transport.Instructions[(int)CargoSlot.Colonists].Action);
            Assert.AreEqual(80, transport.Instructions[(int)CargoSlot.Colonists].Amount);
            Assert.AreEqual(4095, transport.Instructions[(int)CargoSlot.Fuel].Amount);
            Assert.IsFalse(transport.Pending, "Pending is recomputed by Perform, never saved");

            Assert.AreEqual(3, ((LayMinesTask)loaded[5].Task).Duration);
            Assert.AreEqual(0x200000007L, ((SplitMergeTask)loaded[7].Task).OtherFleetKey);

            SplitMergeTask split = (SplitMergeTask)loaded[8].Task;
            Assert.AreEqual(2, split.LeftComposition[design.Key].Quantity);
            Assert.AreEqual(75, split.LeftComposition[design.Key].Armor);
            Assert.AreEqual(3, split.RightComposition[design.Key].Quantity);

            PatrolTask patrol = (PatrolTask)loaded[9].Task;
            Assert.AreEqual(7, patrol.Speed);
            Assert.AreEqual(4, patrol.RangeIndex);
            Assert.AreEqual(3, ((TransferFleetTask)loaded[10].Task).RecipientId);

            Waypoint pursuit = loaded[11];
            Assert.AreEqual(WaypointTargetKind.Fleet, pursuit.TargetKind);
            Assert.AreEqual(0x200000003L, pursuit.TargetFleetKey);
            Assert.IsTrue(pursuit.PursuitFrozen);
            Assert.IsTrue(pursuit.IsFleetTarget);
        }

        [Test]
        public void ShipToken_PackedDamage_RoundTripsIdentically()
        {
            EmpireData empire = MakeEmpire(1, MakeRace("Wayfarer", "JOAT"));
            ShipToken token = new ShipToken(MakeDesign(empire, "Hulk", 100, 0), 3) { Armor = 92.5, PackedDamage = 0xFFFF };

            ShipToken loaded = RoundTrip(token.ToXml, node => new ShipToken(node), (t, doc) => t.ToXml(doc));

            Assert.AreEqual(0xFFFF, loaded.PackedDamage);
            Assert.AreEqual(92.5, loaded.Armor);
            Assert.AreEqual(3, loaded.Quantity);
            Assert.AreEqual(token.Key, loaded.Key);
        }

        [Test]
        public void Star_WithAProductionOrderOfEveryUnitType_RoundTripsIdentically()
        {
            EmpireData empire = MakeEmpire(1, MakeRace("Builder", "PP"));
            ShipDesign ship = MakeDesign(empire, "Freighter", 200, 100);
            ShipDesign starbase = MakeDesign(empire, "Fort", 0, 0);
            Star star = MakeOwnedStar(empire, "Forge", 10, 20);
            star.Starbase = new Fleet(0x100000009L);
            FillQueue(star, empire, ship, starbase);

            Star loaded = RoundTrip(star.ToXml, node => new Star(node), (s, doc) => s.ToXml(doc));

            Assert.AreEqual(star.Key, loaded.Key);
            Assert.AreEqual(123400, loaded.Colonists);
            Assert.AreEqual(37, loaded.PopulationCarry);
            Assert.IsTrue(loaded.IsHomeWorld);
            Assert.IsTrue(loaded.OnlyLeftover);
            Assert.AreEqual("Far Away", loaded.PacketDestination);
            Assert.AreEqual(8, loaded.PacketWarp);
            Assert.AreEqual(new Resources(3, 4, 5, 0), loaded.MineralMiningProgress);
            Assert.AreEqual(0x100000009L, loaded.Starbase.Key);
            Assert.AreEqual("Builder", loaded.ThisRace.Name);

            List<ProductionOrder> queue = loaded.ManufacturingQueue.Queue;
            Assert.AreEqual(star.ManufacturingQueue.Queue.Count, queue.Count);
            Assert.IsInstanceOf<FactoryProductionUnit>(queue[0].Unit);
            Assert.IsTrue(queue[0].IsAutoBuild);
            Assert.AreEqual(10, queue[0].Quantity);
            Assert.IsInstanceOf<MineProductionUnit>(queue[1].Unit);
            Assert.IsInstanceOf<DefenseProductionUnit>(queue[2].Unit);
            Assert.IsInstanceOf<AlchemyProductionUnit>(queue[3].Unit);
            Assert.IsFalse(queue[3].IsAutoBuild, "manual alchemy");
            Assert.AreEqual(60, queue[3].Unit.RemainingCost.Energy, "the part-paid unit keeps its progress");
            Assert.IsTrue(queue[4].IsAutoBuild, "auto alchemy");
            Assert.IsTrue(((TerraformProductionUnit)queue[5].Unit).MinimumOnly);
            Assert.AreEqual(7, ((TerraformProductionUnit)queue[5].Unit).Reach.Radiation);
            Assert.IsFalse(((TerraformProductionUnit)queue[6].Unit).MinimumOnly);
            Assert.IsNull(((TerraformProductionUnit)queue[7].Unit).Reach);
            Assert.AreEqual("Freighter", queue[8].Unit.Name);
            Assert.AreEqual("Fort", queue[9].Unit.Name);
            Assert.AreEqual(PacketMineral.Boranium, ((PacketProductionUnit)queue[10].Unit).Mineral);
            Assert.IsTrue(((PacketProductionUnit)queue[11].Unit).AutoBuild);
            Assert.IsInstanceOf<GenesisDeviceProductionUnit>(queue[12].Unit);
            Assert.AreEqual(3800, queue[12].Unit.RemainingCost.Energy);
        }

        [Test]
        public void ProductionOrder_OfEveryUnitType_RoundTripsIdentically()
        {
            EmpireData empire = MakeEmpire(1, MakeRace("Builder", "PP"));
            ShipDesign ship = MakeDesign(empire, "Freighter", 200, 100);
            ShipDesign starbase = MakeDesign(empire, "Fort", 0, 0);
            Star star = MakeOwnedStar(empire, "Forge", 10, 20);
            FillQueue(star, empire, ship, starbase);

            foreach (ProductionOrder order in star.ManufacturingQueue.Queue)
            {
                ProductionOrder loaded = RoundTrip(order.ToXml, node => new ProductionOrder(node), (o, doc) => o.ToXml(doc));
                Assert.AreEqual(order.Unit.GetType(), loaded.Unit.GetType());
                Assert.AreEqual(order.Quantity, loaded.Quantity);
                Assert.AreEqual(order.IsAutoBuild, loaded.IsAutoBuild);
                Assert.AreEqual(order.Unit.Cost, loaded.Unit.Cost, order.Unit.Name);
                Assert.AreEqual(order.Unit.RemainingCost, loaded.Unit.RemainingCost, order.Unit.Name);
            }
        }

        [Test]
        public void Minefield_OfEveryType_WithDetonateAndVisibleTo_RoundTripsIdentically()
        {
            EmpireData empire = MakeEmpire(2, MakeRace("Miner", "SD"));
            foreach (MinefieldType type in new[] { MinefieldType.Standard, MinefieldType.Heavy, MinefieldType.SpeedBump })
            {
                Minefield field = new Minefield
                {
                    Name = type + " field",
                    Position = new NovaPoint(400, 500),
                    NumberOfMines = 2500,
                    SafeSpeed = Minefield.SafeWarpByType[(int)type],
                    FieldType = type,
                    Detonate = type == MinefieldType.Standard,
                };
                field.Key = empire.GetNextMinefieldKey();
                field.VisibleTo.Add(5);
                field.VisibleTo.Add(1);

                Minefield loaded = RoundTrip(field.ToXml, node => new Minefield(node), (m, doc) => m.ToXml(doc));

                Assert.AreEqual(field.Key, loaded.Key);
                Assert.AreEqual(2, loaded.Owner);
                Assert.AreEqual(type, loaded.FieldType);
                Assert.AreEqual(2500, loaded.NumberOfMines);
                Assert.AreEqual(field.SafeSpeed, loaded.SafeSpeed);
                Assert.AreEqual(field.Detonate, loaded.Detonate);
                CollectionAssert.AreEquivalent(new[] { 1, 5 }, loaded.VisibleTo);
            }
        }

        [Test]
        public void MineralPacket_RoundTripsIdentically()
        {
            MineralPacket packet = new MineralPacket
            {
                Key = ((long)0).SetOwner(3).SetId(2),
                Name = "Packet",
                Position = new NovaPoint(12, 34),
                Minerals = new Resources(1, 2, 3, 0),
                Warp = 9,
                OverspeedClass = 2,
                OriginName = "A",
                TargetName = "B",
                Destination = new NovaPoint(56, 78),
                HasMoved = true,
            };

            MineralPacket loaded = RoundTrip(packet.ToXml, node => new MineralPacket(node), (p, doc) => p.ToXml(doc));

            Assert.AreEqual(packet.Key, loaded.Key);
            Assert.AreEqual(2, loaded.OverspeedClass);
            Assert.IsTrue(loaded.HasMoved);
        }

        [Test]
        public void Wormhole_RoundTripsIdentically()
        {
            Wormhole wormhole = new Wormhole { Name = "Wormhole A", Position = new NovaPoint(7, 8), PairedKey = 0x51, StabilityTier = 4 };
            wormhole.Key = 0x50;
            wormhole.UsedBy.Add(3);
            wormhole.UsedBy.Add(1);

            Wormhole loaded = RoundTrip(wormhole.ToXml, node => new Wormhole(node), (w, doc) => w.ToXml(doc));

            Assert.AreEqual(0x50, loaded.Key);
            Assert.AreEqual(0x51, loaded.PairedKey);
            Assert.AreEqual(4, loaded.StabilityTier);
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, loaded.UsedBy);
        }

        [Test]
        public void MysteryTrader_RoundTripsIdentically()
        {
            MysteryTrader trader = new MysteryTrader { Position = new NovaPoint(123, 45), Destination = new NovaPoint(380, 77), Speed = 11, Item = 7 };
            trader.Key = 3;
            trader.ServedRaces.Add(2);
            trader.ServedRaces.Add(1);

            MysteryTrader loaded = RoundTrip(trader.ToXml, node => new MysteryTrader(node), (t, doc) => t.ToXml(doc));

            Assert.AreEqual(new NovaPoint(380, 77), loaded.Destination);
            Assert.AreEqual(11, loaded.Speed);
            Assert.AreEqual(7, loaded.Item);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, loaded.ServedRaces);
        }

        [Test]
        public void DeepSpaceMinerals_WithDecayGrace_RoundTripIdentically()
        {
            foreach (bool grace in new[] { true, false })
            {
                DeepSpaceMinerals wreck = new DeepSpaceMinerals(new NovaPoint(9, 10)) { Minerals = new Resources(100, 200, 300, 0), DecayGrace = grace };

                DeepSpaceMinerals loaded = RoundTrip(wreck.ToXml, node => new DeepSpaceMinerals(node), (w, doc) => w.ToXml(doc));

                Assert.AreEqual(grace, loaded.DecayGrace);
                Assert.AreEqual(new Resources(100, 200, 300, 0), loaded.Minerals);
                Assert.AreEqual(new NovaPoint(9, 10), loaded.Position);
            }
        }

        [Test]
        public void ProductionTemplates_RoundTripIdentically()
        {
            ProductionTemplateSet set = new ProductionTemplateSet();
            ProductionTemplate first = new ProductionTemplate { Name = "Growth", OnlyLeftover = true };
            first.TryAdd(new ProductionTemplateEntry(TemplateItemType.Factories, 50));
            first.TryAdd(new ProductionTemplateEntry(TemplateItemType.MinTerraform, 4));
            first.TryAdd(new ProductionTemplateEntry(TemplateItemType.MineralPackets, 1023));
            ProductionTemplate third = new ProductionTemplate { Name = "Defence" };
            third.TryAdd(new ProductionTemplateEntry(TemplateItemType.Defenses, 10));
            third.TryAdd(new ProductionTemplateEntry(TemplateItemType.Alchemy, 1));
            set.SetSlot(0, first);
            set.SetSlot(2, third);
            set.DefaultSlot = 2;

            ProductionTemplateSet loaded = RoundTrip(set.ToXml, ProductionTemplateSet.FromXml, (s, doc) => s.ToXml(doc));

            Assert.AreEqual(2, loaded.DefaultSlot);
            Assert.AreEqual("Growth", loaded[0].Name);
            Assert.IsTrue(loaded[0].OnlyLeftover);
            Assert.AreEqual(3, loaded[0].Entries.Count);
            Assert.AreEqual(1023, loaded[0].Entries[2].Quantity);
            Assert.IsTrue(loaded[1].IsEmpty);
            Assert.AreEqual(TemplateItemType.Alchemy, loaded[2].Entries[1].Type);

            set.DefaultSlot = ProductionTemplateSet.NoDefault;
            Assert.AreEqual(ProductionTemplateSet.NoDefault, RoundTrip(set.ToXml, ProductionTemplateSet.FromXml, (s, doc) => s.ToXml(doc)).DefaultSlot);
        }

        [Test]
        public void ScoreHistory_RoundTripsIdentically()
        {
            ScoreHistory history = new ScoreHistory();
            history.Record(2101, new[]
            {
                new ScoreRecord { EmpireId = 1, Rank = 2, Score = 300, Planets = 4, Starbases = 1, UnarmedShips = 5, EscortShips = 6, CapitalShips = 7, TechLevel = 30, Resources = 900 },
                new ScoreRecord { EmpireId = 11, Rank = 1, Score = 400 },
            });
            history.Record(2102, new ScoreRecord[0]);

            ScoreHistory loaded = RoundTrip(history.ToXml, node => new ScoreHistory(node), (h, doc) => h.ToXml(doc));

            CollectionAssert.AreEqual(new[] { 2101, 2102 }, loaded.Years);
            Assert.AreEqual(11, loaded.For(2101)[1].EmpireId);
            Assert.AreEqual(900, loaded.For(2101)[0].Resources);
            Assert.AreEqual(0, loaded.For(2102).Count);
        }

        [Test]
        public void BattlePlan_RoundTripsIdentically()
        {
            BattlePlan plan = new BattlePlan
            {
                Name = "Hunter",
                PrimaryTarget = "Starbase",
                SecondaryTarget = "Bombers/Freighters",
                Tactic = "Disengage if Challenged",
                Attack = "Specific Player",
                TargetId = 3,
                DumpCargo = true,
            };

            BattlePlan loaded = RoundTrip(plan.ToXml, node => new BattlePlan(node), (p, doc) => p.ToXml(doc));

            Assert.AreEqual("Hunter", loaded.Name);
            Assert.AreEqual("Specific Player", loaded.Attack);
            Assert.AreEqual(3, loaded.TargetId);
            Assert.IsTrue(loaded.DumpCargo);
        }

        /// <summary>A message whose Event is a minefield or battle report is saved with that
        /// object's key; once loaded the event is that key as text. Saving the loaded message
        /// again (the client re-saves every message it read from its turn file, and the server
        /// re-saves AllMessages) must write the same key back.</summary>
        [Test]
        public void Message_WithAMinefieldOrBattleEvent_RoundTripsIdentically()
        {
            Minefield field = new Minefield { Position = new NovaPoint(1, 1) };
            field.Key = 0x20000002AL;
            Message minefieldMessage = new Message(2, "Your fleet hit a minefield", "Minefield", field);

            Message loaded = RoundTrip(minefieldMessage.ToXml, node => new Message(node), (m, doc) => m.ToXml(doc));
            Assert.AreEqual(field.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), loaded.Event);
            Assert.AreEqual("Minefield", loaded.Type);
            Assert.AreEqual(2, loaded.Audience);

            BattleReport battle = new BattleReport { Location = "Somewhere", Year = 2105 };
            Message battleMessage = new Message(1, "A battle took place", "BattleReport", battle);
            Message loadedBattle = RoundTrip(battleMessage.ToXml, node => new Message(node), (m, doc) => m.ToXml(doc));
            Assert.AreEqual(battle.Key, loadedBattle.Event);
        }

        // ------------------------------------------------------------------ EmpireData

        private static EmpireData MakePopulatedEmpire(out EmpireData other, out Fleet fleet, out Star home)
        {
            EmpireData empire = MakeEmpire(1, MakeRace("Hero", "SD"));
            other = MakeEmpire(2, MakeRace("Villain", "WM"));

            empire.TurnSubmitted = true;
            empire.LastTurnSubmitted = 2104;
            empire.Eliminated = true;
            empire.ResearchBudget = 23;
            empire.ResearchLevels = new TechLevel(1, 2, 3, 4, 5, 6);
            empire.ResearchResources = new TechLevel(10, 20, 30, 40, 50, 60);
            empire.ResearchTopics = new TechLevel(0, 1, 0, 0, 1, 0);
            empire.ResearchNextField = Research.NextFieldLowest;
            empire.GrantedSpecialComponents.Add("Mini Morph");
            empire.GrantedSpecialComponents.Add("Jump Gate");

            ShipDesign ship = MakeDesign(empire, "Freighter", 200, 100);
            ShipDesign starbase = MakeDesign(empire, "Fort", 0, 0);
            home = MakeOwnedStar(empire, "Home", 10, 20);
            FillQueue(home, empire, ship, starbase);

            Fleet station = new Fleet(starbase, 1, home, empire.GetNextFleetKey()) { Name = "Home Starbase" };
            empire.AddOrUpdateFleet(station);
            home.Starbase = station;

            Star foreign = MakeStar("Foreign", 60, 70);
            foreign.Owner = other.Id;
            foreign.Colonists = 5000;
            empire.StarReports.Add(home.Name, home.GenerateReport(ScanLevel.Owned, 2105));
            empire.StarReports.Add(foreign.Name, foreign.GenerateReport(ScanLevel.InDeepScan, 2104));

            fleet = MakeFleet(empire, ship, home, 0x200000001L);

            Wormhole wormhole = new Wormhole { Name = "Wormhole", Position = new NovaPoint(7, 8), PairedKey = 0x61, StabilityTier = 2 };
            wormhole.Key = 0x60;
            empire.WormholeReports.Add(wormhole.Key, new WormholeIntel(wormhole, 2103));

            MineralPacket packet = new MineralPacket
            {
                Key = ((long)0).SetOwner(2).SetId(1),
                Name = "Packet",
                Position = new NovaPoint(12, 34),
                Minerals = new Resources(1, 2, 3, 0),
                Warp = 9,
                OriginName = "Foreign",
                TargetName = "Home",
                Destination = new NovaPoint(10, 20),
            };
            empire.MineralPacketReports.Add(packet.Key, packet);

            empire.VisibleMinefields.Add(0x200000003L);
            empire.VisibleMinefields.Add(empire.GetNextMinefieldKey());

            empire.EmpireReports.Add(other.Id, new EmpireIntel(other) { Relation = PlayerRelation.Enemy });
            empire.BattlePlans.Add("Hunter", new BattlePlan { Name = "Hunter", PrimaryTarget = "Starbase", Attack = "Specific Player", TargetId = 2, DumpCargo = true });

            ProductionTemplate template = new ProductionTemplate { Name = "Basic" };
            template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Mines, 20));
            empire.ProductionTemplates.SetSlot(1, template);
            empire.ProductionTemplates.DefaultSlot = 1;

            return empire;
        }

        [Test]
        public void EmpireData_FullyPopulated_RoundTripsIdentically()
        {
            EmpireData empire = MakePopulatedEmpire(out EmpireData other, out Fleet fleet, out Star home);

            EmpireData loaded = RoundTrip(empire.ToXml, node => new EmpireData(node), (e, doc) => e.ToXml(doc));

            Assert.AreEqual(1, loaded.Id);
            Assert.AreEqual(2105, loaded.TurnYear);
            Assert.IsTrue(loaded.TurnSubmitted);
            Assert.AreEqual(2104, loaded.LastTurnSubmitted);
            Assert.IsTrue(loaded.Eliminated);
            Assert.AreEqual(23, loaded.ResearchBudget);
            Assert.AreEqual(Research.NextFieldLowest, loaded.ResearchNextField);
            Assert.AreEqual(6, loaded.ResearchLevels[TechLevel.ResearchField.Construction]);
            CollectionAssert.AreEquivalent(new[] { "Mini Morph", "Jump Gate" }, loaded.GrantedSpecialComponents);
            Assert.AreEqual(2, loaded.Designs.Count);
            Assert.AreEqual(2, loaded.StarReports.Count);
            Assert.AreEqual(1, loaded.OwnedStars.Count);
            Assert.AreEqual(2, loaded.OwnedFleets.Count);
            Assert.AreEqual(1, loaded.WormholeReports.Count);
            Assert.AreEqual(1, loaded.MineralPacketReports.Count);
            Assert.AreEqual(2, loaded.VisibleMinefields.Count);
            Assert.AreEqual(PlayerRelation.Enemy, loaded.EmpireReports[2].Relation);
            Assert.IsTrue(loaded.BattlePlans.ContainsKey("Default"));
            Assert.AreEqual(2, loaded.BattlePlans["Hunter"].TargetId);
            Assert.AreEqual(1, loaded.ProductionTemplates.DefaultSlot);
            Assert.AreEqual("Basic", loaded.ProductionTemplates[1].Name);

            Star loadedHome = loaded.OwnedStars[home.Name];
            Assert.AreSame(loaded.OwnedFleets[home.Starbase.Key], loadedHome.Starbase, "LinkReferences resolves the starbase");
            Assert.AreEqual(home.ManufacturingQueue.Queue.Count, loadedHome.ManufacturingQueue.Queue.Count);

            Fleet loadedFleet = loaded.OwnedFleets[fleet.Key];
            Assert.IsTrue(loadedFleet.RepeatOrders);
            Assert.AreEqual("Hunter", loadedFleet.BattlePlan);
            Assert.AreEqual(35.5, loadedFleet.Cloaked);
            Assert.AreEqual(123.75, loadedFleet.FuelAvailable);
            Assert.AreEqual(fleet.Waypoints.Count, loadedFleet.Waypoints.Count);
            Assert.AreEqual(8, loadedFleet.Cargo.ColonistsInKilotons);
            ShipToken token = loadedFleet.Composition.Values.Single();
            Assert.AreEqual(0x1A32, token.PackedDamage);
            Assert.AreEqual(150, token.Armor);
            Assert.AreSame(loaded.Designs[token.Key], token.Design, "LinkReferences resolves the design");
            Assert.AreSame(loadedHome, loadedFleet.InOrbit, "LinkReferences resolves the orbit");
            Assert.AreEqual(0x200000001L, loadedFleet.Waypoints.Last().TargetFleetKey);
        }

        // ------------------------------------------------------------------ ServerData

        [Test]
        public void ServerData_WholeStateFile_RoundTripsIdentically_ThroughSaveAndRestore()
        {
            EmpireData hero = MakePopulatedEmpire(out EmpireData villain, out Fleet fleet, out Star home);
            hero.EmpireReports[villain.Id].Relation = PlayerRelation.Neutral;
            villain.EmpireReports.Add(hero.Id, new EmpireIntel(hero) { Relation = PlayerRelation.Friend });

            ServerData server = new ServerData();
            server.GameInProgress = true;
            server.TurnYear = 2105;
            server.AllPlayers.Add(new PlayerSettings { RaceName = "Hero", AiProgram = "Human", PlayerNumber = 1 });
            server.AllPlayers.Add(new PlayerSettings { RaceName = "Villain", AiProgram = "Default AI", PlayerNumber = 2, AiCategory = 3 });
            server.AllRaces.Add(hero.Race.Name, hero.Race);
            server.AllRaces.Add(villain.Race.Name, villain.Race);
            server.AllEmpires.Add(hero.Id, hero);
            server.AllEmpires.Add(villain.Id, villain);
            server.AllTechLevels.Add(1, 21);
            server.AllTechLevels.Add(2, 9);

            server.AllStars.Add(home.Name, home);
            Star wild = MakeStar("Wild", 300, 300);
            server.AllStars.Add(wild.Name, wild);

            foreach (MinefieldType type in new[] { MinefieldType.Standard, MinefieldType.Heavy, MinefieldType.SpeedBump })
            {
                Minefield field = new Minefield { Name = type + " field", Position = new NovaPoint(100, 100), NumberOfMines = 900, SafeSpeed = 5, FieldType = type, Detonate = type == MinefieldType.Standard };
                field.Key = hero.GetNextMinefieldKey();
                field.VisibleTo.Add(2);
                server.AllMinefields.Add(field.Key, field);
            }

            Wormhole a = new Wormhole { Name = "A", Position = new NovaPoint(1, 2), PairedKey = 0x71, StabilityTier = 3 };
            a.Key = 0x70;
            a.UsedBy.Add(1);
            Wormhole b = new Wormhole { Name = "B", Position = new NovaPoint(3, 4), PairedKey = 0x70, StabilityTier = 3 };
            b.Key = 0x71;
            server.AllWormholes.Add(a.Key, a);
            server.AllWormholes.Add(b.Key, b);

            MysteryTrader trader = new MysteryTrader { Position = new NovaPoint(5, 6), Destination = new NovaPoint(380, 6), Speed = 10, Item = MysteryTrader.ShipsItem };
            trader.Key = 1;
            trader.ServedRaces.Add(2);
            server.AllMysteryTraders.Add(trader.Key, trader);
            server.MysteryTraderGiftFleets.Add(0x200000004L);

            MineralPacket packet = new MineralPacket
            {
                Key = ((long)0).SetOwner(1).SetId(1),
                Name = "Packet",
                Position = new NovaPoint(50, 60),
                Minerals = new Resources(10, 0, 5, 0),
                Warp = 8,
                OverspeedClass = 1,
                OriginName = "Home",
                TargetName = "Wild",
                Destination = new NovaPoint(300, 300),
            };
            server.AllMineralPackets.Add(packet.Key, packet);

            server.AllMessages.Add(new Message(1, "Plain news", "TechAdvance", null));
            server.AllMessages.Add(new Message(0, "To everyone", null, null));

            DeepSpaceMinerals first = new DeepSpaceMinerals(new NovaPoint(77, 88)) { Minerals = new Resources(30000, 0, 0, 0) };
            DeepSpaceMinerals second = new DeepSpaceMinerals(new NovaPoint(77, 88)) { Minerals = new Resources(5, 6, 7, 0), DecayGrace = true };
            server.AllDeepSpaceMinerals.Add(first.Position.ToHashString(), first);
            server.AllDeepSpaceMinerals.Add(first.Position.ToHashString() + "#1", second);

            server.ScoreHistory.Record(2104, new[] { new ScoreRecord { EmpireId = 1, Rank = 1, Score = 100 }, new ScoreRecord { EmpireId = 2, Rank = 2, Score = 50 } });
            server.ScoreHistory.Record(2105, new[] { new ScoreRecord { EmpireId = 2, Rank = 1, Score = 150 } });

            string folder = Path.Combine(Path.GetTempPath(), "NovaSaveFormat_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                server.GameFolder = folder;
                server.StatePathName = Path.Combine(folder, "game.sstate");
                server.Save();
                string firstText = File.ReadAllText(server.StatePathName);

                ServerData restored = new ServerData { StatePathName = server.StatePathName };
                restored.Restore();
                restored.Save();
                string secondText = File.ReadAllText(restored.StatePathName);

                AssertSameText(firstText, secondText);

                Assert.IsTrue(restored.GameInProgress);
                Assert.AreEqual(2105, restored.TurnYear);
                Assert.AreEqual(folder, restored.GameFolder);
                Assert.AreEqual(2, restored.AllPlayers.Count);
                Assert.AreEqual(3, restored.AllPlayers[1].AiCategory);
                Assert.AreEqual(21, restored.AllTechLevels[1]);
                Assert.AreEqual(2, restored.AllRaces.Count);
                Assert.AreEqual(2, restored.AllEmpires.Count);
                Assert.AreEqual(2, restored.AllStars.Count);
                Assert.AreEqual(3, restored.AllMinefields.Count);
                Assert.AreEqual(2, restored.AllWormholes.Count);
                Assert.AreEqual(MysteryTrader.ShipsItem, restored.AllMysteryTraders[1].Item);
                CollectionAssert.AreEquivalent(new[] { 0x200000004L }, restored.MysteryTraderGiftFleets);
                Assert.AreEqual(5, restored.AllMineralPackets[packet.Key].Minerals.Germanium);
                Assert.AreEqual(2, restored.AllMessages.Count);
                Assert.AreEqual(2, restored.AllDeepSpaceMinerals.Count);
                Assert.IsTrue(restored.AllDeepSpaceMinerals[first.Position.ToHashString() + "#1"].DecayGrace);
                CollectionAssert.AreEqual(new[] { 2104, 2105 }, restored.ScoreHistory.Years);

                // LinkServerStateReferences: the server's own star and the empire's fleet are linked up.
                Star restoredHome = restored.AllStars[home.Name];
                Assert.AreSame(restored.AllEmpires[1].OwnedFleets[home.Starbase.Key], restoredHome.Starbase);
                Assert.AreSame(restored.AllRaces["Hero"], restoredHome.ThisRace);
                Assert.AreSame(restoredHome, restored.AllEmpires[1].OwnedFleets[fleet.Key].InOrbit);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
