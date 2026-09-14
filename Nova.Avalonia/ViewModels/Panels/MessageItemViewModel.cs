using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Messages panel.
/// </summary>
public class MessageItemViewModel
{
    public string Text { get; }

    public string Type { get; }

    public MessageItemViewModel(Message message)
    {
        Text = message.Text ?? "";
        Type = message.Type ?? "";
    }
}
