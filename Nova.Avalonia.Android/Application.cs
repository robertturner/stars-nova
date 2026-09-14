using System.IO;
using Android.App;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media.Imaging;
using Nova.Common;

namespace Nova.Avalonia.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<Nova.Avalonia.App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();

            // Must happen before anything in the shared app (Common/Nova.Avalonia) tries to load
            // components.xml/Graphics/HelpContent - both AllComponents (via FileSearcher.
            // GetNovaRoot()) and HelpViewModel resolve those relative to this hook, and neither is
            // touched until the user actually opens a game, so doing this synchronously here,
            // ahead of any UI, is simple and safe rather than racing a lazier trigger.
            AssetExtractor.ExtractAll(this);
            PlatformHooks.NovaRootOverride = () => FilesDir!.AbsolutePath;

            // AllRaceIcons.Restore() (Race Designer's icon picker) calls this - same portable
            // Avalonia Bitmap(string) constructor as the desktop head, no Android-specific image
            // API needed.
            PlatformHooks.LoadImage = path => new Bitmap(path);

            // See PlatformHooks.GamesRootOverride's own comment: a new game's default folder
            // can't be a SAF content:// picker result (Gameinitializer/GameSettings.Save/
            // IntelWriter all need plain File/Directory access), so this points new games at the
            // same private writable storage AssetExtractor/NovaRootOverride already use.
            PlatformHooks.GamesRootOverride = () => Path.Combine(FilesDir!.AbsolutePath, "Games");

            // Neither this host nor the desktop one previously wired ShowError/ShowInformation
            // at all, so every Report.Error/Report.Information call anywhere in Common/
            // ServerState (there are many) silently vanished into Console/logcat - confirmed
            // live as a real bug: a failed Load Race file pick reported nothing to the user at
            // all, "dialog closes, nothing happens". A Toast is the simplest native equivalent
            // that needs no extra permissions or a hosted dialog Window (which the single-view
            // Activity has no real concept of for its own root content anyway - see
            // PlatformHooks.TryHandleBackRequest's own comment on that same limitation). Posted
            // through the main-looper Handler since Report.Error/Information can be called from
            // a background thread (e.g. TurnHost's AI processing) and Toasts must be shown from
            // the UI thread.
            Handler mainHandler = new Handler(Looper.MainLooper!);
            PlatformHooks.ShowError = message =>
                mainHandler.Post(() => Toast.MakeText(this, message, ToastLength.Long)?.Show());
            PlatformHooks.ShowInformation = message =>
                mainHandler.Post(() => Toast.MakeText(this, message, ToastLength.Long)?.Show());
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
