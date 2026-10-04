using Avalonia;
using Avalonia.Headless;

// Every [AvaloniaTest] runs on the headless platform's UI thread, against the real Nova App (its
// App.axaml styles - Fluent, DataGrid, Dock - and its ViewLocator), built once for the whole
// assembly: re-creating the Application per test would reload every theme for no benefit.
[assembly: AvaloniaTestApplication(typeof(Nova.Avalonia.Tests.TestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace Nova.Avalonia.Tests;

/// <summary>
/// The headless application the tests run in. Skia drawing (UseHeadlessDrawing = false) so a
/// test can capture a rendered frame and check it is not blank; HarfBuzz for text shaping, the
/// same Inter font the desktop head uses.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        TestGame.PrepareEnvironment();

        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
