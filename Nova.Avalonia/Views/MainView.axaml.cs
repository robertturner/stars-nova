using Avalonia.Controls;

namespace Nova.Avalonia.Views;

/// <summary>
/// The actual game screen (menu, status bar, docked panels) - a plain UserControl so it can be
/// hosted either as a desktop Window's content (MainWindow) or as content swapped into a
/// single-view shell (Android, once that exists). See ShowAboutCommand/AboutRequested on
/// MainViewModel for how "About" is handled per-host instead of living here.
/// </summary>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }
}
