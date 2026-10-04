using System;
using System.Linq;

using NUnit.Framework;

using Nova.Ai;
using Nova.Common;
using Nova.Common.RaceDefinition;

namespace Nova.Tests.UnitTests
{
    /// <summary>
    /// behavior-specs-10/ai-opponent-behavior.md §14 (the 24 built-in AI race templates, now
    /// production data in Nova.Ai.AiRaceTemplates) and §1a (template archetype x 4 + tier, the
    /// "Random" draws). The point totals are the §14 resolved table, re-scored here through the
    /// wizard's routine (RaceAdvantagePointCalculator) from the production races.
    /// </summary>
    [TestFixture]
    public class AiRaceTemplatesTest
    {
        private readonly RaceAdvantagePointCalculator calculator = new RaceAdvantagePointCalculator();

        private class ScriptedRandom : Random
        {
            private readonly int[] rolls;
            private int next;

            public ScriptedRandom(params int[] rolls)
            {
                this.rolls = rolls;
            }

            public override int Next(int maxValue)
            {
                return rolls[next++];
            }
        }

        [TestCase(0, 963)]
        [TestCase(1, 79)]
        [TestCase(2, -611)]
        [TestCase(3, -1161)]
        [TestCase(4, 289)]
        [TestCase(5, 13)]
        [TestCase(6, -75)]
        [TestCase(7, -1173)]
        [TestCase(8, 450)]
        [TestCase(9, 96)]
        [TestCase(10, -289)]
        [TestCase(11, -1211)]
        [TestCase(12, 821)]
        [TestCase(13, 1)]
        [TestCase(14, -173)]
        [TestCase(15, -971)]
        [TestCase(16, 832)]
        [TestCase(17, -2)]
        [TestCase(18, -608)]
        [TestCase(19, -1246)]
        [TestCase(20, 480)]
        [TestCase(21, -169)]
        [TestCase(22, -824)]
        [TestCase(23, -1287)]
        public void Template_ScoresTheSpecPoints(int index, int points)
        {
            AiRaceTemplate template = AiRaceTemplates.All[index];
            Assert.AreEqual(index, template.Index);
            Assert.AreEqual(points, template.ExpectedAdvantagePoints);
            Assert.AreEqual(points, calculator.calculateAdvantagePoints(AiRaceTemplates.CreateRace(template)));
        }

        [Test]
        public void FourteenTemplatesWouldBeIllegalForAHuman()
        {
            string[] negative = AiRaceTemplates.All.Where(t => t.ExpectedAdvantagePoints < 0).Select(t => t.Name).ToArray();

            Assert.AreEqual(14, negative.Length);
            Assert.IsTrue(AiRaceTemplates.All.Where(t => t.Tier >= AiRaceTemplates.Tough).All(t => t.ExpectedAdvantagePoints < 0), "every Tough and Expert template");
            CollectionAssert.Contains(negative, "Cybertrons Standard");
            CollectionAssert.Contains(negative, "Macinti Standard");
        }

        [Test]
        public void Get_IsArchetypeTimesFourPlusTier()
        {
            AiRaceTemplate template = AiRaceTemplates.Get(3, AiRaceTemplates.Tough);

            Assert.AreEqual(14, template.Index);
            Assert.AreEqual("Rototills Tough", template.Name);
            Assert.AreEqual("CA", template.PrimaryTrait);
        }

        [Test]
        public void CreateRace_CopiesTurindronesExpert()
        {
            Race race = AiRaceTemplates.CreateRace(AiCategory.Turindrones, AiRaceTemplates.Expert);

            Assert.AreEqual("SS", race.Traits.Primary.Code);
            foreach (string code in new[] { "IFE", "ARM", "MA", "RS", "ExtraTech", "CF" })
            {
                Assert.IsTrue(race.HasTrait(code), code);
            }

            Assert.IsFalse(race.GravityTolerance.Immune);
            Assert.AreEqual(31, race.GravityTolerance.MinimumValue);
            Assert.AreEqual(93, race.GravityTolerance.MaximumValue);
            Assert.AreEqual(5, race.TemperatureTolerance.MinimumValue);
            Assert.AreEqual(53, race.TemperatureTolerance.MaximumValue);
            Assert.IsTrue(race.RadiationTolerance.Immune);
            Assert.AreEqual(15, (int)race.GrowthRate);
            Assert.AreEqual(800, race.ColonistsPerResource);
            Assert.AreEqual(15, race.FactoryProduction);
            Assert.AreEqual(25, race.OperableFactories);
            Assert.AreEqual(175, race.ResearchCosts[TechLevel.ResearchField.Energy]);
            Assert.AreEqual(175, race.ResearchCosts[TechLevel.ResearchField.Biotechnology]);
            Assert.AreEqual(AiRaceTemplates.SurfaceMinerals, race.LeftoverPointTarget);
        }

