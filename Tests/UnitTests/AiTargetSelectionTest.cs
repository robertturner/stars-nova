#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using Nova.Ai;
    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    using NUnit.Framework;

    /// <summary>
    /// The AI's colonization-target search and freighter router
    /// (docs/behavior-specs-10/ai-opponent-behavior.md §2 and §5). spec-10 replaces the scored,
    /// randomly accepted colonization pick (with its "too eager" roll and exploratory fallback)
    /// by a deterministic nearest-unclaimed search, and the 700-unit shortfall/surplus freighter
    /// selector by the hub-collecting router `FUN_1090_19c8`; the old tests asserted the
    /// retracted behaviour and were rewritten with the code.
    /// </summary>
    [TestFixture]
    public class AiTargetSelectionTest
    {
        private ClientData clientState;
        private uint nextFleetId = 1;

        [SetUp]
        public void SetUp()
        {
            clientState = new ClientData();
            clientState.EmpireState.Id = 1;
            clientState.EmpireState.Race = new Race();
        }

        private int Ideal
        {
            get { return clientState.EmpireState.Race.GravityTolerance.OptimumLevel; }
        }

        private StarIntel AddStarReport(string name, NovaPoint position, ushort owner, int environment = -1)
        {
            int value = environment < 0 ? Ideal : environment;
            StarIntel report = new StarIntel();
            report.Name = name;
            report.Position = position;
            report.Owner = owner;
            report.Gravity = value;
            report.Temperature = value;
            report.Radiation = value;
            clientState.EmpireState.StarReports.Add(name, report);
            return report;
        }

        private Star AddOwnedStar(string name, NovaPoint position, int ironium, int boranium, int germanium, bool starbase = false)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = clientState.EmpireState.Id;
            star.Position = position;
            star.ResourcesOnHand = new Resources(ironium, boranium, germanium, 0);
            if (starbase)
            {
                star.Starbase = new Fleet("Starbase " + name, clientState.EmpireState.Id, 900 + nextFleetId++, position);
            }

            clientState.EmpireState.OwnedStars.Add(star);
            return star;
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

        private Fleet AddFleet(string name, NovaPoint position, ShipDesign design = null)
        {
            Fleet fleet = new Fleet(name, clientState.EmpireState.Id, nextFleetId++, position);
            if (design != null)
            {
                ShipToken token = new ShipToken(design, 1);
                fleet.Composition.Add(token.Key, token);
            }

            fleet.Waypoints.Add(new Waypoint { Position = position, Destination = name });
            clientState.EmpireState.OwnedFleets.Add(fleet);
            return fleet;
        }

        private static void GiveNextWaypoint(Fleet fleet, StarIntel target, IWaypointTask task)
        {
            fleet.Waypoints.Add(new Waypoint { Position = target.Position, Destination = target.Name, Task = task });
        }

        // ================================================================ colonization (§2)

        [Test]
        public void Colonization_PicksTheNearestUnownedPlanet_EveryTime()
        {
            AddStarReport("Far", new NovaPoint(60, 0), Global.Nobody);
            AddStarReport("Near", new NovaPoint(20, 0), Global.Nobody);
            AddStarReport("Middle", new NovaPoint(40, 0), Global.Nobody);
            Fleet fleet = AddFleet("Colonizer", new NovaPoint(0, 0));

            // No Random at all: the search is deterministic (no roll-under-score, no "too
            // eager" rejection, no exploratory pick).
            for (int trial = 0; trial < 10; trial++)
            {
                var selector = new ColonizationTargetSelector(clientState, AiCategory.Automitrons);
                Assert.AreEqual("Near", selector.SelectTarget(fleet).Name);
            }
        }

        [Test]
        public void Colonization_NeverReturnsAnOwnedPlanet()
        {
            AddStarReport("Mine", new NovaPoint(5, 0), clientState.EmpireState.Id);
            AddStarReport("Theirs", new NovaPoint(10, 0), 2);
            AddStarReport("Free", new NovaPoint(50, 0), Global.Nobody);
            Fleet fleet = AddFleet("Colonizer", new NovaPoint(0, 0));

            var selector = new ColonizationTargetSelector(clientState, AiCategory.Automitrons);
            Assert.AreEqual("Free", selector.SelectTarget(fleet).Name);
        }

        [Test]
        public void Colonization_ReturnsNull_WhenNothingIsEligible()
        {
            AddStarReport("Home", new NovaPoint(0, 0), clientState.EmpireState.Id);
            Fleet fleet = AddFleet("Colonizer", new NovaPoint(0, 0));

            Assert.IsNull(new ColonizationTargetSelector(clientState, AiCategory.Automitrons).SelectTarget(fleet));
        }

        [Test]
        public void Colonization_HabitabilityFilter_LeavesOutNegativeTerraformedHabitability_ExceptForCategoriesZeroAndFive()
        {
            // Every axis at 0: even after the full 15-click terraform allowance it is outside the
            // default 20-80 band, so its terraformed habitability is negative.
            AddStarReport("Hostile", new NovaPoint(10, 0), Global.Nobody, environment: 0);
            AddStarReport("Pleasant", new NovaPoint(80, 0), Global.Nobody);
            Fleet fleet = AddFleet("Colonizer", new NovaPoint(0, 0));

            Assert.AreEqual("Pleasant", new ColonizationTargetSelector(clientState, AiCategory.Automitrons).SelectTarget(fleet).Name);
            Assert.AreEqual("Pleasant", new ColonizationTargetSelector(clientState, AiCategory.Cybertrons).SelectTarget(fleet).Name);
            Assert.AreEqual("Hostile", new ColonizationTargetSelector(clientState, AiCategory.Robotoids).SelectTarget(fleet).Name, "category 0 has no habitability filter");
            Assert.AreEqual("Hostile", new ColonizationTargetSelector(clientState, AiCategory.Macinti).SelectTarget(fleet).Name, "category 5 has no habitability filter");
        }

        [Test]
        public void Colonization_HabitabilityFilter_UsesTheTerraformedValue_AndAcceptsZero()
        {
            // Every axis at 5: uninhabitable now, but 15 clicks of terraforming bring each axis to
            // the band edge (20), a habitability of exactly 0, which is not negative.
            StarIntel edge = AddStarReport("Edge", new NovaPoint(10, 0), Global.Nobody, environment: 5);
            StarIntel beyond = AddStarReport("Beyond", new NovaPoint(20, 0), Global.Nobody, environment: 4);
            Race race = clientState.EmpireState.Race;

            Assume.That(race.HabitalValue(edge), Is.LessThan(0), "test setup: uninhabitable as it stands");
            Assert.IsTrue(ColonizationTargetSelector.PassesHabitabilityFilter(race, edge));
            Assert.IsFalse(ColonizationTargetSelector.PassesHabitabilityFilter(race, beyond));
        }

        [Test]
        public void Colonization_CategoriesZeroAndFive_OnlyColonizeWaypointsClaim()
        {
            StarIntel near = AddStarReport("Near", new NovaPoint(10, 0), Global.Nobody);
            AddStarReport("Far", new NovaPoint(50, 0), Global.Nobody);
            Fleet fleet = AddFleet("Colonizer", new NovaPoint(0, 0));

            Fleet scout = AddFleet("Scout", new NovaPoint(0, 0));
            GiveNextWaypoint(scout, near, new NoTask());
            Assert.AreEqual("Near", new ColonizationTargetSelector(clientState, AiCategory.Robotoids).SelectTarget(fleet).Name, "a non-Colonize waypoint does not claim");

            Fleet other = AddFleet("Other colonizer", new NovaPoint(0, 0));
            GiveNextWaypoint(other, near, new ColoniseTask());
            Assert.AreEqual("Far", new ColonizationTargetSelector(clientState, AiCategory.Robotoids).SelectTarget(fleet).Name);
        }

        [Test]
        public void Colonization_CategoriesZeroAndFive_RebuildClaimsOnEverySearch()
        {
            StarIntel near = AddStarReport("Near", new NovaPoint(10, 0), Global.Nobody);
            AddStarReport("Far", new NovaPoint(50, 0), Global.Nobody);
            Fleet fleet = AddFleet("Colonizer", new NovaPoint(0, 0));
            var selector = new ColonizationTargetSelector(clientState, AiCategory.Macinti);

            Assert.AreEqual("Near", selector.SelectTarget(fleet).Name);

            Fleet other = AddFleet("Other colonizer", new NovaPoint(0, 0));
            GiveNextWaypoint(other, near, new ColoniseTask());
            Assert.AreEqual("Far", selector.SelectTarget(fleet).Name, "an order written since the last search is seen");
        }

        [Test]
        public void Colonization_OtherCategories_AnyNextWaypointClaims_BuiltOnceAndThenMarked()
        {
            StarIntel near = AddStarReport("Near", new NovaPoint(10, 0), Global.Nobody);
            StarIntel middle = AddStarReport("Middle", new NovaPoint(30, 0), Global.Nobody);
            AddStarReport("Far", new NovaPoint(50, 0), Global.Nobody);
            Fleet fleet = AddFleet("Colonizer", new NovaPoint(0, 0));

            Fleet scout = AddFleet("Scout", new NovaPoint(0, 0));
            GiveNextWaypoint(scout, near, new NoTask());

            var selector = new ColonizationTargetSelector(clientState, AiCategory.Automitrons);
            Assert.AreEqual("Middle", selector.SelectTarget(fleet).Name, "any task claims for this category");

            // Built once: a later order is not seen ...
            Fleet late = AddFleet("Late", new NovaPoint(0, 0));
            GiveNextWaypoint(late, middle, new ColoniseTask());
            Assert.AreEqual("Middle", selector.SelectTarget(fleet).Name);

            // ... the caller marks what it orders instead.
            selector.MarkClaimed(middle);
            Assert.AreEqual("Far", selector.SelectTarget(fleet).Name);
        }

        // ================================================================ freighters (§5)

        [Test]
        public void Freighter_ScarcityLevel_FollowsTheHubRule()
        {
            ResourceType scarce;

            Assert.AreEqual(2, FreighterRoutingSelector.ScarcityLevel(new Resources(1000, 1000, 1000, 0), out scarce), "Ironium scarcest: the comparison is effectively infinite");
            Assert.AreEqual(ResourceType.Ironium, scarce);

            Assert.AreEqual(0, FreighterRoutingSelector.ScarcityLevel(new Resources(1000, 900, 900, 0), out scarce));
            Assert.AreEqual(ResourceType.Boranium, scarce, "the first strictly smallest");

            Assert.AreEqual(1, FreighterRoutingSelector.ScarcityLevel(new Resources(1000, 400, 1000, 0), out scarce));
            Assert.AreEqual(2, FreighterRoutingSelector.ScarcityLevel(new Resources(1000, 200, 1000, 0), out scarce));

            Assert.AreEqual(1, FreighterRoutingSelector.ScarcityLevel(new Resources(1000, 800, 300, 0), out scarce), "compared with min(Ironium, Boranium) = 800");
            Assert.AreEqual(ResourceType.Germanium, scarce);
        }

        [Test]
        public void Freighter_TravelTime_IsTwentyFiveLyLegsRoundedUp()
        {
            Assert.AreEqual(1, FreighterRoutingSelector.TravelTime(0));
            Assert.AreEqual(1, FreighterRoutingSelector.TravelTime(25 * 25));
            Assert.AreEqual(2, FreighterRoutingSelector.TravelTime(26 * 26));
            Assert.AreEqual(4, FreighterRoutingSelector.TravelTime(100 * 100));
        }

        [Test]
        public void Freighter_AtTheHub_CollectsFromTheBestValuePerTravelTimePlanet()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true);
            AddOwnedStar("Near", new NovaPoint(25, 0), 300, 300, 300);
            AddOwnedStar("Far", new NovaPoint(100, 0), 5000, 5000, 5000);
            Fleet freighter = AddFleet("Freighter", hub.Position, MakeDesign(1000));
            freighter.InOrbit = hub;

            // Near: M = 900, value 100 x min(90, 100) = 9,000 / T 1. Far: 100 x 100 / T 4 = 2,500.
            FreighterRun run = new FreighterRoutingSelector(clientState, AiCategory.Automitrons, 50).SelectRun(freighter);

            Assert.IsNotNull(run);
            Assert.AreEqual("Near", run.Destination.Name);
            Assert.AreEqual(CargoMode.Load, run.Mode);
            Assert.AreEqual(300, run.Amount.Ironium, "level 0: Load All of every mineral");
            Assert.AreEqual(300, run.Amount.Boranium);
            Assert.AreEqual(300, run.Amount.Germanium);
        }

        [Test]
        public void Freighter_LevelTwo_LoadsOnlyTheHubsScarceMineral()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 100, 1000, 1000, starbase: true);
            AddOwnedStar("Source", new NovaPoint(25, 0), 300, 300, 300);
            Fleet freighter = AddFleet("Freighter", hub.Position, MakeDesign(1000));
            freighter.InOrbit = hub;

            FreighterRun run = new FreighterRoutingSelector(clientState, AiCategory.Automitrons, 50).SelectRun(freighter);

            Assert.AreEqual(300, run.Amount.Ironium);
            Assert.AreEqual(0, run.Amount.Boranium);
            Assert.AreEqual(0, run.Amount.Germanium);
        }

        [Test]
        public void Freighter_LevelOne_FillsToSixtySixAndThirtyThreePercent()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 400, 1000, starbase: true);
            // Free space (1,000) exceeds the source's Boranium (500), so the fill-to-percent rule
            // applies rather than "scarce mineral only".
            AddOwnedStar("Source", new NovaPoint(25, 0), 2000, 500, 2000);
            Fleet freighter = AddFleet("Freighter", hub.Position, MakeDesign(1000));
            freighter.InOrbit = hub;

            FreighterRun run = new FreighterRoutingSelector(clientState, AiCategory.Automitrons, 50).SelectRun(freighter);

            Assert.AreEqual(330, run.Amount.Ironium, "33% of the hold for each other mineral");
            Assert.AreEqual(500, run.Amount.Boranium, "the scarce mineral up to 66% of the hold, capped by the stock");
            Assert.AreEqual(170, run.Amount.Germanium, "33%, capped by the space left");
        }

        [Test]
        public void Freighter_LevelOne_LoadsOnlyTheScarceMineral_WhenItsStockFillsTheHold()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 400, 1000, starbase: true);
            AddOwnedStar("Source", new NovaPoint(25, 0), 2000, 2000, 2000);
            Fleet freighter = AddFleet("Freighter", hub.Position, MakeDesign(1000));
            freighter.InOrbit = hub;

            FreighterRun run = new FreighterRoutingSelector(clientState, AiCategory.Automitrons, 50).SelectRun(freighter);

            Assert.AreEqual(0, run.Amount.Ironium);
            Assert.AreEqual(1000, run.Amount.Boranium);
            Assert.AreEqual(0, run.Amount.Germanium);
        }

        [Test]
        public void Freighter_AFullFreighterGoesHomeToUnload()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true);
            Star source = AddOwnedStar("Source", new NovaPoint(25, 0), 300, 300, 300);
            Fleet freighter = AddFleet("Freighter", source.Position, MakeDesign(1000));
            freighter.InOrbit = source;
            freighter.Cargo.Ironium = 600;
            freighter.Cargo.Boranium = 400;

            FreighterRun run = new FreighterRoutingSelector(clientState, AiCategory.Automitrons, 50).SelectRun(freighter);

            Assert.AreEqual("Hub", run.Destination.Name);
            Assert.IsTrue(run.IsHub);
            Assert.AreEqual(CargoMode.Unload, run.Mode);
            Assert.AreEqual(600, run.Amount.Ironium);
            Assert.AreEqual(400, run.Amount.Boranium);
        }

        [Test]
        public void Freighter_SkipsOtherStarbasePlanets_AndPlanetsAnotherFreighterOfTheSameDesignServes()
        {
            Star hub = AddOwnedStar("Hub", new NovaPoint(0, 0), 1000, 900, 900, starbase: true);
            AddOwnedStar("Fortress", new NovaPoint(10, 0), 3000, 3000, 3000, starbase: true);
            AddOwnedStar("Served", new NovaPoint(20, 0), 3000, 3000, 3000);
            AddOwnedStar("Spare", new NovaPoint(30, 0), 300, 300, 300);
            ShipDesign design = MakeDesign(1000);
            Fleet freighter = AddFleet("Freighter", hub.Position, design);
            freighter.InOrbit = hub;

            Fleet colleague = AddFleet("Colleague", hub.Position, design);
            colleague.Waypoints.Add(new Waypoint { Position = new NovaPoint(20, 0), Destination = "Served", Task = new CargoTask() });

            FreighterRun run = new FreighterRoutingSelector(clientState, AiCategory.Automitrons, 50).SelectRun(freighter);

            Assert.AreEqual("Spare", run.Destination.Name);
        }

        [Test]
        public void Freighter_GetsNoOrder_WithoutAnyStarbasePlanet()
        {
            Star home = AddOwnedStar("Home", new NovaPoint(0, 0), 1000, 1000, 1000);
            AddOwnedStar("Source", new NovaPoint(25, 0), 300, 300, 300);
            Fleet freighter = AddFleet("Freighter", home.Position, MakeDesign(1000));
            freighter.InOrbit = home;

            Assert.IsNull(new FreighterRoutingSelector(clientState, AiCategory.Automitrons, 50).SelectRun(freighter));
        }

        [Test]
        public void Freighter_HubQualification_FollowsTheSection5Thresholds()
        {
            Star star = new Star();
            star.Colonists = 8000;
            star.Mines = 20;
            star.Factories = 20;
            star.MineralConcentration = new Resources(20, 20, 20, 0);
            star.ResourcesOnHand = new Resources(0, 0, 2200, 0);

            // 3 x 4 x 20^2 = 4,800 + 2,200 = 7,000.
            Assert.IsTrue(FreighterRoutingSelector.QualifiesAsHub(star));

            star.ResourcesOnHand = new Resources(0, 0, 2199, 0);
            Assert.IsFalse(FreighterRoutingSelector.QualifiesAsHub(star), "6,999 is short");

            star.ResourcesOnHand = new Resources(0, 0, 2200, 0);
            star.Colonists = 7999;
            Assert.IsFalse(FreighterRoutingSelector.QualifiesAsHub(star), "population must exceed 79 units");
        }
    }
}
