using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nova.Ai;
using Nova.Client;
using Nova.Common;
using Nova.Server;

namespace Nova.Avalonia;

/// <summary>
/// Advances a turn once every player (human plus any in-process AI) has submitted orders - the
/// native, in-process replacement for Nova\WinForms\NovaConsole.cs's polling-timer
/// orchestration (ConsoleTimer_Tick/RunAI/GenerateTurn), which only exists in the WinForms
/// console app and spawns each AI as a separate subprocess (Android has no such concept).
/// AbstractAI.Initialize/DoMove and OrderWriter.WriteOrders are plain, self-contained method
/// calls with no subprocess-specific assumptions - confirmed by how Nova\Ai\AiRunner.cs's
/// subprocess entry point already just calls them directly, with no environment/working-
/// directory/exit-code dependency beyond the CommandArguments built here. The only real
/// orchestration NovaConsole contributes is "who hasn't submitted, and is it my turn to run
/// them" - trivial to reproduce, since TurnGenerator/OrderReader/OrderWriter/IntelWriter already
/// live in the portable Nova.Server/Nova.Client assemblies.
///
/// Runs AI turns in the background as soon as a game is opened (see MainViewModel's
/// constructor), rather than only after the human submits - each AI computes its own orders
/// from its own intel for the CURRENT turn, entirely independently of what the human decides,
/// so there's no reason to make the human wait for it. Submitting just waits for that
/// already-in-flight (or, if it somehow hasn't started yet, freshly kicked off) work to finish
/// rather than starting it fresh.
/// </summary>
public static class TurnHost
{
    private static readonly Dictionary<string, Task> PendingAiRuns = new();

    private static readonly object Padlock = new();

    /// <summary>
    /// Starts (or returns the already-running) background pass computing orders for every AI
    /// player that hasn't submitted this turn yet. Safe to call more than once for the same
    /// game folder - a second call while one is already in flight just returns that same Task
    /// rather than launching a redundant, file-colliding duplicate.
    /// </summary>
    public static Task RunPendingAiTurnsAsync(string gameFolder)
    {
        lock (Padlock)
        {
            if (PendingAiRuns.TryGetValue(gameFolder, out Task? existing) && !existing.IsCompleted)
            {
                return existing;
            }

            Task task = Task.Run(() => RunPendingAiTurns(gameFolder));
            PendingAiRuns[gameFolder] = task;
            return task;
        }
    }

    /// <summary>
    /// Writes the human player's own orders, waits for any in-flight (or freshly started)
    /// background AI processing to finish, then advances the turn if everyone has now
    /// submitted. Returns true if the turn actually advanced - the caller should reload its
    /// ClientData for the new turn in that case.
    /// </summary>
    public static async Task<bool> SubmitAndTryAdvanceTurnAsync(ClientData clientState)
    {
        GameActions.SubmitTurn(clientState);

        string gameFolder = clientState.GameFolder;
        await RunPendingAiTurnsAsync(gameFolder).ConfigureAwait(false);

        return await Task.Run(() => TryAdvanceTurn(gameFolder)).ConfigureAwait(false);
    }

    private static void RunPendingAiTurns(string gameFolder)
    {
        ServerData? serverState = LoadServerState(gameFolder);
        if (serverState == null)
        {
            return;
        }

        new OrderReader(serverState).ReadOrders();

        foreach (PlayerSettings settings in serverState.AllPlayers)
        {
            if (settings.AiProgram != "Human" && !IsTurnedIn(serverState, settings))
            {
                RunOneAiTurn(serverState, settings);
            }
        }
    }

    private static bool TryAdvanceTurn(string gameFolder)
    {
        ServerData? serverState = LoadServerState(gameFolder);
        if (serverState == null)
        {
            return false;
        }

        new OrderReader(serverState).ReadOrders();

        if (!serverState.AllPlayers.All(settings => IsTurnedIn(serverState, settings)))
        {
            return false;
        }

        new TurnGenerator(serverState).Generate();
        serverState.Save();
        return true;
    }

    private static void RunOneAiTurn(ServerData serverState, PlayerSettings settings)
    {
        CommandArguments args = new CommandArguments();
        args.Add(CommandArguments.Option.RaceName, settings.RaceName);
        args.Add(CommandArguments.Option.Turn, serverState.TurnYear);
        args.Add(CommandArguments.Option.IntelFileName, Path.Combine(serverState.GameFolder, settings.RaceName + Global.IntelExtension));

        AbstractAI ai = new DefaultAi();
        ai.Initialize(args);
        ai.DoMove();
        new OrderWriter(ai.ClientState).WriteOrders();
    }

    private static bool IsTurnedIn(ServerData serverState, PlayerSettings settings)
    {
        return serverState.AllEmpires.TryGetValue(settings.PlayerNumber, out EmpireData? empireData)
            && empireData.TurnYear == serverState.TurnYear
            && empireData.TurnSubmitted;
    }

    private static ServerData? LoadServerState(string gameFolder)
    {
        string? statePath = Directory.GetFiles(gameFolder, "*" + Global.ServerStateExtension).FirstOrDefault();
        if (statePath == null)
        {
            return null;
        }

        ServerData serverState = new ServerData { StatePathName = statePath };
        serverState.Restore();
        return serverState;
    }
}
