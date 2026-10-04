namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // Regression test for a latent-trap bug behavior-specs-7/diplomacy-relations.md's row 4
    // flags: EmpireIntel.Relation is an auto-property with no initializer, so any caller
    // constructing a new EmpireIntel(EmpireData) without immediately setting .Relation afterward
    // silently got Enemy (PlayerRelation's default enum value, 0) rather than the spec-confirmed
    // Neutral default for a newly-met race. GameInitialiser.cs's own new-game path happened to
    // work around this by setting .Relation explicitly right after construction, masking the bug
    // there - but the constructor itself still had the wrong default for any other caller.
    [TestFixture]
    public class EmpireIntelDefaultRelationTest
    {
        [Test]
        public void NewlyConstructedEmpireIntel_DefaultsToNeutral_NotEnemy()
        {
            EmpireData empire = new EmpireData { Id = 1, Race = new Race { Name = "Testrace" } };

            EmpireIntel intel = new EmpireIntel(empire);

            Assert.AreEqual(PlayerRelation.Neutral, intel.Relation,
                "A newly-met race's relation should default to Neutral, not silently fall back to Enemy.");
        }
    }
}
