namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;

    using Nova.Ai;

    using NUnit.Framework;

    /// <summary>
    /// The arithmetic of the AI's per-planet production rules
    /// (docs/behavior-specs-10/ai-opponent-behavior.md §4 colony-ship gate, §2/§6 isolation check,
    /// §6 advisor chain, bomber defence and end-of-pass top-up). Population figures are the
    /// original's stored units of 100 colonists (§13).
    /// </summary>
    [TestFixture]
    public class AiPlanetAdvisorsTest
    {
        /// <summary>Replays a fixed list of Next(max) results.</summary>
        private class ScriptedRandom : Random
        {
            private readonly Queue<int> rolls;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = new Queue<int>(rolls);
            }

            public int Calls { get; private set; }

            public override int Next(int maxValue)
            {
                Calls++;
                return rolls.Count > 0 ? rolls.Dequeue() : 0;
            }
        }

        // ---------------------------------------------------------------- population gate

        [Test]
        public void PopulationGate_CategoryZeroNeedsFortyUnits_OthersSixty()
        {
            Assert.IsFalse(PlanetAdvisors.PassesAdvisorPopulationGate(AiCategory.Robotoids, 39, false));
            Assert.IsTrue(PlanetAdvisors.PassesAdvisorPopulationGate(AiCategory.Robotoids, 40, false));
            Assert.IsFalse(PlanetAdvisors.PassesAdvisorPopulationGate(AiCategory.Automitrons, 59, false));
            Assert.IsTrue(PlanetAdvisors.PassesAdvisorPopulationGate(AiCategory.Automitrons, 60, false));
            Assert.IsTrue(PlanetAdvisors.PassesAdvisorPopulationGate(AiCategory.Automitrons, 1, true), "the urgent-supply flag passes the gate");
        }

        // ---------------------------------------------------------------- Defenses advisor

        [Test]
        public void DefensesAdvisor_NeedsSixteenHundredUnits()
        {
            Assert.AreEqual(0, PlanetAdvisors.DefensesAdvisor(1599, 0, 100, 0, false));
            Assert.AreEqual(4, PlanetAdvisors.DefensesAdvisor(1600, 0, 100, 0, false));
        }

        [Test]
        public void DefensesAdvisor_StopsAtOneDefencePerEightyUnits()
        {
            // 1,600 units -> target 20 defences.
            Assert.AreEqual(4, PlanetAdvisors.DefensesAdvisor(1600, 19, 100, 0, false));
            Assert.AreEqual(0, PlanetAdvisors.DefensesAdvisor(1600, 20, 100, 0, false));
        }

        [Test]
        public void DefensesAdvisor_QueuesAtMostFour_AndAtMostTheRemainingCap()
        {
            Assert.AreEqual(4, PlanetAdvisors.DefensesAdvisor(8000, 0, 100, 0, false));
            Assert.AreEqual(2, PlanetAdvisors.DefensesAdvisor(8000, 8, 10, 0, false), "cap 10 - 8 built = 2");
            Assert.AreEqual(0, PlanetAdvisors.DefensesAdvisor(8000, 10, 10, 0, false));
        }

        [Test]
        public void DefensesAdvisor_DoesNothingWhileAManualDefensesItemIsQueued()
        {
            Assert.AreEqual(0, PlanetAdvisors.DefensesAdvisor(8000, 0, 100, 3, true));
        }

        // ---------------------------------------------------------------- Terraform advisor

        [Test]
        public void TerraformAdvisor_NeverForCategoriesZeroAndFour()
        {
            Assert.AreEqual(0, PlanetAdvisors.TerraformAdvisor(AiCategory.Robotoids, 1000, false, true, 10, 0));
            Assert.AreEqual(0, PlanetAdvisors.TerraformAdvisor(AiCategory.Cybertrons, 1000, false, true, 10, 0));
            Assert.AreEqual(4, PlanetAdvisors.TerraformAdvisor(AiCategory.Automitrons, 1000, false, true, 10, 0));
        }

        [Test]
        public void TerraformAdvisor_NeedsTwoHundredUnits_AnOffIdealAxis_AndHeadroom()
        {
            Assert.AreEqual(0, PlanetAdvisors.TerraformAdvisor(AiCategory.Automitrons, 199, false, true, 10, 0));
            Assert.AreEqual(0, PlanetAdvisors.TerraformAdvisor(AiCategory.Automitrons, 200, false, false, 10, 0));
            Assert.AreEqual(0, PlanetAdvisors.TerraformAdvisor(AiCategory.Automitrons, 200, true, true, 10, 0), "manual Terraform already queued");
            Assert.AreEqual(3, PlanetAdvisors.TerraformAdvisor(AiCategory.Automitrons, 200, false, true, 5, 2), "C = headroom 5 - queued 2");
            Assert.AreEqual(0, PlanetAdvisors.TerraformAdvisor(AiCategory.Automitrons, 200, false, true, 2, 2));
        }

        // ---------------------------------------------------------------- bomber defence

        [Test]
        public void BomberDefence_FirstTurnIsSizeIndexPlusTwoTimesTen()
        {
            Assert.AreEqual(20, PlanetAdvisors.BomberDefenceFirstTurn(0));
            Assert.AreEqual(60, PlanetAdvisors.BomberDefenceFirstTurn(4));
        }

        [Test]
        public void BomberDefence_QueuesNDefences_WhenMineralsCoverThem()
        {
            // R = 300: n = 12, minus 12/6 = 10. m = min(100, 100/5) = 20, so n <= m.
            Assert.AreEqual((10, 0), PlanetAdvisors.BomberDefence(300, 100, 100, 100, 100, false, false));
        }

        [Test]
        public void BomberDefence_IsCappedByTheRemainingDefenceCap()
        {
            Assert.AreEqual((3, 0), PlanetAdvisors.BomberDefence(300, 100, 100, 100, 3, false, false));
        }

        [Test]
        public void BomberDefence_AddsAlchemyForTheExtraDefences_WhenMineralsRunShort()
        {
            // R = 1000: n = 40 - 6 = 34; m = 20 / 5 = 4; R' = 900; e = (900 - 100 x 4) / 150 = 3.
            Assert.AreEqual((7, 15), PlanetAdvisors.BomberDefence(1000, 20, 20, 20, 100, false, false));

            // Mineral Alchemy: a = 25, e = (900 - 100) / 150 = 5.
            Assert.AreEqual((9, 25), PlanetAdvisors.BomberDefence(1000, 20, 20, 20, 100, false, true));
        }

        [Test]
        public void BomberDefence_FloorsTheExtraAtZero()
        {
            // R = 300: n = 10 > m = 4; e = (270 - 400) / 150 < 0 -> 0.
            Assert.AreEqual((4, 0), PlanetAdvisors.BomberDefence(300, 20, 20, 20, 100, false, false));
        }

        [Test]
        public void BomberDefence_NeedsFiftyResources_ACap_AndNoManualDefences()
        {
            Assert.AreEqual((0, 0), PlanetAdvisors.BomberDefence(49, 100, 100, 100, 100, false, false));
            Assert.AreEqual((0, 0), PlanetAdvisors.BomberDefence(300, 100, 100, 100, 0, false, false));
            Assert.AreEqual((0, 0), PlanetAdvisors.BomberDefence(300, 100, 100, 100, 100, true, false));
        }

        // ---------------------------------------------------------------- top-up

        private static TopUpResult TopUp(
            int category = AiCategory.Automitrons,
            long i = 200, long b = 150, long g = 30, long r = 500,
            int factoryRoom = 40, int mineRoom = 25, int terraform = 0,
            bool headIsAlchemy = false, bool allTech26 = false, int year = 50, int alchemyCost = 100)
        {
            return PlanetAdvisors.TopUp(category, i, b, g, r, factoryRoom, mineRoom, 10, 4, 5, alchemyCost, terraform, headIsAlchemy, allTech26, year);
        }

        [Test]
        public void TopUp_SpecWorkedExample_SevenFactoriesAndTwentyFiveMines()
        {
            TopUpResult result = TopUp();

            Assert.AreEqual(7, result.Factories, "f0 = min(40, 30 / 4 = 7); f = min(7, 500 / 10)");
            Assert.AreEqual(25, result.Mines, "m = min(25, 430 / 5)");
            Assert.AreEqual(0, result.Terraform);
            Assert.AreEqual(0, result.Alchemy);
        }

        [Test]
        public void TopUp_FactoriesAreCappedByResourcesAfterGermanium()
        {
            TopUpResult result = TopUp(r: 50);

            Assert.AreEqual(5, result.Factories, "min(7, 50 / 10)");
            Assert.AreEqual(0, result.Mines, "all 50 resources went to factories");
        }

        [Test]
        public void TopUp_CategoryFive_QueuesTerraformFirstAndNothingElse()
        {
            TopUpResult result = TopUp(category: AiCategory.Macinti, r: 141, terraform: 10);

            Assert.AreEqual(3, result.Terraform, "(141 + 69) / 70");
            Assert.AreEqual(0, result.Factories);
            Assert.AreEqual(0, result.Mines);
        }

        [Test]
        public void TopUp_TerraformStepIsCategoryFiveOnly()
        {
            TopUpResult result = TopUp(category: AiCategory.Automitrons, terraform: 10);

            Assert.AreEqual(0, result.Terraform);
            Assert.AreEqual(7, result.Factories);
        }

        [Test]
        public void TopUp_AMineralShortfall_QueuesMinesOnly()
        {
            TopUpResult result = TopUp(b: 0);

            Assert.AreEqual(0, result.Factories);
            Assert.AreEqual(25, result.Mines);
        }

        [Test]
        public void TopUp_AMineralShortfall_QueuesNothingBehindAQueuedAlchemy()
        {
            TopUpResult result = TopUp(b: 0, headIsAlchemy: true);

            Assert.AreEqual(0, result.Factories);
            Assert.AreEqual(0, result.Mines);
        }

        [Test]
        public void TopUp_NegativeResourceSurplus_QueuesNothing()
        {
            TopUpResult result = TopUp(r: -1);

            Assert.AreEqual(0, result.Factories);
            Assert.AreEqual(0, result.Mines);
        }

        [Test]
        public void TopUp_Alchemy_NeedsAllTechsAt26AfterYear100_AndBuysOneMoreThanTheLeftoverPays()
        {
            Assert.AreEqual(0, TopUp(allTech26: true, year: 100).Alchemy);
            Assert.AreEqual(0, TopUp(allTech26: false, year: 101).Alchemy);

            TopUpResult result = TopUp(allTech26: true, year: 101);
            Assert.AreEqual(4, result.Alchemy, "305 left / 100 + 1");
            Assert.IsFalse(result.AlchemyAtTop, "after step 2b alchemy goes to the bottom");

            Assert.IsTrue(TopUp(b: 0, allTech26: true, year: 101).AlchemyAtTop, "after step 2a it goes to the top");
        }

        // ---------------------------------------------------------------- colony-ship gate

        [Test]
        public void ColonyGate_EasyRefusesInOddYears_EvenEarly()
        {
            Assert.IsFalse(PlanetAdvisors.ColonyShipGate(0, 3, true, 0, 0, 0, 100, new ScriptedRandom()));
            Assert.IsTrue(PlanetAdvisors.ColonyShipGate(0, 4, true, 0, 0, 0, 100, new ScriptedRandom()));
        }

        [Test]
        public void ColonyGate_AllowsBeforeYear30_WithoutRolling()
        {
            ScriptedRandom random = new ScriptedRandom(1);
            Assert.IsTrue(PlanetAdvisors.ColonyShipGate(1, 29, false, 50, 0, 1000, 10, random));
            Assert.AreEqual(0, random.Calls);
        }

        [Test]
        public void ColonyGate_RefusesWithoutAColonyDesign()
        {
            Assert.IsFalse(PlanetAdvisors.ColonyShipGate(1, 30, false, 0, 0, 0, 100, new ScriptedRandom(0)));
        }

        [Test]
        public void ColonyGate_RefusesBeyondTwentyFleetsPerSizeStepPlusTen()
        {
            // With step 6 unavailable this refusal coincides with step 8's (more than 10 fleets
            // can never pass the under-5 coin), so these only document the outcome.
            ScriptedRandom random = new ScriptedRandom(0);
            Assert.IsFalse(PlanetAdvisors.ColonyShipGate(1, 30, true, 11, 0, 0, 1000, random));
            Assert.IsFalse(PlanetAdvisors.ColonyShipGate(1, 30, true, 91, 4, 0, 1000, random));
            Assert.AreEqual(0, random.Calls, "refused before any roll");
        }

        [Test]
        public void ColonyGate_RefusesWhenFleetsPlusKnownPlanetsPassFourFifthsOfTheGalaxy()
        {
            // 100 planets -> limit 80.
            Assert.IsFalse(PlanetAdvisors.ColonyShipGate(1, 30, true, 1, 0, 80, 100, new ScriptedRandom(0)));
            Assert.IsTrue(PlanetAdvisors.ColonyShipGate(1, 30, true, 1, 0, 79, 100, new ScriptedRandom(0)));
        }

        [Test]
        public void ColonyGate_AfterYear30_IsACoinWhileUnderFiveColonyFleets()
        {
            Assert.IsTrue(PlanetAdvisors.ColonyShipGate(1, 40, true, 4, 0, 0, 100, new ScriptedRandom(0)));
            Assert.IsFalse(PlanetAdvisors.ColonyShipGate(1, 40, true, 4, 0, 0, 100, new ScriptedRandom(1)));
            Assert.IsFalse(PlanetAdvisors.ColonyShipGate(1, 40, true, 5, 0, 0, 100, new ScriptedRandom(0)), "five or more colony fleets: refuse");
        }

        // ---------------------------------------------------------------- isolation check

        [Test]
        public void IsolationCheck_OnlyAfterYear59()
        {
            Assert.IsTrue(PlanetAdvisors.PassesIsolationCheck(59, 1e9, new ScriptedRandom()));
            Assert.IsFalse(PlanetAdvisors.PassesIsolationCheck(60, 1e9, new ScriptedRandom()));
        }

        [Test]
        public void IsolationCheck_RefusesBeyond350_RollsBeyond300And250()
        {
            Assert.IsFalse(PlanetAdvisors.PassesIsolationCheck(60, 350.5 * 350.5, new ScriptedRandom(1, 1)));
            Assert.IsTrue(PlanetAdvisors.PassesIsolationCheck(60, 349.0 * 349.0, new ScriptedRandom(1, 1)));
            Assert.IsFalse(PlanetAdvisors.PassesIsolationCheck(60, 349.0 * 349.0, new ScriptedRandom(1, 0)), "second roll (250 ly) refuses");
            Assert.IsFalse(PlanetAdvisors.PassesIsolationCheck(60, 260.0 * 260.0, new ScriptedRandom(0)));

            ScriptedRandom near = new ScriptedRandom(0, 0);
            Assert.IsTrue(PlanetAdvisors.PassesIsolationCheck(60, 249.0 * 249.0, near));
            Assert.AreEqual(0, near.Calls, "no roll under 250 ly");
        }

        [Test]
        public void GalaxySizeIndex_MatchesTheServerFormula()
        {
            Assert.AreEqual(0, PlanetAdvisors.GalaxySizeIndex(400));
            Assert.AreEqual(1, PlanetAdvisors.GalaxySizeIndex(800));
            Assert.AreEqual(4, PlanetAdvisors.GalaxySizeIndex(2000));
            Assert.AreEqual(4, PlanetAdvisors.GalaxySizeIndex(4000));
        }
    }
}
