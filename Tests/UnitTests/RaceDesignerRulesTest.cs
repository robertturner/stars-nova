using System;
using System.Linq;

using NUnit.Framework;

using Nova.Client;
using Nova.Common;

namespace Nova.Tests.UnitTests
{
    /// <summary>
    /// behavior-specs-10/race-designer-ui-and-availability.md, Nova.Client side: the economic
    /// stepper (step 1, Shift 3, clamp min-then-max), the Alternate Reality locks and reset,
    /// the JOAT tech-4 label, the draft-local working copy with per-section revert, the eight
    /// Identity-stage presets and the Random generator.
    /// </summary>
    [TestFixture]
    public class RaceDesignerRulesTest
    {
        private static Race NewHumanoid()
        {
            Race race = new Race();
            RacePresets.Apply(RacePresets.Humanoid, race);
            return race;
        }

        [Test]
        public void Step_IsOnePerClick_ThreeWithShift()
        {
            Assert.AreEqual(11, RaceDesignerRules.Step(1, 10, +1, false));
            Assert.AreEqual(13, RaceDesignerRules.Step(1, 10, +1, true));
            Assert.AreEqual(9, RaceDesignerRules.Step(1, 10, -1, false));
            Assert.AreEqual(7, RaceDesignerRules.Step(1, 10, -1, true));
        }

        [Test]
        public void Step_ClampsToEachSlotsTable()
        {
            Assert.AreEqual(15, RaceDesignerRules.Step(1, 14, +1, true), "factory output max 15");
            Assert.AreEqual(2, RaceDesignerRules.Step(5, 3, -1, true), "mine cost min is 2, not 3");
            Assert.AreEqual(7, RaceDesignerRules.Step(0, 8, -1, true), "colonists per resource min 7 (700)");
            Assert.AreEqual(25, RaceDesignerRules.Step(0, 24, +1, true), "colonists per resource max 25 (2,500)");
        }

        [Test]
        public void ClampSlot_RaisesToMinimumThenLowersToMaximum()
        {
            Assert.AreEqual(5, RaceDesignerRules.ClampSlot(6, 3));
            Assert.AreEqual(25, RaceDesignerRules.ClampSlot(6, 99));
        }

        [Test]
        public void SetSlot_StoresColonistsTimesOneHundred()
        {
            Race race = NewHumanoid();
            RaceDesignerRules.SetSlot(race, 0, 12);
            Assert.AreEqual(1200, race.ColonistsPerResource);
            Assert.AreEqual(12, RaceDesignerRules.GetSlot(race, 0));
        }

        [Test]
        public void AlternateReality_LocksRowsTwoToSeven_AndResetsThem()
        {
            Race race = NewHumanoid();
            race.FactoryProduction = 15;
            race.MineBuildCost = 2;
            race.OperableMines = 25;
            RaceDesignerRules.SetLesserTrait(race, RaceDesignerRules.CheapFactories, true);

            RaceDesignerRules.ApplyPrimaryTrait(race, "AR");

            Assert.AreEqual(new[] { 10, 10, 10, 10, 5, 10 }, Enumerable.Range(1, 6).Select(slot => RaceDesignerRules.GetSlot(race, slot)).ToArray());
            Assert.IsFalse(RaceDesignerRules.HasLesserTrait(race, RaceDesignerRules.CheapFactories), "the Germanium discount is cleared");
            Assert.IsTrue(RaceDesignerRules.IsEconomySlotEditable(race, 0), "row 1 stays editable as the AR divisor");
            for (int slot = 1; slot <= 6; slot++)
            {
                Assert.IsFalse(RaceDesignerRules.IsEconomySlotEditable(race, slot), "slot " + slot);
            }
        }

        [Test]
        public void OtherPrimaryTraits_LeaveTheEconomyAlone()
        {
            Race race = NewHumanoid();
            race.FactoryProduction = 15;
            RaceDesignerRules.ApplyPrimaryTrait(race, "HE");
            Assert.AreEqual(15, race.FactoryProduction);
            Assert.IsTrue(Enumerable.Range(0, 7).All(slot => RaceDesignerRules.IsEconomySlotEditable(race, slot)));
        }

