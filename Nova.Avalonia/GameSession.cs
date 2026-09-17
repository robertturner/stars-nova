using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nova.Client;
using Nova.Common;
using Nova.Common.Components;

namespace Nova.Avalonia;

/// <summary>
/// Loads a real game via the existing Nova.Client data pipeline (ClientData/IntelReader - the
/// same code NovaGUI itself uses), so the Avalonia panels have real data to bind to instead of
/// reinventing turn-file parsing here.
///
/// Deliberately bypasses ClientData.Initialize() - that method also does registry-based
/// game-folder lookup and race-selection dialog fallbacks (via FileSearcher/SelectRaceDialog)
/// that don't apply here since OpenGameViewModel already knows exactly which file to load
/// (mirroring how NovaLauncher.OpenGameButton_Click picks a specific .intel file directly,
/// rather than ClientData.SelectRace's folder-scan-for-*.race-files fallback - real save
/// folders observed in this project don't actually contain any .race files, only .intel/
/// .cstate ones, so that fallback path doesn't apply to real data anyway).
/// </summary>
public static class GameSession
{
    public static ClientData Load(string gameFolder, string raceName)
    {
        // AllComponents.Restore() shows a WinForms ProgressDialog via ShowDialog(), which
        // deadlocks when hosted outside a classic single-threaded WinForms Application (the
        // ThreadPool loader thread's Control.Invoke back onto the dialog hangs after the
        // first call). RestoreHeadless() runs the same XML load synchronously with no dialog
        // and no cross-thread marshaling, which sidesteps the conflict entirely.
        new AllComponents(false).RestoreHeadless();

        // GameSettings.Restore() (called later by the Star Map panel) falls back to
        // FileSearcher.GetSettingsFile() when SettingsPathName is unset, which can pop an
        // OpenFileDialog asking the user to locate a ".settings" file - since we already
        // know exactly which game this is, point it there directly instead.
        GameSettings.Data.SettingsPathName = Path.Combine(gameFolder, GameSettings.Data.GameName + ".settings");

        var clientState = new ClientData();
        string intelFilePath = Path.Combine(gameFolder, raceName + Global.IntelExtension);
        new IntelReader(clientState).ReadIntel(intelFilePath);

        // ClientData.Initialize() (bypassed above) would normally set these from the launch
        // arguments/registry; OrderWriter.WriteOrders() and ClientData.Save() both need them to
        // know where to write the .orders/.cstate files.
        clientState.GameFolder = gameFolder;
        clientState.StatePathName = Path.Combine(gameFolder, raceName + Global.ClientStateExtension);
        clientState.FirstTurn = false;

        RecordAsLastGame(clientState);

        return clientState;
    }

    /// <summary>
    /// Records this game's .cstate path under Global.ClientStateKey in nova.conf - the same
    /// config key NovaLauncher's own "Continue Game" button already reads
    /// (FileSearcher.GetFile(Global.ClientStateKey, ...), see NovaLauncher.cs's constructor).
    /// Repo-wide search confirmed nothing anywhere ever actually wrote this key before, which is
    /// why that WinForms button is permanently disabled in practice today.
    ///
    /// FileSearcher.GetNovaRoot() derives nova.conf's location from the running executable's own
    /// folder (walking up two levels only when that folder is literally named "Debug"/
    /// "Release" - true for Nova.exe's Build\Debug\ output, not for Nova.Avalonia.exe's own
    /// bin\Debug\net9.0-windows\), so each app ends up with its own separate nova.conf rather
    /// than truly sharing one - confirmed by a nova.conf already present under Nova.Avalonia's
    /// own build output. Continue therefore remembers this app's own last-opened game, same as
    /// NovaLauncher's button would for WinForms' own launches, but the two don't currently see
    /// each other's.
    /// </summary>
    private static void RecordAsLastGame(ClientData clientState)
    {
        using var conf = new Config();
        conf[Global.ClientStateKey] = clientState.StatePathName;
    }

    /// <summary>
    /// Finds the game most recently opened via <see cref="Load"/> in this app, for the Avalonia
    /// startup screen's "Continue" button. Returns null if nothing's been recorded yet, or if
    /// that game's .cstate file no longer exists (e.g. the save was moved or deleted).
    /// </summary>
    public static (string GameFolder, string RaceName)? FindContinuableGame()
    {
        string statePath = FileSearcher.GetFile(Global.ClientStateKey, false, "", "", "", false);
        if (statePath == null)
        {
            return null;
        }

        string gameFolder = Path.GetDirectoryName(statePath);
        if (gameFolder == null)
        {
            return null;
        }

        return (gameFolder, Path.GetFileNameWithoutExtension(statePath));
    }

    /// <summary>
    /// Bundles everything in a game folder useful for diagnosing a crash into one shareable
    /// blob: the player's own .intel (what they know), the single .sstate file TurnHost.
    /// LoadServerState reads (the actual, authoritative state TurnGenerator.Generate() operates
    /// on during End Turn, covering every empire including any in-process AI), the player's own
    /// .cstate (the CLIENT's own editing state - Waypoints/Commands the Inspector directly
    /// manipulates before Submit Turn writes them out, and so a very plausible place for a
    /// malformed Waypoint to actually originate), and every *.orders file present (each player's
    /// submitted orders for the upcoming turn - what OrderReader.ReadOrders() re-parses on every
    /// single load/End-Turn, so a bad Waypoint baked into one of these would explain a fault that
    /// recurs, and recurs more than once per turn, exactly as reported live). Earlier versions of
    /// this only shared the .intel, then the .intel+.sstate - neither actually contains the
    /// player's own in-progress waypoint edits or submitted orders, which is why they didn't
    /// reveal anything despite the crash being real and reproducible. Reads whatever exists and
    /// skips the rest rather than failing outright, since a fresh game might not have all of
    /// these yet.
    /// </summary>
    public static string BuildShareableSaveText(string gameFolder, string raceName)
    {
        var sections = new List<string>();

        void AddIfExists(string path)
        {
            if (File.Exists(path))
            {
                sections.Add($"===== {Path.GetFileName(path)} =====\n{File.ReadAllText(path)}");
            }
        }

        AddIfExists(Path.Combine(gameFolder, raceName + Global.IntelExtension));
        AddIfExists(Path.Combine(gameFolder, raceName + Global.ClientStateExtension));

        if (Directory.Exists(gameFolder))
        {
            string? statePath = Directory.GetFiles(gameFolder, "*" + Global.ServerStateExtension).FirstOrDefault();
            if (statePath != null)
            {
                AddIfExists(statePath);
            }

            foreach (string ordersPath in Directory.GetFiles(gameFolder, "*" + Global.OrdersExtension))
            {
                AddIfExists(ordersPath);
            }
        }

        return string.Join("\n\n", sections);
    }
}
