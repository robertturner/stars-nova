namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;

    /// <summary>One of the race wizard's eight Identity-stage archetype buttons.</summary>
    public sealed class RacePreset
    {
        public RacePreset(string name, string primaryTrait, params int[] economySlots)
        {
            Name = name;
            PrimaryTrait = primaryTrait;
            EconomySlots = economySlots;
        }

        /// <summary>The button label (race-designer-ui-and-availability.md, "Identity and archetype stage").</summary>
        public string Name { get; }

        /// <summary>The preset's PRT code; null for Random and Custom.</summary>
        public string PrimaryTrait { get; }

        /// <summary>Economic slots 0-6 as stored (slot 0 in hundreds of colonists); empty for Random and Custom.</summary>
        public int[] EconomySlots { get; }

        public bool IsRandom => Name == RacePresets.RandomName;

        public bool IsCustom => Name == RacePresets.CustomName;

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// The eight Identity-stage archetypes (behavior-specs-10/race-designer-ui-and-availability.md:
    /// "Humanoid / Rabbitoid / Insectoid / Nucleotid / Silicanoid / Antetheral / Random / Custom")
    /// and the data the spec gives for them: the seven economic slots and the PRT of the six named
    /// presets ("The other named presets, as stored"), plus Humanoid's full default column.
    /// </summary>
    /// <remarks>
    /// SPEC GAP: the spec says selecting a preset "replaces the draft's race-design fields with that
    /// preset's complete configuration", but it records only slots 0-6 and the PRT for each named
    /// preset (and slots 7-13 and the Germanium checkbox for Humanoid alone). The habitability
    /// bands, growth rate and lesser traits of every preset, and the research classes and
    /// leftover choice of the five non-Humanoid presets, are not given, so applying a preset leaves
    /// those draft fields as they were.
    /// </remarks>
    public static class RacePresets
    {
        public const string RandomName = "Random";
        public const string CustomName = "Custom";

        public static readonly RacePreset Humanoid = new RacePreset("Humanoid", "JOAT", 10, 10, 10, 10, 10, 5, 10);

        /// <summary>All eight, in button order; Random is the 7th (index 6) and Custom the 8th.</summary>
        public static readonly IReadOnlyList<RacePreset> All = new[]
        {
            Humanoid,
            new RacePreset("Rabbitoid", "IT", 10, 10, 9, 17, 10, 9, 10),
            new RacePreset("Insectoid", "WM", 10, 10, 10, 10, 9, 10, 6),
            // Nucleotid's stored "mines per 10,000 colonists" of 3 is below that slot's minimum
            // of 5; the shared clamp (applied on every store and by the whole-record validation
            // pass) raises it to 5. Reported as a spec inconsistency.
            new RacePreset("Nucleotid", "SS", 9, 10, 10, 10, 15, 5, 3),
            new RacePreset("Silicanoid", "HE", 8, 12, 12, 15, 10, 9, 10),
            new RacePreset("Antetheral", "SD", 7, 11, 10, 18, 10, 10, 10),
            new RacePreset(RandomName, null),
            new RacePreset(CustomName, null),
        };

        /// <summary>
        /// Applies a named preset to the draft: slots 0-6 through the shared clamp and the PRT;
        /// for Humanoid also its default research classes (all standard), leftover choice
        /// (Surface minerals) and Germanium checkbox (off). An empty name gets the preset's name
        /// as generic identity text (the spec does not give that text; the preset label stands in,
        /// for the plural too). Custom keeps the draft; Random is <see cref="RandomRaceGenerator"/>.
        /// </summary>
        public static void Apply(RacePreset preset, Race race)
        {
            if (preset.IsCustom || preset.IsRandom)
            {
                return;
            }

            for (int slot = 0; slot < preset.EconomySlots.Length; slot++)
            {
                RaceDesignerRules.SetSlot(race, slot, preset.EconomySlots[slot]);
            }

            race.Traits.SetPrimary(preset.PrimaryTrait);

            if (ReferenceEquals(preset, Humanoid))
            {
                foreach (TechLevel.ResearchField field in RaceDesignerRules.ResearchSlotOrder)
                {
                    race.ResearchCosts[field] = 100;
                }

                race.LeftoverPointTarget = RaceDesignerRules.LeftoverPointTargets[0];
                RaceDesignerRules.SetLesserTrait(race, RaceDesignerRules.CheapFactories, false);
            }

            FillEmptyIdentity(race, preset.Name);
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
