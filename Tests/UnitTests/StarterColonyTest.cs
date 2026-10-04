namespace Nova.Tests.UnitTests
{
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;

    // behavior-specs-9/population-growth.md section 3: Alternate Reality's capacity comes entirely from
    // the starbase orbiting the planet, so a won AR colonisation installs design slot 0 "Starter
    // Colony" (an Orbital Fort hull, 250,000 colonists); and losing the starbase in battle depopulates
    // the planet (owner cleared, population 0).
    [TestFixture]
    public class StarterColonyTest
    {
        private ServerData serverState;
        private EmpireData empire;
        private Star star;

        [SetUp]
        public void Init()
        {
            serverState = new SimpleServerData();
            empire = new SimpleEmpireData { Id = 1, Race = new Race() };
            empire.Race.Traits.SetPrimary("AR");
            serverState.AllEmpires.Add(empire.Id, empire);

            star = new Star { Name = "Newtown", Owner = empire.Id, ThisRace = empire.Race, Colonists = 1000 };
            serverState.AllStars.Add(star.Key, star);
            empire.OwnedStars.Add(star);
        }

        [Test]
        public void EnsureDesign_CreatesTheStarterColonyOnTheOrbitalFortHull_Once()
        {
            ShipDesign first = StarterColony.EnsureDesign(empire);
            ShipDesign second = StarterColony.EnsureDesign(empire);

            Assert.AreSame(first, second);
            Assert.AreEqual("Starter Colony", first.Name);
            Assert.AreEqual("Orbital Fort", first.Blueprint.Name);
            Assert.AreEqual(1, empire.Designs.Values.Count(d => d.Name == "Starter Colony"));
        }

        [Test]
        public void Install_GivesTheNewColonyAnOrbitalFortStarbase_WithAnAlternateRealityCapacityOf250000()
        {
            StarterColony.Install(star, empire);

            Assert.IsNotNull(star.Starbase);
            Assert.AreEqual(ItemType.Starbase, star.Starbase.Type);
            Assert.IsTrue(empire.OwnedFleets.ContainsKey(star.Starbase.Key));

            star.Colonists = 125000;
            Assert.AreEqual(50, star.Capacity(empire.Race), "half of the Orbital Fort's 250,000");
        }

        [Test]
        public void Install_DoesNothingWhenThePlanetAlreadyHasAStarbase()
        {
            StarterColony.Install(star, empire);
            Fleet original = star.Starbase;

            StarterColony.Install(star, empire);

            Assert.AreSame(original, star.Starbase);
        }

        [Test]
        public void LosingTheStarbase_DepopulatesAnAlternateRealityPlanet()
        {
            StarterColony.Install(star, empire);
            star.Starbase.Composition.Clear(); // destroyed in battle

            serverState.CleanupFleets();

            Assert.IsNull(star.Starbase);
            Assert.AreEqual(0, star.Colonists);
            Assert.AreEqual(Global.Nobody, star.Owner);
            Assert.IsFalse(empire.OwnedStars.ContainsKey(star.Key));
            Assert.IsTrue(serverState.AllMessages.Any(m => m.Audience == empire.Id), "the owner is told");
        }

        [Test]
        public void LosingTheStarbase_DoesNotDepopulateAnOrdinaryRacesPlanet()
        {
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("HE");
            star.ThisRace = empire.Race;
            StarterColony.Install(star, empire);
            star.Starbase.Composition.Clear();

            serverState.CleanupFleets();

            Assert.IsNull(star.Starbase);
            Assert.AreEqual(1000, star.Colonists);
            Assert.AreEqual(empire.Id, star.Owner);
        }
    }
}
