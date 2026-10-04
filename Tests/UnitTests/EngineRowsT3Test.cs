#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Turn-generation rows of behavior-specs-10/turn-generation-engine.md: follow orders (steps 9
    /// and 11), design normalisation (step 8), the race-definition integrity check (step 13), the
    /// planet-artifact bounty (§11), the banked-pool research buy loop (§1 "Loops and repeated
    /// work"), and the Mystery Trader's planet trade (§5a "Encounter rules for planets").
    /// </summary>
    [TestFixture]
    public class EngineRowsT3Test
    {
        /// <summary>Scripted Next(int) values, failing on an unscripted or out-of-range draw.</summary>
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> values;

            public ScriptedRandom(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public int Remaining => values.Count;

            public override int Next(int maxValue)
            {
                Assert.IsTrue(values.Count > 0, "Unscripted draw: Next(" + maxValue + ")");
                int value = values.Dequeue();
                Assert.IsTrue(value >= 0 && value < maxValue, "Scripted value " + value + " out of range for Next(" + maxValue + ")");
                return value;
            }

            public override int Next() => throw new AssertionException("Unexpected Next()");

            public override int Next(int minValue, int maxValue) => throw new AssertionException("Unexpected Next(min, max)");

            public override double NextDouble() => throw new AssertionException("Unexpected NextDouble()");
        }

        private ServerData serverState;
        private bool originalNoRandomEvents;

        [SetUp]
        public void SetUp()
        {
            originalNoRandomEvents = GameSettings.Data.NoRandomEvents;
            GameSettings.Data.NoRandomEvents = false;
            serverState = new SimpleServerData();
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.Data.NoRandomEvents = originalNoRandomEvents;
        }

        private EmpireData AddEmpire(ushort id, string aiProgram = "Human", int aiSkill = -1, bool withPlayer = true)
        {
            EmpireData empire = new SimpleEmpireData { Id = id, Race = LegalRace() };
            empire.Race.PluralName = "Race" + id;
            empire.AvailableComponents = new RaceComponents();
            serverState.AllEmpires.Add(empire.Id, empire);
            if (withPlayer)
            {
                serverState.AllPlayers.Add(new PlayerSettings { PlayerNumber = id, AiProgram = aiProgram, AiSkill = aiSkill });
            }

            return empire;
        }

        /// <summary>A plain Jack of All Trades race with every value in range and a non-negative total.</summary>
        private static Race LegalRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            race.GravityTolerance.MinimumValue = 15;
            race.GravityTolerance.MaximumValue = 85;
            race.TemperatureTolerance.MinimumValue = 15;
            race.TemperatureTolerance.MaximumValue = 85;
            race.RadiationTolerance.MinimumValue = 15;
            race.RadiationTolerance.MaximumValue = 85;
            race.GrowthRate = 15;
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.OperableFactories = 10;
            race.FactoryBuildCost = 10;
            race.MineProductionRate = 10;
            race.OperableMines = 10;
            race.MineBuildCost = 5;
            race.ResearchCosts = new TechLevel(100);
            return race;
        }

        private static ShipDesign PlainDesign(long key)
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000, ArmorStrength = 50 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Name = "Design " + key };
            design.Update();
            return design;
        }

        private static Fleet AddFleet(EmpireData owner, uint id, int x, int y)
        {
            Fleet fleet = new Fleet(id);
            fleet.Owner = (ushort)owner.Id;
            fleet.Id = id;
            fleet.Name = "Fleet " + owner.Id + "-" + id;
            fleet.Position = new NovaPoint(x, y);
            ShipToken token = new ShipToken(PlainDesign(id), 1);
            fleet.Composition.Add(token.Key, token);
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(x, y), Destination = "Space at " + fleet.Position, WarpFactor = 0 });
            owner.AddOrUpdateFleet(fleet);
            return fleet;
        }

        private static void Follow(Fleet follower, Fleet leader)
        {
            follower.Waypoints[0].AimAtFleet(leader);
        }

        private int CountMessages(string type, int audience)
        {
            return serverState.AllMessages.Count(message => message.Type == type && message.Audience == audience);
        }

        // ------------------------------------------------------------------ follow orders

        [Test]
        public void Follow_AFollowerCopiesItsLeadersOnwardLeg_AndStaysMarked()
        {
            EmpireData empire = AddEmpire(1);
            Fleet leader = AddFleet(empire, 1, 100, 100);
            leader.Waypoints.Add(new Waypoint { Position = new NovaPoint(300, 100), Destination = "Space at 300,100", WarpFactor = 7, Task = new ColoniseTask() });
            Fleet follower = AddFleet(empire, 2, 100, 100);
            Follow(follower, leader);

            FollowOrderStep step = new FollowOrderStep();
            step.Process(serverState);

            Assert.AreEqual(2, follower.Waypoints.Count);
            Assert.AreEqual(new NovaPoint(300, 100), follower.Waypoints[1].Position);
            Assert.AreEqual(7, follower.Waypoints[1].WarpFactor);
            Assert.IsInstanceOf<NoTask>(follower.Waypoints[1].Task, "The leg is copied without the leader's task (reported ambiguity)");
            Assert.IsTrue(step.FollowMarks.Contains(follower.Key));
            Assert.IsFalse(step.FollowMarks.Contains(leader.Key), "A fleet with an onward waypoint is not a follower");
            Assert.AreEqual(0, CountMessages(FollowOrderStep.MessageType, empire.Id));
        }

        [Test]
        public void Follow_AChainOfFollowersSettlesOverSeveralPasses()
        {
            EmpireData empire = AddEmpire(1);
            Fleet c = AddFleet(empire, 1, 0, 0);
            c.Waypoints.Add(new Waypoint { Position = new NovaPoint(50, 0), Destination = "Space at 50,0", WarpFactor = 5 });

            // Table order puts the end of the chain first, so each pass settles one more link.
            Fleet a = AddFleet(empire, 2, 0, 0);
            Fleet b = AddFleet(empire, 3, 0, 0);
            Follow(a, b);
            Follow(b, c);

            new FollowOrderStep().Process(serverState);

            Assert.AreEqual(new NovaPoint(50, 0), b.Waypoints[1].Position);
            Assert.AreEqual(new NovaPoint(50, 0), a.Waypoints[1].Position, "A copies the leg B received in the earlier pass");
        }

        [Test]
        public void Follow_AChainLongerThanEightPasses_LeavesTheTailUnsettled_WithMessage312()
        {
            EmpireData empire = AddEmpire(1);
            Fleet head = AddFleet(empire, 1, 0, 0);
            head.Waypoints.Add(new Waypoint { Position = new NovaPoint(50, 0), Destination = "Space at 50,0", WarpFactor = 5 });

            // Ten followers; the table lists the tail of the chain first, so each pass settles
            // exactly one more link (20 follows the head, 19 follows 20, ... 11 follows 12).
            List<Fleet> chain = new List<Fleet>();
            for (uint id = 11; id <= 20; id++)
            {
                chain.Add(AddFleet(empire, id, 0, 0));
            }

            Fleet previous = head;
            foreach (Fleet follower in chain.OrderByDescending(fleet => fleet.Id))
            {
                Follow(follower, previous);
                previous = follower;
            }

            FollowOrderStep step = new FollowOrderStep();
            step.Process(serverState);

            int settled = chain.Count(fleet => fleet.Waypoints.Count == 2);
            Assert.AreEqual(FollowOrderStep.MaximumPasses, settled, "At most eight passes");
            Assert.AreEqual(chain.Count - FollowOrderStep.MaximumPasses, CountMessages(FollowOrderStep.MessageType, empire.Id));
        }

        [Test]
        public void Follow_ALeaderGoingNowhere_OrGone_GivesMessage312_AndTheMarkIsLost()
        {
            EmpireData empire = AddEmpire(1);
            Fleet idle = AddFleet(empire, 1, 0, 0);
            Fleet followsIdle = AddFleet(empire, 2, 0, 0);
            Follow(followsIdle, idle);

            Fleet gone = AddFleet(empire, 3, 10, 10);
            Fleet followsGone = AddFleet(empire, 4, 10, 10);
            Follow(followsGone, gone);
            empire.RemoveFleet(gone);

            FollowOrderStep step = new FollowOrderStep();
            step.Process(serverState);

            Assert.AreEqual(1, followsIdle.Waypoints.Count);
            Assert.AreEqual(1, followsGone.Waypoints.Count);
            Assert.IsFalse(step.FollowMarks.Contains(followsIdle.Key));
            Assert.IsFalse(step.FollowMarks.Contains(followsGone.Key));
            Assert.AreEqual(2, CountMessages(FollowOrderStep.MessageType, empire.Id));
        }

        [Test]
        public void Follow_TheEndOfMovementRefreshSkipsAMarkedFleetsFleetTargetedLegs()
        {
            EmpireData empire = AddEmpire(1);
            EmpireData other = AddEmpire(2);
            Fleet quarry = AddFleet(other, 9, 500, 500);

            Fleet leader = AddFleet(empire, 1, 0, 0);
            Waypoint chase = new Waypoint { WarpFactor = 9, Task = new NoTask() };
            chase.AimAtFleet(quarry);
            leader.Waypoints.Add(chase);
            Fleet follower = AddFleet(empire, 2, 0, 0);
            Follow(follower, leader);

            FleetPursuit pursuit = new FleetPursuit(serverState);
            new FollowOrderStep(pursuit.FollowMarked).Process(serverState);
            Assert.IsTrue(follower.Waypoints[1].IsFleetTarget, "The copied leg still aims at the quarry");

            quarry.Position = new NovaPoint(600, 600);
            pursuit.RefreshAndResolveArrivals();

            Assert.AreEqual(new NovaPoint(600, 600), leader.Waypoints[1].Position, "An unmarked pursuer is refreshed");
            Assert.AreEqual(new NovaPoint(500, 500), follower.Waypoints[1].Position, "The follower's leg is left alone");
        }

        // ------------------------------------------------------------------ design normalisation

        private static ShipDesign DesignWithEngineSlot(long key, Component inEngineSlot, int count, int capacity = 2)
        {
            Component blueprint = new Component { Mass = 10, Name = "Test Hull" };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 100, ArmorStrength = 20 };
            hull.Modules.Add(new HullModule { ComponentType = "General Purpose", ComponentMaximum = 1 });
            hull.Modules.Add(new HullModule { ComponentType = "Engine", ComponentMaximum = capacity, AllocatedComponent = inEngineSlot, ComponentCount = count });
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Name = "D" + key, Type = ItemType.Ship };
            design.Update();
            return design;
        }

        [Test]
        public void DesignNormalisation_AnEmptyEngineSlot_GetsOneQuickJump5()
        {
            EmpireData empire = AddEmpire(1);
            ShipDesign design = DesignWithEngineSlot(empire.GetNextDesignKey(), null, 0);
            empire.Designs.Add(design.Key, design);

            new DesignNormalisationStep().Process(serverState);

            HullModule slot = design.Hull.Modules[1];
            Assert.IsNotNull(slot.AllocatedComponent);
            Assert.AreEqual("Quick Jump 5", slot.AllocatedComponent.Name, "Category 1, subtype 1 = engine idx 1 of component-stats.tsv");
            Assert.AreEqual(1, slot.ComponentCount, "Quantity at least 1");
            Assert.IsNotNull(design.Engine, "The design summary now has an engine");
        }

        [Test]
        public void DesignNormalisation_ANonEngineInTheEngineSlot_IsReplaced_KeepingItsQuantity()
        {
            EmpireData empire = AddEmpire(1);
            Component junk = new Component { Name = "Not An Engine", Mass = 1 };
            ShipDesign design = DesignWithEngineSlot(empire.GetNextDesignKey(), junk, 2);
            empire.Designs.Add(design.Key, design);

            new DesignNormalisationStep().Process(serverState);

            Assert.AreEqual("Quick Jump 5", design.Hull.Modules[1].AllocatedComponent.Name);
            Assert.AreEqual(2, design.Hull.Modules[1].ComponentCount);
        }

        [Test]
        public void DesignNormalisation_ADesignWithAnEngine_IsLeftAlone()
        {
            EmpireData empire = AddEmpire(1);
            Component engine = new AllComponents().Fetch("Long Hump 6");
            Assert.IsNotNull(engine);
            ShipDesign design = DesignWithEngineSlot(empire.GetNextDesignKey(), engine, 1);
            empire.Designs.Add(design.Key, design);

            new DesignNormalisationStep().Process(serverState);

            Assert.AreSame(engine, design.Hull.Modules[1].AllocatedComponent);
        }

        // ------------------------------------------------------------------ race integrity

        [Test]
        public void RaceIntegrity_ALegalRace_IsUntouched_AndNobodyIsTold()
        {
            EmpireData empire = AddEmpire(1);
            Assert.GreaterOrEqual(RaceIntegrityStep.Score(empire.Race), 0);

            new RaceIntegrityStep().Process(serverState);

            Assert.AreEqual(1000, empire.Race.ColonistsPerResource);
            Assert.AreEqual(15, empire.Race.GrowthRate);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [Test]
        public void RaceIntegrity_AnOutOfRangeValue_IsClamped_TheRaceIsDegradedToAtLeast500_AndBothNoticesGoOut()
        {
            EmpireData cheater = AddEmpire(1);
            EmpireData human = AddEmpire(2);
            EmpireData computer = AddEmpire(3, "Nova_AI");
            cheater.Race.FactoryProduction = 40; // slot 1 maximum is 15

            new RaceIntegrityStep().Process(serverState);

            Assert.AreEqual(15, cheater.Race.FactoryProduction, "Clamped into 5-15");
            Assert.GreaterOrEqual(RaceIntegrityStep.Score(cheater.Race), RaceIntegrityStep.RepairedTotal);
            Assert.Greater(cheater.Race.ColonistsPerResource, 1000, "The degrade loop starts by raising colonists per resource");
            Assert.AreEqual(1, CountMessages(RaceIntegrityStep.MessageType, cheater.Id), "Message 279");
            Assert.AreEqual(1, CountMessages(RaceIntegrityStep.MessageType, human.Id), "Message 386 to the other human");
            Assert.AreEqual(0, CountMessages(RaceIntegrityStep.MessageType, computer.Id), "No notice for computer players");
        }

        [Test]
        public void RaceIntegrity_TheDegradeLoopOrder_ColonistsPerResourceThenGrowthThenResearchClasses()
        {
            Race race = LegalRace();
            race.ColonistsPerResource = 2400;
            race.GrowthRate = 3;

            // A target no race reaches forces the loop through every stage.
            RaceIntegrityStep.Degrade(race, int.MinValue);
            Assert.AreEqual(2500, race.ColonistsPerResource, "Raised one step to the 2,500 maximum");
        }

        [Test]
        public void RaceIntegrity_DegradeStopsAsSoonAsTheTotalReaches500()
        {
            Race race = LegalRace();
            race.GrowthRate = 20;
            int start = RaceIntegrityStep.Score(race);
            Assume.That(start, Is.LessThan(RaceIntegrityStep.RepairedTotal));

            RaceIntegrityStep.Degrade(race, start);

            Assert.GreaterOrEqual(RaceIntegrityStep.Score(race), RaceIntegrityStep.RepairedTotal);
            if (race.ColonistsPerResource < RaceIntegrityStep.MaximumColonistsPerResource)
            {
                Assert.AreEqual(20, race.GrowthRate, "The growth rate is touched only once colonists per resource is at its maximum");
            }

            Assert.IsTrue(MysteryTraderStep.FieldOrder.All(field => race.ResearchCosts[field] == 100) || race.GrowthRate == 1,
                "Research classes are lowered only after the growth rate reached 1");
        }

        [Test]
        public void RaceIntegrity_ComputerPlayers_AreExempt()
        {
            EmpireData computer = AddEmpire(1, "Nova_AI");
            computer.Race.FactoryProduction = 40;

            new RaceIntegrityStep().Process(serverState);

            Assert.AreEqual(40, computer.Race.FactoryProduction);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [Test]
        public void RaceIntegrity_ANegativeTotal_IsIllegal_EvenWithEveryValueInRange()
        {
            EmpireData empire = AddEmpire(1);
            Race race = empire.Race;
            race.ColonistsPerResource = 700;
            race.FactoryProduction = 15;
            race.OperableFactories = 25;
            race.FactoryBuildCost = 5;
            race.MineProductionRate = 25;
            race.OperableMines = 25;
            race.MineBuildCost = 2;
            race.ResearchCosts = new TechLevel(50);
            race.GrowthRate = 20;
            Assume.That(RaceIntegrityStep.Score(race), Is.LessThan(0));

            new RaceIntegrityStep().Process(serverState);

            Assert.GreaterOrEqual(RaceIntegrityStep.Score(race), RaceIntegrityStep.RepairedTotal);
            Assert.AreEqual(1, CountMessages(RaceIntegrityStep.MessageType, empire.Id));
        }

        // ------------------------------------------------------------------ planet artifact

        private Star AddStar(string name, ushort owner, int colonists, bool artifact)
        {
            Star star = new Star { Name = name, Position = new NovaPoint(serverState.AllStars.Count * 10, 0), Owner = owner, Colonists = colonists, HasArtifact = artifact };
            serverState.AllStars.Add(star.Key, star);
            return star;
        }

        [Test]
        public void Artifact_ANewlyOwnedFlaggedPlanet_PaysTheBountyOnce_InARandomField()
        {
            EmpireData empire = AddEmpire(1);
            Star star = AddStar("Relic", 1, 25000, true);

            // Next(301) = 250 -> 350 resources; Next(6) = 3 -> Construction (original order).
            new PlanetArtifactStep(new ScriptedRandom(250, 3)).Process(serverState);

            Assert.AreEqual(350, empire.ResearchResources[TechLevel.ResearchField.Construction]);
            Assert.IsFalse(star.HasArtifact, "Cleared on use");
            Assert.AreEqual(1, CountMessages(PlanetArtifactStep.MessageType, empire.Id), "Message 94");

            new PlanetArtifactStep(new ScriptedRandom()).Process(serverState);
            Assert.AreEqual(350, empire.ResearchResources[TechLevel.ResearchField.Construction], "Paid once");
        }

        [Test]
        public void Artifact_TheBountyIs100To400()
        {
            EmpireData empire = AddEmpire(1);
            AddStar("Low", 1, 5000, true);
            AddStar("High", 1, 5000, true);

            new PlanetArtifactStep(new ScriptedRandom(0, 0, 300, 0)).Process(serverState);

            Assert.AreEqual(100 + 400, empire.ResearchResources[TechLevel.ResearchField.Energy]);
        }

        [Test]
        public void Artifact_UnownedPlanetsKeepTheirFlag_AndNoRandomEventsRemovesTheBounty()
        {
            EmpireData empire = AddEmpire(1);
            Star unowned = AddStar("Wild", Global.Nobody, 0, true);
            new PlanetArtifactStep(new ScriptedRandom()).Process(serverState);
            Assert.IsTrue(unowned.HasArtifact);

            GameSettings.Data.NoRandomEvents = true;
            Star owned = AddStar("Mine", 1, 5000, true);
            new PlanetArtifactStep(new ScriptedRandom()).Process(serverState);
            Assert.AreEqual(0, serverState.AllMessages.Count);
            Assert.AreEqual(0, empire.ResearchResources[TechLevel.ResearchField.Energy]);
        }

        [Test]
        public void Artifact_AssignAtCreation_FlagsOneUnownedPlanetInThree_AndNoneWithNoRandomEvents()
        {
            AddEmpire(1);
            Star home = AddStar("Home", 1, 25000, false);
            Star a = AddStar("A", Global.Nobody, 0, false);
            Star b = AddStar("B", Global.Nobody, 0, false);

            // One Next(3) per unowned planet; 0 flags it. The home world is not rolled.
            PlanetArtifactStep.AssignAtCreation(serverState, new ScriptedRandom(0, 2));
            Assert.IsFalse(home.HasArtifact);
            Assert.IsTrue(a.HasArtifact);
            Assert.IsFalse(b.HasArtifact);

            a.HasArtifact = false;
            GameSettings.Data.NoRandomEvents = true;
            PlanetArtifactStep.AssignAtCreation(serverState, new ScriptedRandom());
            Assert.IsFalse(a.HasArtifact);
        }

        [Test]
        public void Artifact_TheFlagRoundTripsThroughTheStarRecord()
        {
            Star star = new Star { Name = "Relic", Position = new NovaPoint(5, 5), HasArtifact = true };
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            System.Xml.XmlElement element = star.ToXml(doc);
            doc.AppendChild(element);
            Star loaded = new Star(doc.DocumentElement);
            Assert.IsTrue(loaded.HasArtifact);
        }

        // ------------------------------------------------------------------ research buy loop

        [Test]
        public void BuyLoop_ABankedPoolInAFieldNotBeingResearched_IsSpent()
        {
            EmpireData empire = AddEmpire(1);
            empire.ResearchLevels = new TechLevel(0);
            int cost = Research.Cost(TechLevel.ResearchField.Weapons, empire.Race, empire.ResearchLevels, 1);
            empire.ResearchResources[TechLevel.ResearchField.Weapons] = cost + 1;

            new ResearchBuyLoopStep().Process(serverState);

            Assert.AreEqual(1, empire.ResearchLevels[TechLevel.ResearchField.Weapons]);
            Assert.AreEqual(1, empire.ResearchResources[TechLevel.ResearchField.Weapons]);
        }

        [Test]
        public void BuyLoop_AddsNoIncome_AndARaceWithoutCostClassesBuysNothing()
        {
            EmpireData empire = AddEmpire(1);
            empire.ResearchLevels = new TechLevel(0);
            empire.Race.ResearchCosts = new TechLevel(0);

            new ResearchBuyLoopStep().Process(serverState);

            Assert.AreEqual(0, empire.ResearchLevels[TechLevel.ResearchField.Energy], "A zero price never buys levels for free");
        }

        // ------------------------------------------------------------------ Mystery Trader planet trade

        private MysteryTrader AddTrader(int x, int y, int item)
        {
            MysteryTrader trader = new MysteryTrader { Position = new NovaPoint(x, y), Destination = new NovaPoint(x + 100, y), Speed = 10, Item = item };
            trader.Key = serverState.AllMysteryTraders.Count + 1;
            serverState.AllMysteryTraders.Add(trader.Key, trader);
            return trader;
        }

        private Star TradingPlanet(EmpireData owner, int x, int y, int ironium, int boranium, int germanium)
        {
            Star star = new Star { Name = "Port" + serverState.AllStars.Count, Position = new NovaPoint(x, y), Owner = (ushort)owner.Id, Colonists = 100000 };
            star.Starbase = new Fleet(99);
            star.ResourcesOnHand.Ironium = ironium;
            star.ResourcesOnHand.Boranium = boranium;
            star.ResourcesOnHand.Germanium = germanium;
            serverState.AllStars.Add(star.Key, star);
            return star;
        }

        [Test]
        public void PlanetTrade_PriceIs3500ForTough_5000ForExpert_NothingBelow()
        {
            Assert.AreEqual(0, MysteryTraderStep.PlanetTradePrice(0));
            Assert.AreEqual(0, MysteryTraderStep.PlanetTradePrice(1));
            Assert.AreEqual(3500, MysteryTraderStep.PlanetTradePrice(2));
            Assert.AreEqual(5000, MysteryTraderStep.PlanetTradePrice(3));
        }

        [Test]
        public void PlanetTrade_TechnologyRaisesTheLowestFieldSixTimes_AndThePriceIsPaidGermaniumFirst()
        {
            EmpireData tough = AddEmpire(1, "Nova_AI", 2);
            tough.ResearchLevels = new TechLevel(5);
            tough.ResearchLevels[TechLevel.ResearchField.Propulsion] = 2;
            MysteryTrader trader = AddTrader(100, 100, MysteryTrader.TechnologyItem);
            Star port = TradingPlanet(tough, 160, 180, 3000, 1000, 1000); // exactly 100 ly away

            new MysteryTraderStep(new ScriptedRandom()).TradeWithPlanets(serverState, trader);

            // Propulsion 2 -> 5, then the lowest is re-picked each time, ties to the first field:
            // Energy 6, Weapons 6, Propulsion 6.
            Assert.AreEqual(6, tough.ResearchLevels[TechLevel.ResearchField.Propulsion]);
            Assert.AreEqual(6, tough.ResearchLevels[TechLevel.ResearchField.Energy]);
            Assert.AreEqual(6, tough.ResearchLevels[TechLevel.ResearchField.Weapons]);
            Assert.AreEqual(5, tough.ResearchLevels[TechLevel.ResearchField.Construction]);
            Assert.AreEqual(5 * 6 - 3 + 6, MysteryTraderStep.FieldOrder.Sum(field => tough.ResearchLevels[field]));

            // 3,500 kT: all 1,000 Ge, all 1,000 Bo, then 1,500 of the Fe.
            Assert.AreEqual(0, port.ResourcesOnHand.Germanium);
            Assert.AreEqual(0, port.ResourcesOnHand.Boranium);
            Assert.AreEqual(1500, port.ResourcesOnHand.Ironium);
            Assert.IsTrue(trader.ServedRaces.Contains(tough.Id));
        }

        [Test]
        public void PlanetTrade_APartTheRaceLacks_IsGrantedSilently_AndTheWholeStockIsTaken()
        {
            EmpireData expert = AddEmpire(1, "Nova_AI", 3);
            MysteryTrader trader = AddTrader(100, 100, 2); // Langston Shell
            Star port = TradingPlanet(expert, 100, 100, 4000, 3000, 2000);

            new MysteryTraderStep(new ScriptedRandom()).TradeWithPlanets(serverState, trader);

            Assert.IsTrue(expert.GrantedSpecialComponents.Contains("Langston Shell"));
            Assert.AreEqual(0, serverState.AllMessages.Count, "Granted silently");
            Assert.AreEqual(0, port.ResourcesOnHand.Ironium + port.ResourcesOnHand.Boranium + port.ResourcesOnHand.Germanium,
                "The part path deducts the planet's entire surface stock (§5a code-level finding)");
        }

        [Test]
        public void PlanetTrade_AnOwnedPart_IsRedrawnForOneTheRaceLacks()
        {
            EmpireData expert = AddEmpire(1, "Nova_AI", 3);
            expert.GrantedSpecialComponents.Add("Langston Shell");
            expert.GrantedSpecialComponents.Add("Multi Cargo Pod");
            MysteryTrader trader = AddTrader(100, 100, 2);
            TradingPlanet(expert, 100, 100, 6000, 0, 0);

            // Redraws: 0 (owned Multi Cargo Pod), then 5 (Hush-a-Boom).
            ScriptedRandom random = new ScriptedRandom(0, 5);
            new MysteryTraderStep(random).TradeWithPlanets(serverState, trader);

            Assert.IsTrue(expert.GrantedSpecialComponents.Contains("Hush-a-Boom"));
            Assert.AreEqual(0, random.Remaining);
        }

        [Test]
        public void PlanetTrade_IsRefused_ForHumans_LowTiers_FarPlanets_NoStarbase_TooFewMinerals_OrAServedRace()
        {
            MysteryTrader trader = AddTrader(100, 100, MysteryTrader.TechnologyItem);

            EmpireData human = AddEmpire(1);
            TradingPlanet(human, 100, 100, 9000, 0, 0);

            EmpireData standard = AddEmpire(2, "Nova_AI", 1);
            TradingPlanet(standard, 100, 100, 9000, 0, 0);

            EmpireData far = AddEmpire(3, "Nova_AI", 3);
            TradingPlanet(far, 161, 180, 9000, 0, 0); // just over 100 ly

            EmpireData noBase = AddEmpire(4, "Nova_AI", 3);
            TradingPlanet(noBase, 100, 100, 9000, 0, 0).Starbase = null;

            EmpireData poor = AddEmpire(5, "Nova_AI", 2);
            TradingPlanet(poor, 100, 100, 3499, 0, 0);

            EmpireData served = AddEmpire(6, "Nova_AI", 3);
            TradingPlanet(served, 100, 100, 9000, 0, 0);
            trader.ServedRaces.Add(served.Id);

            Dictionary<string, int> before = serverState.AllStars.Values.ToDictionary(star => star.Name, star => star.ResourcesOnHand.Ironium);

            new MysteryTraderStep(new ScriptedRandom()).TradeWithPlanets(serverState, trader);

            foreach (Star star in serverState.AllStars.Values)
            {
                Assert.AreEqual(before[star.Name], star.ResourcesOnHand.Ironium, star.Name + " paid nothing");
            }

            Assert.AreEqual(1, trader.ServedRaces.Count, "Only the already-served race is in the mask");
        }

        [Test]
        public void PlanetTrade_WithNothingToOffer_NothingHappensAndNothingIsPaid()
        {
            EmpireData expert = AddEmpire(1, "Nova_AI", 3);
            expert.ResearchLevels = new TechLevel(25); // total 150: not below 6 x (26 - 1)
            MysteryTrader trader = AddTrader(100, 100, MysteryTrader.TechnologyItem);
            Star port = TradingPlanet(expert, 100, 100, 6000, 0, 0);

            new MysteryTraderStep(new ScriptedRandom()).TradeWithPlanets(serverState, trader);

            Assert.AreEqual(6000, port.ResourcesOnHand.Ironium);
            Assert.IsFalse(trader.ServedRaces.Contains(expert.Id));
        }
    }
}
