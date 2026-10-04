using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Messages panel: this turn's events, oldest first (matching the WinForms Messages
/// control's ordering), with the original's per-message-type filter
/// (behavior-specs-10/client-ui-dialog-catalog.md, Messages):
/// - the current message's header shows a tick (its type is shown) or a cross (filtered);
///   clicking it, or pressing "+", flips the filter for that type and its sibling types;
/// - a magnifier appears only when some type present this turn is filtered; clicking it, or
///   pressing "-", switches between hiding filtered messages (default) and showing them;
/// - in hide mode Next/Previous and the list skip filtered messages, in show mode they step
///   through every message (filtered ones listed dimmed);
/// - the filter starts all-clear when the game is opened and is then restored from the player's
///   own saved filter, so it persists across turns.
/// The pure rules live in Nova.Client.MessageFilter (unit-tested there).
/// </summary>
public class MessagesViewModel : Tool
{
    private readonly MessageFilter filter;

    private readonly string filterPath;

    private readonly List<string> types;

    /// <summary>Every message this turn, filtered or not.</summary>
    public IReadOnlyList<MessageItemViewModel> Messages { get; }

    private IReadOnlyList<MessageItemViewModel> visibleMessages = Array.Empty<MessageItemViewModel>();

    /// <summary>The messages the list shows: all of them in "show filtered" mode, otherwise only
    /// those whose type is not filtered.</summary>
    public IReadOnlyList<MessageItemViewModel> VisibleMessages
    {
        get => visibleMessages;
        private set => SetProperty(ref visibleMessages, value);
    }

    private int currentIndex = -1;

    private MessageItemViewModel? current;

    /// <summary>The current message (null when there is none to show).</summary>
    public MessageItemViewModel? Current
    {
        get => current;
        private set
        {
            if (SetProperty(ref current, value))
            {
                OnPropertyChanged(nameof(HasCurrent));
            }
        }
    }

    public bool HasCurrent => current != null;

    private string positionText = "";

    /// <summary>"Message 3 of 12" style position of the current message.</summary>
    public string PositionText
    {
        get => positionText;
        private set => SetProperty(ref positionText, value);
    }

    private bool isCurrentTypeFiltered;

    /// <summary>True when the current message's type is filtered (red cross), false for a
    /// shown type (blue tick).</summary>
    public bool IsCurrentTypeFiltered
    {
        get => isCurrentTypeFiltered;
        private set
        {
            if (SetProperty(ref isCurrentTypeFiltered, value))
            {
                OnPropertyChanged(nameof(IsCurrentTypeShown));
            }
        }
    }

    public bool IsCurrentTypeShown => !isCurrentTypeFiltered;

    private bool isMagnifierVisible;

    /// <summary>The magnifier is shown only when some type present this turn is filtered.</summary>
    public bool IsMagnifierVisible
    {
        get => isMagnifierVisible;
        private set => SetProperty(ref isMagnifierVisible, value);
    }

    private bool showFiltered;

    /// <summary>"Show filtered messages" mode (the magnifier); false = hide them (default).</summary>
    public bool ShowFiltered
    {
        get => showFiltered;
        private set => SetProperty(ref showFiltered, value);
    }

    private string filterSummary = "";

    /// <summary>How many of this turn's messages are hidden by the filter (empty when none).</summary>
    public string FilterSummary
    {
        get => filterSummary;
        private set
        {
            if (SetProperty(ref filterSummary, value))
            {
                OnPropertyChanged(nameof(HasFilterSummary));
            }
        }
    }

    public bool HasFilterSummary => !string.IsNullOrEmpty(filterSummary);

    public IRelayCommand NextCommand { get; }

    public IRelayCommand PreviousCommand { get; }

    /// <summary>The tick/cross (and the "+" key).</summary>
    public IRelayCommand ToggleFilterCommand { get; }

    /// <summary>The magnifier (and the "-" key).</summary>
    public IRelayCommand ToggleShowFilteredCommand { get; }

