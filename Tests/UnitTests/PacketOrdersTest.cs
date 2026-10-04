namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// The client side of mineral packets (Nova.Client.PacketOrders; production-queue.md §10
    /// types 6 and 14-17, §10b; fleet-movement-scanning-cargo.md "With a planet selected,
    /// Shift+left-click sets the planet's packet destination") and the routing of the packet
    /// notices (Nova.Client.MessageRouting).
    /// </summary>
    [TestFixture]
    public class PacketOrdersTest
    {
        private static long nextKey = 9000;

        private static Race NewRace(string primary)
        {
            Race race = new Race();
            race.Traits.SetPrimary(primary);
            return race;
        }

        private static Fleet MakeStarbase(ushort owner, params int[] driverWarps)
        {
            Fleet starbase = new Fleet(nextKey++);
            starbase.Owner = owner;
            starbase.Type = ItemType.Starbase;

            ShipDesign design = new ShipDesign(nextKey++);
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            foreach (int warp in driverWarps)
            {
                Component driver = new Component { Name = "Mass Driver " + warp };
                driver.Properties.Add("Mass Driver", new MassDriver(warp));
                hull.Modules.Add(new HullModule { AllocatedComponent = driver, ComponentCount = 1 });
            }

            design.Blueprint.Properties.Add("Hull", hull);
            ShipToken token = new ShipToken(design, 1);
            starbase.Composition.Add(token.Key, token);
            return starbase;
        }

        private static Star MakeStar(string name, int x, int y, ushort owner)
        {
            Star star = new Star();
            star.Name = name;
            star.Position = new NovaPoint(x, y);
            star.Owner = owner;
            star.ResourcesOnHand = new Resources();
            return star;
        }

        /// <summary>An empire owning Chlorine (with the given drivers) that knows of Tierra and
        /// Zebra.</summary>
        private static ClientData World(out Star origin, params int[] driverWarps)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Id = 1;
            empire.Race = NewRace("JOAT");
            empire.AvailableComponents = new RaceComponents();

            origin = MakeStar("Chlorine", 0, 0, 1);
            if (driverWarps.Length > 0)
            {
                origin.Starbase = MakeStarbase(1, driverWarps);
            }

            empire.OwnedStars.Add(origin);
            empire.StarReports.Add(origin.Name, origin.GenerateReport(ScanLevel.Owned, 2100));
            foreach (Star other in new[] { MakeStar("Zebra", 300, 0, Global.Nobody), MakeStar("Tierra", 168, 0, Global.Nobody) })
            {
                empire.StarReports.Add(other.Name, other.GenerateReport(ScanLevel.Owned, 2100));
            }

            return new ClientData { EmpireState = empire };
        }

        // ------------------------------------------------------------------ catalog

        [Test]
        public void Catalog_OffersPackets_OnlyWithAMassDriver()
        {
            World(out Star noBase);
            Assert.IsEmpty(PacketOrders.CatalogItems(noBase, NewRace("JOAT")));

            noBase.Starbase = MakeStarbase(1); // a starbase with no driver
            Assert.IsEmpty(PacketOrders.CatalogItems(noBase, NewRace("JOAT")));
        }

        [Test]
        public void Catalog_HasTheAutoEntry_ThenTheFourManualItems_AtTheSpecPrices()
        {
            World(out Star origin, 5);
            IReadOnlyList<PacketProductionUnit> items = PacketOrders.CatalogItems(origin, NewRace("JOAT"));

            CollectionAssert.AreEqual(
                new[] { "Mineral Packets", "Mixed Mineral Packet", "Ironium Mineral Packet", "Boranium Mineral Packet", "Germanium Mineral Packet" },
                items.Select(item => item.Name).ToList());
            CollectionAssert.AreEqual(new[] { true, false, false, false, false }, items.Select(item => item.AutoBuild).ToList());

            // §10: mixed 44 kT of each mineral + 10 resources; one mineral 110 kT + 10.
            Assert.AreEqual(new Resources(44, 44, 44, 10), items[0].Cost);
            Assert.AreEqual(new Resources(44, 44, 44, 10), items[1].Cost);
            Assert.AreEqual(new Resources(0, 110, 0, 10), items[3].Cost);

            // Packet Physics: 25 kT + 5.
            Assert.AreEqual(new Resources(25, 25, 25, 5), PacketOrders.CatalogItems(origin, NewRace("PP"))[0].Cost);
        }

        [Test]
        public void FreshUnit_IsANewUnitOfTheSameKind()
        {
            World(out Star origin, 5);
            PacketProductionUnit catalog = PacketOrders.CatalogItems(origin, NewRace("JOAT"))[2];
            PacketProductionUnit fresh = PacketOrders.FreshUnit(catalog, NewRace("JOAT"));

            Assert.AreNotSame(catalog, fresh);
            Assert.AreEqual(PacketMineral.Ironium, fresh.Mineral);
            Assert.IsFalse(fresh.AutoBuild);
        }

        [Test]
        public void Toggle_SwapsTheMixedPacketBetweenAutoAndManual_AndLeavesSingleMineralPacketsManual()
        {
            Race race = NewRace("JOAT");

            PacketProductionUnit toAuto = PacketOrders.ToggledUnit(new ProductionOrder(2, new PacketProductionUnit(race, PacketMineral.Mixed, false), false), race);
            Assert.IsTrue(toAuto.AutoBuild);
            Assert.AreEqual(PacketMineral.Mixed, toAuto.Mineral);

            PacketProductionUnit toManual = PacketOrders.ToggledUnit(new ProductionOrder(2, new PacketProductionUnit(race, PacketMineral.Mixed, true), true), race);
            Assert.IsFalse(toManual.AutoBuild);

            Assert.IsNull(PacketOrders.ToggledUnit(new ProductionOrder(1, new PacketProductionUnit(race, PacketMineral.Germanium, false), false), race));
            Assert.IsNull(PacketOrders.ToggledUnit(new ProductionOrder(1, new FactoryProductionUnit(race), false), race));
        }

        // ------------------------------------------------------------------ speed

        [Test]
        public void SpeedChoices_Run_From5_ToTheBestDriverWarpPlus3()
        {
            World(out Star origin, 5);
            CollectionAssert.AreEqual(new[] { 5, 6, 7, 8 }, PacketOrders.SpeedChoices(origin).ToList());

            // The best single driver's warp, not the two-slot launch rating (6 here, not 7).
            origin.Starbase = MakeStarbase(1, 6, 6);
            CollectionAssert.AreEqual(new[] { 5, 6, 7, 8, 9 }, PacketOrders.SpeedChoices(origin).ToList());

            origin.Starbase = null;
            Assert.IsEmpty(PacketOrders.SpeedChoices(origin));
        }

        [TestCase(0, 5)]   // not chosen: the launch rating
        [TestCase(7, 7)]   // chosen inside 5..8
        [TestCase(8, 8)]
        [TestCase(9, 5)]   // outside: the launch rating
        public void LaunchSpeed_IsTheChosenSpeedInRange_OtherwiseTheLaunchRating(int chosen, int expected)
        {
            World(out Star origin, 5);
            origin.PacketWarp = chosen;
            Assert.AreEqual(expected, PacketOrders.LaunchSpeed(origin));
        }

        [Test]
        public void LaunchSpeed_UsesTheTwoSlotRating_WhenNoSpeedIsChosen()
        {
            World(out Star origin, 5, 5);
            Assert.AreEqual(6, PacketOrders.LaunchSpeed(origin));
        }

        // ------------------------------------------------------------------ destination order

        [Test]
        public void DestinationChoices_AreNoneThenTheOtherKnownPlanets_ByName()
        {
            ClientData client = World(out Star origin, 5);
            CollectionAssert.AreEqual(new[] { PacketOrders.NoDestination, "Tierra", "Zebra" },
                PacketOrders.DestinationChoices(origin, client.EmpireState).ToList());
        }

        [Test]
        public void DestinationOrder_SetsOrClears()
        {
            World(out Star origin, 5);

            PacketDestinationCommand set = PacketOrders.DestinationOrder(origin, "Tierra", 7);
            Assert.AreEqual("Chlorine", set.StarKey);
            Assert.AreEqual("Tierra", set.Destination);
            Assert.AreEqual(7, set.Warp);

            Assert.IsNull(PacketOrders.DestinationOrder(origin, PacketOrders.NoDestination, 7).Destination);
            Assert.IsNull(PacketOrders.DestinationOrder(origin, "Chlorine", 7).Destination, "the planet itself clears");
            Assert.IsNull(PacketOrders.DestinationOrder(origin, "", 7).Destination);
        }

        [Test]
        public void Issue_QueuesAndApplies_AValidOrder()
        {
            ClientData client = World(out Star origin, 5);

            Assert.IsTrue(PacketOrders.Issue(client, PacketOrders.DestinationOrder(origin, "Tierra", 7)));
            Assert.AreEqual(1, client.Commands.Count);
            Assert.AreEqual("Tierra", origin.PacketDestination);
            Assert.AreEqual(7, origin.PacketWarp);
            Assert.AreEqual("Tierra", PacketOrders.DestinationText(origin));

            Assert.IsTrue(PacketOrders.Issue(client, PacketOrders.DestinationOrder(origin, "Chlorine", 0)));
            Assert.IsNull(origin.PacketDestination);
            Assert.AreEqual("None", PacketOrders.DestinationText(origin));
        }

        [Test]
        public void Issue_RefusesADestination_WithoutAMassDriver_AndQueuesNothing()
        {
            ClientData client = World(out Star origin);

            Assert.IsFalse(PacketOrders.CanSetDestination(origin));
            Assert.IsFalse(PacketOrders.Issue(client, PacketOrders.DestinationOrder(origin, "Tierra", 7)));
            Assert.AreEqual(0, client.Commands.Count);
            Assert.IsNull(origin.PacketDestination);
        }

        [Test]
        public void NearestPlanet_HasNoDistanceLimit_AndCanPickThePlanetItself()
        {
            ClientData client = World(out Star origin, 5);
            IEnumerable<StarIntel> planets = client.EmpireState.StarReports.Values;

            Assert.AreEqual("Zebra", PacketOrders.NearestPlanet(planets, 5000, 4000));
            Assert.AreEqual("Tierra", PacketOrders.NearestPlanet(planets, 150, 30));
            Assert.AreEqual("Chlorine", PacketOrders.NearestPlanet(planets, 2, 2));
            Assert.IsNull(PacketOrders.NearestPlanet(Enumerable.Empty<StarIntel>(), 0, 0));
        }

        // ------------------------------------------------------------------ inspector rows

        [Test]
        public void DescribePacket_ListsTheSighting()
        {
            MineralPacket packet = new MineralPacket
            {
                Position = new NovaPoint(68, 0),
                Minerals = new Resources(50, 40, 60, 0),
                Warp = 7,
                OriginName = "Chlorine",
                TargetName = "Tierra",
                Destination = new NovaPoint(168, 0),
            };

            Dictionary<string, string> rows = PacketOrders.DescribePacket(packet, "You").ToDictionary(row => row.Key, row => row.Value);
            Assert.AreEqual("You", rows["Owner"]);
            Assert.AreEqual("Chlorine", rows["From"]);
            Assert.AreEqual("Tierra", rows["To"]);
            Assert.AreEqual("Warp 7", rows["Speed"]);
            Assert.AreEqual("40 kT", rows["Boranium"]);
            Assert.AreEqual("150 kT", rows["Total"]);
            Assert.AreEqual("100.0 ly", rows["Distance to go"]);

            Dictionary<string, string> unknown = PacketOrders.DescribePacket(new MineralPacket(), null).ToDictionary(row => row.Key, row => row.Value);
            Assert.AreEqual("Unknown", unknown["Owner"]);
            Assert.AreEqual("Unknown", unknown["To"]);
            Assert.IsFalse(unknown.ContainsKey("Distance to go"));
        }

        // ------------------------------------------------------------------ message routing

        [Test]
        public void PacketNoticeType_MatchesTheServers()
        {
            Assert.AreEqual(PacketLaunch.MessageType, MessageRouting.MineralPacketType);
        }

        [Test]
        public void LaunchNotice_SelectsTheLaunchingPlanet_NotTheTarget()
        {
            ClientData client = World(out Star origin, 5);
            origin.PacketDestination = "Tierra";

            ServerData server = new ServerData();
            server.AllEmpires.Add(client.EmpireState.Id, client.EmpireState);
            server.AllStars.Add(origin.Name, origin);
            Star tierra = MakeStar("Tierra", 168, 0, Global.Nobody);
            server.AllStars.Add(tierra.Name, tierra);

            PacketLaunch.Launch(server, origin, new PacketProductionUnit(client.EmpireState.Race, PacketMineral.Mixed, true), 1);
            Message launch = server.AllMessages.Single();
            StringAssert.Contains("Tierra", launch.Text);

            MessageDestination destination = MessageRouting.Destination(launch, client.EmpireState.StarReports.Keys);
            Assert.AreEqual(MessageDestinationKind.Planet, destination.Kind);
            Assert.AreEqual("Chlorine", destination.PlanetName);
        }

        [Test]
        public void ArrivalNotice_SelectsTheReceivingPlanet()
        {
            var planets = new[] { "Chlorine", "Tierra" };
            Message caught = new Message(1, "The mass driver at Tierra has caught a mineral packet of 150kT.", MessageRouting.MineralPacketType, null);
            Assert.AreEqual("Tierra", MessageRouting.Destination(caught, planets).PlanetName);

            Message struck = new Message(1, "A mineral packet has struck Tierra, killing 2200 colonists.", MessageRouting.MineralPacketType, null);
            Assert.AreEqual("Tierra", MessageRouting.Destination(struck, planets).PlanetName);
        }

        [Test]
        public void PacketNotice_GoesNowhere_WithoutAKnownPlanet_AndPrefersAnEventName()
        {
            Message message = new Message(1, "A mineral packet has struck Nowhere.", MessageRouting.MineralPacketType, null);
            Assert.AreEqual(MessageDestinationKind.None, MessageRouting.Destination(message, new[] { "Tierra" }).Kind);
            Assert.AreEqual(MessageDestinationKind.None, MessageRouting.Destination(message).Kind);

            Message withEvent = new Message(1, "Tierra and Chlorine", MessageRouting.MineralPacketType, "Chlorine");
            Assert.AreEqual("Chlorine", MessageRouting.Destination(withEvent, new[] { "Tierra" }).PlanetName);
        }

        [Test]
        public void FirstNamedPlanet_MatchesWholeNames_LongestFirst()
        {
            Assert.AreEqual("Tierra Nova", MessageRouting.FirstNamedPlanet("Tierra Nova was bombarded.", new[] { "Tierra", "Tierra Nova" }));
            Assert.AreEqual("Nova", MessageRouting.FirstNamedPlanet("Supernova sent a packet to Nova.", new[] { "Nova" }));
            Assert.IsNull(MessageRouting.FirstNamedPlanet("Supernova", new[] { "Nova" }));
        }
    }
}
