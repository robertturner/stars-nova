namespace Nova.Tests.UnitTests
{
    using System;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // Regression tests for Ultimate Recycling's scrap-fleet resources, per
    // behavior-specs-9/production-queue.md 10g ("Resource funding") and race-traits.md's
    // Ultimate Recycling entry. These REPLACE UltimateRecyclingDeferredResourcesTest, which
    // asserted the superseded spec-7 reading (tested on the scrapper's race, 70% at a starbase
    // only, credited in full on the NEXT turn through a persisted Star.DeferredScrapResources).
    // The spec-9 rule: when the PLANET OWNER has UR (and the planet is owned), scrapping adds the
    // fleet's whole resource cost (ship count x design resource cost, 100%) to a per-planet
    // accumulator d that lives for one turn generation only; the same generation's production
    // pass uses r + floor(d x r / (d + r)) as the planet's output (the remainder is lost), and
    // nothing carries into the next turn or is saved.
    [TestFixture]
    public class UltimateRecyclingResourcesTest
    {
        private static Star OwnedPlanet(EmpireData owner, bool starbase)
        {
            Star star = new Star { Name = "Recycler", Owner = owner.Id, ThisRace = owner.Race, Colonists = 100000 };
            if (starbase)
            {
                star.Starbase = new Fleet(99);
            }

            return star;
        }

        private static void Scrap(Star star, EmpireData scrapper, EmpireData planetOwner, int resourceCost, int quantity)
        {
            Fleet fleet = ScrapFleetRecoveryTest.MakeFleet(scrapper.Id, new Resources(10, 0, 0, resourceCost), quantity);
            fleet.InOrbit = star;
            new ScrapTask().Perform(fleet, star, scrapper, planetOwner);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PlanetOwnerWithUR_AccumulatesTheWholeResourceCost_AtStarbaseOrBarePlanet(bool starbase)
        {
            EmpireData owner = new EmpireData { Id = 1 };
            owner.Race.Traits.Add("UR");
            Star star = OwnedPlanet(owner, starbase);

            Scrap(star, owner, owner, 1000, 2);

            Assert.AreEqual(2000, star.RecycledScrapResources, "ship count x design resource cost, at 100%");
            Assert.AreEqual(0, star.ResourcesOnHand.Energy, "Nothing is credited to the stockpile directly");
        }

        [Test]
        public void AccumulatorSumsFleets_AndClampsEachFleetTo65535()
        {
            EmpireData owner = new EmpireData { Id = 1 };
            owner.Race.Traits.Add("UR");
            Star star = OwnedPlanet(owner, true);

            Scrap(star, owner, owner, 300, 1);
            Scrap(star, owner, owner, 200, 1);
            Assert.AreEqual(500, star.RecycledScrapResources);

            Star other = OwnedPlanet(owner, true);
            Scrap(other, owner, owner, 40000, 2);
            Assert.AreEqual(65535, other.RecycledScrapResources, "The per-fleet sum is clamped to 65,535");
        }

        [Test]
        public void ScrappersURDoesNothing_OverANonURPlanet()
        {
            EmpireData urScrapper = new EmpireData { Id = 1 };
            urScrapper.Race.Traits.Add("UR");
            EmpireData plainOwner = new EmpireData { Id = 2 };
            Star star = OwnedPlanet(plainOwner, true);

            Scrap(star, urScrapper, plainOwner, 1000, 1);

            Assert.AreEqual(0, star.RecycledScrapResources);
        }

        [Test]
        public void ForeignFleetScrappedOverAURPlanet_FeedsThatPlanetsAccumulator()
        {
            EmpireData scrapper = new EmpireData { Id = 1 };
            EmpireData urOwner = new EmpireData { Id = 2 };
            urOwner.Race.Traits.Add("UR");
            Star star = OwnedPlanet(urOwner, false);

            Scrap(star, scrapper, urOwner, 1000, 1);

            Assert.AreEqual(1000, star.RecycledScrapResources);
        }

        [Test]
        public void UnownedPlanet_RecyclesNothing()
        {
            EmpireData urScrapper = new EmpireData { Id = 1 };
            urScrapper.Race.Traits.Add("UR");
            Star star = new Star { Name = "Empty" };

            Scrap(star, urScrapper, null, 1000, 1);

            Assert.AreEqual(0, star.RecycledScrapResources);
        }

        [TestCase(100, 100, 150)]
        [TestCase(30, 70, 51)]   // 30 + floor(70 x 30 / 100) = 30 + 21
        [TestCase(10, 3, 12)]    // 10 + floor(30 / 13) = 10 + 2
        [TestCase(100, 0, 100)]
        [TestCase(0, 50, 0)]
        public void Blend_IsROutputPlusDxROverDPlusR_InIntegerArithmetic(int output, int recycled, int expected)
        {
            Assert.AreEqual(expected, ScrapTask.BlendRecycledResources(output, recycled));
        }

        private static ServerData OneUrPlanetGame(out EmpireData empire, out Star star)
        {
            ServerData serverState = new ServerData();
            empire = new EmpireData { Id = 1, ResearchBudget = 0 };
            empire.Race.Traits.Add("UR");
            empire.Race.ColonistsPerResource = 1000;
            empire.Race.FactoryProduction = 10;

            // Every field maxed, so research resources simply accumulate (no level-up spends
            // them) and the leftover-to-research total equals the planet's output this turn.
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                empire.ResearchLevels[field] = TechLevel.MaxLevel;
            }

            serverState.AllEmpires.Add(empire.Id, empire);
            star = OwnedPlanet(empire, true);
            serverState.AllStars.Add(star.Key, star);
            empire.OwnedStars.Add(star);
            return serverState;
        }

        private static int TotalResearch(EmpireData empire)
        {
            int sum = 0;
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                sum += empire.ResearchResources[field];
            }

            return sum;
        }

        [Test]
        public void StarUpdateStep_BlendsTheAccumulatorIntoThisGenerationsOutput_AndDropsTheRest()
        {
            ServerData serverState = OneUrPlanetGame(out EmpireData empire, out Star star);
            int r = star.GetResourceRate();
            Assert.Greater(r, 0, "test setup: the planet must produce something");

            int d = 3 * r;
            star.RecycledScrapResources = d;

            new StarUpdateStep().Process(serverState);

            int expected = r + (d * r / (d + r));
            Assert.AreEqual(expected, TotalResearch(empire),
                "This generation's spendable output is r + d x r / (d + r) - not r + d, and not deferred to next turn");
            Assert.AreEqual(0, star.RecycledScrapResources, "The accumulator never outlives the generation");
        }

        [Test]
        public void ScrapThenProduction_SameGeneration_AndNothingCarriesIntoTheNext()
        {
            ServerData serverState = OneUrPlanetGame(out EmpireData empire, out Star star);
            int r = star.GetResourceRate();

            Fleet fleet = ScrapFleetRecoveryTest.MakeFleet(empire.Id, new Resources(10, 0, 0, r), 1);
            fleet.Waypoints.Add(new Waypoint { Destination = star.Name, Position = star.Position, Task = new ScrapTask() });
            empire.OwnedFleets.Add(fleet);

            // Generation 1: ScrapFleetStep runs before StarUpdateStep, so the credit (d = r,
            // i.e. +50%) is spent in the same generation.
            new ScrapFleetStep().Process(serverState);
            new StarUpdateStep().Process(serverState);
            int firstGeneration = TotalResearch(empire);
            Assert.AreEqual(r + r / 2, firstGeneration);

            // Generation 2: no scrap - plain output only, the unblended remainder was lost.
            // (The population changed in generation 1, so re-read this generation's r.)
            new ScrapFleetStep().Process(serverState);
            int r2 = star.GetResourceRate();
            new StarUpdateStep().Process(serverState);
            Assert.AreEqual(r2, TotalResearch(empire) - firstGeneration);
        }

        [Test]
        public void ScrapFleetStep_ZeroesAnyStaleAccumulator()
        {
            ServerData serverState = OneUrPlanetGame(out EmpireData empire, out Star star);
            star.RecycledScrapResources = 1234;

            new ScrapFleetStep().Process(serverState);

            Assert.AreEqual(0, star.RecycledScrapResources);
        }

        [Test]
        public void Accumulator_IsNotSaved_AndAnOldDeferredFieldStillLoads()
        {
            EmpireData owner = new EmpireData { Id = 1 };
            Star star = OwnedPlanet(owner, false);
            star.RecycledScrapResources = 500;

            XmlDocument doc = new XmlDocument();
            XmlElement element = star.ToXml(doc);
            StringAssert.DoesNotContain("RecycledScrapResources", element.OuterXml);
            StringAssert.DoesNotContain("DeferredScrapResources", element.OuterXml);

            // A file written by the superseded build carries <DeferredScrapResources>.
            XmlElement legacy = doc.CreateElement("DeferredScrapResources");
            legacy.InnerText = "700";
            element.AppendChild(legacy);

            Star loaded = new Star(element);
            Assert.AreEqual(0, loaded.RecycledScrapResources);
            Assert.AreEqual(star.Name, loaded.Name);
        }
    }
}