        [Test]
        public void CreateRace_CopiesAutomitronsExpert_MineralConcentrationsAndCheapResearch()
        {
            Race race = AiRaceTemplates.CreateRace(AiCategory.Automitrons, AiRaceTemplates.Expert);

            Assert.AreEqual("IS", race.Traits.Primary.Code);
            Assert.IsFalse(race.HasTrait("CE"), "Automitrons Tough and Expert drop Cheap Engines");
            Assert.IsTrue(race.TemperatureTolerance.Immune);
            Assert.AreEqual(0, race.RadiationTolerance.MinimumValue);
            Assert.AreEqual(100, race.RadiationTolerance.MaximumValue);
            Assert.AreEqual(AiRaceTemplates.MineralConcentration, race.LeftoverPointTarget);

            Race cyber = AiRaceTemplates.CreateRace(AiCategory.Cybertrons, AiRaceTemplates.Expert);
            Assert.AreEqual(50, cyber.ResearchCosts[TechLevel.ResearchField.Energy]);
            Assert.AreEqual(50, cyber.ResearchCosts[TechLevel.ResearchField.Weapons]);
            Assert.AreEqual(175, cyber.ResearchCosts[TechLevel.ResearchField.Propulsion]);
            Assert.AreEqual(100, cyber.ResearchCosts[TechLevel.ResearchField.Biotechnology]);
        }

        [Test]
        public void Macinti_CarryTheAlternateRealityIncomeDivisorInTheFirstEconomySlot()
        {
            Assert.AreEqual(1600, AiRaceTemplates.CreateRace(AiCategory.Macinti, AiRaceTemplates.Easy).ColonistsPerResource);
            Assert.AreEqual(1200, AiRaceTemplates.CreateRace(AiCategory.Macinti, AiRaceTemplates.Standard).ColonistsPerResource);
            Assert.AreEqual(1000, AiRaceTemplates.CreateRace(AiCategory.Macinti, AiRaceTemplates.Expert).ColonistsPerResource);
        }

        [Test]
        public void TryIdentify_RecoversArchetypeAndTier_ForAll24()
        {
            foreach (AiRaceTemplate template in AiRaceTemplates.All)
            {
                Race race = AiRaceTemplates.CreateRace(template).Clone();
                race.Name = "Renamed";

                Assert.IsTrue(AiRaceTemplates.TryIdentify(race, out int archetype, out int tier), template.Name);
                Assert.AreEqual(template.Archetype, archetype, template.Name);
                Assert.AreEqual(template.Tier, tier, template.Name);
            }
        }

        [Test]
        public void TryIdentify_RejectsAnOrdinaryRace()
        {
            Race race = AiRaceTemplates.CreateRace(AiCategory.Robotoids, AiRaceTemplates.Easy);
            race.GrowthRate = 19;

            Assert.IsFalse(AiRaceTemplates.TryIdentify(race, out int archetype, out int tier));
            Assert.IsFalse(AiRaceTemplates.TryIdentify(new Race(), out archetype, out tier));
        }

        [Test]
        public void ResolveRandom_DrawsTierThenArchetype()
        {
            int archetype = AiRaceTemplates.RandomArchetype;
            int tier = AiRaceTemplates.RandomTier;
            AiRaceTemplates.ResolveRandom(ref archetype, ref tier, new ScriptedRandom(2, 5));

            Assert.AreEqual(2, tier);
            Assert.AreEqual(5, archetype);

            archetype = 1;
            tier = 3;
            AiRaceTemplates.ResolveRandom(ref archetype, ref tier, new ScriptedRandom());
            Assert.AreEqual(1, archetype);
            Assert.AreEqual(3, tier);
        }
    }
}
