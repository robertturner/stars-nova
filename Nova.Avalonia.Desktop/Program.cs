using Avalonia;
using Avalonia.Media.Imaging;
using System;
using System.IO;
using Nova.Common;

namespace Nova.Avalonia;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Unlike Nova.exe (see its Program.cs HandleException), this app previously had no
        // unhandled-exception logging at all - a crash just vanished with nothing to diagnose
        // it from afterward. AppDomain.UnhandledException can't stop the process from
        // terminating (unlike WinForms' recoverable Application.ThreadException, which Avalonia
        // has no direct equivalent of), but logging what actually happened, right next to the
        // executable, is still a strict improvement over nothing.
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => LogCrash(e.ExceptionObject as Exception);

        RegisterPlatformHooks();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Was previously entirely unset on this host - AllRaceIcons.Restore() (needed for Race
    /// Designer's icon picker) calls PlatformHooks.LoadImage and got nothing back on Avalonia
    /// desktop until now. Avalonia's own Bitmap(string) constructor is the direct portable
    /// equivalent of WinForms' `new Bitmap(path)` (see Nova/Program.cs's own registration) - no
    /// desktop-specific API needed, so this same expression is reused verbatim in
    /// Nova.Avalonia.Android/Application.cs.
    /// </summary>
    private static void RegisterPlatformHooks()
    {
        PlatformHooks.LoadImage = path => new Bitmap(path);
    }

    private static void LogCrash(Exception exception)
    {
        if (exception == null)
        {
            return;
        }

        try
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "nova-avalonia-crash.log");
            string entry = string.Format(
                "{0:u}{1}{2}{1}{1}",
                DateTime.Now,
                Environment.NewLine,
                exception);
            File.AppendAllText(logPath, entry);
        }
        catch
        {
            // Logging is best-effort - the process is already on its way down regardless.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
