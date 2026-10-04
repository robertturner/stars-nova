namespace Nova.Tests.UnitTests
{
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    // behavior-specs-10/fleet-movement-scanning-cargo.md §4, "Colonist unload outcomes, complete
    // table" (Unload task column): an Alternate Reality race unloading colonists onto any planet
    // that is not its own is REFUSED at the handler (message 86) and the colonists stay aboard.
    // Only the Transfer-dialog route destroys them (message 87), which this port does not model.
    // The trait test comes before the starbase test. (Rewritten for spec-10: this test used to
    // assert that the colonists were destroyed.)
    [TestFixture]
    public class AlternateRealityInvasionTest
    {
        private EmpireData attacker;
        private EmpireData defender;
        private Star star;
        private Fleet fleet;

        private void Build(string primaryTrait)
        {
            attacker = new EmpireData { Id = 1 };
            attacker.Race = new Race();
            attacker.Race.Traits.SetPrimary(primaryTrait);

            defender = new EmpireData { Id = 2 };
            defender.Race = new Race();

            attacker.EmpireReports.Add(defender.Id, new EmpireIntel(defender) { Relation = PlayerRelation.Enemy });

            star = new Star { Name = "Target", Owner = defender.Id, Colonists = 5000 };

            fleet = new Fleet(1) { Owner = attacker.Id };
            fleet.InOrbit = star;
            fleet.Cargo.ColonistsInKilotons = 50;
        }

        [Test]
        public void AlternateRealityUnload_OnAnotherRacesPlanet_IsRefused_AndTheColonistsStayAboard()
        {
            Build("AR");
            InvadeTask task = new InvadeTask();

            bool valid = task.IsValid(fleet, star, attacker, defender);

            Assert.IsFalse(valid);
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons, "Refused at the handler: the colonists stay aboard (message 86)");
            Assert.AreEqual(5000, star.Colonists, "The defenders are untouched");
            Assert.AreEqual(defender.Id, star.Owner);
            StringAssert.Contains("remain aboard", task.Messages.Last().Text);
        }

        /// <summary>The Alternate Reality test comes before the starbase test (message 86, not
        /// 309).</summary>
        [Test]
        public void AlternateRealityUnload_AtAStarbasePlanet_GetsTheAlternateRealityRefusal()
        {
            Build("AR");
            star.Starbase = new Fleet(99);
            InvadeTask task = new InvadeTask();

            Assert.IsFalse(task.IsValid(fleet, star, attacker, defender));
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons);
            StringAssert.Contains("remain aboard", task.Messages.Last().Text);
            StringAssert.DoesNotContain("starbase", task.Messages.Last().Text);
        }

        [Test]
        public void OtherRaces_StillInvadeAnEnemyPlanetNormally()
        {
            Build("WM");
            InvadeTask task = new InvadeTask();

            bool valid = task.IsValid(fleet, star, attacker, defender);

            Assert.IsTrue(valid, "Only Alternate Reality is barred from landing on an owned planet");
            Assert.AreEqual(50, fleet.Cargo.ColonistsInKilotons);
        }
    }
}
