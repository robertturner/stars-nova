namespace Nova.Tests.Simulation
{
    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    using NUnit.Framework;

    /// <summary>
    /// Regression tests for bugs the simulation harness found and that were small enough to fix
    /// in place (see docs/SIMULATION.md "Bugs found").
    /// </summary>
    [TestFixture]
    [Category("Simulation")]
    public class SimulationFoundBugsTest
    {
        /// <summary>
        /// Nova.Sim seed 7, Tiny, 3 AIs, turn 16: a Turindrones colony ship unloading onto a
        /// Robotoids planet invaded and won it, but the planet kept ThisRace = Robotoids (it grew
        /// and produced as the defender's race) until a reload relinked it. A captured planet's
        /// population is the invader's.
        /// </summary>
        [Test]
        public void AWonInvasion_MakesThePlanetTheInvadersRace()
        {
            EmpireData attacker = new EmpireData { Id = 1 };
            attacker.Race = new Race { Name = "Attackers" };
            attacker.Race.Traits.SetPrimary("WM");
            attacker.ResearchLevels[TechLevel.ResearchField.Energy] = 7;

            EmpireData defender = new EmpireData { Id = 2 };
            defender.Race = new Race { Name = "Defenders" };
            defender.Race.Traits.SetPrimary("JOAT");
            attacker.EmpireReports.Add(defender.Id, new EmpireIntel(defender) { Relation = PlayerRelation.Enemy });

            Star star = new Star { Name = "Target", Owner = defender.Id, Colonists = 1000, ThisRace = defender.Race };
            defender.OwnedStars.Add(star);

            Fleet fleet = new Fleet(1) { Owner = attacker.Id };
            fleet.InOrbit = star;
            fleet.Cargo.ColonistsInKilotons = 500;

            new InvadeTask().Invade(fleet, star, attacker, defender, 500);

            Assert.AreEqual(attacker.Id, star.Owner, "50,000 troops beat 1,000 defenders");
            Assert.AreSame(attacker.Race, star.ThisRace);
            Assert.AreEqual(7, star.EnergyTechLevel);
            Assert.IsTrue(attacker.OwnedStars.Contains(star));
            Assert.IsFalse(defender.OwnedStars.Contains(star));
        }

        /// <summary>
        /// SIM-4 (Nova.Sim seed 7, Tiny, 3 AIs, turn 18; seed 1, Small, 4 AIs, from turn 48):
        /// planet stockpiles reached -1 kT. A partial purchase paid Resources x fraction rounded
        /// UP per component; with Germanium cost 3 and 1 kT on hand the fraction is 1 - 2/3, and
        /// 3 x 0.33333333333333337 rounds up to 2. production-queue.md 10k: the purchase pays
        /// the rounded-down amounts.
        /// </summary>
        [Test]
        public void APartialFactoryPurchase_NeverSpendsMoreThanIsOnHand()
        {
            Race race = new Race { FactoryBuildCost = 10 };
            race.Traits.SetPrimary("JOAT");
            race.Traits.Add("CF");
            Assert.AreEqual(3, race.GetFactoryResources().Germanium, "CF factories cost 3 kT Germanium");

            Star star = new Star { Name = "Short", ResourcesOnHand = new Resources(0, 0, 1, 100) };
            bool built = new FactoryProductionUnit(race).Construct(star);

            Assert.IsFalse(built);
            Assert.AreEqual(0, star.ResourcesOnHand.Germanium, "the one kT on hand is spent, not two");
            Assert.GreaterOrEqual(star.ResourcesOnHand.Energy, 0);
            Assert.AreEqual(97, star.ResourcesOnHand.Energy, "a third of 10 resources, rounded down");
        }

        [Test]
        public void PartialPayment_RoundsDown_AndIsCappedByWhatIsOnHand()
        {
            Resources cost = new Resources(10, 7, 3, 70);
            Resources paid = Resources.PartialPayment(cost, 1 - (2.0 / 3.0), new Resources(100, 100, 1, 100));
            Assert.AreEqual(3, paid.Ironium, "10 x 1/3 = 3.33 -> 3");
            Assert.AreEqual(2, paid.Boranium, "7 x 1/3 = 2.33 -> 2");
            Assert.AreEqual(1, paid.Germanium, "3 x 0.33333333333333337 -> 1, not 2");
            Assert.AreEqual(23, paid.Energy);

            Resources capped = Resources.PartialPayment(cost, 0.5, new Resources(2, 0, 0, 1));
            Assert.AreEqual(new Resources(2, 0, 0, 1), capped);
        }

        /// <summary>
        /// SIM-6 (Nova.Sim seed 1, Small, 4 AIs, from turn 82): a fleet that leaves a star and
        /// stops mid-flight gets its current-position waypoint relabelled "Space at (x, y)", but
        /// it kept TargetKind Planet from the star it left, so a Planet waypoint named no star.
        /// </summary>
        [Test]
        public void AFleetStoppedInDeepSpace_HasADeepSpaceCurrentPositionWaypoint()
        {
            Nova.Server.ServerData state = new Nova.Server.SimpleServerData();
            EmpireData empire = new SimpleEmpireData { Id = 1 };
            empire.AvailableComponents = new Nova.Common.Components.RaceComponents();
            state.AllEmpires.Add(empire.Id, empire);
            Star home = new Star { Name = "Home", Position = new NovaPoint(0, 0) };
            state.AllStars.Add(home.Key, home);

            Nova.Common.Components.Engine engine = new Nova.Common.Components.Engine();
            engine.FuelConsumption = new[] { 0, 0, 0, 0, 0, 100, 110, 150, 200, 300 };
            Nova.Common.Components.Component engineComponent = new Nova.Common.Components.Component { Name = "Test Engine" };
            engineComponent.Properties.Add("Engine", engine);
            Nova.Common.Components.Hull hull = new Nova.Common.Components.Hull { Modules = new System.Collections.Generic.List<Nova.Common.Components.HullModule>(), FuelCapacity = 1000 };
            hull.Modules.Add(new Nova.Common.Components.HullModule { AllocatedComponent = engineComponent, ComponentCount = 1 });
            Nova.Common.Components.Component blueprint = new Nova.Common.Components.Component { Mass = 100 };
            blueprint.Properties.Add("Hull", hull);
            Nova.Common.Components.ShipDesign design = new Nova.Common.Components.ShipDesign(1) { Blueprint = blueprint, Icon = new ShipIcon("hull0000.png", null) };
            design.Update();

            Fleet fleet = new Fleet(10) { Owner = 1, Name = "Traveller", Position = new NovaPoint(0, 0), InOrbit = home, FuelAvailable = 1000 };
            ShipToken token = new ShipToken(design, 1);
            fleet.Composition.Add(token.Key, token);
            Waypoint here = new Waypoint { WarpFactor = 0 };
            here.MakeFixedPoint(home);
            Assert.AreEqual(WaypointTargetKind.Planet, here.TargetKind);
            fleet.Waypoints.Add(here);
            fleet.Waypoints.Add(new Waypoint { Position = new NovaPoint(500, 0), WarpFactor = 5, Destination = "Space at 500,0", Task = new NoTask() });
            empire.AddOrUpdateFleet(fleet);

            new Nova.Server.SimpleTurnGenerator(state).Generate();

            Waypoint current = fleet.Waypoints[0];
            Assert.AreEqual(25, current.Position.X, "warp 5 covers 25 ly");
            StringAssert.StartsWith("Space at", current.Destination);
            Assert.AreEqual(WaypointTargetKind.DeepSpace, current.TargetKind);
        }

        /// <summary>A race with no icon saves an empty RaceIcon element; loading it used to throw
        /// (caught, but reported as an error on every intel load of every AI-template race).</summary>
        [Test]
        public void ARaceIconWithNoSource_RoundTripsWithoutAnError()
        {
            System.Xml.XmlDocument document = new System.Xml.XmlDocument();
            document.AppendChild(new RaceIcon().ToXml(document));

            // Through text, as a saved file is: the empty value becomes <RaceIcon />.
            System.Xml.XmlDocument reread = new System.Xml.XmlDocument();
            reread.LoadXml(document.OuterXml);
            System.Xml.XmlElement saved = reread.DocumentElement;

            string reported = null;
            System.Action<string> previous = PlatformHooks.ShowError;
            PlatformHooks.ShowError = message => reported = message;
            try
            {
                RaceIcon loaded = new RaceIcon(saved);
                Assert.IsTrue(string.IsNullOrEmpty(loaded.Source));
            }
            finally
            {
                PlatformHooks.ShowError = previous;
            }

            Assert.IsNull(reported, reported);
        }
    }
}
