#region Copyright Notice
// ============================================================================
// Copyright (C) 2010, 2011 The Stars-Nova Project
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

namespace Nova.Common
{
    using System;
    using System.IO;
    using System.Xml.Serialization;
    
    /// <summary>
    /// Records the settings specific to a running game (map, victory conditions)
    /// This module is implemented as a singleton. 
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        // Map settings

        public int MapWidth = 400;
        public int MapHeight = 400;

        public int StarSeparation = 10;
        public int StarDensity = 40;
        public int StarUniformity = 60;

        public int NumberOfStars = 50;

        /// <summary>
        /// The seed used to generate this game's galaxy (star positions, mineral
        /// concentrations, homeworld assignment, and star/race name allocation - see
        /// Gameinitializer.Initialize). Null means "not yet resolved" - Gameinitializer resolves
        /// a null Seed to a freshly-generated one and writes it back here before generating,
        /// so the actual seed used is always recorded afterward and can be echoed back to the
        /// user for reproducibility (e.g. "share this seed to regenerate the same galaxy").
        /// </summary>
        public int? Seed = null;

        // Victory conditions (with initial default values)

        public EnabledValue PlanetsOwned        = new EnabledValue(true, 60);
        public EnabledValue TechLevels          = new EnabledValue(true, 22);
        public EnabledValue NumberOfFields      = new EnabledValue(true, 4);
        public EnabledValue TotalScore          = new EnabledValue(false, 11000);
        public EnabledValue SecondPlaceScore    = new EnabledValue(true, 100);
        public EnabledValue ProductionCapacity  = new EnabledValue(false, 100);
        public EnabledValue CapitalShips        = new EnabledValue(false, 100);
        public EnabledValue HighestScore        = new EnabledValue(false, 100);
        public int TargetsToMeet = 1;
        public int MinimumGameTime = 50;

        public string SettingsPathName;
        public string GameName = "Feel the Nova";

        public bool AcceleratedStart = false;

        /// <summary>
        /// The "No Random Events" game option (behavior-specs-9/turn-generation-engine.md §1b, bit
        /// 0x80): skips the yearly random-event wrapper (comet, environment shift, mineral
        /// deposit - see RandomEventsStep). Persisted with the rest of the settings.
        /// </summary>
        public bool NoRandomEvents = false;

        /// <summary>
        /// The "Public Player Scores" game option (behavior-specs-11/save-turn-file-format.md §3,
        /// "Score records"): when on and the new turn counter exceeds 19 (the file for 2420
        /// onward), every player's turn file carries every race's score record; otherwise only the
        /// viewer's own, an eliminated race's, and (once the game is over) every race's.
        /// </summary>
        public bool PublicPlayerScores = false;

        /// <summary>
        /// The "Slow Tech Advance" game option: every research level costs twice as much, the
        /// doubling applied last, after the field's cost factor (behavior-specs-10/
        /// research-tech-tree.md section 3; see Research.Cost). Persisted with the settings.
        /// </summary>
        public bool SlowTechAdvance = false;

        // New Game wizard, general page (behavior-specs-10/new-game-setup.md sections 1 and 3).

        /// <summary>
        /// When true the galaxy is generated from the discrete wizard options below
        /// (GalaxySizeSetting, StarDensitySetting): a square map (size + 1) x 400 ly across and
        /// a formula star count capped at 999 - see ApplyGalaxyPreset. When false (the default,
        /// and every older settings file) the free MapWidth/MapHeight/StarDensity/StarUniformity
        /// generator is used as before.
        /// </summary>
        public bool UseGalaxyPresets = false;

        /// <summary>Galaxy Size (Tiny .. Huge, index 0-4). Only read when UseGalaxyPresets is set.</summary>
        public GalaxySize GalaxySizeSetting = GalaxySize.Tiny;

        /// <summary>Star Density (Sparse .. Packed, index 0-3). Only read when UseGalaxyPresets is set.</summary>
        public GalaxyDensity StarDensitySetting = GalaxyDensity.Sparse;

        /// <summary>
        /// Starting Distance (Close / Moderate / Farther / Distant). The default is Moderate, what
        /// the Simplified dialog writes for its default Standard difficulty (behavior-specs-11/
        /// new-game-setup.md section 2; the earlier "reset value, index 2" came from a retracted
        /// reading of section 8). The home-world placement it governs (section 3, "Home-world
        /// placement and Starting Distance") is not implemented yet, so it is stored but has no
        /// effect on generation.
        /// </summary>
        public StartingDistance StartingDistanceSetting = StartingDistance.Moderate;

