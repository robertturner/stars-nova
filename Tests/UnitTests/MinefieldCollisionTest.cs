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

    /// <summary>
    /// A Random whose Next(max) returns scripted values, then max - 1 (never a hit) once the
    /// script runs out; it also counts the calls (one per light-year roll).
    /// </summary>
    public class ScriptedRandom : Random
    {
        private readonly Queue<int> script;

        public ScriptedRandom(params int[] values)
        {
            script = new Queue<int>(values);
        }

        public int Calls { get; private set; }

        public override int Next(int maxValue)
        {
            Calls++;
            return script.Count > 0 ? script.Dequeue() : maxValue - 1;
        }
    }

    /// <summary>
    /// The minefield routine for moving fleets: behavior-specs-10/fleet-movement-scanning-cargo.md
    /// section 5, "Minefield rules, code-confirmed".
    /// </summary>
    [TestFixture]
    public class MinefieldCollisionTest
    {
        private ServerData serverState;
        private EmpireData fleetOwner;
        private EmpireData fieldOwner;
        private long nextDesignKey = 100;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            fleetOwner = new SimpleEmpireData { Id = 1, Race = new Race { PluralName = "Movers" } };
            fieldOwner = new SimpleEmpireData { Id = 2, Race = new Race { PluralName = "Miners" } };
            serverState.AllEmpires.Add(fleetOwner.Id, fleetOwner);
            serverState.AllEmpires.Add(fieldOwner.Id, fieldOwner);
        }

        private ShipDesign MakeDesign(int armor, int engines = 1, bool scoop = false, int shield = 0)
        {
            ShipDesign design = new ShipDesign(nextDesignKey++);
            design.Blueprint = new Component();
            design.Blueprint.Mass = 50;
            Hull hull = new Hull();
            hull.ArmorStrength = armor;
            hull.FuelCapacity = 10000;
            hull.Modules = new List<HullModule>();

            Engine engine = new Engine();
            for (int i = 0; i < engine.FuelConsumption.Length; i++)
            {
                engine.FuelConsumption[i] = 100;
            }

            if (scoop)
            {
                engine.FuelConsumption[3] = 0; // no fuel at warp 4
            }

            Component engineComponent = new Component();
            engineComponent.Name = "Test Engine";
            engineComponent.Properties.Add("Engine", engine);
            hull.Modules.Add(new HullModule { AllocatedComponent = engineComponent, ComponentCount = engines, ComponentMaximum = engines });

            if (shield > 0)
            {
                Component shieldComponent = new Component();
                shieldComponent.Name = "Test Shield";
                shieldComponent.Properties.Add("Shield", new IntegerProperty(shield));
                hull.Modules.Add(new HullModule { AllocatedComponent = shieldComponent, ComponentCount = 1 });
            }

            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            return design;
        }

        private Fleet MakeFleet(params (ShipDesign design, int quantity)[] stacks)
        {
            Fleet fleet = new Fleet(fleetOwner.GetNextFleetKey());
            fleet.Name = "Test fleet";
            fleet.Position = new NovaPoint(0, 0);
            foreach ((ShipDesign design, int quantity) in stacks)
            {
                ShipToken token = new ShipToken(design, quantity);
                fleet.Composition.Add(token.Key, token);
            }

            return fleet;
        }

        private Minefield AddField(EmpireData owner, int x, int y, int mines, MinefieldType type = MinefieldType.Standard)
        {
            Minefield field = new Minefield { NumberOfMines = mines, FieldType = type };
            field.Key = owner.GetNextMinefieldKey();
            field.Position = new NovaPoint(x, y);
            serverState.AllMinefields[field.Key] = field;
            return field;
        }

        /// <summary>Moves the fleet to (distance, 0) and runs the routine from the origin.</summary>
        private MinefieldHit Travel(Fleet fleet, int distance, Random random)
        {
            fleet.Position = new NovaPoint(distance, 0);
            return new CheckForMinefields(serverState, random).Check(fleet, new NovaPoint(0, 0));
        }

        private static double ArmorOf(Fleet fleet)
        {
            return fleet.Composition.Values.First().Armor;
        }

        [Test]
        public void EffectiveWarp_IsTheSmallestWFrom3To10_WithWSquaredAtLeastDistanceMinusOne()
        {
            Assert.AreEqual(3, CheckForMinefields.EffectiveWarp(1));
            Assert.AreEqual(3, CheckForMinefields.EffectiveWarp(10), "9 >= 9");
            Assert.AreEqual(4, CheckForMinefields.EffectiveWarp(11), "9 < 10, 16 >= 10");
            Assert.AreEqual(9, CheckForMinefields.EffectiveWarp(82), "81 >= 81");
            Assert.AreEqual(10, CheckForMinefields.EffectiveWarp(83));
            Assert.AreEqual(10, CheckForMinefields.EffectiveWarp(300), "capped at 10");
        }

        [Test]
        public void FieldLossOnHit_FollowsTheTwoTierRule()
        {
            Assert.AreEqual(10, CheckForMinefields.FieldLossOnHit(100), "100/20 = 5, at least 10");
            Assert.AreEqual(20, CheckForMinefields.FieldLossOnHit(400));
            Assert.AreEqual(50, CheckForMinefields.FieldLossOnHit(1000), "1000/20 = 50, still the first tier");
            Assert.AreEqual(50, CheckForMinefields.FieldLossOnHit(1020), "1020/20 = 51 > 50: max(50, 10)");
            Assert.AreEqual(100, CheckForMinefields.FieldLossOnHit(10000), "max(50, 100)");
        }

        [Test]
        public void StandardField_RollsOncePerWholeLightYearInside_AndStopsTheFleetAtTheHit()
        {
            // Path 0..81 along x; the field (400 mines, radius 20) covers x = 20..60, so the
            // stretch enters at 20 and is 40 ly long. Warp used: 9. c = (9 - 4) x 3 = 15.
            Minefield field = AddField(fieldOwner, 40, 0, 400);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom random = new ScriptedRandom(999, 15, 14);

            MinefieldHit result = Travel(fleet, 81, random);

            Assert.AreEqual(MinefieldHit.Hit, result);
            Assert.AreEqual(3, random.Calls, "15 is not below c = 15; 14 is: the third roll hits");
            Assert.AreEqual(22, fleet.Position.X, "stopped at entry (20) plus the two light-years rolled before the hit");
            Assert.AreEqual(0, fleet.Position.Y);

            // One ship, one engine: 100 per ship, topped up to the 500 minimum (fleet of <= 4).
            Assert.AreEqual(500, ArmorOf(fleet));

            // The field loses max(10, 400 / 20) = 20.
            Assert.AreEqual(380, field.NumberOfMines);

            Assert.AreEqual(1, serverState.AllMessages.Count(m => m.Audience == fleetOwner.Id));
            Assert.AreEqual(1, serverState.AllMessages.Count(m => m.Audience == fieldOwner.Id));
        }

        [Test]
        public void NoHit_LeavesTheFleetWhereItsTravelEnded()
        {
            Minefield field = AddField(fieldOwner, 40, 0, 400);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom random = new ScriptedRandom();

            Assert.AreEqual(MinefieldHit.None, Travel(fleet, 81, random));
            Assert.AreEqual(40, random.Calls, "one roll per whole light-year of the 40 ly stretch");
            Assert.AreEqual(81, fleet.Position.X);
            Assert.AreEqual(400, field.NumberOfMines);
        }

        [Test]
        public void OwnFields_NeverHitTheirOwnersFleets()
        {
            AddField(fleetOwner, 40, 0, 400, MinefieldType.SpeedBump);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom random = new ScriptedRandom(0, 0, 0);

            Assert.AreEqual(MinefieldHit.None, Travel(fleet, 81, random));
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void FieldsOfARaceThatRatesTheFleetFriend_DoNotCount()
        {
            fieldOwner.EmpireReports[fleetOwner.Id] = new EmpireIntel(fleetOwner) { Relation = PlayerRelation.Friend };
            AddField(fieldOwner, 40, 0, 400);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom random = new ScriptedRandom(0);

            Assert.AreEqual(MinefieldHit.None, Travel(fleet, 81, random));

            fieldOwner.EmpireReports[fleetOwner.Id].Relation = PlayerRelation.Neutral;
            fleet.Position = new NovaPoint(0, 0);
            Assert.AreEqual(MinefieldHit.Hit, Travel(fleet, 81, new ScriptedRandom(0)), "neutral is not friend");
        }

        [Test]
        public void AFleetThatDidNotMove_IsNeverHit()
        {
            AddField(fieldOwner, 0, 0, 400, MinefieldType.SpeedBump);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom random = new ScriptedRandom(0);

            Assert.AreEqual(MinefieldHit.None, Travel(fleet, 0, random));
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void SlowFleets_UseTheDistanceCovered_NotTheOrderedWarp()
        {
            // 16 ly covered: w = 4 = the standard safe warp, so c = 0 and nothing is rolled.
            AddField(fieldOwner, 8, 0, 400);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom random = new ScriptedRandom(0);

            Assert.AreEqual(MinefieldHit.None, Travel(fleet, 16, random));
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void SpaceDemolition_AddsTwoToTheSafeWarp_AndNothingHappensAtOrBelowThreePlusAllowance()
        {
            // 25 ly: w = 5. Standard c = (5 - 4) x 3 = 3 for an ordinary race...
            AddField(fieldOwner, 12, 0, 400);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom ordinary = new ScriptedRandom();
            Travel(fleet, 25, ordinary);
            Assert.Greater(ordinary.Calls, 0);

            // ...but w = 5 <= 3 + 2 for Space Demolition: no rolls at all.
            fleetOwner.Race.Traits.SetPrimary("SD");
            fleet.Position = new NovaPoint(0, 0);
            ScriptedRandom demolition = new ScriptedRandom(0);
            Assert.AreEqual(MinefieldHit.None, Travel(fleet, 25, demolition));
            Assert.AreEqual(0, demolition.Calls);
        }

        [Test]
        public void SuperStealth_AddsOneToTheSafeWarp()
        {
            fleetOwner.Race.Traits.SetPrimary("SS");
            Assert.AreEqual(1, CheckForMinefields.RacialAllowance(fleetOwner.Race));

            // 25 ly: w = 5, c = (5 - 1 - 4) x 3 = 0 for a standard field: no rolls.
            AddField(fieldOwner, 12, 0, 400);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom random = new ScriptedRandom(0);
            Assert.AreEqual(MinefieldHit.None, Travel(fleet, 25, random));
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void HeavyField_UsesSafeWarp6_Rate10_AndHeavyDamage()
        {
            // w = 9: c = (9 - 6) x 10 = 30.
            AddField(fieldOwner, 40, 0, 400, MinefieldType.Heavy);
            Fleet fleet = MakeFleet((MakeDesign(5000), 1));
            ScriptedRandom random = new ScriptedRandom(30, 29);

            Assert.AreEqual(MinefieldHit.Hit, Travel(fleet, 81, random));
            Assert.AreEqual(2, random.Calls);
            Assert.AreEqual(21, fleet.Position.X);

            // 500 per ship, topped up to the 2,000 minimum.
            Assert.AreEqual(3000, ArmorOf(fleet));
        }

        [Test]
        public void SpeedBumpField_OnlyStopsTheFleet()
        {
            // w = 9: c = (9 - 5) x 35 = 140.
            Minefield field = AddField(fieldOwner, 40, 0, 400, MinefieldType.SpeedBump);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));
            ScriptedRandom random = new ScriptedRandom(139);

            Assert.AreEqual(MinefieldHit.Hit, Travel(fleet, 81, random));
            Assert.AreEqual(20, fleet.Position.X, "the first roll hit: stopped at the entry point");
            Assert.AreEqual(1000, ArmorOf(fleet), "no damage at all");
            Assert.AreEqual(380, field.NumberOfMines, "the field still loses mines");
        }

        [Test]
        public void ShieldsAbsorbUpToHalfTheDamage()
        {
            AddField(fieldOwner, 40, 0, 400);
            Fleet weakShields = MakeFleet((MakeDesign(1000, shield: 100), 1));
            Travel(weakShields, 81, new ScriptedRandom(0));
            Assert.AreEqual(600, ArmorOf(weakShields), "500 raw, 100 absorbed");

            Fleet strongShields = MakeFleet((MakeDesign(1000, shield: 1000), 1));
            Travel(strongShields, 81, new ScriptedRandom(0));
            Assert.AreEqual(750, ArmorOf(strongShields), "at most half of 500 is absorbed");
        }

        [Test]
        public void DamageExactlyEqualToTheArmor_DoesNotDestroyTheStack()
        {
            AddField(fieldOwner, 40, 0, 400);
            ShipDesign twinEngine = MakeDesign(1000, engines: 2);
            Fleet fleet = MakeFleet((twinEngine, 1));

            MinefieldHit result = Travel(fleet, 81, new ScriptedRandom(0));

            // (1 x 100 + 400 top-up) x 2 engines = 1,000 = the whole armor: every ship survives.
            Assert.AreEqual(MinefieldHit.Hit, result);
            Assert.AreEqual(1, fleet.Composition.Count, "a figure exactly equal to the armor does not destroy the stack");
            Assert.Greater(ArmorOf(fleet), 0, "the ships survive at damage equal to their armor");
            Assert.AreEqual(0, serverState.AllDeepSpaceMinerals.Count, "nothing was destroyed, so no wreckage");
        }

        [Test]
        public void DamageIsMultipliedByTheEnginesInTheDesign_AndADestroyedFleetLeavesWreckage()
        {
            AddField(fieldOwner, 40, 0, 400);
            ShipDesign tripleEngine = MakeDesign(1000, engines: 3);
            tripleEngine.Blueprint.Cost = new Resources(30, 0, 0, 0); // wreckage needs minerals to exist
            tripleEngine.Update();
            Fleet fleet = MakeFleet((tripleEngine, 1));

            MinefieldHit result = Travel(fleet, 81, new ScriptedRandom(0));

            // (1 x 100 + 400 top-up) x 3 engines = 1,500, above the whole armor.
            Assert.AreEqual(MinefieldHit.Destroyed, result);
            Assert.AreEqual(0, fleet.Composition.Count);
            Assert.AreEqual(1, serverState.AllDeepSpaceMinerals.Count, "destroyed ships leave wreckage at the stop point");
        }

        [Test]
        public void TheTopUpAppliesOnlyToFleetsOfFourShipsOrFewer()
        {
            AddField(fieldOwner, 40, 0, 400);

            Fleet four = MakeFleet((MakeDesign(1000), 4));
            Travel(four, 81, new ScriptedRandom(0));
            // 4 x 100 + top-up 100 = 500, spread as 125 per ship and stored in the damage word
            // rounded up to the next 1/500 of the 1,000 armor: 63 units = 126 per ship, 504 in all
            // (combat-resolution.md section 8).
            Assert.AreEqual(4000 - 504, ArmorOf(four), "4 x 100 + top-up 100 = 500, rounded up per ship to 126");

            Fleet five = MakeFleet((MakeDesign(1000), 5));
            Travel(five, 81, new ScriptedRandom(0));
            Assert.AreEqual(4500, ArmorOf(five), "5 x 100, no top-up");
        }

        [Test]
        public void TheTopUpGoesToTheFirstDamagedStackOnly()
        {
            AddField(fieldOwner, 40, 0, 400);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1), (MakeDesign(1000), 1));

            Travel(fleet, 81, new ScriptedRandom(0));

            List<ShipToken> stacks = fleet.Composition.Values.ToList();
            Assert.AreEqual(600, stacks[0].Armor, "100 + top-up (500 - 100 x 2)");
            Assert.AreEqual(900, stacks[1].Armor, "100 only");
        }

        [Test]
        public void ScoopFleets_UseTheSecondColumn()
        {
            AddField(fieldOwner, 40, 0, 400);
            Fleet fleet = MakeFleet((MakeDesign(1000, scoop: true), 1));
            Assert.IsTrue(CheckForMinefields.IsScoopFleet(fleet));

            Travel(fleet, 81, new ScriptedRandom(0));

            Assert.AreEqual(400, ArmorOf(fleet), "125 per ship topped up to 600");
        }

        [Test]
        public void OverlappingFieldsOfTheSameType_MergeIntoOneStretch()
        {
            // Radius 10 each: x = 20..40 and 30..50, merged into one stretch 20..50 (30 rolls),
            // not 20 + 20.
            AddField(fieldOwner, 30, 0, 100);
            AddField(fieldOwner, 40, 0, 100);
            ScriptedRandom merged = new ScriptedRandom();
            Travel(MakeFleet((MakeDesign(1000), 1)), 81, merged);
            Assert.AreEqual(30, merged.Calls);
        }

        [Test]
        public void SeparateFields_AreSeparateStretches()
        {
            AddField(fieldOwner, 20, 0, 100);
            AddField(fieldOwner, 60, 0, 100);
            ScriptedRandom random = new ScriptedRandom();
            Travel(MakeFleet((MakeDesign(1000), 1)), 81, random);
            Assert.AreEqual(40, random.Calls);
        }

        [Test]
        public void AFleetIsHitAtMostOncePerYear()
        {
            Minefield first = AddField(fieldOwner, 20, 0, 100);
            Minefield second = AddField(fieldOwner, 60, 0, 100);
            Fleet fleet = MakeFleet((MakeDesign(5000), 1));
            ScriptedRandom random = new ScriptedRandom(Enumerable.Repeat(0, 100).ToArray());

            Assert.AreEqual(MinefieldHit.Hit, Travel(fleet, 81, random));
            Assert.AreEqual(1, random.Calls, "the first hit ends the routine");
            Assert.AreEqual(10, fleet.Position.X);
            Assert.AreEqual(90, first.NumberOfMines);
            Assert.AreEqual(100, second.NumberOfMines);
            Assert.AreEqual(4500, ArmorOf(fleet));
        }

        [Test]
        public void AFieldThatLosesItsWholeCount_IsRemoved()
        {
            Minefield field = AddField(fieldOwner, 40, 0, 10, MinefieldType.SpeedBump);
            Fleet fleet = MakeFleet((MakeDesign(1000), 1));

            // Radius ~3.2: x = 37..43, 6 rolls.
            Assert.AreEqual(MinefieldHit.Hit, Travel(fleet, 81, new ScriptedRandom(0)));
            Assert.IsFalse(serverState.AllMinefields.ContainsKey(field.Key));
        }

        [Test]
        public void MinefieldTypeAndLayMinesDuration_SurviveXmlRoundTrips()
        {
            System.Xml.XmlDocument xmldoc = new System.Xml.XmlDocument();
            Minefield field = new Minefield { NumberOfMines = 50, FieldType = MinefieldType.Heavy };
            System.Xml.XmlElement element = field.ToXml(xmldoc);
            Assert.AreEqual(MinefieldType.Heavy, new Minefield(element).FieldType);

            LayMinesTask task = new LayMinesTask { Duration = 2 };
            Assert.AreEqual(2, new LayMinesTask(task.ToXml(xmldoc)).Duration);
            Assert.AreEqual(LayMinesTask.Indefinitely, new LayMinesTask().Duration, "a new order runs indefinitely (duration 5)");
        }
    }
}
