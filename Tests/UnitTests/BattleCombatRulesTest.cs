namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Drawing;
    using System.Reflection;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Shared builders for the spec-9 combat-rule tests below: synthetic hulls/components built
    /// through the same Hull/HullModule/Component aggregation ShipDesign.Update() uses for real
    /// designs, and a two-empire battle with every private BattleEngine step reachable by
    /// reflection.
    /// </summary>
    internal static class CombatTestKit
    {
        public const int WolfId = 1;
        public const int LambId = 2;

        public static Component Part(ItemType type, string propertyKey, ComponentProperty property)
        {
            Component component = new Component { Type = type };
            component.Properties.Add(propertyKey, property);
            return component;
        }

        public static Component BeamPart(int power, int range, WeaponType group = WeaponType.standardBeam)
        {
            return Part(ItemType.BeamWeapons, "Weapon", new Weapon { Power = power, Range = range, Accuracy = 100, Group = group });
        }

        public static Component MissilePart(int power, int accuracy, WeaponType group)
        {
            return Part(ItemType.Torpedoes, "Weapon", new Weapon { Power = power, Range = 4, Accuracy = accuracy, Group = group });
        }

        public static Component DeflectorPart()
        {
            return Part(ItemType.Mechanical, "Beam Deflector", new ProbabilityProperty(10));
        }

        public static Component CapacitorPart(int rate)
        {
            return Part(ItemType.Electrical, "Capacitor", new CapacitorProperty(rate));
        }

        public static Component ComputerPart(int initiative, int accuracy)
        {
            return Part(ItemType.Electrical, "Computer", new Computer { Initiative = initiative, Accuracy = accuracy });
        }

        public static Component JammerPart(int jam)
        {
            return Part(ItemType.Electrical, "Jammer", new ProbabilityProperty(jam));
        }

        public static Component ShieldPart(int shield)
        {
            return Part(ItemType.Shield, "Shield", new IntegerProperty(shield));
        }

        public static ShipDesign Design(long key, int hullArmor, Race race, bool starbase, params (Component part, int count)[] slots)
        {
            Component blueprint = new Component { Cost = new Resources(0, 50, 0, 50), Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = starbase ? 0 : 1000, ArmorStrength = hullArmor };
            foreach ((Component part, int count) in slots)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = count });
            }

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Name = "Design" + key, Blueprint = blueprint };
            design.Update(race);
            return design;
        }

        public static ShipDesign Design(long key, int hullArmor, params (Component part, int count)[] slots)
        {
            return Design(key, hullArmor, null, false, slots);
        }

        public static ServerData TwoEmpires()
        {
            ServerData serverState = new ServerData();
            EmpireData wolf = new EmpireData { Id = WolfId };
            EmpireData lamb = new EmpireData { Id = LambId };
            serverState.AllEmpires[wolf.Id] = wolf;
            serverState.AllEmpires[lamb.Id] = lamb;
            wolf.EmpireReports.Add(lamb.Id, new EmpireIntel(lamb) { Relation = PlayerRelation.Enemy });
            lamb.EmpireReports.Add(wolf.Id, new EmpireIntel(wolf) { Relation = PlayerRelation.Enemy });
            wolf.BattlePlans["Default"] = new BattlePlan { Attack = "Everyone", PrimaryTarget = "Any", SecondaryTarget = "Any" };
            lamb.BattlePlans["Default"] = new BattlePlan { Attack = "Everyone", PrimaryTarget = "Any", SecondaryTarget = "Any" };
            return serverState;
        }

        public static BattleEngine Engine(ServerData serverState, List<Stack> stacks)
        {
            BattleReport report = new BattleReport();
            report.Losses[WolfId] = 0;
            report.Losses[LambId] = 0;
            BattleEngine engine = new BattleEngine(serverState, report);
            typeof(BattleEngine).GetField("currentBattleStacks", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(engine, stacks);
            return engine;
        }

        public static Stack MakeStack(ShipDesign design, int owner, Point position, uint stackId, int quantity = 1)
        {
            Fleet fleet = new Fleet("fleet-" + owner + "-" + stackId, (ushort)owner, stackId, position);
            ShipToken token = new ShipToken(design, quantity);
            fleet.Composition.Add(token.Key, token);
            return new Stack(fleet, stackId, token);
        }

        public static object Invoke(BattleEngine engine, string method, params object[] args)
        {
            return typeof(BattleEngine).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(engine, args);
        }

        public static bool Fire(BattleEngine engine, Stack attacker, Stack target, Weapon weapon)
        {
            WeaponDetails attack = new WeaponDetails { SourceStack = attacker, TargetStack = target, Weapon = weapon };
            return (bool)Invoke(engine, "ProcessAttack", attack);
        }
    }

    /// <summary>
    /// Design-level combat stats: beam deflectors (previously never wired at all), computer
    /// stacking (Computer.operator+ returned its left operand) and capacitors -
    /// behavior-specs-9/combat-resolution.md §6, §7a, §10d.
    /// </summary>
    [TestFixture]
    public class BattleDesignCombatStatsTest
    {
        [TestCase(0, 100)]
        [TestCase(1, 90)]
        [TestCase(2, 81)]
        [TestCase(3, 72)]
        [TestCase(4, 65)]
        public void BeamDeflectorPercent_TruncatesEachStepOnAThousandScale(int deflectors, int expectedPercent)
        {
            ShipDesign design = deflectors == 0
                ? CombatTestKit.Design(1, 100)
                : CombatTestKit.Design(1, 100, (CombatTestKit.DeflectorPart(), deflectors));

            Assert.AreEqual(expectedPercent, design.BeamDeflectorPercent, "x0.9 per deflector on a 1000 scale: 900, 810, 729, 656 -> 90/81/72/65 (spec §6)");
            Assert.AreEqual(100 - expectedPercent, design.BeamDeflectors, 1e-9);
        }

        [Test]
        public void BeamDeflectors_InSeparateSlots_StackTheSameWay()
        {
            ShipDesign design = CombatTestKit.Design(1, 100, (CombatTestKit.DeflectorPart(), 1), (CombatTestKit.DeflectorPart(), 1), (CombatTestKit.DeflectorPart(), 1));

            Assert.AreEqual(72, design.BeamDeflectorPercent);
        }

        [Test]
        public void BeamDeflector_FromComponentsXml_ReducesBeamDamage()
        {
            // The Beam Deflector record exactly as components.xml stores it: its property type is
            // "Beam Deflector" (which SumProperty used to drop, while BeamDeflectors read a
            // never-written "Deflector" key).
            XmlDocument xml = new XmlDocument();
            xml.LoadXml(
                "<Component><Item><Key>0</Key><Name>Beam Deflector</Name><Type>Mechanical</Type></Item>"
                + "<Mass>35</Mass><Cost><Boranium>0</Boranium><Ironium>0</Ironium><Germanium>10</Germanium><Energy>8</Energy></Cost>"
                + "<Tech><Biotechnology>0</Biotechnology><Electronics>6</Electronics><Energy>6</Energy><Propulsion>0</Propulsion><Weapons>6</Weapons><Construction>6</Construction></Tech>"
                + "<Description></Description><Race_Restrictions /><Image>Mechanical/Beam_Deflector.png</Image>"
                + "<Property><Value>10</Value><Type>Beam Deflector</Type></Property></Component>");
            Component loaded = new Component(xml.DocumentElement);
            ShipDesign design = CombatTestKit.Design(1, 100, (loaded, 2));

            Assert.AreEqual(81, design.BeamDeflectorPercent);
            Assert.AreEqual(19, design.BeamDeflectors, 1e-9);
        }

        [Test]
        public void Computers_InDifferentSlots_StackAccuracyAndInitiative()
        {
            ShipDesign design = CombatTestKit.Design(1, 100, (CombatTestKit.ComputerPart(1, 20), 1), (CombatTestKit.ComputerPart(2, 30), 1));

            Assert.AreEqual(44, design.ComputerAccuracy, 1e-9, "Diminishing returns: 100 - 80 x 70 / 100 (spec §7a output (b))");
            Assert.AreEqual(3, design.Initiative, "Initiative from both computers");
        }

        [Test]
        public void ComputerOperatorPlus_ReturnsTheSum()
        {
            Computer sum = new Computer { Initiative = 1, Accuracy = 20 } + new Computer { Initiative = 2, Accuracy = 30 };

            Assert.AreEqual(3, sum.Initiative);
            Assert.AreEqual(44, sum.Accuracy, 1e-9);
        }

        [Test]
        public void Computers_InOneSlot_StillDiminish()
        {
            ShipDesign design = CombatTestKit.Design(1, 100, (CombatTestKit.ComputerPart(1, 20), 2));

            Assert.AreEqual(36, design.ComputerAccuracy, 1e-9);
            Assert.AreEqual(2, design.Initiative);
        }

        [TestCase(0, 0, 100)]
        [TestCase(1, 0, 110)]
        [TestCase(2, 0, 121)]
        [TestCase(5, 0, 160)]
        [TestCase(0, 1, 120)]
        [TestCase(1, 1, 132)]
        [TestCase(0, 6, 255)]
        [TestCase(20, 0, 255)]
        public void CapacitorPercent_CompoundsPerCapacitor_CappedAt255(int energy, int flux, int expected)
        {
            List<(Component, int)> slots = new List<(Component, int)>();
            if (energy > 0)
            {
                slots.Add((CombatTestKit.CapacitorPart(10), energy));
            }

            if (flux > 0)
            {
                slots.Add((CombatTestKit.CapacitorPart(20), flux));
            }

            ShipDesign design = CombatTestKit.Design(1, 100, slots.ToArray());

            Assert.AreEqual(expected, design.CapacitorPercent);
        }

        [Test]
        public void CapacitorProperty_BonusCapIsTheTotal255Percent()
        {
            Assert.AreEqual(155, (new CapacitorProperty(20) * 10).Value, 1e-9, "Total beam damage is capped at 255% of base, i.e. a bonus of 155 (spec §6)");
        }
    }

    /// <summary>
    /// The confirmed accuracy formula (behavior-specs-9/combat-resolution.md §6, FUN_10f0_41ca)
    /// and its three published cross-check figures.
    /// </summary>
    [TestFixture]
    public class BattleAccuracyFormulaTest
    {
        private static int Accuracy(int baseAccuracy, Component attackerExtra, Component targetExtra)
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            Component missile = CombatTestKit.MissilePart(85, baseAccuracy, WeaponType.missile);
            ShipDesign wolfDesign = attackerExtra == null
                ? CombatTestKit.Design(1, 100, (missile, 1))
                : CombatTestKit.Design(1, 100, (missile, 1), (attackerExtra, 1));
            ShipDesign lambDesign = targetExtra == null ? CombatTestKit.Design(2, 100) : CombatTestKit.Design(2, 100, (targetExtra, 1));

            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(lambDesign, CombatTestKit.LambId, new Point(0, 0), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            return (int)(double)CombatTestKit.Invoke(engine, "CalculateWeaponAccuracy", wolf, wolfDesign.Weapons[0], lamb);
        }

        [Test]
        public void BattleSuperComputer_RaisesBase20To44()
        {
            Assert.AreEqual(44, Accuracy(20, CombatTestKit.ComputerPart(2, 30), null));
        }

        [Test]
        public void Jammer20_LowersBase20To16()
        {
            Assert.AreEqual(16, Accuracy(20, null, CombatTestKit.JammerPart(20)));
        }

        [Test]
        public void BattleSuperComputerAgainstJammer20_Gives28()
        {
            Assert.AreEqual(28, Accuracy(20, CombatTestKit.ComputerPart(2, 30), CombatTestKit.JammerPart(20)));
        }

        [Test]
        public void NoComputerNoJammer_IsTheBaseAccuracy()
        {
            Assert.AreEqual(75, Accuracy(75, null, null));
        }

        [Test]
        public void Accuracy_IsFlooredAtOne()
        {
            Assert.AreEqual(1, Accuracy(1, null, CombatTestKit.JammerPart(50)));
        }
    }

    /// <summary>
    /// Beam target score (behavior-specs-9/combat-resolution.md §4 and its resolved Open
    /// Question): cost x deflector% / (armor + shields + 1) for a standard beam, and
    /// ceil(cost x deflector% / shields) for a sapper, which never picks a shieldless token.
    /// </summary>
    [TestFixture]
    public class BattleBeamTargetScoreTest
    {
        [Test]
        public void StandardBeam_Score_IsCostTimesDeflectorOverArmorPlusShieldsPlusOne()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(10, 2), 1));
            ShipDesign lambDesign = CombatTestKit.Design(2, 99, (CombatTestKit.DeflectorPart(), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(lambDesign, CombatTestKit.LambId, new Point(1, 0), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            // cost = boranium 50 + resources 50 = 100, scaled x100 = 10,000 (behavior-specs-10 §4);
            // deflector 90% -> 9,000; armor 99, no shields: 100 x 9,000 / (99 + 0 + 1) = 9,000.
            Assert.AreEqual(100.0 * 9000 / 100, engine.GetAttractiveness(wolf, lamb), 1e-9);
        }

        [Test]
        public void Sapper_ScoresAShieldlessTokenZero_AndUsesCeilOfCostTimesDeflectorOverShields()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(10, 2, WeaponType.shieldSapper), 1));
            ShipDesign shieldless = CombatTestKit.Design(2, 100);
            ShipDesign shielded = CombatTestKit.Design(3, 100, (CombatTestKit.ShieldPart(30), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack bare = CombatTestKit.MakeStack(shieldless, CombatTestKit.LambId, new Point(1, 0), 2);
            Stack covered = CombatTestKit.MakeStack(shielded, CombatTestKit.LambId, new Point(1, 0), 3);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, bare, covered });

            Assert.AreEqual(0, engine.GetAttractiveness(wolf, bare), "A shieldless token scores zero for a sapper (previously double.MaxValue - the MOST attractive)");
            // Scaled cost 10,000 (spec-10 §4): ceil(100 x 10,000 / 30).
            Assert.AreEqual(System.Math.Ceiling(100.0 * 10000 / 30), engine.GetAttractiveness(wolf, covered), 1e-9);

            engine.SelectTargets(new List<Stack> { wolf, bare, covered });
            Assert.AreSame(covered, wolf.Target);
        }

        [Test]
        public void Sapper_WithOnlyShieldlessEnemies_PicksNothing()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(10, 2, WeaponType.shieldSapper), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack bare = CombatTestKit.MakeStack(CombatTestKit.Design(2, 100), CombatTestKit.LambId, new Point(1, 0), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, bare });

            engine.SelectTargets(new List<Stack> { wolf, bare });

            Assert.IsNull(wolf.Target);
        }
    }

    /// <summary>
    /// The beam shot's exact order of operations, reach and damage application
    /// (behavior-specs-9/combat-resolution.md §6).
    /// </summary>
    [TestFixture]
    public class BattleBeamDamageTest
    {
        private static double Power(BattleEngine engine, Stack wolf, Stack lamb)
        {
            return (double)CombatTestKit.Invoke(engine, "CalculateWeaponPower", wolf, wolf.Token.Design.Weapons[0], lamb);
        }

        [TestCase(0, 0, 26)]
        [TestCase(1, 0, 25)]
        [TestCase(1, 1, 25)]
        [TestCase(2, 1, 24)]
        [TestCase(3, 3, 23)]
        public void RangeFalloff_IsWholePercentOfTenTimesGridDistanceOverRange(int dx, int dy, int expected)
        {
            // Worked Example 1: Colloidal Phaser 26 dp, range 3. Grid distance is the larger of
            // the column and row differences, so (1,1) is distance 1.
            ServerData serverState = CombatTestKit.TwoEmpires();
            Stack wolf = CombatTestKit.MakeStack(CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(26, 3), 1)), CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 100), CombatTestKit.LambId, new Point(dx, dy), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            Assert.AreEqual(expected, Power(engine, wolf, lamb));
        }

        [Test]
        public void Capacitors_ThenDeflectors_ThenFalloff_EachStepTruncating()
        {
            // One slot of 2 weapons x 3 ships x 50 = 300; x121% (two Energy Capacitors) = 363;
            // x81% (two deflectors) = 294 (294.03); range 3 at grid distance 2 loses 6% -> 276
            // (276.36).
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(50, 3), 2), (CombatTestKit.CapacitorPart(10), 2));
            ShipDesign lambDesign = CombatTestKit.Design(2, 100, (CombatTestKit.DeflectorPart(), 2));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1, quantity: 3);
            Stack lamb = CombatTestKit.MakeStack(lambDesign, CombatTestKit.LambId, new Point(2, 1), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            Assert.AreEqual(276, Power(engine, wolf, lamb));
        }

        [Test]
        public void StarbaseBonusSquare_ReachesOneFurther_ButFalloffUsesStoredRange()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign starbase = CombatTestKit.Design(1, 1000, null, true, (CombatTestKit.BeamPart(100, 1), 1));
            Stack wolf = CombatTestKit.MakeStack(starbase, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 1000), CombatTestKit.LambId, new Point(2, 0), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            Assert.IsTrue(CombatTestKit.Fire(engine, wolf, lamb, starbase.Weapons[0]), "A starbase's range-1 beam reaches distance 2");
            Assert.AreEqual(1000 - 80, lamb.Token.Armor, 1e-9, "...losing 20% (10 x 2 / 1), not 10%");
        }

        [Test]
        public void RangeZeroBeam_NeverLosesDamage_EvenAtTheStarbaseBonusSquare()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign starbase = CombatTestKit.Design(1, 1000, null, true, (CombatTestKit.BeamPart(100, 0), 1));
            Stack wolf = CombatTestKit.MakeStack(starbase, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 1000), CombatTestKit.LambId, new Point(1, 1), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            Assert.IsTrue(CombatTestKit.Fire(engine, wolf, lamb, starbase.Weapons[0]));
            Assert.AreEqual(900, lamb.Token.Armor, 1e-9);
        }

        [Test]
        public void ShipBeam_DoesNotReachBeyondItsStoredRange()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(100, 1), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 1000), CombatTestKit.LambId, new Point(2, 1), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            Assert.IsFalse(CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]));
            Assert.AreEqual(1000, lamb.Token.Armor, 1e-9);
        }

        [Test]
        public void Sapper_DrainsShieldsButNeverTouchesArmor()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(200, 1, WeaponType.shieldSapper), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 300, (CombatTestKit.ShieldPart(50), 1)), CombatTestKit.LambId, new Point(0, 0), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(0, lamb.Token.Shields, 1e-9);
            Assert.AreEqual(300, lamb.Token.Armor, 1e-9, "The 150 left after the shields must not reach armor");
        }

        [Test]
        public void Sapper_HasNoEffectOnAShieldlessToken()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(200, 1, WeaponType.shieldSapper), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 300), CombatTestKit.LambId, new Point(0, 0), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(300, lamb.Token.Armor, 1e-9);
            Assert.IsFalse(lamb.HasTakenDamage);
        }

        [Test]
        public void PartialShieldDamage_LeavesTheRemainingPoolDividedEvenlyRoundedDown()
        {
            // 3 ships x 50 shields = 150; 40 damage leaves 110, i.e. 36.67 per ship -> 36 x 3.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(40, 0), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 300, (CombatTestKit.ShieldPart(50), 1)), CombatTestKit.LambId, new Point(0, 0), 2, quantity: 3);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(108, lamb.Token.Shields, 1e-9);
        }

        [Test]
        public void StandardBeam_OverflowsOntoTheNextTargetInReach_WithItsOwnDeflectorsApplied()
        {
            // 300 dp at distance 0: kills the 100-armor token, 200 carries on, and the next token's
            // single deflector takes it to 180.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(300, 1), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack first = CombatTestKit.MakeStack(CombatTestKit.Design(2, 100), CombatTestKit.LambId, new Point(0, 0), 2);
            Stack second = CombatTestKit.MakeStack(CombatTestKit.Design(3, 1000, (CombatTestKit.DeflectorPart(), 1)), CombatTestKit.LambId, new Point(0, 0), 3);
            Stack outOfReach = CombatTestKit.MakeStack(CombatTestKit.Design(4, 1000), CombatTestKit.LambId, new Point(5, 5), 4);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, first, second, outOfReach });

            CombatTestKit.Fire(engine, wolf, first, wolfDesign.Weapons[0]);

            Assert.IsTrue(first.IsDestroyed);
            Assert.AreEqual(1000 - 180, second.Token.Armor, 1e-9);
            Assert.AreEqual(1000, outOfReach.Token.Armor, 1e-9);
        }

        [Test]
        public void ShotThatHitsAStarbase_NeverOverflows()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(300, 1), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack starbase = CombatTestKit.MakeStack(CombatTestKit.Design(2, 100, null, true), CombatTestKit.LambId, new Point(0, 0), 2);
            Stack bystander = CombatTestKit.MakeStack(CombatTestKit.Design(3, 1000), CombatTestKit.LambId, new Point(0, 0), 3);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, starbase, bystander });

            CombatTestKit.Fire(engine, wolf, starbase, wolfDesign.Weapons[0]);

            Assert.IsTrue(starbase.IsDestroyed);
            Assert.AreEqual(1000, bystander.Token.Armor, 1e-9);
        }

        [Test]
        public void Gatling_HitsEveryEligibleTokenInReach_WithNoFalloff()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(100, 2, WeaponType.gatlingGun), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack near = CombatTestKit.MakeStack(CombatTestKit.Design(2, 1000), CombatTestKit.LambId, new Point(0, 0), 2);
            Stack atRange = CombatTestKit.MakeStack(CombatTestKit.Design(3, 1000, (CombatTestKit.DeflectorPart(), 1)), CombatTestKit.LambId, new Point(2, 2), 3);
            Stack tooFar = CombatTestKit.MakeStack(CombatTestKit.Design(4, 1000), CombatTestKit.LambId, new Point(3, 0), 4);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, near, atRange, tooFar });

            Assert.IsTrue(CombatTestKit.Fire(engine, wolf, near, wolfDesign.Weapons[0]));

            Assert.AreEqual(900, near.Token.Armor, 1e-9);
            Assert.AreEqual(910, atRange.Token.Armor, 1e-9, "Full damage x its own 90% deflector, no range falloff at distance 2");
            Assert.AreEqual(1000, tooFar.Token.Armor, 1e-9);
        }
    }

    [TestFixture]
    public class BattleStartShieldsTest
    {
        [Test]
        public void DoBattle_StartsEveryTokenAtFullShields()
        {
            // "Every battle starts each token at full shields; only armor damage persists between
            // battles" (behavior-specs-9/combat-resolution.md §6). No enemies here, so the battle
            // ends before any round is fought.
            ServerData serverState = CombatTestKit.TwoEmpires();
            Stack stack = CombatTestKit.MakeStack(CombatTestKit.Design(1, 100, (CombatTestKit.ShieldPart(50), 1)), CombatTestKit.WolfId, new Point(0, 0), 1, quantity: 2);
            stack.Token.Shields = 10;
            stack.Token.Armor = 150;
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { stack });

            engine.DoBattle(new List<Stack> { stack });

            Assert.AreEqual(100, stack.Token.Shields, 1e-9);
            Assert.AreEqual(150, stack.Token.Armor, 1e-9, "Armor damage is not repaired");
        }
    }

    /// <summary>
    /// Missile salvo rules (behavior-specs-9/combat-resolution.md §6): the capital-missile
    /// doubling is decided once per salvo, and one missile can kill at most one ship. Uses 100%
    /// accuracy so every missile hits.
    /// </summary>
    [TestFixture]
    public class BattleMissileSalvoTest
    {
        [Test]
        public void CapitalMissileDoubling_IsDecidedBeforeTheSalvo_NotWhenShieldsFallMidSalvo()
        {
            // 3 x 85 = 255 against 25 shields: shields take 25 of the 127 half, armor takes the
            // other 102 plus a second 127, 229 (the half is truncated twice, behavior-specs-10 §6)
            // - no doubling, since the shields were up when the salvo started.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.MissilePart(85, 100, WeaponType.missile), 3));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 1000, (CombatTestKit.ShieldPart(25), 1)), CombatTestKit.LambId, new Point(0, 0), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            // The 229 points are stored in the damage word rounded up to the next 1/500 of the
            // 1,000 armor: 115 units = 230 points (behavior-specs-10 §8).
            Assert.AreEqual(0, lamb.Token.Shields, 1e-9);
            Assert.AreEqual(1000 - 230, lamb.Token.Armor, 1e-9);

            // The next salvo finds the pool already empty: each missile counts double (510 more,
            // 740 carried = exactly 370 units).
            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);
            Assert.AreEqual(1000 - 740, lamb.Token.Armor, 1e-9);
        }

        [Test]
        public void Torpedoes_AreNeverDoubled()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.MissilePart(90, 100, WeaponType.torpedo), 2));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 1000), CombatTestKit.LambId, new Point(0, 0), 2);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(1000 - 180, lamb.Token.Armor, 1e-9);
        }

        [Test]
        public void OneMissileOneKill_SurplusDamageIsDiscarded()
        {
            // One 1000-dp torpedo against five 100-armor ships kills exactly one of them.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.MissilePart(1000, 100, WeaponType.torpedo), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 100), CombatTestKit.LambId, new Point(0, 0), 2, quantity: 5);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(4, lamb.Token.Quantity);
            Assert.AreEqual(400, lamb.Token.Armor, 1e-9, "The 900 left after the one kill is discarded, not spread over the survivors");
        }

        [Test]
        public void Beams_HaveNoKillLimit()
        {
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(1000, 0), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 100), CombatTestKit.LambId, new Point(0, 0), 2, quantity: 5);
            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });

            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.IsTrue(lamb.IsDestroyed);
        }
    }
}
