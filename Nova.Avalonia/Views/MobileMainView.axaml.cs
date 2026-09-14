using Avalonia.Controls;

namespace Nova.Avalonia.Views;

/// <summary>
/// Android's game screen - a plain UserControl (same reasoning as MainView: hostable as content
/// swapped into the single-view shell). See MobileMainViewModel's own comment for why this
/// exists as a completely separate screen from the desktop dock layout rather than a variant of
/// it. AboutRequested/TurnAdvanced are handled per-host exactly like MainViewModel's, via
/// ShellView - nothing host-specific lives here.
/// </summary>
public partial class MobileMainView : UserControl
{
    public MobileMainView()
    {
        InitializeComponent();
    }
}
