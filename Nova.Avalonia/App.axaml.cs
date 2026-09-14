using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Nova.Avalonia.Views;

namespace Nova.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Start at "Open Game" (ports NovaLauncher's Open Game button + SelectRaceDialog)
            // rather than jumping straight into a hardcoded save - see OpenGameViewModel.
            // OpenGameWindow itself swaps desktop.MainWindow over to the real game window once
            // a game has actually been loaded.
            desktop.MainWindow = new OpenGameWindow();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            // Android's newer, activity-recreation-safe single-view hosting: a factory so the
            // host can rebuild the view (e.g. after a configuration change) without this class
            // needing to know why. ShellView is the single-view equivalent of the desktop
            // OpenGameWindow -> MainWindow (-> AboutWindow) swapping above - see its own comment.
            activityLifetime.MainViewFactory = () => new ShellView();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Older/other single-view hosts (e.g. browser) that only support a fixed MainView
            // rather than IActivityApplicationLifetime's factory.
            singleView.MainView = new ShellView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
