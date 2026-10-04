namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    // Regression tests for Regenerating Shields (RS), as traced in full by
    // behavior-specs-9/race-traits.md §3 (RS, bit 13) and combat-resolution.md §6: shields gain
    // floor(2/5) (capped at 65,535); only the armor of Armor-category parts is halved, rounded
    // down per slot - NOT the hull's base armor, nor the armor carried by shield-category or
    // mechanical parts (Croby Sharmor, Langston Shell, Multi Cargo Pod); and a per-round
    // regeneration of floor(full / 10) per ship from round 2, only for tokens whose shields are
    // not already zero. An earlier version of this file asserted spec-7's flat reading (hull
    // armor 200 -> 100, and regeneration of tokens at zero shields), which spec-9 retracted.
    // Implementing RS originally also surfaced and fixed an adjacent real bug: ShipToken's
    // constructor set Shields to a single ship's rating instead of the whole token's total
    // (Shields*Quantity), unlike Armor, which already scaled correctly.
    [TestFixture]
    public class RegeneratingShieldsDesignMultiplierTest
    {
        private static Race RsRace()
        {
            Race race = new Race();
            race.Traits.Add("RS");
            return race;
        }

        private static Component Part(ItemType type, string key, int value)
        {
            Component component = new Component { Type = type };
            component.Properties.Add(key, new IntegerProperty(value));
            return component;
        }

        private static ShipDesign BuildDesign(Race race, int hullArmor, params (Component part, int count)[] slots)
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000, ArmorStrength = hullArmor };
            foreach ((Component part, int count) in slots)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = count });
            }

            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update(race);
            return design;
        }

        [TestCase(100, 140)]
        [TestCase(25, 35)]
        [TestCase(7, 9)]
        [TestCase(5, 7)]
        [TestCase(50000, 65535)]
        public void Update_RegeneratingShieldsTrait_AddsFloorOfTwoFifths_CappedAt65535(int listed, int expected)
        {
            ShipDesign design = BuildDesign(RsRace(), 0, (Part(ItemType.Shield, "Shield", listed), 1));

            Assert.AreEqual(expected, design.Shield);
        }

        [Test]
        public void Update_RegeneratingShieldsTrait_DoesNotHalveHullBaseArmor()
        {
            ShipDesign design = BuildDesign(RsRace(), 200);

            Assert.AreEqual(200, design.Armor, "Hull base armor is not halved (retracts the old 200 -> 100 expectation)");
        }

        [Test]
        public void Update_RegeneratingShieldsTrait_HalvesArmorCategoryPartsPerSlotRoundedDown()
        {
            // Hull 200 + one slot of 3 x 25 (75 -> 37) + one slot of 1 x 75 (-> 37).
            ShipDesign design = BuildDesign(
                RsRace(),
                200,
                (Part(ItemType.Armor, "Armor", 25), 3),
                (Part(ItemType.Armor, "Armor", 75), 1));

            Assert.AreEqual(200 + 37 + 37, design.Armor);
        }

        [Test]
        public void Update_RegeneratingShieldsTrait_DoesNotHalveArmorOfShieldOrMechanicalParts()
        {
            // Croby Sharmor / Langston Shell (shield category) and the Multi Cargo Pod
            // (mechanical) carry armor that RS leaves alone.
            ShipDesign design = BuildDesign(
                RsRace(),
                100,
                (Part(ItemType.Shield, "Armor", 65), 1),
                (Part(ItemType.Mechanical, "Armor", 50), 2));

            Assert.AreEqual(100 + 65 + 100, design.Armor);
        }

        [Test]
        public void Update_NonRegeneratingShieldsRace_GetsThePlainListedValues()
        {
            ShipDesign design = BuildDesign(
                new Race(),
                200,
                (Part(ItemType.Shield, "Shield", 100), 1),
                (Part(ItemType.Armor, "Armor", 75), 1));

            Assert.AreEqual(100, design.Shield);
            Assert.AreEqual(275, design.Armor);
        }
    }

    [TestFixture]
    public class ShipTokenShieldsScalingTest
    {
        [Test]
        public void Constructor_ScalesShieldsByQuantity_SameConventionAsArmor()
        {
            Component blueprint = new Component { Mass = 100 };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000, ArmorStrength = 10 };
            Component shieldComponent = new Component();
            shieldComponent.Properties.Add("Shield", new IntegerProperty(50));
            hull.Modules.Add(new HullModule { AllocatedComponent = shieldComponent, ComponentCount = 1 });
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update();

            ShipToken token = new ShipToken(design, 5);

            Assert.AreEqual(250, token.Shields, "5 ships * 50 shield rating each - same totalling convention Armor already uses");
            Assert.AreEqual(50, token.Armor, "5 ships * 10 armor rating each (unaffected by this fix, already correct)");
        }
    }

    [TestFixture]
    public class RegeneratingShieldsBattleRoundRegenTest
    {
        private static MethodInfo ApplyRegeneratingShieldsMethod =>
            typeof(BattleEngine).GetMethod("ApplyRegeneratingShields", BindingFlags.NonPublic | BindingFlags.Instance);

        private static ShipDesign BuildDesignWithShield(Race race, int shieldRating)
        {
            Component blueprint = new Component { Mass = 100 };
            // Some hull armor: a token with no armor counts as destroyed, and only surviving
            // tokens regenerate.
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000, ArmorStrength = 100 };
            Component shieldComponent = new Component();
            shieldComponent.Properties.Add("Shield", new IntegerProperty(shieldRating));
            hull.Modules.Add(new HullModule { AllocatedComponent = shieldComponent, ComponentCount = 1 });
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update(race);
            return design;
        }

        [Test]
        public void ApplyRegeneratingShields_RSRace_RestoresTenPercentOfMaximum_NotOfCurrent()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            empire.Race.Traits.Add("RS");
            serverState.AllEmpires.Add(empire.Id, empire);

            // Design's own Shield property is already RS-boosted to 140 (100 * 1.4) by Update.
            ShipDesign design = BuildDesignWithShield(empire.Race, shieldRating: 100);
            Fleet fleet = new Fleet(1) { Owner = empire.Id };
            ShipToken token = new ShipToken(design, 1) { Shields = 50 }; // partially depleted from an earlier round

            Stack stack = new Stack(fleet, 1, token);
            BattleEngine engine = new BattleEngine(serverState, new BattleReport());

            ApplyRegeneratingShieldsMethod.Invoke(engine, new object[] { new List<Stack> { stack } });

            Assert.AreEqual(64, stack.Token.Shields, "50 + (10% of the 140 maximum = 14) = 64 - not 10% of the current 50");
        }

        [Test]
        public void ApplyRegeneratingShields_NeverExceedsMaximumShields()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            empire.Race.Traits.Add("RS");
            serverState.AllEmpires.Add(empire.Id, empire);

            ShipDesign design = BuildDesignWithShield(empire.Race, shieldRating: 100); // max 140
            Fleet fleet = new Fleet(1) { Owner = empire.Id };
            ShipToken token = new ShipToken(design, 1) { Shields = 138 }; // close to the 140 max

            Stack stack = new Stack(fleet, 1, token);
            BattleEngine engine = new BattleEngine(serverState, new BattleReport());

            ApplyRegeneratingShieldsMethod.Invoke(engine, new object[] { new List<Stack> { stack } });

            Assert.AreEqual(140, stack.Token.Shields, "Regen must clamp at the design's own (already RS-boosted) maximum, not overshoot it");
        }

        [Test]
        public void ApplyRegeneratingShields_RegainsFloorOfATenthPerShip_TimesShipCount()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            empire.Race.Traits.Add("RS");
            serverState.AllEmpires.Add(empire.Id, empire);

            // 25 listed -> 35 per ship with RS; a tenth of that rounded down is 3 per ship.
            ShipDesign design = BuildDesignWithShield(empire.Race, shieldRating: 25);
            Fleet fleet = new Fleet(1) { Owner = empire.Id };
            ShipToken token = new ShipToken(design, 4) { Shields = 100 }; // full is 4 x 35 = 140

            Stack stack = new Stack(fleet, 1, token);
            BattleEngine engine = new BattleEngine(serverState, new BattleReport());

            ApplyRegeneratingShieldsMethod.Invoke(engine, new object[] { new List<Stack> { stack } });

            Assert.AreEqual(112, stack.Token.Shields, "100 + floor(35 / 10) x 4 ships = 112, not 100 + 10% of 140 = 114");
        }

        [Test]
        public void ApplyRegeneratingShields_TokenKnockedToZero_NeverRegenerates()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 };
            empire.Race.Traits.Add("RS");
            serverState.AllEmpires.Add(empire.Id, empire);

            ShipDesign design = BuildDesignWithShield(empire.Race, shieldRating: 100);
            Fleet fleet = new Fleet(1) { Owner = empire.Id };
            ShipToken token = new ShipToken(design, 1) { Shields = 0 };

            Stack stack = new Stack(fleet, 1, token);
            BattleEngine engine = new BattleEngine(serverState, new BattleReport());

            ApplyRegeneratingShieldsMethod.Invoke(engine, new object[] { new List<Stack> { stack } });

            Assert.AreEqual(0, stack.Token.Shields, "A token whose shields reached zero gets nothing back for the rest of the battle");
        }

        [Test]
        public void ApplyRegeneratingShields_NonRSRace_NeverRegenerates()
        {
            ServerData serverState = new ServerData();
            EmpireData empire = new EmpireData { Id = 1 }; // no RS trait
            serverState.AllEmpires.Add(empire.Id, empire);

            ShipDesign design = BuildDesignWithShield(empire.Race, shieldRating: 100);
            Fleet fleet = new Fleet(1) { Owner = empire.Id };
            ShipToken token = new ShipToken(design, 1) { Shields = 50 };

            Stack stack = new Stack(fleet, 1, token);
            BattleEngine engine = new BattleEngine(serverState, new BattleReport());

            ApplyRegeneratingShieldsMethod.Invoke(engine, new object[] { new List<Stack> { stack } });

            Assert.AreEqual(50, stack.Token.Shields, "Without Regenerating Shields, nothing restores shields mid-battle");
        }
    }
}
