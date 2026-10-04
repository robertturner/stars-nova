using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Nova.Client;
using Nova.Client.Shell;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Shared turn-submission/About plumbing for a loaded game's main screen - factored out of
/// MainViewModel (the desktop dock layout) so MobileMainViewModel (Android's own, non-docked
/// screen) can reuse the exact same End Turn/background-AI/About behavior instead of
/// duplicating it, while each still owns its own, completely different content layout.
/// </summary>
public abstract class GameShellViewModelBase : ViewModelBase
{
    protected readonly ClientData clientState;

    private bool isSubmitting;

    public IAsyncRelayCommand SubmitTurnCommand { get; }

    public IRelayCommand ShowAboutCommand { get; }

    /// <summary>View > Race (F8; client-interface.md commands 156/157): "copies the player's own
    /// race and opens the same wizard view-only" - the host shows RaceDesignerView(race,
    /// isEditable: false), which works on its own copy (RaceViewRequested).</summary>
    public IRelayCommand ViewRaceCommand { get; }

    /// <summary>Raised by <see cref="ViewRaceCommand"/> with the player's race; how it is shown
    /// differs per host (a window on the desktop, a swapped-in screen on Android).</summary>
    public event Action<Nova.Common.Race>? RaceViewRequested;

    /// <summary>Report > Dump to Text File: writes the Planets / Universe / Fleets plain-text
    /// export (Nova.Client.ReportExport) into the game folder; the parameter names the kind.</summary>
    public IRelayCommand<string> ExportReportCommand { get; }

    /// <summary>
    /// Raised when the user asks to see the About screen - kept as an event rather than
    /// something this ViewModel shows itself, since "how" differs per host: the desktop head
    /// (MainWindow.axaml.cs) opens a real modal Window; the single-view Android host swaps in
    /// an AboutView as content instead. Same decoupling pattern OpenGameViewModel already uses
    /// for GameOpened.
    /// </summary>
    public event Action? AboutRequested;

    /// <summary>Raised once submitting the turn has caused it to actually advance (every
    /// player, human plus any in-process AI, had submitted - see TurnHost), carrying a freshly
    /// loaded ClientData for the new turn. The host swaps in a new screen/view model for it -
    /// this one's own content was all built against the old turn's ClientData and isn't set up
    /// to rebind in place.</summary>
    public event Action<ClientData>? TurnAdvanced;

    private string statusMessage = "";

    public string StatusMessage
    {
        get => statusMessage;
        protected set
        {
            if (SetProperty(ref statusMessage, value))
            {
                HasStatusMessage = !string.IsNullOrEmpty(value);
            }
        }
    }

    private bool hasStatusMessage;

    public bool HasStatusMessage
    {
        get => hasStatusMessage;
        private set => SetProperty(ref hasStatusMessage, value);
    }

    // Whether the orders changed since they were last written (shutdown confirmation, autosave).
    private readonly UnsavedChangesTracker unsavedChanges = new UnsavedChangesTracker();

    /// <summary>True when the pending orders changed since they were last saved or submitted.</summary>
    public bool HasUnsavedChanges => unsavedChanges.IsDirty(clientState.Commands.Cast<object>());

    /// <summary>Writes the client state (orders) to disk and records it as saved.</summary>
    public void SaveOrders()
    {
        clientState.Save();
        MarkOrdersSaved();
    }

    protected void MarkOrdersSaved()
    {
        unsavedChanges.MarkSaved(clientState.Commands.Cast<object>());
    }

    /// <summary>
    /// The progress dialog of long-running actions (client-ui-dialog-catalog.md "Progress and
    /// failure feedback", via Nova.Client.Shell.ProgressSurface: created once, updated, torn
    /// down; a failure shows its own explanation and a Close button instead of the gauge).
    /// End Turn reports its stages here (TurnProgressStages).
    /// </summary>
    public ProgressSurface Progress { get; } = new ProgressSurface();

    public bool IsProgressVisible => Progress.Exists;

    public bool IsProgressRunning => Progress.Exists && !Progress.HasFailed;

