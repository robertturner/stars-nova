namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Spec-driven coverage of behavior-specs-10/diplomacy-relations.md (coverage rows 1, 5, 8,
    /// 11) and the relation matrices of combat-resolution.md section 4, diplomacy-relations.md
    /// sections 4-5 and turn-generation-engine.md section 4 ("Who bombs whom"): for every Battle
    /// Plan "Attack Who" value against every relation (Enemy / Neutral / Friend / no report),
    /// only the attacker's own opinion is read; bombing uses the same rule; fleet terraforming
    /// improves only an own or Friend-rated owner's planet.
    /// </summary>
    [TestFixture]
    public class DiplomacyCoverageTest
    {
        private const ushort Attacker = 1;
        private const ushort Target = 2;

        /// <summary>The "no report on file" column: the relation byte's default (Neutral).</summary>
        private const string Missing = "Missing";

        private ServerData serverState;
        private EmpireData attacker;
        private EmpireData target;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            attacker = new SimpleEmpireData { Id = Attacker, Race = new Race { Name = "Wolves", PluralName = "Wolves" } };
            target = new SimpleEmpireData { Id = Target, Race = new Race { Name = "Lambs", PluralName = "Lambs" } };
            serverState.AllEmpires[Attacker] = attacker;
            serverState.AllEmpires[Target] = target;
        }

        /// <summary>Sets the attacker's OWN opinion of the target (or removes it).</summary>
        private void AttackerRates(string relation)
        {
            attacker.EmpireReports.Remove(Target);
            if (relation != Missing)
            {
                attacker.EmpireReports[Target] = new EmpireIntel(target) { Relation = (PlayerRelation)Enum.Parse(typeof(PlayerRelation), relation) };
            }
        }

        private void TargetRates(PlayerRelation relation)
        {
            target.EmpireReports[Attacker] = new EmpireIntel(attacker) { Relation = relation };
        }

        private void SetPlan(EmpireData empire, string attack, int targetId = 0)
        {
            empire.BattlePlans["Default"] = new BattlePlan { Attack = attack, TargetId = targetId };
        }

        // ================================================================ "Attack Who" x relation

        // Expected per diplomacy-relations.md section 4: "Nobody never bombs. Enemies bombs only
        // planets whose owner the bomber rates Enemy. Neutrals and Enemies bombs every owner
        // except those rated Friend. Everyone bombs all owners, Friends included. A
        // specific-player setting bombs only that player's planets." A missing report reads as
        // the Neutral default of a freshly initialised relation row.
        private static readonly object[] Matrix =
        {
            new object[] { "None", "Enemy", false },
            new object[] { "None", "Neutral", false },
            new object[] { "None", "Friend", false },
            new object[] { "None", Missing, false },
            new object[] { "Enemies", "Enemy", true },
            new object[] { "Enemies", "Neutral", false },
            new object[] { "Enemies", "Friend", false },
            new object[] { "Enemies", Missing, false },
            new object[] { "Enemies and Neutrals", "Enemy", true },
            new object[] { "Enemies and Neutrals", "Neutral", true },
            new object[] { "Enemies and Neutrals", "Friend", false },
            new object[] { "Enemies and Neutrals", Missing, true },
            new object[] { "Everyone", "Enemy", true },
            new object[] { "Everyone", "Neutral", true },
            new object[] { "Everyone", "Friend", true },
            new object[] { "Everyone", Missing, true },
        };

        [TestCaseSource(nameof(Matrix))]
        public void LegitimateTarget_AttackWhoTimesRelation(string attack, string relation, bool expected)
        {
            SetPlan(attacker, attack);
            AttackerRates(relation);

            Assert.AreEqual(expected, BattleEngine.IsLegitimateTarget(serverState, Attacker, "Default", Target));
        }

        [TestCase("Enemy")]
        [TestCase("Neutral")]
        [TestCase("Friend")]
        [TestCase(Missing)]
        public void LegitimateTarget_SpecificPlayer_IsThatPlayerWhateverTheRelation_AndNobodyElse(string relation)
        {
            AttackerRates(relation);

            SetPlan(attacker, BattleEngine.SpecificPlayerAttack, Target);
            Assert.IsTrue(BattleEngine.IsLegitimateTarget(serverState, Attacker, "Default", Target), "the chosen player");

            SetPlan(attacker, BattleEngine.SpecificPlayerAttack, 3);
            Assert.IsFalse(BattleEngine.IsLegitimateTarget(serverState, Attacker, "Default", Target), "another player is never a target");
        }

        [Test]
        public void LegitimateTarget_NeverOneself_EvenOnEveryone()
        {
            SetPlan(attacker, "Everyone");
            Assert.IsFalse(BattleEngine.IsLegitimateTarget(serverState, Attacker, "Default", Attacker));
        }

        [TestCase(PlayerRelation.Enemy)]
        [TestCase(PlayerRelation.Neutral)]
        [TestCase(PlayerRelation.Friend)]
        public void LegitimateTarget_OnlyTheAttackersOwnOpinionIsRead(PlayerRelation targetsOpinionOfAttacker)
        {
            // The attacker calls the target a Friend; the target's opinion of the attacker,
            // whatever it is, changes nothing.
            SetPlan(attacker, "Enemies and Neutrals");
            AttackerRates("Friend");
            TargetRates(targetsOpinionOfAttacker);

            Assert.IsFalse(BattleEngine.IsLegitimateTarget(serverState, Attacker, "Default", Target));
        }

        [Test]
        public void LegitimateTarget_IsAsymmetric_EachSideJudgesByItsOwnRow()
        {
            SetPlan(attacker, "Enemies");
            SetPlan(target, "Enemies");
            AttackerRates("Friend");
            TargetRates(PlayerRelation.Enemy);

            Assert.IsFalse(BattleEngine.IsLegitimateTarget(serverState, Attacker, "Default", Target), "the wolf rates the lamb Friend");
            Assert.IsTrue(BattleEngine.IsLegitimateTarget(serverState, Target, "Default", Attacker), "the lamb rates the wolf Enemy");
        }

        // ================================================================ bombing uses the same rule

        private Star ForeignPlanet(out Fleet bomber)
        {
            Star star = new Star { Name = "Pasture", Owner = Target, Colonists = 10000, Position = new NovaPoint(0, 0) };
            serverState.AllStars[star.Key] = star;
            bomber = new Fleet("Bomber", Attacker, 1, new NovaPoint(0, 0));
            bomber.BattlePlan = "Default";
            bomber.InOrbit = star;
            return star;
        }

        [TestCaseSource(nameof(Matrix))]
        public void Bombing_AttackWhoTimesRelation_IsTheSameRule(string attack, string relation, bool expected)
        {
            Star star = ForeignPlanet(out Fleet bomber);
            SetPlan(attacker, attack);
            AttackerRates(relation);
            TargetRates(PlayerRelation.Friend); // never read

            Assert.AreEqual(expected, new Bombing(serverState).MayBomb(bomber, star));
        }

        [Test]
        public void Bombing_SpecificPlayer_BombsOnlyThatPlayersPlanets()
        {
            Star star = ForeignPlanet(out Fleet bomber);
            AttackerRates("Friend");

            SetPlan(attacker, BattleEngine.SpecificPlayerAttack, Target);
            Assert.IsTrue(new Bombing(serverState).MayBomb(bomber, star));

            SetPlan(attacker, BattleEngine.SpecificPlayerAttack, 3);
            Assert.IsFalse(new Bombing(serverState).MayBomb(bomber, star));
        }

        [Test]
        public void Bombing_NeedsAForeignOwnedPlanetWithoutAStarbase_WhateverThePlan()
        {
            Star star = ForeignPlanet(out Fleet bomber);
            SetPlan(attacker, "Everyone");

            star.Starbase = new Fleet("Base", Target, 99, new NovaPoint(0, 0));
            Assert.IsFalse(new Bombing(serverState).MayBomb(bomber, star), "any starbase prevents bombing");

            star.Starbase = null;
            star.Owner = Global.Nobody;
            Assert.IsFalse(new Bombing(serverState).MayBomb(bomber, star), "an unowned planet");

            star.Owner = Attacker;
            Assert.IsFalse(new Bombing(serverState).MayBomb(bomber, star), "an own planet");
        }

        // ================================================================ fleet terraforming (Friend rule)

        [TestCase("Enemy", false)]
        [TestCase("Neutral", false)]
        [TestCase("Friend", true)]
        [TestCase(Missing, false)]
        public void FleetTerraforming_ImprovesOnlyAFriendRatedOwnersPlanet(string relation, bool improves)
        {
            AttackerRates(relation);
            TargetRates(PlayerRelation.Friend); // the planet owner's opinion is never read

            Assert.AreEqual(improves, FleetTerraformStep.Improves(attacker, Target));
        }

        [Test]
        public void FleetTerraforming_AlwaysImprovesTheFleetOwnersOwnPlanet()
        {
            Assert.IsTrue(FleetTerraformStep.Improves(attacker, Attacker));
        }

        // ================================================================ row 1: one value per ordered pair

        [Test]
        public void Row1_OneRelationPerOrderedPair_TheTwoDirectionsAreIndependent()
        {
            AttackerRates("Enemy");
            TargetRates(PlayerRelation.Friend);

            attacker.EmpireReports[Target].Relation = PlayerRelation.Neutral;

            Assert.AreEqual(PlayerRelation.Neutral, attacker.EmpireReports[Target].Relation);
            Assert.AreEqual(PlayerRelation.Friend, target.EmpireReports[Attacker].Relation, "B's opinion of A is a separate value");
        }

        [Test]
        public void Row1_BothDirectionsSurviveTheSaveFileRoundTrip()
        {
            EmpireData a = new EmpireData { Id = 1, Race = new Race { Name = "A" } };
            EmpireData b = new EmpireData { Id = 2, Race = new Race { Name = "B" } };
            a.EmpireReports[b.Id] = new EmpireIntel(b) { Relation = PlayerRelation.Enemy };
            b.EmpireReports[a.Id] = new EmpireIntel(a) { Relation = PlayerRelation.Friend };

            EmpireData loadedA = RoundTrip(a);
            EmpireData loadedB = RoundTrip(b);

            Assert.AreEqual(PlayerRelation.Enemy, loadedA.EmpireReports[b.Id].Relation);
            Assert.AreEqual(PlayerRelation.Friend, loadedB.EmpireReports[a.Id].Relation);
        }

        private static EmpireData RoundTrip(EmpireData empire)
        {
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Root");
            doc.AppendChild(root);
            root.AppendChild(empire.ToXml(doc));
            return new EmpireData(root.FirstChild);
        }

        // ================================================================ row 5: a queued turn order

        /// <summary>Exposes the turn generator's command-application step.</summary>
        private class CommandParsingTurnGenerator : TurnGenerator
        {
            public CommandParsingTurnGenerator(ServerData serverState)
                : base(serverState)
            {
            }

            public void ApplyCommands()
            {
                ParseCommands();
            }
        }

        [Test]
        public void Row5_ARelationChangeIsAQueuedOrder_AppliedOnlyWhenTheTurnIsGenerated()
        {
            AttackerRates("Neutral");
            TargetRates(PlayerRelation.Neutral);

            // The client's order, as it travels in the .orders file.
            RelationCommand order = new RelationCommand(Target, PlayerRelation.Enemy);
            XmlDocument doc = new XmlDocument();
            RelationCommand read = new RelationCommand(order.ToXml(doc));
            Assert.AreEqual(Target, read.TargetEmpireId);
            Assert.AreEqual(PlayerRelation.Enemy, read.NewRelation);

            Stack<ICommand> queue = new Stack<ICommand>();
            queue.Push(read);
            serverState.AllCommands[Attacker] = queue;

            Assert.AreEqual(PlayerRelation.Neutral, attacker.EmpireReports[Target].Relation, "nothing changes while the order is only queued");

            new CommandParsingTurnGenerator(serverState).ApplyCommands();

            Assert.AreEqual(PlayerRelation.Enemy, attacker.EmpireReports[Target].Relation, "applied at turn generation");
            Assert.AreEqual(PlayerRelation.Neutral, target.EmpireReports[Attacker].Relation, "only the issuer's own row is written");
        }

        [Test]
        public void Row5_ReadingTheOrdersFile_DoesNotYetChangeTheRelation()
        {
            AttackerRates("Neutral");
            string folder = Path.Combine(Path.GetTempPath(), "nova-diplomacy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                serverState.GameFolder = folder;
                serverState.TurnYear = Global.StartingYear + 3;

                XmlDocument doc = new XmlDocument();
                XmlElement root = Global.InitializeXmlDocument(doc);
                Global.SaveData(doc, root, "Turn", serverState.TurnYear.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Global.SaveData(doc, root, "Id", Attacker.ToString("X"));
                XmlElement orders = doc.CreateElement("Orders");
                root.AppendChild(orders);
                orders.AppendChild(new RelationCommand(Target, PlayerRelation.Friend).ToXml(doc));
                doc.Save(Path.Combine(folder, attacker.Race.Name + Global.OrdersExtension));

                new OrderReader(serverState).ReadOrders();

                Assert.AreEqual(PlayerRelation.Neutral, attacker.EmpireReports[Target].Relation, "reading the order does not apply it");
                Assert.IsInstanceOf<RelationCommand>(serverState.AllCommands[Attacker].Peek());

                new CommandParsingTurnGenerator(serverState).ApplyCommands();
                Assert.AreEqual(PlayerRelation.Friend, attacker.EmpireReports[Target].Relation);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void Row5_AnOrderAboutARaceWithNoReportOnFile_IsRefused()
        {
            AttackerRates(Missing);
            RelationCommand order = new RelationCommand(Target, PlayerRelation.Enemy);

            Assert.IsFalse(order.IsValid(attacker));
            order.ApplyToState(attacker);
            Assert.IsFalse(attacker.EmpireReports.ContainsKey(Target));
        }

        // ================================================================ row 8: invasion ignores relations

        [TestCase("Enemy", PlayerRelation.Enemy)]
        [TestCase("Neutral", PlayerRelation.Neutral)]
        [TestCase("Friend", PlayerRelation.Friend)]
        [TestCase(Missing, PlayerRelation.Enemy)]
        [TestCase("Friend", PlayerRelation.Enemy)]
        [TestCase("Enemy", PlayerRelation.Friend)]
        public void Row8_InvasionIsAllowedWhateverEitherSidesRelation(string invadersOpinion, PlayerRelation defendersOpinion)
        {
            // fleet-movement-scanning-cargo.md section 4, "Colonist unload outcomes": "No route
            // ever asks about diplomatic relations: unloading onto a friend's planet is an
            // invasion like any other." (The coverage row's "requires Enemy relation" is stale.)
            AttackerRates(invadersOpinion);
            TargetRates(defendersOpinion);
            Star star = new Star { Name = "Pasture", Owner = Target, Colonists = 10000 };
            Fleet fleet = new Fleet("Troops", Attacker, 1, new NovaPoint(0, 0));
            fleet.InOrbit = star;
            fleet.Cargo.ColonistsInKilotons = 10;

            Assert.IsTrue(new InvadeTask().IsValid(fleet, star, attacker, target));
        }

        // ================================================================ row 11: no AI relation orders

        private class TestableAi : DefaultAi
        {
            public TestableAi(ClientData state, int personality)
            {
                clientState = state;
                commandArguments = new CommandArguments();
                commandArguments.Add(CommandArguments.Option.AiPersonality, personality);
                AiRandom = new Random(5);
            }
        }

        private static ClientData AiGame()
        {
            ClientData clientState = new ClientData();
            clientState.EmpireState.Id = 1;
            Race race = new Race
            {
                ColonistsPerResource = 1000,
                FactoryProduction = 10,
                FactoryBuildCost = 10,
                OperableFactories = 10,
                MineBuildCost = 5,
                MineProductionRate = 10,
                OperableMines = 10,
            };
            clientState.EmpireState.Race = race;
            clientState.EmpireState.TurnYear = Global.StartingYear + 40;

            Star home = new Star { Name = "Home", Owner = 1, Position = new NovaPoint(100, 100), ThisRace = race, Colonists = 250000 };
            home.ResourcesOnHand = new Resources(5000, 5000, 5000, 0);
            clientState.EmpireState.OwnedStars.Add(home);
            clientState.EmpireState.StarReports.Add(home.Name, new StarIntel { Name = home.Name, Position = home.Position, Owner = 1 });

            ushort[] others = { 2, 3, 4 };
            PlayerRelation[] relations = { PlayerRelation.Enemy, PlayerRelation.Neutral, PlayerRelation.Friend };
            for (int i = 0; i < others.Length; i++)
            {
                EmpireData other = new EmpireData { Id = others[i], Race = new Race { Name = "Other" + others[i] } };
                clientState.EmpireState.EmpireReports[other.Id] = new EmpireIntel(other) { Relation = relations[i] };
                string name = "Their" + others[i];
                clientState.EmpireState.StarReports.Add(name, new StarIntel { Name = name, Position = new NovaPoint(130 + (20 * i), 100), Owner = other.Id, Year = clientState.EmpireState.TurnYear });
            }

            return clientState;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void Row11_NoBuiltInAiPersonality_EverSubmitsARelationChange(int personalityCode)
        {
            ClientData clientState = AiGame();
            Dictionary<ushort, PlayerRelation> before = clientState.EmpireState.EmpireReports.ToDictionary(r => r.Key, r => r.Value.Relation);

            new TestableAi(clientState, personalityCode).DoMove();

            Assert.IsFalse(clientState.Commands.Any(c => c is RelationCommand), "no relation-change order is queued");
            foreach (KeyValuePair<ushort, PlayerRelation> pair in before)
            {
                Assert.AreEqual(pair.Value, clientState.EmpireState.EmpireReports[pair.Key].Relation, "the AI never edits its relation row");
            }
        }
    }
}
