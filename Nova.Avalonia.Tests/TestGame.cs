using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nova.Client;
using Nova.Client.Map;
using Nova.Common;
using Nova.Common.Components;
using Nova.Server.NewGame;

namespace Nova.Avalonia.Tests;

/// <summary>
/// A real, small game generated once per test run with the engine's own new-game code
/// (Gameinitializer, the same call the New Game screen makes), copied to a fresh folder for every
/// test and loaded through GameSession.Load - the same pipeline the app uses to open a game - so
/// every view model is built against genuine ClientData (intel, designs, starbases, fleets in
/// orbit, battle plans) and no test can see another test's edits.
///
/// Two human players: <see cref="PacketRace"/> (Packet Physics - two planets, a mass-driver
/// starbase, so the packet controls have something to work on) and <see cref="DemolitionRace"/>
/// (Space Demolition - its own standard minefields may be detonated). Both start from the shipped
/// Humanoid race file.
///
/// Everything the engine looks up "next to the executable" (components.xml, DefaultRaces,
/// HelpContent, Graphics, nova.conf) resolves to this test project's own output folder via
/// PlatformHooks.NovaRootOverride, so a test run never touches a real installation's nova.conf.
/// </summary>
public static class TestGame
{
    public const string GameName = "NovaUiTestGame";

    public const string PacketRace = "Packeteers";

    public const string DemolitionRace = "Demolishers";

    private const int Seed = 4242;

    private static readonly object Padlock = new object();

    private static string? scratchRoot;

    private static string? templateFolder;

    private static bool environmentReady;

    /// <summary>Where this run's game copies (and any game a New Game test creates) live.</summary>
    public static string ScratchRoot
    {
        get
        {
            PrepareEnvironment();
            return scratchRoot!;
        }
    }

