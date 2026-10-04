namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Shared helpers for the spec-10 combat tests below.
    /// </summary>
    internal static class Spec10CombatKit
    {
        /// <summary>Returns queued values for Next(min, max); the low end once the queue is empty.</summary>
        public class ScriptedRandom : Random
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

        public static BattleEngine Engine(ServerData serverState, List<Stack> stacks, Random random)
        {
            BattleReport report = new BattleReport();
            report.Losses[CombatTestKit.WolfId] = 0;
            report.Losses[CombatTestKit.LambId] = 0;
            BattleEngine engine = new BattleEngine(serverState, report, random);
            typeof(BattleEngine).GetField("currentBattleStacks", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(engine, stacks);
            return engine;
        }

        public static Component Real(string name)
        {
            Component component = new AllComponents().Fetch(name);
            Assert.IsNotNull(component, name + " must exist in components.xml");
            return component;
        }

        /// <summary>A design on a hull with explicit mass, cost, cargo and initiative.</summary>
        public static ShipDesign Design(long key, int hullMass, int hullArmor, int baseCargo, Resources cost, int hullInitiative, Race race, params (Component part, int count)[] slots)
        {
            Component blueprint = new Component { Cost = cost ?? new Resources(0, 50, 0, 50), Mass = hullMass };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000, ArmorStrength = hullArmor, BaseCargo = baseCargo, BattleInitiative = hullInitiative };
            foreach ((Component part, int count) in slots)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = count });
            }

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Name = "Design" + key, Blueprint = blueprint };
            design.Update(race);
            return design;
        }
    }

    /// <summary>
    /// Missile allotment exactly per behavior-specs-10/combat-resolution.md §6 ("Allotment
    /// arithmetic, exact"): the n-search with hits rounded up, the miss chip misses x D / 8 and the
    /// twice-truncated half of hits x D, with the surplus missiles rolling against the next target.
    /// </summary>
    [TestFixture]
    public class BattleMissileAllotmentTest
    {
        [Test]
        public void SpecExample_TwoShipTokenTakesThreeMissiles_TheOtherThreeRollAgainstTheNextTarget()
        {
            // 6 missiles of 100 roll 3 hits against a 2-ship token with 100 armor each: n = 2
            // gives 1 hit / 1 miss (estimate 100, too little), n = 3 gives 2 hits / 1 miss
            // (estimate 200): the token takes 3 and both ships die. The other 3 roll afresh.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.MissilePart(100, 50, WeaponType.torpedo), 6));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack pair = CombatTestKit.MakeStack(CombatTestKit.Design(2, 100), CombatTestKit.LambId, new Point(0, 0), 2, quantity: 2);
            Stack next = CombatTestKit.MakeStack(CombatTestKit.Design(3, 1000), CombatTestKit.LambId, new Point(0, 0), 3);

            // First roll: 3 of 6 below 50. Second roll (3 missiles left): all hit.
            Random random = new Spec10CombatKit.ScriptedRandom(0, 0, 0, 99, 99, 99, 0, 0, 0);
            BattleEngine engine = Spec10CombatKit.Engine(serverState, new List<Stack> { wolf, pair, next }, random);

            CombatTestKit.Fire(engine, wolf, pair, wolfDesign.Weapons[0]);

            Assert.IsTrue(pair.IsDestroyed);
            Assert.AreEqual(1000 - 300, next.Token.Armor, 1e-9, "The 3 unallotted missiles hit the next most attractive token in reach");
        }

        [Test]
        public void MissChip_MultipliesBeforeDividingByEight()
        {
            // 4 misses of 12 damage: 4 x 12 / 8 = 6, not 4 x (12 / 8) = 4.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.MissilePart(12, 50, WeaponType.torpedo), 4));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 1000, (CombatTestKit.ShieldPart(100), 1)), CombatTestKit.LambId, new Point(0, 0), 2);
            BattleEngine engine = Spec10CombatKit.Engine(serverState, new List<Stack> { wolf, lamb }, new Spec10CombatKit.ScriptedRandom(99, 99, 99, 99));

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(94, lamb.Token.Shields, 1e-9);
            Assert.AreEqual(1000, lamb.Token.Armor, 1e-9, "A miss never touches armor");
        }

        [Test]
        public void OddHitDamage_LosesOnePoint_BecauseTheHalfIsTruncatedTwice()
        {
            // 3 torpedo hits of 85 = 255: 127 offered to the (empty) shields, then 127 more - 254.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.MissilePart(85, 100, WeaponType.torpedo), 3));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 1000), CombatTestKit.LambId, new Point(0, 0), 2);
            BattleEngine engine = Spec10CombatKit.Engine(serverState, new List<Stack> { wolf, lamb }, new Spec10CombatKit.ScriptedRandom());

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(1000 - 254, lamb.Token.Armor, 1e-9);
        }

        [Test]
        public void TokenWithAtLeastAsManyShipsAsMissiles_TakesTheWholeSalvo()
        {
            // 3 missiles at a 5-ship token: no search, all 3 allotted, kill limit 3.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.MissilePart(1000, 100, WeaponType.torpedo), 3));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 100), CombatTestKit.LambId, new Point(0, 0), 2, quantity: 5);
            Stack bystander = CombatTestKit.MakeStack(CombatTestKit.Design(3, 1000), CombatTestKit.LambId, new Point(0, 0), 3);
            BattleEngine engine = Spec10CombatKit.Engine(serverState, new List<Stack> { wolf, lamb, bystander }, new Spec10CombatKit.ScriptedRandom());

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(2, lamb.Token.Quantity);
            Assert.AreEqual(1000, bystander.Token.Armor, 1e-9);
        }
    }

    /// <summary>
    /// Target score scaling, behavior-specs-10/combat-resolution.md §4: whole-token cost
    /// (resources + boranium) x ships, x100 below 100,000 else fixed at 10,000,000; beam score
    /// 100 x cost x deflector% / 100 / (A + S + 1); sapper ceil(100 x cost / S); at least 1.
    /// </summary>
    [TestFixture]
    public class BattleTargetScoreScalingTest
    {
        private static double Score(ShipDesign lambDesign, int quantity, WeaponType group = WeaponType.standardBeam)
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(10, 2, group), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(lambDesign, CombatTestKit.LambId, new Point(1, 0), 2, quantity);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });
            return engine.GetAttractiveness(wolf, lamb);
        }

        [Test]
        public void StandardBeam_UsesTheWholeTokensScaledCost()
        {
            // cost (50 + 50) x 3 ships = 300 -> x100 = 30,000; armor 3 x 100 = 300:
            // 100 x 30,000 / 301 = 9,966.
            Assert.AreEqual(100L * 30000 / 301, Score(CombatTestKit.Design(2, 100), 3));
        }

        [Test]
        public void CostOfAHundredThousandOrMore_IsFixedAtTenMillion()
        {
            // 1,000 ships x 100 = 100,000 -> 10,000,000; armor 100,000: 1e9 / 100,001 = 9,999.
            Assert.AreEqual(1000000000L / 100001, Score(CombatTestKit.Design(2, 100), 1000));
        }

        [Test]
        public void EveryScore_IsAtLeastOne()
        {
            Assert.AreEqual(1, Score(CombatTestKit.Design(2, 2000000000), 1));
        }

        [Test]
        public void Sapper_IsCeilOfHundredTimesScaledCostOverShields()
        {
            // 10,000 scaled cost, 30 shields: ceil(1,000,000 / 30) = 33,334.
            Assert.AreEqual(33334, Score(CombatTestKit.Design(2, 100, (CombatTestKit.ShieldPart(30), 1)), 1, WeaponType.shieldSapper));
        }
    }

    /// <summary>
    /// Initiative cap 63 and the truncating computer accuracy walk (combat-resolution.md §5, §7a).
    /// </summary>
    [TestFixture]
    public class BattleInitiativeAndComputerTest
    {
        [Test]
        public void TokenInitiative_IsCappedAt63()
        {
            ShipDesign design = Spec10CombatKit.Design(1, 100, 100, 0, null, 60, null, (Spec10CombatKit.Real("Battle Nexus"), 2));

            Assert.AreEqual(63, design.Initiative, "60 + 2 x 3 = 66, capped at 63");
        }

        [Test]
        public void SlotBracket_IsTokenPlusWeaponInitiative_CappedAt63()
        {
            ShipDesign design = Spec10CombatKit.Design(1, 100, 100, 0, null, 55, null, (CombatTestKit.BeamPart(10, 1), 1));
            Weapon weapon = new Weapon { Power = 10, Range = 1, Accuracy = 100, Initiative = 12, Group = WeaponType.standardBeam };
            Stack stack = CombatTestKit.MakeStack(design, CombatTestKit.WolfId, new Point(0, 0), 1);

            Assert.AreEqual(63, new WeaponDetails { SourceStack = stack, Weapon = weapon }.TotalInitiative);
        }

        [Test]
        public void ThreeBattleComputers_Give48_TruncatingEachStep()
        {
            ShipDesign design = Spec10CombatKit.Design(1, 100, 100, 0, null, 0, null, (Spec10CombatKit.Real("Battle Computer"), 3));

            Assert.AreEqual(48, design.ComputerAccuracy, 1e-9, "20 -> 36 -> 48 (36 + 12.8 truncated)");
            Assert.AreEqual(3, design.Initiative);
        }

        [Test]
        public void MultiContainedMunition_RaisesAccuracyByAFlatTen_WithNoInitiative()
        {
            ShipDesign munitionOnly = Spec10CombatKit.Design(1, 100, 100, 0, null, 0, null, (Spec10CombatKit.Real("Multi Contained Munition"), 1));
            ShipDesign withComputer = Spec10CombatKit.Design(2, 100, 100, 0, null, 0, null,
                (Spec10CombatKit.Real("Battle Computer"), 1), (Spec10CombatKit.Real("Multi Contained Munition"), 1));

            Assert.AreEqual(10, munitionOnly.ComputerAccuracy, 1e-9);
            Assert.AreEqual(0, munitionOnly.Initiative, "The Munition is not an initiative trigger");
            Assert.AreEqual(28, withComputer.ComputerAccuracy, 1e-9, "20 + 80 x 10 / 100");
        }
    }

    /// <summary>
    /// Battle movement, behavior-specs-10/combat-resolution.md §5 (FUN_10f0_2184), including the
    /// dump-cargo worked example.
    /// </summary>
    [TestFixture]
    public class BattleMovementTest
    {
        private static ShipDesign MediumFreighter(long key, Race race = null)
        {
            // Hull mass 60, cargo 210, one Long Hump 6 (mass 9): design mass 69.
            return Spec10CombatKit.Design(key, 60, 50, 210, null, 0, race, (Spec10CombatKit.Real("Long Hump 6"), 1));
        }

        [Test]
        public void WorkedExample_DesignCard()
        {
            ShipDesign design = MediumFreighter(1);

            Assert.AreEqual(69, design.Mass);
            Assert.AreEqual(2, design.BattleMovement, "Long Hump 6 base 6 (first fuel entry below 121 from warp 9 down), minus 4");
            Assert.AreEqual(1.0, design.BattleSpeed, 1e-9, "(2 + 2) quarter-squares");
        }

        [TestCase(200, false, 0)]
        [TestCase(0, true, 1)]
        [TestCase(20, false, 1)]
        [TestCase(0, false, 2)]
        public void WorkedExample_CargoAndDump(int cargoShare, bool dumped, int expected)
        {
            Assert.AreEqual(expected, MediumFreighter(1).BattleMovementFor(cargoShare, dumped, false));
        }

        [Test]
        public void WarMonger_AddsTwo_InBattleButNotOnTheCard()
        {
            Race warMonger = new Race();
            warMonger.Traits.SetPrimary("WM");
            ShipDesign design = MediumFreighter(1, warMonger);

            Assert.AreEqual(2, design.BattleMovement);
            Assert.AreEqual(4, design.BattleMovementFor(0, false, true));
        }

        [Test]
        public void BaseTenEngines_AndTheComponentBonuses()
        {
            // Galaxy Scoop base 10; +1 Maneuvering Jet, +2 Overthruster, +1 Multi Function Pod,
            // + ceil((0 + 1 Alien Miner) / 2) = 1; -4; mass / 70 truncated.
            ShipDesign design = Spec10CombatKit.Design(1, 10, 50, 0, null, 0, null,
                (Spec10CombatKit.Real("Galaxy Scoop"), 1),
                (Spec10CombatKit.Real("Maneuvering Jet"), 1),
                (Spec10CombatKit.Real("Overthruster"), 1),
                (Spec10CombatKit.Real("Multi Function Pod"), 1),
                (Spec10CombatKit.Real("Alien Miner"), 1));
            int massPenalty = design.Mass / 70;

            Assert.AreEqual(Math.Min(8, 10 + 1 + 2 + 1 + 1 - 4 - massPenalty), design.BattleMovement);
        }

        [Test]
        public void MassPenalty_IsDividedByTheEngineCount()
        {
            // A 200 kT hull: with two Long Hump 6 the mass term is mass / 70 / 2 = 1 (6 - 4 - 1);
            // with one it is 209 / 70 = 2 (6 - 4 - 2).
            ShipDesign twoEngines = Spec10CombatKit.Design(1, 200, 50, 0, null, 0, null, (Spec10CombatKit.Real("Long Hump 6"), 2));
            ShipDesign oneEngine = Spec10CombatKit.Design(2, 200, 50, 0, null, 0, null, (Spec10CombatKit.Real("Long Hump 6"), 1));

            Assert.AreEqual(2 - (twoEngines.Mass / 70 / 2), twoEngines.BattleMovement);
            Assert.AreEqual(1, twoEngines.BattleMovement);
            Assert.AreEqual(0, oneEngine.BattleMovement);
        }

        [Test]
        public void DumpCargo_AtTokenSetup_EmptiesTheMineralHolds_AndCostsCargoTokensOneMovement()
        {
            // The worked example fleet: two Medium Freighters with 300 kT ironium and 100 kT
            // boranium. Dumping in deep space: the 400 kT become wreckage, colonists stay, and
            // the movement is 2 - 1 - 69/70 = 1 instead of 0.
            ServerData serverState = CombatTestKit.TwoEmpires();
            serverState.AllEmpires[CombatTestKit.WolfId].BattlePlans["Default"].DumpCargo = true;
            ShipDesign design = MediumFreighter(1);
            Fleet fleet = new Fleet("Freighters", CombatTestKit.WolfId, 1, new Point(10, 10));
            ShipToken token = new ShipToken(design, 2);
            fleet.Composition.Add(token.Key, token);
            fleet.Cargo.Ironium = 300;
            fleet.Cargo.Boranium = 100;
            fleet.Cargo.ColonistsInKilotons = 1; // stays aboard and still counts (1 x 210 / 420 = 0 kT each)
            serverState.AllEmpires[CombatTestKit.WolfId].OwnedFleets.Add(fleet);

            BattleEngine engine = new BattleEngine(serverState, new BattleReport());
            List<Stack> stacks = engine.GenerateStacks(new List<Fleet> { fleet });
            CombatTestKit.Invoke(engine, "DumpCargo", stacks);
            CombatTestKit.Invoke(engine, "SetUpTokenMovement", stacks);

            Assert.AreEqual(0, fleet.Cargo.Ironium + fleet.Cargo.Boranium + fleet.Cargo.Germanium);
            Assert.AreEqual(1, fleet.Cargo.ColonistsInKilotons, "Colonists are never dumped");
            DeepSpaceMinerals wreckage = serverState.AllDeepSpaceMinerals.Values.Single();
            Assert.AreEqual(300, wreckage.Minerals.Ironium);
            Assert.AreEqual(100, wreckage.Minerals.Boranium);
            Assert.AreEqual(1, (int)CombatTestKit.Invoke(engine, "TokenMovement", stacks[0]));
        }

        [Test]
        public void WithoutTheDumpOption_TheCargoShareSlowsTheToken()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign design = MediumFreighter(1);
            Fleet fleet = new Fleet("Freighters", CombatTestKit.WolfId, 1, new Point(10, 10));
            ShipToken token = new ShipToken(design, 2);
            fleet.Composition.Add(token.Key, token);
            fleet.Cargo.Ironium = 300;
            fleet.Cargo.Boranium = 100;
            serverState.AllEmpires[CombatTestKit.WolfId].OwnedFleets.Add(fleet);

            BattleEngine engine = new BattleEngine(serverState, new BattleReport());
            List<Stack> stacks = engine.GenerateStacks(new List<Fleet> { fleet });
            CombatTestKit.Invoke(engine, "DumpCargo", stacks);
            CombatTestKit.Invoke(engine, "SetUpTokenMovement", stacks);

            Assert.AreEqual(300, fleet.Cargo.Ironium);
            Assert.AreEqual(0, (int)CombatTestKit.Invoke(engine, "TokenMovement", stacks[0]), "2 - (69 + 200) / 70 = -1, clamped to 0");
        }
    }

    /// <summary>
    /// Salvage, behavior-specs-10/combat-resolution.md §7 correction (FUN_10f0_50e0): ships lost
    /// x mineral cost / 3 plus the destroyed ships' cargo, x 8/10 over a planet with a starbase,
    /// 5/10 over one without, 3/4 in deep space; Energy is not salvaged.
    /// </summary>
    [TestFixture]
    public class BattleSalvageTest
    {
        private ServerData serverState;
        private Fleet lambFleet;

        private void SetUp(Star star, int lambIronium)
        {
            serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 100, 1000, 0, null, 0, null, (CombatTestKit.BeamPart(100000, 10), 1));
            ShipDesign lambDesign = Spec10CombatKit.Design(2, 100, 50, 100, new Resources(30, 60, 90, 50), 0, null);

            Point position = new Point(100, 100);
            Fleet wolfFleet = new Fleet("Wolf", CombatTestKit.WolfId, 1, position);
            ShipToken wolfToken = new ShipToken(wolfDesign, 1);
            wolfFleet.Composition.Add(wolfToken.Key, wolfToken);
            lambFleet = new Fleet("Lamb", CombatTestKit.LambId, 1, position);
            ShipToken lambToken = new ShipToken(lambDesign, 1);
            lambFleet.Composition.Add(lambToken.Key, lambToken);
            lambFleet.Cargo.Ironium = lambIronium;

            if (star != null)
            {
                star.Position = new NovaPoint(position.X, position.Y);
                serverState.AllStars[star.Name] = star;
                wolfFleet.InOrbit = star;
                lambFleet.InOrbit = star;
            }

            serverState.AllEmpires[CombatTestKit.WolfId].OwnedFleets.Add(wolfFleet);
            serverState.AllEmpires[CombatTestKit.LambId].OwnedFleets.Add(lambFleet);

            new BattleEngine(serverState, new BattleReport()).Run();
            Assert.IsFalse(serverState.AllEmpires[CombatTestKit.LambId].OwnedFleets.ContainsKey(lambFleet.Key), "The lamb must have been destroyed");
        }

        [Test]
        public void DeepSpace_ThreeQuartersOfAThird_PlusTheCargo_NoEnergy()
        {
            SetUp(null, 100);

            Resources wreck = serverState.AllDeepSpaceMinerals.Values.Single().Minerals;
            Assert.AreEqual(110 - (110 / 4), wreck.Ironium, "30 / 3 + 100 cargo, minus a quarter");
            Assert.AreEqual(20 - (20 / 4), wreck.Boranium);
            Assert.AreEqual(30 - (30 / 4), wreck.Germanium);
            Assert.AreEqual(0, wreck.Energy, "Energy is not salvaged");
        }

        [Test]
        public void OverAPlanetWithoutAStarbase_FiveTenths()
        {
            Star star = new Star { Name = "Battlefield" };
            SetUp(star, 0);

            Assert.AreEqual(5, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(10, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(15, star.ResourcesOnHand.Germanium);
            Assert.AreEqual(0, star.ResourcesOnHand.Energy);
        }

        [Test]
        public void OverAPlanetWithAStarbase_EightTenths()
        {
            Star star = new Star { Name = "Battlefield" };
            star.Starbase = new Fleet("Base", 3, 1, new Point(0, 0));
            SetUp(star, 0);

            Assert.AreEqual(8, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(16, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(24, star.ResourcesOnHand.Germanium);
        }
    }
}