    public string ProgressCaption => Progress.Caption;

    public int ProgressPercent => Progress.Percent;

    public bool HasProgressFailed => Progress.HasFailed;

    public string ProgressFailureMessage => Progress.FailureMessage ?? "";

    /// <summary>Closes the failure surface (the only follow-up it offers).</summary>
    public IRelayCommand CloseProgressCommand { get; }

    private void OnProgressChanged()
    {
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(IsProgressRunning));
        OnPropertyChanged(nameof(ProgressCaption));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(HasProgressFailed));
        OnPropertyChanged(nameof(ProgressFailureMessage));
    }

    protected GameShellViewModelBase(ClientData clientState)
    {
        this.clientState = clientState;
        MarkOrdersSaved();
        Progress.Created += OnProgressChanged;
        Progress.Updated += OnProgressChanged;
        Progress.Destroyed += OnProgressChanged;
        CloseProgressCommand = new RelayCommand(() => Progress.End());

        SubmitTurnCommand = new AsyncRelayCommand(SubmitTurnAsync, () => !isSubmitting);
        ShowAboutCommand = new RelayCommand(() => AboutRequested?.Invoke());
        ViewRaceCommand = new RelayCommand(
            () => RaceViewRequested?.Invoke(clientState.EmpireState.Race),
            () => clientState.EmpireState?.Race != null);
        ExportReportCommand = new RelayCommand<string>(ExportReport);

        // Each AI computes its own orders from its own intel for the CURRENT turn, entirely
        // independently of what the human decides here - so there's no reason to wait until
        // Submit Turn to start that work. Fire-and-forget: TurnHost caches the running Task
        // itself, and SubmitTurnAsync will await this same run rather than starting another.
        _ = TurnHost.RunPendingAiTurnsAsync(clientState.GameFolder);
    }

    /// <summary>Writes one of the plain-text exports (client-ui-dialog-catalog.md "Reports":
    /// no file prompt, no confirmation) and reports where it went.</summary>
    protected void ExportReport(string? kind)
    {
        if (!Enum.TryParse(kind, true, out ReportExport.Kind exportKind))
        {
            return;
        }

        try
        {
            string path = ReportExport.Write(clientState.EmpireState, exportKind, clientState.GameFolder);
            StatusMessage = $"Wrote {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    // The End Turn stage last shown; stage reports posted from the background thread that
    // arrive late (after a later stage or after teardown) are ignored.
    private int lastTurnStage = -1;

    private void ReportTurnStage(int stage)
    {
        if (!isSubmitting || stage <= lastTurnStage || stage < 0 || stage >= TurnProgressStages.Count)
        {
            return;
        }

        lastTurnStage = stage;
        Progress.Report(TurnProgressStages.Captions[stage], stage, TurnProgressStages.Count);
    }

    private async Task SubmitTurnAsync()
    {
        isSubmitting = true;
        lastTurnStage = -1;
        SubmitTurnCommand.NotifyCanExecuteChanged();
        StatusMessage = "Submitting turn...";

        try
        {
            ReportTurnStage(TurnProgressStages.SavingOrders);
            bool advanced = await TurnHost.SubmitAndTryAdvanceTurnAsync(
                clientState,
                stage => Dispatcher.UIThread.Post(() => ReportTurnStage(stage)));
            MarkOrdersSaved();
            if (advanced)
            {
                ReportTurnStage(TurnProgressStages.LoadingNewTurn);
                ClientData freshState = await Task.Run(
                    () => GameSession.Load(clientState.GameFolder, clientState.EmpireState.Race.Name));
                Progress.End();
                TurnAdvanced?.Invoke(freshState);
            }
            else
            {
                Progress.End();
                StatusMessage = $"Turn submitted ({clientState.Commands.Count} order(s)) - waiting for other players.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Submit failed: {ex.Message}";
            Progress.Fail($"The turn could not be submitted: {ex.Message}");
        }
        finally
        {
            isSubmitting = false;
            SubmitTurnCommand.NotifyCanExecuteChanged();
        }
    }
}