        /// <summary>
        /// "Beginner: Maximum Minerals" (option bit 0x01): every planet's three concentrations are
        /// 100 instead of rolled, and the high-radiation raise and low-concentration roll are skipped
        /// (new-game-setup.md section 1 option table and section 3). Home worlds copy the template
        /// planet, so they start at exactly 100 too.
        /// </summary>
        public bool MaximumMinerals = false;

        /// <summary>
        /// "Galaxy Clumping" (option bit 0x100): runs the single relaxation pass of new-game-setup.md
        /// section 3 after star placement, pulling each star toward its nearest neighbour.
        /// </summary>
        public bool GalaxyClumping = false;

        /// <summary>
        /// The galaxy-size index s (0 Tiny .. 4 Huge). With presets it is GalaxySizeSetting;
        /// otherwise it is recovered from the free map width by inverting the diameter formula,
        /// clamp(MapWidth / 400 - 1, 0, 4) - the same rule RandomEventsStep.GalaxySizeIndex uses.
        /// </summary>
        [XmlIgnore]
        public int GalaxySizeIndex
        {
            get
            {
                if (UseGalaxyPresets)
                {
                    return (int)GalaxySizeSetting;
                }
                return Math.Max(0, Math.Min(4, (MapWidth / 400) - 1));
            }
        }

        /// <summary>The hard cap on the number of stars in a galaxy (new-game-setup.md section 3).</summary>
        public const int MaximumStars = 999;

        /// <summary>
        /// Galaxy diameter in light-years: 400 x (size index + 1), i.e. 400 / 800 / 1200 / 1600 /
        /// 2000 for Tiny .. Huge (new-game-setup.md section 3).
        /// </summary>
        public static int GalaxyDiameter(GalaxySize size)
        {
            return 400 * ((int)size + 1);
        }

        /// <summary>
        /// Star count for a galaxy size and density (behavior-specs-11/new-game-setup.md section
        /// 3, segment 16 `0x1342`-`0x13a9`): n = diameter squared / 5000, plus (n / 4) x
        /// (density index - 1) with the 0-based density index (Sparse 0 .. Packed 3) and n / 4
        /// truncated - so Sparse subtracts a quarter and Normal is the unadjusted base - then,
        /// for Packed only, a further truncated quarter of the new value; capped at 999. Results
        /// Sparse / Normal / Dense / Packed: Tiny 24 / 32 / 40 / 60, Small 96 / 128 / 160 / 240,
        /// Medium 216 / 288 / 360 / 540, Large 384 / 512 / 640 / 960, Huge 600 / 800 / 999 / 999.
        /// These are upper bounds: the 12 ly separation sweep can leave fewer stars.
        /// </summary>
        public static int PresetStarCount(GalaxySize size, GalaxyDensity density)
        {
            int diameter = GalaxyDiameter(size);
            int count = diameter * diameter / 5000;
            int densityIndex = (int)density;

            count += (count / 4) * (densityIndex - 1);
            if (density == GalaxyDensity.Packed)
            {
                count += count / 4;
            }

            return Math.Min(MaximumStars, count);
        }

        /// <summary>
        /// Switches to the discrete wizard galaxy: sets UseGalaxyPresets and makes the map a
        /// square of the preset diameter with NumberOfStars set to the preset star count.
        /// </summary>
        public void ApplyGalaxyPreset(GalaxySize size, GalaxyDensity density)
        {
            UseGalaxyPresets = true;
            GalaxySizeSetting = size;
            StarDensitySetting = density;
            MapWidth = GalaxyDiameter(size);
            MapHeight = MapWidth;
            NumberOfStars = PresetStarCount(size, density);
        }

        #region Singleton

        // ============================================================================
        // Data private to this module.
        // ============================================================================

        private static GameSettings instance;
        private static object padlock = new object();


        // ============================================================================
        // Private constructor to prevent anyone else creating instances of this class.
        // ============================================================================

        private GameSettings() 
        { 
        }


        // ============================================================================
        // Provide a mechanism of accessing the single instance of this class that we
        // will create locally. Creation of the data is thread-safe.
        // ============================================================================

        public static GameSettings Data
        {
            get
            {
                if (instance == null)
                {
                    lock (padlock)
                    {
                        if (instance == null)
                        {
                            instance = new GameSettings();
                        }
                    }
                }
                return instance;
            }

            // ----------------------------------------------------------------------------

            set
            {
                instance = value;
            }
        }

        #endregion

        #region Per-game settings

        /// <summary>
        /// A deep copy (through the same XML form Save writes), so a game can keep its own
        /// settings independently of the process-wide <see cref="Data"/>.
        /// </summary>
        public GameSettings Clone()
        {
            return FromXmlText(ToXmlText());
        }

