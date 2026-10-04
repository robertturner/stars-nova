namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Orbital bombardment, complete rule - behavior-specs-10/turn-generation-engine.md §4
    /// (FUN_10f0_6ea2 and its gate, totaller and defence routine), including the spec's worked
    /// example.
    /// </summary>
    [TestFixture]
    public class BombingRuleTest
    {
        private const ushort BomberId = 1;
        private const ushort OwnerId = 2;

        /// <summary>Returns queued values for Next(min, max); the low end once the queue is empty.</summary>
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> values;

            public ScriptedRandom(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public override int Next(int minValue, int maxValue)
            {
                return values.Count > 0 ? values.Dequeue() : minValue;
            }

            public override int Next(int maxValue)
            {
                return Next(0, maxValue);
            }
        }

        private ServerData serverState;
        private Star star;
        private uint nextFleetId = 1;

        [SetUp]
        public void Init()
        {
            serverState = new ServerData();
            EmpireData bomber = new EmpireData { Id = BomberId };
            EmpireData owner = new EmpireData { Id = OwnerId };
            serverState.AllEmpires[bomber.Id] = bomber;
            serverState.AllEmpires[owner.Id] = owner;
            bomber.EmpireReports.Add(owner.Id, new EmpireIntel(owner) { Relation = PlayerRelation.Enemy });
            owner.EmpireReports.Add(bomber.Id, new EmpireIntel(bomber) { Relation = PlayerRelation.Enemy });
            bomber.BattlePlans["Default"] = new BattlePlan { Attack = "Enemies" };

            // The spec's worked example planet: 250,000 colonists (P = 2,500), 100 mines, 150
            // factories, 50 working Missile Batteries (c = 20).
            star = new Star { Name = "Target", Owner = OwnerId, Colonists = 250000, Mines = 100, Factories = 150, DefenseType = "Missile" };
            star.Defenses = 50;
            serverState.AllStars[star.Name] = star;
            owner.OwnedStars.Add(star);
        }

        private static Component Part(string name)
        {
            Component component = new AllComponents().Fetch(name);
            Assert.IsNotNull(component, name + " must exist in components.xml");
            return component;
        }

        private Fleet AddFleet(ushort owner, int ships, params (Component part, int count)[] slots)
        {
            ShipDesign design = CombatTestKit.Design(nextFleetId, 100, null, false, slots);
            Fleet fleet = new Fleet("Bomber fleet " + nextFleetId, owner, nextFleetId, star.Position);
            nextFleetId++;
            ShipToken token = new ShipToken(design, ships);
            fleet.Composition.Add(token.Key, token);
            fleet.InOrbit = star;
            serverState.AllEmpires[owner].OwnedFleets.Add(fleet);
            return fleet;
        }

        private List<Message> MessagesFor(int audience)
        {
            return serverState.AllMessages.Where(m => m.Audience == audience).ToList();
        }

        [Test]
        public void ComponentData_MatchesTheSpecBombTable()
        {
            // Lady Finger kills 6 tenths of a percent (components.xml had 0.2); Hush-a-Boom adds
            // no minimum kill (it had 300 colonists).
            Assert.AreEqual(0.6, ((Bomb)Part("Lady Finger Bomb").Properties["Bomb"]).PopKill, 1e-9);
            Assert.AreEqual(0, ((Bomb)Part("Hush-a-Boom").Properties["Bomb"]).MinimumKill);
        }

        [Test]
        public void WorkedExample_Totals()
        {
            Fleet fleet = AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            Bombing.BombTotals totals = Bombing.Totals(new[] { fleet });

            Assert.AreEqual(250, totals.NormalKill, "N = 25 x 10");
            Assert.AreEqual(30, totals.MinimumKill, "M = 3 x 10 units");
            Assert.AreEqual(100, totals.Installations, "D = 10 x 10");
            Assert.AreEqual(0, totals.SmartKill);
        }

        [Test]
        public void WorkedExample_WithDefences_LowRolls()
        {
            AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            // Population draw 999 > 500: no extra unit; defence draw 299 >= 100: no extra one.
            new Bombing(serverState, new ScriptedRandom(999, 299)).BombAll();

            // F = 0.98^50: N 91, M 11, D 68. 2,500 x 91 / 1000 = 227.5 -> 227 units.
            Assert.AreEqual(250000 - 22700, star.Colonists);
            Assert.AreEqual(150 - 34, star.Factories, "150 x 68 / 300 = 34 exactly");
            Assert.AreEqual(50 - 11, star.Defenses, "50 x 68 / 300 = 11.33 -> 11");
            Assert.AreEqual(100 - 23, star.Mines, "68 - 34 - 11");

            Message bomber = MessagesFor(BomberId).Single();
            Message owner = MessagesFor(OwnerId).Single();
            StringAssert.Contains("68 installations", bomber.Text);
            StringAssert.Contains("63.58%", bomber.Text, "(1 - F) x 10,000 = 6,358 hundredths of a percent stopped");
            StringAssert.Contains("63.58%", owner.Text);
        }

        [Test]
        public void WorkedExample_WithDefences_HighRolls()
        {
            AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            // Draw 500 <= remainder 500 adds the unit (probability 501/1000); defence draw 0 < 100.
            new Bombing(serverState, new ScriptedRandom(500, 0)).BombAll();

            Assert.AreEqual(250000 - 22800, star.Colonists);
            Assert.AreEqual(50 - 12, star.Defenses);
            Assert.AreEqual(100 - 22, star.Mines);
        }

        [Test]
        public void WorkedExample_WithoutDefences()
        {
            // The owner has no defence technology (c = 0), so F = 1: 625 units and 100
            // installations (50 factories, 17 defences with the remainder rolled up, 33 mines).
            star.DefenseType = "None";
            AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            new Bombing(serverState, new ScriptedRandom(0)).BombAll();

            Assert.AreEqual(250000 - 62500, star.Colonists);
            Assert.AreEqual(150 - 50, star.Factories);
            Assert.AreEqual(50 - 17, star.Defenses);
            Assert.AreEqual(100 - 33, star.Mines);
            StringAssert.DoesNotContain("defenses", MessagesFor(BomberId).Single().Text, "No defence variant when F is exactly 1");
        }

        [Test]
        public void DefenceCoverage_UsesTheOwnersBestDefenceTechnology()
        {
            EmpireData owner = serverState.AllEmpires[OwnerId];
            owner.AvailableComponents.Add(Part("SDI"));
            Assert.AreEqual(10, Bombing.DefenceCoverage(star, owner));
            owner.AvailableComponents.Add(Part("Missile Battery"));
            owner.AvailableComponents.Add(Part("Laser Battery"));
            Assert.AreEqual(24, Bombing.DefenceCoverage(star, owner));
            owner.AvailableComponents.Add(Part("Neutron Shield"));
            Assert.AreEqual(38, Bombing.DefenceCoverage(star, owner));
        }

        [Test]
        public void SameOwnerFleets_ArePooled_AndOnlyTheFirstPlanIsTested()
        {
            // Two single-ship fleets with 2 Cherry Bombs each, no defences, P = 1,000 units.
            // Pooled: N = 100, so exactly 100 units die. Bombing one fleet at a time would kill
            // 50 and then 47 or 48.
            star.DefenseType = "None";
            star.Colonists = 100000;
            AddFleet(BomberId, 1, (Part("Cherry Bomb"), 2));
            Fleet second = AddFleet(BomberId, 1, (Part("Cherry Bomb"), 2));
            serverState.AllEmpires[BomberId].BattlePlans["Passive"] = new BattlePlan { Attack = "None" };
            second.BattlePlan = "Passive";

            new Bombing(serverState, new ScriptedRandom(999, 999, 999)).BombAll();

            Assert.AreEqual(100000 - 10000, star.Colonists);
            Assert.AreEqual(1, MessagesFor(BomberId).Count, "One pooled bombardment, one message");
            StringAssert.StartsWith("Your fleets", MessagesFor(BomberId)[0].Text, "The several-fleets message set");
        }

        [Test]
        public void SmartBombs_Compound_AndAreNoLongerIgnored()
        {
            // 10 Smart Bombs (1.3%): survival 0.987^10 = 0.8774, S = 123 (rounded to nearest).
            star.DefenseType = "None";
            star.Colonists = 100000;
            star.Mines = 0;
            star.Factories = 0;
            star.Defenses = 0;
            Fleet fleet = AddFleet(BomberId, 1, (Part("Smart Bomb"), 10));

            Assert.AreEqual(123, Bombing.Totals(new[] { fleet }).SmartKill);

            new Bombing(serverState, new ScriptedRandom()).BombAll();

            Assert.AreEqual(100000 - 12300, star.Colonists, "1,000 x 123 / 1000 smart deaths");
        }

        [Test]
        public void SmartBombsAlone_NeverKillTheLastHundredColonists()
        {
            star.DefenseType = "None";
            star.Colonists = 100;
            Fleet fleet = AddFleet(BomberId, 50, (Part("Annihilator Bomb"), 9));

            new Bombing(serverState, new ScriptedRandom()).BombAll();

            Assert.AreEqual(100, star.Colonists);
            Assert.AreEqual(OwnerId, star.Owner);
        }

        [Test]
        public void OrbitalConstructionModule_CountsAsABomber_WithAMinimumOfTwentyUnits()
        {
            star.DefenseType = "None";
            star.Colonists = 100000;
            Fleet fleet = AddFleet(BomberId, 1, (Part("Orbital Construction Module"), 1));

            Assert.AreEqual(20, Bombing.Totals(new[] { fleet }).MinimumKill);

            new Bombing(serverState, new ScriptedRandom()).BombAll();

            Assert.AreEqual(100000 - 2000, star.Colonists);
        }

        [Test]
        public void MultiContainedMunition_AddsTwentyKillThreeMinimumFiveInstallations()
        {
            Fleet fleet = AddFleet(BomberId, 2, (Part("Multi Contained Munition"), 1));

            Bombing.BombTotals totals = Bombing.Totals(new[] { fleet });

            Assert.AreEqual(40, totals.NormalKill);
            Assert.AreEqual(6, totals.MinimumKill);
            Assert.AreEqual(10, totals.Installations);
        }

        [Test]
        public void RetroBombs_UndoTerraformingTowardTheOriginalValues()
        {
            star.DefenseType = "None";
            star.Gravity = 60;
            star.OriginalGravity = 50;
            star.Temperature = 40;
            star.OriginalTemperature = 45;
            star.Radiation = 50;
            star.OriginalRadiation = 50;
            AddFleet(BomberId, 1, (Part("Retro Bomb"), 3));

            new Bombing(serverState, new ScriptedRandom()).BombAll();

            Assert.AreEqual(57, star.Gravity);
            Assert.AreEqual(43, star.Temperature);
            Assert.AreEqual(50, star.Radiation);
            Assert.AreEqual(250000, star.Colonists, "Retro Bombs kill nobody");
            StringAssert.Contains("retro-bombed", MessagesFor(BomberId).Single().Text);
            StringAssert.Contains("undoing 6", MessagesFor(OwnerId).Single().Text, "3 + 3 + 0 points moved over the three axes");
        }

        [Test]
        public void DepopulatedPlanet_FormerOwnerStillGetsTheMessage()
        {
            // Previously the owner message was addressed after Owner = Nobody.
            star.DefenseType = "None";
            star.Colonists = 1000;
            AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            new Bombing(serverState, new ScriptedRandom()).BombAll();

            Assert.AreEqual(0, star.Colonists);
            Assert.AreEqual(Global.Nobody, star.Owner);
            Assert.AreEqual(1, MessagesFor(OwnerId).Count, "The former owner is told");
            Assert.AreEqual(0, MessagesFor(Global.Nobody).Count);
            Assert.IsFalse(serverState.AllEmpires[OwnerId].OwnedStars.ContainsKey(star.Name));
        }

        [Test]
        public void AnyStarbase_PreventsAllBombing()
        {
            star.Starbase = new Fleet("Base", OwnerId, 99, star.Position);
            AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            new Bombing(serverState, new ScriptedRandom()).BombAll();

            Assert.AreEqual(250000, star.Colonists);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [Test]
        public void AttackNobody_NeverBombs_EvenWithAStaleTargetId()
        {
            serverState.AllEmpires[BomberId].BattlePlans["Default"] = new BattlePlan { Attack = "None", TargetId = OwnerId };
            AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            new Bombing(serverState, new ScriptedRandom()).BombAll();

            Assert.AreEqual(250000, star.Colonists);
        }

        [Test]
        public void TheGate_UsesTheBombersOwnOpinionOfThePlanetOwner()
        {
            // The bomber rates the owner a Friend; the owner rates the bomber an Enemy.
            serverState.AllEmpires[BomberId].EmpireReports[OwnerId].Relation = PlayerRelation.Friend;
            serverState.AllEmpires[BomberId].BattlePlans["Default"] = new BattlePlan { Attack = "Enemies and Neutrals" };
            AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            new Bombing(serverState, new ScriptedRandom()).BombAll();
            Assert.AreEqual(250000, star.Colonists, "Neutrals and Enemies: never a Friend");

            serverState.AllEmpires[BomberId].BattlePlans["Default"] = new BattlePlan { Attack = "Everyone" };
            new Bombing(serverState, new ScriptedRandom(999, 299)).BombAll();
            Assert.Less(star.Colonists, 250000, "Everyone: Friends included");
        }

        [Test]
        public void OwnAndUnownedPlanets_AreNeverBombed()
        {
            serverState.AllEmpires[BomberId].BattlePlans["Default"] = new BattlePlan { Attack = "Everyone" };
            star.Owner = BomberId;
            AddFleet(BomberId, 5, (Part("Cherry Bomb"), 2));

            new Bombing(serverState, new ScriptedRandom()).BombAll();
            Assert.AreEqual(250000, star.Colonists);

            star.Owner = Global.Nobody;
            new Bombing(serverState, new ScriptedRandom()).BombAll();
            Assert.AreEqual(250000, star.Colonists);
        }
    }
}
