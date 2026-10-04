namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // behavior-specs-8/turn-generation-engine.md section 11 (repair): a fleet's yearly repair rate is
    // 5 if it moved this year, else 10 in deep space, 15 orbiting a planet it does not own, 25 at its
    // own planet without a starbase, 40 at its own starbase and 100 when that starbase has a dock;
    // Inner Strength doubles it; then a FLEET-WIDE bonus is added (behavior-specs-9 §11 "Repair,
    // units and exact rule"): 25 if any stack is a Fuel Transport hull, 50 if any is a Super-Fuel
    // Transport, 50 (not 75) with both, also for a fleet that moved; a starbase repairs itself 50 a
    // year (75 for Inner Strength). The "damage figure" units are fifths of a percent of armor
    // (5 = 1%), so as percentages: 1, 2, 3, 5, 8, 20, +5/+10, and 10% (15% for Inner Strength) for
    // a starbase's own repair.
    //
    // Rewritten for behavior-specs-10 (turn-generation-engine.md section 1 step 25, section 11): repair
    // is now its own RepairStep after the battle (it used to run inside the movement loop via
    // TurnGenerator.RegenerateFleet, which these tests used to invoke); fleets that fought or saw
    // action are skipped, an own planet whose starbase fought gives 5%, a starbase that fought does
    // not repair itself, and there is no 1-armor minimum.
    [TestFixture]
    public class FleetRepairRateTest
    {
        private const int Armor = 10000;

        // Every test ship starts half wrecked: a 100% x 250/500 damage word, so each percent of
        // repair (5 units) gives back exactly 100 armor and the repaired percentage reads exactly.
        // (These tests used to start from Armor = 0, which no damage word can represent: the
        // word caps a surviving ship at 499/500 damage.)
        private const int HalfArmor = Armor / 2;

        private ServerData serverState;
        private EmpireData empire;
        private Star star;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            empire = new SimpleEmpireData { Id = 1, Race = new Race() };
            serverState.AllEmpires.Add(empire.Id, empire);

            star = new Star { Name = "Home", Owner = empire.Id };
            serverState.AllStars.Add(star.Key, star);
        }

        private static ShipDesign MakeDesign(string hullName, int fuelCapacity, int dockCapacity, long key = 1)
        {
            Component blueprint = new Component { Name = hullName, Mass = 100 };
            Hull hull = new Hull { FuelCapacity = fuelCapacity, DockCapacity = dockCapacity, ArmorStrength = Armor };
            hull.Modules = new List<HullModule>();
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Blueprint = blueprint, Name = hullName };
            design.Update();
            return design;
        }

        private static Fleet MakeFleet(ShipDesign design, ushort owner)
        {
            Fleet fleet = new Fleet(1) { Owner = owner };
            fleet.Composition.Add(design.Key, new ShipToken(design, 1) { Armor = HalfArmor });
            return fleet;
        }

        private double RepairedPercent(Fleet fleet, bool moved, ISet<long> foughtThisTurn = null)
        {
            RepairStep.Repair(fleet, RepairStep.RepairRatePercent(serverState, fleet, moved, foughtThisTurn ?? new HashSet<long>()));

            ShipToken token = null;
            foreach (ShipToken t in fleet.Composition.Values)
            {
                token = t;
            }

            return (token.Armor * 100.0 / token.Design.Armor) - 50;
        }

        private void GiveStarBase(int dockCapacity)
        {
            ShipDesign baseDesign = MakeDesign("Base", 0, dockCapacity);
            star.Starbase = MakeFleet(baseDesign, empire.Id);
        }

        [Test]
        public void MovedThisYear_Repairs1Percent_EvenAtAFriendlyStarbase()
        {
            GiveStarBase(1000);
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id);
            fleet.InOrbit = star;

            Assert.AreEqual(1, RepairedPercent(fleet, true));
        }

        [Test]
        public void StoppedInDeepSpace_Repairs2Percent()
        {
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id);

            Assert.AreEqual(2, RepairedPercent(fleet, false));
        }

        [Test]
        public void OrbitingAPlanetItDoesNotOwn_Repairs3Percent()
        {
            star.Owner = 2;
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id);
            fleet.InOrbit = star;

            Assert.AreEqual(3, RepairedPercent(fleet, false));
        }

        [Test]
        public void OwnPlanetWithoutAStarbase_Repairs5Percent()
        {
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id);
            fleet.InOrbit = star;

            Assert.AreEqual(5, RepairedPercent(fleet, false));
        }

        [Test]
        public void OwnStarbaseWithNoDock_Repairs8Percent()
        {
            GiveStarBase(0);
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id);
            fleet.InOrbit = star;

            Assert.AreEqual(8, RepairedPercent(fleet, false));
        }

        [Test]
        public void OwnStarbaseWithADock_Repairs20Percent()
        {
            GiveStarBase(1000);
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id);
            fleet.InOrbit = star;

            Assert.AreEqual(20, RepairedPercent(fleet, false));
        }

        [Test]
        public void InnerStrength_DoublesTheRate()
        {
            empire.Race.Traits.SetPrimary("IS");
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id);
            fleet.InOrbit = star;

            Assert.AreEqual(10, RepairedPercent(fleet, false), "own planet without a starbase: 5% doubled");
        }

        [Test]
        public void FuelTransportStack_AddsFivePercent_AndSuperFuelTransportTen()
        {
            Fleet fuel = MakeFleet(MakeDesign("Fuel Transport", 1000, 0), empire.Id);
            Assert.AreEqual(2 + 5, RepairedPercent(fuel, false));

            Fleet superFuel = MakeFleet(MakeDesign("Super-Fuel Transport", 1000, 0), empire.Id);
            Assert.AreEqual(2 + 10, RepairedPercent(superFuel, false));
        }

        /// <summary>The transport bonus is per fleet, not per stack: two Fuel Transport stacks still
        /// add 5 percentage points once.</summary>
        [Test]
        public void TwoFuelTransportStacks_AddTheBonusOnce()
        {
            Fleet fleet = MakeFleet(MakeDesign("Fuel Transport", 1000, 0, key: 1), empire.Id);
            ShipDesign second = MakeDesign("Fuel Transport", 1000, 0, key: 2);
            fleet.Composition.Add(second.Key, new ShipToken(second, 1) { Armor = HalfArmor });

            Assert.AreEqual(2 + 5, RepairedPercent(fleet, false));
        }

        /// <summary>Fuel Transport plus Super-Fuel Transport in one fleet: +10, not +15.</summary>
        [Test]
        public void FuelAndSuperFuelTransport_AddTenNotFifteen()
        {
            Fleet fleet = MakeFleet(MakeDesign("Fuel Transport", 1000, 0, key: 1), empire.Id);
            ShipDesign second = MakeDesign("Super-Fuel Transport", 1000, 0, key: 2);
            fleet.Composition.Add(second.Key, new ShipToken(second, 1) { Armor = HalfArmor });

            Assert.AreEqual(2 + 10, RepairedPercent(fleet, false));
        }

        /// <summary>Inner Strength doubles the location rate only; the bonus is added after.</summary>
        [Test]
        public void InnerStrength_DoublesTheLocationRateOnly_TransportBonusAddedAfter()
        {
            empire.Race.Traits.SetPrimary("IS");
            Fleet fleet = MakeFleet(MakeDesign("Fuel Transport", 1000, 0), empire.Id);
            fleet.InOrbit = star;

            Assert.AreEqual((5 * 2) + 5, RepairedPercent(fleet, false));
        }

        /// <summary>The bonus applies even to a fleet that moved (1% + 10%).</summary>
        [Test]
        public void TransportBonus_AppliesEvenWhenTheFleetMoved()
        {
            Fleet fleet = MakeFleet(MakeDesign("Super-Fuel Transport", 1000, 0), empire.Id);

            Assert.AreEqual(1 + 10, RepairedPercent(fleet, true));
        }

        [Test]
        public void StarbaseRepairsItself_10Percent_15ForInnerStrength()
        {
            Fleet starbase = MakeFleet(MakeDesign("Base", 0, 1000), empire.Id);
            starbase.InOrbit = star;
            Assert.AreEqual(10, RepairedPercent(starbase, false));

            empire.Race.Traits.SetPrimary("IS");
            Fleet isStarbase = MakeFleet(MakeDesign("Base", 0, 1000), empire.Id);
            isStarbase.InOrbit = star;
            Assert.AreEqual(15, RepairedPercent(isStarbase, false), "75 damage units, not a plain doubling of 50");
        }

        // ---- behavior-specs-10 section 11 "Which fleets" and the starbase-fought cases ----

        private static Fleet MakeFleet(ShipDesign design, ushort owner, uint id)
        {
            Fleet fleet = new Fleet(id) { Owner = owner };
            fleet.Composition.Add(design.Key, new ShipToken(design, 1) { Armor = HalfArmor });
            return fleet;
        }

        private void RecordBattle(params Fleet[] participants)
        {
            BattleReport report = new BattleReport();
            uint stackId = 1;
            foreach (Fleet participant in participants)
            {
                foreach (ShipToken token in participant.Composition.Values)
                {
                    Stack stack = new Stack(participant, stackId++, token);
                    report.Stacks[stack.Key] = stack;
                }
            }

            empire.BattleReports.Add(report);
        }

        /// <summary>Percentage points of armor repaired from the half-wrecked starting state.</summary>
        private static double ArmorPercent(Fleet fleet)
        {
            foreach (ShipToken token in fleet.Composition.Values)
            {
                return (token.Armor * 100.0 / (token.Design.Armor * token.Quantity)) - 50;
            }

            return double.NaN;
        }

        [Test]
        public void Step_FleetThatFoughtThisTurn_IsNotRepaired()
        {
            Fleet fighter = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id, 11);
            Fleet bystander = MakeFleet(MakeDesign("Ship", 100, 0, key: 2), empire.Id, 12);
            empire.OwnedFleets.Add(fighter);
            empire.OwnedFleets.Add(bystander);
            RecordBattle(fighter);

            new RepairStep().Process(serverState);

            Assert.AreEqual(0, ArmorPercent(fighter), "enrolled in a battle this generation: skipped");
            Assert.AreEqual(2, ArmorPercent(bystander), "deep space, not moved: 2%");
        }

        [Test]
        public void Step_FleetThatSawActionInMovement_IsNotRepaired()
        {
            Fleet minefieldVictim = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id, 11);
            empire.OwnedFleets.Add(minefieldVictim);

            new RepairStep(new HashSet<long>(), new HashSet<long> { minefieldVictim.Key }).Process(serverState);

            Assert.AreEqual(0, ArmorPercent(minefieldVictim), "hit a minefield / jumped a gate: skipped");
        }

        [Test]
        public void Step_UsesTheMovedRate_ForAFleetInTheMovedSet()
        {
            Fleet mover = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id, 11);
            empire.OwnedFleets.Add(mover);

            new RepairStep(new HashSet<long> { mover.Key }, new HashSet<long>()).Process(serverState);

            Assert.AreEqual(1, ArmorPercent(mover));
        }

        [Test]
        public void OwnPlanetWhoseStarbaseFought_Repairs5Percent_NotTheStarbaseRate()
        {
            GiveStarBase(1000);
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0, key: 2), empire.Id, 11);
            fleet.InOrbit = star;

            Assert.AreEqual(5, RepairedPercent(fleet, false, new HashSet<long> { star.Starbase.Key }));
        }

        [Test]
        public void Step_StarbaseThatFought_DoesNotRepairItself()
        {
            Fleet starbase = MakeFleet(MakeDesign("Base", 0, 1000), empire.Id, 21);
            starbase.InOrbit = star;
            star.Starbase = starbase;
            empire.OwnedFleets.Add(starbase);
            RecordBattle(starbase);

            new RepairStep().Process(serverState);

            Assert.AreEqual(0, ArmorPercent(starbase));
        }

        /// <summary>
        /// Repair works on the damage word, not on pooled armor (rewritten from the old
        /// "1% of a 20-armor ship is exactly 0.2 armor" test, which added the percentage to the
        /// pooled armor): 10 damage on a 20-armor ship is 250/500; 1% is 5 units, leaving 245/500,
        /// which is 245 x 20 / 500 = 9.8, truncated to 9 points of damage - 11 armor.
        /// </summary>
        [Test]
        public void SmallShip_RepairsOnTheDamageWord_DamageTruncatedToWholePoints()
        {
            Component blueprint = new Component { Name = "Tiny", Mass = 10 };
            Hull hull = new Hull { FuelCapacity = 100, ArmorStrength = 20 };
            hull.Modules = new List<HullModule>();
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(7) { Blueprint = blueprint, Name = "Tiny" };
            design.Update();
            Fleet fleet = MakeFleet(design, empire.Id, 11);
            ShipToken token = null;
            foreach (ShipToken t in fleet.Composition.Values)
            {
                token = t;
            }

            token.Armor = 10;
            RepairStep.Repair(fleet, 1);

            Assert.AreEqual(11, token.Armor, 1e-9);
            Assert.AreEqual(245, Nova.Common.Combat.DamageWord.FromPacked(token.PackedDamage).Units);
        }

        /// <summary>
        /// The damaged-ship percentage survives repair: 2 of 4 ships at 100/500 damage, repaired
        /// 2% (10 units), stay 2 of 4 ships damaged, now at 90/500; a rate at least the units
        /// clears the word completely.
        /// </summary>
        [Test]
        public void Repair_KeepsTheDamagedShipPercentage_AndClearsTheWordWhenTheRateCoversIt()
        {
            ShipDesign design = MakeDesign("Ship", 100, 0);
            Fleet fleet = new Fleet(31) { Owner = empire.Id };
            ShipToken token = new ShipToken(design, 4);
            fleet.Composition.Add(design.Key, token);
            Nova.Common.Combat.DamageWord.Store(token, new Nova.Common.Combat.DamageWord(50, 100));

            RepairStep.Repair(fleet, 2);

            Nova.Common.Combat.DamageWord word = Nova.Common.Combat.DamageWord.FromPacked(token.PackedDamage);
            Assert.AreEqual(50, word.Percent);
            Assert.AreEqual(90, word.Units);
            Assert.AreEqual((4 * Armor) - (2 * (90 * Armor / 500)), token.Armor, 1e-9);

            RepairStep.Repair(fleet, 20);
            Assert.AreEqual(0, token.PackedDamage);
            Assert.AreEqual(4 * Armor, token.Armor, 1e-9);
        }

        /// <summary>Repair is turn step 25: registered after the Mystery Trader step (23, the end
        /// of the post-movement stage) and before the design pass (90) and the scan step (99).</summary>
        [Test]
        public void RepairStep_IsRegisteredAfterThePostBattleSteps_AndBeforeTheDesignPass()
        {
            SimpleTurnGenerator generator = new SimpleTurnGenerator(serverState);
            var steps = (SortedList<int, ITurnStep>)typeof(TurnGenerator)
                .GetField("turnSteps", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(generator);

            int repairKey = -1;
            int traderKey = -1;
            int designKey = -1;
            foreach (KeyValuePair<int, ITurnStep> step in steps)
            {
                if (step.Value is RepairStep)
                {
                    repairKey = step.Key;
                }
                else if (step.Value is MysteryTraderStep)
                {
                    traderKey = step.Key;
                }
                else if (step.Value is DesignLegalityStep)
                {
                    designKey = step.Key;
                }
            }

            Assert.Greater(repairKey, traderKey, "after the post-movement stage");
            Assert.Less(repairKey, designKey, "before the design pass");
        }

        /// <summary>The movement-time pass no longer repairs (it used to, before the battle).</summary>
        [Test]
        public void MovementTimeRegeneration_NoLongerRepairs()
        {
            Fleet fleet = MakeFleet(MakeDesign("Ship", 100, 0), empire.Id, 11);
            fleet.InOrbit = star;
            SimpleTurnGenerator generator = new SimpleTurnGenerator(serverState);
            typeof(TurnGenerator)
                .GetMethod("RegenerateFleet", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(generator, new object[] { fleet, false });

            Assert.AreEqual(0, ArmorPercent(fleet));
        }
    }
}
