#region Copyright Notice
// ============================================================================
// Copyright (C) 2009 - 2017 stars-nova
//
// This file is part of Stars-Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// One of the 24 built-in AI race templates (behavior-specs-10/ai-opponent-behavior.md §14,
    /// `FUN_1078_7aca`: template archetype × 4 + tier). Bands are on the 0-100 click scale ("imm"
    /// for immune); economy is colonists per resource (for Alternate Reality the income divisor ×
    /// 100, its factory and mine slots being ignored), factory output / cost / operated per
    /// 10,000, mine output / cost / operated per 10,000; research is E W P C El B with "+"
    /// expensive (175%), "n" normal, "-" cheap (50%).
    /// </summary>
    public sealed class AiRaceTemplate
    {
        public int Index;
        public int Archetype;
        public int Tier;
        public string PrimaryTrait;
        public string[] LesserTraits;
        public string Gravity;
        public string Temperature;
        public string Radiation;
        public int GrowthRate;
        public int[] Economy;
        public string Research;

        /// <summary>The "expensive research starts at level 3" checkbox (trait bit 29, Nova's
        /// "ExtraTech").</summary>
        public bool ExpensiveTechStartsAtThree;

        /// <summary>The "factories cost 1 kT less Germanium" checkbox (trait bit 31, Nova's
        /// "CF").</summary>
        public bool CheapFactoryGermanium;

        /// <summary>The leftover-points choice, in the spelling the home-world adjuster
        /// reads.</summary>
        public string LeftoverPointTarget;

        /// <summary>The advantage-point figure the wizard would show (§14's resolved
        /// table).</summary>
        public int ExpectedAdvantagePoints;

        public string Name
        {
            get { return AiRaceTemplates.ArchetypeNames[Archetype] + " " + AiRaceTemplates.TierNames[Tier]; }
        }
    }

    /// <summary>
    /// The 24 AI race templates of §14 and the setup-side rules of §1a: tier 4 ("Random") draws
    /// 0-3 and archetype 6 ("Random") draws 0-5; the template is copied unvalidated (14 of them
    /// would be illegal for a human); the computer flag, skill = tier and category = archetype go
    /// with it. Templates hold no designs and no name (the 24 lore names are a separate pool), so
    /// <see cref="CreateRace"/> names the race after its archetype.
    /// </summary>
    /// <remarks>
    /// Wiring for a new-game screen: put <c>CreateRace(archetype, tier)</c> into the known-races
    /// dictionary passed to <c>Gameinitializer.Initialize</c> under the race's name and give the
    /// player row that RaceName and an AI AiProgram. The AI side recovers archetype and tier from
    /// the race itself with <see cref="TryIdentify"/> (no save-format change).
    /// </remarks>
    public static class AiRaceTemplates
    {
        public const int Easy = 0;
        public const int Standard = 1;
        public const int Tough = 2;
        public const int Expert = 3;

        /// <summary>The setup menu's "Random" entries (§1a).</summary>
        public const int RandomTier = 4;
        public const int RandomArchetype = 6;

        public const string SurfaceMinerals = "Surface minerals";
        public const string MineralConcentration = "Mineral concentration";

        public static readonly string[] ArchetypeNames = { "Robotoids", "Turindrones", "Automitrons", "Rototills", "Cybertrons", "Macinti" };

        public static readonly string[] TierNames = { "Easy", "Standard", "Tough", "Expert" };

        private static readonly AiRaceTemplate[] Templates =
        {
            T(0, "HE", "IFE MA CE OBRM BET", "imm", "imm", "imm", 5, "1000 12 10 16 10 5 10", "nn-nn+", "", SurfaceMinerals, 963),
            T(1, "HE", "IFE MA CE OBRM", "imm", "imm", "imm", 6, "900 13 9 16 10 4 11", "nn-nn+", "", SurfaceMinerals, 79),
            T(2, "HE", "IFE UR MA OBRM", "imm", "imm", "imm", 6, "800 13 9 18 10 4 12", "n---n+", "CF", SurfaceMinerals, -611),
            T(3, "HE", "IFE UR MA OBRM", "imm", "imm", "imm", 7, "800 13 9 16 10 4 8", "n-n-n+", "CF", SurfaceMinerals, -1161),
            T(4, "SS", "IFE ARM MA RS", "27-89", "7-63", "35-95", 14, "1000 9 10 9 9 5 8", "n+nnn+", "", SurfaceMinerals, 289),
            T(5, "SS", "IFE ARM MA RS", "32-92", "6-60", "26-96", 14, "1000 10 10 10 10 5 9", "nnnnnn", "CF", SurfaceMinerals, 13),
            T(6, "SS", "IFE ARM MA RS", "31-95", "4-52", "30-94", 14, "900 11 10 10 10 5 9", "+n+nnn", "CF", SurfaceMinerals, -75),
            T(7, "SS", "IFE ARM MA RS", "31-93", "5-53", "imm", 15, "800 15 10 25 10 5 9", "++++++", "ExtraTech CF", SurfaceMinerals, -1173),
            T(8, "IS", "GR CE OBRM NAS LSP", "7-63", "26-94", "5-71", 15, "900 11 10 14 11 6 14", "++++++", "ExtraTech", MineralConcentration, 450),
            T(9, "IS", "GR CE OBRM NAS LSP", "7-63", "26-94", "5-71", 15, "800 13 9 14 10 6 14", "++++++", "ExtraTech CF", MineralConcentration, 96),
            T(10, "IS", "GR OBRM NAS LSP", "7-63", "26-94", "5-71", 15, "800 14 9 15 14 5 15", "++++++", "ExtraTech CF", MineralConcentration, -289),
            T(11, "IS", "GR OBRM NAS LSP", "7-63", "imm", "0-100", 16, "800 14 9 14 14 5 14", "++++++", "ExtraTech CF", MineralConcentration, -1211),
            T(12, "CA", "TT CE OBRM NAS LSP BET", "32-68", "31-69", "31-69", 15, "1000 10 10 10 10 5 10", "+++++-", "ExtraTech", MineralConcentration, 821),
            T(13, "CA", "TT OBRM NAS LSP BET", "32-68", "31-69", "31-69", 15, "800 12 10 12 14 5 12", "+++++-", "ExtraTech", MineralConcentration, 1),
            T(14, "CA", "TT OBRM NAS LSP BET", "23-77", "24-76", "25-75", 15, "800 12 10 12 14 5 12", "+++++-", "ExtraTech", MineralConcentration, -173),
            T(15, "CA", "TT OBRM NAS LSP BET", "imm", "24-76", "25-75", 15, "800 15 10 15 15 5 15", "+++++-", "ExtraTech", MineralConcentration, -971),
            T(16, "PP", "IFE TT OBRM LSP", "22-78", "22-78", "22-78", 12, "1000 9 18 9 9 10 8", "n++n++", "ExtraTech", MineralConcentration, 832),
            T(17, "PP", "IFE TT OBRM NAS LSP", "19-81", "19-81", "19-81", 17, "1000 10 13 19 10 10 7", "n++nnn", "ExtraTech", MineralConcentration, -2),
            T(18, "PP", "IFE TT MA OBRM NAS LSP", "18-82", "18-82", "18-82", 17, "1000 14 10 20 10 10 6", "nn+nn-", "ExtraTech CF", MineralConcentration, -608),
            T(19, "PP", "IFE TT MA OBRM NAS LSP", "17-83", "17-83", "17-83", 19, "1000 15 9 25 10 10 5", "--+-nn", "ExtraTech CF", MineralConcentration, -1246),
            T(20, "AR", "IFE TT ISB GR CE", "20-80", "20-80", "20-80", 10, "1600 10 10 10 10 5 10", "nnnn+n", "", SurfaceMinerals, 480),
            T(21, "AR", "IFE TT ISB GR", "15-85", "15-85", "15-85", 14, "1200 10 10 10 10 5 10", "-nnn+n", "", SurfaceMinerals, -169),
            T(22, "AR", "IFE TT ARM ISB GR UR MA", "15-85", "15-85", "15-85", 17, "1000 10 10 10 10 5 10", "-nnnnn", "", SurfaceMinerals, -824),
            T(23, "AR", "IFE TT ARM ISB GR UR MA", "15-85", "15-85", "15-85", 20, "1000 10 10 10 10 5 10", "-nn-nn", "", SurfaceMinerals, -1287),
        };

        private static readonly TechLevel.ResearchField[] ResearchOrder =
        {
            TechLevel.ResearchField.Energy, TechLevel.ResearchField.Weapons, TechLevel.ResearchField.Propulsion,
            TechLevel.ResearchField.Construction, TechLevel.ResearchField.Electronics, TechLevel.ResearchField.Biotechnology
        };

        public static IReadOnlyList<AiRaceTemplate> All
        {
            get { return Templates; }
        }

        /// <summary>Template archetype × 4 + tier.</summary>
        public static AiRaceTemplate Get(int archetype, int tier)
        {
            if (archetype < 0 || archetype >= ArchetypeNames.Length || tier < 0 || tier >= TierNames.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(archetype), "archetype 0-5 and tier 0-3");
            }

            return Templates[(archetype * 4) + tier];
        }

        /// <summary>§1a step 1: a "Random" tier (4) becomes a uniform 0-3 draw and a "Random"
        /// archetype (6) a uniform 0-5 draw; the tier is drawn first.</summary>
        public static void ResolveRandom(ref int archetype, ref int tier, Random random)
        {
            if (tier == RandomTier)
            {
                tier = random.Next(4);
            }

            if (archetype == RandomArchetype)
            {
                archetype = random.Next(6);
            }
        }

        /// <summary>A fresh race holding the template's settings.</summary>
        public static Race CreateRace(int archetype, int tier)
        {
            return CreateRace(Get(archetype, tier));
        }

        public static Race CreateRace(AiRaceTemplate template)
        {
            Race race = new Race();
            race.Name = ArchetypeNames[template.Archetype];
            race.PluralName = ArchetypeNames[template.Archetype];
            race.Traits.SetPrimary(template.PrimaryTrait);
            foreach (string code in template.LesserTraits)
            {
                race.Traits.Add(code);
            }

            if (template.ExpensiveTechStartsAtThree)
            {
                race.Traits.Add("ExtraTech");
            }

            if (template.CheapFactoryGermanium)
            {
                race.Traits.Add("CF");
            }

            ApplyBand(race.GravityTolerance, template.Gravity);
            ApplyBand(race.TemperatureTolerance, template.Temperature);
            ApplyBand(race.RadiationTolerance, template.Radiation);
            race.GrowthRate = template.GrowthRate;

            race.ColonistsPerResource = template.Economy[0];
            race.FactoryProduction = template.Economy[1];
            race.FactoryBuildCost = template.Economy[2];
            race.OperableFactories = template.Economy[3];
            race.MineProductionRate = template.Economy[4];
            race.MineBuildCost = template.Economy[5];
            race.OperableMines = template.Economy[6];

            for (int i = 0; i < ResearchOrder.Length; i++)
            {
                race.ResearchCosts[ResearchOrder[i]] = ResearchCost(template.Research[i]);
            }

            race.LeftoverPointTarget = template.LeftoverPointTarget;
            return race;
        }

        /// <summary>
        /// Recovers the template a race was created from by comparing its settings (name aside),
        /// so an AI can learn its archetype (category) and tier (skill) from its own race.
        /// </summary>
        public static bool TryIdentify(Race race, out int archetype, out int tier)
        {
            foreach (AiRaceTemplate template in Templates)
            {
                if (Matches(race, CreateRace(template)))
                {
                    archetype = template.Archetype;
                    tier = template.Tier;
                    return true;
                }
            }

            archetype = -1;
            tier = -1;
            return false;
        }

        private static bool Matches(Race race, Race template)
        {
            if (race == null || race.Traits.Primary is null || race.Traits.Primary.Code != template.Traits.Primary.Code)
            {
                return false;
            }

            if (!TraitCodes(race).SetEquals(TraitCodes(template)))
            {
                return false;
            }

            if (!SameBand(race.GravityTolerance, template.GravityTolerance)
                || !SameBand(race.TemperatureTolerance, template.TemperatureTolerance)
                || !SameBand(race.RadiationTolerance, template.RadiationTolerance))
            {
                return false;
            }

            if ((int)Math.Round(race.GrowthRate) != (int)Math.Round(template.GrowthRate)
                || race.ColonistsPerResource != template.ColonistsPerResource
                || race.FactoryProduction != template.FactoryProduction
                || race.FactoryBuildCost != template.FactoryBuildCost
                || race.OperableFactories != template.OperableFactories
                || race.MineProductionRate != template.MineProductionRate
                || race.MineBuildCost != template.MineBuildCost
                || race.OperableMines != template.OperableMines)
            {
                return false;
            }

            return ResearchOrder.All(field => race.ResearchCosts[field] == template.ResearchCosts[field]);
        }

        /// <summary>RacialTraits' own enumerator yields the primary then every lesser trait
        /// (the IEnumerable one of its DictionaryBase would yield DictionaryEntry values).</summary>
        private static HashSet<string> TraitCodes(Race race)
        {
            HashSet<string> codes = new HashSet<string>();
            foreach (TraitEntry trait in race.Traits)
            {
                codes.Add(trait.Code);
            }

            return codes;
        }

        private static bool SameBand(EnvironmentTolerance a, EnvironmentTolerance b)
        {
            if (a.Immune || b.Immune)
            {
                return a.Immune == b.Immune;
            }

            return a.MinimumValue == b.MinimumValue && a.MaximumValue == b.MaximumValue;
        }

        private static int ResearchCost(char code)
        {
            return code == '+' ? 175 : code == '-' ? 50 : 100;
        }

        private static void ApplyBand(EnvironmentTolerance tolerance, string band)
        {
            if (band == "imm")
            {
                tolerance.Immune = true;
                return;
            }

            string[] parts = band.Split('-');
            tolerance.Immune = false;
            tolerance.MinimumValue = int.Parse(parts[0], CultureInfo.InvariantCulture);
            tolerance.MaximumValue = int.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        private static AiRaceTemplate T(int index, string prt, string lrts, string gravity, string temperature, string radiation,
            int growth, string economy, string research, string checkboxes, string leftover, int points)
        {
            string[] boxes = checkboxes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return new AiRaceTemplate
            {
                Index = index,
                Archetype = index / 4,
                Tier = index % 4,
                PrimaryTrait = prt,
                LesserTraits = lrts.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                Gravity = gravity,
                Temperature = temperature,
                Radiation = radiation,
                GrowthRate = growth,
                Economy = economy.Split(' ').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray(),
                Research = research,
                ExpensiveTechStartsAtThree = boxes.Contains("ExtraTech"),
                CheapFactoryGermanium = boxes.Contains("CF"),
                LeftoverPointTarget = leftover,
                ExpectedAdvantagePoints = points,
            };
        }
    }
}