        [Test]
        public void NonEditableContext_DisablesEveryRow()
        {
            Race race = NewHumanoid();
            Assert.IsFalse(Enumerable.Range(0, 7).Any(slot => RaceDesignerRules.IsEconomySlotEditable(race, slot, editable: false)));
        }

        [Test]
        public void ExtraTechLabel_IsFourForJackOfAllTrades()
        {
            Race race = NewHumanoid();
            Assert.AreEqual(4, RaceDesignerRules.ExtraTechStartLevel(race));
            RaceDesignerRules.ApplyPrimaryTrait(race, "SS");
            Assert.AreEqual(3, RaceDesignerRules.ExtraTechStartLevel(race));
        }

        [Test]
        public void WorkingCopy_EditsDoNotTouchTheOriginal_UntilCommitted()
        {
            Race original = NewHumanoid();
            original.Name = "Original";
            Race draft = RaceDesignerRules.CopyOf(original);

            draft.Name = "Edited";
            draft.FactoryProduction = 14;
            RaceDesignerRules.SetLesserTrait(draft, "IFE", true);
            draft.GravityTolerance.Immune = true;

            Assert.AreEqual("Original", original.Name);
            Assert.AreEqual(10, original.FactoryProduction);
            Assert.IsFalse(RaceDesignerRules.HasLesserTrait(original, "IFE"));
            Assert.IsFalse(original.GravityTolerance.Immune);

            RaceDesignerRules.CopyAll(draft, original);
            Assert.AreEqual("Edited", original.Name);
            Assert.AreEqual(14, original.FactoryProduction);
            Assert.IsTrue(RaceDesignerRules.HasLesserTrait(original, "IFE"));
            Assert.IsTrue(original.GravityTolerance.Immune);
        }

        [Test]
        public void SectionRevert_RestoresOnlyThatSection()
        {
            Race original = NewHumanoid();
            Race draft = RaceDesignerRules.CopyOf(original);
            draft.FactoryProduction = 14;
            RaceDesignerRules.SetLesserTrait(draft, "IFE", true);

            CollectionAssert.AreEquivalent(
                new[] { RaceDraftSection.Production, RaceDraftSection.Traits },
                RaceDesignerRules.ChangedSections(original, draft).ToList());

            RaceDesignerRules.CopySection(original, draft, RaceDraftSection.Production);

            Assert.AreEqual(10, draft.FactoryProduction, "production reverted");
            Assert.IsTrue(RaceDesignerRules.HasLesserTrait(draft, "IFE"), "the trait edit survives");
            CollectionAssert.AreEquivalent(new[] { RaceDraftSection.Traits }, RaceDesignerRules.ChangedSections(original, draft).ToList());
        }

        [Test]
        public void Presets_AreTheEightButtonsInOrder()
        {
            CollectionAssert.AreEqual(
                new[] { "Humanoid", "Rabbitoid", "Insectoid", "Nucleotid", "Silicanoid", "Antetheral", "Random", "Custom" },
                RacePresets.All.Select(p => p.Name).ToArray());
            Assert.IsTrue(RacePresets.All[6].IsRandom);
            Assert.IsTrue(RacePresets.All[7].IsCustom);
        }

        [TestCase("Humanoid", "JOAT", 1000, 10, 10, 10, 10, 5, 10)]
        [TestCase("Rabbitoid", "IT", 1000, 10, 9, 17, 10, 9, 10)]
        [TestCase("Insectoid", "WM", 1000, 10, 10, 10, 9, 10, 6)]
        [TestCase("Nucleotid", "SS", 900, 10, 10, 10, 10, 15, 5)] // spec-11 correction: mine output 10, mine cost 15, mines 5
        [TestCase("Silicanoid", "HE", 800, 12, 12, 15, 10, 9, 10)]
        [TestCase("Antetheral", "SD", 700, 11, 10, 18, 10, 10, 10)]
        public void ApplyingANamedPreset_SetsItsEconomyAndPrimaryTrait(string name, string prt, int colonists,
            int factoryOutput, int factoryCost, int factories, int mineOutput, int mineCost, int mines)
        {
            Race race = new Race();
            RacePresets.Apply(RacePresets.All.Single(p => p.Name == name), race);

            Assert.AreEqual(prt, race.Traits.Primary.Code);
            Assert.AreEqual(colonists, race.ColonistsPerResource);
            Assert.AreEqual(factoryOutput, race.FactoryProduction);
            Assert.AreEqual(factoryCost, race.FactoryBuildCost);
            Assert.AreEqual(factories, race.OperableFactories);
            Assert.AreEqual(mineOutput, race.MineProductionRate);
            Assert.AreEqual(mineCost, race.MineBuildCost);
            Assert.AreEqual(mines, race.OperableMines);
            Assert.AreEqual(name, race.Name, "an empty name gets the preset's identity text");
        }

