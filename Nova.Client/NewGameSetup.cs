namespace Nova.Client
{
    using System;
    using System.Collections.Generic;

    using Nova.Common;

    /// <summary>
    /// Pure new-game setup rules shared by the New Game screen (Nova.Avalonia's NewGameViewModel)
    /// and its tests: the Simplified path's player-count table, year-gate seeding and the state
    /// its OK button writes (behavior-specs-11/new-game-setup.md section 2, victory-conditions.md
    /// section 1), the reset-to-defaults routine (new-game-setup.md section 8) and the small
    /// helpers the AI opponent picker needs (ai-opponent-behavior.md section 1a).
    /// </summary>
    public static class NewGameSetup
    {
        /// <summary>The largest player count the setup accepts (new-game-setup.md section 4: "Player count 1 - 16").</summary>
        public const int MaximumPlayers = 16;

        /// <summary>The Simplified dialog's difficulty radio buttons (new-game-setup.md section 2): Easy, Standard, Harder, Expert = 0-3.</summary>
        public const int Easy = 0;
        public const int Standard = 1;
        public const int Harder = 2;
        public const int Expert = 3;

        /// <summary>The Simplified dialog's default difficulty (Standard) and galaxy size (Small).</summary>
        public const int DefaultDifficulty = Standard;
        public const GalaxySize DefaultSimplifiedSize = GalaxySize.Small;

        /// <summary>
        /// One "1 in n" draw of the Simplified player-count table: a hit (the uniform 0 to n - 1
        /// draw giving 0) decides on one of <see cref="Outcomes"/>, with equal chances when there
        /// are several.
        /// </summary>
        private readonly struct Chance
        {
            public Chance(int oneIn, params int[] outcomes)
            {
                OneIn = oneIn;
                Outcomes = outcomes;
            }

            public int OneIn { get; }

            public int[] Outcomes { get; }
        }

        /// <summary>
        /// The Simplified player-count table of new-game-setup.md section 2, by size (Tiny ..
        /// Huge) then difficulty (Easy, Standard, Harder, Expert): the draws in order, the first
        /// hit deciding, and the base count otherwise. Easy and Standard make no draw.
        /// </summary>
        private static readonly (Chance[] Draws, int Otherwise)[][] SimplifiedPlayerCountTable =
        {
            // Tiny
            new[]
            {
                (new Chance[0], 2),
                (new Chance[0], 2),
                (new Chance[0], 2),
                (new[] { new Chance(3, 3) }, 2),
            },
            // Small
            new[]
            {
                (new Chance[0], 3),
                (new Chance[0], 3),
                (new[] { new Chance(4, 4) }, 3),
                (new[] { new Chance(4, 5), new Chance(3, 4) }, 3),
            },
            // Medium
            new[]
            {
                (new Chance[0], 7),
                (new Chance[0], 7),
                (new[] { new Chance(5, 8), new Chance(5, 6) }, 7),
                (new[] { new Chance(10, 9), new Chance(10, 5), new Chance(4, 8), new Chance(4, 6) }, 7),
            },
            // Large
            new[]
            {
                (new Chance[0], 12),
                (new Chance[0], 12),
                (new[] { new Chance(5, 13), new Chance(5, 11) }, 12),
                (new[] { new Chance(10, 14, 15), new Chance(10, 9, 10), new Chance(4, 13), new Chance(4, 11) }, 12),
            },
            // Huge
            new[]
            {
                (new Chance[0], 16),
                (new Chance[0], 16),
                (new[] { new Chance(7, 14), new Chance(5, 15) }, 16),
                (new[] { new Chance(10, 11, 12, 13), new Chance(6, 14), new Chance(4, 15) }, 16),
            },
        };

        /// <summary>
        /// The Simplified path's year gate (new-game-setup.md section 2, victory-conditions.md
        /// section 1): the raw value is 2 x the size index, read back as (raw + 3) x 10 years -
        /// Tiny 30, Small 50, Medium 70, Large 90, Huge 110 (no clamp; the field's maximum raw
        /// value is 47).
        /// </summary>
        public const int SimplifiedYearGateRawPerSizeStep = 2;

        /// <summary>The year-gate formula of victory-conditions.md section 1: (raw + 3) x 10.</summary>
        public static int YearGateYears(int raw)
        {
            return (raw + 3) * 10;
        }

        /// <summary>The Simplified path's auto-seeded year gate for a galaxy size.</summary>
        public static int SimplifiedYearGate(GalaxySize size)
        {
            return YearGateYears(SimplifiedYearGateRawPerSizeStep * (int)size);
        }

        /// <summary>
        /// The Simplified path's player count, the human included (new-game-setup.md section 2,
        /// `FUN_1078_53c4`): the table's draws for the galaxy size and difficulty are made in
        /// order, each "1 in n" a separate uniform 0 to n - 1 draw hitting on 0, and the first
        /// hit decides; otherwise the base count. Easy and Standard always give 2 / 3 / 7 / 12 /
        /// 16 for Tiny .. Huge; only Expert can go below the base. Capped at
        /// <see cref="MaximumPlayers"/>.
        /// </summary>
        /// <remarks>
        /// A hit with several outcomes ("14 or 15, even chance"; "11, 12 or 13, equal chances")
        /// is decided by one more uniform draw among them; how the original splits that chance
        /// is not given, so only an exact replay of its draws could differ.
        /// </remarks>
        public static int ChooseSimplifiedPlayerCount(GalaxySize size, int difficulty, Random random)
        {
            (Chance[] draws, int otherwise) = SimplifiedPlayerCountTable[(int)size][ClampDifficulty(difficulty)];

            foreach (Chance chance in draws)
            {
                if (random.Next(chance.OneIn) == 0)
                {
                    int outcome = chance.Outcomes.Length == 1 ? chance.Outcomes[0] : chance.Outcomes[random.Next(chance.Outcomes.Length)];
                    return Math.Min(MaximumPlayers, outcome);
                }
            }

            return Math.Min(MaximumPlayers, otherwise);
        }

        /// <summary>The smallest and largest player count <see cref="ChooseSimplifiedPlayerCount"/> can give for a size and difficulty.</summary>
        public static (int Min, int Max) SimplifiedPlayerCountRange(GalaxySize size, int difficulty)
        {
            (Chance[] draws, int otherwise) = SimplifiedPlayerCountTable[(int)size][ClampDifficulty(difficulty)];
            int min = otherwise;
            int max = otherwise;
            foreach (Chance chance in draws)
            {
                foreach (int outcome in chance.Outcomes)
                {
                    min = Math.Min(min, outcome);
                    max = Math.Max(max, outcome);
                }
            }

            return (min, Math.Min(MaximumPlayers, max));
        }

        /// <summary>
        /// The Starting Distance the Simplified dialog writes (new-game-setup.md section 2):
        /// Moderate for Easy and Standard, Farther for Harder and Expert.
        /// </summary>
        public static StartingDistance SimplifiedStartingDistance(int difficulty)
        {
            return ClampDifficulty(difficulty) >= Harder ? StartingDistance.Farther : StartingDistance.Moderate;
        }

        /// <summary>
        /// What the Simplified dialog's OK (and Advanced Game) writes to the galaxy settings
        /// (new-game-setup.md section 2), keeping the chosen galaxy size: density Normal,
        /// Starting Distance by difficulty (<see cref="SimplifiedStartingDistance"/>), and the
        /// option flags Maximum Minerals, Slower Tech Advances, Accelerated BBS Play, No Random
        /// Events and Galaxy Clumping cleared. The player count, computer slots and year gate are
        /// the caller's (<see cref="ChooseSimplifiedPlayerCount"/>, <see cref="SimplifiedYearGate"/>).
        /// </summary>
        /// <remarks>
        /// "Public Player Scores" (cleared) and "Computer Players Form Alliances" (set only for
        /// Expert) have no GameSettings flag yet, so neither is written. The twenty fixed game
        /// titles (dynamic strings 459 + 5 x difficulty + size) are not available (spec gap), so
        /// the game name is left alone.
        /// </remarks>
        public static void ApplySimplifiedDefaults(GameSettings settings, int difficulty)
        {
            settings.ApplyGalaxyPreset(settings.GalaxySizeSetting, GalaxyDensity.Normal);
            settings.StartingDistanceSetting = SimplifiedStartingDistance(difficulty);

            settings.MaximumMinerals = false;
            settings.SlowTechAdvance = false;
            settings.AcceleratedStart = false;
            settings.NoRandomEvents = false;
            settings.GalaxyClumping = false;
        }

        /// <summary>
        /// "Reset settings to defaults": the original has no such command (new-game-setup.md
        /// section 8), so this port's button restores the Simplified dialog's own state for the
        /// chosen difficulty, as that section recommends - its default galaxy size (Small) with
        /// <see cref="ApplySimplifiedDefaults"/> (density Normal, Starting Distance Moderate for
        /// Easy and Standard or Farther for Harder and Expert, every option flag cleared) - and
        /// the victory defaults of victory-conditions.md section 1 read off the real dialog
        /// (planets 60% on, tech 22 in 4 fields on, score 11000 off, second place 100 on,
        /// production 100 off, capital ships 100 off, highest score 100 off, 1 condition, year
        /// gate 50). The free map sliders go back to their shipped values.
        /// </summary>
        /// <remarks>
        /// Game name, folder, seed and the player list are not settings of the general page and
        /// are left alone.
        /// </remarks>
        public static void ResetToDefaults(GameSettings settings, int difficulty = DefaultDifficulty)
        {
            settings.GalaxySizeSetting = DefaultSimplifiedSize;
            ApplySimplifiedDefaults(settings, difficulty);

            settings.StarSeparation = 10;
            settings.StarDensity = 40;
            settings.StarUniformity = 60;

            Set(settings.PlanetsOwned, true, 60);
            Set(settings.TechLevels, true, 22);
            Set(settings.NumberOfFields, true, 4);
            Set(settings.TotalScore, false, 11000);
            Set(settings.SecondPlaceScore, true, 100);
            Set(settings.ProductionCapacity, false, 100);
            Set(settings.CapitalShips, false, 100);
            Set(settings.HighestScore, false, 100);
            settings.TargetsToMeet = 1;
            settings.MinimumGameTime = 50;
        }

        /// <summary>
        /// A display name for a computer player's template race that collides with none of
        /// <paramref name="takenNames"/>: the base name itself, else "base 2", "base 3" ...
        /// </summary>
        /// <remarks>
        /// SPEC GAP: the original gives each computer player a random name from a 24-entry lore
        /// pool (dynamic strings 1390-1413, ai-opponent-behavior.md section 1a), whose texts the
        /// spec does not list. The template's archetype name plus a numeric suffix stands in.
        /// </remarks>
        public static string UniqueRaceName(string baseName, ICollection<string> takenNames)
        {
            if (!takenNames.Contains(baseName))
            {
                return baseName;
            }

            for (int suffix = 2; ; suffix++)
            {
                string candidate = baseName + " " + suffix;
                if (!takenNames.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        /// <summary>
        /// The <c>-n</c> personality code DefaultAi takes for a spec AI category: the inverse of
        /// Nova.Ai.AiCategory.ForPersonality (categories 0-5 are codes 2-7, category 6 the
        /// disabled code 0, category 7 the passive code 1). The literals mirror
        /// DefaultAi.DisabledPersonality / PassivePersonality / MinStandardPersonality, which
        /// this assembly cannot reference (Nova.Ai references Nova.Client);
        /// NewGameSetupTest pins the round trip.
        /// </summary>
        public static int PersonalityCodeForCategory(int category)
        {
            if (category == 6)
            {
                return 0;
            }

            if (category == 7)
            {
                return 1;
            }

            return Math.Max(0, Math.Min(5, category)) + 2;
        }

        private static int ClampDifficulty(int difficulty)
        {
            return Math.Max(Easy, Math.Min(Expert, difficulty));
        }

        private static void Set(EnabledValue value, bool isChecked, int numeric)
        {
            value.IsChecked = isChecked;
            value.NumericValue = numeric;
        }
    }
}
