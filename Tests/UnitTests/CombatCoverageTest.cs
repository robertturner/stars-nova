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
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Shared helpers for the combat coverage and whole-battle conformance tests below
    /// (behavior-specs-10/combat-resolution.md). Every expected number in these tests is worked
    /// out by hand from the spec text cited next to it, not read back from the engine.
    /// </summary>
    internal static class CombatCoverageKit
    {
        public const int ThirdId = 3;

        /// <summary>
        /// Next(min, max) returns the queued values (the low end once empty, or max - 1 when
        /// built with <c>high: true</c>); NextDouble always returns 0.999, so the near-parity
        /// movement swap (BattleEngine.MovementOrder) never fires.
        /// </summary>
        public class ScriptedRandom : Random
        {
            private readonly Queue<int> values;
            private readonly bool high;

            public ScriptedRandom(params int[] values)
                : this(false, values)
            {
            }

            public ScriptedRandom(bool high, params int[] values)
            {
                this.values = new Queue<int>(values);
                this.high = high;
            }

            public override int Next(int minValue, int maxValue)
            {
                if (values.Count > 0)
                {
                    return values.Dequeue();
                }

                return high ? Math.Max(minValue, maxValue - 1) : minValue;
            }

            public override int Next(int maxValue)
            {
                return Next(0, maxValue);
            }

            public override int Next()
            {
                return Next(0, int.MaxValue);
            }

            public override double NextDouble()
            {
                return 0.999;
            }
        }

        public static BattleEngine Engine(ServerData serverState, List<Stack> stacks, Random random, BattleReport report)
        {
            foreach (int id in serverState.AllEmpires.Keys)
            {
                report.Losses[id] = 0;
            }

            BattleEngine engine = new BattleEngine(serverState, report, random);
            typeof(BattleEngine).GetField("currentBattleStacks", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(engine, stacks);
            return engine;
        }

        /// <summary>One round of firing exactly as DoBattle runs it, minus movement: targets,
        /// the initiative-sorted slot queue, then every slot in turn.</summary>
        public static void FireRound(BattleEngine engine, List<Stack> stacks)
        {
            engine.SelectTargets(stacks);
            List<WeaponDetails> attacks = (List<WeaponDetails>)CombatTestKit.Invoke(engine, "GenerateAttacks", stacks);
            foreach (WeaponDetails attack in attacks)
            {
                CombatTestKit.Invoke(engine, "ProcessAttack", attack);
            }
        }

        public static void At(Stack stack, int x, int y)
        {
            stack.Position = new NovaPoint(x, y);
        }

        public static T Field<T>(BattleEngine engine, string name)
        {
            return (T)typeof(BattleEngine).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(engine);
        }

        public static void SetField(BattleEngine engine, string name, object value)
        {
            typeof(BattleEngine).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(engine, value);
        }

        /// <summary>A real fleet registered with its owner, at a position, one design.</summary>
        public static Fleet AddFleet(ServerData serverState, int owner, uint id, Point position, ShipDesign design, int ships = 1)
        {
            Fleet fleet = new Fleet("Fleet " + owner + "-" + id, (ushort)owner, id, position);
            ShipToken token = new ShipToken(design, ships);
            fleet.Composition.Add(token.Key, token);
            serverState.AllEmpires[owner].OwnedFleets.Add(fleet);
            return fleet;
        }

        public static void AddThirdEmpire(ServerData serverState, PlayerRelation opinionOfOthers, PlayerRelation othersOpinionOfIt)
        {
            EmpireData third = new EmpireData { Id = ThirdId };
            serverState.AllEmpires[third.Id] = third;
            third.BattlePlans["Default"] = new BattlePlan { Attack = "Enemies", PrimaryTarget = "Any", SecondaryTarget = "Any" };
            foreach (EmpireData other in serverState.AllEmpires.Values.Where(e => e.Id != ThirdId).ToList())
            {
                third.EmpireReports.Add(other.Id, new EmpireIntel(other) { Relation = opinionOfOthers });
                other.EmpireReports.Add(third.Id, new EmpireIntel(third) { Relation = othersOpinionOfIt });
            }
        }

        public static Component Real(string name)
        {
            return Spec10CombatKit.Real(name);
        }
    }

    /// <summary>
    /// Coverage row 1 (combat-resolution.md §1): a battle is generated only where two races are
    /// stacked AND at least one carries orders to attack the other; ship types are irrelevant;
    /// a fleet with no attack order against anyone there, that nobody there has orders to
    /// attack, sits the battle out entirely. Also §5 dump cargo: "a fleet of a race not drawn
    /// into the fight ... keeps its cargo".
    /// </summary>
    [TestFixture]
    public class BattleTriggerCoverageTest
    {
        private ServerData serverState;
        private Fleet wolf;
        private Fleet lamb;

        [SetUp]
        public void Init()
        {
            serverState = CombatTestKit.TwoEmpires();
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                empire.BattlePlans["Default"].Attack = "Enemies";
                foreach (EmpireIntel report in empire.EmpireReports.Values)
                {
                    report.Relation = PlayerRelation.Neutral;
                }
            }

            ShipDesign armed = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatTestKit.BeamPart(10, 1), 1));
            wolf = CombatCoverageKit.AddFleet(serverState, CombatTestKit.WolfId, 1, new Point(50, 50), armed);
            lamb = CombatCoverageKit.AddFleet(serverState, CombatTestKit.LambId, 1, new Point(50, 50), armed);
        }

        private void Run(Random random = null)
        {
            new BattleEngine(serverState, new BattleReport(), random ?? new CombatCoverageKit.ScriptedRandom()).Run();
        }

        private double ArmorOf(Fleet fleet)
        {
            return fleet.Composition.Values.Single().Armor;
        }

        [Test]
        public void CoLocatedRaces_WithNoHostileOrders_FightNoBattle()
        {
            Run();

            Assert.AreEqual(0, serverState.AllEmpires[CombatTestKit.WolfId].BattleReports.Count);
            Assert.AreEqual(0, serverState.AllEmpires[CombatTestKit.LambId].BattleReports.Count);
            Assert.AreEqual(0, serverState.AllMessages.Count);
            Assert.AreEqual(500, ArmorOf(wolf));
            Assert.AreEqual(500, ArmorOf(lamb));
        }

        [Test]
        public void OneSidedHostileOrders_StartTheBattle_AndOnlyTheHostileSideFires()
        {
            // The wolf rates the lamb an Enemy; the lamb rates the wolf Neutral (Attack
            // "Enemies"): the lamb is a legitimate target, the wolf is not.
            serverState.AllEmpires[CombatTestKit.WolfId].EmpireReports[CombatTestKit.LambId].Relation = PlayerRelation.Enemy;

            Run();

            Assert.AreEqual(1, serverState.AllEmpires[CombatTestKit.WolfId].BattleReports.Count);
            Assert.AreEqual(1, serverState.AllEmpires[CombatTestKit.LambId].BattleReports.Count);
            Assert.Less(ArmorOf(lamb), 500, "The lamb is shot at");
            Assert.AreEqual(500, ArmorOf(wolf), "The lamb has no attack order, so it never fires");
        }

        [Test]
        public void ShipTypesAreIrrelevant_AnUnarmedFleetWithAttackOrdersStillTriggersTheBattle()
        {
            serverState.AllEmpires[CombatTestKit.WolfId].OwnedFleets.Clear();
            ShipDesign unarmed = Spec10CombatKit.Design(2, 10, 500, 0, null, 0, null);
            CombatCoverageKit.AddFleet(serverState, CombatTestKit.WolfId, 2, new Point(50, 50), unarmed);
            serverState.AllEmpires[CombatTestKit.WolfId].BattlePlans["Default"].Attack = "Everyone";

            Run();

            Assert.AreEqual(1, serverState.AllEmpires[CombatTestKit.LambId].BattleReports.Count, "A battle is generated (ship types are irrelevant to eligibility)");
            Assert.AreEqual(500, ArmorOf(lamb), "...but the unarmed fleet cannot hurt anyone");
        }

        [Test]
        public void ARaceNobodyIsHostileToAndThatIsHostileToNobody_SitsTheBattleOutEntirely()
        {
            serverState.AllEmpires[CombatTestKit.WolfId].EmpireReports[CombatTestKit.LambId].Relation = PlayerRelation.Enemy;
            CombatCoverageKit.AddThirdEmpire(serverState, PlayerRelation.Neutral, PlayerRelation.Neutral);
            EmpireData third = serverState.AllEmpires[CombatCoverageKit.ThirdId];
            third.BattlePlans["Default"].DumpCargo = true;
            ShipDesign freighter = Spec10CombatKit.Design(3, 10, 500, 100, null, 0, null);
            Fleet bystander = CombatCoverageKit.AddFleet(serverState, CombatCoverageKit.ThirdId, 1, new Point(50, 50), freighter);
            bystander.Cargo.Ironium = 40;

            // high: every engine roll lands at its top (the end-of-battle reward roll passes).
            Run(new CombatCoverageKit.ScriptedRandom(high: true));

            Assert.AreEqual(1, serverState.AllEmpires[CombatTestKit.LambId].BattleReports.Count, "The real battle still runs");
            Assert.AreEqual(0, third.BattleReports.Count, "The bystander is not in the battle");
            Assert.AreEqual(0, serverState.AllMessages.Count(m => m.Audience == CombatCoverageKit.ThirdId));
            Assert.AreEqual(40, bystander.Cargo.Ironium, "A fleet of a race not drawn into the fight keeps its cargo (§5 dump cargo)");
            Assert.AreEqual(0, serverState.AllDeepSpaceMinerals.Count);
            Assert.AreEqual(0, third.GrantedSpecialComponents.Count, "It forfeits the battle's tech-gain opportunity (§1)");
            Assert.IsFalse(third.EmpireReports[CombatTestKit.WolfId].Designs.ContainsKey(wolf.Composition.Values.Single().Design.Key), "It does not see the battle's designs");
        }

        [Test]
        public void AFleetSomeoneIsHostileTo_IsDrawnIn_EvenWithoutOrdersOfItsOwn()
        {
            // Third race rates nobody an enemy, but the wolf (Attack "Everyone") attacks it.
            serverState.AllEmpires[CombatTestKit.WolfId].BattlePlans["Default"].Attack = "Everyone";
            serverState.AllEmpires[CombatTestKit.LambId].OwnedFleets.Clear();
            CombatCoverageKit.AddThirdEmpire(serverState, PlayerRelation.Neutral, PlayerRelation.Neutral);
            ShipDesign target = Spec10CombatKit.Design(3, 10, 500, 0, null, 0, null);
            Fleet victim = CombatCoverageKit.AddFleet(serverState, CombatCoverageKit.ThirdId, 1, new Point(50, 50), target);

            Run();

            Assert.AreEqual(1, serverState.AllEmpires[CombatCoverageKit.ThirdId].BattleReports.Count);
            Assert.Less(victim.Composition.Values.Sum(t => t.Armor), 500);
        }
    }

    /// <summary>
    /// Coverage row 4 (combat-resolution.md §2): at most 256 tokens per battle, split evenly
    /// per race with unused shares redistributed; within a race the fleets with the highest
    /// fleet ids are dropped first.
    /// </summary>
    [TestFixture]
    public class BattleTokenCapCoverageTest
    {
        private static List<Stack> Tokens(int owner, int count)
        {
            ShipDesign design = CombatTestKit.Design(owner, 100);
            List<Stack> stacks = new List<Stack>();
            for (uint id = 1; id <= count; id++)
            {
                stacks.Add(CombatTestKit.MakeStack(design, owner, new Point(0, 0), id));
            }

            return stacks;
        }

        [Test]
        public void UnusedSharesAreRedistributed_ThreeRaces()
        {
            // 256 / 3 = 85 each; race 3 needs only 20, so the 65 + 1 left go to races 1 and 2:
            // 20 + 118 + 118 = 256.
            List<Stack> all = Tokens(1, 200).Concat(Tokens(2, 150)).Concat(Tokens(3, 20)).ToList();
            ServerData serverState = CombatTestKit.TwoEmpires();

            List<Stack> capped = new BattleEngine(serverState, new BattleReport()).ApplyTokenCap(all);

            Assert.AreEqual(256, capped.Count);
            Assert.AreEqual(118, capped.Count(s => s.Owner == 1));
            Assert.AreEqual(118, capped.Count(s => s.Owner == 2));
            Assert.AreEqual(20, capped.Count(s => s.Owner == 3));
        }

        [Test]
        public void ARedistributedShareThatIsNotNeeded_GoesOnToTheRaceThatStillNeedsMore()
        {
            // 85 / 85 / 20, then 66 left split 33 / 33 - race 2 needs only 15 of its 33, so the
            // 18 it cannot use go to race 1: 136 + 100 + 20 = 256.
            List<Stack> all = Tokens(1, 200).Concat(Tokens(2, 100)).Concat(Tokens(3, 20)).ToList();
            ServerData serverState = CombatTestKit.TwoEmpires();

            List<Stack> capped = new BattleEngine(serverState, new BattleReport()).ApplyTokenCap(all);

            Assert.AreEqual(136, capped.Count(s => s.Owner == 1));
            Assert.AreEqual(100, capped.Count(s => s.Owner == 2));
            Assert.AreEqual(20, capped.Count(s => s.Owner == 3));
        }

        [Test]
        public void HighestFleetIdsAreDroppedFirst()
        {
            List<Stack> all = Tokens(1, 300).Concat(Tokens(2, 10)).ToList();
            all.Reverse();
            ServerData serverState = CombatTestKit.TwoEmpires();

            List<Stack> capped = new BattleEngine(serverState, new BattleReport()).ApplyTokenCap(all);

            Assert.AreEqual(256, capped.Count);
            Assert.AreEqual(10, capped.Count(s => s.Owner == 2), "A race needing fewer than its share keeps every token");
            List<uint> kept = capped.Where(s => s.Owner == 1).Select(s => s.Id).OrderBy(id => id).ToList();
            Assert.AreEqual(246, kept.Count);
            CollectionAssert.AreEqual(Enumerable.Range(1, 246).Select(i => (uint)i).ToList(), kept, "Fleets 247-300 (the highest ids) are dropped");
        }

        [Test]
        public void AtOrBelowTheCap_NothingIsDropped()
        {
            List<Stack> all = Tokens(1, 128).Concat(Tokens(2, 128)).ToList();
            ServerData serverState = CombatTestKit.TwoEmpires();

            Assert.AreEqual(256, new BattleEngine(serverState, new BattleReport()).ApplyTokenCap(all).Count);
        }
    }

    /// <summary>
    /// Coverage rows 5 and 24 (combat-resolution.md §7): a battle stops at the 16-round cap, or
    /// when only one race is left, or when none of the races left has hostile orders toward
    /// another (including mid-battle, once the only race someone was hostile to is gone).
    /// </summary>
    [TestFixture]
    public class BattleEndCoverageTest
    {
        [Test]
        public void TwoImmortalTokens_FightExactlySixteenRounds()
        {
            // One 1-damage beam each, 500 armor (1/500 storage is exact): 16 hits of 1.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign design = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatTestKit.BeamPart(1, 1), 1));
            Stack wolf = CombatTestKit.MakeStack(design, CombatTestKit.WolfId, new Point(4, 4), 1);
            Stack lamb = CombatTestKit.MakeStack(design, CombatTestKit.LambId, new Point(4, 4), 2);
            List<Stack> stacks = new List<Stack> { wolf, lamb };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), new BattleReport());

            engine.DoBattle(stacks);

            Assert.AreEqual(500 - 16, wolf.Token.Armor, 1e-9);
            Assert.AreEqual(500 - 16, lamb.Token.Armor, 1e-9);
        }

        [Test]
        public void LastRaceStanding_EndsTheBattleAtOnce()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign killer = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatTestKit.BeamPart(1000, 1), 1));
            ShipDesign victim = Spec10CombatKit.Design(2, 10, 50, 0, null, 0, null);
            Stack wolf = CombatTestKit.MakeStack(killer, CombatTestKit.WolfId, new Point(4, 4), 1);
            Stack lamb = CombatTestKit.MakeStack(victim, CombatTestKit.LambId, new Point(4, 4), 2);
            List<Stack> stacks = new List<Stack> { wolf, lamb };
            BattleReport report = new BattleReport();
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), report);

            engine.DoBattle(stacks);

            Assert.IsTrue(lamb.IsDestroyed);
            Assert.AreEqual(2, CombatCoverageKit.Field<int>(engine, "battleRound"), "Round 2 finds no target and the battle stops");
            int destroyedAt = report.Steps.FindIndex(s => s is BattleStepDestroy);
            Assert.AreEqual(report.Steps.Count - 1, destroyedAt, "Nothing happens after the last enemy dies");
        }

        [Test]
        public void MutualNonHostility_MidBattle_EndsIt_EvenWithTwoRacesLeft()
        {
            // The wolf is hostile to the lamb only; a third race is present, armed, and hostile
            // to nobody (and nobody to it). Once the lamb dies, two races remain but none has
            // hostile orders toward the other.
            ServerData serverState = CombatTestKit.TwoEmpires();
            serverState.AllEmpires[CombatTestKit.WolfId].BattlePlans["Default"].Attack = "Enemies";
            serverState.AllEmpires[CombatTestKit.LambId].BattlePlans["Default"].Attack = "None";
            CombatCoverageKit.AddThirdEmpire(serverState, PlayerRelation.Neutral, PlayerRelation.Neutral);
            ShipDesign killer = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatTestKit.BeamPart(1000, 1), 1));
            ShipDesign victim = Spec10CombatKit.Design(2, 10, 50, 0, null, 0, null);
            Stack wolf = CombatTestKit.MakeStack(killer, CombatTestKit.WolfId, new Point(4, 4), 1);
            Stack lamb = CombatTestKit.MakeStack(victim, CombatTestKit.LambId, new Point(4, 4), 2);
            Stack third = CombatTestKit.MakeStack(killer, CombatCoverageKit.ThirdId, new Point(4, 4), 3);
            List<Stack> stacks = new List<Stack> { wolf, lamb, third };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), new BattleReport());

            engine.DoBattle(stacks);

            Assert.IsTrue(lamb.IsDestroyed);
            Assert.AreEqual(2, CombatCoverageKit.Field<int>(engine, "battleRound"));
            Assert.AreEqual(500, wolf.Token.Armor, 1e-9);
            Assert.AreEqual(500, third.Token.Armor, 1e-9);
        }

        [Test]
        public void NoHostileOrdersAtAll_NoRoundIsFought()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                empire.BattlePlans["Default"].Attack = "None";
            }

            ShipDesign design = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatTestKit.BeamPart(10, 1), 1));
            Stack wolf = CombatTestKit.MakeStack(design, CombatTestKit.WolfId, new Point(4, 4), 1);
            Stack lamb = CombatTestKit.MakeStack(design, CombatTestKit.LambId, new Point(4, 4), 2);
            List<Stack> stacks = new List<Stack> { wolf, lamb };
            BattleReport report = new BattleReport();
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), report);

            engine.DoBattle(stacks);

            Assert.AreEqual(1, CombatCoverageKit.Field<int>(engine, "battleRound"));
            Assert.AreEqual(0, report.Steps.Count);
        }
    }

    /// <summary>
    /// Coverage row 6 (combat-resolution.md §3, §5): a token with battle-movement value v moves
    /// (v + 2) quarter-squares a round: the whole part every round plus a bonus square on a
    /// schedule - a 1/4 bonus on rounds 1, 5, 9, 13; a 1/2 bonus every other round from round 1;
    /// a 3/4 bonus on 3 of every 4 rounds - and movement resolves in tiers: 3-square tokens take
    /// their first step, then 2+-square tokens, then every token with a square left.
    /// </summary>
    [TestFixture]
    public class BattleMovementScheduleCoverageTest
    {
        private ServerData serverState;
        private BattleReport report;
        private BattleEngine engine;
        private Stack goal;
        private List<Stack> stacks;

        [SetUp]
        public void Init()
        {
            serverState = CombatTestKit.TwoEmpires();
            report = new BattleReport();
            goal = CombatTestKit.MakeStack(CombatTestKit.Design(9, 100), CombatTestKit.LambId, new Point(9, 9), 9);
            stacks = new List<Stack> { goal };
            engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), report);
        }

        private Stack Mover(uint id, int movement, int mass = 10)
        {
            ShipDesign design = Spec10CombatKit.Design(id, mass, 100, 0, null, 0, null, (CombatTestKit.BeamPart(1, 1), 1));
            Stack mover = CombatTestKit.MakeStack(design, CombatTestKit.WolfId, new Point(0, 0), id);
            mover.Target = goal;
            stacks.Add(mover);
            CombatCoverageKit.Field<Dictionary<Stack, int>>(engine, "tokenMovement")[mover] = movement;
            return mover;
        }

        private List<int> SquaresPerRound(Stack mover, int rounds)
        {
            List<int> squares = new List<int>();
            for (int round = 1; round <= rounds; round++)
            {
                CombatCoverageKit.SetField(engine, "battleRound", round);
                int before = report.Steps.Count;
                engine.MoveStacks(stacks);
                squares.Add(report.Steps.Skip(before).OfType<BattleStepMovement>().Count(s => s.StackKey == mover.Key));
                CombatCoverageKit.At(mover, 0, 0);
            }

            return squares;
        }

        [TestCase(0, new[] { 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0 })]
        [TestCase(2, new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 })]
        [TestCase(3, new[] { 2, 1, 1, 1, 2, 1, 1, 1, 2, 1, 1, 1, 2, 1, 1, 1 })]
        [TestCase(4, new[] { 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1 })]
        [TestCase(6, new[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 })]
        [TestCase(7, new[] { 3, 2, 2, 2, 3, 2, 2, 2, 3, 2, 2, 2, 3, 2, 2, 2 })]
        [TestCase(8, new[] { 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2 })]
        public void WholeSquaresPlusTheQuarterAndHalfBonusSchedule(int movement, int[] expected)
        {
            Stack mover = Mover(1, movement);

            CollectionAssert.AreEqual(expected, SquaresPerRound(mover, 16));
        }

        [TestCase(1, 0)]
        [TestCase(5, 1)]
        public void ThreeQuarterBonus_LandsOnThreeRoundsOfEveryFour(int movement, int wholeSquares)
        {
            // Which three of the four rounds is not stated, only the count.
            Stack mover = Mover(1, movement);
            List<int> squares = SquaresPerRound(mover, 16);

            for (int block = 0; block < 4; block++)
            {
                List<int> four = squares.Skip(block * 4).Take(4).ToList();
                Assert.AreEqual((4 * wholeSquares) + 3, four.Sum(), "Rounds " + ((block * 4) + 1) + "-" + ((block * 4) + 4));
                Assert.IsTrue(four.All(s => s == wholeSquares || s == wholeSquares + 1));
            }
        }

        [Test]
        public void MovementResolvesInTiers_ThreeSquareTokensStepFirst_EvenWhenLighter()
        {
            // Round 1: the light token moves 3 squares (v = 8), the heavy one 1 (v = 2). Step 1:
            // only 3-square tokens; step 2: 2+-square tokens; step 3: everyone with a square
            // left, heavier first. So the order is fast, fast, slow, fast.
            Stack fast = Mover(1, 8, mass: 10);
            Stack slow = Mover(2, 2, mass: 1000);
            CombatCoverageKit.SetField(engine, "battleRound", 1);

            engine.MoveStacks(stacks);

            List<long> order = report.Steps.OfType<BattleStepMovement>().Select(s => s.StackKey).ToList();
            CollectionAssert.AreEqual(new[] { fast.Key, fast.Key, slow.Key, fast.Key }, order);
        }
    }

    /// <summary>
    /// Coverage row 14 (combat-resolution.md §5, §6): firing is weapon-slot by weapon-slot -
    /// all weapons of one slot fire together as one shot of weapons x ships x damage.
    /// </summary>
    [TestFixture]
    public class BattleSlotFiringCoverageTest
    {
        [Test]
        public void EachSlotIsOneShot_OfWeaponsTimesShipsTimesDamage()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 10, 100, 0, null, 0, null,
                (CombatCoverageKit.Real("Laser"), 2),
                (CombatCoverageKit.Real("Colloidal Phaser"), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1, quantity: 3);
            Stack lamb = CombatTestKit.MakeStack(Spec10CombatKit.Design(2, 10, 500, 0, null, 0, null), CombatTestKit.LambId, new Point(0, 0), 2);
            List<Stack> stacks = new List<Stack> { wolf, lamb };
            BattleReport report = new BattleReport();
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), report);

            CombatCoverageKit.FireRound(engine, stacks);

            List<BattleStepWeapons> hits = report.Steps.OfType<BattleStepWeapons>().Where(s => s.Targeting == BattleStepWeapons.TokenDefence.Armor).ToList();
            CollectionAssert.AreEqual(new double[] { 2 * 3 * 10, 1 * 3 * 26 }, hits.Select(h => h.Damage).ToList(),
                "Two slots, two shots: the laser slot (initiative 9) then the phaser slot (initiative 5)");
            Assert.AreEqual(500 - 60 - 78, lamb.Token.Armor, 1e-9);
        }
    }

    /// <summary>
    /// The spec's Worked Example 1 (combat-resolution.md "Worked Examples"), run through the
    /// engine's own firing queue round by round at the example's distances 3, 1 and 0:
    /// initiative order, per-slot shots, beam range falloff floor(10 d / range) and the
    /// whole-ship kill. A Destroyer (200 + 2 Tritanium = 300 armor, two slots of one Colloidal
    /// Phaser, 26 dp, range 3, initiative 5) against a Frigate (45 + 100 = 145 armor, one slot of
    /// 3 Lasers, 10 dp, range 1, initiative 9). Hull initiatives 3 and 4 as in §5.
    /// </summary>
    [TestFixture]
    public class BattleWorkedExampleOneTest
    {
        [Test]
        public void DestroyerAgainstFrigate_RoundByRound()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign destroyer = Spec10CombatKit.Design(1, 30, 200, 0, null, 3, null,
                (CombatCoverageKit.Real("Colloidal Phaser"), 1),
                (CombatCoverageKit.Real("Colloidal Phaser"), 1),
                (CombatCoverageKit.Real("Tritanium"), 2));
            ShipDesign frigate = Spec10CombatKit.Design(2, 8, 45, 0, null, 4, null,
                (CombatCoverageKit.Real("Laser"), 3),
                (CombatCoverageKit.Real("Tritanium"), 2));
            Assert.AreEqual(300, destroyer.Armor);
            Assert.AreEqual(145, frigate.Armor);

            Stack a = CombatTestKit.MakeStack(destroyer, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack b = CombatTestKit.MakeStack(frigate, CombatTestKit.LambId, new Point(3, 0), 2);
            List<Stack> stacks = new List<Stack> { a, b };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), new BattleReport());

            // Round 1, range 3: only A reaches; 26 x 90 / 100 = 23 per slot.
            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(145 - 46, b.Token.Armor, 1e-9);
            Assert.AreEqual(300, a.Token.Armor, 1e-9);

            // Round 2, range 1: B fires first (initiative 4 + 9 > 3 + 5): 30 x 90 / 100 = 27;
            // A's slots lose floor(10 / 3) = 3%: 25 each.
            CombatCoverageKit.At(b, 1, 0);
            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(300 - 27, a.Token.Armor, 1e-9);
            Assert.AreEqual(99 - 25 - 25, b.Token.Armor, 1e-9);

            // Round 3, range 0: B 30 (A 243); A's first slot 26 (B 23); the second slot's 26
            // destroys the whole ship.
            CombatCoverageKit.At(b, 0, 0);
            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(273 - 30, a.Token.Armor, 1e-9);
            Assert.IsTrue(b.IsDestroyed);
        }
    }

    /// <summary>
    /// Coverage row 18 and Worked Examples 2 and 3 (combat-resolution.md §6): every torpedo is
    /// an independent roll of 0-99 that hits when STRICTLY below the accuracy; a miss chips
    /// D / 8 off the shields only; a hit offers half to the shields and the rest (plus what the
    /// shields cannot take) to armor; a capital missile doubles only when the shields are
    /// already 0 when the salvo starts.
    /// </summary>
    [TestFixture]
    public class BattleTorpedoCoverageTest
    {
        private ServerData serverState;

        [SetUp]
        public void Init()
        {
            serverState = CombatTestKit.TwoEmpires();
        }

        private static ShipDesign BattleCruiser(long key, params (Component part, int count)[] slots)
        {
            return Spec10CombatKit.Design(key, 120, 1000, 0, null, 5, null, slots);
        }

        [Test]
        public void WorkedExampleTwo_RhoTorpedoesAgainstTwoBearBarriers()
        {
            // Ship D carries the example's 200 shields; its armor is 500 here (the example's
            // Cruiser has 800) so that the 1/500 damage storage of §8 is exact and the example's
            // own subtraction can be followed point for point.
            ShipDesign c = BattleCruiser(1, (CombatCoverageKit.Real("Rho Torpedo"), 3));
            ShipDesign d = Spec10CombatKit.Design(2, 90, 400, 0, null, 5, null,
                (CombatCoverageKit.Real("Bear Neutrino Barrier"), 1),
                (CombatCoverageKit.Real("Bear Neutrino Barrier"), 1),
                (CombatCoverageKit.Real("Tritanium"), 2));
            Stack shooter = CombatTestKit.MakeStack(c, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack target = CombatTestKit.MakeStack(d, CombatTestKit.LambId, new Point(5, 0), 2);
            List<Stack> stacks = new List<Stack> { shooter, target };

            // Round 1: 2 hits, 1 miss. Round 2: 3 hits. Round 3: 2 hits, 1 miss.
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(0, 0, 99, 0, 0, 0, 0, 0, 99), new BattleReport());

            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(200 - 11 - 90, target.Token.Shields, 1e-9, "Miss 90 / 8 = 11, then 45 + 45 to shields");
            Assert.AreEqual(500 - 90, target.Token.Armor, 1e-9);

            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(0, target.Token.Shields, 1e-9);
            Assert.AreEqual(410 - (270 - 99), target.Token.Armor, 1e-9, "The 99 left absorb 99 of the 135 half; 171 reach armor");

            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(239 - 180, target.Token.Armor, 1e-9, "Shields already 0: the miss does nothing, 2 x 90 to armor");
        }

        [Test]
        public void RealCruiserTarget_DamageIsStoredRoundedUpToTheNextFiveHundredth()
        {
            // The example's real Cruiser (700 + 2 x 50 = 800 armor): round 1's 90 armor damage is
            // stored per §8 as ceil(90 x 500 / 800) = 57 units, which read back as
            // 57 x 800 / 500 = 91 (truncated) - 709, not the example's 710.
            ShipDesign c = BattleCruiser(1, (CombatCoverageKit.Real("Rho Torpedo"), 3));
            ShipDesign d = Spec10CombatKit.Design(2, 90, 700, 0, null, 5, null,
                (CombatCoverageKit.Real("Bear Neutrino Barrier"), 1),
                (CombatCoverageKit.Real("Bear Neutrino Barrier"), 1),
                (CombatCoverageKit.Real("Tritanium"), 2));
            Stack shooter = CombatTestKit.MakeStack(c, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack target = CombatTestKit.MakeStack(d, CombatTestKit.LambId, new Point(0, 0), 2);
            List<Stack> stacks = new List<Stack> { shooter, target };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(0, 0, 99), new BattleReport());

            CombatCoverageKit.FireRound(engine, stacks);

            Assert.AreEqual(189 - 90, target.Token.Shields, 1e-9);
            Assert.AreEqual(800 - 91, target.Token.Armor, 1e-9);
        }

        [Test]
        public void TheHitTestIsStrictlyBelowTheAccuracy()
        {
            // Rho Torpedo 75%: a draw of 74 hits, 75 misses.
            ShipDesign c = BattleCruiser(1, (CombatCoverageKit.Real("Rho Torpedo"), 3));
            ShipDesign d = Spec10CombatKit.Design(2, 90, 400, 0, null, 0, null,
                (CombatCoverageKit.Real("Bear Neutrino Barrier"), 2), (CombatCoverageKit.Real("Tritanium"), 2));
            Stack shooter = CombatTestKit.MakeStack(c, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack target = CombatTestKit.MakeStack(d, CombatTestKit.LambId, new Point(0, 0), 2);
            List<Stack> stacks = new List<Stack> { shooter, target };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(74, 75, 75), new BattleReport());

            CombatCoverageKit.FireRound(engine, stacks);

            Assert.AreEqual(200 - 22 - 45, target.Token.Shields, 1e-9, "2 misses chip 2 x 90 / 8 = 22, the hit's half 45");
            Assert.AreEqual(500 - 45, target.Token.Armor, 1e-9);
        }

        [Test]
        public void WorkedExampleThree_JihadMissiles_DoubleOnlyOnceTheShieldsAreAlreadyGone()
        {
            // Ship E: 3 Jihad Missiles (85, 20%) + a Battle Super Computer: 44%. Ship F: a
            // Destroyer with one Mole-skin Shield (25) and 2 Tritanium (300 armor). A second
            // target G (1,000 armor, no shields) sits in reach for the unallotted missiles.
            ShipDesign e = BattleCruiser(1, (CombatCoverageKit.Real("Jihad Missile"), 3), (CombatCoverageKit.Real("Battle Super Computer"), 1));
            ShipDesign f = Spec10CombatKit.Design(2, 30, 200, 0, null, 3, null,
                (CombatCoverageKit.Real("Mole-skin Shield"), 1), (CombatCoverageKit.Real("Tritanium"), 2));
            Stack shooter = CombatTestKit.MakeStack(e, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack target = CombatTestKit.MakeStack(f, CombatTestKit.LambId, new Point(0, 0), 2);
            List<Stack> stacks = new List<Stack> { shooter, target };

            // Round 1 at 44%: 43 hits, 0 hits, 44 misses. Round 2 (F): 1 hit of 3; then the two
            // unallotted missiles roll again against G: both hit.
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(43, 0, 44, 0, 99, 99, 0, 0), new BattleReport());

            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(0, target.Token.Shields, 1e-9, "25 - 85 / 8 = 15, then the hits' half takes the last 15");
            Assert.AreEqual(300 - 155, target.Token.Armor, 1e-9, "70 of the shield half + the 85 armor half; no doubling (shields were up when the salvo began)");

            Stack second = CombatTestKit.MakeStack(Spec10CombatKit.Design(3, 30, 1000, 0, null, 0, null), CombatTestKit.LambId, new Point(0, 0), 3);
            stacks.Add(second);
            CombatCoverageKit.FireRound(engine, stacks);

            Assert.IsTrue(target.IsDestroyed, "One allotted missile carrying a doubled 170 kills the 145-armor ship");
            Assert.AreEqual(1000 - (2 * 170), second.Token.Armor, 1e-9, "The other two missiles re-rolled against G, doubled (its shields are 0)");
        }

        [Test]
        public void WorkedExampleThree_TheRhoContrast()
        {
            // "Had the same hits come from Rho Torpedoes ... round 1 would have left the
            // Destroyer on 134 armor and round 2's single hit on 44, still alive."
            ShipDesign e = BattleCruiser(1, (CombatCoverageKit.Real("Rho Torpedo"), 3));
            ShipDesign f = Spec10CombatKit.Design(2, 30, 200, 0, null, 3, null,
                (CombatCoverageKit.Real("Mole-skin Shield"), 1), (CombatCoverageKit.Real("Tritanium"), 2));
            Stack shooter = CombatTestKit.MakeStack(e, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack target = CombatTestKit.MakeStack(f, CombatTestKit.LambId, new Point(0, 0), 2);
            List<Stack> stacks = new List<Stack> { shooter, target };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(0, 0, 99, 0, 99, 99), new BattleReport());

            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(134, target.Token.Armor, 1e-9);

            CombatCoverageKit.FireRound(engine, stacks);
            Assert.AreEqual(44, target.Token.Armor, 1e-9);
            Assert.IsFalse(target.IsDestroyed);
        }

        [Test]
        public void Jammer20_CutsRhoTo60_AndTheSuperComputerLiftsItBackTo78()
        {
            // Jam left over: 75 x (100 - 20) / 100 = 60. Computer left over (30 - 20 = 10):
            // 100 - 90 x 25 / 100 = 78 (integer).
            ShipDesign plain = BattleCruiser(1, (CombatCoverageKit.Real("Rho Torpedo"), 3));
            ShipDesign withComputer = BattleCruiser(3, (CombatCoverageKit.Real("Rho Torpedo"), 3), (CombatCoverageKit.Real("Battle Super Computer"), 1));
            ShipDesign jammed = Spec10CombatKit.Design(2, 30, 500, 0, null, 0, null, (CombatCoverageKit.Real("Jammer 20"), 1));

            foreach ((ShipDesign shooterDesign, int accuracy) in new[] { (plain, 60), (withComputer, 78) })
            {
                ServerData state = CombatTestKit.TwoEmpires();
                Stack shooter = CombatTestKit.MakeStack(shooterDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
                Stack target = CombatTestKit.MakeStack(jammed, CombatTestKit.LambId, new Point(0, 0), 2);
                List<Stack> stacks = new List<Stack> { shooter, target };
                BattleEngine engine = CombatCoverageKit.Engine(state, stacks, new CombatCoverageKit.ScriptedRandom(accuracy - 1, accuracy, accuracy), new BattleReport());

                CombatCoverageKit.FireRound(engine, stacks);

                Assert.AreEqual(500 - 90, target.Token.Armor, 1e-9, "Exactly one hit at " + accuracy + "%");
            }
        }
    }

    /// <summary>
    /// Whole-battle conformance for initiative order, capacitors/deflectors/falloff and
    /// sappers (combat-resolution.md §5, §6).
    /// </summary>
    [TestFixture]
    public class BattleFiringOrderConformanceTest
    {
        private static (Stack wolf, Stack lamb, List<Stack> stacks, BattleEngine engine) Duel(ShipDesign wolfDesign, ShipDesign lambDesign, int distance)
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(lambDesign, CombatTestKit.LambId, new Point(distance, 0), 2);
            List<Stack> stacks = new List<Stack> { wolf, lamb };
            return (wolf, lamb, stacks, CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), new BattleReport()));
        }

        [Test]
        public void HigherTotalInitiativeFiresFirst_AndADestroyedTokenNeverFiresItsSlots()
        {
            // Wolf: hull initiative 10 + Colloidal Phaser 5 = 15; lamb: 4 + Laser 9 = 13. The
            // wolf's 3 x 26 = 78 kills the 50-armor lamb before its lasers fire.
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 10, 500, 0, null, 10, null, (CombatCoverageKit.Real("Colloidal Phaser"), 3));
            ShipDesign lambDesign = Spec10CombatKit.Design(2, 10, 50, 0, null, 4, null, (CombatCoverageKit.Real("Laser"), 3));
            var duel = Duel(wolfDesign, lambDesign, 0);

            CombatCoverageKit.FireRound(duel.engine, duel.stacks);

            Assert.IsTrue(duel.lamb.IsDestroyed);
            Assert.AreEqual(500, duel.wolf.Token.Armor, 1e-9);
        }

        [Test]
        public void WithLowerInitiative_TheSameWolfIsShotFirst()
        {
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatCoverageKit.Real("Colloidal Phaser"), 3));
            ShipDesign lambDesign = Spec10CombatKit.Design(2, 10, 50, 0, null, 4, null, (CombatCoverageKit.Real("Laser"), 3));
            var duel = Duel(wolfDesign, lambDesign, 0);

            CombatCoverageKit.FireRound(duel.engine, duel.stacks);

            Assert.AreEqual(500 - 30, duel.wolf.Token.Armor, 1e-9);
            Assert.IsTrue(duel.lamb.IsDestroyed);
        }

        [Test]
        public void ComputersAddToTheTokenInitiative()
        {
            // 10 + Battle Super Computer 2 + weapon 0 = 12 beats 0 + 11.
            Component wolfGun = CombatTestKit.Part(ItemType.BeamWeapons, "Weapon", new Weapon { Power = 1000, Range = 1, Accuracy = 100, Initiative = 0, Group = WeaponType.standardBeam });
            Component lambGun = CombatTestKit.Part(ItemType.BeamWeapons, "Weapon", new Weapon { Power = 1000, Range = 1, Accuracy = 100, Initiative = 11, Group = WeaponType.standardBeam });
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 10, 500, 0, null, 10, null, (wolfGun, 1), (CombatCoverageKit.Real("Battle Super Computer"), 1));
            ShipDesign lambDesign = Spec10CombatKit.Design(2, 10, 500, 0, null, 0, null, (lambGun, 1));
            var duel = Duel(wolfDesign, lambDesign, 0);

            CombatCoverageKit.FireRound(duel.engine, duel.stacks);

            Assert.IsTrue(duel.lamb.IsDestroyed);
            Assert.AreEqual(500, duel.wolf.Token.Armor, 1e-9);
        }

        [Test]
        public void TiedInitiative_TheShorterRangedWeaponFiresFirst()
        {
            Component longGun = CombatTestKit.Part(ItemType.BeamWeapons, "Weapon", new Weapon { Power = 1000, Range = 3, Accuracy = 100, Initiative = 9, Group = WeaponType.standardBeam });
            Component shortGun = CombatTestKit.Part(ItemType.BeamWeapons, "Weapon", new Weapon { Power = 1000, Range = 1, Accuracy = 100, Initiative = 9, Group = WeaponType.standardBeam });
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (longGun, 1));
            ShipDesign lambDesign = Spec10CombatKit.Design(2, 10, 500, 0, null, 0, null, (shortGun, 1));
            var duel = Duel(wolfDesign, lambDesign, 0);

            CombatCoverageKit.FireRound(duel.engine, duel.stacks);

            Assert.IsTrue(duel.wolf.IsDestroyed, "The range-1 gun fires before the range-3 gun at the same initiative");
            Assert.AreEqual(500, duel.lamb.Token.Armor, 1e-9);
        }

        [Test]
        public void Capacitors_Deflectors_AndFalloff_InOneShot()
        {
            // 3 Lasers (30) x 2 Flux Capacitors (100 -> 120 -> 144%) = 43; x 2 Beam Deflectors
            // (81%) = 34; at distance 1 of range 1 (-10%) = 30.
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null,
                (CombatCoverageKit.Real("Laser"), 3), (CombatCoverageKit.Real("Flux Capacitor"), 2));
            ShipDesign lambDesign = Spec10CombatKit.Design(2, 10, 500, 0, null, 0, null, (CombatCoverageKit.Real("Beam Deflector"), 2));
            Assert.AreEqual(144, wolfDesign.CapacitorPercent);
            Assert.AreEqual(81, lambDesign.BeamDeflectorPercent);
            var duel = Duel(wolfDesign, lambDesign, 1);

            CombatCoverageKit.FireRound(duel.engine, duel.stacks);

            Assert.AreEqual(500 - 30, duel.lamb.Token.Armor, 1e-9);
        }

        [Test]
        public void TenEnergyCapacitors_AreCappedAt255Percent()
        {
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null,
                (CombatCoverageKit.Real("Laser"), 1), (CombatCoverageKit.Real("Energy Capacitor"), 10));
            ShipDesign lambDesign = Spec10CombatKit.Design(2, 10, 500, 0, null, 0, null);
            var duel = Duel(wolfDesign, lambDesign, 0);

            CombatCoverageKit.FireRound(duel.engine, duel.stacks);

            Assert.AreEqual(255, wolfDesign.CapacitorPercent);
            Assert.AreEqual(500 - 25, duel.lamb.Token.Armor, 1e-9, "10 x 255 / 100 = 25");
        }

        [Test]
        public void Sapper_StripsShieldsOnly_ThenOverflowsOntoTheNextShieldedToken()
        {
            // Pulsed Sapper 82 at distance 0: the first token's 50 shields go, armor untouched;
            // the unused 32 re-aim at the next shielded token in reach (100 shields -> 68). A
            // shieldless token is never picked by a sapper.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatCoverageKit.Real("Pulsed Sapper"), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack first = CombatTestKit.MakeStack(Spec10CombatKit.Design(2, 10, 500, 0, null, 0, null, (CombatTestKit.ShieldPart(50), 1)), CombatTestKit.LambId, new Point(0, 0), 2);
            Stack bare = CombatTestKit.MakeStack(Spec10CombatKit.Design(3, 10, 500, 0, null, 0, null), CombatTestKit.LambId, new Point(0, 0), 3);
            Stack second = CombatTestKit.MakeStack(Spec10CombatKit.Design(4, 10, 500, 0, null, 0, null, (CombatTestKit.ShieldPart(100), 1)), CombatTestKit.LambId, new Point(0, 0), 4);
            List<Stack> stacks = new List<Stack> { wolf, first, bare, second };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), new BattleReport());

            CombatTestKit.Fire(engine, wolf, first, wolfDesign.Weapons[0]);

            Assert.AreEqual(0, first.Token.Shields, 1e-9);
            Assert.AreEqual(500, first.Token.Armor, 1e-9);
            Assert.AreEqual(500, bare.Token.Armor, 1e-9);
            Assert.AreEqual(100 - 32, second.Token.Shields, 1e-9);
            Assert.AreEqual(500, second.Token.Armor, 1e-9);
        }
    }

    /// <summary>
    /// Whole-battle conformance for retreat (combat-resolution.md §3, §7) and salvage of a
    /// partly destroyed fleet (§7 correction).
    /// </summary>
    [TestFixture]
    public class BattleRetreatAndSalvageConformanceTest
    {
        [Test]
        public void Disengage_SevenSquaresOfMovementLeaveTheBoard_DiagonalStepsCountOneSquareEach()
        {
            // A fast (v = 8: 3, 2, 3 squares in rounds 1-3) unarmed Disengage token flees
            // diagonally from a slow long-range hunter that keeps it in range. "Retreating off
            // the board requires accumulating 7 squares of movement" (§3, §7) on a grid whose
            // distance is the larger of the column and row differences (§6): after round 2 it
            // has moved 5 squares and is still on the board; the 7th square comes in round 3.
            ServerData serverState = CombatTestKit.TwoEmpires();
            serverState.AllEmpires[CombatTestKit.LambId].BattlePlans["Default"].Tactic = "Disengage";
            ShipDesign hunterDesign = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatTestKit.BeamPart(10, 9), 1));
            ShipDesign runnerDesign = Spec10CombatKit.Design(2, 10, 500, 0, null, 0, null);
            Stack hunter = CombatTestKit.MakeStack(hunterDesign, CombatTestKit.WolfId, new Point(1, 1), 1);
            Stack runner = CombatTestKit.MakeStack(runnerDesign, CombatTestKit.LambId, new Point(2, 2), 2);
            List<Stack> stacks = new List<Stack> { hunter, runner };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), new BattleReport());
            Dictionary<Stack, int> movement = CombatCoverageKit.Field<Dictionary<Stack, int>>(engine, "tokenMovement");
            movement[hunter] = 0;
            movement[runner] = 8;

            for (int round = 1; round <= 3; round++)
            {
                CombatCoverageKit.SetField(engine, "battleRound", round);
                Assert.AreEqual(2, engine.SelectTargets(stacks), "Round " + round);
                engine.MoveStacks(stacks);
                List<WeaponDetails> attacks = (List<WeaponDetails>)CombatTestKit.Invoke(engine, "GenerateAttacks", stacks);
                foreach (WeaponDetails attack in attacks)
                {
                    CombatTestKit.Invoke(engine, "ProcessAttack", attack);
                }

                if (round == 2)
                {
                    Assert.IsFalse(runner.HasRetreated, "5 squares are not enough");
                    Assert.AreEqual(5, runner.DisengageDistanceAccumulated, 1e-9);
                }
            }

            Assert.IsTrue(runner.HasRetreated);
            Assert.AreEqual(7, runner.DisengageDistanceAccumulated, 1e-9);
            Assert.IsFalse(runner.IsDestroyed);
            Assert.AreEqual(1, runner.Token.Quantity);
            Assert.IsTrue(serverState.AllMessages.Any(m => m.Audience == CombatTestKit.LambId && m.Text.Contains("disengaged")));
            Assert.AreEqual(0, engine.SelectTargets(stacks), "A retreated token neither picks nor can be picked");
            Assert.IsNull(hunter.Target);
        }

        [Test]
        public void PartlyDestroyedFleet_SalvageIsAThirdOfTheLostShipsPlusTheirCargoShare()
        {
            // Two 50-armor freighters (cargo 100 each, cost 30 ironium) carry 100 kT ironium. A
            // 60-point beam kills exactly one: 1 x 30 / 3 = 10 plus its share 100 x 100 / 200 =
            // 50, in deep space minus a quarter: 60 - 15 = 45. The fleet keeps 50 kT.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign gun = Spec10CombatKit.Design(1, 10, 500, 0, null, 0, null, (CombatTestKit.BeamPart(60, 1), 1));
            ShipDesign freighter = Spec10CombatKit.Design(2, 10, 50, 100, new Resources(30, 0, 0, 10), 0, null);
            Fleet convoy = CombatCoverageKit.AddFleet(serverState, CombatTestKit.LambId, 7, new Point(40, 40), freighter, ships: 2);
            convoy.Cargo.Ironium = 100;
            Stack wolf = CombatTestKit.MakeStack(gun, CombatTestKit.WolfId, new Point(0, 0), 1);
            BattleEngine scratch = new BattleEngine(serverState, new BattleReport());
            Stack lamb = scratch.BuildFleetStacks(convoy).Single();
            CombatCoverageKit.At(lamb, 0, 0);
            List<Stack> stacks = new List<Stack> { wolf, lamb };
            BattleEngine engine = CombatCoverageKit.Engine(serverState, stacks, new CombatCoverageKit.ScriptedRandom(), new BattleReport());

            CombatTestKit.Fire(engine, wolf, lamb, gun.Weapons[0]);

            Assert.AreEqual(1, lamb.Token.Quantity);
            Assert.AreEqual(50, convoy.Cargo.Ironium);
            Resources salvage = CombatCoverageKit.Field<Resources>(engine, "totalSalvage");
            Assert.AreEqual(45, salvage.Ironium);
            Assert.AreEqual(0, salvage.Energy);
        }
    }

    /// <summary>
    /// Coverage row 27 (combat-resolution.md §7 Aftermath, turn-generation-engine.md §3 step
    /// 18): deep-space salvage loses 10% of each mineral, or 10 kT, whichever is larger, per year
    /// after a one-turn grace flag is used up, and is deleted at zero. (How the 10% is rounded
    /// and whether a top-up re-arms the grace flag are open questions 6.3 / 6.4 - avoided here.)
    /// </summary>
    [TestFixture]
    public class SalvageDecayCoverageTest
    {
        private static ServerData WithWreck(Resources minerals, bool grace)
        {
            ServerData serverState = new ServerData();
            DeepSpaceMinerals wreck = new DeepSpaceMinerals(new NovaPoint(10, 10)) { Minerals = minerals, DecayGrace = grace };
            serverState.AllDeepSpaceMinerals["wreck"] = wreck;
            return serverState;
        }

        [Test]
        public void TenPercent_OrTenKilotonsWhicheverIsLarger()
        {
            ServerData serverState = WithWreck(new Resources(1000, 50, 200, 0), false);

            new DeepSpaceMineralDecayStep().Process(serverState);

            Resources left = serverState.AllDeepSpaceMinerals["wreck"].Minerals;
            Assert.AreEqual(900, left.Ironium, "10% of 1,000");
            Assert.AreEqual(40, left.Boranium, "10% of 50 is 5 - the 10 kT minimum applies");
            Assert.AreEqual(180, left.Germanium, "10% of 200 is exactly 20");
        }

        [Test]
        public void TheGraceFlag_SkipsOneYear()
        {
            ServerData serverState = WithWreck(new Resources(1000, 0, 0, 0), true);

            new DeepSpaceMineralDecayStep().Process(serverState);
            Assert.AreEqual(1000, serverState.AllDeepSpaceMinerals["wreck"].Minerals.Ironium, "The grace year");
            Assert.IsFalse(serverState.AllDeepSpaceMinerals["wreck"].DecayGrace);

            new DeepSpaceMineralDecayStep().Process(serverState);
            Assert.AreEqual(900, serverState.AllDeepSpaceMinerals["wreck"].Minerals.Ironium);
        }

        [Test]
        public void ARecordThatReachesZero_IsDeleted()
        {
            ServerData serverState = WithWreck(new Resources(10, 0, 0, 0), false);

            new DeepSpaceMineralDecayStep().Process(serverState);

            Assert.AreEqual(0, serverState.AllDeepSpaceMinerals.Count);
        }

        [Test]
        public void BattleWreckage_IsCreatedWithTheGraceFlag()
        {
            ServerData serverState = new ServerData();

            BattleEngine.AddWreckage(serverState, new NovaPoint(5, 5), new Resources(1000, 0, 0, 0));
            DeepSpaceMinerals wreck = serverState.AllDeepSpaceMinerals.Values.Single();
            Assert.IsTrue(wreck.DecayGrace);

            new DeepSpaceMineralDecayStep().Process(serverState);
            Assert.AreEqual(1000, wreck.Minerals.Ironium);
            new DeepSpaceMineralDecayStep().Process(serverState);
            Assert.AreEqual(900, wreck.Minerals.Ironium);
        }

        [Test]
        public void PlanetSideSalvage_IsNeverARecord()
        {
            // "planet-side salvage does not decay": a battle over a planet adds to its surface.
            ServerData serverState = new ServerData();
            Star star = new Star { Name = "Here", Position = new NovaPoint(5, 5) };
            serverState.AllStars[star.Name] = star;

            BattleEngine.AddWreckage(serverState, new NovaPoint(5, 5), new Resources(1000, 0, 0, 0));

            Assert.AreEqual(0, serverState.AllDeepSpaceMinerals.Count);
        }
    }

    /// <summary>
    /// Coverage row 29, the remaining orbital-bombardment clauses of turn-generation-engine.md
    /// §4 not already covered by BombingRuleTest: the 1-unit and M-unit kill floors, the
    /// installation caps and the "must carry something that bombs" gate.
    /// </summary>
    [TestFixture]
    public class BombingCoverageTest
    {
        private const ushort BomberId = 1;
        private const ushort OwnerId = 2;

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

            star = new Star { Name = "Target", Owner = OwnerId, Colonists = 10000, Mines = 0, Factories = 0, DefenseType = "None" };
            serverState.AllStars[star.Name] = star;
            owner.OwnedStars.Add(star);
        }

        private void AddBomber(params (Component part, int count)[] slots)
        {
            ShipDesign design = CombatTestKit.Design(nextFleetId, 100, null, false, slots);
            Fleet fleet = new Fleet("Bomber " + nextFleetId, BomberId, nextFleetId, star.Position);
            nextFleetId++;
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);
            fleet.InOrbit = star;
            serverState.AllEmpires[BomberId].OwnedFleets.Add(fleet);
        }

        private void Bomb(params int[] draws)
        {
            new Bombing(serverState, new CombatCoverageKit.ScriptedRandom(true, draws)).BombAll();
        }

        [Test]
        public void ANonzeroKillRate_KillsAtLeastOneUnit()
        {
            // 100 units, one LBU-17 (N = 2, no minimum): 100 x 2 / 1000 = 0 remainder 200; the
            // 0-999 draw 999 adds nothing; the kill is raised to 1 unit (100 colonists).
            AddBomber((CombatCoverageKit.Real("LBU-17 Bomb"), 1));

            Bomb(999);

            Assert.AreEqual(10000 - 100, star.Colonists);
        }

        [Test]
        public void TheKill_IsRaisedToTheMinimumKill()
        {
            // One Lady Finger: N = 6 gives 0 units (remainder 600, draw 999 adds nothing), raised
            // to 1, then to the minimum M = 3 units (300 colonists).
            AddBomber((CombatCoverageKit.Real("Lady Finger Bomb"), 1));

            Bomb(999);

            Assert.AreEqual(10000 - 300, star.Colonists);
        }

        [Test]
        public void InstallationLosses_AreCappedAtWhatIsPresent_AndMinesNeverGoNegative()
        {
            // LBU-17: D = 16 against 5 factories only (T = 5): factories 5 x 16 / 5 = 16, capped
            // at 5; defences 0; mines 16 - 5 - 0 = 11 capped at the 0 present.
            star.Factories = 5;
            AddBomber((CombatCoverageKit.Real("LBU-17 Bomb"), 1));

            Bomb(999);

            Assert.AreEqual(0, star.Factories);
            Assert.AreEqual(0, star.Mines);
            Assert.AreEqual(0, star.Defenses);
        }

        [Test]
        public void MinesTakeWhatTheFactoriesAndDefencesLeaveOfD()
        {
            // LBU-17 (D = 16) against 30 mines and 10 factories (T = 40): factories 10 x 16 / 40
            // = 4 exactly, defences 0, mines 16 - 4 = 12.
            star.Mines = 30;
            star.Factories = 10;
            AddBomber((CombatCoverageKit.Real("LBU-17 Bomb"), 1));

            Bomb(999, 999, 999);

            Assert.AreEqual(10 - 4, star.Factories);
            Assert.AreEqual(30 - 12, star.Mines);
        }

        [Test]
        public void AFleetWithNothingThatBombs_DoesNothing()
        {
            star.Factories = 10;
            AddBomber((CombatTestKit.BeamPart(100, 1), 1));

            Bomb();

            Assert.AreEqual(10000, star.Colonists);
            Assert.AreEqual(10, star.Factories);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }
    }
}
