namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Combat;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>Shared fixture pieces for the H3 turn-step remainder tests.</summary>
    public abstract class H3Kit
    {
        protected ServerData serverState;
        protected EmpireData mover;
        protected EmpireData layer;
        private long nextDesignKey = 900;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            mover = new SimpleEmpireData { Id = 1, Race = new Race { PluralName = "Movers" } };
            layer = new SimpleEmpireData { Id = 2, Race = new Race { PluralName = "Miners" } };
            serverState.AllEmpires.Add(mover.Id, mover);
            serverState.AllEmpires.Add(layer.Id, layer);
            layer.EmpireReports.Add(mover.Id, new EmpireIntel(mover) { Relation = PlayerRelation.Enemy });
            mover.EmpireReports.Add(layer.Id, new EmpireIntel(layer) { Relation = PlayerRelation.Enemy });
        }

        protected ShipDesign MakeDesign(int armor = 1000, bool starbase = false, string partName = null)
        {
            ShipDesign design = new ShipDesign(nextDesignKey++);
            design.Name = "Design " + design.Key;
            design.Icon = new ShipIcon("hull0000.png", null);
            design.Blueprint = new Component { Name = starbase ? "Test Base" : "Test Hull", Mass = 50 };
            Hull hull = new Hull { ArmorStrength = armor, FuelCapacity = starbase ? 0 : 10000, Modules = new List<HullModule>() };
            if (partName != null)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = new Component { Name = partName }, ComponentCount = 1, ComponentMaximum = 1 });
            }

            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            return design;
        }

        protected Fleet AddFleet(EmpireData owner, NovaPoint position, ShipDesign design, int quantity)
        {
            Fleet fleet = new Fleet(owner.GetNextFleetKey());
            fleet.Owner = owner.Id;
            fleet.Name = "Fleet " + fleet.Key;
            fleet.Position = position;
            ShipToken token = new ShipToken(design, quantity);
            fleet.Composition.Add(token.Key, token);
            owner.OwnedFleets.Add(fleet);
            return fleet;
        }

        protected Minefield AddField(EmpireData owner, int x, int y, int mines, MinefieldType type = MinefieldType.Standard, bool detonate = false)
        {
            Minefield field = new Minefield { NumberOfMines = mines, FieldType = type, Detonate = detonate };
            field.Key = owner.GetNextMinefieldKey();
            field.Owner = owner.Id;
            field.Position = new NovaPoint(x, y);
            serverState.AllMinefields[field.Key] = field;
            return field;
        }

        protected static int StepKey<T>(ServerData state)
        {
            SimpleTurnGenerator generator = new SimpleTurnGenerator(state);
            var steps = (SortedList<int, ITurnStep>)typeof(TurnGenerator)
                .GetField("turnSteps", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(generator);
            return steps.Single(s => s.Value is T).Key;
        }
    }

    /// <summary>
    /// Mine sweeping, turn step 24 - behavior-specs-10/turn-generation-engine.md section 11
    /// "Mine sweeping" (coverage fleet row 59, turn row 20). The sweep RATE (damage x range
    /// squared per beam, +1 range on a starbase) is the community rule, not a spec number.
    /// </summary>
    [TestFixture]
    public class MineSweepStepTest : H3Kit
    {
        private ShipDesign Sweeper(int power = 10, int range = 2, WeaponType group = WeaponType.standardBeam, bool starbase = false)
        {
            ShipDesign design = MakeDesign(starbase: starbase);
            design.Weapons.Add(new Weapon { Power = power, Range = range, Group = group });
            return design;
        }

        [Test]
        public void MinesSwept_IsTheRate_AThirdAgainstSpeedBumps_AtLeastTwo_CappedByWhatExcludesTheSweeperAndByTheField()
        {
            Assert.AreEqual(100, MineSweepStep.MinesSwept(100, MinefieldType.Standard, 1000, 0));
            Assert.AreEqual(100, MineSweepStep.MinesSwept(100, MinefieldType.Heavy, 1000, 0));
            Assert.AreEqual(33, MineSweepStep.MinesSwept(100, MinefieldType.SpeedBump, 1000, 0), "one third against the third type");
            Assert.AreEqual(2, MineSweepStep.MinesSwept(1, MinefieldType.Standard, 1000, 0), "at least 2");
            Assert.AreEqual(2, MineSweepStep.MinesSwept(3, MinefieldType.SpeedBump, 1000, 0), "at least 2");
            Assert.AreEqual(101, MineSweepStep.MinesSwept(500, MinefieldType.Standard, 1000, 900), "1000 - 900 + 1 puts the sweeper just outside");
            Assert.AreEqual(300, MineSweepStep.MinesSwept(5000, MinefieldType.Standard, 300, 0), "never more than the field holds");
            Assert.AreEqual(0, MineSweepStep.MinesSwept(0, MinefieldType.Standard, 300, 0), "no rate, no sweep");
        }

        [Test]
        public void SweepRate_IsBeamDamageTimesRangeSquared_StarbasesGetOneMoreRange_SappersAndMissilesNone()
        {
            Assert.AreEqual(10 * 2 * 2, MineSweepStep.SweepRate(Sweeper(10, 2), false));
            Assert.AreEqual(10 * 3 * 3, MineSweepStep.SweepRate(Sweeper(10, 2), true));
            Assert.AreEqual(0, MineSweepStep.SweepRate(Sweeper(10, 2, WeaponType.shieldSapper), false));
            Assert.AreEqual(0, MineSweepStep.SweepRate(Sweeper(10, 4, WeaponType.missile), false));
        }

        [Test]
        public void AFleetInsideAnEnemyField_SweepsItsRate_AndBothOwnersAreTold()
        {
            Minefield field = AddField(layer, 0, 0, 1000);
            AddFleet(mover, new NovaPoint(10, 0), Sweeper(10, 2), 2);

            new MineSweepStep().Process(serverState);

            Assert.AreEqual(1000 - 80, field.NumberOfMines, "2 ships x 10 x 2 squared");
            Assert.AreEqual(1, serverState.AllMessages.Count(m => m.Audience == mover.Id), "message 194");
            Assert.AreEqual(1, serverState.AllMessages.Count(m => m.Audience == layer.Id), "message 190");
        }

        [Test]
        public void OwnFields_FieldsItIsOutside_AndOwnersItsPlanWouldNotAttack_AreNotSwept()
        {
            Minefield own = AddField(mover, 0, 0, 1000);
            Minefield far = AddField(layer, 100, 100, 100);
            AddFleet(mover, new NovaPoint(0, 0), Sweeper(), 1);

            mover.EmpireReports[layer.Id].Relation = PlayerRelation.Neutral;
            Minefield neutralOwners = AddField(layer, 0, 0, 1000);

            new MineSweepStep().Process(serverState);

            Assert.AreEqual(1000, own.NumberOfMines);
            Assert.AreEqual(100, far.NumberOfMines);
            Assert.AreEqual(1000, neutralOwners.NumberOfMines, "the Default plan attacks Enemies only");
        }

        [Test]
        public void AStarbase_SweepsAnyOwnerItHasNotRatedFriend()
        {
            mover.EmpireReports[layer.Id].Relation = PlayerRelation.Neutral;
            Minefield field = AddField(layer, 0, 0, 1000);
            AddFleet(mover, new NovaPoint(0, 0), Sweeper(10, 2, starbase: true), 1);

            new MineSweepStep().Process(serverState);
            Assert.AreEqual(1000 - 90, field.NumberOfMines, "neutral owner, range 2 + 1");

            mover.EmpireReports[layer.Id].Relation = PlayerRelation.Friend;
            new MineSweepStep().Process(serverState);
            Assert.AreEqual(1000 - 90, field.NumberOfMines, "never a Friend's field");
        }

        [Test]
        public void AFieldSweptToNothing_IsDeleted()
        {
            Minefield field = AddField(layer, 0, 0, 30);
            AddFleet(mover, new NovaPoint(0, 0), Sweeper(10, 2), 1);

            new MineSweepStep().Process(serverState);

            Assert.IsFalse(serverState.AllMinefields.ContainsKey(field.Key));
        }

        [Test]
        public void MineSweeping_IsStep24_AfterThePostBattleStage_BeforeRepair()
        {
            int sweep = StepKey<MineSweepStep>(serverState);
            Assert.Greater(sweep, StepKey<MysteryTraderStep>(serverState));
            Assert.Greater(sweep, StepKey<TransferFleetStep>(serverState), "after the Transfer Fleet task mode (23h)");
            Assert.Less(sweep, StepKey<RepairStep>(serverState));
        }
    }

    /// <summary>
    /// Fleet terraforming (Orbital Adjuster), turn step 27 - turn-generation-engine.md section 11
    /// "Fleet terraforming"; diplomacy-relations.md section 3 (coverage fleet row 60, diplomacy
    /// row 12).
    /// </summary>
    [TestFixture]
    public class FleetTerraformStepTest : H3Kit
    {
        private Star star;

        [SetUp]
        public void PlaceStar()
        {
            SetIdeal(mover.Race, 50, 50, 50);
            SetIdeal(layer.Race, 50, 50, 50);
            star = new Star { Name = "Target", Position = new NovaPoint(0, 0), Owner = mover.Id };
            SetEnvironment(50, 50, 50);
            serverState.AllStars.Add(star.Key, star);
        }

        private static void SetIdeal(Race race, int gravity, int temperature, int radiation)
        {
            race.GravityTolerance.MinimumValue = gravity - 10;
            race.GravityTolerance.MaximumValue = gravity + 10;
            race.TemperatureTolerance.MinimumValue = temperature - 10;
            race.TemperatureTolerance.MaximumValue = temperature + 10;
            race.RadiationTolerance.MinimumValue = radiation - 10;
            race.RadiationTolerance.MaximumValue = radiation + 10;
        }

        private void SetEnvironment(int gravity, int temperature, int radiation)
        {
            star.Gravity = star.OriginalGravity = gravity;
            star.Temperature = star.OriginalTemperature = temperature;
            star.Radiation = star.OriginalRadiation = radiation;
        }

        private Fleet Adjusters(EmpireData owner, int adjustersPerShip, int ships)
        {
            ShipDesign design = MakeDesign();
            design.Summary.Properties["Orbital Adjuster"] = new IntegerProperty(adjustersPerShip);
            Fleet fleet = AddFleet(owner, new NovaPoint(0, 0), design, ships);
            fleet.InOrbit = star;
            return fleet;
        }

        [Test]
        public void Capacity_IsTheSummedOrbitalAdjusterUnits()
        {
            Assert.AreEqual(6, FleetTerraformStep.TerraformCapacity(Adjusters(mover, 2, 3)));
        }

        [Test]
        public void AFleetAtItsOwnPlanet_MovesOnePointPerUnitOfCapacity_TowardItsIdeal()
        {
            SetEnvironment(30, 50, 50);
            Adjusters(mover, 1, 3);

            new FleetTerraformStep().Process(serverState);

            Assert.AreEqual(33, star.Gravity);
            Assert.AreEqual(30, star.OriginalGravity, "the original value is untouched");
            Assert.AreEqual(1, serverState.AllMessages.Count, "message 300 to the fleet owner only (it is also the planet owner)");
        }

        [Test]
        public void AFriendRatedOwnersPlanet_IsImproved_TowardTheFleetOwnersHabitability()
        {
            star.Owner = layer.Id;
            SetIdeal(layer.Race, 80, 80, 80);
            SetEnvironment(40, 50, 50);
            mover.EmpireReports[layer.Id].Relation = PlayerRelation.Friend;
            Adjusters(mover, 1, 2);

            new FleetTerraformStep().Process(serverState);

            Assert.AreEqual(42, star.Gravity, "toward the fleet owner's 50, not the planet owner's 80");
            Assert.AreEqual(1, serverState.AllMessages.Count(m => m.Audience == layer.Id), "the planet owner is told when a value changed");
        }

        [Test]
        public void ANonFriendsPlanetWithoutAStarbase_IsDegraded_AwayFromThePlanetOwnersIdeal()
        {
            star.Owner = layer.Id;
            mover.EmpireReports[layer.Id].Relation = PlayerRelation.Neutral;
            SetEnvironment(50, 20, 80);
            Adjusters(mover, 1, 2);

            new FleetTerraformStep().Process(serverState);

            Assert.AreEqual(52, star.Gravity, "the axis nearest the owner's ideal is pushed away, 2 points");
            Assert.AreEqual(20, star.Temperature);
            Assert.AreEqual(80, star.Radiation);
            Assert.AreEqual(1, serverState.AllMessages.Count(m => m.Audience == layer.Id));
        }

        [Test]
        public void ANonFriendsPlanetWithAStarbase_IsLeftAlone()
        {
            star.Owner = layer.Id;
            star.Starbase = AddFleet(layer, new NovaPoint(0, 0), MakeDesign(starbase: true), 1);
            SetEnvironment(50, 50, 50);
            Adjusters(mover, 1, 2);

            new FleetTerraformStep().Process(serverState);

            Assert.AreEqual(50, star.Gravity);
            Assert.AreEqual(0, serverState.AllMessages.Count);
        }

        [Test]
        public void AnUnownedPlanet_IsNotTerraformed()
        {
            star.Owner = Global.Nobody;
            SetEnvironment(30, 50, 50);
            Adjusters(mover, 1, 2);

            new FleetTerraformStep().Process(serverState);

            Assert.AreEqual(30, star.Gravity);
        }

        [Test]
        public void APlanetAlreadyAsGoodAsTheAllowanceAllows_GetsTheCannotImproveMessageOnly()
        {
            SetEnvironment(30, 50, 50);
            star.Gravity = 45; // 15 points used: the whole 15% allowance without Total Terraforming
            Adjusters(mover, 1, 2);

            new FleetTerraformStep().Process(serverState);

            Assert.AreEqual(45, star.Gravity);
            Assert.AreEqual(1, serverState.AllMessages.Count, "message 301");
            StringAssert.Contains("cannot", serverState.AllMessages[0].Text);
        }

        [Test]
        public void FleetTerraforming_IsStep27_AfterRepair_BeforeTheDesignPass()
        {
            int terraform = StepKey<FleetTerraformStep>(serverState);
            Assert.Greater(terraform, StepKey<RepairStep>(serverState));
            Assert.Less(terraform, StepKey<DesignLegalityStep>(serverState));
        }
    }

    /// <summary>
    /// Radiating Hydro-Ram Scoop colonist losses (coverage fleet row 10). The 85 mR exemption is
    /// the spec's; the int((86 - centre) / 2)% yearly loss is the Stars!wiki figure.
    /// </summary>
    [TestFixture]
    public class RamScoopRadiationStepTest : H3Kit
    {
        private static Race RaceWithRadiationCentre(int centre)
        {
            Race race = new Race();
            race.RadiationTolerance.MinimumValue = centre - 10;
            race.RadiationTolerance.MaximumValue = centre + 10;
            return race;
        }

        [Test]
        public void LossPercent_IsHalfOf86MinusTheRadiationCentre_ZeroFrom85OrWhenImmune()
        {
            Assert.AreEqual(33, RamScoopRadiationStep.LossPercent(RaceWithRadiationCentre(20)));
            Assert.AreEqual(18, RamScoopRadiationStep.LossPercent(RaceWithRadiationCentre(50)));
            Assert.AreEqual(3, RamScoopRadiationStep.LossPercent(RaceWithRadiationCentre(80)));
            Assert.AreEqual(0, RamScoopRadiationStep.LossPercent(RaceWithRadiationCentre(85)));

            Race immune = RaceWithRadiationCentre(20);
            immune.RadiationTolerance.Immune = true;
            Assert.AreEqual(0, RamScoopRadiationStep.LossPercent(immune));
        }

        [Test]
        public void AFleetWithTheEngine_LosesColonists_OthersDoNot()
        {
            mover.Race = RaceWithRadiationCentre(20);
            Fleet scoop = AddFleet(mover, new NovaPoint(0, 0), MakeDesign(partName: RamScoopRadiationStep.RadiatingEngineName), 1);
            scoop.Cargo.ColonistsInKilotons = 100;
            Fleet other = AddFleet(mover, new NovaPoint(0, 0), MakeDesign(partName: "Long Hump 6"), 1);
            other.Cargo.ColonistsInKilotons = 100;

            new RamScoopRadiationStep().Process(serverState);

            Assert.AreEqual(67, scoop.Cargo.ColonistsInKilotons, "33% of 100 kT");
            Assert.AreEqual(100, other.Cargo.ColonistsInKilotons);
            Assert.AreEqual(1, serverState.AllMessages.Count);
        }
    }

    /// <summary>
    /// Battle movement order: heavier tokens first, near-parity randomization within 20%
    /// (combat-resolution.md section 3; coverage combat row 7). The linear 50%-to-0 curve is an
    /// interpretation of "a proportionally increasing chance".
    /// </summary>
    [TestFixture]
    public class BattleMovementOrderTest : H3Kit
    {
        private class FixedRandom : Random
        {
            private readonly double value;

            public FixedRandom(double value)
            {
                this.value = value;
            }

            public int Draws { get; private set; }

            public override double NextDouble()
            {
                Draws++;
                return value;
            }
        }

        [Test]
        public void LighterMovesFirstChance_IsEvenAtParity_FallingToZeroAtA20PercentDifference()
        {
            Assert.AreEqual(0.5, BattleEngine.LighterMovesFirstChance(100, 100), 1e-9);
            Assert.AreEqual(0.25, BattleEngine.LighterMovesFirstChance(100, 90), 1e-9);
            Assert.AreEqual(0.125, BattleEngine.LighterMovesFirstChance(100, 85), 1e-9);
            Assert.AreEqual(0, BattleEngine.LighterMovesFirstChance(100, 80), 1e-9);
            Assert.AreEqual(0, BattleEngine.LighterMovesFirstChance(100, 10), 1e-9);
        }

        [Test]
        public void MovementOrder_IsHeaviestFirst_WithARollOnlyForNearParityPairs()
        {
            ShipDesign design = MakeDesign();
            Fleet fleet = AddFleet(mover, new NovaPoint(0, 0), design, 1);
            Stack light = new Stack(fleet, 1, new ShipToken(design, 1));
            Stack heavy = new Stack(fleet, 2, new ShipToken(design, 1));
            Stack nearlyHeavy = new Stack(fleet, 3, new ShipToken(design, 1));
            Dictionary<Stack, double> weight = new Dictionary<Stack, double> { { light, 100 }, { heavy, 300 }, { nearlyHeavy, 290 } };
            List<Stack> stacks = new List<Stack> { light, heavy, nearlyHeavy };

            FixedRandom never = new FixedRandom(0.99);
            CollectionAssert.AreEqual(new[] { heavy, nearlyHeavy, light }, BattleEngine.MovementOrder(stacks, never, s => weight[s]));
            Assert.AreEqual(1, never.Draws, "only the 300/290 pair is within 20%");

            FixedRandom always = new FixedRandom(0.0);
            CollectionAssert.AreEqual(new[] { nearlyHeavy, heavy, light }, BattleEngine.MovementOrder(stacks, always, s => weight[s]));
        }
    }

    /// <summary>
    /// The persisted damage word (ShipToken.PackedDamage) and the minefield-detonation "saw
    /// action" mark (turn-generation-engine.md section 11 "Which fleets": bit 0x40 is set by the
    /// minefield routine, which detonation shares).
    /// </summary>
    [TestFixture]
    public class PersistedDamageWordTest : H3Kit
    {
        [Test]
        public void TheDamageWord_SurvivesASaveAndReload_AndIsUsedInsteadOfReDerivingIt()
        {
            ShipDesign design = MakeDesign(1000);
            ShipToken token = new ShipToken(design, 10);
            DamageWord.Store(token, new DamageWord(40, 100)); // 4 of 10 ships at 200 damage

            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Root");
            root.AppendChild(token.ToXml(doc));
            ShipToken reloaded = new ShipToken(root.FirstChild);
            reloaded.Design = design;

            Assert.AreEqual(token.PackedDamage, reloaded.PackedDamage);
            Assert.AreEqual(token.Armor, reloaded.Armor, 1e-9);
            DamageWord word = DamageWord.For(reloaded);
            Assert.AreEqual(40, word.Percent, "re-deriving from pooled armor would give 100%");
            Assert.AreEqual(100, word.Units);
        }

        [Test]
        public void AnOldSaveWithoutTheField_LoadsAsZero_AndTheWordIsReDerived()
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml("<Token><Design>384</Design><Quantity>2</Quantity><Armor>1500</Armor></Token>");
            ShipToken token = new ShipToken(doc.DocumentElement);
            token.Design = MakeDesign(1000);

            Assert.AreEqual(0, token.PackedDamage);
            Assert.AreEqual(100, DamageWord.For(token).Percent);
            Assert.AreEqual(125, DamageWord.For(token).Units, "250 damage per ship = 125/500");
        }

        [Test]
        public void AStaleStoredWord_IsIgnored_WhenArmorWasChangedBehindItsBack()
        {
            ShipToken token = new ShipToken(MakeDesign(1000), 10);
            DamageWord.Store(token, new DamageWord(40, 100));
            token.Armor = 5000;

            Assert.AreEqual(100, DamageWord.For(token).Percent);
        }

        [Test]
        public void FleetsCaughtByADetonatingField_AreMarkedAsHavingSeenAction()
        {
            AddField(layer, 0, 0, 400, detonate: true);
            Fleet inside = AddFleet(mover, new NovaPoint(10, 0), MakeDesign(5000), 1);
            Fleet outside = AddFleet(mover, new NovaPoint(50, 0), MakeDesign(5000), 1);
            HashSet<long> sawAction = new HashSet<long>();

            new MinefieldDecayStep(sawAction).Process(serverState, null, new Random(1));

            Assert.IsTrue(sawAction.Contains(inside.Key));
            Assert.IsFalse(sawAction.Contains(outside.Key));
        }
    }
}
