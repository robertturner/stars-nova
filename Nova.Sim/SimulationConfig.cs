namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Ai;
    using Nova.Common;

    /// <summary>
    /// One computer player of a simulated game: the AI race template (archetype x tier, one of the
    /// 24 built-in templates of ai-opponent-behavior.md section 14) and the AI category its
    /// DefaultAi plays (section 1a: category = archetype unless overridden, e.g. 7 for the
    /// economy-only driver or 6 for "no driver").
    /// </summary>
    public sealed class PlayerSpec
    {
        public int Archetype { get; set; }

        public int Tier { get; set; } = AiRaceTemplates.Standard;

        /// <summary>The AI category DefaultAi plays; null means "the archetype" (the New Game
        /// screen's rule).</summary>
        public int? Category { get; set; }

        public PlayerSpec()
        {
        }

        public PlayerSpec(int archetype, int tier, int? category = null)
        {
            Archetype = archetype;
            Tier = tier;
            Category = category;
        }

        public int EffectiveCategory
        {
            get { return Category ?? Archetype; }
        }

        /// <summary>
        /// Parses "archetype[/tier][@category]" where archetype and tier are names
        /// (Robotoids, Standard ...) or numbers (0-5, 0-3); "Random" (or 6 / 4) picks one from
        /// the simulation seed. Examples: "Robotoids", "2/3", "Macinti/Expert@7".
        /// </summary>
        public static PlayerSpec Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new FormatException("empty player spec");
            }

            string body = text.Trim();
            int? category = null;
            int at = body.IndexOf('@');
            if (at >= 0)
            {
                category = int.Parse(body.Substring(at + 1), CultureInfo.InvariantCulture);
                body = body.Substring(0, at);
            }

            string[] parts = body.Split(new[] { '/', ':' }, StringSplitOptions.RemoveEmptyEntries);
            int archetype = ParseIndex(parts[0], AiRaceTemplates.ArchetypeNames, AiRaceTemplates.RandomArchetype);
            int tier = parts.Length > 1 ? ParseIndex(parts[1], AiRaceTemplates.TierNames, AiRaceTemplates.RandomTier) : AiRaceTemplates.Standard;
            return new PlayerSpec(archetype, tier, category);
        }

        private static int ParseIndex(string token, string[] names, int randomValue)
        {
            token = token.Trim();
            if (token.Equals("Random", StringComparison.OrdinalIgnoreCase))
            {
                return randomValue;
            }

            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].Equals(token, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            throw new FormatException("unknown name '" + token + "' (expected one of " + string.Join(", ", names) + ")");
        }

        public override string ToString()
        {
            string archetype = Archetype >= 0 && Archetype < AiRaceTemplates.ArchetypeNames.Length ? AiRaceTemplates.ArchetypeNames[Archetype] : Archetype.ToString(CultureInfo.InvariantCulture);
            string tier = Tier >= 0 && Tier < AiRaceTemplates.TierNames.Length ? AiRaceTemplates.TierNames[Tier] : Tier.ToString(CultureInfo.InvariantCulture);
            return archetype + "/" + tier + (Category.HasValue ? "@" + Category.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
        }
    }

    /// <summary>
    /// Thresholds of the invariant rules (InvariantChecker). These are the harness's own sanity
    /// bounds, not game rules: the game caps (fleets, designs, tech level) come from the game's
    /// own constants; the rest are deliberately generous "something has gone badly wrong" limits.
    /// </summary>
    public sealed class SimulationLimits
    {
        /// <summary>Messages any one empire may receive in one turn before it counts as a
        /// message storm.</summary>
        public int MaxMessagesPerEmpirePerTurn { get; set; } = 400;

        /// <summary>Wall-clock bound for one whole turn (all AI moves plus generation).</summary>
        public double MaxTurnSeconds { get; set; } = 60;

        /// <summary>How far outside the 0..MapWidth / 0..MapHeight box a fleet or waypoint may
        /// be (light-years) before it counts as "off the map".</summary>
        public int MapMargin { get; set; } = 100;

        /// <summary>Population may exceed a planet's capacity (overcrowding); more than this
        /// multiple of max(capacity, PopulationFloor) is treated as a runaway.</summary>
        public double PopulationCapacityFactor { get; set; } = 4.0;

        public int PopulationFloor { get; set; } = 25000;

        public int MaxQueueLength { get; set; } = 200;

        /// <summary>Game caps (Global): 512 fleets, 16 ship designs, 10 starbase designs per race
        /// (production-queue.md section 10e, ship-design-and-components.md).</summary>
        public int MaxFleetsPerEmpire { get; set; } = Global.MaxFleetAmount;

        public int MaxShipDesigns { get; set; } = Global.MaxDesignsAmount;

        public int MaxStarbaseDesigns { get; set; } = Global.MaxStarbaseDesignsAmount;

        /// <summary>Report.Error texts containing any of these substrings do not count as
        /// errors (case-insensitive).</summary>
        public List<string> AllowedErrorSubstrings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Everything that defines one simulated game. Two runs with an equal config are expected to
    /// be identical once the game itself is deterministic from its seed (T1's work).
    /// </summary>
    public sealed class SimulationConfig
    {
        public int Seed { get; set; } = 1;

        public string GameName { get; set; } = "NovaSim";

        public GalaxySize GalaxySize { get; set; } = GalaxySize.Tiny;

        public GalaxyDensity Density { get; set; } = GalaxyDensity.Normal;

        public StartingDistance StartingDistance { get; set; } = StartingDistance.Moderate;

        /// <summary>Overrides the preset's star count when set.</summary>
        public int? NumberOfStars { get; set; }

        public List<PlayerSpec> Players { get; set; } = new List<PlayerSpec>();

        public int Turns { get; set; } = 30;

        public bool AcceleratedStart { get; set; }

        public bool NoRandomEvents { get; set; }

        public bool SlowTechAdvance { get; set; }

        public bool MaximumMinerals { get; set; }

        public bool GalaxyClumping { get; set; }

        /// <summary>Reload the game from its saved state file every this many turns (0 = never),
        /// so save/load is exercised; the reloaded state is the one play continues from.</summary>
        public int ReloadEvery { get; set; } = 5;

        /// <summary>On reload turns, also check that reloading and re-saving gives the same
        /// text.</summary>
        public bool CheckRoundTrip { get; set; } = true;

        /// <summary>Keep TurnGenerator's per-year backup folders (off: they only cost disk).</summary>
        public bool KeepTurnBackups { get; set; }

        /// <summary>Where the game is created; null makes a fresh temporary folder.</summary>
        public string WorkFolder { get; set; }

        /// <summary>Delete the work folder when the run ends (only if it was temporary).</summary>
        public bool DeleteWorkFolder { get; set; } = true;

        /// <summary>Stop at the first turn with an invariant violation.</summary>
        public bool StopOnViolation { get; set; }

        /// <summary>Invariant rule names to skip.</summary>
        public HashSet<string> DisabledInvariants { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SimulationLimits Limits { get; set; } = new SimulationLimits();

        /// <summary>The components.xml to use; null searches the usual places.</summary>
        public string ComponentsFile { get; set; }

        /// <summary>
        /// The usual "N players" setup: archetypes 0, 1, 2 ... in order (wrapping after the six),
        /// all Standard tier.
        /// </summary>
        public static List<PlayerSpec> DefaultPlayers(int count, int tier = AiRaceTemplates.Standard)
        {
            return Enumerable.Range(0, count)
                .Select(i => new PlayerSpec(i % AiRaceTemplates.ArchetypeNames.Length, tier))
                .ToList();
        }

        public SimulationConfig Clone()
        {
            SimulationConfig copy = (SimulationConfig)MemberwiseClone();
            copy.Players = Players.Select(p => new PlayerSpec(p.Archetype, p.Tier, p.Category)).ToList();
            copy.DisabledInvariants = new HashSet<string>(DisabledInvariants, StringComparer.OrdinalIgnoreCase);
            copy.Limits = new SimulationLimits
            {
                MaxMessagesPerEmpirePerTurn = Limits.MaxMessagesPerEmpirePerTurn,
                MaxTurnSeconds = Limits.MaxTurnSeconds,
                MapMargin = Limits.MapMargin,
                PopulationCapacityFactor = Limits.PopulationCapacityFactor,
                PopulationFloor = Limits.PopulationFloor,
                MaxQueueLength = Limits.MaxQueueLength,
                MaxFleetsPerEmpire = Limits.MaxFleetsPerEmpire,
                MaxShipDesigns = Limits.MaxShipDesigns,
                MaxStarbaseDesigns = Limits.MaxStarbaseDesigns,
                AllowedErrorSubstrings = new List<string>(Limits.AllowedErrorSubstrings),
            };
            return copy;
        }

        public string Describe()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "seed {0}, {1}/{2}, {3} turns, accelerated {4}, players [{5}]",
                Seed,
                GalaxySize,
                Density,
                Turns,
                AcceleratedStart ? "on" : "off",
                string.Join(", ", Players.Select(p => p.ToString())));
        }
    }
}
