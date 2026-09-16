using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Nova.Avalonia.Views;
using Nova.Common;

namespace Nova.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Restores a theme the user explicitly chose (see MobileMainViewModel.IsDarkMode) on a
    /// previous launch - left untouched (this XAML's own "Default", i.e. follow the system
    /// theme) if nothing has ever been saved, or on desktop, which has no UI to set this at all
    /// yet (PlatformHooks.LoadThemePreference's default no-op there).
    ///
    /// Called from both OnFrameworkInitializationCompleted below (fine for desktop, where
    /// OpenGameWindow is created in that same method so timing can't be an issue) AND from
    /// ShellView's own constructor - confirmed live as a real, necessary duplication: on Android,
    /// AvaloniaAndroidApplication's own bootstrapping runs OnFrameworkInitializationCompleted
    /// before Nova.Avalonia.Android.Application.OnCreate ever gets to register
    /// PlatformHooks.LoadThemePreference at all, so calling this only here left the very first
    /// screen (OpenGameView, before ShellView ever swaps it out) stuck on the system theme no
    /// matter what had been saved. ShellView's own constructor, by contrast, only ever runs once
    /// Android's normal Activity-after-Application lifecycle guarantees OnCreate has already
    /// registered the hook - this method is safe to call twice, so both call sites just stay.
    /// </summary>
    public static void ApplySavedThemePreference()
    {
        string savedTheme = PlatformHooks.LoadThemePreference();
        if (savedTheme == "Dark")
        {
            Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
        else if (savedTheme == "Light")
        {
            Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplySavedThemePreference();

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
