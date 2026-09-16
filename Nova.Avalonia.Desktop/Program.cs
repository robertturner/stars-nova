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
        RegisterErrorLogging();

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

    /// <summary>
    /// Neither Report.Error nor Report.FatalError was ever wired on this host - both fell through
    /// to PlatformHooks.ShowError/ShowFatalError's own default (Console.Error.WriteLine), which
    /// vanishes on a desktop app with no attached console. Logs the same non-fatal/handled errors
    /// this session's mobile counterpart now persists too (see Nova.Avalonia.Android/
    /// Application.cs's own comment) into a sibling file next to the unhandled-crash log - this
    /// host has plain file access already, so unlike Android there's no need for an in-app "share"
    /// action, just somewhere for the text to land instead of disappearing.
    /// </summary>
    private static void RegisterErrorLogging()
    {
        PlatformHooks.ShowError = message => AppendToLog("nova-error.log", message);
        PlatformHooks.ShowFatalError = message => AppendToLog("nova-error.log", message);
    }

    private static void AppendToLog(string fileName, string text)
    {
        try
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, fileName);
            string entry = string.Format("{0:u}{1}{2}{1}{1}", DateTime.Now, Environment.NewLine, text);
            File.AppendAllText(logPath, entry);
        }
        catch
        {
            // Best-effort, same as LogCrash below.
        }
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
