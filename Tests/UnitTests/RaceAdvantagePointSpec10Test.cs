using System;

using NUnit.Framework;

using Nova.Common;
using Nova.Common.RaceDefinition;

namespace Nova.Tests.UnitTests
{
    /// <summary>
    /// behavior-specs-10/race-traits.md section 1b (the habitability-quality sampler, using the
    /// integer evaluator Race.HabPercent as each sample's Planet Value) and the 18 live wizard
    /// readings; ai-opponent-behavior.md section 14 (the 24 AI templates, scored by the same
    /// routine). The templates have asymmetric bands, so they also pin the sampler's axis order
    /// (Gravity, Temperature, Radiation innermost).
    /// </summary>
    [TestFixture]
    public class RaceAdvantagePointSpec10Test
    {
        private readonly RaceAdvantagePointCalculator calculator = new RaceAdvantagePointCalculator();

        private static void SetBand(EnvironmentTolerance tolerance, int low, int high)
        {
            tolerance.Immune = false;
            tolerance.MinimumValue = low;
            tolerance.MaximumValue = high;
        }

        /// <summary>The Humanoid preset: JOAT, no LRTs, every band 15-85, growth 15%, Humanoid economy.</summary>
        private static Race Humanoid()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            SetBand(race.GravityTolerance, 15, 85);
            SetBand(race.TemperatureTolerance, 15, 85);
            SetBand(race.RadiationTolerance, 15, 85);
            race.GrowthRate = 15;
            race.ColonistsPerResource = 1000;
            race.FactoryProduction = 10;
            race.FactoryBuildCost = 10;
            race.OperableFactories = 10;
            race.MineProductionRate = 10;
            race.MineBuildCost = 5;
            race.OperableMines = 10;
            race.ResearchCosts[TechLevel.ResearchField.Energy] = 100;
            race.ResearchCosts[TechLevel.ResearchField.Weapons] = 100;
            race.ResearchCosts[TechLevel.ResearchField.Propulsion] = 100;
            race.ResearchCosts[TechLevel.ResearchField.Construction] = 100;
            race.ResearchCosts[TechLevel.ResearchField.Electronics] = 100;
            race.ResearchCosts[TechLevel.ResearchField.Biotechnology] = 100;
            return race;
        }

        [Test]
        public void Sampler_HumanoidQuality()
        {
            Assert.AreEqual(3293786, calculator.HabitabilityQuality(Humanoid()));
        }

        [Test]
        public void Sampler_HumanoidGravityImmuneQuality()
        {
            Race race = Humanoid();
            race.GravityTolerance.Immune = true;
            Assert.AreEqual(6345682, calculator.HabitabilityQuality(race));
        }

        [Test]
        public void Sampler_TriImmuneQuality()
        {
            Race race = Humanoid();
            race.GravityTolerance.Immune = true;
            race.TemperatureTolerance.Immune = true;
            race.RadiationTolerance.Immune = true;
            Assert.AreEqual(23958000, calculator.HabitabilityQuality(race));
        }

        [TestCase(null, 25)]
        [TestCase("TT", -115)]
        [TestCase("GR", 38)]
        [TestCase("UR", -55)]
        [TestCase("MA", -26)]
        [TestCase("IFE", -53)]
        public void LiveReading_HumanoidWithOneLesserTrait(string lrt, int expected)
        {
            Race race = Humanoid();
            if (lrt != null)
            {
                race.Traits.Add(lrt);
            }
            Assert.AreEqual(expected, calculator.calculateAdvantagePoints(race));
        }

        [Test]
        public void LiveReading_ImmunitySequence()
        {
            Race race = Humanoid();

            race.GravityTolerance.Immune = true;
            Assert.AreEqual(-547, calculator.calculateAdvantagePoints(race), "Gravity immune");

            SetBand(race.GravityTolerance, 20, 80);
            race.TemperatureTolerance.Immune = true;
            Assert.AreEqual(-431, calculator.calculateAdvantagePoints(race), "Gravity 20-80, Temperature immune");

            SetBand(race.TemperatureTolerance, 20, 80);
            race.RadiationTolerance.Immune = true;
            Assert.AreEqual(-326, calculator.calculateAdvantagePoints(race), "Gravity and Temperature 20-80, Radiation immune");

            race.GravityTolerance.Immune = true;
            Assert.AreEqual(-1464, calculator.calculateAdvantagePoints(race), "+ Gravity immune");

            race.TemperatureTolerance.Immune = true;
            Assert.AreEqual(-3900, calculator.calculateAdvantagePoints(race), "+ Temperature immune");

            SetBand(race.GravityTolerance, 20, 80);
            SetBand(race.TemperatureTolerance, 20, 80);
            SetBand(race.RadiationTolerance, 20, 80);
            Assert.AreEqual(178, calculator.calculateAdvantagePoints(race), "all immunities cleared, 20-80 bands");
        }

