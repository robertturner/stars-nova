namespace Nova.Tests.UnitTests
{
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
    /// The freighter router's urgent-supply score, personality 0's 6,500 value and the category
    /// 0/1 colonist loads at the hub (behavior-specs-10/ai-opponent-behavior.md §5 steps 4 and 6;
    /// population units of §13).
    /// </summary>
    [TestFixture]
    public class AiFreighterSpec10Test
    {
        private ClientData clientState;
        private uint nextFleetId = 1;

        [SetUp]
        public void SetUp()
        {
            clientState = new ClientData();
            clientState.EmpireState.Id = 1;
            clientState.EmpireState.Race = new Race(); // bands 20-80, centre 50
        }

        private Star AddOwnedStar(string name, NovaPoint position, int ironium, int boranium, int germanium, bool starbase = false, int colonists = 0, int environment = 50)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = clientState.EmpireState.Id;
            star.Position = position;
            star.ResourcesOnHand = new Resources(ironium, boranium, germanium, 0);
            star.Colonists = colonists;
            star.Gravity = environment;
            star.Temperature = environment;
            star.Radiation = environment;
            if (starbase)
            {
                star.Starbase = new Fleet("Starbase " + name, clientState.EmpireState.Id, 900 + nextFleetId++, position);
            }

            clientState.EmpireState.OwnedStars.Add(star);
            return star;
        }

        private StarIntel AddReport(string name, NovaPoint position, ushort owner, int colonists)
        {
            StarIntel report = new StarIntel();
            report.Name = name;
            report.Position = position;
            report.Owner = owner;
            report.Colonists = colonists;
            clientState.EmpireState.StarReports.Add(name, report);
            return report;
        }

        private ShipDesign MakeDesign(int cargoCapacity)
        {
            ShipDesign design = new ShipDesign(clientState.EmpireState.GetNextDesignKey());
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.BaseCargo = cargoCapacity;
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();
            return design;
        }

        private Fleet AddFreighterAt(Star star, int capacity = 1000)
        {
            Fleet fleet = new Fleet("Freighter", clientState.EmpireState.Id, nextFleetId++, star.Position);
            ShipToken token = new ShipToken(MakeDesign(capacity), 1);
            fleet.Composition.Add(token.Key, token);
            fleet.Waypoints.Add(new Waypoint { Position = star.Position, Destination = star.Name });
            fleet.InOrbit = star;
            clientState.EmpireState.OwnedFleets.Add(fleet);
            return fleet;
        }

        private FreighterRun Route(int category, Fleet freighter)
        {
            return new FreighterRoutingSelector(clientState, category, 50).SelectRun(freighter);
        }

        // ============================================================ urgent supply (25,000)

        [Test]
        public void UrgentSupply_ScoresAFlat25000_ForCategoriesOneToThree_AtTheHub()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true);
            AddOwnedStar("Rich", new NovaPoint(25, 0), 300, 300, 300);            // 9,000 / T 1
            AddOwnedStar("Hostile", new NovaPoint(200, 0), 0, 0, 0, environment: 5); // negative habitability, no stock
            Fleet freighter = AddFreighterAt(hub);

            Assert.AreEqual("Hostile", Route(AiCategory.Automitrons, freighter).Destination.Name, "25,000 is not divided by T (8)");
            Assert.AreEqual("Hostile", Route(AiCategory.Turindrones, freighter).Destination.Name);
            Assert.AreEqual("Hostile", Route(AiCategory.Rototills, freighter).Destination.Name);
            Assert.AreEqual("Rich", Route(AiCategory.Robotoids, freighter).Destination.Name, "category 0 sets no urgent-supply flag");
        }

        [Test]
        public void UrgentSupply_OnlyWhenTheFreighterIsAtItsHub()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true);
            Star elsewhere = AddOwnedStar("Elsewhere", new NovaPoint(10, 0), 0, 0, 0);
            AddOwnedStar("Rich", new NovaPoint(25, 0), 300, 300, 300);
            AddOwnedStar("Hostile", new NovaPoint(30, 0), 0, 0, 0, environment: 5);
            Fleet freighter = AddFreighterAt(elsewhere);

            Assert.AreEqual("Rich", Route(AiCategory.Automitrons, freighter).Destination.Name);
        }

        [Test]
        public void UrgentSupplyFlag_UsesTheIntegerHabitability()
        {
            Race race = clientState.EmpireState.Race;
            Star star = new Star { Gravity = 50, Temperature = 50, Radiation = 19 };
            Assert.IsTrue(FreighterRoutingSelector.HasUrgentSupplyFlag(AiCategory.Automitrons, star, race), "one click outside the band");
            star.Radiation = 20;
            Assert.IsFalse(FreighterRoutingSelector.HasUrgentSupplyFlag(AiCategory.Automitrons, star, race), "on the band edge");
            star.Radiation = 19;
            Assert.IsFalse(FreighterRoutingSelector.HasUrgentSupplyFlag(AiCategory.Cybertrons, star, race), "categories 1-3 only");
        }

        // ============================================================ personality 0's 6,500

        [Test]
        public void ReportedFigure_IsColonistsOver400_ClampedToOneThrough4090()
        {
            Assert.AreEqual(0, FreighterRoutingSelector.ReportedPopulationFigure(new StarIntel { Owner = Global.Nobody, Colonists = 0 }));
            Assert.AreEqual(0, FreighterRoutingSelector.ReportedPopulationFigure(new StarIntel { Owner = 2, Colonists = 0 }));
            Assert.AreEqual(1, FreighterRoutingSelector.ReportedPopulationFigure(new StarIntel { Owner = 2, Colonists = 100 }));
            Assert.AreEqual(14, FreighterRoutingSelector.ReportedPopulationFigure(new StarIntel { Owner = 2, Colonists = 5600 }));
            Assert.AreEqual(4090, FreighterRoutingSelector.ReportedPopulationFigure(new StarIntel { Owner = 2, Colonists = 3000000 }));
        }

        [Test]
        public void ForeignPlanetValue_FollowsTheTwoPopulationRules()
        {
            Assert.AreEqual(6500, FreighterRoutingSelector.ForeignPlanetValue(AiCategory.Robotoids, true, 301, 14));
            Assert.AreEqual(0, FreighterRoutingSelector.ForeignPlanetValue(AiCategory.Robotoids, true, 300, 14), "hub must exceed 300 units");
            Assert.AreEqual(0, FreighterRoutingSelector.ForeignPlanetValue(AiCategory.Robotoids, true, 1100, 15));
            Assert.AreEqual(6500, FreighterRoutingSelector.ForeignPlanetValue(AiCategory.Robotoids, true, 1101, 61));
            Assert.AreEqual(0, FreighterRoutingSelector.ForeignPlanetValue(AiCategory.Robotoids, true, 1101, 62));
            Assert.AreEqual(0, FreighterRoutingSelector.ForeignPlanetValue(AiCategory.Robotoids, false, 5000, 0), "only at the hub");
            Assert.AreEqual(0, FreighterRoutingSelector.ForeignPlanetValue(AiCategory.Automitrons, true, 5000, 0), "personality 0 only");
        }

        [Test]
        public void Personality0_SendsColonistsToAWeakForeignPlanet()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true, colonists: 40000); // 400 units
            AddOwnedStar("Source", new NovaPoint(25, 0), 20, 20, 20);                  // 600 / T 1
            AddReport("Weak", new NovaPoint(50, 0), 2, 5000);                          // figure 12: 6,500 / T 2
            AddReport("Strong", new NovaPoint(25, 0), 2, 20000);                       // figure 50: not a candidate at 400 units
            Fleet freighter = AddFreighterAt(hub);

            FreighterRun run = Route(AiCategory.Robotoids, freighter);

            Assert.AreEqual("Weak", run.Destination.Name);
            Assert.IsInstanceOf<StarIntel>(run.Destination);
            Assert.AreEqual(100, run.ColonistsToLoadAtHubKt, "another player's planet: 100 units (hub not over 900)");
            Assert.IsTrue(run.UnloadColonistsAtDestination);
            Assert.AreEqual(0, run.Amount.Mass, "no minerals are taken from a planet the AI does not own");

            Assert.AreEqual("Source", Route(AiCategory.Automitrons, freighter).Destination.Name, "other categories ignore it");
        }

        [Test]
        public void Personality0_UnownedPlanetsHaveFigureZero_ButGetNoColonists()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true, colonists: 40000);
            AddReport("Empty", new NovaPoint(25, 0), Global.Nobody, 0);
            Fleet freighter = AddFreighterAt(hub);

            FreighterRun run = Route(AiCategory.Robotoids, freighter);

            Assert.AreEqual("Empty", run.Destination.Name);
            Assert.AreEqual(0, run.ColonistsToLoadAtHubKt, "§5 step 6 gives no load for an unowned destination");
            Assert.IsFalse(run.UnloadColonistsAtDestination);
        }

        // ============================================================ colonist loads at the hub

        [Test]
        public void HubColonistLoad_Category0_OwnDestination()
        {
            Assert.AreEqual(250, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 2500, true, false, 100), "p = 10");
            Assert.AreEqual(128, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 1600, true, false, 100), "p = 8");
            Assert.AreEqual(60, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 1200, true, false, 100), "p = 5");
            Assert.AreEqual(32, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 800, true, false, 100), "p = (800 - 400) / 100 = 4");
            Assert.AreEqual(5, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 501, true, false, 100), "p = 1");
            Assert.AreEqual(0, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 500, true, false, 100), "hub under 501");
            Assert.AreEqual(0, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 5000, true, false, 1000), "destination over 999");
            Assert.AreEqual(0, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 600, true, false, 400), "hub exactly 1.5 x the destination");
            Assert.AreEqual(12, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 601, true, false, 400), "p = 2");
        }

        [Test]
        public void HubColonistLoad_Category0_ForeignAndUnownedDestinations()
        {
            Assert.AreEqual(100, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 900, false, true, 5000));
            Assert.AreEqual(300, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 901, false, true, 5000));
            Assert.AreEqual(0, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Robotoids, 5000, false, false, 0));
        }

        [Test]
        public void HubColonistLoad_Category1_AndOthers()
        {
            Assert.AreEqual(1000, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Turindrones, 1201, true, false, 1200));
            Assert.AreEqual(1000, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Turindrones, 1201, false, true, 0));
            Assert.AreEqual(0, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Turindrones, 1200, true, false, 0), "hub must exceed 1,200");
            Assert.AreEqual(0, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Turindrones, 1300, true, false, 1300), "destination must be smaller");
            Assert.AreEqual(0, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Turindrones, 1300, false, false, 0), "unowned");
            Assert.AreEqual(0, FreighterRoutingSelector.HubColonistLoadUnits(AiCategory.Automitrons, 5000, true, false, 0), "categories 0 and 1 only");
        }

        [Test]
        public void Category0_CollectionRunFromTheHub_CarriesColonists_CappedByTheFreeHold()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true, colonists: 250000); // 2,500 units
            AddOwnedStar("Source", new NovaPoint(25, 0), 300, 300, 300, colonists: 10000);                        // 100 units
            Fleet freighter = AddFreighterAt(hub, capacity: 200);

            FreighterRun run = Route(AiCategory.Robotoids, freighter);

            Assert.AreEqual("Source", run.Destination.Name);
            Assert.AreEqual(200, run.ColonistsToLoadAtHubKt, "250 units, capped by the 200 kT hold");
            Assert.IsTrue(run.UnloadColonistsAtDestination);
        }

        [Test]
        public void DeliverCargo_LoadsColonistsAtTheHub_ThenUnloadsThemBeforeLoadingMinerals()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true, colonists: 250000);
            AddOwnedStar("Source", new NovaPoint(25, 0), 300, 300, 300, colonists: 10000);
            Fleet freighter = AddFreighterAt(hub);

            FreighterRun run = Route(AiCategory.Robotoids, freighter);
            new DefaultFleetAI(freighter, clientState, null).DeliverCargo(run);

            List<Waypoint> waypoints = freighter.Waypoints.Skip(1).ToList();
            Assert.AreEqual(3, waypoints.Count);

            CargoTask atHub = (CargoTask)waypoints[0].Task;
            Assert.AreEqual("Hub", waypoints[0].Destination);
            Assert.AreEqual(CargoMode.Load, atHub.Mode);
            Assert.AreEqual(250, atHub.Amount.ColonistsInKilotons);

            CargoTask unload = (CargoTask)waypoints[1].Task;
            Assert.AreEqual("Source", waypoints[1].Destination);
            Assert.AreEqual(CargoMode.Unload, unload.Mode);
            Assert.AreEqual(250, unload.Amount.ColonistsInKilotons);

            CargoTask minerals = (CargoTask)waypoints[2].Task;
            Assert.AreEqual("Source", waypoints[2].Destination);
            Assert.AreEqual(CargoMode.Load, minerals.Mode);
            Assert.AreEqual(300, minerals.Amount.Ironium);
            Assert.AreEqual(0, minerals.Amount.ColonistsInKilotons);
        }
    }
}
