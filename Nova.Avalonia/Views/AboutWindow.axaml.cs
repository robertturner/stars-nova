using Avalonia.Controls;

namespace Nova.Avalonia.Views;

/// <summary>
/// Ports Nova/WinForms/AboutBox.cs - a one-shot informational popup, not a docked panel, so
/// (unlike every other ported dialog in this app) this stays a plain modal Window rather than a
/// Tool in the dock layout; there's nothing here a player would want to keep visible alongside
/// the game. Desktop-only shell around the portable AboutView content - see that class for the
/// actual UI/version-text logic.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        AboutContent.CloseRequested += Close;
    }
}