        /// <summary>These settings as the XML text <see cref="Save"/> writes (invariant: the
        /// serializer formats numbers culture-independently).</summary>
        public string ToXmlText()
        {
            XmlSerializer serializer = new XmlSerializer(typeof(GameSettings));
            using (StringWriter writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, this);
                return writer.ToString();
            }
        }

        /// <summary>Reads settings from <see cref="ToXmlText"/>'s (or a .settings file's) text.</summary>
        public static GameSettings FromXmlText(string text)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(GameSettings));
            using (StringReader reader = new StringReader(text))
            {
                return (GameSettings)serializer.Deserialize(reader);
            }
        }

        /// <summary>Reads a .settings file, or returns null when there is none or it cannot be read.</summary>
        public static GameSettings TryLoad(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
            {
                return null;
            }

            try
            {
                return FromXmlText(File.ReadAllText(fileName));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Makes <paramref name="settings"/> the process-wide <see cref="Data"/> until the
        /// returned scope is disposed, which puts the previous instance back. Turn generation
        /// runs inside such a scope with the game's own settings (ServerData.Settings), so its
        /// rules never read whatever another game, the New Game screen or a test left in the
        /// singleton. A null <paramref name="settings"/> leaves Data alone.
        /// </summary>
        public static IDisposable Use(GameSettings settings)
        {
            GameSettings previous = instance;
            if (settings != null)
            {
                instance = settings;
            }

            return new SettingsScope(previous, settings != null);
        }

        private sealed class SettingsScope : IDisposable
        {
            private readonly GameSettings previous;
            private bool active;

            public SettingsScope(GameSettings previous, bool active)
            {
                this.previous = previous;
                this.active = active;
            }

            public void Dispose()
            {
                if (active)
                {
                    instance = previous;
                    active = false;
                }
            }
        }

        #endregion

        #region Methods


        /// <summary>
        /// Restore the persistent data.
        /// </summary>
        public static void Restore()
        {
            string fileName = Data.SettingsPathName;
            if (fileName == null)
            {
                fileName = FileSearcher.GetSettingsFile();
            }
            if (File.Exists(fileName))
            {
                bool waitForFile = false;
                double waitTime = 0.0; // seconds
                do
                {
                    try
                    {
                        using (FileStream state = new FileStream(fileName, FileMode.Open))
                        {
                            // Data = Serializer.Deserialize(state) as GameSettings;
                            XmlSerializer s = new XmlSerializer(typeof(GameSettings));
                            Data = (GameSettings)s.Deserialize(state);
                        }
                        waitForFile = false;
                    }
                    catch (System.IO.IOException)
                    {
                        // IOException. Is the file locked? Try waiting.
                        if (waitTime < Global.TotalFileWaitTime)
                        {
                            waitForFile = true;
                            System.Threading.Thread.Sleep(Global.FileWaitRetryTime);
                            waitTime += 0.1;
                        }
                        else
                        {
                            // Give up, maybe something else is wrong?
                            throw;
                        }
                    }
                } 
                while (waitForFile);
            }
        }

        /// <summary>
        /// Save the console persistent data.
        /// </summary>
        public static void Save()
        {
            if (Data.SettingsPathName == null)
            {
                // TODO (priority 5) add the nicities. Update the game files location.
                string chosen = PlatformHooks.AskUserForSaveFile("Choose a location to save the game settings.");
                if (chosen != null)
                {
                    Data.SettingsPathName = chosen;
                }
                else
                {
                    throw new System.IO.IOException("File dialog cancelled.");
                }
            }
            using (Stream stream = new FileStream(Data.SettingsPathName, FileMode.Create))
            {
                // Serializer.Serialize(stream, GameSettings.Data);
                XmlSerializer s = new XmlSerializer(typeof(GameSettings));
                s.Serialize(stream, GameSettings.Data);
            }
        }

        #endregion
    }

    /// <summary>The wizard's five galaxy sizes (new-game-setup.md section 3); the value is the size index.</summary>
    public enum GalaxySize
    {
        Tiny = 0,
        Small = 1,
        Medium = 2,
        Large = 3,
        Huge = 4
    }

    /// <summary>The wizard's four star densities (new-game-setup.md section 3); the value is the density index.</summary>
    public enum GalaxyDensity
    {
        Sparse = 0,
        Normal = 1,
        Dense = 2,
        Packed = 3
    }

    /// <summary>The wizard's four starting-distance choices (new-game-setup.md section 1).</summary>
    public enum StartingDistance
    {
        Close = 0,
        Moderate = 1,
        Farther = 2,
        Distant = 3
    }
}