    /// <summary>The test output folder, standing in for the installation folder.</summary>
    public static string NovaRoot => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Points the engine's file lookups at the test output and loads the components.</summary>
    public static void PrepareEnvironment()
    {
        lock (Padlock)
        {
            if (environmentReady)
            {
                return;
            }

            scratchRoot = Path.Combine(Path.GetTempPath(), "NovaUiTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratchRoot);

            PlatformHooks.NovaRootOverride = () => NovaRoot;
            PlatformHooks.GamesRootOverride = () => scratchRoot;

            // Point the race folder at the shipped DefaultRaces copy before anything asks for it
            // (FileSearcher.GetFolder would otherwise create an empty one).
            using (Config conf = new Config())
            {
                conf[Global.RaceFolderKey] = Path.Combine(NovaRoot, Global.RaceFolderName);
                conf[Global.ComponentFileKey] = Path.Combine(NovaRoot, Global.ComponentFileName);
            }

            new AllComponents(false).RestoreHeadless();
            environmentReady = true;
        }
    }

    /// <summary>Deletes this run's scratch folder (best effort).</summary>
    public static void CleanUp()
    {
        if (scratchRoot == null)
        {
            return;
        }

        try
        {
            Directory.Delete(scratchRoot, true);
        }
        catch (Exception)
        {
            // A background reader (TurnHost's AI pass) may still hold a file; the OS temp
            // cleaner gets it later.
        }
    }

    /// <summary>A fresh copy of the generated game, loaded for <paramref name="raceName"/>.</summary>
    public static ClientData Load(string raceName = PacketRace)
    {
        string source = EnsureTemplate();
        string copy = Path.Combine(ScratchRoot, "game_" + Guid.NewGuid().ToString("N"));
        CopyFolder(source, copy);

        GameSettings.Data = FreshSettings();
        GameSettings.Data.GameName = GameName;
        ClientData clientState = GameSession.Load(copy, raceName);
        GameSettings.Restore();
        ResetMapViewOptions();
        return clientState;
    }

    /// <summary>
    /// The star map's view options are static (they survive a turn change in the app); put them
    /// back to the shipped defaults so one test's toggles never leak into the next. The values
    /// come from MapViewOptions' own seams - nothing here pins them.
    /// </summary>
    public static void ResetMapViewOptions()
    {
        MapViewOptions options = Nova.Avalonia.ViewModels.Panels.StarMapDocumentViewModel.ViewOptions;
        options.ScannerPercentage = MapViewOptions.MaxScannerPercentage; // also forces scan circles on
        options.Mode = MapViewOptions.DefaultMode;
        options.ShowScanCircles = (MapViewOptions.DefaultWord1 & MapViewOptions.ScanCirclesBit) != 0;
        options.ShowMinefields = (MapViewOptions.DefaultWord1 & MapViewOptions.MinefieldsBit) != 0;
        options.ShowRouteOverlap = (MapViewOptions.DefaultWord1 & MapViewOptions.RouteOverlapBit) != 0;
        options.ShowPlanetNames = (MapViewOptions.DefaultWord2 & MapViewOptions.PlanetNamesBit) != 0;
        options.ShowShipCountBadges = (MapViewOptions.DefaultWord2 & MapViewOptions.BadgeBit) != 0;
        options.ZoomStep = MapZoom.DefaultLevel;
    }

    /// <summary>The empire's home world: the owned planet whose starbase is the full "Starbase".</summary>
    public static Star HomeStar(ClientData clientState)
    {
        return clientState.EmpireState.OwnedStars.Values
            .OrderByDescending(star => star.Colonists)
            .First();
    }

    /// <summary>The owned non-starbase fleets orbiting <paramref name="star"/>.</summary>
    public static List<Fleet> FleetsAt(ClientData clientState, Star star)
    {
        return clientState.EmpireState.OwnedFleets.Values
            .Where(fleet => fleet.InOrbit == star && fleet != star.Starbase && !fleet.IsStarbase)
            .OrderBy(fleet => fleet.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static string EnsureTemplate()
    {
        lock (Padlock)
        {
            if (templateFolder != null)
            {
                return templateFolder;
            }

            string folder = Path.Combine(ScratchRoot, "template");
            Directory.CreateDirectory(folder);

            GameSettings.Data = FreshSettings();
            GameSettings.Data.GameName = GameName;
            GameSettings.Data.Seed = Seed;

            // Small, not Tiny: Packet Physics' second planet needs a galaxy-size index of at
            // least 1 (new-game-setup.md section 5a).
            GameSettings.Data.ApplyGalaxyPreset(GalaxySize.Small, GalaxyDensity.Sparse);

            string humanoidFile = Path.Combine(NovaRoot, Global.RaceFolderName, "Humanoid.race");
            Race packet = new Race(humanoidFile);
            packet.Name = PacketRace;
            packet.PluralName = PacketRace;
            packet.Traits.SetPrimary("PP");

            Race demolition = new Race(humanoidFile);
            demolition.Name = DemolitionRace;
            demolition.PluralName = DemolitionRace;
            demolition.Traits.SetPrimary("SD");

            var races = new Dictionary<string, Race> { { packet.Name, packet }, { demolition.Name, demolition } };
            var players = new List<PlayerSettings>
            {
                new PlayerSettings { PlayerNumber = 1, RaceName = packet.Name, AiProgram = "Human" },
                new PlayerSettings { PlayerNumber = 2, RaceName = demolition.Name, AiProgram = "Human" },
            };

            Gameinitializer.Initialize(folder, players, races);
            GameSettings.Save();

            templateFolder = folder;
            return folder;
        }
    }

    /// <summary>A default GameSettings (its constructor is private: the class is a singleton the
    /// engine replaces wholesale on Restore, so a test does the same).</summary>
    public static GameSettings FreshSettings()
    {
        return (GameSettings)Activator.CreateInstance(typeof(GameSettings), nonPublic: true)!;
    }

    private static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }
}