        // ai-opponent-behavior.md section 14. Bands are "low-high" or "imm"; economy is colonists
        // per resource (for AR the income divisor x 100), factory output/cost/operated, mine
        // output/cost/operated; research is E W P C El B with + expensive, n normal, - cheap;
        // extras are ExtraTech (tech-3 start) and CF (Germanium discount).
        [TestCase("HE", "IFE MA CE OBRM BET", "imm", "imm", "imm", 5, "1000 12 10 16 10 5 10", "nn-nn+", "", 963, TestName = "Template00_RobotoidsEasy")]
        [TestCase("HE", "IFE MA CE OBRM", "imm", "imm", "imm", 6, "900 13 9 16 10 4 11", "nn-nn+", "", 79, TestName = "Template01_RobotoidsStandard")]
        [TestCase("HE", "IFE UR MA OBRM", "imm", "imm", "imm", 6, "800 13 9 18 10 4 12", "n---n+", "CF", -611, TestName = "Template02_RobotoidsTough")]
        [TestCase("HE", "IFE UR MA OBRM", "imm", "imm", "imm", 7, "800 13 9 16 10 4 8", "n-n-n+", "CF", -1161, TestName = "Template03_RobotoidsExpert")]
        [TestCase("SS", "IFE ARM MA RS", "27-89", "7-63", "35-95", 14, "1000 9 10 9 9 5 8", "n+nnn+", "", 289, TestName = "Template04_TurindronesEasy")]
        [TestCase("SS", "IFE ARM MA RS", "32-92", "6-60", "26-96", 14, "1000 10 10 10 10 5 9", "nnnnnn", "CF", 13, TestName = "Template05_TurindronesStandard")]
        [TestCase("SS", "IFE ARM MA RS", "31-95", "4-52", "30-94", 14, "900 11 10 10 10 5 9", "+n+nnn", "CF", -75, TestName = "Template06_TurindronesTough")]
        [TestCase("SS", "IFE ARM MA RS", "31-93", "5-53", "imm", 15, "800 15 10 25 10 5 9", "++++++", "ExtraTech CF", -1173, TestName = "Template07_TurindronesExpert")]
        [TestCase("IS", "GR CE OBRM NAS LSP", "7-63", "26-94", "5-71", 15, "900 11 10 14 11 6 14", "++++++", "ExtraTech", 450, TestName = "Template08_AutomitronsEasy")]
        [TestCase("IS", "GR CE OBRM NAS LSP", "7-63", "26-94", "5-71", 15, "800 13 9 14 10 6 14", "++++++", "ExtraTech CF", 96, TestName = "Template09_AutomitronsStandard")]
        [TestCase("IS", "GR OBRM NAS LSP", "7-63", "26-94", "5-71", 15, "800 14 9 15 14 5 15", "++++++", "ExtraTech CF", -289, TestName = "Template10_AutomitronsTough")]
        [TestCase("IS", "GR OBRM NAS LSP", "7-63", "imm", "0-100", 16, "800 14 9 14 14 5 14", "++++++", "ExtraTech CF", -1211, TestName = "Template11_AutomitronsExpert")]
        [TestCase("CA", "TT CE OBRM NAS LSP BET", "32-68", "31-69", "31-69", 15, "1000 10 10 10 10 5 10", "+++++-", "ExtraTech", 821, TestName = "Template12_RototillsEasy")]
        [TestCase("CA", "TT OBRM NAS LSP BET", "32-68", "31-69", "31-69", 15, "800 12 10 12 14 5 12", "+++++-", "ExtraTech", 1, TestName = "Template13_RototillsStandard")]
        [TestCase("CA", "TT OBRM NAS LSP BET", "23-77", "24-76", "25-75", 15, "800 12 10 12 14 5 12", "+++++-", "ExtraTech", -173, TestName = "Template14_RototillsTough")]
        [TestCase("CA", "TT OBRM NAS LSP BET", "imm", "24-76", "25-75", 15, "800 15 10 15 15 5 15", "+++++-", "ExtraTech", -971, TestName = "Template15_RototillsExpert")]
        [TestCase("PP", "IFE TT OBRM LSP", "22-78", "22-78", "22-78", 12, "1000 9 18 9 9 10 8", "n++n++", "ExtraTech", 832, TestName = "Template16_CybertronsEasy")]
        [TestCase("PP", "IFE TT OBRM NAS LSP", "19-81", "19-81", "19-81", 17, "1000 10 13 19 10 10 7", "n++nnn", "ExtraTech", -2, TestName = "Template17_CybertronsStandard")]
        [TestCase("PP", "IFE TT MA OBRM NAS LSP", "18-82", "18-82", "18-82", 17, "1000 14 10 20 10 10 6", "nn+nn-", "ExtraTech CF", -608, TestName = "Template18_CybertronsTough")]
        [TestCase("PP", "IFE TT MA OBRM NAS LSP", "17-83", "17-83", "17-83", 19, "1000 15 9 25 10 10 5", "--+-nn", "ExtraTech CF", -1246, TestName = "Template19_CybertronsExpert")]
        [TestCase("AR", "IFE TT ISB GR CE", "20-80", "20-80", "20-80", 10, "1600 10 10 10 10 5 10", "nnnn+n", "", 480, TestName = "Template20_MacintiEasy")]
        [TestCase("AR", "IFE TT ISB GR", "15-85", "15-85", "15-85", 14, "1200 10 10 10 10 5 10", "-nnn+n", "", -169, TestName = "Template21_MacintiStandard")]
        [TestCase("AR", "IFE TT ARM ISB GR UR MA", "15-85", "15-85", "15-85", 17, "1000 10 10 10 10 5 10", "-nnnnn", "", -824, TestName = "Template22_MacintiTough")]
        [TestCase("AR", "IFE TT ARM ISB GR UR MA", "15-85", "15-85", "15-85", 20, "1000 10 10 10 10 5 10", "-nn-nn", "", -1287, TestName = "Template23_MacintiExpert")]
        public void AiTemplate_ScoresAsTheWizardWould(string prt, string lrts, string gravity, string temperature, string radiation,
            int growth, string economy, string research, string extras, int expected)
        {
            Race race = new Race();
            race.Traits.SetPrimary(prt);
            foreach (string code in (lrts + " " + extras).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                race.Traits.Add(code);
            }

            ApplyBand(race.GravityTolerance, gravity);
            ApplyBand(race.TemperatureTolerance, temperature);
            ApplyBand(race.RadiationTolerance, radiation);
            race.GrowthRate = growth;

            string[] e = economy.Split(' ');
            race.ColonistsPerResource = int.Parse(e[0]);
            race.FactoryProduction = int.Parse(e[1]);
            race.FactoryBuildCost = int.Parse(e[2]);
            race.OperableFactories = int.Parse(e[3]);
            race.MineProductionRate = int.Parse(e[4]);
            race.MineBuildCost = int.Parse(e[5]);
            race.OperableMines = int.Parse(e[6]);

            TechLevel.ResearchField[] fields =
            {
                TechLevel.ResearchField.Energy, TechLevel.ResearchField.Weapons, TechLevel.ResearchField.Propulsion,
                TechLevel.ResearchField.Construction, TechLevel.ResearchField.Electronics, TechLevel.ResearchField.Biotechnology
            };
            for (int i = 0; i < 6; i++)
            {
                race.ResearchCosts[fields[i]] = research[i] == '+' ? 175 : research[i] == '-' ? 50 : 100;
            }

            Assert.AreEqual(expected, calculator.calculateAdvantagePoints(race));
        }

        private static void ApplyBand(EnvironmentTolerance tolerance, string band)
        {
            if (band == "imm")
            {
                tolerance.Immune = true;
                return;
            }
            string[] parts = band.Split('-');
            SetBand(tolerance, int.Parse(parts[0]), int.Parse(parts[1]));
        }
    }
}
