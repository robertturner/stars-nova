using System;
using System.Reflection;
using Avalonia.Controls;

namespace Nova.Avalonia.Views;

/// <summary>
/// The About screen's actual content, as a plain UserControl so it can be hosted either in a
/// desktop modal Window (AboutWindow) or as content swapped into a single-view shell (Android,
/// once that exists) - see AboutWindow's own comment for why "About" stays a one-shot screen
/// rather than a docked panel either way. Raises CloseRequested instead of calling Window.Close()
/// directly, since this control has no Window of its own to close.
/// </summary>
public partial class AboutView : UserControl
{
    public event Action? CloseRequested;

    public AboutView()
    {
        InitializeComponent();

        // VersionInfo.VersionNumber (the source WinForms AboutBox.cs reads) isn't usable
        // directly here - VersionInfo.cs is linked into both Nova.csproj and ControlLibrary
        // .csproj, so referencing it from an assembly that pulls in both is ambiguous. Reading
        // this assembly's own version is a safe, unambiguous equivalent.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        this.FindControl<TextBlock>("VersionText")!.Text = version != null ? $"Version {version.Major}.{version.Minor}.{version.Build}" : "";

        this.FindControl<Button>("CloseButton")!.Click += (_, _) => CloseRequested?.Invoke();
    }
}
