using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using Avalonia.Android;
using Nova.Common;

namespace Nova.Avalonia.Android;

[Activity(
    Label = "Stars! Nova",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// This single Activity hosts every screen in the app as swapped *content* (see ShellView),
    /// not a back stack of Activities - so without handling this ourselves, the OS back
    /// button/gesture's default behavior is to finish the Activity (exit the app) regardless of
    /// which in-app screen is showing, rather than navigating back within it.
    ///
    /// Overriding the legacy Activity.OnBackPressed() does NOT work here - confirmed live (added
    /// a temporary log line; it was never invoked) - because on this API level the platform
    /// dispatches back-navigation through OnBackPressedDispatcher, and once anything doesn't
    /// register a callback there, the dispatcher goes straight to its own default (finish the
    /// Activity) without ever calling the old override. Registering a real
    /// OnBackPressedCallback below is the actual fix.
    ///
    /// The callback always intercepts first (giving PlatformHooks.TryHandleBackRequest, which
    /// ShellView registers itself against, first refusal). When nothing in-app has anywhere left
    /// to go back to, it briefly disables itself and re-invokes the dispatcher - the standard
    /// AndroidX pattern for "sometimes handle back, otherwise fall through to the platform
    /// default" - which then reaches the dispatcher's own default (finishing the Activity), and
    /// re-enables itself immediately after for the next back press.
    /// </summary>
    private sealed class BackCallback : OnBackPressedCallback
    {
        private readonly MainActivity activity;

        public BackCallback(MainActivity activity) : base(true)
        {
            this.activity = activity;
        }

        public override void HandleOnBackPressed()
        {
            if (PlatformHooks.TryHandleBackRequest())
            {
                return;
            }

            Enabled = false;
            activity.OnBackPressedDispatcher.OnBackPressed();
            Enabled = true;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
    }
}
