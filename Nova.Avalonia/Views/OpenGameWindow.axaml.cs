using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Nova.Avalonia.ViewModels;
using Nova.Client;

namespace Nova.Avalonia.Views;

/// <summary>
/// Desktop-only Window shell around the portable OpenGameView content - see that class for the
/// actual ViewModel/file-picker logic. This class owns only what's genuinely desktop-specific:
/// swapping desktop.MainWindow once a game is opened, and closing this window either way.
/// </summary>
public partial class OpenGameWindow : Window
{
    public OpenGameWindow()
    {
        InitializeComponent();

        OpenGameContent.GameOpened += OnGameOpened;
    }

    private void OnGameOpened(ClientData clientState)
    {
        var mainWindow = new MainWindow(new MainViewModel(clientState));

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = mainWindow;
        }

        mainWindow.Show();
        Close();
    }
}
