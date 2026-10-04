namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // behavior-specs-7/research-tech-tree.md row 7 confirms Research.Cost's formula (base cost
    // table + totalLevels*10 surcharge, scaled by the race's per-field cost factor) as an exact
    // match to the real game - but the audit flagged it as having zero direct tests despite that,
    // alongside the rest of the file's near-total test-coverage gap (2/31, both weak/incidental).
    // These pin down each term of the formula independently.
    [TestFixture]
    public class ResearchCostTest
    {
        private static Race MakeRaceWithCostFactor(TechLevel.ResearchField field, int costFactor)
        {
            Race race = new Race();
            race.ResearchCosts[field] = costFactor;
            return race;
        }

        [Test]
        public void Cost_AtLevel1_WithNoTotalLevelsAndDefaultCostFactor_ReturnsTheRawBaseCost()
        {
            Race race = MakeRaceWithCostFactor(TechLevel.ResearchField.Energy, 100);
            TechLevel totalLevels = new TechLevel(0, 0, 0, 0, 0, 0);

            int cost = Research.Cost(TechLevel.ResearchField.Energy, race, totalLevels, 1);

            Assert.AreEqual(50, cost, "BaseCost[1] = 50, no surcharge, 100% cost factor");
        }

        [Test]
        public void Cost_AddsTenPerTotalLevelAttained_AcrossAllSixFields()
        {
            Race race = MakeRaceWithCostFactor(TechLevel.ResearchField.Energy, 100);
            // Sum of levels = 1+2+3+4+5+6 = 21 -> +210 surcharge, regardless of which field is
            // being priced - the surcharge is driven by the empire's TOTAL tech investment, not
            // just the field in question.
            TechLevel totalLevels = new TechLevel(1, 2, 3, 4, 5, 6);

            int cost = Research.Cost(TechLevel.ResearchField.Energy, race, totalLevels, 5);

            Assert.AreEqual(550, cost, "BaseCost[5] = 340, +21*10 = 210 surcharge, 100% cost factor -> 550");
        }

        [Test]
        public void Cost_ScalesByTheRacesPerFieldCostFactor()
        {
            Race cheapRace = MakeRaceWithCostFactor(TechLevel.ResearchField.Weapons, 50);
            Race expensiveRace = MakeRaceWithCostFactor(TechLevel.ResearchField.Weapons, 200);
            TechLevel totalLevels = new TechLevel(0, 0, 0, 0, 0, 0);

            Assert.AreEqual(65, Research.Cost(TechLevel.ResearchField.Weapons, cheapRace, totalLevels, 3),
                "BaseCost[3] = 130, 50% cost factor -> 65");
            Assert.AreEqual(260, Research.Cost(TechLevel.ResearchField.Weapons, expensiveRace, totalLevels, 3),
                "BaseCost[3] = 130, 200% cost factor -> 260");
        }

        [Test]
        public void Cost_CostFactorIsPerField_NotSharedAcrossFields()
        {
            // Confirms the field parameter actually selects a different cost factor, not just a
            // different base-cost-table lookup - a race can have Energy expensive but Weapons cheap.
            Race race = new Race();
            race.ResearchCosts[TechLevel.ResearchField.Energy] = 200;
            race.ResearchCosts[TechLevel.ResearchField.Weapons] = 50;
            TechLevel totalLevels = new TechLevel(0, 0, 0, 0, 0, 0);

            Assert.AreEqual(160, Research.Cost(TechLevel.ResearchField.Energy, race, totalLevels, 2),
                "BaseCost[2] = 80, 200% cost factor -> 160");
            Assert.AreEqual(40, Research.Cost(TechLevel.ResearchField.Weapons, race, totalLevels, 2),
                "BaseCost[2] = 80, 50% cost factor -> 40");
        }
    }

    // Regression test for research-tech-tree.md row 29: the "turns until next level" forecast
    // (Nova.Avalonia's ResearchViewModel.RefreshPreview) divided outstanding cost by the FULL
    // per-turn budgeted contribution, even for a Generalized Research race whose target field only
    // actually receives half of it (StarUpdateStep.ContributeResearch's 50%/15%x5 split) - understating
    // a GR race's true time-to-completion by roughly 2x. Research.TargetFieldContributionFraction is
    // the shared fraction both the forecast and (conceptually) the turn-processing split are built on.
    [TestFixture]
    public class TargetFieldContributionFractionTest
    {
        [Test]
        public void GRRace_TargetFieldOnlyReceivesHalfOfAnyContribution()
        {
            Race race = new Race();
            race.Traits.Add("GR");

            Assert.AreEqual(0.5, Research.TargetFieldContributionFraction(race));
        }

        [Test]
        public void NonGRRace_TargetFieldReceivesTheWholeContribution()
        {
            Race race = new Race();

            Assert.AreEqual(1.0, Research.TargetFieldContributionFraction(race));
        }
    }
}
