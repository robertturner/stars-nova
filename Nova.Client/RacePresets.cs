namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Client.Shell;
    using Nova.Common;

    /// <summary>
    /// One environmental axis of a preset's stored habitability: either the immunity state, or an
    /// inclusive lower/upper band on the normalized 0-100 scale (race-designer-ui-and-availability.md,
    /// "The seven preset records"). The centre is always the midpoint, so it is derived.
    /// </summary>
    public readonly struct HabitabilityBand
    {
        public HabitabilityBand(int minimum, int maximum)
        {
            Immune = false;
            Minimum = minimum;
            Maximum = maximum;
        }

        private HabitabilityBand(bool immune)
        {
            Immune = immune;
            Minimum = 0;
            Maximum = 100;
        }

        /// <summary>The special full-tolerance state ("immune").</summary>
        public static HabitabilityBand Immunity { get; } = new HabitabilityBand(true);

        public bool Immune { get; }

        public int Minimum { get; }

        public int Maximum { get; }
    }

    /// <summary>One of the race wizard's eight Identity-stage archetype buttons and its complete stored record.</summary>
    public sealed class RacePreset
    {
        public RacePreset(
            string name,
            string primaryTrait,
            HabitabilityBand gravity,
            HabitabilityBand temperature,
            HabitabilityBand radiation,
            double growthRate,
            int[] economySlots,
            string[] lesserTraits,
            bool germaniumDiscount,
            bool extraTechStart,
            int leftoverTarget,
            int[] researchClasses,
            int portraitIndex,
            bool randomizeAtUniverseCreation = false)
        {
            Name = name;
            PrimaryTrait = primaryTrait;
            Gravity = gravity;
            Temperature = temperature;
            Radiation = radiation;
            GrowthRate = growthRate;
            EconomySlots = economySlots;
            LesserTraits = lesserTraits;
            GermaniumDiscount = germaniumDiscount;
            ExtraTechStart = extraTechStart;
            LeftoverTarget = leftoverTarget;
            ResearchClasses = researchClasses;
            PortraitIndex = portraitIndex;
            RandomizeAtUniverseCreation = randomizeAtUniverseCreation;
        }

        /// <summary>The button label (race-designer-ui-and-availability.md, "Identity and archetype stage").</summary>
        public string Name { get; }

        /// <summary>The preset's PRT code; null for Custom.</summary>
        public string PrimaryTrait { get; }

        public HabitabilityBand Gravity { get; }

        public HabitabilityBand Temperature { get; }

        public HabitabilityBand Radiation { get; }

        /// <summary>The stored growth rate, in percent.</summary>
        public double GrowthRate { get; }

        /// <summary>Economic slots 0-6 as stored (slot 0 in hundreds of colonists); empty for Custom.</summary>
        public int[] EconomySlots { get; }

        /// <summary>The lesser-trait bits that are set, as Nova codes in wizard bit order 0-13.</summary>
        public string[] LesserTraits { get; }

        /// <summary>Trait bit 31 ("factories cost 1 kT less Germanium"), the Germanium-discount checkbox.</summary>
        public bool GermaniumDiscount { get; }

        /// <summary>Trait bit 29 ("expensive research starts at tech 3, or 4 for JOAT"), the tech-3 checkbox.</summary>
        public bool ExtraTechStart { get; }

        /// <summary>The leftover-points choice as an index into <see cref="RaceDesignerRules.LeftoverPointTargets"/>.</summary>
        public int LeftoverTarget { get; }

        /// <summary>Research slots 8-13 as stored cost classes: 0 = 75% extra, 1 = standard, 2 = 50% less.</summary>
        public int[] ResearchClasses { get; }

        /// <summary>The spec's portrait index (1-32 on the original portrait sheet).</summary>
        public int PortraitIndex { get; }

        /// <summary>The Random placeholder's trait bit 30 ("randomise at universe creation"). No other preset sets it.</summary>
        public bool RandomizeAtUniverseCreation { get; }

        /// <summary>True for the six named presets and Random, false for Custom (which has no stored record).</summary>
        public bool HasRecord => !IsCustom;

        public bool IsRandom => Name == RacePresets.RandomName;

        public bool IsCustom => Name == RacePresets.CustomName;

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// The eight Identity-stage archetypes (race-designer-ui-and-availability.md:
    /// "Humanoid / Rabbitoid / Insectoid / Nucleotid / Silicanoid / Antetheral / Random / Custom")
    /// and their complete stored records from "The seven preset records": habitability bands,
    /// growth rate, PRT, the 14 lesser-trait bits, the two flat-cost checkboxes, the leftover-points
    /// choice, the seven economy values, the six research classes and the portrait index, with the
    /// advantage-point totals 25 / 32 / 43 / 11 / 9 / 7 and Random's 12.
    /// </summary>
    public static class RacePresets
    {
        public const string RandomName = "Random";
        public const string CustomName = "Custom";

        private static readonly HabitabilityBand Immune = HabitabilityBand.Immunity;

        public static readonly RacePreset Humanoid = new RacePreset(
            "Humanoid",
            "JOAT",
            new HabitabilityBand(15, 85),
            new HabitabilityBand(15, 85),
            new HabitabilityBand(15, 85),
            15,
            new[] { 10, 10, 10, 10, 10, 5, 10 },
            new string[0],
            false,
            false,
            0,
            new[] { 1, 1, 1, 1, 1, 1 },
            1);

        /// <summary>The Random placeholder record (trait bit 30 set; scored at universe generation).</summary>
        public static readonly RacePreset Random = new RacePreset(
            RandomName,
            "HE",
            new HabitabilityBand(17, 83),
            new HabitabilityBand(17, 83),
            new HabitabilityBand(17, 83),
            15,
            new[] { 10, 10, 10, 10, 10, 3, 10 },
            new string[0],
            false,
            false,
            0,
            new[] { 1, 1, 1, 1, 1, 1 },
            31,
            randomizeAtUniverseCreation: true);

        /// <summary>All eight, in button order; Random is the 7th (index 6) and Custom the 8th.</summary>
        public static readonly IReadOnlyList<RacePreset> All = new[]
        {
            Humanoid,
            new RacePreset(
                "Rabbitoid",
                "IT",
                new HabitabilityBand(10, 56),
                new HabitabilityBand(35, 81),
                new HabitabilityBand(13, 53),
                20,
                new[] { 10, 10, 9, 17, 10, 9, 10 },
                new[] { "IFE", "TT", "CE", "NAS" },
                true,
                false,
                4,
                new[] { 0, 0, 2, 1, 1, 2 },
                12),
            new RacePreset(
                "Insectoid",
                "WM",
                Immune,
                new HabitabilityBand(0, 100),
                new HabitabilityBand(70, 100),
                10,
                new[] { 10, 10, 10, 10, 9, 10, 6 },
                new[] { "ISB", "CE", "RS" },
                false,
                false,
                1,
                new[] { 2, 2, 2, 2, 1, 0 },
                4),
            new RacePreset(
                "Nucleotid",
                "SS",
                Immune,
                new HabitabilityBand(12, 88),
                new HabitabilityBand(0, 100),
                10,
                new[] { 9, 10, 10, 10, 10, 15, 5 },
                new[] { "ARM", "ISB" },
                false,
                true,
                3,
                new[] { 0, 0, 0, 0, 0, 0 },
                25),
            new RacePreset(
                "Silicanoid",
                "HE",
                Immune,
                Immune,
                Immune,
                6,
                new[] { 8, 12, 12, 15, 10, 9, 10 },
                new[] { "IFE", "UR", "OBRM", "BET" },
                false,
                false,
                3,
                new[] { 1, 1, 2, 2, 1, 0 },
                5),
            new RacePreset(
                "Antetheral",
                "SD",
                new HabitabilityBand(0, 30),
                new HabitabilityBand(0, 100),
                new HabitabilityBand(70, 100),
                7,
                new[] { 7, 11, 10, 18, 10, 10, 10 },
                new[] { "ARM", "MA", "NRS", "CE", "NAS" },
                false,
                false,
                0,
                new[] { 2, 0, 2, 2, 2, 2 },
                18),
            Random,
            new RacePreset(
                CustomName,
                null,
                new HabitabilityBand(20, 80),
                new HabitabilityBand(20, 80),
                new HabitabilityBand(20, 80),
                15,
                new int[0],
                new string[0],
                false,
                false,
                0,
                new int[0],
                0),
        };

        /// <summary>
        /// Applies a named preset to the draft (its complete stored record, then the preset's name
        /// and the derived plural where the draft's are empty). Custom keeps the draft. Random is
        /// skipped here: the designer delegates it to <see cref="RandomRaceGenerator"/>, though its
        /// placeholder record is available through <see cref="LoadRecord"/>.
        /// </summary>
        public static void Apply(RacePreset preset, Race race)
        {
            if (preset.IsCustom || preset.IsRandom)
            {
                return;
            }

            LoadRecord(preset, race);

            if (string.IsNullOrWhiteSpace(race.Name))
            {
                race.Name = preset.Name;
            }

            if (string.IsNullOrWhiteSpace(race.PluralName))
            {
                // The spec's identity rule: a preset supplies its name and that name plus "s".
                race.PluralName = RaceNameText.Plural(preset.Name, null);
            }
        }

        /// <summary>
        /// Writes a preset's complete stored record onto the draft: habitability bands, growth,
        /// PRT, the 14 lesser-trait bits, the two flat-cost checkboxes, the leftover-points choice,
        /// the seven economy slots, the six research classes and (via <see cref="PortraitSource"/>)
        /// the portrait. Custom has no record and leaves the draft alone; the name/plural fields are
        /// not part of the record and are untouched (see <see cref="Apply"/>).
        /// </summary>
        public static void LoadRecord(RacePreset preset, Race race)
        {
            if (!preset.HasRecord)
            {
                return;
            }

            ApplyBand(preset.Gravity, race.GravityTolerance);
            ApplyBand(preset.Temperature, race.TemperatureTolerance);
            ApplyBand(preset.Radiation, race.RadiationTolerance);

            race.GrowthRate = preset.GrowthRate;

            for (int slot = 0; slot < preset.EconomySlots.Length; slot++)
            {
                RaceDesignerRules.SetSlot(race, slot, preset.EconomySlots[slot]);
            }

            race.Traits.SetPrimary(preset.PrimaryTrait);

            foreach (string code in RaceDesignerRules.LesserTraitOrder)
            {
                RaceDesignerRules.SetLesserTrait(race, code, false);
            }

            foreach (string code in preset.LesserTraits)
            {
                RaceDesignerRules.SetLesserTrait(race, code, true);
            }

            RaceDesignerRules.SetLesserTrait(race, RaceDesignerRules.CheapFactories, preset.GermaniumDiscount);
            RaceDesignerRules.SetLesserTrait(race, RaceDesignerRules.ExtraTech, preset.ExtraTechStart);

            for (int i = 0; i < RaceDesignerRules.ResearchSlotOrder.Length; i++)
            {
                race.ResearchCosts[RaceDesignerRules.ResearchSlotOrder[i]] =
                    RaceDesignerRules.ResearchCostByClass[preset.ResearchClasses[i]];
            }

            race.LeftoverPointTarget = RaceDesignerRules.LeftoverPointTargets[preset.LeftoverTarget];

            string portrait = PortraitSource(preset.PortraitIndex);
            if (!string.IsNullOrEmpty(portrait))
            {
                race.Icon.Source = portrait;
            }
        }

        /// <summary>
        /// SPEC GAP seam: the preset's portrait index to the icon file name. The spec records the
        /// index ("The seven preset records": Humanoid 1, Rabbitoid 12, Insectoid 4, Nucleotid 25,
        /// Silicanoid 5, Antetheral 18, Random 31) but not the portrait sheet's file order, so no
        /// mapping is applied and the draft's icon is left as it was. The shipped
        /// DefaultRaces/*.race files cannot supply it either: they disagree (Humanoid 06.jpg,
        /// Nucleotid 28.jpg, Insectoid 28.jpg, Silicanoid and Antetheral both 11.jpg).
        /// </summary>
        public static string PortraitSource(int portraitIndex)
        {
            return null;
        }

        private static void ApplyBand(HabitabilityBand band, EnvironmentTolerance tolerance)
        {
            tolerance.Immune = band.Immune;
            tolerance.MinimumValue = band.Minimum;
            tolerance.MaximumValue = band.Maximum;
        }

        internal static void FillEmptyIdentity(Race race, string text)
        {
            if (string.IsNullOrWhiteSpace(race.Name))
            {
                race.Name = text;
            }

            if (string.IsNullOrWhiteSpace(race.PluralName))
            {
                race.PluralName = text;
            }
        }
    }

    /// <summary>The Random archetype's result.</summary>
    public sealed class RandomRaceResult
    {
        public RandomRaceResult(Race race, bool usedFallback, int attempts)
        {
            Race = race;
            UsedFallback = usedFallback;
            Attempts = attempts;
        }

        public Race Race { get; }

        /// <summary>True when no in-window race was reached and the Humanoid fallback was used.</summary>
        public bool UsedFallback { get; }

        /// <summary>Nudge attempts made.</summary>
        public int Attempts { get; }
    }

    /// <summary>
    /// The Random archetype (race-designer-ui-and-availability.md, "A 'Random' archetype option"
    /// and the Random generator's economy step): rolls the environment axes, the economy, the
    /// research classes, the PRT and the trait bits, then nudges the draft (immunity toggles, trait
    /// flips, economy changes) until its advantage points land in 0-50, falling back to the
    /// Humanoid preset if a bounded number of attempts fails.
    /// </summary>
    /// <remarks>
    /// Implemented from the spec: one time in three Humanoid's economy with a uniform 0-4
    /// leftover choice, otherwise every slot 0-7 uniform over its [min, max] (slot 7 can give the
    /// unlabelled 5 or 6, which act as Surface minerals); research all standard one time in three,
    /// otherwise each class uniform 0-2; PRT uniform 0-9; an independent coin flip for each of the
    /// 14 lesser traits; the 0-50 target window; the Humanoid fallback.
    /// SPEC GAPS (stand-ins, see the constants): the environment roll shape (the "difficulty-band
    /// die", the immune chance and the centre/width roll) is not given - each axis gets a uniform
    /// band at least 20 wide and is never immune at first; how bits 29 (tech-3 start) and 31
    /// (Germanium discount) are "set separately" is not given - each gets its own coin flip; the
    /// direction and size of each nudge and the attempt bound are not given - each attempt makes
    /// one random change of the next kind in the order the spec lists and keeps it only if the
    /// total moves toward the window; the generated name comes from a 24-entry lore pool whose
    /// texts are not given - the draft's own name is kept (or "Random" when empty); the growth
    /// rate is not part of the rolled slots and is left as drafted; the fallback record's
    /// habitability and lesser traits are not given - the 20-80 reset band and no lesser traits
    /// stand in.
    /// </remarks>
    public static class RandomRaceGenerator
    {
        public const int TargetMinimumPoints = 0;
        public const int TargetMaximumPoints = 50;

        /// <summary>SPEC GAP stand-in: "a bounded number of attempts" (the bound is not given).</summary>
        public const int MaximumNudgeAttempts = 120;

        /// <summary>The spec's minimum band width for a non-immune axis.</summary>
        public const int MinimumBandWidth = 20;

        /// <summary>
        /// Generates a random race from <paramref name="draft"/> (whose name, plural, emblem,
        /// password and growth rate carry over). <paramref name="points"/> scores a race (the
        /// wizard's advantage-point routine; Race.GetAdvantagePoints in production).
        /// </summary>
        public static RandomRaceResult Generate(Race draft, Random random, Func<Race, int> points)
        {
            Race race = RaceDesignerRules.CopyOf(draft);
            RollEnvironment(race, random);
            RollEconomy(race, random);
            RollResearch(race, random);
            race.Traits.SetPrimary(RaceDesignerRules.PrimaryTraitOrder[random.Next(10)]);
            foreach (string code in RaceDesignerRules.LesserTraitOrder)
            {
                RaceDesignerRules.SetLesserTrait(race, code, random.Next(2) == 1);
            }

            RaceDesignerRules.SetLesserTrait(race, RaceDesignerRules.ExtraTech, random.Next(2) == 1);
            RaceDesignerRules.SetLesserTrait(race, RaceDesignerRules.CheapFactories, random.Next(2) == 1);
            RacePresets.FillEmptyIdentity(race, RacePresets.RandomName);

            int current = points(race);
            int attempts = 0;
            while (Distance(current) > 0 && attempts < MaximumNudgeAttempts)
            {
                Race candidate = RaceDesignerRules.CopyOf(race);
                switch (attempts % 3)
                {
                    case 0:
                        ToggleImmunity(candidate, random);
                        break;
                    case 1:
                        FlipTrait(candidate, random);
                        break;
                    default:
                        AdjustEconomy(candidate, random);
                        break;
                }

                attempts++;
                int candidatePoints = points(candidate);
                if (Distance(candidatePoints) < Distance(current))
                {
                    race = candidate;
                    current = candidatePoints;
                }
            }

            if (Distance(current) == 0)
            {
                return new RandomRaceResult(race, false, attempts);
            }

            return new RandomRaceResult(Fallback(draft), true, attempts);
        }

        /// <summary>The fallback template: the Humanoid preset over the draft (see the remarks for the stand-in fields).</summary>
        public static Race Fallback(Race draft)
        {
            Race race = RaceDesignerRules.CopyOf(draft);
            foreach (EnvironmentTolerance tolerance in new[] { race.GravityTolerance, race.TemperatureTolerance, race.RadiationTolerance })
            {
                tolerance.Immune = false;
                tolerance.MinimumValue = 20;
                tolerance.MaximumValue = 80;
            }

            foreach (string code in RaceDesignerRules.LesserTraitOrder)
            {
                RaceDesignerRules.SetLesserTrait(race, code, false);
            }

            RaceDesignerRules.SetLesserTrait(race, RaceDesignerRules.ExtraTech, false);
            RacePresets.Apply(RacePresets.Humanoid, race);
            return race;
        }

        private static int Distance(int points)
        {
            if (points < TargetMinimumPoints)
            {
                return TargetMinimumPoints - points;
            }

            return points > TargetMaximumPoints ? points - TargetMaximumPoints : 0;
        }

        private static void RollEnvironment(Race race, Random random)
        {
            foreach (EnvironmentTolerance tolerance in new[] { race.GravityTolerance, race.TemperatureTolerance, race.RadiationTolerance })
            {
                RollBand(tolerance, random);
            }
        }

        private static void RollBand(EnvironmentTolerance tolerance, Random random)
        {
            int width = MinimumBandWidth + random.Next(100 - MinimumBandWidth + 1);
            int minimum = random.Next(100 - width + 1);
            tolerance.Immune = false;
            tolerance.MinimumValue = minimum;
            tolerance.MaximumValue = minimum + width;
        }

        private static void RollEconomy(Race race, Random random)
        {
            if (random.Next(3) == 0)
            {
                for (int slot = 0; slot < 7; slot++)
                {
                    RaceDesignerRules.SetSlot(race, slot, RaceDesignerRules.EconomySlots[slot].Default);
                }

                race.LeftoverPointTarget = RaceDesignerRules.LeftoverPointTargets[random.Next(5)];
                return;
            }

            for (int slot = 0; slot < 7; slot++)
            {
                (int min, int max, int _) = RaceDesignerRules.EconomySlots[slot];
                RaceDesignerRules.SetSlot(race, slot, min + random.Next(max - min + 1));
            }

            int leftover = random.Next(RaceDesignerRules.LeftoverSlotMaximum + 1);
            race.LeftoverPointTarget = RaceDesignerRules.LeftoverPointTargets[leftover >= 5 ? 0 : leftover];
        }

        private static void RollResearch(Race race, Random random)
        {
            bool allStandard = random.Next(3) == 0;
            foreach (TechLevel.ResearchField field in RaceDesignerRules.ResearchSlotOrder)
            {
                int costClass = allStandard ? 1 : random.Next(3);
                race.ResearchCosts[field] = RaceDesignerRules.ResearchCostByClass[costClass];
            }
        }

        private static void ToggleImmunity(Race race, Random random)
        {
            EnvironmentTolerance[] axes = { race.GravityTolerance, race.TemperatureTolerance, race.RadiationTolerance };
            EnvironmentTolerance axis = axes[random.Next(3)];
            if (axis.Immune)
            {
                RollBand(axis, random);
            }
            else
            {
                axis.Immune = true;
            }
        }

        private static void FlipTrait(Race race, Random random)
        {
            List<string> codes = RaceDesignerRules.LesserTraitOrder
                .Concat(new[] { RaceDesignerRules.ExtraTech, RaceDesignerRules.CheapFactories })
                .ToList();
            string code = codes[random.Next(codes.Count)];
            RaceDesignerRules.SetLesserTrait(race, code, !RaceDesignerRules.HasLesserTrait(race, code));
        }

        private static void AdjustEconomy(Race race, Random random)
        {
            int slot = random.Next(7);
            int direction = random.Next(2) == 0 ? -1 : 1;
            RaceDesignerRules.SetSlot(race, slot, RaceDesignerRules.GetSlot(race, slot) + direction);
        }
    }
}