        [Test]
        public void ApplyingAPreset_KeepsANameAlreadyTyped_AndCustomChangesNothing()
        {
            Race race = new Race { Name = "Mine", PluralName = "Mines" };
            race.FactoryProduction = 14;
            RacePresets.Apply(RacePresets.All.Single(p => p.IsCustom), race);
            Assert.AreEqual(14, race.FactoryProduction);

            RacePresets.Apply(RacePresets.All[1], race);
            Assert.AreEqual("Mine", race.Name);
            Assert.AreEqual("Mines", race.PluralName);
        }

        [Test]
        public void Humanoid_AlsoSetsItsDefaultResearchLeftoverAndGermanium()
        {
            Race race = new Race();
            race.ResearchCosts[TechLevel.ResearchField.Weapons] = 50;
            race.LeftoverPointTarget = "Defenses";
            RaceDesignerRules.SetLesserTrait(race, RaceDesignerRules.CheapFactories, true);

            RacePresets.Apply(RacePresets.Humanoid, race);

            Assert.IsTrue(RaceDesignerRules.ResearchSlotOrder.All(field => race.ResearchCosts[field] == 100));
            Assert.AreEqual("Surface minerals", race.LeftoverPointTarget);
            Assert.IsFalse(RaceDesignerRules.HasLesserTrait(race, RaceDesignerRules.CheapFactories));
        }

        [Test]
        public void ApplyingANamedPreset_WritesItsFullRecordAndDerivedPlural()
        {
            Race race = new Race();
            RacePresets.Apply(RacePresets.All.Single(p => p.Name == "Silicanoid"), race);

            Assert.IsTrue(race.GravityTolerance.Immune && race.TemperatureTolerance.Immune && race.RadiationTolerance.Immune);
            Assert.AreEqual(6.0, race.GrowthRate);
            Assert.AreEqual("HE", race.Traits.Primary.Code);
            Assert.AreEqual(100, race.ResearchCosts[TechLevel.ResearchField.Energy]);
            Assert.AreEqual(175, race.ResearchCosts[TechLevel.ResearchField.Biotechnology]);
            Assert.AreEqual("Factories", race.LeftoverPointTarget);
            Assert.AreEqual("Silicanoid", race.Name);
            Assert.AreEqual("Silicanoids", race.PluralName, "the preset supplies its name plus the derived 's'");
        }

        private static Race LoadPreset(string name)
        {
            Race race = new Race();
            RacePresets.LoadRecord(RacePresets.All.Single(p => p.Name == name), race);
            return race;
        }

