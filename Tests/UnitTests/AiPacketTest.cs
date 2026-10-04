namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    using NUnit.Framework;

    /// <summary>
    /// The AI's mineral-packet rules (behavior-specs-10/ai-opponent-behavior.md §6 shared advisor
    /// `FUN_1090_4d10`, §12 personality 4 `FUN_10a8_123e` with the chooser `FUN_10a8_1aec`,
    /// §12 personality 5's planet-pass packets, §16 packet word), asserting the spec's numbers.
    /// </summary>
    [TestFixture]
    public class AiPacketTest
    {
        /// <summary>Returns the scripted rolls in order, then maxValue - 1.</summary>
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> rolls;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = new Queue<int>(rolls);
            }

            public int Calls { get; private set; }

            public override int Next(int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
            }

            public override int Next(int minValue, int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
            }
        }

        private ClientData clientState;
        private Race race;
        private uint nextFleetId;

        [SetUp]
        public void SetUp()
        {
            clientState = new ClientData();
            clientState.EmpireState.Id = 1;
            race = new Race();
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.OperableFactories = 10;
            race.MineBuildCost = 5;
            race.MineProductionRate = 10;
            race.OperableMines = 10;
            race.GrowthRate = 15;
            clientState.EmpireState.Race = race;
            clientState.EmpireState.TurnYear = Global.StartingYear + 50;
            clientState.EmpireState.ResearchLevels = new TechLevel(10);
            nextFleetId = 1;
        }

        private Star AddOwnedStar(string name, int x, int y, int colonists, Resources stock)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = clientState.EmpireState.Id;
            star.Position = new NovaPoint(x, y);
            star.ThisRace = race;
            star.Colonists = colonists;
            star.ResourcesOnHand = stock;
            star.MineralConcentration = new Resources(0, 0, 0, 0);
            clientState.EmpireState.OwnedStars.Add(star);
            clientState.EmpireState.StarReports.Add(name, new StarIntel { Name = name, Position = new NovaPoint(x, y), Owner = clientState.EmpireState.Id, Year = clientState.EmpireState.TurnYear });
            return star;
        }

        private StarIntel AddForeignPlanet(string name, int x, int y, int colonists, int yearsOld = 0, ushort owner = 2)
        {
            StarIntel report = new StarIntel
            {
                Name = name,
                Position = new NovaPoint(x, y),
                Owner = owner,
                Colonists = colonists,
                Year = clientState.EmpireState.TurnYear - yearsOld,
            };
            clientState.EmpireState.StarReports.Add(name, report);
            return report;
        }

        /// <summary>A starbase design (registered as the empire's, so it takes the next
        /// starbase slot) with one slot per driver warp.</summary>
        private ShipDesign StarbaseDesign(params int[] driverWarps)
        {
            ShipDesign design = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            design.Name = "Base " + design.Key;
            design.Type = ItemType.Starbase;
            design.Blueprint = new Component { Name = "Space Station" };
            Hull hull = new Hull { Modules = new List<HullModule>() };
            foreach (int warp in driverWarps)
            {
                Component driver = new Component { Name = "Mass Driver " + warp };
                driver.Properties.Add("Mass Driver", new MassDriver(warp));
                hull.Modules.Add(new HullModule { AllocatedComponent = driver, ComponentCount = 1 });
            }

            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            clientState.EmpireState.Designs[design.Key] = design;
            return design;
        }

        private void GiveStarbase(Star star, ShipDesign design)
        {
            Fleet starbase = new Fleet(star.Name + " Starbase", clientState.EmpireState.Id, nextFleetId++, star.Position);
            ShipToken token = new ShipToken(design, 1);
            starbase.Composition.Add(token.Key, token);
            starbase.Type = ItemType.Starbase;
            starbase.InOrbit = star;
            star.Starbase = starbase;
        }

        private DefaultPlanetAI PlanetAi(Star star, int category, Random random)
        {
            return new DefaultPlanetAI(star, clientState, new DefaultAIPlanner(clientState, category), random, category);
        }

        private static List<ProductionOrder> Packets(Star star)
        {
            return star.ManufacturingQueue.Queue.Where(order => order.Unit is PacketProductionUnit).ToList();
        }

        private static int Count(Star star, PacketMineral mineral)
        {
            return Packets(star).Where(order => ((PacketProductionUnit)order.Unit).Mineral == mineral).Sum(order => order.Quantity);
        }

        // ================================================================ pure rules

        [Test]
        public void SharedOrders_FollowTheMineralLadder()
        {
            List<AiPacketOrder> rich = AiPacketRules.SharedOrders(new Resources(3001, 4001, 3001, 0), new ScriptedRandom());
            Assert.AreEqual(1, rich.Count);
            Assert.AreEqual(PacketMineral.Mixed, rich[0].Mineral);
            Assert.AreEqual(30, rich[0].Count);

            List<AiPacketOrder> germanium = AiPacketRules.SharedOrders(new Resources(3001, 4001, 20001, 0), new ScriptedRandom(1));
            Assert.AreEqual(PacketMineral.Germanium, germanium[0].Mineral, "two times in three (roll 0 or 1)");
            Assert.AreEqual(80, germanium[0].Count);
            Assert.AreEqual(30, germanium[1].Count);
            Assert.AreEqual(1, AiPacketRules.SharedOrders(new Resources(3001, 4001, 20001, 0), new ScriptedRandom(2)).Count, "roll 2 fails");

            Assert.AreEqual(15, AiPacketRules.SharedOrders(new Resources(1501, 2251, 1501, 0), new ScriptedRandom()).Single().Count);

            List<AiPacketOrder> excess = AiPacketRules.SharedOrders(new Resources(1450, 2000, 1260, 0), new ScriptedRandom());
            Assert.AreEqual(2, excess.Count, "Boranium needs more than 2,500");
            Assert.AreEqual(PacketMineral.Ironium, excess[0].Mineral);
            Assert.AreEqual(1, excess[0].Count, "200 / 200");
            Assert.AreEqual(1, excess[1].Count, "10 / 200 is at least 1");
            Assert.AreEqual(25, AiPacketRules.SharedOrders(new Resources(10000, 0, 0, 0), new ScriptedRandom()).Single().Count, "at most 25");
        }

        [Test]
        public void SharedGates_SkillPacketsMineralsAndRating()
        {
            Resources plenty = new Resources(1001, 1000, 1000, 0);
            Assert.IsTrue(AiPacketRules.SharedAdvisorGates(2, false, plenty, 10));
            Assert.IsFalse(AiPacketRules.SharedAdvisorGates(1, false, plenty, 10));
            Assert.IsFalse(AiPacketRules.SharedAdvisorGates(2, true, plenty, 10));
            Assert.IsFalse(AiPacketRules.SharedAdvisorGates(2, false, new Resources(1000, 1000, 1000, 0), 10), "must exceed 3,000");
            Assert.IsFalse(AiPacketRules.SharedAdvisorGates(2, false, plenty, 9));
            Assert.IsTrue(AiPacketRules.SharedTargetWeakEnough(14, 749));
            Assert.IsFalse(AiPacketRules.SharedTargetWeakEnough(14, 750));
        }

        [Test]
        public void MassNeeded_AndTheSixteenBitWrap()
        {
            // min(4 x 125, 1,000) / (169 x 95 x 6.25e-5) = 500 / 1.0034375 = 498.3.
            Assert.AreEqual(498, AiPacketRules.MassNeeded(100, 0, 13, 0));
            Assert.AreEqual(1000 * 16000 / (169 * 95), AiPacketRules.MassNeeded(4000, 0, 13, 0), "capped at 1,000");

            // r above s: (169 - 196) x 95 wraps to 62,971; 500 / 3.9356875 = 127.
            Assert.AreEqual(127, AiPacketRules.MassNeeded(100, 14, 13, 0));
        }

        [Test]
        public void AttackBudgetAndAmount()
        {
            Assert.AreEqual(2790 - 210, AiPacketRules.AttackSurplus(new Resources(1000, 1000, 790, 0)));
            Assert.AreEqual(1000, AiPacketRules.AttackBudget(1000, 300), "70 x ((150 - 5) / 5) = 2,030");
            Assert.AreEqual(70 * 19, AiPacketRules.AttackBudget(5000, 200));
            Assert.AreEqual(0.75, AiPacketRules.SurvivingShare(false));
            Assert.AreEqual(0.875, AiPacketRules.SurvivingShare(true));

            // One full year of flight: budget x 0.75; M = K x 4/3.
            Assert.AreEqual(750, AiPacketRules.BudgetAfterDecay(1000, 0.75, 169, 13));
            Assert.AreEqual(400, AiPacketRules.AmountSent(5000, 300, 4.0 / 3.0, 169, 13));
            Assert.AreEqual(100, AiPacketRules.AmountSent(100, 300, 4.0 / 3.0, 0, 13), "min(S, K)");
        }

        [Test]
        public void Split_TakesTheLargestSurplusEachTime_TiesToTheEarlierMineral()
        {
            int[] counts = AiPacketRules.SplitIntoPackets(200, new Resources(100, 300, 300, 0));
            CollectionAssert.AreEqual(new[] { 0, 2, 1 }, counts, "3 packets = 200 / 70 rounded up");
            CollectionAssert.AreEqual(new[] { 1, 0, 0 }, AiPacketRules.SplitIntoPackets(70, new Resources(5, 5, 5, 0)));
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, AiPacketRules.SplitIntoPackets(0, new Resources(5, 5, 5, 0)));
        }

        [Test]
        public void Chooser_BorderPointsSlideStepAndDistanceQuirk()
        {
            int px;
            int py;
            AiPacketRules.BorderPoint(0, 100, 50, 400, out px, out py);
            Assert.AreEqual((400, 50), (px, py));
            AiPacketRules.BorderPoint(1, 100, 50, 400, out px, out py);
            Assert.AreEqual((150, 0), (px, py));
            AiPacketRules.BorderPoint(1, 300, 250, 400, out px, out py);
            Assert.AreEqual((400, 150), (px, py));
            AiPacketRules.BorderPoint(3, 100, 50, 400, out px, out py);
            Assert.AreEqual((50, 0), (px, py));
            AiPacketRules.BorderPoint(3, 50, 100, 400, out px, out py);
            Assert.AreEqual((0, 50), (px, py));
            AiPacketRules.BorderPoint(5, 100, 50, 400, out px, out py);
            Assert.AreEqual((0, 150), (px, py));
            AiPacketRules.BorderPoint(7, 100, 50, 400, out px, out py);
            Assert.AreEqual((400, 350), (px, py));
            AiPacketRules.BorderPoint(7, 50, 100, 400, out px, out py);
            Assert.AreEqual((350, 400), (px, py));

            Assert.AreEqual(120, AiPacketRules.SlideDrawRange(400));
            Assert.AreEqual(60, AiPacketRules.SlideOffset(400));

            px = 400; py = 390;
            AiPacketRules.Slide(30, 400, ref px, ref py);
            Assert.AreEqual((380, 400), (px, py), "20 past the corner, carried onto the top edge");
            px = 10; py = 0;
            AiPacketRules.Slide(-30, 400, ref px, ref py);
            Assert.AreEqual((0, 20), (px, py));

            AiPacketRules.StepInward(50, 400, ref px, ref py);
            Assert.AreEqual((50, 20), (px, py), "on x = 0, not on a horizontal edge: moves along x");
            px = 380; py = 400;
            AiPacketRules.StepInward(50, 400, ref px, ref py);
            Assert.AreEqual((380, 350), (px, py));

            Assert.IsFalse(AiPacketRules.ChooserDistancePasses(28560, 13), "13^4 = 28,561");
            Assert.IsTrue(AiPacketRules.ChooserDistancePasses(28561, 13));
            Assert.IsTrue(AiPacketRules.ChooserDistancePasses(1, 14), "14^4 overflows a signed 16-bit value");

            Assert.AreEqual(4, AiPacketRules.NextDirectionIndex(3, 3));
            Assert.AreEqual(3, AiPacketRules.NextDirectionIndex(3, 2));
            Assert.AreEqual(7, AiPacketRules.NextDirectionIndex(6, 6));
            Assert.AreEqual(400, AiPacketRules.FrameSide(0));
            Assert.AreEqual(2000, AiPacketRules.FrameSide(4));
        }

        [Test]
        public void MacintiShipment_AFifthOfTheStock_AtMost20000()
        {
            Assert.IsTrue(AiPacketRules.MacintiShipment(new Resources(4000, 60000, 6000, 0), out PacketMineral mineral, out int amount));
            Assert.AreEqual(PacketMineral.Boranium, mineral);
            Assert.AreEqual(12000, amount);
            Assert.AreEqual(120, AiPacketRules.MacintiPacketCount(amount));

            AiPacketRules.MacintiShipment(new Resources(200000, 0, 0, 0), out mineral, out amount);
            Assert.AreEqual(20000, amount);
            Assert.IsFalse(AiPacketRules.MacintiShipment(new Resources(5000, 5000, 5000, 0), out mineral, out amount), "more than 5,000 kT");
        }

        // ================================================================ §6 shared advisor

        private Star SharedSource(int warp)
        {
            Star source = AddOwnedStar("Source", 100, 100, 200000, new Resources(4000, 5000, 4000, 0));
            GiveStarbase(source, StarbaseDesign(warp));
            return source;
        }

        [Test]
        public void SharedAdvisor_SetsTheDestinationAndQueues30MixedAtTheBottom()
        {
            Star source = SharedSource(10);
            source.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new DefenseProductionUnit(race), false));
            AddForeignPlanet("Enemy", 300, 100, 40000);

            ScriptedRandom random = new ScriptedRandom(0, 0);
            bool queued = new AiPacketAdvisor().RunSharedAdvisor(PlanetAi(source, AiCategory.Automitrons, random), AiCategory.Automitrons, 2, random);

            Assert.IsTrue(queued);
            Assert.AreEqual("Enemy", source.PacketDestination);
            Assert.IsTrue(clientState.Commands.Any(command => command is PacketDestinationCommand));
            Assert.AreEqual(30, Count(source, PacketMineral.Mixed));
            Assert.IsInstanceOf<PacketProductionUnit>(source.ManufacturingQueue.Queue.Last().Unit, "advisors queue at the bottom");
            Assert.AreEqual(2, random.Calls, "the 1-in-4 roll and one reservoir draw");
        }

        [Test]
        public void SharedAdvisor_NeedsSkill2_Rating10_AFreshForeignReport_AndTheRoll()
        {
            Star source = SharedSource(9);
            AddForeignPlanet("Enemy", 300, 100, 40000);
            ScriptedRandom random = new ScriptedRandom(0, 0);
            AiPacketAdvisor advisor = new AiPacketAdvisor();

            Assert.IsFalse(advisor.RunSharedAdvisor(PlanetAi(source, AiCategory.Automitrons, random), AiCategory.Automitrons, 2, random), "rating 9");
            Assert.AreEqual(0, random.Calls, "no roll when a gate fails");

            GiveStarbase(source, StarbaseDesign(9, 9));
            Assert.IsFalse(advisor.RunSharedAdvisor(PlanetAi(source, AiCategory.Automitrons, random), AiCategory.Automitrons, 1, random), "skill 1");
            Assert.IsFalse(advisor.RunSharedAdvisor(PlanetAi(source, AiCategory.Cybertrons, random), AiCategory.Cybertrons, 3, random), "never personality 4");

            Assert.IsFalse(advisor.RunSharedAdvisor(PlanetAi(source, AiCategory.Automitrons, new ScriptedRandom(1)), AiCategory.Automitrons, 2, new ScriptedRandom(1)), "the 1-in-4 roll fails");

            clientState.EmpireState.StarReports["Enemy"].Year -= 3;
            Assert.IsFalse(advisor.RunSharedAdvisor(PlanetAi(source, AiCategory.Automitrons, random), AiCategory.Automitrons, 2, random), "a report three years old");
            Assert.IsNull(source.PacketDestination);
            Assert.AreEqual(0, Packets(source).Count);
        }

        // ================================================================ §12 personality 5

        [Test]
        public void Macinti_ShipsAFifthToTheOwnWarp10PlanetHoldingLeast()
        {
            clientState.EmpireState.TurnYear = Global.StartingYear + 121;
            Star source = AddOwnedStar("Source", 100, 100, 1000100, new Resources(4000, 60000, 6000, 0));
            GiveStarbase(source, StarbaseDesign(10));
            Star poor = AddOwnedStar("Poor", 200, 100, 1000, new Resources(0, 100, 0, 0));
            GiveStarbase(poor, StarbaseDesign(10));
            Star richer = AddOwnedStar("Richer", 150, 100, 1000, new Resources(0, 500, 0, 0));
            GiveStarbase(richer, StarbaseDesign(10));
            Star noDriver = AddOwnedStar("Slow", 120, 100, 1000, new Resources(0, 0, 0, 0));
            GiveStarbase(noDriver, StarbaseDesign(9));
            Star far = AddOwnedStar("Far", 100, 261, 1000, new Resources(0, 0, 0, 0));
            GiveStarbase(far, StarbaseDesign(10));

            ScriptedRandom random = new ScriptedRandom(0);
            new AiPacketAdvisor().RunMacintiPlanetPassPackets(PlanetAi(source, AiCategory.Macinti, random), 121, random);

            Assert.AreEqual("Poor", source.PacketDestination, "least Boranium among warp-10 planets within 160 ly");
            Assert.AreEqual(120, Count(source, PacketMineral.Boranium), "12,000 kT / 100");
        }

        [Test]
        public void Macinti_OnlyAfterYear120_OneTimeInFour_AndTheTargetMustHoldLess()
        {
            Star source = AddOwnedStar("Source", 100, 100, 1000100, new Resources(0, 60000, 0, 0));
            GiveStarbase(source, StarbaseDesign(10));
            Star full = AddOwnedStar("Full", 200, 100, 1000, new Resources(0, 12000, 0, 0));
            GiveStarbase(full, StarbaseDesign(10));
            AiPacketAdvisor advisor = new AiPacketAdvisor();

            advisor.RunMacintiPlanetPassPackets(PlanetAi(source, AiCategory.Macinti, new ScriptedRandom(0)), 120, new ScriptedRandom(0));
            advisor.RunMacintiPlanetPassPackets(PlanetAi(source, AiCategory.Macinti, new ScriptedRandom(1)), 121, new ScriptedRandom(1));
            advisor.RunMacintiPlanetPassPackets(PlanetAi(source, AiCategory.Macinti, new ScriptedRandom(0)), 121, new ScriptedRandom(0));

            Assert.IsNull(source.PacketDestination, "12,000 is not less than the 12,000 shipped");
            Assert.AreEqual(0, Packets(source).Count);

            full.ResourcesOnHand = new Resources(0, 11999, 0, 0);
            advisor.RunMacintiPlanetPassPackets(PlanetAi(source, AiCategory.Macinti, new ScriptedRandom(0)), 121, new ScriptedRandom(0));
            Assert.AreEqual(120, Count(source, PacketMineral.Boranium));
        }

        // ================================================================ §12 personality 4

        private Star CybertronSource(Resources stock)
        {
            race.Traits.SetPrimary("PP");
            Star source = AddOwnedStar("Source", 200, 200, 200000, stock);
            GiveStarbase(source, StarbaseDesign(10));
            return source;
        }

        private void RunCybertrons(Random random, CybertronPacketMemory memory, params Star[] planets)
        {
            List<DefaultPlanetAI> ais = planets.Select(star => PlanetAi(star, AiCategory.Cybertrons, random)).ToList();
            new AiPacketAdvisor { Memory = memory }.RunCybertronPackets(ais, 2, 0, random);
        }

        [Test]
        public void HubSupply_SendsSevenOfTheMissingMineral_AtTheSmallerRating()
        {
            race.Traits.SetPrimary("PP");
            Star target = AddOwnedStar("Target", 300, 200, 200000, new Resources(500, 1000, 1000, 0));
            GiveStarbase(target, StarbaseDesign(12));                    // slot 0: not a hub, short below 1,000
            Star hub = AddOwnedStar("Hub", 200, 200, 200000, new Resources(1000, 1000, 1000, 0));
            GiveStarbase(hub, StarbaseDesign(10));                       // slot 1: a packet hub

            ScriptedRandom random = new ScriptedRandom();
            RunCybertrons(random, null, hub);

            Assert.AreEqual("Target", hub.PacketDestination);
            Assert.AreEqual(10, hub.PacketWarp, "the smaller launch rating: no overspeed");
            Assert.AreEqual(7, Count(hub, PacketMineral.Ironium), "min(n = 200 / 2 / 5 = 20, 7)");
            Assert.AreEqual(0, Count(hub, PacketMineral.Boranium), "1,000 kT is not short");
            Assert.AreSame(Packets(hub).First(), hub.ManufacturingQueue.Queue.First(), "packets go to the top");
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void Attack_SendsEnoughThatKArrives_AndSetsTheCoolDown()
        {
            Star source = CybertronSource(new Resources(1000, 1000, 1000, 0));
            AddForeignPlanet("Enemy", 300, 200, 40000);                   // f = 100, d = 100, s = 13
            CybertronPacketMemory memory = new CybertronPacketMemory();

            RunCybertrons(new ScriptedRandom(), memory, source);

            // S = 2,790; budget = min(S, 70 x 19) = 1,330; K = 498; M = 498 x (4/3)^(100/169) = 590,
            // so 9 packets of 70 kT, split by largest surplus with ties to the earlier mineral.
            Assert.AreEqual("Enemy", source.PacketDestination);
            Assert.AreEqual(13, source.PacketWarp, "best driver warp + 3");
            Assert.AreEqual(3, Count(source, PacketMineral.Ironium));
            Assert.AreEqual(3, Count(source, PacketMineral.Boranium));
            Assert.AreEqual(3, Count(source, PacketMineral.Germanium));
            Assert.AreEqual(3, memory.CoolDown("Enemy"));
            Assert.IsFalse(CybertronPacketWord.Has(memory.Word("Source"), CybertronPacketWord.FollowUpBit), "100 ly is within a year (169)");
        }

        [Test]
        public void Attack_BeyondOneYear_SetsTheFollowUp_WhichNextTurnAddsOnePacket()
        {
            Star source = CybertronSource(new Resources(1000, 1000, 1000, 0));
            AddForeignPlanet("Enemy", 400, 200, 40000);                   // d = 200 > 169
            CybertronPacketMemory memory = new CybertronPacketMemory();

            RunCybertrons(new ScriptedRandom(), memory, source);
            Assert.IsTrue(CybertronPacketWord.Has(memory.Word("Source"), CybertronPacketWord.FollowUpBit));
            int before = Packets(source).Sum(order => order.Quantity);

            ScriptedRandom second = new ScriptedRandom();
            RunCybertrons(second, memory, source);

            Assert.AreEqual(before + 1, Packets(source).Sum(order => order.Quantity), "one follow-up packet, no new attack");
            Assert.AreEqual("Enemy", source.PacketDestination, "the destination stays the attack target");
            Assert.IsFalse(CybertronPacketWord.Has(memory.Word("Source"), CybertronPacketWord.FollowUpBit), "cleared");
            Assert.IsFalse(CybertronPacketWord.Has(memory.Word("Source"), CybertronPacketWord.RestBit), "no rest after a follow-up");
            Assert.AreEqual(0, second.Calls, "no direction is drawn on a follow-up");
            Assert.AreEqual(2, memory.CoolDown("Enemy"), "3, less one at the start of the run");
        }

        [Test]
        public void Fallback_ShootsAtTheBorderTarget_ThenRestsATurn()
        {
            // S = 300 - 210 = 90: no attack. Roll 2 -> index 2 (-y): border point (200, 0);
            // slide draw 60 -> j = 0; step 0. The nearest planet to (200, 0) is "Rim", 190 ly
            // from the source: at least s^2 = 169 ly.
            Star source = CybertronSource(new Resources(300, 0, 0, 0));
            AddForeignPlanet("Rim", 200, 10, 0, 0, Global.Nobody);
            CybertronPacketMemory memory = new CybertronPacketMemory();

            ScriptedRandom first = new ScriptedRandom(2, 60, 0);
            RunCybertrons(first, memory, source);

            Assert.AreEqual(3, first.Calls);
            Assert.AreEqual("Rim", source.PacketDestination);
            Assert.AreEqual(13, source.PacketWarp);
            Assert.AreEqual(1, Count(source, PacketMineral.Ironium), "one packet of the largest surplus (above 169)");
            Assert.AreEqual(3, memory.CoolDown("Rim"));
            Assert.AreEqual(2, CybertronPacketWord.DirectionIndex(memory.Word("Source")));
            Assert.IsTrue(CybertronPacketWord.Has(memory.Word("Source"), CybertronPacketWord.RestBit));

            ScriptedRandom rest = new ScriptedRandom();
            RunCybertrons(rest, memory, source);
            Assert.AreEqual(0, rest.Calls, "the rest turn draws nothing");
            Assert.AreEqual(1, Count(source, PacketMineral.Ironium));
            Assert.IsFalse(CybertronPacketWord.Has(memory.Word("Source"), CybertronPacketWord.RestBit));
        }

        [Test]
        public void Fallback_RefusesAnOwnOrTooCloseNearestPlanet()
        {
            Star source = CybertronSource(new Resources(300, 0, 0, 0));
            AddForeignPlanet("Near", 200, 100, 0, 0, Global.Nobody);       // 100 ly: under s^2
            ScriptedRandom random = new ScriptedRandom(2, 60, 100);         // step 100 in: point (200, 100)
            RunCybertrons(random, new CybertronPacketMemory(), source);

            Assert.IsNull(source.PacketDestination);
            Assert.AreEqual(0, Packets(source).Count);
        }

        [Test]
        public void StatelessMemory_TreatsPlanetsOurPacketsAreBoundFor_AsCoolingDown()
        {
            Star source = CybertronSource(new Resources(1000, 1000, 1000, 0));
            AddForeignPlanet("Enemy", 300, 200, 40000);
            clientState.EmpireState.MineralPacketReports[1] = new MineralPacket { Key = 1, Owner = 1, TargetName = "Enemy" };
            clientState.EmpireState.MineralPacketReports[2] = new MineralPacket { Key = 2, Owner = 1, TargetName = "Source" };

            CybertronPacketMemory memory = CybertronPacketMemory.FromPacketsInFlight(clientState.EmpireState);
            Assert.AreEqual(1, memory.CoolDown("Enemy"));
            Assert.AreEqual(0, memory.CoolDown("Source"), "own planets get no cool-down");

            // The attack therefore finds no target; the fallback's chooser runs instead.
            RunCybertrons(new ScriptedRandom(2, 60, 0), null, source);
            Assert.AreNotEqual("Enemy", source.PacketDestination);
        }
    }
}
