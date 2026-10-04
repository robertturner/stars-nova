namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Drawing;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Covers two of the combat-resolution audit's confirmed gaps against
    /// docs/behavior-specs-5/combat-resolution.md: §4's Primary/Secondary target-type
    /// filtering (previously BattleEngine.SelectTargets picked the single most attractive
    /// enemy stack across ALL types, ignoring BattlePlan.PrimaryTarget/SecondaryTarget
    /// entirely), and §3/§7's Disengage 7-square retreat threshold (previously tracked via
    /// Stack.DisengageDistanceAccumulated but never actually used to remove a stack from the
    /// battle).
    /// </summary>
    [TestFixture]
    public class BattleTargetTypeAndDisengageTest
    {
        private const int WolfId = 1;
        private const int LambId = 2;

        private ServerData serverState;
        private BattleEngine battleEngine;

        [SetUp]
        public void Init()
        {
            serverState = new ServerData();
            battleEngine = new BattleEngine(serverState, new BattleReport());

            EmpireData wolfEmpire = new EmpireData { Id = WolfId };
            EmpireData lambEmpire = new EmpireData { Id = LambId };

            serverState.AllEmpires[wolfEmpire.Id] = wolfEmpire;
            serverState.AllEmpires[lambEmpire.Id] = lambEmpire;

            wolfEmpire.EmpireReports.Add(lambEmpire.Id, new EmpireIntel(lambEmpire) { Relation = PlayerRelation.Enemy });
            lambEmpire.EmpireReports.Add(wolfEmpire.Id, new EmpireIntel(wolfEmpire) { Relation = PlayerRelation.Enemy });

            wolfEmpire.BattlePlans["Default"] = new BattlePlan { Attack = "Everyone" };
            lambEmpire.BattlePlans["Default"] = new BattlePlan { Attack = "Everyone" };
        }

        /// <summary>
        /// A design with a real weapon slot, built via the same Hull/HullModule/Component
        /// aggregation ShipDesign.Update() uses for real designs - so its Weapons list (and
        /// therefore Weapon.Count, the WeaponsInSlot value FireMissile relies on) is populated
        /// the same way real gameplay data is, rather than poking the field directly.
        /// </summary>
        private static ShipDesign BuildDesign(long key, string name, int fuelCapacity, int baseCargo, int weaponSlotCount)
        {
            Component blueprint = new Component
            {
                Cost = new Resources(10, 10, 10, 10),
                Mass = 100,
            };

            Hull hull = new Hull
            {
                FuelCapacity = fuelCapacity,
                BaseCargo = baseCargo,
                ArmorStrength = 100,
                Modules = new List<HullModule>(),
            };

            if (weaponSlotCount > 0)
            {
                Component weaponComponent = new Component();
                weaponComponent.Properties.Add("Weapon", new Weapon { Power = 10, Range = 1, Accuracy = 75, Group = WeaponType.torpedo });

                HullModule weaponModule = new HullModule
                {
                    AllocatedComponent = weaponComponent,
                    ComponentCount = weaponSlotCount,
                };

                hull.Modules.Add(weaponModule);
            }

            blueprint.Properties.Add("Hull", hull);
            // Engine-less test hull: under the behavior-specs-10 §5 integer movement rule its
            // value is 0 (base) + 4 x this - 4 - 100/70, so 2.0 squares of "Battle Movement" give
            // v = 3 - enough to move on round 0 of the movement table.
            blueprint.Properties.Add("Battle Movement", new DoubleProperty(2.0));

            ShipDesign design = new ShipDesign(key) { Name = name, Blueprint = blueprint };
            design.Update();
            return design;
        }

        private static Stack MakeStack(ShipDesign design, int owner, Point position, int armor)
        {
            Fleet fleet = new Fleet("fleet-" + owner + "-" + design.Name, (ushort)owner, 1, position);
            ShipToken token = new ShipToken(design, 1) { Armor = armor };
            fleet.Composition.Add(token.Key, token);
            return new Stack(fleet, 0, token);
        }

        [Test]
        public void SelectTargets_PrimaryTypeUnmatched_FallsThroughToSecondary_ThenToNone()
        {
            ShipDesign wolfDesign = BuildDesign(1, "Wolf", fuelCapacity: 100, baseCargo: 0, weaponSlotCount: 1);
            ShipDesign unarmedLambDesign = BuildDesign(2, "UnarmedLamb", fuelCapacity: 100, baseCargo: 0, weaponSlotCount: 0);

            Stack wolf = MakeStack(wolfDesign, WolfId, new Point(0, 0), 100);
            Stack lamb = MakeStack(unarmedLambDesign, LambId, new Point(1, 0), 100);

            serverState.AllEmpires[WolfId].BattlePlans["Default"].PrimaryTarget = "Armed Ships";
            serverState.AllEmpires[WolfId].BattlePlans["Default"].SecondaryTarget = "None";

            List<Stack> stacks = new List<Stack> { wolf, lamb };
            battleEngine.SelectTargets(stacks);

            // Note: the lamb's own (unmodified default) Battle Plan targets "Armed Ships" and
            // the wolf IS armed, so lamb.Target legitimately ends up pointing at wolf - this
            // assertion is only about the wolf's own targeting, which is what Primary=Armed
            // Ships/Secondary=None actually constrains.
            Assert.IsNull(wolf.Target, "An unarmed lamb shouldn't be selected when Primary=Armed Ships and Secondary=None leaves no matching type at all.");
        }

        [Test]
        public void SelectTargets_PrefersPrimaryTypeMatch_OverAnUnarmedDecoy()
        {
            ShipDesign wolfDesign = BuildDesign(1, "Wolf", fuelCapacity: 100, baseCargo: 0, weaponSlotCount: 1);
            ShipDesign armedLambDesign = BuildDesign(2, "ArmedLamb", fuelCapacity: 100, baseCargo: 0, weaponSlotCount: 1);
            ShipDesign unarmedLambDesign = BuildDesign(3, "UnarmedLamb", fuelCapacity: 100, baseCargo: 0, weaponSlotCount: 0);

            Stack wolf = MakeStack(wolfDesign, WolfId, new Point(0, 0), 100);
            Stack armedLamb = MakeStack(armedLambDesign, LambId, new Point(1, 0), 100);
            Stack unarmedLamb = MakeStack(unarmedLambDesign, LambId, new Point(2, 0), 100);

            serverState.AllEmpires[WolfId].BattlePlans["Default"].PrimaryTarget = "Armed Ships";
            serverState.AllEmpires[WolfId].BattlePlans["Default"].SecondaryTarget = "Any";

            // Both lambs are otherwise identical in cost/armor - only their weapon count differs
            // (and therefore which target-type category matches) - so a pre-fix "most attractive
            // overall, ignoring type" selection would be free to pick either one arbitrarily. The
            // type filter must deterministically prefer the armed one, since it's the Primary
            // Target type and a match exists.
            List<Stack> stacks = new List<Stack> { wolf, armedLamb, unarmedLamb };
            battleEngine.SelectTargets(stacks);

            Assert.AreSame(armedLamb, wolf.Target, "Wolf's Primary Target type (Armed Ships) has a real match present, so it must be preferred over the unarmed decoy.");
        }

        [Test]
        public void MatchesTargetType_StarbaseAndUnarmedAndFreighterClassification()
        {
            ShipDesign starbaseDesign = BuildDesign(1, "Starbase", fuelCapacity: 0, baseCargo: 0, weaponSlotCount: 0);
            ShipDesign freighterDesign = BuildDesign(2, "Freighter", fuelCapacity: 100, baseCargo: 500, weaponSlotCount: 0);
            ShipDesign armedDesign = BuildDesign(3, "Warship", fuelCapacity: 100, baseCargo: 0, weaponSlotCount: 2);

            Assert.IsTrue(starbaseDesign.IsStarbase);
            Assert.IsFalse(freighterDesign.IsStarbase);
            Assert.Greater(freighterDesign.CargoCapacity, 0);
            Assert.Greater(armedDesign.Weapons.Count, 0, "Sanity check - the 'armed' design must actually carry weapons.");
            Assert.Greater(armedDesign.Weapons[0].Count, 0, "Weapon.Count should reflect the aggregated WeaponsInSlot value.");
            Assert.AreEqual(2, armedDesign.Weapons[0].Count, "Two identical torpedo launchers in one slot should aggregate to Count=2.");
        }

        /// <summary>
        /// Weapon.Count (the WeaponsInSlot value FireMissile divides hitPower by to resolve
        /// each missile independently - see docs/behavior-specs-5/combat-resolution.md §6)
        /// must survive the same Power-scaling operators ShipDesign.SumProperty uses to fold
        /// multiple identical components in one hull slot into a single aggregated Weapon.
        /// </summary>
        [Test]
        public void WeaponOperatorMultiply_ScalesCountAlongsidePower()
        {
            Weapon singleLauncher = new Weapon { Power = 10, Count = 1 };

            Weapon threeInOneSlot = singleLauncher * 3;

            Assert.AreEqual(30, threeInOneSlot.Power);
            Assert.AreEqual(3, threeInOneSlot.Count, "Multiplying a Weapon by a component count must scale Count the same way it scales Power.");
        }

        [Test]
        public void Disengage_CrossingSevenSquares_SetsHasRetreated_AndRemovesItFromTargeting()
        {
            ShipDesign fleeingDesign = BuildDesign(1, "Fleeing", fuelCapacity: 100, baseCargo: 0, weaponSlotCount: 1);
            ShipDesign chaserDesign = BuildDesign(2, "Chaser", fuelCapacity: 100, baseCargo: 0, weaponSlotCount: 1);

            Stack fleeing = MakeStack(fleeingDesign, WolfId, new Point(5, 5), 100);
            Stack chaser = MakeStack(chaserDesign, LambId, new Point(5, 6), 100);

            serverState.AllEmpires[WolfId].BattlePlans["Default"].Tactic = "Disengage";

            // Just below the 7-square threshold - one more square of movement should cross it.
            fleeing.DisengageDistanceAccumulated = 6.5;

            List<Stack> stacks = new List<Stack> { fleeing, chaser };

            // The two tokens weigh the same, so their movement order is a coin flip
            // (combat-resolution.md section 3, near-parity randomization); pin it to list order
            // (the fleeing token first) so the test is deterministic.
            battleEngine = new BattleEngine(serverState, new BattleReport(), new NeverSwapRandom());

            // SelectTargets must run first each round in real play (DoBattle's own order) so
            // stack.Target is populated - MoveStacks only moves stacks that have one.
            battleEngine.SelectTargets(stacks);
            Assume.That(fleeing.HasRetreated, Is.False, "Shouldn't have retreated yet - still under threshold.");

            battleEngine.MoveStacks(stacks);

            Assert.IsTrue(fleeing.HasRetreated, "Crossing the 7-square Disengage threshold should mark the stack as having successfully fled the battle.");

            // Once retreated, it must drop out of targeting entirely - both as a wolf (nothing
            // left to pick a target with) and as a lamb (nobody else can target it either).
            fleeing.Target = null;
            chaser.Target = null;
            int numberOfTargets = battleEngine.SelectTargets(stacks);

            Assert.IsNull(chaser.Target, "A retreated stack must not be selectable as anyone else's target.");
            Assert.AreEqual(0, numberOfTargets);
        }

        /// <summary>NextDouble always 0.99: a near-parity pair never swaps in the movement order.</summary>
        private class NeverSwapRandom : System.Random
        {
            public NeverSwapRandom()
                : base(1)
            {
            }

            public override double NextDouble()
            {
                return 0.99;
            }
        }
    }
}
