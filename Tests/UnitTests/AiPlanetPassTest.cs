namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    using NUnit.Framework;

    /// <summary>
    /// The per-personality planet passes of docs/behavior-specs-10/ai-opponent-behavior.md §12
    /// (personalities 2, 3, 4 and 5 as specified; personality 0 by its summary with neutral
    /// seams), asserting the spec's thresholds and quantities.
    /// </summary>
    [TestFixture]
    public class AiPlanetPassTest
    {
        /// <summary>Returns the scripted rolls in order, then maxValue - 1 (a failing roll for
        /// every "roll below" test).</summary>
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> rolls;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = new Queue<int>(rolls);
            }

            public int Calls { get; private set; }

            public override int Next(int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
            }

            public override int Next(int minValue, int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : maxValue - 1;
            }
        }

        private ClientData clientState;
        private Race race;
        private Star home;
        private uint nextFleetId = 1;

        [SetUp]
        public void SetUp()
        {
            clientState = new ClientData();
            clientState.EmpireState.Id = 1;
            race = new Race();
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.OperableFactories = 10;
            race.MineBuildCost = 5;
            race.MineProductionRate = 10;
            race.OperableMines = 10;
            race.GrowthRate = 15;
            clientState.EmpireState.Race = race;
            clientState.EmpireState.TurnYear = Global.StartingYear + 1;
            clientState.EmpireState.ResearchLevels = new TechLevel(10);
            nextFleetId = 1;

            home = AddOwnedStar("Home", new NovaPoint(100, 100), 200000);
        }

        private int Ideal
        {
            get { return race.GravityTolerance.OptimumLevel; }
        }

        private int Year
        {
            set { clientState.EmpireState.TurnYear = Global.StartingYear + value; }
        }

        private Star AddOwnedStar(string name, NovaPoint position, int colonists)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = clientState.EmpireState.Id;
            star.Position = position;
            star.ThisRace = race;
            star.Colonists = colonists;
            star.Gravity = star.OriginalGravity = Ideal;
            star.Temperature = star.OriginalTemperature = Ideal;
            star.Radiation = star.OriginalRadiation = Ideal;
            star.ResourcesOnHand = new Resources(1000, 1000, 1000, 0);
            star.MineralConcentration = new Resources(50, 50, 50, 0);
            clientState.EmpireState.OwnedStars.Add(star);
            clientState.EmpireState.StarReports.Add(name, new StarIntel { Name = name, Position = position, Owner = clientState.EmpireState.Id });
            return star;
        }

        private StarIntel AddUnownedPlanet(string name, NovaPoint position)
        {
            StarIntel report = new StarIntel { Name = name, Position = position, Owner = Global.Nobody, Gravity = Ideal, Temperature = Ideal, Radiation = Ideal };
            clientState.EmpireState.StarReports.Add(name, report);
            return report;
        }

        private ShipDesign MakeDesign(string hullName, string tag, ItemType type = ItemType.Ship, int cost = 10)
        {
            ShipDesign design = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            design.Name = tag == null ? hullName : AiDesignRoleTag.Name(hullName, tag, Global.StartingYear + 1);
            design.Type = type;
            design.Blueprint = new Component();
            design.Blueprint.Name = hullName;
            design.Blueprint.Cost = new Resources(cost, cost, cost, cost);
            Hull hull = new Hull();
            hull.FuelCapacity = type == ItemType.Starbase ? 0 : 100; // Nova: no fuel tank = starbase
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            clientState.EmpireState.Designs[design.Key] = design;
            return design;
        }

        private ShipDesign GiveStarbase(Star star, ShipDesign design = null)
        {
            design = design ?? MakeDesign("Space Station", null, ItemType.Starbase);
            Fleet starbase = new Fleet(star.Name + " Starbase", clientState.EmpireState.Id, nextFleetId++, star.Position);
            ShipToken token = new ShipToken(design, 1);
            starbase.Composition.Add(token.Key, token);
            starbase.Type = ItemType.Starbase;
            starbase.InOrbit = star;
            star.Starbase = starbase;
            return design;
        }

        private Fleet AddFleet(ShipDesign design, int ships, Star at)
        {
            Fleet fleet = new Fleet(design.Name + " fleet", clientState.EmpireState.Id, nextFleetId++, at.Position);
            ShipToken token = new ShipToken(design, ships);
            fleet.Composition.Add(token.Key, token);
            fleet.InOrbit = at;
            fleet.Waypoints.Add(new Waypoint { Position = at.Position, Destination = at.Name });
            clientState.EmpireState.OwnedFleets.Add(fleet);
            return fleet;
        }

        private AiPlanetPassContext Context(int category, Random random, int skill = AiCategory.StandardSkill)
        {
            DefaultAIPlanner planner = new DefaultAIPlanner(clientState, category);
            AiFleetContext fleets = new AiFleetContext(clientState, category, skill, random, clientState.EmpireState.OwnedFleets.Values.ToList());
            return new AiPlanetPassContext(clientState, category, skill, random, planner, fleets);
        }

        private void RunPass(Star star, AiPlanetPassContext context)
        {
            DefaultPlanetAI planetAI = new DefaultPlanetAI(star, clientState, context.Planner, context.Random, context.Category);
            AiPlanetPasses.Run(planetAI, context);
        }

        private List<Tuple<ShipDesign, int>> QueuedShips(Star star)
        {
            return star.ManufacturingQueue.Queue
                .Where(order => order.Unit is ShipProductionUnit)
                .Select(order => Tuple.Create(clientState.EmpireState.Designs[((ShipProductionUnit)order.Unit).DesignKey], order.Quantity))
                .ToList();
        }

        // ================================================================ rules

        [Test]
        public void Automitron_FreighterQuota_IsTheLargerOfATenthOfThePlanetsAndTwiceTheHubs()
        {
            Assert.IsFalse(AiPlanetPassRules.AutomitronWantsFreighter(4, 0, 100, 5, new ScriptedRandom()), "needs Pr > 4");
            Assert.IsTrue(AiPlanetPassRules.AutomitronWantsFreighter(5, 9, 100, 3, new ScriptedRandom()), "9 < max(10, 6)");
            Assert.IsTrue(AiPlanetPassRules.AutomitronWantsFreighter(5, 9, 30, 5, new ScriptedRandom()), "9 < max(3, 10)");

            // At or over the quota: fewer than 10/7 of it on a 1-in-4 roll (quota 10: up to 14).
            Assert.IsTrue(AiPlanetPassRules.AutomitronWantsFreighter(5, 14, 100, 0, new ScriptedRandom(0)));
            Assert.IsFalse(AiPlanetPassRules.AutomitronWantsFreighter(5, 14, 100, 0, new ScriptedRandom(1)));
            Assert.IsFalse(AiPlanetPassRules.AutomitronWantsFreighter(5, 15, 100, 0, new ScriptedRandom(0)), "15 is not below 100/7");
        }

        [Test]
        public void Automitron_Minelayers_ThreeOnTheRolls()
        {
            Assert.AreEqual(3, AiPlanetPassRules.AutomitronMinelayers(0, new ScriptedRandom(0, 1, 0)), "1-in-3, limit 10, 1-in-1");
            Assert.AreEqual(0, AiPlanetPassRules.AutomitronMinelayers(0, new ScriptedRandom(1)), "the 1-in-3 roll fails");
            Assert.AreEqual(0, AiPlanetPassRules.AutomitronMinelayers(10, new ScriptedRandom(0, 1)), "10 is not fewer than 10");
            Assert.AreEqual(3, AiPlanetPassRules.AutomitronMinelayers(10, new ScriptedRandom(0, 0, 0)), "the 1-in-8 roll raises the limit to 17");
            Assert.AreEqual(0, AiPlanetPassRules.AutomitronMinelayers(2, new ScriptedRandom(0, 1, 1)), "1-in-5 roll fails");
        }

        [Test]
        public void Automitron_CappedBuild_StopsAtFiveTheCapOrTheSurplus()
        {
            Resources cost = new Resources(10, 10, 10, 10);
            Assert.AreEqual(5, AiPlanetPassRules.AutomitronCappedBuild(0, 100, new Resources(1000, 1000, 1000, 1000), cost));
            Assert.AreEqual(1, AiPlanetPassRules.AutomitronCappedBuild(3, 4, new Resources(1000, 1000, 1000, 1000), cost));
            Assert.AreEqual(2, AiPlanetPassRules.AutomitronCappedBuild(0, 100, new Resources(1000, 25, 1000, 1000), cost), "boranium surplus pays for two");
            Assert.AreEqual(0, AiPlanetPassRules.AutomitronCappedBuild(0, 100, new Resources(5, 1000, 1000, 1000), cost));
            Assert.AreEqual(4, AiPlanetPassRules.AutomitronGarrisonCap(23));
            Assert.AreEqual(5, AiPlanetPassRules.AutomitronGarrisonCap(24));
            Assert.AreEqual(9, AiPlanetPassRules.AutomitronLineCap(12));
        }

        [Test]
        public void Rototill_ColonyShip_WhileExistingPlusOneAreFewerThanTargets_OrNoneExist()
        {
            Assert.IsTrue(AiPlanetPassRules.RototillWantsColonyShip(0, 0), "none exist");
            Assert.IsTrue(AiPlanetPassRules.RototillWantsColonyShip(1, 3));
            Assert.IsFalse(AiPlanetPassRules.RototillWantsColonyShip(2, 3));
        }

        [Test]
        public void Cybertron_Terraform()
        {
            Assert.AreEqual(3, AiPlanetPassRules.CybertronTerraform(9, 50, 140), "habitability below 10: surplus / 70 + 1");
            Assert.AreEqual(1, AiPlanetPassRules.CybertronTerraform(9, 50, 0));
            Assert.AreEqual(1, AiPlanetPassRules.CybertronTerraform(10, 11, 71), "below the potential, surplus over 70");
            Assert.AreEqual(0, AiPlanetPassRules.CybertronTerraform(10, 11, 70));
            Assert.AreEqual(0, AiPlanetPassRules.CybertronTerraform(11, 11, 1000), "at the potential");
        }

        [Test]
        public void Cybertron_ColonyShips_AlternateUnlessTheGrowthProductIsHigh()
        {
            Assert.AreEqual(1, AiPlanetPassRules.CybertronColonyShips(false, 100, 0, true, 50));
            Assert.AreEqual(0, AiPlanetPassRules.CybertronColonyShips(true, 5500, 0, true, 50), "bit 3 set, product not above 5,500");
            Assert.AreEqual(1, AiPlanetPassRules.CybertronColonyShips(true, 5501, 0, true, 50));
            Assert.AreEqual(2, AiPlanetPassRules.CybertronColonyShips(true, 15001, 0, true, 99), "a second above 15,000 before year 100");
            Assert.AreEqual(1, AiPlanetPassRules.CybertronColonyShips(true, 15001, 0, true, 100));
            Assert.AreEqual(0, AiPlanetPassRules.CybertronColonyShips(false, 100, 40, true, 50), "40 slot-1 ships exist");
            Assert.AreEqual(0, AiPlanetPassRules.CybertronColonyShips(false, 100, 0, false, 50), "isolation check");
            Assert.AreEqual(17 * 300 * 2, AiPlanetPassRules.GrowthProduct(300, 17, true), "doubled for Hyper Expansion");
        }

        [Test]
        public void Cybertron_Chooser_ChanceThenOneRoll()
        {
            // 10% chance: roll 9 passes, 10 fails.
            Assert.AreEqual(CybertronBuild.None, AiPlanetPassRules.CybertronChooser(false, true, 0, true, 0, true, true, 0, new ScriptedRandom(10)));
            Assert.AreEqual(CybertronBuild.BattleGroup, AiPlanetPassRules.CybertronChooser(false, true, 0, false, 0, true, true, 0, new ScriptedRandom(9, 51)));
            Assert.AreEqual(CybertronBuild.Hunter, AiPlanetPassRules.CybertronChooser(false, true, 0, false, 0, true, true, 0, new ScriptedRandom(9, 50)), "r = 50 is not above 50");

            // 40% with room: roll 39 passes.
            Assert.AreEqual(CybertronBuild.Hunter, AiPlanetPassRules.CybertronChooser(true, false, 0, false, 0, true, true, 0, new ScriptedRandom(39, 99)));

            // Guard: offered on its chance roll (50% near a foreign planet), taken for r 26-50 too.
            Assert.AreEqual(CybertronBuild.Guard, AiPlanetPassRules.CybertronChooser(false, true, 0, true, 0, true, true, 0, new ScriptedRandom(0, 49, 40)));
            Assert.AreEqual(CybertronBuild.Hunter, AiPlanetPassRules.CybertronChooser(false, true, 0, true, 0, false, true, 0, new ScriptedRandom(0, 10, 40)), "10% away from the frontier");
            Assert.AreEqual(CybertronBuild.Guard, AiPlanetPassRules.CybertronChooser(false, true, 0, true, 0, false, true, 0, new ScriptedRandom(0, 9, 40)));
            Assert.AreEqual(CybertronBuild.Hunter, AiPlanetPassRules.CybertronChooser(false, true, 0, true, 40, true, true, 0, new ScriptedRandom(0, 40)), "40 guard fleets: no offer, no roll");
        }

        [Test]
        public void Cybertron_Chooser_FleetCaps()
        {
            Assert.AreEqual(CybertronBuild.Hunter, AiPlanetPassRules.CybertronChooser(false, true, 251, false, 0, true, true, 0, new ScriptedRandom(0, 99)), "no group above 250 bound fleets");
            Assert.AreEqual(CybertronBuild.BattleGroup, AiPlanetPassRules.CybertronChooser(false, true, 250, false, 0, true, true, 0, new ScriptedRandom(0, 99)));
            Assert.AreEqual(CybertronBuild.None, AiPlanetPassRules.CybertronChooser(false, false, 0, false, 0, true, true, 121, new ScriptedRandom(0, 0)), "no hunter above 120 unbound fleets");
            Assert.AreEqual(CybertronBuild.Hunter, AiPlanetPassRules.CybertronChooser(false, false, 0, false, 0, true, true, 120, new ScriptedRandom(0, 0)));
        }

        [Test]
        public void Macinti_ColonyLineSkip_WithTheEightPercentOverride()
        {
            Assert.IsFalse(AiPlanetPassRules.MacintiColonyLineSkipped(50, 10, true, new ScriptedRandom()), "nothing refuses");
            Assert.IsTrue(AiPlanetPassRules.MacintiColonyLineSkipped(50, 10, false, new ScriptedRandom(8)), "the gate refuses");
            Assert.IsFalse(AiPlanetPassRules.MacintiColonyLineSkipped(50, 10, false, new ScriptedRandom(7)), "overridden on a roll of 7 or less");
            Assert.IsTrue(AiPlanetPassRules.MacintiColonyLineSkipped(50, 101, true, new ScriptedRandom(50)), "F above 100");
            Assert.IsTrue(AiPlanetPassRules.MacintiColonyLineSkipped(121, 41, true, new ScriptedRandom(50)), "F above 40 after year 120");
            Assert.IsFalse(AiPlanetPassRules.MacintiColonyLineSkipped(120, 41, true, new ScriptedRandom()));
            Assert.IsTrue(AiPlanetPassRules.MacintiColonyLineSkipped(121, 50, true, new ScriptedRandom(0)), "F above 49 after year 120: no override");
        }

        [Test]
        public void Macinti_ExtraColonyShips()
        {
            Assert.AreEqual(0, AiPlanetPassRules.MacintiExtraColonyShips(4, 10000, 100, 3), "not before year 5");
            Assert.AreEqual(1, AiPlanetPassRules.MacintiExtraColonyShips(5, 2301, 36, 1));
            Assert.AreEqual(0, AiPlanetPassRules.MacintiExtraColonyShips(5, 2301, 36, 0), "skill 1 or more");
            Assert.AreEqual(1, AiPlanetPassRules.MacintiExtraColonyShips(5, 3601, 51, 1), "the third needs skill 2");
            Assert.AreEqual(2, AiPlanetPassRules.MacintiExtraColonyShips(5, 3601, 51, 2));
            Assert.AreEqual(1, AiPlanetPassRules.MacintiExtraColonyShips(5, 3601, 50, 2), "resources must exceed 50");
        }

        [Test]
        public void Macinti_GenesisSelection()
        {
            Resources poor = new Resources(100, 100, 100, 0);
            Assert.IsTrue(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, poor, 14, new ScriptedRandom()), "c below 15 always");
            Assert.IsFalse(AiPlanetPassRules.MacintiGenesisCandidate(10000, false, poor, 14, new ScriptedRandom()), "population must exceed 10,000 units");
            Assert.IsFalse(AiPlanetPassRules.MacintiGenesisCandidate(10001, true, poor, 14, new ScriptedRandom()), "one already queued");
            Assert.IsFalse(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, new Resources(2000, 2000, 1999, 0), 14, new ScriptedRandom()), "exactly two leading minerals");
            Assert.IsTrue(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, new Resources(2000, 2000, 2000, 0), 14, new ScriptedRandom()), "three leading minerals pass");
            Assert.IsTrue(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, new Resources(1999, 2000, 0, 0), 14, new ScriptedRandom()), "zero leading minerals pass");

            Assert.IsTrue(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, poor, 20, new ScriptedRandom(1)), "15-29: the 2-in-3 roll");
            Assert.IsTrue(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, poor, 20, new ScriptedRandom(2, 3)), "then the 4-in-5 roll");
            Assert.IsFalse(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, poor, 20, new ScriptedRandom(2, 4)));
            Assert.IsTrue(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, poor, 59, new ScriptedRandom(3)), "30-59: the 4-in-5 roll alone");
            Assert.IsFalse(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, poor, 59, new ScriptedRandom(4)));
            Assert.IsFalse(AiPlanetPassRules.MacintiGenesisCandidate(10001, false, poor, 60, new ScriptedRandom(0)), "60 or more never");
        }

        // ================================================================ personality 2

        [Test]
        public void Automitron_Garrison_UpToTheCap_AtACoveredPlanet()
        {
            GiveStarbase(home);
            ShipDesign garrison = MakeDesign("Battleship", "garrison");
            Fleet existing = AddFleet(garrison, 1, home);

            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom()));

            // One planet: cap 1 / 24 + 4 = 4, one exists.
            Assert.AreEqual(3, QueuedShips(home).Single(item => item.Item1 == garrison).Item2);
            Assert.IsNotNull(existing);
        }

        [Test]
        public void Automitron_Coverage_StarbasePopulationOverFifteenHundredUnits_NoShipQueued()
        {
            ShipDesign garrison = MakeDesign("Battleship", "garrison");
            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom()));
            Assert.IsEmpty(QueuedShips(home), "no starbase");

            GiveStarbase(home);
            home.Colonists = 149900;
            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom()));
            Assert.IsEmpty(QueuedShips(home), "1,499 units is not over 1,499");

            home.Colonists = 150000;
            ShipDesign other = MakeDesign("Scout", null);
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(other), false));
            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom()));
            Assert.AreEqual(1, QueuedShips(home).Count, "a ship design already queued");
            Assert.IsNotNull(garrison);
        }

        [Test]
        public void Automitron_ColonyShip_AfterYearTen_WhenNoneExists_AndATargetIsHabitable()
        {
            GiveStarbase(home);
            ShipDesign colonizer = MakeDesign("Medium Freighter", "colonizer");
            AddUnownedPlanet("Target", new NovaPoint(150, 100));

            Year = 10;
            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom()));
            Assert.IsEmpty(QueuedShips(home), "only after year 10");

            Year = 11;
            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom()));
            Assert.AreEqual(1, QueuedShips(home).Single(item => item.Item1 == colonizer).Item2);

            home.ManufacturingQueue.Queue.Clear();
            Star other = AddOwnedStar("Other", new NovaPoint(300, 300), 1000);
            AddFleet(colonizer, 1, other);
            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom()));
            Assert.IsEmpty(QueuedShips(home), "a slot-1 ship exists");
        }

        [Test]
        public void Automitron_Minelayers_AndBombersIntoAStrongGroup()
        {
            GiveStarbase(home);
            ShipDesign minelayer = MakeDesign("Privateer", "minelayer");
            ShipDesign bomber = MakeDesign("B-17 Bomber", "bomber-a");
            ShipDesign garrisonDesign = MakeDesign("Battleship", "garrison");

            // Year 1: K = 3, Q = 3. A group of 2 garrison ships (strength 4) and 3 bombers.
            Fleet group = new Fleet("Group", clientState.EmpireState.Id, nextFleetId++, home.Position);
            ShipToken heavy = new ShipToken(garrisonDesign, 2);
            ShipToken bombers = new ShipToken(bomber, 3);
            group.Composition.Add(heavy.Key, heavy);
            group.Composition.Add(bombers.Key, bombers);
            group.InOrbit = home;
            group.Waypoints.Add(new Waypoint { Position = home.Position, Destination = home.Name });
            clientState.EmpireState.OwnedFleets.Add(group);

            // Minelayer rolls: 1-in-3 = 0, 1-in-8 = 1 (limit 10), 1-in-(2*0+1) = 0.
            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom(0, 1, 0)));

            List<Tuple<ShipDesign, int>> queued = QueuedShips(home);
            Assert.AreEqual(3, queued.Single(item => item.Item1 == minelayer).Item2);
            Assert.AreEqual(4, queued.Single(item => item.Item1 == bomber).Item2, "four bombers into the existing group");
            Assert.AreEqual(2, queued.Single(item => item.Item1 == garrisonDesign).Item2, "cap 4, two exist");
        }

        [Test]
        public void Automitron_NeverQueuesItsExplorer()
        {
            GiveStarbase(home);
            MakeDesign("Scout", "explorer");

            RunPass(home, Context(AiCategory.Automitrons, new ScriptedRandom()));

            Assert.IsEmpty(QueuedShips(home), "§12: personality 2's planet pass has no slot-0 line");
        }

        // ================================================================ personality 3

        [Test]
        public void Rototill_YearZero_TwoSlotZeroShips_ThenOneColonyShipAtTheFirstPlanet()
        {
            GiveStarbase(home);
            Star second = AddOwnedStar("Second", new NovaPoint(300, 100), 200000);
            GiveStarbase(second);
            ShipDesign explorer = MakeDesign("Scout", "explorer");
            ShipDesign colonizer = MakeDesign("Colony Ship", "colonizer");

            Year = 0;
            AiPlanetPassContext context = Context(AiCategory.Rototills, new ScriptedRandom());
            RunPass(home, context);
            Assert.AreEqual(2, QueuedShips(home).Single(item => item.Item1 == explorer).Item2);

            home.ManufacturingQueue.Queue.Clear();
            Year = 1;
            context = Context(AiCategory.Rototills, new ScriptedRandom());
            RunPass(home, context);
            RunPass(second, context);
            Assert.AreEqual(1, QueuedShips(home).Single(item => item.Item1 == colonizer).Item2, "none exist: one colony ship");
            Assert.IsEmpty(QueuedShips(second), "only at the first eligible planet");
        }

        // ================================================================ personality 4

        [Test]
        public void Cybertron_Chooser_QueuesTheNewestHunter()
        {
            GiveStarbase(home, MakeDesign("Starbase", null, ItemType.Starbase));
            ShipDesign hunter = MakeDesign("Destroyer", "hunter-a");

            // Chooser: 5 < 10% chance, no guard design (no roll), r = 10: hunter.
            AiPlanetPassContext context = Context(AiCategory.Cybertrons, new ScriptedRandom(5, 10));
            RunPass(home, context);

            Assert.AreEqual(1, QueuedShips(home).Single(item => item.Item1 == hunter).Item2);
            Assert.AreEqual(1, context.CybertronUnboundWarshipFleets, "each hunter order adds one");
        }

        [Test]
        public void Cybertron_BattleGroup_TwoEachOfTheLowerSlots_ThirdThreeInFour_TopOneInTwo()
        {
            GiveStarbase(home, MakeDesign("Starbase", null, ItemType.Starbase));
            ShipDesign a = MakeDesign("Cruiser", "group1-a");
            ShipDesign b = MakeDesign("Cruiser", "group1-b");
            ShipDesign c = MakeDesign("Nubian", "group1-c");
            ShipDesign d = MakeDesign("Battleship", "group1-d");

            // Chance 0, r = 60 (group), third 74 (< 75), top 50 (not < 50).
            AiPlanetPassContext context = Context(AiCategory.Cybertrons, new ScriptedRandom(0, 60, 74, 50));
            RunPass(home, context);

            List<Tuple<ShipDesign, int>> queued = QueuedShips(home);
            Assert.AreEqual(2, queued.Single(item => item.Item1 == a).Item2);
            Assert.AreEqual(2, queued.Single(item => item.Item1 == b).Item2);
            Assert.AreEqual(1, queued.Single(item => item.Item1 == c).Item2);
            Assert.IsFalse(queued.Any(item => item.Item1 == d));
            Assert.AreEqual(1, context.CybertronBoundWarshipFleets);
        }

        [Test]
        public void Cybertron_ColonyShipsAndHauler_AtANonHubStarbasePlanet()
        {
            GiveStarbase(home, MakeDesign("Starbase", null, ItemType.Starbase));
            home.Colonists = 300000; // 3,000 units: product 3,000 x 15 = 45,000
            AddOwnedStar("Small", new NovaPoint(200, 100), 5000);
            ShipDesign colonizer = MakeDesign("Colony Ship", "colonizer");
            ShipDesign hauler = MakeDesign("Privateer", "hauler-a");

            Year = 50;
            RunPass(home, Context(AiCategory.Cybertrons, new ScriptedRandom()));

            List<Tuple<ShipDesign, int>> queued = QueuedShips(home);
            Assert.AreEqual(2, queued.Single(item => item.Item1 == colonizer).Item2, "product over 15,000 before year 100");
            Assert.AreEqual(1, queued.Single(item => item.Item1 == hauler).Item2, "over 2,000 units, a small own planet within 400 ly");
        }

        [Test]
        public void Cybertron_NothingAtAPacketHubStarbase_AndTheChooserDoesNotRun()
        {
            MakeDesign("Starbase", null, ItemType.Starbase);
            ShipDesign hubDesign = MakeDesign("Space Dock", null, ItemType.Starbase); // slot 1: a packet hub
            GiveStarbase(home, hubDesign);
            home.Colonists = 300000;
            MakeDesign("Colony Ship", "colonizer");
            MakeDesign("Destroyer", "hunter-a");

            RunPass(home, Context(AiCategory.Cybertrons, new ScriptedRandom(0, 0)));

            // A packet hub builds no colony ships, haulers, minelayers or warships from the pass.
            CollectionAssert.IsEmpty(QueuedShips(home));
        }

        [Test]
        public void Cybertron_SkipsAnOverCommittedPlanet()
        {
            GiveStarbase(home, MakeDesign("Starbase", null, ItemType.Starbase));
            MakeDesign("Destroyer", "hunter-a");
            ShipDesign expensive = MakeDesign("Battleship", null, ItemType.Ship, 100000);
            home.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(expensive), false));

            ScriptedRandom random = new ScriptedRandom(0, 0);
            RunPass(home, Context(AiCategory.Cybertrons, random));

            Assert.AreEqual(1, home.ManufacturingQueue.Queue.Count);
            Assert.AreEqual(0, random.Calls);
        }

        // ================================================================ personality 5

        private ShipDesign MacintiSlotOneStarbase()
        {
            MakeDesign("Orbital Fort", null, ItemType.Starbase);
            return GiveStarbase(home, MakeDesign("Space Station", null, ItemType.Starbase));
        }

        [Test]
        public void Macinti_ColonyShip_AndBeforeYearFiveNothingElse()
        {
            MacintiSlotOneStarbase();
            home.Colonists = 30000; // 300 units
            ShipDesign colonizer = MakeDesign("Colony Ship", "colonizer");
            MakeDesign("Frigate", "minelayer");

            Year = 3;
            RunPass(home, Context(AiCategory.Macinti, new ScriptedRandom()));

            CollectionAssert.AreEqual(new[] { colonizer }, QueuedShips(home).Select(item => item.Item1).ToArray());
        }

        [Test]
        public void Macinti_MineralStop_And_SlotZeroSkip()
        {
            MacintiSlotOneStarbase();
            home.Colonists = 30000;
            MakeDesign("Colony Ship", "colonizer");
            MakeDesign("Frigate", "minelayer");
            home.ResourcesOnHand = new Resources(1000, 29, 1000, 0);

            Year = 10;
            RunPass(home, Context(AiCategory.Macinti, new ScriptedRandom()));
            Assert.IsEmpty(QueuedShips(home), "a mineral under 30 kT: nothing more this turn");

            SetUp();
            ShipDesign fort = MakeDesign("Orbital Fort", null, ItemType.Starbase);
            GiveStarbase(home, fort);
            MakeDesign("Colony Ship", "colonizer");
            Year = 10;
            RunPass(home, Context(AiCategory.Macinti, new ScriptedRandom()));
            Assert.IsEmpty(QueuedShips(home), "a starbase in slot 0 (the Starter Colony) builds nothing");
        }

        [Test]
        public void Macinti_SlotOneStarbase_CoveredOnlyBeforeYearTwentySix()
        {
            MacintiSlotOneStarbase();
            home.Colonists = 30000;
            MakeDesign("Colony Ship", "colonizer");

            Year = 26;
            RunPass(home, Context(AiCategory.Macinti, new ScriptedRandom()));

            Assert.IsEmpty(QueuedShips(home));
        }

        [Test]
        public void Macinti_Haulers_OneTimeInThree_UpToAQuarterOfThePlanets()
        {
            MacintiSlotOneStarbase();
            home.Colonists = 30000;
            for (int i = 0; i < 7; i++)
            {
                AddOwnedStar("Colony " + i, new NovaPoint(400 + (i * 50), 400), 1000);
            }

            ShipDesign hauler = MakeDesign("Large Freighter", "hauler-a");

            // 8 planets: cap min(64, 2). Colony line: no colonizer design. Hauler roll 0, miner none.
            Year = 10;
            AiPlanetPassContext context = Context(AiCategory.Macinti, new ScriptedRandom(0));
            RunPass(home, context);
            Assert.AreEqual(1, QueuedShips(home).Single(item => item.Item1 == hauler).Item2);
            Assert.AreEqual(1, context.MacintiHaulersQueued);

            home.ManufacturingQueue.Queue.Clear();
            Star other = clientState.EmpireState.OwnedStars.Values.First(star => star.Name == "Colony 0");
            AddFleet(hauler, 2, other);
            RunPass(home, Context(AiCategory.Macinti, new ScriptedRandom(0)));
            Assert.IsEmpty(QueuedShips(home), "two haulers already exist");
        }

        [Test]
        public void Macinti_GenesisBudget_IsATwentiethOfThePlanets_AtMostTen()
        {
            Assert.AreEqual(0, Context(AiCategory.Macinti, new ScriptedRandom()).GenesisBudget, "one planet");

            for (int i = 0; i < 39; i++)
            {
                AddOwnedStar("Colony " + i, new NovaPoint(400 + i, 400), 1000);
            }

            Assert.AreEqual(2, Context(AiCategory.Macinti, new ScriptedRandom()).GenesisBudget);
        }

        // ================================================================ personality 0

        [Test]
        public void Robotoid_QueuesEachRoleOnce_WhileNoneIsQueued()
        {
            GiveStarbase(home);
            ShipDesign minelayer = MakeDesign("Frigate", "minelayer");
            ShipDesign strike = MakeDesign("Battleship", "strike2-a");
            ShipDesign line = MakeDesign("Destroyer", "line-a");

            AiPlanetPassContext context = Context(AiCategory.Robotoids, new ScriptedRandom());
            context.ColonyGateAllows = false;
            RunPass(home, context);

            List<Tuple<ShipDesign, int>> queued = QueuedShips(home);
            Assert.AreEqual(AiPlanetPassRules.MinelayerBatch, queued.Single(item => item.Item1 == minelayer).Item2, "minelayers four at a time");
            Assert.AreEqual(1, queued.Single(item => item.Item1 == strike).Item2);
            Assert.AreEqual(1, queued.Single(item => item.Item1 == line).Item2);

            RunPass(home, context);
            Assert.AreEqual(3, QueuedShips(home).Count, "nothing is added while the role is queued");
        }
    }
}
