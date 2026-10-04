using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Nova.Client.Shell;

namespace Nova.Avalonia.Views;

/// <summary>
/// The small modal boxes of the desktop main window, built in code: the shutdown confirmation
/// (client-ui-dialog-catalog.md "Main workspace frame"), an error box (a recent file that is
/// missing or has no extension; a failed Print Map) and the Print Map page-count dialog
/// (client-interface.md command 213). Each returns a distinct "cancelled" result when closed
/// without a choice (client-ui-dialog-catalog.md "Global dialog contract").
/// </summary>
public static class ShellDialogs
{
    /// <summary>Save / Don't Save / Cancel; closing the box counts as Cancel.</summary>
    public static async Task<ShutdownChoice> AskShutdownAsync(Window owner)
    {
        ShutdownChoice choice = ShutdownChoice.Cancel;
        Window dialog = CreateDialog("Stars! Nova", ShutdownConfirmation.Prompt, out StackPanel buttons);
        AddButton(buttons, "Save", true, () => { choice = ShutdownChoice.Save; dialog.Close(); });
        AddButton(buttons, "Don't Save", false, () => { choice = ShutdownChoice.Discard; dialog.Close(); });
        AddButton(buttons, "Cancel", false, () => { choice = ShutdownChoice.Cancel; dialog.Close(); }, isCancel: true);
        await dialog.ShowDialog(owner);
        return choice;
    }

    public static Task ShowErrorAsync(Window owner, string text)
    {
        Window dialog = CreateDialog("Stars! Nova", text, out StackPanel buttons);
        AddButton(buttons, "OK", true, dialog.Close, isCancel: true);
        return dialog.ShowDialog(owner);
    }

    /// <summary>The page-count dialog: pages across and down (MapPrintLayout); null on Cancel.
    /// The values stay local to the dialog until OK.</summary>
    public static async Task<(int Across, int Down)?> AskPrintPagesAsync(Window owner)
    {
        (int, int)? result = null;
        Window dialog = CreateDialog("Print Map", "Print the map over how many pages? Each page is saved as an image in the game folder.", out StackPanel buttons);
        var across = new NumericUpDown { Minimum = 1, Maximum = MapPrintLayout.MaxPagesPerSide, Value = 1, Increment = 1, FormatString = "0", Width = 120 };
        var down = new NumericUpDown { Minimum = 1, Maximum = MapPrintLayout.MaxPagesPerSide, Value = 1, Increment = 1, FormatString = "0", Width = 120 };
        if (dialog.Content is StackPanel body)
        {
            body.Children.Insert(1, Row("Pages across", across));
            body.Children.Insert(2, Row("Pages down", down));
        }

        AddButton(buttons, "OK", true, () =>
        {
            result = (MapPrintLayout.ClampPages((int)(across.Value ?? 1)), MapPrintLayout.ClampPages((int)(down.Value ?? 1)));
            dialog.Close();
        });
        AddButton(buttons, "Cancel", false, dialog.Close, isCancel: true);
        await dialog.ShowDialog(owner);
        return result;
    }

    private static Control Row(string label, Control field)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, Width = 100, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(field);
        return row;
    }

    private static Window CreateDialog(string title, string text, out StackPanel buttons)
    {
        buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var body = new StackPanel { Margin = new global::Avalonia.Thickness(16), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = text, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, MaxWidth = 380 });
        body.Children.Add(buttons);
        return new Window
        {
            Title = title,
            Content = body,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
    }

    private static void AddButton(StackPanel buttons, string text, bool isDefault, System.Action onClick, bool isCancel = false)
    {
        var button = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => onClick();
        buttons.Children.Add(button);
    }
}