        [Test]
        public void NamedPresetRecords_CarryTheSpecsBandsGrowthAndTraits()
        {
            Race rabbit = LoadPreset("Rabbitoid");
            AssertBand(rabbit.GravityTolerance, false, 10, 56);
            AssertBand(rabbit.TemperatureTolerance, false, 35, 81);
            AssertBand(rabbit.RadiationTolerance, false, 13, 53);
            Assert.AreEqual(20.0, rabbit.GrowthRate);
            Assert.AreEqual("IT", rabbit.Traits.Primary.Code);
            Assert.IsTrue(new[] { "IFE", "TT", "CE", "NAS" }.All(code => RaceDesignerRules.HasLesserTrait(rabbit, code)));
            Assert.IsFalse(RaceDesignerRules.HasLesserTrait(rabbit, "ARM"));
            Assert.IsTrue(RaceDesignerRules.HasLesserTrait(rabbit, RaceDesignerRules.CheapFactories), "the Germanium discount");
            Assert.IsFalse(RaceDesignerRules.HasLesserTrait(rabbit, RaceDesignerRules.ExtraTech));
            Assert.AreEqual("Defenses", rabbit.LeftoverPointTarget);
            Assert.AreEqual(RaceDesignerRules.ResearchCostByClass[0], rabbit.ResearchCosts[TechLevel.ResearchField.Energy]);
            Assert.AreEqual(RaceDesignerRules.ResearchCostByClass[0], rabbit.ResearchCosts[TechLevel.ResearchField.Weapons]);
            Assert.AreEqual(RaceDesignerRules.ResearchCostByClass[2], rabbit.ResearchCosts[TechLevel.ResearchField.Propulsion]);
            Assert.AreEqual(RaceDesignerRules.ResearchCostByClass[1], rabbit.ResearchCosts[TechLevel.ResearchField.Construction]);
            Assert.AreEqual(RaceDesignerRules.ResearchCostByClass[1], rabbit.ResearchCosts[TechLevel.ResearchField.Electronics]);
            Assert.AreEqual(RaceDesignerRules.ResearchCostByClass[2], rabbit.ResearchCosts[TechLevel.ResearchField.Biotechnology]);

            Race insect = LoadPreset("Insectoid");
            Assert.IsTrue(insect.GravityTolerance.Immune);
            AssertBand(insect.TemperatureTolerance, false, 0, 100);
            AssertBand(insect.RadiationTolerance, false, 70, 100);
            Assert.AreEqual("WM", insect.Traits.Primary.Code);
            Assert.AreEqual(9, insect.MineProductionRate);
            Assert.AreEqual(10, insect.MineBuildCost);
            Assert.AreEqual(6, insect.OperableMines);
            Assert.IsTrue(new[] { "ISB", "CE", "RS" }.All(code => RaceDesignerRules.HasLesserTrait(insect, code)));

            Race nucleo = LoadPreset("Nucleotid");
            Assert.IsTrue(nucleo.GravityTolerance.Immune);
            AssertBand(nucleo.TemperatureTolerance, false, 12, 88);
            AssertBand(nucleo.RadiationTolerance, false, 0, 100);
            Assert.AreEqual("SS", nucleo.Traits.Primary.Code);
            Assert.AreEqual(10, nucleo.MineProductionRate, "stored mine output 10, not 15");
            Assert.AreEqual(15, nucleo.MineBuildCost, "stored mine cost 15, not 5");
            Assert.AreEqual(5, nucleo.OperableMines, "stored mines 5; the old '3' read the leftover choice");
            Assert.IsTrue(new[] { "ARM", "ISB" }.All(code => RaceDesignerRules.HasLesserTrait(nucleo, code)));
            Assert.IsFalse(RaceDesignerRules.HasLesserTrait(nucleo, RaceDesignerRules.CheapFactories));
            Assert.IsTrue(RaceDesignerRules.HasLesserTrait(nucleo, RaceDesignerRules.ExtraTech), "the tech-3 start");
            Assert.AreEqual("Factories", nucleo.LeftoverPointTarget);

            Race silica = LoadPreset("Silicanoid");
            Assert.IsTrue(silica.GravityTolerance.Immune && silica.TemperatureTolerance.Immune && silica.RadiationTolerance.Immune);
            Assert.AreEqual(6.0, silica.GrowthRate);
            Assert.AreEqual("HE", silica.Traits.Primary.Code);
            Assert.IsTrue(new[] { "IFE", "UR", "OBRM", "BET" }.All(code => RaceDesignerRules.HasLesserTrait(silica, code)));

            Race anther = LoadPreset("Antetheral");
            AssertBand(anther.GravityTolerance, false, 0, 30);
            AssertBand(anther.TemperatureTolerance, false, 0, 100);
            AssertBand(anther.RadiationTolerance, false, 70, 100);
            Assert.AreEqual(7.0, anther.GrowthRate);
            Assert.AreEqual("SD", anther.Traits.Primary.Code);
            Assert.IsTrue(new[] { "ARM", "MA", "NRS", "CE", "NAS" }.All(code => RaceDesignerRules.HasLesserTrait(anther, code)));
        }

        [Test]
        public void PresetPortraitIndices_AreTheSpecs()
        {
            Assert.AreEqual(1, RacePresets.Humanoid.PortraitIndex);
            Assert.AreEqual(12, RacePresets.All.Single(p => p.Name == "Rabbitoid").PortraitIndex);
            Assert.AreEqual(4, RacePresets.All.Single(p => p.Name == "Insectoid").PortraitIndex);
            Assert.AreEqual(25, RacePresets.All.Single(p => p.Name == "Nucleotid").PortraitIndex);
            Assert.AreEqual(5, RacePresets.All.Single(p => p.Name == "Silicanoid").PortraitIndex);
            Assert.AreEqual(18, RacePresets.All.Single(p => p.Name == "Antetheral").PortraitIndex);
            Assert.AreEqual(31, RacePresets.All.Single(p => p.Name == "Random").PortraitIndex);
        }

