using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Messages panel: this turn's events, oldest first (matching the WinForms Messages
/// control's ordering).
/// </summary>
public class MessagesViewModel : Tool
{
    public IReadOnlyList<MessageItemViewModel> Messages { get; }

    public MessagesViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;

        Messages = clientState.Messages
            .Select(message => new MessageItemViewModel(message))
            .ToList();
    }
}
