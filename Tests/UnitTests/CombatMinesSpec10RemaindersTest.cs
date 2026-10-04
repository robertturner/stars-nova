namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Combat;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// The packed damage word, behavior-specs-10/combat-resolution.md §8 (FUN_10f0_52c4): low 7
    /// bits the percentage of surviving ships that carry damage, high 9 bits each damaged ship's
    /// damage in 1/500 of its armor; unpack, damaged-ships-first kills, repack rounded up.
    /// </summary>
    [TestFixture]
    public class DamageWordTest
    {
        [Test]
        public void SpecWorkedExample_TenShipsAt40PercentAnd100Units_TakeA450PointHit()
        {
            // 4 damaged ships with 100 points each; a 450-point hit kills one damaged ship (needs
            // 400) and leaves 50, which with the 3 x 100 still carried makes 350 over 9
            // survivors, rounded up to 39 points each, stored as 39 units at 100%.
            DamageWord word = new DamageWord(40, 100);

            Assert.AreEqual(4, word.DamagedShips(10));
            Assert.AreEqual(100, word.DamagePerShip(500));

            DamageWord.DamageResult result = word.Apply(450, 10, 500);

            Assert.AreEqual(1, result.ShipsKilled);
            Assert.AreEqual(100, result.Word.Percent);
            Assert.AreEqual(39, result.Word.Units);
            Assert.AreEqual(9 * (500 - 39), result.Word.PooledArmor(9, 500));
        }

        [Test]
        public void NoDamageLeftOver_KeepsTheUnits_AndThePercentageIsRoundedUp()
        {
            // Exactly 400 kills one of the 4 damaged ships with nothing left: 3 damaged of 9
            // survivors = 33.3%, rounded up to 34; the 100 units stay.
            DamageWord.DamageResult result = new DamageWord(40, 100).Apply(400, 10, 500);

            Assert.AreEqual(1, result.ShipsKilled);
            Assert.AreEqual(34, result.Word.Percent);
            Assert.AreEqual(100, result.Word.Units);
        }

        [Test]
        public void TheLastDamagedShipDying_WithNothingLeft_ClearsTheWord()
        {
            DamageWord.DamageResult result = new DamageWord(10, 100).Apply(400, 10, 500);

            Assert.AreEqual(1, result.ShipsKilled);
            Assert.IsTrue(result.Word.IsUndamaged);
            Assert.AreEqual(0, result.Word.Packed);
        }

        [Test]
        public void OnePointOfDamage_IsStoredAsAtLeastOneFiveHundredth_RoundedInTheDamagesFavour()
        {
            // 1 point on a 1,000-armor ship: 1 x 500 / 1000 rounded up = 1 unit = 2 points.
            DamageWord.DamageResult result = new DamageWord(0, 0).Apply(1, 1, 1000);

            Assert.AreEqual(1, result.Word.Units);
            Assert.AreEqual(100, result.Word.Percent);
            Assert.AreEqual(1000 - 2, result.Word.PooledArmor(1, 1000));
        }

        [Test]
        public void TheKillLimit_ThrowsAwayTheDamageLeft()
        {
            // 1,000 points at 5 undamaged ships of 100 armor with a limit of 2: two die, the
            // rest of the damage is discarded and the 3 survivors stay undamaged.
            DamageWord.DamageResult result = new DamageWord(0, 0).Apply(1000, 5, 100, 2);

            Assert.AreEqual(2, result.ShipsKilled);
            Assert.AreEqual(800, result.Discarded);
            Assert.IsTrue(result.Word.IsUndamaged);
        }

        [Test]
        public void UnitsAreCappedAt499()
        {
            DamageWord.DamageResult result = new DamageWord(0, 0).Apply(99, 1, 100);

            Assert.AreEqual(0, result.ShipsKilled, "99 does not cover a 100-armor ship");
            Assert.AreEqual(495, result.Word.Units);

            Assert.AreEqual(DamageWord.MaxUnits, DamageWord.ToUnits(1000, 100));
        }

        [Test]
        public void ThePackedWord_IsTheUnitsAboveTheSevenBitPercentage()
        {
            DamageWord word = new DamageWord(100, 39);

            Assert.AreEqual((39 << 7) | 100, word.Packed);
            Assert.AreEqual(39, DamageWord.FromPacked(word.Packed).Units);
            Assert.AreEqual(100, DamageWord.FromPacked(word.Packed).Percent);
        }

        [Test]
        public void ATokenWithNoRememberedWord_IsReadAsEveryShipDamaged()
        {
            ShipDesign design = CombatTestKit.Design(1, 500);
            ShipToken token = new ShipToken(design, 10, 4600);

            DamageWord word = DamageWord.For(token);

            Assert.AreEqual(100, word.Percent);
            Assert.AreEqual(40, word.Units, "400 over 10 ships = 40 points = 40/500 of 500");
        }
    }

    /// <summary>
    /// The damage word inside the battle engine: damaged ships die first, needing only their
    /// remaining armor (combat-resolution.md §6 / §8).
    /// </summary>
    [TestFixture]
    public class BattleDamageWordTest
    {
        [Test]
        public void ABeamHit_KillsADamagedShipFirst_AndRepacksTheSurvivors()
        {
            // The spec's worked example through a real shot: pooled armor 4,600 would need 460 a
            // ship (no kill), the word kills a damaged ship for 400.
            ServerData serverState = CombatTestKit.TwoEmpires();
            ShipDesign wolfDesign = CombatTestKit.Design(1, 100, (CombatTestKit.BeamPart(450, 1), 1));
            Stack wolf = CombatTestKit.MakeStack(wolfDesign, CombatTestKit.WolfId, new Point(0, 0), 1);
            Stack lamb = CombatTestKit.MakeStack(CombatTestKit.Design(2, 500), CombatTestKit.LambId, new Point(0, 0), 2, quantity: 10);
            DamageWord.Store(lamb.Token, new DamageWord(40, 100));
            Assert.AreEqual(4600, lamb.Token.Armor, 1e-9);

            BattleEngine engine = CombatTestKit.Engine(serverState, new List<Stack> { wolf, lamb });
            CombatTestKit.Fire(engine, wolf, lamb, wolfDesign.Weapons[0]);

            Assert.AreEqual(9, lamb.Token.Quantity);
            Assert.AreEqual(9 * (500 - 39), lamb.Token.Armor, 1e-9);
            Assert.AreEqual(39, DamageWord.For(lamb.Token).Units);
        }
    }

    /// <summary>
    /// Wreckage objects (combat-resolution.md §5 dump cargo, §7): at most 30,000 kT each, the rest
    /// overflowing into further objects at the same spot; nothing at a planet's position.
    /// </summary>
    [TestFixture]
    public class WreckageCapTest
    {
        [Test]
        public void SeventyThousandKilotons_MakeThreeObjectsAtTheSameSpot()
        {
            ServerData serverState = new ServerData();
            NovaPoint spot = new NovaPoint(500, 500);

            BattleEngine.AddWreckage(serverState, spot, new Resources(50000, 20000, 0, 0));

            Assert.AreEqual(3, serverState.AllDeepSpaceMinerals.Count);
            List<DeepSpaceMinerals> objects = serverState.AllDeepSpaceMinerals.Values.ToList();
            Assert.IsTrue(objects.All(o => o.Position.X == 500 && o.Position.Y == 500));
            Assert.AreEqual(new[] { 30000, 30000, 10000 }, objects.Select(o => o.Minerals.Ironium + o.Minerals.Boranium + o.Minerals.Germanium).ToArray());
            Assert.AreEqual(50000, objects.Sum(o => o.Minerals.Ironium));
            Assert.AreEqual(20000, objects.Sum(o => o.Minerals.Boranium));
        }

        [Test]
        public void AnExistingObjectIsToppedUpFirst()
        {
            ServerData serverState = new ServerData();
            NovaPoint spot = new NovaPoint(10, 10);
            BattleEngine.AddWreckage(serverState, spot, new Resources(29000, 0, 0, 0));

            BattleEngine.AddWreckage(serverState, spot, new Resources(0, 0, 3000, 0));

            Assert.AreEqual(2, serverState.AllDeepSpaceMinerals.Count);
            DeepSpaceMinerals first = serverState.AllDeepSpaceMinerals[spot.ToHashString()];
            Assert.AreEqual(1000, first.Minerals.Germanium);
            Assert.AreEqual(2000, serverState.AllDeepSpaceMinerals[spot.ToHashString() + "#1"].Minerals.Germanium);
        }

        [Test]
        public void ForceNew_CreatesANewObjectRatherThanToppingUp()
        {
            ServerData serverState = new ServerData();
            NovaPoint spot = new NovaPoint(10, 10);
            BattleEngine.AddWreckage(serverState, spot, new Resources(100, 0, 0, 0));

            BattleEngine.AddWreckage(serverState, spot, new Resources(50, 0, 0, 0), forceNew: true);

            Assert.AreEqual(2, serverState.AllDeepSpaceMinerals.Count);
            Assert.AreEqual(100, serverState.AllDeepSpaceMinerals[spot.ToHashString()].Minerals.Ironium, "the existing object is left alone");
            Assert.AreEqual(50, serverState.AllDeepSpaceMinerals[spot.ToHashString() + "#1"].Minerals.Ironium, "the forced object is new");
        }

        [Test]
        public void OnlyTheFirstReceivingObjectGetsTheGraceMark()
        {
            ServerData serverState = new ServerData();
            NovaPoint spot = new NovaPoint(500, 500);
            BattleEngine.AddWreckage(serverState, spot, new Resources(40000, 0, 0, 0));

            Assert.IsTrue(serverState.AllDeepSpaceMinerals[spot.ToHashString()].DecayGrace, "the first receiving object is marked");
            Assert.IsFalse(serverState.AllDeepSpaceMinerals[spot.ToHashString() + "#1"].DecayGrace, "the overflow object is not marked");
        }

        [Test]
        public void ZeroAmounts_InventSalvageOfAtMostNineKilotonsEach()
        {
            ServerData serverState = new ServerData();
            NovaPoint spot = new NovaPoint(1, 2);
            using (GameRandom.Use(new Random(1234)))
            {
                BattleEngine.AddWreckage(serverState, spot, new Resources(0, 0, 0, 0));
            }

            DeepSpaceMinerals wreck = serverState.AllDeepSpaceMinerals.Values.Single();
            int total = wreck.Minerals.Ironium + wreck.Minerals.Boranium + wreck.Minerals.Germanium;
            Assert.Greater(total, 0);
            Assert.LessOrEqual(total, 27);
            Assert.LessOrEqual(wreck.Minerals.Ironium, 9);
            Assert.LessOrEqual(wreck.Minerals.Boranium, 9);
            Assert.LessOrEqual(wreck.Minerals.Germanium, 9);
        }

        [Test]
        public void AtAPlanetsMapPosition_NoObjectIsMade()
        {
            ServerData serverState = new ServerData();
            Star star = new Star { Name = "Here", Position = new NovaPoint(20, 30) };
            serverState.AllStars[star.Name] = star;

            BattleEngine.AddWreckage(serverState, new NovaPoint(20, 30), new Resources(100, 0, 0, 0));

            Assert.AreEqual(0, serverState.AllDeepSpaceMinerals.Count);
        }

        [Test]
        public void SeveralObjectsAtOneSpot_SurviveASaveAndReload()
        {
            ServerData serverState = new ServerData();
            BattleEngine.AddWreckage(serverState, new NovaPoint(5, 5), new Resources(40000, 0, 0, 0));
            XmlDocument xml = new XmlDocument();
            XmlElement root = xml.CreateElement("AllDeepSpaceMinerals");
            foreach (DeepSpaceMinerals wreck in serverState.AllDeepSpaceMinerals.Values)
            {
                root.AppendChild(wreck.ToXml(xml));
            }

            Dictionary<string, DeepSpaceMinerals> reloaded = new Dictionary<string, DeepSpaceMinerals>();
            foreach (XmlNode node in root.ChildNodes)
            {
                DeepSpaceMinerals wreck = new DeepSpaceMinerals(node);
                reloaded[wreck.Position.ToHashString() + "#" + reloaded.Count] = wreck;
            }

            Assert.AreEqual(40000, reloaded.Values.Sum(o => o.Minerals.Ironium));
        }

        [Test]
        public void ABleedingEdgeOwnersWreckage_UsesTheCostWithoutTheDoubling()
        {
            // The lamb's only part is at its requirement, so BET doubles its 300 ironium to 600;
            // the wreckage is 300 / 3 = 100, minus a quarter in deep space = 75 (not 150).
            ServerData serverState = CombatTestKit.TwoEmpires();
            Race bleedingEdge = new Race();
            bleedingEdge.Traits.Add("BET");
            TechLevel tech = new TechLevel(0, 0, 5, 0, 0, 0);

            Component part = new Component { Name = "Plate", Cost = new Resources(300, 0, 0, 10), RequiredTech = new TechLevel(0, 0, 5, 0, 0, 0) };
            Component blueprint = new Component { Mass = 50 };
            Hull hull = new Hull { Modules = new List<HullModule> { new HullModule { AllocatedComponent = part, ComponentCount = 1 } }, ArmorStrength = 50, FuelCapacity = 100 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign lambDesign = new ShipDesign(2) { Name = "Doubled", Blueprint = blueprint };
            lambDesign.Update(bleedingEdge, tech);
            Assert.AreEqual(600, lambDesign.Cost.Ironium, "Precondition: BET doubles the part");
            Assert.AreEqual(300, lambDesign.CostWithoutBleedingEdgeDoubling.Ironium);

            ShipDesign wolfDesign = Spec10CombatKit.Design(1, 100, 1000, 0, null, 0, null, (CombatTestKit.BeamPart(100000, 10), 1));
            Point position = new Point(100, 100);
            Fleet wolfFleet = new Fleet("Wolf", CombatTestKit.WolfId, 1, position);
            ShipToken wolfToken = new ShipToken(wolfDesign, 1);
            wolfFleet.Composition.Add(wolfToken.Key, wolfToken);
            Fleet lambFleet = new Fleet("Lamb", CombatTestKit.LambId, 1, position);
            ShipToken lambToken = new ShipToken(lambDesign, 1);
            lambFleet.Composition.Add(lambToken.Key, lambToken);
            serverState.AllEmpires[CombatTestKit.WolfId].OwnedFleets.Add(wolfFleet);
            serverState.AllEmpires[CombatTestKit.LambId].OwnedFleets.Add(lambFleet);

            new BattleEngine(serverState, new BattleReport()).Run();

            Assert.IsFalse(serverState.AllEmpires[CombatTestKit.LambId].OwnedFleets.ContainsKey(lambFleet.Key));
            Assert.AreEqual(100 - (100 / 4), serverState.AllDeepSpaceMinerals.Values.Single().Minerals.Ironium);
        }
    }

    /// <summary>
    /// Minefield strikes and detonation - fleet-movement-scanning-cargo.md §5 ("The field",
    /// "Damage", "Detonation"), turn-generation-engine.md §3 (step 18 pass, +25 decay) and
    /// ship-design-and-components.md (Space Demolition learns the designs).
    /// </summary>
    [TestFixture]
    public class MinefieldVisibilityAndDetonationTest
    {
        private ServerData serverState;
        private EmpireData mover;
        private EmpireData layer;
        private long nextDesignKey = 500;

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

        private ShipDesign MakeDesign(int armor, string hullName = "Test Hull")
        {
            ShipDesign design = new ShipDesign(nextDesignKey++);
            design.Name = "Design " + design.Key;
            design.Icon = new ShipIcon("hull0000.png", null);
            design.Blueprint = new Component { Name = hullName, Mass = 50 };
            Hull hull = new Hull { ArmorStrength = armor, FuelCapacity = 10000, Modules = new List<HullModule>() };
            Engine engine = new Engine();
            for (int i = 0; i < engine.FuelConsumption.Length; i++)
            {
                engine.FuelConsumption[i] = 100;
            }

            Component engineComponent = new Component { Name = "Test Engine" };
            engineComponent.Properties.Add("Engine", engine);
            hull.Modules.Add(new HullModule { AllocatedComponent = engineComponent, ComponentCount = 1, ComponentMaximum = 1 });
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            return design;
        }

        private Fleet AddFleet(EmpireData owner, NovaPoint position, params (ShipDesign design, int quantity)[] stacks)
        {
            Fleet fleet = new Fleet(owner.GetNextFleetKey());
            fleet.Name = "Fleet " + fleet.Key;
            fleet.Position = position;
            foreach ((ShipDesign design, int quantity) in stacks)
            {
                ShipToken token = new ShipToken(design, quantity);
                fleet.Composition.Add(token.Key, token);
            }

            owner.OwnedFleets.Add(fleet);
            return fleet;
        }

        private Minefield AddField(EmpireData owner, int x, int y, int mines, bool detonate = false)
        {
            Minefield field = new Minefield { NumberOfMines = mines, Detonate = detonate };
            field.Key = owner.GetNextMinefieldKey();
            field.Owner = owner.Id;
            field.Position = new NovaPoint(x, y);
            serverState.AllMinefields[field.Key] = field;
            return field;
        }

        private MinefieldHit Strike(Fleet fleet)
        {
            // Through a 400-mine field centred at (40, 0), warp 9: the first roll of 0 hits.
            fleet.Position = new NovaPoint(81, 0);
            return new CheckForMinefields(serverState, new ScriptedRandom(0)).Check(fleet, new NovaPoint(0, 0));
        }

        [Test]
        public void AStrike_MakesTheFieldKnownToTheFleetsRace()
        {
            Minefield field = AddField(layer, 40, 0, 400);
            Fleet fleet = AddFleet(mover, new NovaPoint(0, 0), (MakeDesign(5000), 1));
            Assert.IsFalse(field.IsKnownTo(mover.Id));

            Assert.AreEqual(MinefieldHit.Hit, Strike(fleet));

            Assert.IsTrue(field.IsKnownTo(mover.Id));
            Assert.IsTrue(field.IsKnownTo(layer.Id), "The owner always sees its own field");
        }

        [Test]
        public void ASpaceDemolitionOwner_LearnsTheFullDesignsItHit_EvenOfDestroyedStacks()
        {
            layer.Race.Traits.SetPrimary("SD");
            AddField(layer, 40, 0, 400);
            ShipDesign fragile = MakeDesign(100);
            ShipDesign tough = MakeDesign(5000);
            Fleet fleet = AddFleet(mover, new NovaPoint(0, 0), (fragile, 1), (tough, 1));
            layer.EmpireReports[mover.Id].Designs[fragile.Key] = new ShipDesign(fragile) { Key = fragile.Key };
            layer.EmpireReports[mover.Id].Designs[fragile.Key].ClearAllocated();

            Strike(fleet);

            Dictionary<long, ShipDesign> known = layer.EmpireReports[mover.Id].Designs;
            Assert.IsTrue(known.ContainsKey(fragile.Key), "The destroyed stack's design is still learned");
            Assert.IsTrue(known.ContainsKey(tough.Key));
            Assert.IsTrue(known[fragile.Key].Hull.Modules.Any(m => m.AllocatedComponent != null), "A full copy replaces the hull-only scan record");
        }

        [Test]
        public void AnOrdinaryOwner_LearnsNoDesigns()
        {
            AddField(layer, 40, 0, 400);
            Fleet fleet = AddFleet(mover, new NovaPoint(0, 0), (MakeDesign(5000), 1));

            Strike(fleet);

            Assert.AreEqual(0, layer.EmpireReports[mover.Id].Designs.Count);
        }

        [Test]
        public void Detonation_DamagesEveryFleetInside_WithNoRollAndNoStop_TheOwnersOwnIncluded()
        {
            Minefield field = AddField(layer, 0, 0, 400, detonate: true);
            Fleet enemy = AddFleet(mover, new NovaPoint(10, 0), (MakeDesign(1000), 5));
            Fleet own = AddFleet(layer, new NovaPoint(0, 10), (MakeDesign(1000), 1));
            Fleet outside = AddFleet(mover, new NovaPoint(30, 0), (MakeDesign(1000), 1));

            int caught = new CheckForMinefields(serverState, new ScriptedRandom()).Detonate(field, new HashSet<long>());

            Assert.AreEqual(2, caught);
            Assert.AreEqual(5000 - 500, enemy.Composition.Values.Single().Armor, 1e-9, "5 x 100, no top-up");
            Assert.AreEqual(1000 - 500, own.Composition.Values.Single().Armor, 1e-9, "1 x 100 + top-up 400");
            Assert.AreEqual(1000, outside.Composition.Values.Single().Armor, 1e-9);
            Assert.AreEqual(10, enemy.Position.X, "No stop: the fleet stays where it is");
            Assert.AreEqual(400, field.NumberOfMines, "No per-fleet strike loss for a detonation");
            Assert.IsFalse(field.IsKnownTo(mover.Id), "A detonation does not reveal the field to the fleet's race");
        }

        [Test]
        public void Detonation_DamagesAnotherRacesMineLayerHulls_OnlyTheOwnersAreSpared()
        {
            Minefield field = AddField(layer, 0, 0, 400, detonate: true);
            Fleet foreignLayers = AddFleet(mover, new NovaPoint(0, 0),
                (MakeDesign(1000, "Mini Mine Layer"), 1), (MakeDesign(1000, "Super Mine Layer"), 1));

            new CheckForMinefields(serverState, new ScriptedRandom()).Detonate(field, new HashSet<long>());

            List<ShipToken> stacks = foreignLayers.Composition.Values.ToList();
            Assert.Less(stacks[0].Armor, 1000, "Another race's Mini Mine Layer hull takes the damage");
            Assert.Less(stacks[1].Armor, 1000, "Another race's Super Mine Layer hull takes the damage");
        }

        [Test]
        public void Detonation_LeavesNoWreckage()
        {
            Minefield field = AddField(layer, 0, 0, 400, detonate: true);
            ShipDesign fragile = MakeDesign(400);
            fragile.Blueprint.Cost = new Resources(30, 0, 0, 0); // wreckage would need minerals to exist
            fragile.Update();
            AddFleet(mover, new NovaPoint(0, 0), (fragile, 5));

            new CheckForMinefields(serverState, new ScriptedRandom()).Detonate(field, new HashSet<long>());

            Assert.AreEqual(0, serverState.AllDeepSpaceMinerals.Count, "a detonation leaves no wreckage");
        }

        [Test]
        public void Detonation_SparesStacksOnTheMineLayerHulls()
        {
            Minefield field = AddField(layer, 0, 0, 400, detonate: true);
            Fleet fleet = AddFleet(layer, new NovaPoint(0, 0), (MakeDesign(1000, "Mini Mine Layer"), 1), (MakeDesign(1000, "Super Mine Layer"), 1), (MakeDesign(1000), 1));

            new CheckForMinefields(serverState, new ScriptedRandom()).Detonate(field, new HashSet<long>());

            List<ShipToken> stacks = fleet.Composition.Values.ToList();
            Assert.AreEqual(1000, stacks[0].Armor, 1e-9);
            Assert.AreEqual(1000, stacks[1].Armor, 1e-9);
            Assert.AreEqual(1000 - 300, stacks[2].Armor, 1e-9, "100 + the top-up 500 - 3 x 100 = 200, which goes to the first stack actually damaged");
        }

        [Test]
        public void Detonation_SendsTheDetonationNotices_ToBothRaces()
        {
            Minefield field = AddField(layer, 0, 0, 400, detonate: true);
            AddFleet(mover, new NovaPoint(0, 0), (MakeDesign(1000), 5));

            new CheckForMinefields(serverState, new ScriptedRandom()).Detonate(field, new HashSet<long>());

            Message toMover = serverState.AllMessages.Single(m => m.Audience == mover.Id);
            Message toLayer = serverState.AllMessages.Single(m => m.Audience == layer.Id);
            StringAssert.Contains("detonation", toMover.Text);
            StringAssert.Contains("no ships were lost", toMover.Text);
            StringAssert.Contains("detonation", toLayer.Text);
        }

        [Test]
        public void AFleetIsCaughtByOnlyOneDetonatingFieldAYear_AndTheStepCleansUpTheDestroyed()
        {
            AddField(layer, 0, 0, 400, detonate: true);
            AddField(layer, 5, 0, 400, detonate: true);
            Fleet survivor = AddFleet(mover, new NovaPoint(2, 0), (MakeDesign(1000), 5));
            Fleet victim = AddFleet(mover, new NovaPoint(3, 0), (MakeDesign(400), 1));

            new MinefieldDecayStep().Process(serverState, null, new ScriptedRandom());

            Assert.AreEqual(5000 - 500, survivor.Composition.Values.Single().Armor, 1e-9, "Hit once, not twice");
            Assert.IsFalse(mover.OwnedFleets.ContainsKey(victim.Key), "500 points destroy a 400-armor ship; the empty fleet is removed");
        }

        [Test]
        public void TheDetonateFlag_RaisesTheDecayRateBy25Points_AfterTheCap()
        {
            Assert.AreEqual(27, MinefieldDecayStep.LossPercent(0, false, true));
            Assert.AreEqual(75, MinefieldDecayStep.LossPercent(20, false, true), "min(50, 82) + 25");
            Assert.AreEqual(2, MinefieldDecayStep.LossPercent(0, false, false));
        }

        [Test]
        public void ADetonatingFieldDecaysAtTheRaisedRate_InTheStep()
        {
            Minefield field = AddField(layer, 1000, 1000, 1000, detonate: true);

            new MinefieldDecayStep().Process(serverState, null, new ScriptedRandom());

            Assert.AreEqual(1000 - 270, field.NumberOfMines, "27% of 1,000");
        }

        [Test]
        public void DetonateAndKnown_SurviveASaveAndReload()
        {
            Minefield field = AddField(layer, 10, 20, 900, detonate: true);
            field.Known.Add(3);
            field.Known.Add(1);
            XmlDocument xml = new XmlDocument();
            XmlElement element = field.ToXml(xml);

            Minefield reloaded = new Minefield(element);

            Assert.IsTrue(reloaded.Detonate);
            Assert.IsTrue(reloaded.IsKnownTo(1));
            Assert.IsTrue(reloaded.IsKnownTo(3));
            Assert.IsFalse(reloaded.IsKnownTo(4));
        }
    }

    /// <summary>
    /// Message 382 (turn-generation-engine.md §3, "Where the mines go"): no field record can be
    /// created.
    /// </summary>
    [TestFixture]
    public class MineLayingNoFieldRecordTest
    {
        [Test]
        public void WhenNoNewFieldRecordCanBeCreated_TheOwnerGetsTheTechnicalDifficultiesNotice()
        {
            ServerData serverState = new SimpleServerData();
            EmpireData owner = new SimpleEmpireData { Id = 1, Race = new Race { PluralName = "Layers" } };
            serverState.AllEmpires.Add(owner.Id, owner);
            for (int i = 0; i < LayMines.MaxMinefieldSerials; i++)
            {
                Minefield existing = new Minefield { NumberOfMines = 100, Owner = owner.Id, Position = new NovaPoint(5000 + (i * 100), 5000) };
                existing.Key = owner.GetNextMinefieldKey();
                serverState.AllMinefields[existing.Key] = existing;
            }

            Fleet fleet = new Fleet(owner.GetNextFleetKey()) { Name = "Layer", Position = new NovaPoint(0, 0) };
            fleet.Owner = owner.Id;

            Minefield laid = new LayMines(serverState).Lay(fleet, MinefieldType.Standard, 200);

            Assert.IsNull(laid);
            Assert.AreEqual(LayMines.MaxMinefieldSerials, serverState.AllMinefields.Count);
            StringAssert.Contains("failed to lay mines this year due to technical difficulties", serverState.AllMessages.Single().Text);
        }
    }

    /// <summary>
    /// No tech-gain roll after bombing (turn-generation-engine.md §4 and §5's six call sites of
    /// FUN_10f0_61a2, none in the bombardment pass).
    /// </summary>
    [TestFixture]
    public class BombingTechGainTest
    {
        [Test]
        public void BombingAwayEveryDefence_GrantsTheBomberNoResearch()
        {
            ServerData serverState = new ServerData();
            EmpireData bomber = new EmpireData { Id = 1 };
            EmpireData owner = new EmpireData { Id = 2 };
            serverState.AllEmpires[bomber.Id] = bomber;
            serverState.AllEmpires[owner.Id] = owner;
            bomber.EmpireReports.Add(owner.Id, new EmpireIntel(owner) { Relation = PlayerRelation.Enemy });
            owner.EmpireReports.Add(bomber.Id, new EmpireIntel(bomber) { Relation = PlayerRelation.Enemy });
            bomber.BattlePlans["Default"] = new BattlePlan { Attack = "Enemies" };
            owner.ResearchLevels = new TechLevel(10, 10, 10, 10, 10, 10);
            TechLevel before = new TechLevel(bomber.ResearchLevels);

            Star star = new Star { Name = "Target", Owner = owner.Id, Colonists = 100000, Mines = 0, Factories = 0, DefenseType = "None" };
            star.Defenses = 5;
            serverState.AllStars[star.Name] = star;
            owner.OwnedStars.Add(star);

            ShipDesign design = CombatTestKit.Design(1, 100, null, false, (new AllComponents().Fetch("Cherry Bomb"), 10));
            Fleet fleet = new Fleet("Bombers", bomber.Id, 1, new Point(0, 0)) { InOrbit = star };
            ShipToken token = new ShipToken(design, 10);
            fleet.Composition.Add(token.Key, token);
            bomber.OwnedFleets.Add(fleet);

            new Bombing(serverState, new Random(1)).BombAll();

            Assert.AreEqual(0, star.Defenses, "Precondition: the defences were eliminated");
            Assert.IsTrue(bomber.ResearchLevels >= before && before >= bomber.ResearchLevels, "No tech-gain roll follows a bombardment");
            Assert.IsTrue(serverState.AllMessages.All(m => m.Type == "Bombing"));
        }
    }
}