        [TestCase("Humanoid", 25)]
        [TestCase("Rabbitoid", 32)]
        [TestCase("Insectoid", 43)]
        [TestCase("Nucleotid", 11)]
        [TestCase("Silicanoid", 9)]
        [TestCase("Antetheral", 7)]
        public void NamedPresetAdvantagePoints_MatchTheSpec(string name, int points)
        {
            Assert.AreEqual(points, LoadPreset(name).GetAdvantagePoints(), name);
        }

        [Test]
        public void RandomPlaceholderRecord_ScoresTwelve()
        {
            Race random = LoadPreset("Random");
            Assert.AreEqual("HE", random.Traits.Primary.Code);
            Assert.AreEqual(1000, random.ColonistsPerResource);
            Assert.AreEqual(3, random.MineBuildCost);
            Assert.AreEqual(15.0, random.GrowthRate);
            Assert.IsTrue(RacePresets.Random.RandomizeAtUniverseCreation, "trait bit 30");
            Assert.AreEqual(12, random.GetAdvantagePoints());
        }

        private static void AssertBand(EnvironmentTolerance tolerance, bool immune, int minimum, int maximum)
        {
            Assert.AreEqual(immune, tolerance.Immune);
            if (!immune)
            {
                Assert.AreEqual(minimum, tolerance.MinimumValue);
                Assert.AreEqual(maximum, tolerance.MaximumValue);
            }
        }

        [Test]
        public void RandomGenerator_LandsInTheZeroToFiftyWindow_OrFallsBackToHumanoid()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                Race draft = new Race { Name = "Draft" };
                draft.GrowthRate = 15;
                RandomRaceResult result = RandomRaceGenerator.Generate(draft, new Random(seed), race => race.GetAdvantagePoints());

                if (result.UsedFallback)
                {
                    Assert.AreEqual("JOAT", result.Race.Traits.Primary.Code, "seed " + seed);
                    Assert.AreEqual(1000, result.Race.ColonistsPerResource);
                    Assert.AreEqual(RandomRaceGenerator.MaximumNudgeAttempts, result.Attempts);
                }
                else
                {
                    Assert.That(result.Race.GetAdvantagePoints(), Is.InRange(0, 50), "seed " + seed);
                }

                Assert.AreEqual("Draft", result.Race.Name, "the drafted name carries over");
                Assert.AreEqual(15, result.Race.GrowthRate, "growth rate is not a rolled slot");
                Assert.AreEqual("Draft", draft.Name);
            }
        }

        [Test]
        public void RandomGenerator_RollsEveryBandAtLeastTwentyWide()
        {
            RandomRaceResult result = RandomRaceGenerator.Generate(new Race(), new Random(3), race => 25);
            Assert.IsFalse(result.UsedFallback);
            Assert.AreEqual(0, result.Attempts, "an in-window first roll needs no nudge");
            foreach (EnvironmentTolerance axis in new[] { result.Race.GravityTolerance, result.Race.TemperatureTolerance, result.Race.RadiationTolerance })
            {
                Assert.GreaterOrEqual(axis.MaximumValue - axis.MinimumValue, 20);
                Assert.That(axis.MinimumValue, Is.InRange(0, 80));
                Assert.That(axis.MaximumValue, Is.InRange(20, 100));
            }

            Assert.AreEqual("Random", result.Race.Name, "an empty name gets the archetype label");
        }

        [Test]
        public void RandomGenerator_FallsBackWhenTheWindowIsNeverReached()
        {
            RandomRaceResult result = RandomRaceGenerator.Generate(new Race(), new Random(5), race => 500);
            Assert.IsTrue(result.UsedFallback);
            Assert.AreEqual("JOAT", result.Race.Traits.Primary.Code);
            Assert.IsTrue(RaceDesignerRules.LesserTraitOrder.All(code => !RaceDesignerRules.HasLesserTrait(result.Race, code)));
        }
    }
}
