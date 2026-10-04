using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.Input;

using Nova.Client;
using Nova.Common;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Messages panel. Tapping a row makes it the current message (the one the
/// filter tick/cross and Next/Previous work from - see MessagesViewModel); a message whose Event
/// is a BattleReport (see BattleEngine.ReportBattle) also jumps straight to that battle in the
/// Battle Report panel - see MessagesViewModel.BattleReplayRequested. ReplayCommand is ALWAYS a
/// real, always-enabled command rather than null/CanExecute-gated - confirmed live that either
/// of those makes Avalonia's Button render the row as dimmed/disabled, which every ordinary
/// (non-battle) message would otherwise wrongly look like.
/// </summary>
public class MessageItemViewModel : ViewModelBase
{
    /// <summary>This message's position in the turn's full message list.</summary>
    public int Index { get; }

    public string Text { get; }

    public string Type { get; }

    /// <summary>True for a message that can be tapped to open its battle - drives the "Tap to
    /// view battle" hint in MessagesView.</summary>
    public bool CanReplay { get; }

    public IRelayCommand ReplayCommand { get; }

    private bool isSelected;

    /// <summary>True for the current message.</summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    private bool isTypeFiltered;

    /// <summary>True when this message's type is filtered - such a row is only listed in "show
    /// filtered messages" mode, drawn dimmed.</summary>
    public bool IsTypeFiltered
    {
        get => isTypeFiltered;
        set
        {
            if (SetProperty(ref isTypeFiltered, value))
            {
                OnPropertyChanged(nameof(RowOpacity));
            }
        }
    }

    public double RowOpacity => isTypeFiltered ? 0.45 : 1.0;

    public MessageItemViewModel(Message message, Action<BattleReport>? onReplay)
        : this(0, message, onReplay, null)
    {
    }

    /// <summary>Where activating this message goes (Nova.Client.MessageRouting:
    /// client-ui-dialog-catalog.md "Message-click routing" - 62/63 open the named planet's
    /// production queue, other planet notices select the planet).</summary>
    public MessageDestination Destination { get; }

    /// <summary>The row's "tap to ..." hint, empty when the message goes nowhere.</summary>
    public string GotoHint { get; }

    public bool HasGotoHint => GotoHint.Length > 0;

    public MessageItemViewModel(int index, Message message, Action<BattleReport>? onReplay, Action<MessageItemViewModel>? onSelect)
        : this(index, message, onReplay, onSelect, null)
    {
    }

    /// <param name="knownPlanets">The planets the player knows of, so a mineral-packet notice can
    /// select the planet it names (MessageRouting.Destination).</param>
    public MessageItemViewModel(int index, Message message, Action<BattleReport>? onReplay, Action<MessageItemViewModel>? onSelect, Action<MessageDestination>? onGoto,
        IEnumerable<string>? knownPlanets = null)
    {
        Index = index;
        Text = message.Text ?? "";
        Type = message.Type ?? "";

        BattleReport? battleReport = message.Event as BattleReport;
        CanReplay = battleReport != null;
        Destination = MessageRouting.Destination(message, knownPlanets);
        GotoHint = Destination.Kind switch
        {
            MessageDestinationKind.BattleReplay => "Tap to view battle",
            MessageDestinationKind.ProductionQueue => "Tap to open the production queue",
            MessageDestinationKind.Planet => "Tap to select the planet",
            MessageDestinationKind.Research => "Tap to open Research",
            MessageDestinationKind.TechnologyBrowser => "Tap to open the Technology Browser",
            _ => "",
        };

        ReplayCommand = new RelayCommand(() =>
        {
            onSelect?.Invoke(this);
            if (battleReport != null)
            {
                onReplay?.Invoke(battleReport);
            }
            else if (Destination.Kind != MessageDestinationKind.None)
            {
                onGoto?.Invoke(Destination);
            }
        });
    }
}