    /// <summary>Raised when the player taps a battle message (see MessageItemViewModel.
    /// ReplayCommand). This panel has no reference to the dock layout or the Battle Report
    /// panel itself, so it only announces the request - NovaDockFactory.CreateLayout is what
    /// actually owns both panels and subscribes to bring Battle Report to the front with this
    /// battle selected. Matches this app's established event-forwarding pattern for cross-panel
    /// navigation (e.g. OpenGameViewModel's GameOpened/RaceDesignerRequested events).</summary>
    public event Action<BattleReport>? BattleReplayRequested;

    /// <summary>Raised when an activated message points at a planet, its production queue, the
    /// Research panel or the Technology Browser (Nova.Client.MessageRouting) - the host shell
    /// (NovaDockFactory / MobileMainViewModel) selects the planet and shows the panel.</summary>
    public event Action<MessageDestination>? DestinationRequested;

    public MessagesViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;

        filterPath = MessageFilter.FilePath(clientState.GameFolder, clientState.EmpireState?.Race?.Name ?? "");
        filter = MessageFilter.Load(filterPath);

        // Mineral-packet notices name their planet only in the text (MessageRouting).
        List<string> knownPlanets = clientState.EmpireState?.StarReports.Keys.ToList() ?? new List<string>();

        Messages = clientState.Messages
            .Select((message, index) => new MessageItemViewModel(
                index,
                message,
                report => BattleReplayRequested?.Invoke(report),
                Select,
                destination => DestinationRequested?.Invoke(destination),
                knownPlanets))
            .ToList();

        types = Messages.Select(message => message.Type).ToList();

        NextCommand = new RelayCommand(() => MoveTo(filter.Next(types, currentIndex)), () => filter.Next(types, currentIndex) >= 0);
        PreviousCommand = new RelayCommand(() => MoveTo(filter.Previous(types, currentIndex)), () => filter.Previous(types, currentIndex) >= 0);
        ToggleFilterCommand = new RelayCommand(ToggleFilter, () => current != null);
        ToggleShowFilteredCommand = new RelayCommand(ToggleShowFiltered, () => IsMagnifierVisible || ShowFiltered);

        currentIndex = filter.First(types);
        Refresh();
    }

    private void Select(MessageItemViewModel item)
    {
        currentIndex = item.Index;
        Refresh();
    }

    private void MoveTo(int index)
    {
        if (index >= 0)
        {
            currentIndex = index;
            Refresh();
        }
    }

    private void ToggleFilter()
    {
        if (current == null)
        {
            return;
        }

        // The current message stays selected (now showing a red cross) even if hiding it - the
        // original's single-message pane does the same; Next/Previous then skip its type.
        filter.Toggle(current.Type);
        filter.Save(filterPath);
        Refresh();
    }

    private void ToggleShowFiltered()
    {
        currentIndex = filter.ToggleShowFiltered(types, currentIndex);
        Refresh();
    }

    private void Refresh()
    {
        ShowFiltered = filter.ShowFiltered;
        IsMagnifierVisible = filter.IsMagnifierVisible(types);

        foreach (MessageItemViewModel item in Messages)
        {
            item.IsTypeFiltered = filter.IsFiltered(item.Type);
            item.IsSelected = item.Index == currentIndex;
        }

        // The list shows what Next/Previous can reach, plus the current message itself (which
        // stays on screen after its own type has just been filtered).
        VisibleMessages = Messages
            .Where(item => filter.IsVisible(item.Type) || item.Index == currentIndex)
            .ToList();

        Current = currentIndex >= 0 && currentIndex < Messages.Count ? Messages[currentIndex] : null;
        IsCurrentTypeFiltered = current != null && filter.IsFiltered(current.Type);

        int hidden = filter.ShowFiltered ? 0 : Messages.Count(item => filter.IsFiltered(item.Type));
        FilterSummary = hidden > 0 ? $"{hidden} filtered message(s) hidden" : "";

        PositionText = current != null
            ? $"Message {currentIndex + 1} of {Messages.Count}"
            : (Messages.Count == 0 ? "No messages" : "No message selected");

        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        ToggleFilterCommand.NotifyCanExecuteChanged();
        ToggleShowFilteredCommand.NotifyCanExecuteChanged();
    }
}
