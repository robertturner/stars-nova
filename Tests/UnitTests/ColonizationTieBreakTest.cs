namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;

    // Regression tests for the simultaneous-colonization contest, rewritten for spec-9
    // (behavior-specs-9/turn-generation-engine.md §11 "Colonisation and invasion", and
    // fleet-movement-scanning-cargo.md §5 "Colonize resolution, fleet side", step 4). The
    // earlier version asserted the spec-7 reading - raw colonist counts compared, the winner
    // keeping every colonist, losers keeping their ships - which spec-9 supersedes: every
    // colonizing fleet is dismantled at the task pass (see ColoniseDismantleTest), each race's
    // strength is colonists x 110% (165% War Monger, 0 Alternate Reality), an exact tie for
    // first destroys everyone, and the winner keeps colonists x (largest - runner-up) /
    // largest (truncated, minimum 1 unit), the runner-up being the strongest contender with a
    // LOWER race index than the winner (a quirk of the original's running-maximum scan).
    [TestFixture]
    public class ColonizationTieBreakTest
    {
        private ServerData serverState;
        private Star star;

        [SetUp]
        public void Init()
        {
            serverState = new ServerData();
            star = new Star { Name = "Contested" };
            serverState.AllStars.Add(star.Name, star);
        }

        private static Fleet MakeColonizerFleet(string name, int owner, int colonists)
        {
            Fleet fleet = new Fleet(name, (ushort)owner, 1, new NovaPoint(0, 0));
            fleet.Cargo.ColonistsInKilotons = colonists / Global.ColonistsPerKiloton;
            return fleet;
        }

        private static EmpireData MakeEmpire(int id, string trait = null)
        {
            EmpireData empire = new EmpireData { Id = (ushort)id, Race = new Race() };
            if (trait != null)
            {
                empire.Race.Traits.SetPrimary(trait);
            }

            return empire;
        }

        private void Land(EmpireData empire, int colonists)
        {
            star.PendingColonizations.Add(new ColonizationAttempt(MakeColonizerFleet("Fleet " + empire.Id, empire.Id, colonists), empire));
        }

        [Test]
        public void SingleAttempt_Colonizes_WithExactlyTheColonistsCarried()
        {
            EmpireData empire = MakeEmpire(1);
            Land(empire, 500);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(1, star.Owner);
            Assert.AreEqual(500, star.Colonists, "An uncontested landing is not scaled");
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("You have colonised")));
        }

        [Test]
        public void ExistingStockpile_IsKept_NotOverwrittenByTheColonization()
        {
            star.ResourcesOnHand = new Resources(40, 30, 20, 0);
            Land(MakeEmpire(1), 500);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(40, star.ResourcesOnHand.Ironium);
            Assert.AreEqual(30, star.ResourcesOnHand.Boranium);
            Assert.AreEqual(20, star.ResourcesOnHand.Germanium);
        }

        [Test]
        public void HigherIndexWinner_IsScaledByTheLowerIndexRunnerUp()
        {
            // Race 1: 4 units (strength 440); race 2: 10 units (1100). Race 2 displaces race 1,
            // so it keeps 10 x (1100 - 440) / 1100 = 6 units.
            Land(MakeEmpire(1), 400);
            Land(MakeEmpire(2), 1000);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(2, star.Owner);
            Assert.AreEqual(600, star.Colonists);
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("lost the race")),
                "The loser must be notified it lost, not just silently ignored.");
        }

        [Test]
        public void LowerIndexWinner_IsNotScaled_SpecQuirk()
        {
            // Same forces, indices swapped: the weaker contender has a HIGHER index, so it never
            // becomes the runner-up and the winner keeps every colonist (spec quirk, as coded).
            Land(MakeEmpire(1), 1000);
            Land(MakeEmpire(2), 400);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(1, star.Owner);
            Assert.AreEqual(1000, star.Colonists);
        }

        [Test]
        public void RunnerUp_IsTheDisplacedMaximum_NotTheTrueSecondLargest()
        {
            // Race 1: 2 units (220); race 2: 9 units (990, displaces 220); race 3: 8 units (880,
            // weaker, higher index - ignored). Survivors = 9 x (990 - 220) / 990 = 7 units.
            Land(MakeEmpire(1), 200);
            Land(MakeEmpire(2), 900);
            Land(MakeEmpire(3), 800);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(2, star.Owner);
            Assert.AreEqual(700, star.Colonists);
        }

        [Test]
        public void WinnersSurvivors_AreAtLeastOneUnit()
        {
            // Race 1: 1 unit (110); race 2 War Monger: 1 unit (165). 1 x 55 / 165 = 0 -> 1.
            Land(MakeEmpire(1), 100);
            Land(MakeEmpire(2, "WM"), 100);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(2, star.Owner, "War Monger lands at 165% strength");
            Assert.AreEqual(100, star.Colonists);
        }

        [Test]
        public void ExactTieInStrength_DestroysEveryone_WarMongerAt165Percent()
        {
            // 9 units x 110% = 990 = 6 units x 165%: an exact tie for first place.
            Land(MakeEmpire(1), 900);
            Land(MakeEmpire(2, "WM"), 600);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(Global.Nobody, star.Owner);
            Assert.AreEqual(0, star.Colonists);
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("tie")));
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 2 && m.Text.Contains("tie")));
        }

        [Test]
        public void ExactTie_NoOneColonizes_BothNotified()
        {
            Land(MakeEmpire(1), 700);
            Land(MakeEmpire(2), 700);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(Global.Nobody, star.Owner, "An exact tie for first place destroys everyone.");
            Assert.AreEqual(0, star.Colonists);
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("tie")));
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 2 && m.Text.Contains("tie")));
        }

        [Test]
        public void ThreeWayContest_ThirdPlaceLosesEvenThoughOnlyTopTwoTied()
        {
            Land(MakeEmpire(3), 200);
            Land(MakeEmpire(1), 900);
            Land(MakeEmpire(2), 900);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(Global.Nobody, star.Owner, "The top two are tied, so no one colonizes, regardless of the third contender.");
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 1 && m.Text.Contains("tie")));
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 2 && m.Text.Contains("tie")));
            Assert.IsTrue(serverState.AllMessages.Exists(m => m.Audience == 3 && m.Text.Contains("lost the race")),
                "The third-place contender definitively lost outright - it shouldn't get a 'tie' message, since it was never a contender for first.");
        }

        [Test]
        public void AlternateReality_HasZeroContestStrength_ButColonizesUncontested()
        {
            // AR strength 0: a 1-unit ordinary race beats a 50-unit AR landing, and (the AR
            // record having the lower index) is scaled by (110 - 0) / 110, i.e. not at all.
            Land(MakeEmpire(1, "AR"), 5000);
            Land(MakeEmpire(2), 100);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(2, star.Owner);
            Assert.AreEqual(100, star.Colonists);

            // Uncontested, an AR race still founds the colony (message 11).
            Star other = new Star { Name = "Quiet" };
            serverState.AllStars.Add(other.Name, other);
            EmpireData ar = MakeEmpire(3, "AR");
            other.PendingColonizations.Add(new ColonizationAttempt(MakeColonizerFleet("AR colony", 3, 2500), ar));

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(3, other.Owner);
            Assert.AreEqual(2500, other.Colonists);

            // ...and its new colony gets the "Starter Colony" starbase (Orbital Fort hull), without
            // which an Alternate Reality planet has no population capacity at all.
            Assert.IsNotNull(other.Starbase, "an AR winner installs the Starter Colony starbase");
            Assert.AreEqual("Starter Colony", other.Starbase.Composition.Values.First().Design.Name);
        }

        [Test]
        public void AnOrdinaryRacesColony_GetsNoStarbase()
        {
            EmpireData empire = MakeEmpire(1);
            Land(empire, 300);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.IsNull(star.Starbase);
        }

        [Test]
        public void SameRaceFleets_LandAsOneRecord()
        {
            EmpireData empire = MakeEmpire(1);
            Land(empire, 300);
            Land(empire, 400);

            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(1, star.Owner);
            Assert.AreEqual(700, star.Colonists, "One race is never its own rival");
        }

        [Test]
        public void NoAttempts_LeavesStarUntouched()
        {
            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(Global.Nobody, star.Owner);
            Assert.AreEqual(0, star.Colonists);
            Assert.IsEmpty(serverState.AllMessages);
        }
    }

    // behavior-specs-9/fleet-movement-scanning-cargo.md §5, "Colonize resolution, fleet side",
    // steps 1-3: the cancellation list, and the whole fleet dismantled at the task pass - every
    // colonizing fleet, losers too - with floor(3S/4) + cargo of each mineral ADDED to the
    // planet's surface stockpile and energy/fuel lost.
    [TestFixture]
    public class ColoniseDismantleTest
    {
        private static ShipDesign MakeDesign(Resources cost, bool colonizer)
        {
            Component blueprint = new Component { Mass = 100, Cost = cost };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 1000 };
            blueprint.Properties.Add("Hull", hull);
            if (colonizer)
            {
                blueprint.Properties.Add("Colonizer", new Colonizer());
            }

            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update();
            return design;
        }

        private static Fleet MakeFleet(int owner, Star star, int colonistUnits, Resources cost, int quantity, bool colonizer = true)
        {
            Fleet fleet = new Fleet("Colony Ship", (ushort)owner, 1, new NovaPoint(0, 0));
            ShipToken token = new ShipToken(MakeDesign(cost, colonizer), quantity);
            fleet.Composition.Add(token.Key, token);
            fleet.Cargo.ColonistsInKilotons = colonistUnits;
            fleet.InOrbit = star;
            return fleet;
        }

        [Test]
        public void IsValid_CancelsOnAnyOwner_IncludingTheFleetsOwnRace()
        {
            EmpireData empire = new EmpireData { Id = 1 };
            Star own = new Star { Name = "Own", Owner = 1, Colonists = 0 };
            Fleet fleet = MakeFleet(1, own, 5, new Resources(10, 0, 0, 0), 1);

            Assert.IsFalse(new ColoniseTask().IsValid(fleet, own, empire, null),
                "Message 82: an owned planet cancels the order even when it is the fleet's own (and even when its population is 0)");
        }

        [Test]
        public void IsValid_TheOtherCancellations()
        {
            EmpireData empire = new EmpireData { Id = 1 };
            Star star = new Star { Name = "Free" };

            Assert.IsTrue(new ColoniseTask().IsValid(MakeFleet(1, star, 1, new Resources(), 1), star, empire, null),
                "One unit (100 colonists) is enough");

            Assert.IsFalse(new ColoniseTask().IsValid(MakeFleet(1, star, 0, new Resources(), 1), star, empire, null), "No colonists (83)");
            Assert.IsFalse(new ColoniseTask().IsValid(MakeFleet(1, star, 5, new Resources(), 1, colonizer: false), star, empire, null), "No colonization module (84)");

            Fleet deepSpace = MakeFleet(1, star, 5, new Resources(), 1);
            deepSpace.InOrbit = null;
            Assert.IsFalse(new ColoniseTask().IsValid(deepSpace, null, empire, null), "Not at a planet (81)");
        }

        [Test]
        public void Perform_AddsThreeQuartersOfTheFleetCostPlusCargo_ToTheExistingStockpile()
        {
            EmpireData empire = new EmpireData { Id = 1 };
            Star star = new Star { Name = "Free", ResourcesOnHand = new Resources(50, 5, 0, 0) };

            // Two ships of (7 Ir, 3 Bo, 1 Ge, 1000 resources): S = (14, 6, 2).
            Fleet fleet = MakeFleet(1, star, 25, new Resources(7, 3, 1, 1000), 2);
            fleet.Cargo.Ironium = 4;
            fleet.FuelAvailable = 300;

            Assert.IsTrue(new ColoniseTask().Perform(fleet, star, empire, null));

            Assert.AreEqual(50 + 10 + 4, star.ResourcesOnHand.Ironium, "floor(3 x 14 / 4) = 10, plus cargo, ADDED to the stockpile");
            Assert.AreEqual(5 + 4, star.ResourcesOnHand.Boranium, "floor(18 / 4) = 4");
            Assert.AreEqual(1, star.ResourcesOnHand.Germanium, "floor(6 / 4) = 1");
            Assert.AreEqual(0, star.ResourcesOnHand.Energy, "The ships' resource cost is lost (no 75% energy leak)");

            Assert.AreEqual(0, fleet.Composition.Count, "The whole fleet is dismantled at the task pass");
            Assert.AreEqual(Global.Nobody, star.Owner, "Ownership waits for the landing pass");
            Assert.AreEqual(1, star.PendingColonizations.Count);
            Assert.AreEqual(25, star.PendingColonizations[0].ColonistUnits);
        }

        [Test]
        public void LosersAreDismantledToo_AndTheirMineralsStayForTheWinner()
        {
            ServerData serverState = new ServerData();
            Star star = new Star { Name = "Contested" };
            serverState.AllStars.Add(star.Name, star);

            EmpireData winner = new EmpireData { Id = 1, Race = new Race() };
            EmpireData loser = new EmpireData { Id = 2, Race = new Race() };

            Fleet big = MakeFleet(1, star, 10, new Resources(40, 0, 0, 0), 1);
            Fleet small = MakeFleet(2, star, 4, new Resources(40, 0, 0, 0), 1);

            new ColoniseTask().Perform(big, star, winner, null);
            new ColoniseTask().Perform(small, star, loser, null);
            ColonizationResolver.ResolvePendingColonizations(serverState);

            Assert.AreEqual(1, star.Owner);
            Assert.AreEqual(0, small.Composition.Count, "The losing colony ship is gone too");
            Assert.AreEqual(30 + 30, star.ResourcesOnHand.Ironium, "Both fleets' 3/4 salvage is on the planet");
        }
    }
}
