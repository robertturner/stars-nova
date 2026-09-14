using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Nova.Client;

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

    protected GameShellViewModelBase(ClientData clientState)
    {
        this.clientState = clientState;

        SubmitTurnCommand = new AsyncRelayCommand(SubmitTurnAsync, () => !isSubmitting);
        ShowAboutCommand = new RelayCommand(() => AboutRequested?.Invoke());

        // Each AI computes its own orders from its own intel for the CURRENT turn, entirely
        // independently of what the human decides here - so there's no reason to wait until
        // Submit Turn to start that work. Fire-and-forget: TurnHost caches the running Task
        // itself, and SubmitTurnAsync will await this same run rather than starting another.
        _ = TurnHost.RunPendingAiTurnsAsync(clientState.GameFolder);
    }

    private async Task SubmitTurnAsync()
    {
        isSubmitting = true;
        SubmitTurnCommand.NotifyCanExecuteChanged();
        StatusMessage = "Submitting turn...";

        try
        {
            bool advanced = await TurnHost.SubmitAndTryAdvanceTurnAsync(clientState);
            if (advanced)
            {
                ClientData freshState = await Task.Run(
                    () => GameSession.Load(clientState.GameFolder, clientState.EmpireState.Race.Name));
                TurnAdvanced?.Invoke(freshState);
            }
            else
            {
                StatusMessage = $"Turn submitted ({clientState.Commands.Count} order(s)) - waiting for other players.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Submit failed: {ex.Message}";
        }
        finally
        {
            isSubmitting = false;
            SubmitTurnCommand.NotifyCanExecuteChanged();
        }
    }
}
