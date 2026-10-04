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
    /// The AI fleet side of docs/behavior-specs-10/ai-opponent-behavior.md: the §11 fleet-role
    /// predicates, the §12 enemy-fleet hunter (`FUN_1090_1438` and its variant `FUN_1090_3c80`),
    /// the planet-attack handler and target search, the §10/§16 stale-design sweep and
    /// splitter, the wormhole diversion, and the colony and invasion rules of personalities 1-3.
    /// </summary>
    [TestFixture]
    public class AiFleetSpec10Test
    {
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> rolls;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = new Queue<int>(rolls);
            }

            public List<int> Bounds { get; } = new List<int>();

            public override int Next(int maxValue)
            {
                Bounds.Add(maxValue);
                return rolls.Count > 0 ? Math.Min(rolls.Dequeue(), Math.Max(0, maxValue - 1)) : 0;
            }
        }

        private class TestableAi : DefaultAi
        {
            private readonly List<WormholeSighting> wormholes;

            public TestableAi(ClientData state, int personality, Random random = null, List<WormholeSighting> wormholes = null)
            {
                clientState = state;
                commandArguments = new CommandArguments();
                commandArguments.Add(CommandArguments.Option.AiPersonality, personality);
                AiRandom = random ?? new Random(7);
                this.wormholes = wormholes ?? new List<WormholeSighting>();
            }

            protected override IEnumerable<WormholeSighting> KnownWormholes()
            {
                return wormholes;
            }
        }

        private const ushort Me = 1;
        private const ushort Them = 2;

        private ClientData clientState;
        private Race race;
        private Star home;
        private uint nextFleetId = 1;
        private uint nextForeignId = 100;

        /// <summary>Nova code for a category (codes 2-7 are categories 0-5).</summary>
        private static int Code(int category)
        {
            return category + DefaultAi.MinStandardPersonality;
        }

        [SetUp]
        public void SetUp()
        {
            clientState = new ClientData();
            clientState.EmpireState.Id = Me;
            race = new Race();
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.OperableFactories = 10;
            race.MineBuildCost = 5;
            race.MineProductionRate = 10;
            race.OperableMines = 10;
            clientState.EmpireState.Race = race;
            clientState.EmpireState.TurnYear = Global.StartingYear + 1;
            nextFleetId = 1;
            nextForeignId = 100;

            home = AddOwnedStar("Home", new NovaPoint(100, 100), 100000, starbase: true);
        }

        private int Ideal
        {
            get { return race.GravityTolerance.OptimumLevel; }
        }

        // ================================================================ builders

        private Star AddOwnedStar(string name, NovaPoint position, int colonists, bool starbase)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = Me;
            star.Position = position;
            star.ThisRace = race;
            star.Colonists = colonists;
            star.Gravity = star.OriginalGravity = Ideal;
            star.Temperature = star.OriginalTemperature = Ideal;
            star.Radiation = star.OriginalRadiation = Ideal;
            star.ResourcesOnHand = new Resources(1000, 1000, 1000, 0);
            if (starbase)
            {
                star.Starbase = new Fleet(name + " base", Me, 9000 + (uint)clientState.EmpireState.StarReports.Count, position);
            }

            clientState.EmpireState.OwnedStars.Add(star);

            StarIntel report = new StarIntel();
            report.Name = name;
            report.Position = position;
            report.Owner = Me;
            report.Colonists = colonists;
            clientState.EmpireState.StarReports.Add(name, report);
            return star;
        }

        private StarIntel AddPlanet(string name, NovaPoint position, ushort owner, int colonists = 0, bool starbase = false)
        {
            StarIntel report = new StarIntel();
            report.Name = name;
            report.Position = position;
            report.Owner = owner;
            report.Colonists = colonists;
            report.Gravity = Ideal;
            report.Temperature = Ideal;
            report.Radiation = Ideal;
            report.Year = Global.StartingYear;
            if (starbase)
            {
                report.Starbase = new Fleet(name + " base", owner, 8000 + (uint)clientState.EmpireState.StarReports.Count, position);
            }

            clientState.EmpireState.StarReports.Add(name, report);
            return report;
        }

        private ShipDesign MakeDesign(string hullName, bool armed = false, int cargo = 0, bool colonizer = false, string engineName = "Long Hump 6", int fuel = 100)
        {
            ShipDesign design = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            design.Name = hullName + " design";
            design.Blueprint = new Component();
            design.Blueprint.Name = hullName;
            Hull hull = new Hull();
            hull.BaseCargo = cargo;
            hull.FuelCapacity = fuel;
            hull.Modules = new List<HullModule>();

            Component engineComponent = new Component();
            engineComponent.Name = engineName;
            Engine engine = new Engine();
            engine.FuelConsumption = new[] { 0, 0, 0, 0, 35, 120, 175, 235, 360, 420 };
            engineComponent.Properties.Add("Engine", engine);
            HullModule engineModule = new HullModule();
            engineModule.ComponentType = "Engine";
            engineModule.AllocatedComponent = engineComponent;
            engineModule.ComponentCount = 1;
            hull.Modules.Add(engineModule);

            design.Blueprint.Properties.Add("Hull", hull);
            if (armed)
            {
                Weapon weapon = new Weapon();
                weapon.Power = 10;
                weapon.Range = 1;
                design.Blueprint.Properties.Add("Weapon", weapon);
            }

            if (colonizer)
            {
                design.Blueprint.Properties.Add("Colonizer", new DoubleProperty(1.0));
            }

            design.Update();
            clientState.EmpireState.Designs[design.Key] = design;
            return design;
        }

        private Fleet AddFleet(ShipDesign design, int ships, NovaPoint position, Star orbiting)
        {
            Fleet fleet = new Fleet(design.Blueprint.Name + " fleet " + nextFleetId, Me, nextFleetId++, position);
            ShipToken token = new ShipToken(design, ships);
            fleet.Composition.Add(token.Key, token);
            fleet.InOrbit = orbiting;
            fleet.FuelAvailable = fleet.TotalFuelCapacity;
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(position), Destination = orbiting != null ? orbiting.Name : "Space" });
            clientState.EmpireState.OwnedFleets.Add(fleet);
            return fleet;
        }

        private FleetIntel AddForeignFleet(NovaPoint position, ShipDesign design = null, int ships = 1)
        {
            FleetIntel report = new FleetIntel();
            report.Name = "Enemy " + nextForeignId;
            report.Owner = Them;
            report.Id = nextForeignId++;
            report.Position = position;
            report.Composition = new Dictionary<long, ShipToken>();
            if (design != null)
            {
                ShipToken token = new ShipToken(design, ships);
                report.Composition.Add(token.Key, token);
            }

            clientState.EmpireState.FleetReports.Add(report.Key, report);
            return report;
        }

        private AiFleetContext Context(int category, Random random, int skill = AiCategory.StandardSkill)
        {
            List<Fleet> own = clientState.EmpireState.OwnedFleets.Values.Where(f => f.Owner == Me).ToList();
            return new AiFleetContext(clientState, category, skill, random, own);
        }

        // ================================================================ §11 fleet-role predicates (row 39, row 60)

        [Test]
        public void CombatFleet_IsHull6To10WithoutAnArmamentTest_OrAnArmedLightHull()
        {
            Assert.IsTrue(FleetRolePredicates.IsCombatDesign(MakeDesign("Destroyer")), "an unarmed Destroyer is still a combat design");
            Assert.IsTrue(FleetRolePredicates.IsCombatDesign(MakeDesign("Dreadnought")));
            Assert.IsFalse(FleetRolePredicates.IsCombatDesign(MakeDesign("Frigate")), "an unarmed Frigate is not");
            Assert.IsTrue(FleetRolePredicates.IsCombatDesign(MakeDesign("Frigate", armed: true)));
            Assert.IsTrue(FleetRolePredicates.IsCombatDesign(MakeDesign("Nubian", armed: true, cargo: 0)));
            Assert.IsFalse(FleetRolePredicates.IsCombatDesign(MakeDesign("Nubian")), "an unarmed Nubian is neither");
            Assert.IsTrue(FleetRolePredicates.IsCombatDesign(MakeDesign("Meta Morph", armed: true, cargo: 499)));
            Assert.IsFalse(FleetRolePredicates.IsCombatDesign(MakeDesign("Meta Morph", armed: true, cargo: 500)));
            Assert.IsFalse(FleetRolePredicates.IsCombatDesign(MakeDesign("Scout", armed: true)), "the Scout hull is never a combat fleet");
        }

        [Test]
        public void HaulerFleet_IsAFreighterOrPirateHull_OrAnArmedMetaMorphWithABigHold()
        {
            Assert.IsTrue(FleetRolePredicates.IsHaulerDesign(MakeDesign("Small Freighter")));
            Assert.IsTrue(FleetRolePredicates.IsHaulerDesign(MakeDesign("Super Freighter")));
            Assert.IsTrue(FleetRolePredicates.IsHaulerDesign(MakeDesign("Privateer")));
            Assert.IsTrue(FleetRolePredicates.IsHaulerDesign(MakeDesign("Galleon")));
            Assert.IsTrue(FleetRolePredicates.IsHaulerDesign(MakeDesign("Meta Morph", armed: true, cargo: 500)));
            Assert.IsFalse(FleetRolePredicates.IsHaulerDesign(MakeDesign("Meta Morph", armed: false, cargo: 800)), "an unarmed Meta Morph is no hauler");
            Assert.IsFalse(FleetRolePredicates.IsHaulerDesign(MakeDesign("Nubian", armed: true, cargo: 800)), "a Nubian never is");
            Assert.IsFalse(FleetRolePredicates.IsHaulerDesign(MakeDesign("Colony Ship", colonizer: true)));
        }

        [Test]
        public void ScoutOrWarshipLine_IsHull4To10()
        {
            Assert.IsTrue(FleetRolePredicates.IsScoutOrWarshipLineDesign(MakeDesign("Scout")));
            Assert.IsTrue(FleetRolePredicates.IsScoutOrWarshipLineDesign(MakeDesign("Frigate")));
            Assert.IsTrue(FleetRolePredicates.IsScoutOrWarshipLineDesign(MakeDesign("Dreadnought")));
            Assert.IsFalse(FleetRolePredicates.IsScoutOrWarshipLineDesign(MakeDesign("Privateer")));
            Assert.IsFalse(FleetRolePredicates.IsScoutOrWarshipLineDesign(MakeDesign("Nubian", armed: true)));
        }

        [Test]
        public void TheCombatTest_ReadsAForeignFleetsOwnDesigns_NotTheAisDesignTable()
        {
            // §11 defect (row 60): the original indexes the AI's own design table by stack slot.
            FleetIntel freighter = AddForeignFleet(new NovaPoint(0, 0), MakeDesign("Large Freighter"));
            FleetIntel cruiser = AddForeignFleet(new NovaPoint(0, 0), MakeDesign("Cruiser"));

            Assert.IsFalse(FleetRolePredicates.IsCombatFleet(freighter.Composition.Values));
            Assert.IsTrue(FleetRolePredicates.IsCombatFleet(cruiser.Composition.Values));
        }

        [Test]
        public void EngineIndex_FollowsTheOriginalComponentOrder()
        {
            Assert.AreEqual(2, FleetRolePredicates.EngineIndexOf(MakeDesign("Scout", engineName: "Fuel Mizer")));
            Assert.AreEqual(3, FleetRolePredicates.EngineIndexOf(MakeDesign("Scout", engineName: "Long Hump 6")));
            Assert.AreEqual(15, FleetRolePredicates.EngineIndexOf(MakeDesign("Scout", engineName: "Galaxy Scoop")));
        }

        // ================================================================ §12 hunter (row 46)

        [Test]
        public void Hunter_SetsCourseForAnEnemyFleetWithin180Ly()
        {
            Fleet hunter = AddFleet(MakeDesign("Destroyer", armed: true), 3, new NovaPoint(100, 100), home);
            FleetIntel enemy = AddForeignFleet(new NovaPoint(279, 100), MakeDesign("Cruiser"));

            FleetOrder order = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Robotoids, new ScriptedRandom()), new List<Fleet> { hunter }, variant: false);

            Assert.AreSame(enemy, order.PursueFleet, order.Reason);
        }

        [Test]
        public void Hunter_BeyondThe180LyRadius_WithLessThanHalfFuel_RefuelsAtTheNearestStarbasePlanet()
        {
            Star depot = AddOwnedStar("Depot", new NovaPoint(400, 100), 1000, starbase: true);
            Fleet hunter = AddFleet(MakeDesign("Destroyer", armed: true), 3, new NovaPoint(380, 100), null);
            hunter.FuelAvailable = (hunter.TotalFuelCapacity / 2) - 1;
            AddForeignFleet(new NovaPoint(600, 100), MakeDesign("Cruiser"));

            FleetOrder order = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Robotoids, new ScriptedRandom()), new List<Fleet> { hunter }, variant: false);

            Assert.IsNull(order.PursueFleet);
            Assert.AreEqual(depot.Name, order.Destination?.Name, order.Reason);
        }

        [Test]
        public void Hunter_BeyondThe180LyRadius_WithFuel_HeadsForThePlanetNearestTheEnemy()
        {
            AddPlanet("Near enemy", new NovaPoint(590, 100), Global.Nobody);
            AddPlanet("Midway", new NovaPoint(300, 100), Global.Nobody);
            Fleet hunter = AddFleet(MakeDesign("Destroyer", armed: true), 3, new NovaPoint(100, 100), home);
            AddForeignFleet(new NovaPoint(600, 100), MakeDesign("Cruiser"));

            FleetOrder order = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Robotoids, new ScriptedRandom()), new List<Fleet> { hunter }, variant: false);

            Assert.AreEqual("Near enemy", order.Destination?.Name, order.Reason);
        }

        [Test]
        public void Hunter_SkipsAContestedEnemyOneTimeInThree()
        {
            ShipDesign destroyer = MakeDesign("Destroyer", armed: true);
            FleetIntel near = AddForeignFleet(new NovaPoint(120, 100), MakeDesign("Cruiser"));
            FleetIntel far = AddForeignFleet(new NovaPoint(150, 100), MakeDesign("Cruiser"));
            Fleet other = AddFleet(destroyer, 2, new NovaPoint(100, 100), home);
            Waypoint pursuit = new Waypoint();
            pursuit.AimAtFleet(near);
            other.Waypoints.Add(pursuit);
            Fleet hunter = AddFleet(destroyer, 20, new NovaPoint(100, 100), home);
            List<Fleet> combat = new List<Fleet> { other, hunter };

            Assert.AreEqual(2, EnemyFleetHunter.ShipsAlreadyOrderedTo(near, hunter, combat));

            FleetOrder skipped = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Robotoids, new ScriptedRandom(0)), combat, variant: false);
            Assert.AreSame(far, skipped.PursueFleet, "a 0 on the 1-in-3 roll skips the contested fleet");

            // 20 ships is not below 5 x 2, so there is no 1-in-15 roll.
            ScriptedRandom kept = new ScriptedRandom(1);
            FleetOrder chased = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Robotoids, kept), combat, variant: false);
            Assert.AreSame(near, chased.PursueFleet);
            CollectionAssert.AreEqual(new[] { 3 }, kept.Bounds);
        }

        [Test]
        public void Hunter_AFleetBelowFiveTimesTheCommittedShips_IsAlsoSkippedOneTimeInFifteen()
        {
            ShipDesign destroyer = MakeDesign("Destroyer", armed: true);
            FleetIntel near = AddForeignFleet(new NovaPoint(120, 100), MakeDesign("Cruiser"));
            FleetIntel far = AddForeignFleet(new NovaPoint(150, 100), MakeDesign("Cruiser"));
            Fleet other = AddFleet(destroyer, 2, new NovaPoint(100, 100), home);
            Waypoint pursuit = new Waypoint();
            pursuit.AimAtFleet(near);
            other.Waypoints.Add(pursuit);
            Fleet hunter = AddFleet(destroyer, 9, new NovaPoint(100, 100), home);
            List<Fleet> combat = new List<Fleet> { other, hunter };

            ScriptedRandom rolls = new ScriptedRandom(1, 0);
            FleetOrder order = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Robotoids, rolls), combat, variant: false);

            Assert.AreSame(far, order.PursueFleet);
            CollectionAssert.AreEqual(new[] { 3, 15 }, rolls.Bounds);
        }

        [Test]
        public void HunterVariant_KeepsAContestedEnemyTwoTimesInThree_WithNoShipCountRule()
        {
            ShipDesign destroyer = MakeDesign("Destroyer", armed: true);
            FleetIntel near = AddForeignFleet(new NovaPoint(120, 100), MakeDesign("Cruiser"));
            AddForeignFleet(new NovaPoint(150, 100), MakeDesign("Cruiser"));
            Fleet other = AddFleet(destroyer, 2, new NovaPoint(100, 100), home);
            Waypoint pursuit = new Waypoint();
            pursuit.AimAtFleet(near);
            other.Waypoints.Add(pursuit);
            Fleet hunter = AddFleet(destroyer, 1, new NovaPoint(100, 100), home);

            ScriptedRandom rolls = new ScriptedRandom(2);
            FleetOrder order = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Automitrons, rolls), new List<Fleet> { other, hunter }, variant: true);

            Assert.AreSame(near, order.PursueFleet);
            CollectionAssert.AreEqual(new[] { 3 }, rolls.Bounds, "no 1-in-15 roll in the variant");
        }

        [Test]
        public void HunterVariant_AFleetWithNoBeamOrTorpedo_Explores()
        {
            Fleet scout = AddFleet(MakeDesign("Scout"), 1, new NovaPoint(100, 100), home);
            AddForeignFleet(new NovaPoint(120, 100), MakeDesign("Cruiser"));

            FleetOrder order = EnemyFleetHunter.Hunt(scout, Context(AiCategory.Automitrons, new ScriptedRandom()), new List<Fleet>(), variant: true);

            Assert.IsTrue(order.Explore);
            Assert.IsNull(order.PursueFleet);
        }

        [Test]
        public void Hunter_WithNoCandidate_HeadsForTheNearestPlanetOwnedByAnotherPlayer()
        {
            AddPlanet("Theirs", new NovaPoint(400, 100), Them, colonists: 10000);
            AddPlanet("Unowned", new NovaPoint(200, 100), Global.Nobody);
            Fleet hunter = AddFleet(MakeDesign("Destroyer", armed: true), 3, new NovaPoint(100, 100), home);

            FleetOrder order = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Robotoids, new ScriptedRandom()), new List<Fleet> { hunter }, variant: false);

            Assert.AreEqual("Theirs", order.Destination?.Name);
        }

        [Test]
        public void Hunter_AlreadyBoundForAPlanet_KeepsItsOrdersWhenTheNewPickIsAPlanet()
        {
            StarIntel theirs = AddPlanet("Theirs", new NovaPoint(400, 100), Them, colonists: 10000);
            StarIntel other = AddPlanet("Elsewhere", new NovaPoint(100, 400), Global.Nobody);
            Fleet hunter = AddFleet(MakeDesign("Destroyer", armed: true), 3, new NovaPoint(100, 100), home);
            hunter.Waypoints.Add(new Waypoint { Position = other.Position, Destination = other.Name });

            FleetOrder order = EnemyFleetHunter.Hunt(hunter, Context(AiCategory.Robotoids, new ScriptedRandom()), new List<Fleet> { hunter }, variant: false);

            Assert.IsTrue(order.Keep, order.Reason);
            Assert.AreNotEqual(theirs.Name, hunter.Waypoints.Last().Destination);
        }

        [Test]
        public void ARobotoidLineWarship_GetsAPursuitWaypoint_ThroughTheWholeTurn()
        {
            Fleet hunter = AddFleet(MakeDesign("Destroyer", armed: true), 3, new NovaPoint(100, 100), home);
            FleetIntel enemy = AddForeignFleet(new NovaPoint(150, 100), MakeDesign("Cruiser"));

            new TestableAi(clientState, Code(AiCategory.Robotoids)).DoMove();

            Waypoint last = hunter.Waypoints.Last();
            Assert.IsTrue(last.IsFleetTarget);
            Assert.AreEqual(enemy.Key, last.TargetFleetKey);
            Assert.IsInstanceOf<NoTask>(last.Task);
        }

        [Test]
        public void CybertronHunters_LaunchOnceTwiceTheirCountReachesH()
        {
            Assert.AreEqual(1, DefaultAi.HunterThreshold(50));
            Assert.AreEqual(6, DefaultAi.HunterThreshold(100));
            Assert.AreEqual(16, DefaultAi.HunterThreshold(150));
            Assert.AreEqual(36, DefaultAi.HunterThreshold(200));
        }

        // ================================================================ §12 planet-attack handler (row 47)

        [Test]
        public void StrengthAndBomberThresholds_FollowTheYearFormulas()
        {
            Assert.AreEqual(4, PlanetAttackHandler.StrengthK(130, 4));
            Assert.AreEqual(4 + (131 - 120) / 20, PlanetAttackHandler.StrengthK(131, 4));
            Assert.AreEqual(4 + 5, PlanetAttackHandler.StrengthK(220, 4));
            Assert.AreEqual(50, PlanetAttackHandler.StrengthK(2000, 4));

            Assert.AreEqual(6, PlanetAttackHandler.BomberQuotaQ(115, 6));
            Assert.AreEqual(6 + 16 / 22, PlanetAttackHandler.BomberQuotaQ(116, 6));
            Assert.AreEqual(6 + 2, PlanetAttackHandler.BomberQuotaQ(144, 6));
            Assert.AreEqual(12, PlanetAttackHandler.BomberQuotaQ(400, 6));

            Assert.AreEqual(2, PlanetAttackHandler.MinimumBombersQ(6));
            Assert.AreEqual(3, PlanetAttackHandler.MinimumBombersQ(12));
            Assert.AreEqual(0, PlanetAttackHandler.MinimumBombersQ(3), "personality 2's base 3 gives q = 0");
        }

        [Test]
        public void AttackValue_IsTheReportedFigureOver250PlusOne_CappedAtSix_PlusOneForAStarbase()
        {
            AiFleetContext context = Context(AiCategory.Robotoids, new ScriptedRandom());
            Assert.AreEqual(1, PlanetAttackHandler.AttackValue(AddPlanet("Small", new NovaPoint(0, 0), Them, colonists: 40000), context), "figure 100");
            Assert.AreEqual(2, PlanetAttackHandler.AttackValue(AddPlanet("Mid", new NovaPoint(0, 0), Them, colonists: 100000), context), "figure 250");
            Assert.AreEqual(7, PlanetAttackHandler.AttackValue(AddPlanet("Big", new NovaPoint(0, 0), Them, colonists: 1600000, starbase: true), context), "6 + 1");
            Assert.AreEqual(0, PlanetAttackHandler.AttackValue(AddPlanet("Unowned", new NovaPoint(0, 0), Global.Nobody), context));

            AiFleetContext turindrones = Context(AiCategory.Turindrones, new ScriptedRandom());
            Assert.AreEqual(1, PlanetAttackHandler.AttackValue(clientState.EmpireState.StarReports["Mid"], turindrones));
            Assert.AreEqual(2, PlanetAttackHandler.AttackValue(clientState.EmpireState.StarReports["Big"], turindrones));
        }

        [Test]
        public void DistanceBonus_Bands()
        {
            Assert.AreEqual(7, PlanetAttackHandler.DistanceBonus(50 * 50));
            Assert.AreEqual(5, PlanetAttackHandler.DistanceBonus(51 * 51));
            Assert.AreEqual(4, PlanetAttackHandler.DistanceBonus(150 * 150));
            Assert.AreEqual(3, PlanetAttackHandler.DistanceBonus(200 * 200));
            Assert.AreEqual(2, PlanetAttackHandler.DistanceBonus(300 * 300));
            Assert.AreEqual(1, PlanetAttackHandler.DistanceBonus(500 * 500));
            Assert.AreEqual(0, PlanetAttackHandler.DistanceBonus(501 * 501));
        }

        [Test]
        public void TargetSearch_ScoresAttackValuePlusDistance_AndMarksTheWinner()
        {
            AddPlanet("Rich far", new NovaPoint(400, 100), Them, colonists: 1600000); // 6 + 2
            AddPlanet("Poor near", new NovaPoint(140, 100), Them, colonists: 40000);  // 1 + 7
            AiFleetContext context = Context(AiCategory.Robotoids, new ScriptedRandom());

            StarIntel first = PlanetAttackHandler.TargetSearch(new NovaPoint(100, 100), context);
            Assert.AreEqual("Poor near", first.Name, "8 = 8: the tie goes to the nearer planet");
            CollectionAssert.Contains(context.TargetedPlanets, "Poor near");
        }

        [Test]
        public void TargetSearch_AMarkedPlanetIsRejectedThreeTimesInFour_ElseItGains128()
        {
            AddPlanet("Marked", new NovaPoint(400, 100), Them, colonists: 40000);
            AddPlanet("Other", new NovaPoint(140, 100), Them, colonists: 40000);

            AiFleetContext rejecting = Context(AiCategory.Robotoids, new ScriptedRandom(1));
            rejecting.TargetedPlanets.Add("Marked");
            Assert.AreEqual("Other", PlanetAttackHandler.TargetSearch(new NovaPoint(100, 100), rejecting).Name);

            AiFleetContext converging = Context(AiCategory.Robotoids, new ScriptedRandom(0));
            converging.TargetedPlanets.Add("Marked");
            Assert.AreEqual("Marked", PlanetAttackHandler.TargetSearch(new NovaPoint(100, 100), converging).Name, "a surviving mark adds 128");
        }

        [Test]
        public void InvasionEstimate_AndTheGroundAssault()
        {
            // n = 0: 100 - (1 x 18) / 4 = 96.
            Assert.AreEqual(100 * 400 / 96, PlanetAttackHandler.InvasionEstimate(100, 0));
            Assert.AreEqual(100 * 400 / (100 - (16 * 18 / 4)), PlanetAttackHandler.InvasionEstimate(100, 15));

            Assert.IsNull(PlanetAttackHandler.InvasionLanding(0, 416), "nothing aboard: stays and bombs");
            Assert.IsNull(PlanetAttackHandler.InvasionLanding(1000, 416), "C/5 <= E and E >= 200: keeps bombing");
            Assert.AreEqual(Math.Max(2500 / 2, 416 * 5 / 4), PlanetAttackHandler.InvasionLanding(2500, 416), "C/5 > E: lands");
            Assert.AreEqual(Math.Max(300 / 2, 5 * 5 / 4), PlanetAttackHandler.InvasionLanding(300, 5), "E <= 9 and C >= 151: lands");
            Assert.AreEqual(30000, PlanetAttackHandler.InvasionLanding(200000, 100), "capped at 30,000");
        }

        [Test]
        public void TroopLoad_StepsAt1000_2000_3000Units()
        {
            Assert.AreEqual(0, PlanetAttackHandler.TroopLoadAtHome(1000));
            Assert.AreEqual(1001 / 20, PlanetAttackHandler.TroopLoadAtHome(1001));
            Assert.AreEqual(2001 / 15, PlanetAttackHandler.TroopLoadAtHome(2001));
            Assert.AreEqual(3001 / 10, PlanetAttackHandler.TroopLoadAtHome(3001));
        }

        [Test]
        public void AttackHandler_AWeakFleetAtHome_StaysBelowSkillTwo()
        {
            AddPlanet("Theirs", new NovaPoint(140, 100), Them, colonists: 40000);
            Fleet fleet = AddFleet(MakeDesign("Meta Morph", armed: true), 3, home.Position, home); // S = 3 < K = 4

            FleetOrder order = PlanetAttackHandler.Handle(fleet, Context(AiCategory.Robotoids, new ScriptedRandom()), copy: false);

            Assert.IsFalse(order.Moves, order.Reason);
        }

        [Test]
        public void AttackHandler_AStrongFleetAtHome_LoadsTroopsAndRunsTheTargetSearch()
        {
            home.Colonists = 250000; // 2,500 units: troops = 2,500 / 15 = 166
            AddPlanet("Theirs", new NovaPoint(140, 100), Them, colonists: 40000);
            ShipDesign battleship = MakeDesign("Battleship", armed: true, cargo: 1000);
            ShipDesign bomber = MakeDesign("B-52 Bomber");
            Fleet fleet = AddFleet(battleship, 2, home.Position, home); // S = 4
            ShipToken bombers = new ShipToken(bomber, 6);                // B = 6 = Q
            fleet.Composition.Add(bombers.Key, bombers);

            FleetOrder order = PlanetAttackHandler.Handle(fleet, Context(AiCategory.Robotoids, new ScriptedRandom()), copy: false);

            Assert.AreEqual("Theirs", order.Destination?.Name, order.Reason);
            Assert.AreEqual(2500 / 15, order.LoadColonistsKt);

            FleetOrder copied = PlanetAttackHandler.Handle(fleet, Context(AiCategory.Macinti, new ScriptedRandom()), copy: true);
            Assert.AreEqual(0, copied.LoadColonistsKt, "the personality 4/5 copies load no troops");
        }

        [Test]
        public void AttackHandler_AtAForeignPlanet_LandsColonistsWhenTheEstimateAllows()
        {
            StarIntel theirs = AddPlanet("Theirs", new NovaPoint(140, 100), Them, colonists: 2000); // figure 5, E = 20
            Star theirStar = new Star { Name = "Theirs", Position = theirs.Position, Owner = Them };
            ShipDesign metaMorph = MakeDesign("Meta Morph", armed: true, cargo: 400);
            Fleet fleet = AddFleet(metaMorph, 2, theirs.Position, theirStar); // S = 2 = K/2, B = 0 < q = 2
            ShipToken bombers = new ShipToken(MakeDesign("B-52 Bomber"), 2);
            fleet.Composition.Add(bombers.Key, bombers);
            fleet.Cargo.ColonistsInKilotons = 400; // C/5 = 80 > E = 20

            FleetOrder order = PlanetAttackHandler.Handle(fleet, Context(AiCategory.Robotoids, new ScriptedRandom()), copy: false);

            Assert.AreEqual(Math.Max(400 / 2, 20 * 5 / 4), order.UnloadColonistsKt, order.Reason);
            Assert.IsFalse(order.Moves, "it stays");

            FleetOrder copied = PlanetAttackHandler.Handle(fleet, Context(AiCategory.Macinti, new ScriptedRandom()), copy: true);
            Assert.AreEqual(0, copied.UnloadColonistsKt, "no ground assault in the copies");
        }

        [Test]
        public void AttackHandler_KeepsGoingToAForeignPlanet_AndReplansForAFreshUnownedOne()
        {
            StarIntel theirs = AddPlanet("Theirs", new NovaPoint(400, 100), Them, colonists: 40000);
            StarIntel unowned = AddPlanet("Unowned", new NovaPoint(100, 400), Global.Nobody);
            Fleet fleet = AddFleet(MakeDesign("Meta Morph", armed: true), 3, new NovaPoint(150, 100), null);

            fleet.Waypoints.Add(new Waypoint { Position = theirs.Position, Destination = theirs.Name });
            Assert.IsTrue(PlanetAttackHandler.Handle(fleet, Context(AiCategory.Robotoids, new ScriptedRandom()), false).Keep);

            fleet.Waypoints.RemoveAt(1);
            fleet.Waypoints.Add(new Waypoint { Position = unowned.Position, Destination = unowned.Name });
            Assert.IsTrue(PlanetAttackHandler.Handle(fleet, Context(AiCategory.Robotoids, new ScriptedRandom()), false).Keep, "a stale report: keeps going");

            unowned.Year = clientState.EmpireState.TurnYear;
            FleetOrder replanned = PlanetAttackHandler.Handle(fleet, Context(AiCategory.Robotoids, new ScriptedRandom()), false);
            Assert.IsFalse(replanned.Keep, "a report from this year: re-plans");
        }

        [Test]
        public void BomberRule_WaitsAtAHomeStarbaseUntilEscorted()
        {
            AddPlanet("Theirs", new NovaPoint(140, 100), Them, colonists: 40000);
            Fleet bombers = AddFleet(MakeDesign("B-17 Bomber"), 2, home.Position, home);

            Assert.IsFalse(PlanetAttackHandler.BomberRule(bombers, Context(AiCategory.Automitrons, new ScriptedRandom())).Moves);

            ShipToken garrison = new ShipToken(MakeDesign("Battleship", armed: true), 3);
            bombers.Composition.Add(garrison.Key, garrison);
            Assert.AreEqual("Theirs", PlanetAttackHandler.BomberRule(bombers, Context(AiCategory.Automitrons, new ScriptedRandom())).Destination?.Name);
        }

        [Test]
        public void BomberRule_AtAForeignPlanet_KeepsBombingUnlessAForeignCombatFleetIsThere()
        {
            StarIntel theirs = AddPlanet("Theirs", new NovaPoint(140, 100), Them, colonists: 40000);
            AddPlanet("Next", new NovaPoint(300, 100), Them, colonists: 40000);
            Star theirStar = new Star { Name = "Theirs", Position = theirs.Position, Owner = Them };
            Fleet bombers = AddFleet(MakeDesign("B-17 Bomber"), 2, theirs.Position, theirStar);
            AddForeignFleet(new NovaPoint(140, 100), MakeDesign("Large Freighter"));

            Assert.IsFalse(PlanetAttackHandler.BomberRule(bombers, Context(AiCategory.Automitrons, new ScriptedRandom())).Moves, "a freighter is no combat fleet");

            AddForeignFleet(new NovaPoint(140, 100), MakeDesign("Destroyer"));
            Assert.IsTrue(PlanetAttackHandler.BomberRule(bombers, Context(AiCategory.Automitrons, new ScriptedRandom())).Moves);
        }

        // ================================================================ §10/§16 stale sweep and splitter (rows 36/37)

        [Test]
        public void StaleThreshold_Is50_70_100_300()
        {
            Assert.AreEqual(50, StaleDesignSweep.Threshold(119));
            Assert.AreEqual(70, StaleDesignSweep.Threshold(120));
            Assert.AreEqual(100, StaleDesignSweep.Threshold(200));
            Assert.AreEqual(300, StaleDesignSweep.Threshold(400));
        }

        [Test]
        public void Sweep_FlagsAnOldWarshipDesignInUse_AndDeletesAnUnusedOne()
        {
            clientState.EmpireState.TurnYear = Global.StartingYear + 60;
            ShipDesign inUse = MakeDesign("Destroyer", armed: true);
            ShipDesign unused = MakeDesign("Cruiser", armed: true);
            ShipDesign fresh = MakeDesign("Battleship", armed: true);
            fresh.Name = "AI Battleship T" + (Global.StartingYear + 30);
            ShipDesign colony = MakeDesign("Colony Ship", colonizer: true);
            AddFleet(inUse, 1, home.Position, home);
            AddFleet(fresh, 1, home.Position, home);

            HashSet<long> stale = StaleDesignSweep.Sweep(clientState.EmpireState, AiCategory.Robotoids, out List<ShipDesign> toDelete);

            CollectionAssert.AreEquivalent(new[] { inUse.Key }, stale);
            CollectionAssert.AreEquivalent(new[] { unused }, toDelete);
            Assert.IsFalse(stale.Contains(colony.Key), "colony designs are outside the swept range");
        }

        [Test]
        public void Splitter_MovesTheStaleStacksOfAMixedFleetIntoANewFleet()
        {
            clientState.EmpireState.TurnYear = Global.StartingYear + 60;
            ShipDesign old = MakeDesign("Destroyer", armed: true);
            ShipDesign fresh = MakeDesign("Battleship", armed: true);
            fresh.Name = "AI Battleship T" + (Global.StartingYear + 55);
            Fleet mixed = AddFleet(fresh, 2, home.Position, home);
            ShipToken oldShips = new ShipToken(old, 3);
            mixed.Composition.Add(oldShips.Key, oldShips);

            new TestableAi(clientState, Code(AiCategory.Robotoids)).DoMove();

            SplitMergeTask split = mixed.Waypoints.Select(w => w.Task).OfType<SplitMergeTask>().SingleOrDefault();
            Assert.IsNotNull(split, "a split order was written");
            Assert.AreEqual(0, split.OtherFleetKey);
            Assert.AreEqual(3, split.RightComposition[old.Key].Quantity);
            Assert.AreEqual(0, split.LeftComposition[old.Key].Quantity);
            Assert.AreEqual(2, split.LeftComposition[fresh.Key].Quantity);
        }

        [Test]
        public void ObsoleteFleet_IsScrappedAtAnOwnStarbasePlanet()
        {
            clientState.EmpireState.TurnYear = Global.StartingYear + 60;
            Fleet obsolete = AddFleet(MakeDesign("Destroyer", armed: true), 2, home.Position, home);

            FleetOrder order = StaleDesignSweep.ObsoleteFleetOrder(obsolete, Context(AiCategory.Robotoids, new ScriptedRandom(4)));
            Assert.IsTrue(order.Scrap);

            new TestableAi(clientState, Code(AiCategory.Robotoids)).DoMove();
            Assert.IsInstanceOf<ScrapTask>(obsolete.Waypoints.Last().Task);
        }

        [Test]
        public void ObsoleteFleet_AtAnotherOwnPlanet_IsScrappedOneTimeInFive_ElseSentHome()
        {
            Star colony = AddOwnedStar("Colony", new NovaPoint(300, 100), 5000, starbase: false);
            clientState.EmpireState.TurnYear = Global.StartingYear + 60;
            Fleet obsolete = AddFleet(MakeDesign("Destroyer", armed: true), 2, colony.Position, colony);

            Assert.IsTrue(StaleDesignSweep.ObsoleteFleetOrder(obsolete, Context(AiCategory.Robotoids, new ScriptedRandom(0))).Scrap);
            FleetOrder sent = StaleDesignSweep.ObsoleteFleetOrder(obsolete, Context(AiCategory.Robotoids, new ScriptedRandom(1)));
            Assert.AreEqual("Home", sent.Destination?.Name);
        }

        // ================================================================ wormhole diversion (row 59)

        [Test]
        public void WormholeScore_KnownIs70MinusTenPerTier_UnknownIs90Or50()
        {
            Assert.AreEqual(70, WormholeDiversion.Score(new WormholeSighting { Known = true, StabilityTier = 0 }, 100, 50));
            Assert.AreEqual(10, WormholeDiversion.Score(new WormholeSighting { Known = true, StabilityTier = 6 }, 100, 50));
            Assert.AreEqual(90, WormholeDiversion.Score(new WormholeSighting(), 50, 50), "no farther than the planet");
            Assert.AreEqual(50, WormholeDiversion.Score(new WormholeSighting(), 51, 50));
        }

        [Test]
        public void WormholeChoice_RespectsTheRanges_TheBestScore_AndTheRoll()
        {
            NovaPoint from = new NovaPoint(0, 0);
            NovaPoint planet = new NovaPoint(50, 0);
            WormholeSighting tooFar = new WormholeSighting { Name = "far", Position = new NovaPoint(101, 0) };     // beyond 2 x 50
            WormholeSighting known = new WormholeSighting { Name = "known", Position = new NovaPoint(90, 0), Known = true, StabilityTier = 1 }; // 60
            WormholeSighting unknown = new WormholeSighting { Name = "unknown", Position = new NovaPoint(80, 0) }; // 50
            List<WormholeSighting> all = new List<WormholeSighting> { tooFar, known, unknown };

            Assert.AreSame(known, WormholeDiversion.Choose(from, planet, all, new ScriptedRandom(59)));
            Assert.IsNull(WormholeDiversion.Choose(from, planet, all, new ScriptedRandom(60)), "taken only when the roll is below the score");

            WormholeSighting beyond216 = new WormholeSighting { Name = "beyond", Position = new NovaPoint(217, 0) };
            Assert.IsNull(WormholeDiversion.Choose(from, new NovaPoint(200, 0), new List<WormholeSighting> { beyond216 }, new ScriptedRandom(0)));
        }

        [Test]
        public void ARobotoidColonyFleet_DivertsToAWormholeBeforeYear120()
        {
            AddPlanet("Target", new NovaPoint(160, 100), Global.Nobody);
            Fleet colony = AddFleet(MakeDesign("Colony Ship", cargo: 25, colonizer: true), 1, home.Position, home);
            WormholeSighting wormhole = new WormholeSighting { Name = "Wormhole", Position = new NovaPoint(130, 100) };

            // An unknown wormhole no farther than the planet scores 90; every scripted roll is 0.
            new TestableAi(clientState, Code(AiCategory.Robotoids), new ScriptedRandom(), new List<WormholeSighting> { wormhole }).DoMove();

            Waypoint last = colony.Waypoints.Last();
            Assert.AreEqual("Wormhole", last.Destination);
            Assert.IsInstanceOf<NoTask>(last.Task, "a wormhole leg has no task");
            CargoTask load = colony.Waypoints.Select(w => w.Task).OfType<CargoTask>().Single();
            Assert.AreEqual(10, load.Amount.ColonistsInKilotons, "personality 0 loads 10 units");

            clientState.EmpireState.TurnYear = Global.StartingYear + WormholeDiversion.LastYear;
            colony.Waypoints.RemoveRange(1, colony.Waypoints.Count - 1);
            new TestableAi(clientState, Code(AiCategory.Robotoids), new ScriptedRandom(), new List<WormholeSighting> { wormhole }).DoMove();
            Assert.AreEqual("Target", colony.Waypoints.Last().Destination, "no diversion from year 120");
        }

        // ================================================================ personalities 1-3 colony and invasion (row 58)

        [Test]
        public void Automitrons_AColonyFleetBoundForAForeignHabitablePlanet_UnloadsItsColonistsThere()
        {
            StarIntel theirs = AddPlanet("Theirs", new NovaPoint(300, 100), Them, colonists: 5000, starbase: true);
            Fleet colony = AddFleet(MakeDesign("Colony Ship", cargo: 200, colonizer: true), 1, new NovaPoint(200, 100), null);
            colony.Cargo.ColonistsInKilotons = 150;
            colony.Waypoints.Add(new Waypoint { Position = theirs.Position, Destination = theirs.Name, Task = new ColoniseTask() });

            new TestableAi(clientState, Code(AiCategory.Automitrons)).DoMove();

            Waypoint last = colony.Waypoints.Last();
            Assert.AreEqual("Theirs", last.Destination);
            CargoTask unload = last.Task as CargoTask;
            Assert.IsNotNull(unload, "a Transport order (personality 2 ignores the starbase)");
            Assert.AreEqual(CargoMode.Unload, unload.Mode);
            Assert.AreEqual(150, unload.Amount.ColonistsInKilotons);
            Assert.AreEqual(2, colony.Waypoints.Count, "no return leg for personality 2");
        }

        [Test]
        public void Turindrones_UnderWayInvasion_IsBlockedByAStarbase_AndOtherwiseGetsAReturnLeg()
        {
            StarIntel guarded = AddPlanet("Guarded", new NovaPoint(300, 100), Them, colonists: 5000, starbase: true);
            Fleet colony = AddFleet(MakeDesign("Colony Ship", cargo: 200, colonizer: true), 1, new NovaPoint(200, 100), null);
            colony.Cargo.ColonistsInKilotons = 25;
            colony.Waypoints.Add(new Waypoint { Position = guarded.Position, Destination = guarded.Name, Task = new ColoniseTask() });

            FleetOrder blocked = ColonyFleetRules.InvasionUnderWay(colony, Context(AiCategory.Turindrones, new ScriptedRandom()));
            Assert.IsFalse(blocked.Moves, "the route is cut");
            Assert.IsTrue(blocked.ClearTask);

            StarIntel open = AddPlanet("Open", new NovaPoint(300, 200), Them, colonists: 5000);
            colony.Waypoints[1] = new Waypoint { Position = open.Position, Destination = open.Name };
            FleetOrder landing = ColonyFleetRules.InvasionUnderWay(colony, Context(AiCategory.Turindrones, new ScriptedRandom()));
            Assert.AreEqual("Open", landing.Destination.Name);
            Assert.AreEqual(25, ((CargoTask)landing.DestinationTask).Amount.ColonistsInKilotons);
            Assert.AreEqual("Home", landing.ReturnLeg?.Name);
        }

        [Test]
        public void Turindrones_WithNoColonizationTarget_LandOnTheNearestForeignPlanetWithoutAStarbase_AtWarp6()
        {
            AddPlanet("Guarded", new NovaPoint(150, 100), Them, colonists: 5000, starbase: true);
            AddPlanet("Open", new NovaPoint(250, 100), Them, colonists: 5000);
            Fleet colony = AddFleet(MakeDesign("Colony Ship", cargo: 200, colonizer: true), 1, home.Position, home);

            new TestableAi(clientState, Code(AiCategory.Turindrones)).DoMove();

            CargoTask load = colony.Waypoints.Select(w => w.Task).OfType<CargoTask>().First();
            Assert.AreEqual(CargoMode.Load, load.Mode);
            Assert.AreEqual(25, load.Amount.ColonistsInKilotons, "personality 1 loads 25 units");

            Waypoint last = colony.Waypoints.Last();
            Assert.AreEqual("Open", last.Destination);
            Assert.AreEqual(ColonyFleetRules.TransportWarp, last.WarpFactor);
            Assert.AreEqual(CargoMode.Unload, ((CargoTask)last.Task).Mode);
        }

        [Test]
        public void Automitrons_AnEmptyColonyFleetAwayFromHome_FliesHomeWithAGoodEngine_ElseIsScrapped()
        {
            StarIntel theirs = AddPlanet("Theirs", new NovaPoint(300, 100), Them, colonists: 5000);
            Star theirStar = new Star { Name = "Theirs", Position = theirs.Position, Owner = Them };
            Fleet goodEngine = AddFleet(MakeDesign("Colony Ship", cargo: 25, colonizer: true, engineName: "Fuel Mizer"), 1, theirs.Position, theirStar);
            Fleet poorEngine = AddFleet(MakeDesign("Colony Ship", cargo: 25, colonizer: true, engineName: "Quick Jump 5"), 1, theirs.Position, theirStar);
            ColonizationTargetSelector selector = new ColonizationTargetSelector(clientState, AiCategory.Automitrons);
            AiFleetContext context = Context(AiCategory.Automitrons, new ScriptedRandom());

            Assert.AreEqual("Home", ColonyFleetRules.IdleColonyFleet(goodEngine, context, selector, null).Destination?.Name);
            Assert.IsTrue(ColonyFleetRules.IdleColonyFleet(poorEngine, context, selector, null).Scrap);

            Fleet waiting = AddFleet(MakeDesign("Colony Ship", cargo: 25, colonizer: true), 1, home.Position, home);
            home.Colonists = 19900; // under 200 units at an own starbase planet: waits
            Assert.IsNull(ColonyFleetRules.IdleColonyFleet(waiting, context, selector, null));
        }

        [Test]
        public void Rototills_LoadTwentyFiveUnits_AndTurindronesNeedLongHump6ToFlyHome()
        {
            AddPlanet("Target", new NovaPoint(130, 100), Global.Nobody);
            Fleet colony = AddFleet(MakeDesign("Colony Ship", cargo: 1000, colonizer: true), 1, home.Position, home);

            new TestableAi(clientState, Code(AiCategory.Rototills)).DoMove();

            CargoTask load = colony.Waypoints.Select(w => w.Task).OfType<CargoTask>().Single();
            Assert.AreEqual(25, load.Amount.ColonistsInKilotons);
            Assert.IsInstanceOf<ColoniseTask>(colony.Waypoints.Last().Task);

            SetUp();
            StarIntel theirs = AddPlanet("Theirs", new NovaPoint(300, 100), Them, colonists: 5000);
            Star theirStar = new Star { Name = "Theirs", Position = theirs.Position, Owner = Them };
            Fleet mizer = AddFleet(MakeDesign("Colony Ship", cargo: 25, colonizer: true, engineName: "Fuel Mizer"), 1, theirs.Position, theirStar);
            ColonizationTargetSelector selector = new ColonizationTargetSelector(clientState, AiCategory.Turindrones);
            Assert.IsTrue(ColonyFleetRules.IdleColonyFleet(mizer, Context(AiCategory.Turindrones, new ScriptedRandom()), selector, null).Scrap, "personality 1 needs engine index 3");
        }
    }
}
