namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Mineral packets (behavior-specs-10/production-queue.md §10 types 6 and 14-17, §10a, §10b,
    /// §10j test 4, §10k item 4; turn-generation-engine.md §1 steps 15, 18, 21;
    /// fleet-movement-scanning-cargo.md §3 / race-traits.md §2 Packet Physics scanner). Every
    /// number asserted here is the spec's.
    /// </summary>
    [TestFixture]
    public class MineralPacketTest
    {
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> rolls;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = new Queue<int>(rolls);
            }

            public override int Next(int maxValue)
            {
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
            }

            public override int Next(int minValue, int maxValue)
            {
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
            }
        }

        private static long nextKey = 5000;

        private static Race NewRace(string primary)
        {
            Race race = new Race();
            race.Traits.SetPrimary(primary);
            race.GrowthRate = 0;
            race.ColonistsPerResource = 1000;
            race.OperableFactories = 10;
            race.OperableMines = 10;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.MineBuildCost = 5;
            return race;
        }

        /// <summary>A starbase whose design has one slot per entry of <paramref name="driverWarps"/>,
        /// each holding <paramref name="countPerSlot"/> mass drivers of that warp.</summary>
        private static Fleet MakeStarbase(ushort owner, int countPerSlot, params int[] driverWarps)
        {
            Fleet starbase = new Fleet(nextKey++);
            starbase.Owner = owner;
            starbase.Type = ItemType.Starbase;

            ShipDesign design = MakeDriverDesign(nextKey++, countPerSlot, driverWarps);
            ShipToken token = new ShipToken(design, 1);
            starbase.Composition.Add(token.Key, token);
            return starbase;
        }

        private static ShipDesign MakeDriverDesign(long key, int countPerSlot, params int[] driverWarps)
        {
            ShipDesign design = new ShipDesign(key);
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            foreach (int warp in driverWarps)
            {
                Component driver = new Component { Name = "Mass Driver " + warp };
                driver.Properties.Add("Mass Driver", new MassDriver(warp));
                hull.Modules.Add(new HullModule { AllocatedComponent = driver, ComponentCount = countPerSlot });
            }

            design.Blueprint.Properties.Add("Hull", hull);
            return design;
        }

        private static Star MakeStar(string name, int x, int y, ushort owner, Race race)
        {
            Star star = new Star();
            star.Name = name;
            star.Position = new NovaPoint(x, y);
            star.Owner = owner;
            star.ThisRace = race;
            star.ResourcesOnHand = new Resources();
            if (race != null)
            {
                star.Gravity = star.OriginalGravity = race.GravityTolerance.OptimumLevel;
                star.Temperature = star.OriginalTemperature = race.TemperatureTolerance.OptimumLevel;
                star.Radiation = star.OriginalRadiation = race.RadiationTolerance.OptimumLevel;
            }

            return star;
        }

        private static ServerData NewServer(params EmpireData[] empires)
        {
            ServerData server = new ServerData();
            foreach (EmpireData empire in empires)
            {
                server.AllEmpires.Add(empire.Id, empire);
            }

            return server;
        }

        private static EmpireData NewEmpire(int id, Race race)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Id = (ushort)id;
            empire.Race = race;
            empire.AvailableComponents = new RaceComponents();
            return empire;
        }

        private static MineralPacket AddPacket(ServerData server, ushort owner, Star target, NovaPoint at, int warp, int each, int overspeedClass = 0)
        {
            MineralPacket packet = new MineralPacket();
            packet.Key = PacketLaunch.NextPacketKey(server, owner);
            packet.Position = new NovaPoint(at);
            packet.Minerals = new Resources(each, each, each, 0);
            packet.Warp = warp;
            packet.OverspeedClass = overspeedClass;
            packet.TargetName = target.Name;
            packet.Destination = new NovaPoint(target.Position);
            packet.HasMoved = true;
            server.AllMineralPackets[packet.Key] = packet;
            return packet;
        }

        private static List<string> Texts(ServerData server)
        {
            return server.AllMessages.Select(m => m.Text).ToList();
        }

        // ------------------------------------------------------------------ costs (§10)

        [TestCase("JOAT", 44, 10)]
        [TestCase("PP", 25, 5)]
        [TestCase("IT", 48, 10)]
        public void MixedPacket_UnitCost(string prt, int kilotonsEach, int resources)
        {
            Resources cost = MineralPacketRules.UnitCost(NewRace(prt), PacketMineral.Mixed);
            Assert.AreEqual(new Resources(kilotonsEach, kilotonsEach, kilotonsEach, resources), cost);
        }

        [TestCase("JOAT", 110, 10)]
        [TestCase("PP", 70, 5)]
        [TestCase("IT", 120, 10)]
        public void SingleMineralPacket_UnitCost(string prt, int kilotons, int resources)
        {
            Assert.AreEqual(new Resources(kilotons, 0, 0, resources), MineralPacketRules.UnitCost(NewRace(prt), PacketMineral.Ironium));
            Assert.AreEqual(new Resources(0, kilotons, 0, resources), MineralPacketRules.UnitCost(NewRace(prt), PacketMineral.Boranium));
            Assert.AreEqual(new Resources(0, 0, kilotons, resources), MineralPacketRules.UnitCost(NewRace(prt), PacketMineral.Germanium));
        }

        // §10b: a mixed packet carries 40 kT of each (Packet Physics 25), a single-mineral
        // packet 100 kT (Packet Physics 70); Interstellar Travelers pay more for the same cargo.
        [TestCase("JOAT", 40, 100)]
        [TestCase("IT", 40, 100)]
        [TestCase("PP", 25, 70)]
        public void Payloads(string prt, int mixedEach, int single)
        {
            Race race = NewRace(prt);
            Assert.AreEqual(new Resources(mixedEach, mixedEach, mixedEach, 0), MineralPacketRules.UnitPayload(race, PacketMineral.Mixed));
            Assert.AreEqual(new Resources(0, 0, single, 0), MineralPacketRules.UnitPayload(race, PacketMineral.Germanium));
        }

        [Test]
        public void AutoEntry_IsAlwaysTheMixedPacket()
        {
            PacketProductionUnit unit = new PacketProductionUnit(NewRace("JOAT"), PacketMineral.Ironium, true);
            Assert.AreEqual(PacketMineral.Mixed, unit.Mineral);
            Assert.AreEqual("Mineral Packets", unit.Name);
        }

        // ------------------------------------------------------------------ the percentage rule (§10)

        [TestCase(10, 0, 9)]
        [TestCase(10, 5, 59)]
        [TestCase(10, 6, 69)]
        [TestCase(70, 14, 20)]
        [TestCase(70, 2, 3)]
        public void PartialPercent_FollowsTheRecordLayoutRule(int cost, int available, int expected)
        {
            Assert.AreEqual(expected, PacketProductionUnit.AffordablePercent(new Resources(0, 0, 0, cost), new Resources(0, 0, 0, available)));
        }

        // ------------------------------------------------------------------ drivers (§10b)

        [Test]
        public void LaunchRating_BestDriver_PlusOneOnlyForTwoSlots()
        {
            Assert.AreEqual(0, MineralPacketRules.LaunchRating(null));
            Assert.AreEqual(5, MineralPacketRules.LaunchRating(MakeStarbase(1, 1, 5)));
            Assert.AreEqual(6, MineralPacketRules.LaunchRating(MakeStarbase(1, 1, 5, 5)), "the best rating in two slots");
            Assert.AreEqual(5, MineralPacketRules.LaunchRating(MakeStarbase(1, 2, 5)), "two drivers in one slot do not add");
            Assert.AreEqual(6, MineralPacketRules.LaunchRating(MakeStarbase(1, 1, 5, 6)));
            Assert.AreEqual(6, MineralPacketRules.BestDriverWarp(MakeStarbase(1, 1, 6, 6)));
        }

        [TestCase(7, 5, 5, 7)]  // inside 5..8
        [TestCase(8, 5, 5, 8)]
        [TestCase(9, 5, 5, 5)]  // above best + 3: the launch rating
        [TestCase(4, 5, 5, 5)]  // below 5
        [TestCase(0, 5, 6, 6)]  // not chosen: the launch rating
        public void LaunchSpeed(int chosen, int bestWarp, int rating, int expected)
        {
            Assert.AreEqual(expected, MineralPacketRules.LaunchSpeed(chosen, bestWarp, rating));
        }

        [TestCase(5, 5, false, 0)]
        [TestCase(7, 5, false, 2)]
        [TestCase(8, 5, false, 3)]
        [TestCase(5, 5, true, 1)]
        [TestCase(8, 5, true, 3)]
        public void OverspeedClass(int speed, int rating, bool interstellarTraveler, int expected)
        {
            Assert.AreEqual(expected, MineralPacketRules.OverspeedClass(speed, rating, interstellarTraveler));
        }

        // ------------------------------------------------------------------ decay (§10b, step 18)

        [TestCase(0, 1000, false, 0)]
        [TestCase(1, 1000, false, 100)]
        [TestCase(1, 50, false, 10)]   // the 10 kT minimum
        [TestCase(2, 1000, false, 250)]
        [TestCase(3, 1000, false, 500)]
        [TestCase(1, 5, false, 5)]     // never more than the amount
        [TestCase(2, 1000, true, 125)] // Packet Physics: half the percentage
        [TestCase(1, 40, true, 5)]     // ...and a 5 kT minimum
        public void DecayLoss(int overspeedClass, int amount, bool packetPhysics, int expected)
        {
            Assert.AreEqual(expected, MineralPacketRules.DecayLoss(amount, overspeedClass, packetPhysics));
        }

        [Test]
        public void DecayStep_RemovesAnEmptyPacket()
        {
            Race race = NewRace("JOAT");
            EmpireData empire = NewEmpire(1, race);
            ServerData server = NewServer(empire);
            Star target = MakeStar("Target", 1000, 0, 2, null);
            server.AllStars.Add(target.Name, target);

            MineralPacket survivor = AddPacket(server, 1, target, new NovaPoint(0, 0), 7, 100, 1);
            MineralPacket doomed = AddPacket(server, 1, target, new NovaPoint(0, 0), 7, 10, 1);

            new PacketDecayStep().Process(server);

            Assert.AreEqual(90, server.AllMineralPackets[survivor.Key].Minerals.Ironium);
            Assert.IsFalse(server.AllMineralPackets.ContainsKey(doomed.Key));
        }

        // ------------------------------------------------------------------ arrival (§10k item 4)

        // The spec's worked example: a 150 kT mixed packet (50 kT each) at warp 7 reaches a warp-5
        // driver with no defenses on a 100,000-colonist planet: share 510, deposit share 564, so
        // 28 kT of each lands; damage 22 kills 2,200 colonists; message 214.
        [Test]
        public void Arrival_WorkedExample()
        {
            Race race = NewRace("JOAT");
            EmpireData sender = NewEmpire(1, race);
            EmpireData receiver = NewEmpire(2, NewRace("JOAT"));
            ServerData server = NewServer(sender, receiver);
            Star target = MakeStar("Target", 100, 0, 2, receiver.Race);
            target.Colonists = 100000;
            target.Starbase = MakeStarbase(2, 1, 5);
            server.AllStars.Add(target.Name, target);

            Assert.AreEqual(510, MineralPacketRules.CaughtShare(5, 25, 7));
            Assert.AreEqual(564, MineralPacketRules.DepositShare(510));
            Assert.AreEqual(22, MineralPacketRules.RawDamage(7, 25, 150));

            MineralPacket packet = AddPacket(server, 1, target, new NovaPoint(0, 0), 7, 50);
            PacketArrival.Arrive(server, packet, new ScriptedRandom());

            Assert.AreEqual(28, target.ResourcesOnHand.Ironium);
            Assert.AreEqual(28, target.ResourcesOnHand.Boranium);
            Assert.AreEqual(28, target.ResourcesOnHand.Germanium);
            Assert.AreEqual(100000 - 2200, target.Colonists);
            Assert.AreEqual(1, server.AllMessages.Count);
            Assert.AreEqual(2, server.AllMessages[0].Audience);
            StringAssert.Contains("only partly captured", server.AllMessages[0].Text);
            StringAssert.Contains("2200 colonists", server.AllMessages[0].Text);
        }

        // 10j: sender and receiver both warp 5, so 150 kT arrived as 150 kT (message 213).
        [Test]
        public void Arrival_FullCatch_DepositsEverything()
        {
            EmpireData empire = NewEmpire(1, NewRace("PP"));
            ServerData server = NewServer(empire);
            Star target = MakeStar("Tierra", 100, 0, 1, empire.Race);
            target.Colonists = 10000;
            target.Starbase = MakeStarbase(1, 1, 5);
            server.AllStars.Add(target.Name, target);

            PacketArrival.Arrive(server, AddPacket(server, 1, target, new NovaPoint(0, 0), 5, 50), new ScriptedRandom());

            Assert.AreEqual(new Resources(50, 50, 50, 0), target.ResourcesOnHand);
            Assert.AreEqual(10000, target.Colonists);
            Assert.AreEqual(1, server.AllMessages.Count);
            StringAssert.Contains("has caught a mineral packet of 150kT", server.AllMessages[0].Text);
        }

        // Nobody catches: 111 thousandths of each mineral lands, and an unowned planet gets no message.
        [Test]
        public void Arrival_UnownedPlanet_DepositsOneNinthOfTheUncaught_NoMessage()
        {
            EmpireData empire = NewEmpire(1, NewRace("JOAT"));
            ServerData server = NewServer(empire);
            Star target = MakeStar("Rock", 100, 0, (ushort)Global.Nobody, null);
            server.AllStars.Add(target.Name, target);

            PacketArrival.Arrive(server, AddPacket(server, 1, target, new NovaPoint(0, 0), 5, 100), new ScriptedRandom());

            Assert.AreEqual(111, MineralPacketRules.DepositShare(0));
            Assert.AreEqual(11, target.ResourcesOnHand.Ironium);
            Assert.AreEqual(0, server.AllMessages.Count);
        }

        // An Alternate Reality planet is never harmed: message 326 (no driver).
        [Test]
        public void Arrival_AlternateRealityOwner_NoHarm()
        {
            EmpireData sender = NewEmpire(1, NewRace("JOAT"));
            EmpireData owner = NewEmpire(2, NewRace("AR"));
            ServerData server = NewServer(sender, owner);
            Star target = MakeStar("Habitat", 100, 0, 2, owner.Race);
            target.Colonists = 50000;
            server.AllStars.Add(target.Name, target);

            PacketArrival.Arrive(server, AddPacket(server, 1, target, new NovaPoint(0, 0), 10, 1000), new ScriptedRandom());

            Assert.AreEqual(50000, target.Colonists);
            StringAssert.Contains("no harm was done", server.AllMessages.Single().Text);
        }

        // No driver: damage = S² x kT / 160; P x damage / 1,000 vs damage, defenses lost
        // max(D x damage / 1,000, damage / 20); message 217 (colonists and defenses).
        [Test]
        public void Arrival_NoDriver_KillsColonistsAndDefenses()
        {
            EmpireData sender = NewEmpire(1, NewRace("JOAT"));
            EmpireData owner = NewEmpire(2, NewRace("JOAT"));
            ServerData server = NewServer(sender, owner);
            Star target = MakeStar("Victim", 100, 0, 2, owner.Race);
            target.Colonists = 1000000; // 10,000 units
            target.DefenseType = "None";
            target.Defenses = 0;
            server.AllStars.Add(target.Name, target);

            // Warp 10, 3 x 160 = 480 kT: damage = 100 x 480 / 160 = 300.
            PacketArrival.Arrive(server, AddPacket(server, 1, target, new NovaPoint(0, 0), 10, 160), new ScriptedRandom());

            // max(10,000 x 300 / 1,000 = 3,000, 300) units = 300,000 colonists.
            Assert.AreEqual(1000000 - 300000, target.Colonists);
            StringAssert.Contains("was bombarded by a mineral packet", server.AllMessages.Single().Text);
        }

        [Test]
        public void Arrival_WipesAPlanetWhenTheKillReachesThePopulation()
        {
            EmpireData sender = NewEmpire(1, NewRace("JOAT"));
            EmpireData owner = NewEmpire(2, NewRace("JOAT"));
            ServerData server = NewServer(sender, owner);
            Star target = MakeStar("Victim", 100, 0, 2, owner.Race);
            target.Colonists = 10000; // 100 units; damage 300 kills max(30, 300)
            owner.OwnedStars.Add(target);
            server.AllStars.Add(target.Name, target);

            PacketArrival.Arrive(server, AddPacket(server, 1, target, new NovaPoint(0, 0), 10, 160), new ScriptedRandom());

            Assert.AreEqual(0, target.Colonists);
            Assert.AreEqual(Global.Nobody, target.Owner);
            StringAssert.Contains("killing all of your colonists", server.AllMessages.Single().Text);
        }

        // An Interstellar Traveler receiver's catch strength is halved: R = 5 gives 12, so a warp-5
        // packet is 480 thousandths caught.
        [Test]
        public void InterstellarTravelerReceiver_HalvesTheCatchStrength()
        {
            Assert.AreEqual(12, MineralPacketRules.CatchStrength(5, true));
            Assert.AreEqual(480, MineralPacketRules.CaughtShare(5, 12, 5));
        }

        [Test]
        public void Arrival_PacketPhysicsSender_LearnsTheCatchingStarbaseDesign()
        {
            EmpireData sender = NewEmpire(1, NewRace("PP"));
            EmpireData owner = NewEmpire(2, NewRace("JOAT"));
            sender.EmpireReports.Add(2, new EmpireIntel(owner));
            ServerData server = NewServer(sender, owner);
            Star target = MakeStar("Base", 100, 0, 2, owner.Race);
            target.Colonists = 10000;
            target.Starbase = MakeStarbase(2, 1, 6);
            server.AllStars.Add(target.Name, target);

            PacketArrival.Arrive(server, AddPacket(server, 1, target, new NovaPoint(0, 0), 5, 10), new ScriptedRandom());

            long designKey = target.Starbase.Composition.Values.First().Design.Key;
            Assert.IsTrue(sender.EmpireReports[2].Designs.ContainsKey(designKey));
        }

        // ------------------------------------------------------------------ production (§10a, §10j test 4)

        private static ServerData PacketWorld(string prt, out Star origin, out Star target, out EmpireData empire)
        {
            Race race = NewRace(prt);
            empire = NewEmpire(1, race);
            ServerData server = NewServer(empire);

            origin = MakeStar("Chlorine", 0, 0, 1, race);
            origin.Colonists = 20000;
            origin.Starbase = MakeStarbase(1, 1, 5);
            origin.PacketDestination = "Tierra";

            // 168 ly away: 7 years at warp 5.
            target = MakeStar("Tierra", 168, 0, 1, race);
            target.Colonists = 10000;
            target.Starbase = MakeStarbase(1, 1, 5);

            server.AllStars.Add(origin.Name, origin);
            server.AllStars.Add(target.Name, target);
            empire.OwnedStars.Add(origin);
            empire.OwnedStars.Add(target);
            return server;
        }

        // 10j test 4: "Mineral Packets Up to 2" for a Packet Physics race: 10 resources and 50 kT of
        // each mineral a year, one packet (2 x 25 kT each) with a launch message; the 168 ly flight at
        // warp 5 arrives in the 7th generation after the launch and is caught whole (150 kT).
        [Test]
        public void AutoPackets_PacketPhysics_LiveTest4()
        {
            ServerData server = PacketWorld("PP", out Star origin, out Star target, out EmpireData empire);
            origin.ResourcesOnHand = new Resources(955, 900, 900, 100);
            origin.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new PacketProductionUnit(empire.Race, PacketMineral.Mixed, true), true));

            new Manufacture(server).Items(origin);

            Assert.AreEqual(955 - 50, origin.ResourcesOnHand.Ironium);
            Assert.AreEqual(100 - 10, origin.ResourcesOnHand.Energy);
            Assert.AreEqual(1, server.AllMineralPackets.Count);
            MineralPacket packet = server.AllMineralPackets.Values.Single();
            Assert.AreEqual(new Resources(50, 50, 50, 0), packet.Minerals);
            Assert.AreEqual(5, packet.Warp);
            Assert.AreEqual(0, packet.OverspeedClass);
            Assert.IsFalse(packet.HasMoved);
            Assert.That(Texts(server), Has.Some.Contains("has launched a mineral packet of 150kT bound for Tierra"));
            Assert.AreEqual(1, origin.ManufacturingQueue.Queue.Count, "the auto entry stays queued");

            // Launch year: the half step (step 21).
            new PacketMovementStep(true).Process(server);
            Assert.IsTrue(packet.HasMoved);
            Assert.AreEqual(13, packet.Position.X); // 12.5 rounded half away from zero

            // The half step skips packets that have already moved.
            new PacketMovementStep(true).Process(server);
            Assert.AreEqual(13, packet.Position.X);

            for (int year = 1; year <= 6; year++)
            {
                new PacketMovementStep(false).Process(server);
                new PacketDecayStep().Process(server);
                Assert.IsTrue(server.AllMineralPackets.ContainsKey(packet.Key), "still in flight after year " + year);
            }

            server.AllMessages.Clear();
            new PacketMovementStep(false).Process(server);

            Assert.IsFalse(server.AllMineralPackets.ContainsKey(packet.Key));
            Assert.AreEqual(new Resources(50, 50, 50, 0), target.ResourcesOnHand);
            Assert.That(Texts(server), Has.Some.Contains("has caught a mineral packet of 150kT"));
        }

        // 10j, 2414: Germanium 98 buys only 3 of 5 units at 25 kT, and the auto entry pays nothing
        // toward a fourth unit it cannot finish for lack of a mineral (status 3/4).
        [Test]
        public void AutoPackets_BuyTheAffordableUnits_NoPartialPaymentWhenShortOfAMineral()
        {
            ServerData server = PacketWorld("PP", out Star origin, out _, out EmpireData empire);
            origin.ResourcesOnHand = new Resources(900, 900, 98, 100);
            origin.ManufacturingQueue.Queue.Add(new ProductionOrder(5, new PacketProductionUnit(empire.Race, PacketMineral.Mixed, true), true));

            new Manufacture(server).Items(origin);

            Assert.AreEqual(new Resources(900 - 75, 900 - 75, 98 - 75, 100 - 15), origin.ResourcesOnHand);
            Assert.AreEqual(new Resources(75, 75, 75, 0), server.AllMineralPackets.Values.Single().Minerals);
            Assert.That(Texts(server), Has.None.Contains("has completed all of its production orders"), "a mineral-blocked auto entry keeps message 62 quiet");
        }

        // 10k item 4: with no destination the auto entry buys nothing, spends nothing, posts nothing
        // about the packet and stays on the queue.
        [Test]
        public void AutoPackets_NoDestination_BuyNothingAndStayQueued()
        {
            ServerData server = PacketWorld("JOAT", out Star origin, out _, out EmpireData empire);
            origin.PacketDestination = null;
            origin.ResourcesOnHand = new Resources(900, 900, 900, 100);
            origin.ManufacturingQueue.Queue.Add(new ProductionOrder(2, new PacketProductionUnit(empire.Race, PacketMineral.Mixed, true), true));

            new Manufacture(server).Items(origin);

            Assert.AreEqual(new Resources(900, 900, 900, 100), origin.ResourcesOnHand);
            Assert.AreEqual(0, server.AllMineralPackets.Count);
            Assert.AreEqual(1, origin.ManufacturingQueue.Queue.Count);
            Assert.That(Texts(server), Has.None.Contains("packet"));
        }

        // 10a / 10i: a manual packet order with no accelerator is deleted with message 297.
        [Test]
        public void ManualPacket_NoMassDriver_DeletedWithMessage297()
        {
            ServerData server = PacketWorld("JOAT", out Star origin, out _, out EmpireData empire);
            origin.Starbase = null;
            origin.ResourcesOnHand = new Resources(900, 900, 900, 100);
            origin.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new PacketProductionUnit(empire.Race, PacketMineral.Ironium, false), false));

            new Manufacture(server).Items(origin);

            Assert.AreEqual(0, origin.ManufacturingQueue.Queue.Count);
            Assert.AreEqual(new Resources(900, 900, 900, 100), origin.ResourcesOnHand);
            Assert.That(Texts(server), Has.Some.Contains("mineral packet order on Chlorine has been cancelled"));
        }

        // §10b: a second order the same year merges into the packet just launched (message 212);
        // an ordinary race's 110 kT Ironium order carries 100 kT.
        [Test]
        public void SecondOrderTheSameYear_MergesIntoThePacket()
        {
            ServerData server = PacketWorld("JOAT", out Star origin, out _, out EmpireData empire);
            origin.ResourcesOnHand = new Resources(900, 900, 900, 100);
            origin.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new PacketProductionUnit(empire.Race, PacketMineral.Mixed, false), false));
            origin.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new PacketProductionUnit(empire.Race, PacketMineral.Ironium, false), false));

            new Manufacture(server).Items(origin);

            Assert.AreEqual(new Resources(900 - 44 - 110, 900 - 44, 900 - 44, 100 - 20), origin.ResourcesOnHand);
            Assert.AreEqual(new Resources(140, 40, 40, 0), server.AllMineralPackets.Values.Single().Minerals);
            Assert.That(Texts(server), Has.Some.Contains("has added 100kT of minerals"));
        }

        [Test]
        public void Launch_DoesNotMergePastTheMassLimit()
        {
            ServerData server = PacketWorld("JOAT", out Star origin, out _, out EmpireData empire);
            PacketProductionUnit ironium = new PacketProductionUnit(empire.Race, PacketMineral.Ironium, false);

            PacketLaunch.Launch(server, origin, ironium, 160); // 16,000 kT
            PacketLaunch.Launch(server, origin, ironium, 3);   // 16,300: not "under" the limit

            Assert.AreEqual(2, server.AllMineralPackets.Count);
        }

        [Test]
        public void Launch_UsesTheChosenSpeedAndStoresTheOverspeedClass()
        {
            ServerData server = PacketWorld("IT", out Star origin, out _, out EmpireData empire);
            origin.PacketWarp = 7;

            MineralPacket packet = PacketLaunch.Launch(server, origin, new PacketProductionUnit(empire.Race, PacketMineral.Mixed, false), 1);

            Assert.AreEqual(7, packet.Warp);
            Assert.AreEqual(3, packet.OverspeedClass, "2 over the warp-5 driver, plus 1 for Interstellar Traveler");
        }

        // ------------------------------------------------------------------ orders

        [Test]
        public void PacketDestinationCommand_NeedsAMassDriver_AndClearsOnTheSamePlanet()
        {
            ServerData server = PacketWorld("JOAT", out Star origin, out Star target, out EmpireData empire);
            origin.PacketDestination = null;
            empire.StarReports.Add(target.Name, target.GenerateReport(ScanLevel.Owned, 2100));

            PacketDestinationCommand set = new PacketDestinationCommand(origin.Name, target.Name, 6);
            Assert.IsTrue(set.IsValid(empire));
            set.ApplyToState(empire);
            Assert.AreEqual("Tierra", origin.PacketDestination);
            Assert.AreEqual(6, origin.PacketWarp);

            new PacketDestinationCommand(origin.Name, origin.Name, 0).ApplyToState(empire);
            Assert.IsNull(origin.PacketDestination);

            origin.Starbase = null;
            Assert.IsFalse(new PacketDestinationCommand(origin.Name, target.Name, 6).IsValid(empire));
        }

        // ------------------------------------------------------------------ persistence

        [Test]
        public void Packet_StarSettings_AndEmpireReports_RoundTripThroughXml()
        {
            MineralPacket packet = new MineralPacket
            {
                Key = ((long)0).SetOwner(3).SetId(2),
                Position = new NovaPoint(12, 34),
                Minerals = new Resources(1, 2, 3, 0),
                Warp = 9,
                OverspeedClass = 2,
                OriginName = "A",
                TargetName = "B",
                Destination = new NovaPoint(56, 78),
                HasMoved = true,
            };

            XmlDocument xmldoc = new XmlDocument();
            XmlElement root = xmldoc.CreateElement("Root");
            xmldoc.AppendChild(root);
            root.AppendChild(packet.ToXml(xmldoc));
            MineralPacket loaded = new MineralPacket(root.FirstChild);

            Assert.AreEqual(packet.Key, loaded.Key);
            Assert.AreEqual(3, loaded.Owner);
            Assert.AreEqual(12, loaded.Position.X);
            Assert.AreEqual(new Resources(1, 2, 3, 0), loaded.Minerals);
            Assert.AreEqual(9, loaded.Warp);
            Assert.AreEqual(2, loaded.OverspeedClass);
            Assert.AreEqual("A", loaded.OriginName);
            Assert.AreEqual("B", loaded.TargetName);
            Assert.AreEqual(78, loaded.Destination.Y);
            Assert.IsTrue(loaded.HasMoved);

            Star star = MakeStar("Origin", 1, 2, 1, NewRace("JOAT"));
            star.PacketDestination = "B";
            star.PacketWarp = 8;
            XmlDocument starDoc = new XmlDocument();
            XmlElement starRoot = starDoc.CreateElement("Root");
            starDoc.AppendChild(starRoot);
            starRoot.AppendChild(star.ToXml(starDoc));
            Star loadedStar = new Star(starRoot.FirstChild);
            Assert.AreEqual("B", loadedStar.PacketDestination);
            Assert.AreEqual(8, loadedStar.PacketWarp);

            EmpireData empire = new EmpireData { Id = 1 };
            empire.Race = new Race();
            empire.MineralPacketReports.Add(packet.Key, packet);
            XmlDocument empireDoc = new XmlDocument();
            XmlElement empireRoot = empireDoc.CreateElement("Root");
            empireDoc.AppendChild(empireRoot);
            empireRoot.AppendChild(empire.ToXml(empireDoc));
            EmpireData reloaded = new EmpireData(empireRoot.FirstChild);
            Assert.AreEqual(9, reloaded.MineralPacketReports[packet.Key].Warp);
        }

        [Test]
        public void ServerData_SavesAndLoadsThePacketsInFlight()
        {
            ServerData server = new ServerData();
            Star target = MakeStar("B", 56, 78, (ushort)Global.Nobody, null);
            MineralPacket packet = AddPacket(server, 2, target, new NovaPoint(5, 6), 8, 77, 1);
            string path = System.IO.Path.GetTempFileName();
            try
            {
                server.StatePathName = path;
                server.ToXml();

                XmlDocument xmldoc = new XmlDocument();
                xmldoc.Load(path);
                ServerData loaded = new ServerData(xmldoc);

                Assert.IsTrue(loaded.AllMineralPackets.ContainsKey(packet.Key));
                Assert.AreEqual(77, loaded.AllMineralPackets[packet.Key].Minerals.Germanium);
                Assert.AreEqual(8, loaded.AllMineralPackets[packet.Key].Warp);
                Assert.AreEqual("B", loaded.AllMineralPackets[packet.Key].TargetName);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Test]
        public void PacketOrder_RoundTripsThroughTheQueueXml()
        {
            PacketProductionUnit unit = new PacketProductionUnit(NewRace("PP"), PacketMineral.Boranium, false);
            ProductionOrder order = new ProductionOrder(3, unit, false);

            XmlDocument xmldoc = new XmlDocument();
            XmlElement root = xmldoc.CreateElement("Root");
            xmldoc.AppendChild(root);
            root.AppendChild(order.ToXml(xmldoc));
            ProductionOrder loaded = new ProductionOrder(root.FirstChild);

            PacketProductionUnit loadedUnit = (PacketProductionUnit)loaded.Unit;
            Assert.AreEqual(PacketMineral.Boranium, loadedUnit.Mineral);
            Assert.AreEqual(new Resources(0, 70, 0, 5), loadedUnit.Cost);
            Assert.AreEqual(new Resources(0, 70, 0, 0), loadedUnit.UnitPayload);
            Assert.AreEqual(3, loaded.Quantity);
        }

        // ------------------------------------------------------------------ visibility and the PP scanner

        private static ShipDesign PlainDesign(long key)
        {
            Component blueprint = new Component { Mass = 100, Name = "Scout" };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 100 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Icon = new ShipIcon("hull0000.png", null) };
            design.Update();
            return design;
        }

        [Test]
        public void Scan_PacketPhysicsSensesEveryPacket_OthersOnlyTheirOwn()
        {
            EmpireData packetPhysics = new EmpireData { Id = 1 };
            packetPhysics.Race.Traits.SetPrimary("PP");
            EmpireData other = new EmpireData { Id = 2 };
            other.Race.Traits.SetPrimary("JOAT");
            ServerData server = NewServer(packetPhysics, other);
            packetPhysics.EmpireReports.Add(2, new EmpireIntel(other));
            other.EmpireReports.Add(1, new EmpireIntel(packetPhysics));
            Star target = MakeStar("Far", 5000, 5000, (ushort)Global.Nobody, null);
            server.AllStars.Add(target.Name, target);

            MineralPacket othersPacket = AddPacket(server, 2, target, new NovaPoint(4000, 4000), 5, 10);
            MineralPacket ppPacket = AddPacket(server, 1, target, new NovaPoint(3000, 3000), 5, 10);

            new ScanStep(new ScriptedRandom()).Process(server);

            Assert.IsTrue(packetPhysics.MineralPacketReports.ContainsKey(othersPacket.Key));
            Assert.IsTrue(packetPhysics.MineralPacketReports.ContainsKey(ppPacket.Key));
            Assert.IsTrue(other.MineralPacketReports.ContainsKey(othersPacket.Key));
            Assert.IsFalse(other.MineralPacketReports.ContainsKey(ppPacket.Key), "no scanner in range");
        }

        [Test]
        public void Scan_APacketPhysicsPacketScansWarpSquaredLightYears()
        {
            EmpireData packetPhysics = new EmpireData { Id = 1 };
            packetPhysics.Race.Traits.SetPrimary("PP");
            EmpireData other = new EmpireData { Id = 2 };
            other.Race.Traits.SetPrimary("JOAT");
            ServerData server = NewServer(packetPhysics, other);
            packetPhysics.EmpireReports.Add(2, new EmpireIntel(other));
            other.EmpireReports.Add(1, new EmpireIntel(packetPhysics));
            Star target = MakeStar("Far", 5000, 5000, (ushort)Global.Nobody, null);
            server.AllStars.Add(target.Name, target);

            // Warp 7: 49 ly.
            AddPacket(server, 1, target, new NovaPoint(1000, 1000), 7, 10);

            Fleet near = new Fleet(9001) { Owner = 2, Position = new NovaPoint(1040, 1000) };
            ShipDesign design = PlainDesign(9101);
            near.Composition.Add(new ShipToken(design, 1).Key, new ShipToken(design, 1));
            near.Waypoints.Add(new Waypoint { Position = near.Position });
            other.OwnedFleets.Add(near);

            Fleet far = new Fleet(9002) { Owner = 2, Position = new NovaPoint(1060, 1000) };
            ShipDesign farDesign = PlainDesign(9102);
            far.Composition.Add(new ShipToken(farDesign, 1).Key, new ShipToken(farDesign, 1));
            far.Waypoints.Add(new Waypoint { Position = far.Position });
            other.OwnedFleets.Add(far);

            new ScanStep(new ScriptedRandom()).Process(server);

            Assert.IsTrue(packetPhysics.FleetReports.ContainsKey(near.Key), "40 ly is inside 49");
            Assert.IsFalse(packetPhysics.FleetReports.ContainsKey(far.Key), "60 ly is outside 49");
        }

        // 10e: a planet that had no mass driver gets its packet speed initialised to the new
        // driver's rating when a starbase with one completes.
        [Test]
        public void NewStarbaseWithADriver_InitialisesThePacketSpeed()
        {
            ServerData server = PacketWorld("JOAT", out Star origin, out _, out EmpireData empire);
            origin.Starbase = null;
            origin.PacketWarp = 0;

            ShipDesign design = MakeDriverDesign(empire.GetNextDesignKey(), 1, 6, 6);
            design.Type = ItemType.Starbase;
            design.Name = "Driver Base";
            empire.Designs[design.Key] = design;
            origin.ResourcesOnHand = new Resources(10000, 10000, 10000, 10000);
            origin.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(design), false));

            new Manufacture(server).Items(origin);

            Assert.IsNotNull(origin.Starbase);
            Assert.AreEqual(7, origin.PacketWarp);
        }
    }
}
