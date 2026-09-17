using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using AndroidX.Core.Content;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media.Imaging;
using Nova.Common;
using JavaFile = Java.IO.File;

namespace Nova.Avalonia.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<Nova.Avalonia.App>
    {
        // App-specific external storage (not FilesDir/internal storage) - reachable with a plain
        // `adb pull`, no `run-as`/root needed, unlike internal storage. A Report.Error the user
        // only saw as a brief Toast (see ShowError's own comment) previously left nothing behind
        // to diagnose it from afterward - this gives it somewhere to land.
        private string ErrorLogPath => Path.Combine((GetExternalFilesDir(null) ?? FilesDir)!.AbsolutePath, "nova-error.log");

        // See LogCrash's own comment for what writes here, and ShareCrashLog for how the user
        // gets it back out without needing adb.
        private string CrashLogPath => Path.Combine((GetExternalFilesDir(null) ?? FilesDir)!.AbsolutePath, "nova-avalonia-crash.log");

        // Internal storage, not external - purely a display preference nobody needs to `adb
        // pull`, unlike the error log above.
        private string ThemePreferencePath => Path.Combine(FilesDir!.AbsolutePath, "theme-preference.txt");

        // Unlike Report.Error/Report.FatalError (handled, deliberate calls - see AppendToErrorLog/
        // ShowError below), a genuine unhandled exception previously left NOTHING behind on this
        // host at all: no log, no Toast, nothing - the process just vanished, confirmed live as a
        // real, serious gap (a user's crash-on-End-Turn, then crash-on-every-subsequent-load, left
        // them with no way to even reach the "Share Error Log" menu entry, since the app never got
        // far enough to render it). Nova.Avalonia.Desktop/Program.cs already had this exact fix
        // (AppDomain.CurrentDomain.UnhandledException logging to "nova-avalonia-crash.log") - this
        // is the missing Android counterpart, registered in the constructor rather than OnCreate
        // so it's live before ANY of OnCreate's own setup (asset extraction, etc.) could crash.
        // AndroidEnvironment.UnhandledExceptionRaiser is ALSO wired, not just AppDomain's own event
        // - an exception thrown from code Android itself calls back into (a UI-thread callback,
        // for instance) can cross the JNI boundary in a way AppDomain.UnhandledException alone
        // doesn't reliably observe on this runtime; between the two, both directions are covered.
        // Neither can stop the process from actually dying (same limitation the desktop version's
        // own comment already notes for its own OS), so this is purely about leaving a real trail
        // behind - written to external storage (a plain `adb pull`, no `run-as`/root needed) next
        // to nova-error.log, and shareable through the exact same burger-menu action once the app
        // can next be launched far enough to reach it.
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => LogCrash(e.ExceptionObject as Exception);
            AndroidEnvironment.UnhandledExceptionRaiser += (sender, e) =>
            {
                LogCrash(e.Exception);
                e.Handled = true;
            };
        }

        private void LogCrash(Exception? exception)
        {
            if (exception == null)
            {
                return;
            }

            try
            {
                string entry = string.Format("{0:u}{1}{2}{1}{1}", DateTime.Now, System.Environment.NewLine, exception);
                File.AppendAllText(CrashLogPath, entry);
            }
            catch
            {
                // Best-effort - the process is already on its way down regardless.
            }
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
            {
                AppendToErrorLog(message);
                mainHandler.Post(() => Toast.MakeText(this, message, ToastLength.Long)?.Show());
            };
            PlatformHooks.ShowInformation = message =>
                mainHandler.Post(() => Toast.MakeText(this, message, ToastLength.Long)?.Show());
            PlatformHooks.ShowFatalError = message =>
            {
                AppendToErrorLog(message);
                mainHandler.Post(() => Toast.MakeText(this, message, ToastLength.Long)?.Show());
            };
            PlatformHooks.ShareErrorLog = ShareErrorLog;
            PlatformHooks.ShareCrashLog = ShareCrashLog;
            PlatformHooks.ShareText = text => ShareText("Share Nova save file", text);

            PlatformHooks.LoadThemePreference = LoadThemePreference;
            PlatformHooks.SaveThemePreference = SaveThemePreference;
        }

        private string LoadThemePreference()
        {
            try
            {
                return File.Exists(ThemePreferencePath) ? File.ReadAllText(ThemePreferencePath).Trim() : null;
            }
            catch
            {
                return null;
            }
        }

        private void SaveThemePreference(string preference)
        {
            try
            {
                File.WriteAllText(ThemePreferencePath, preference ?? "");
            }
            catch
            {
                // Best-effort - worst case the choice just doesn't survive a restart.
            }
        }

        private void AppendToErrorLog(string message)
        {
            try
            {
                string entry = string.Format("{0:u}{1}{2}{1}{1}", DateTime.Now, System.Environment.NewLine, message);
                File.AppendAllText(ErrorLogPath, entry);
            }
            catch
            {
                // Best-effort - the original Toast (or the app itself, for a fatal error) still
                // gets to happen either way.
            }
        }

        private bool ShareErrorLog()
        {
            string text;
            try
            {
                if (!File.Exists(ErrorLogPath))
                {
                    return false;
                }

                text = File.ReadAllText(ErrorLogPath);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return ShareText("Share Nova error log", text);
        }

        private bool ShareCrashLog()
        {
            string text;
            try
            {
                if (!File.Exists(CrashLogPath))
                {
                    return false;
                }

                text = File.ReadAllText(CrashLogPath);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return ShareText("Share Nova crash log", text);
        }

        // Shared by ShareErrorLog/ShareCrashLog and PlatformHooks.ShareText (the in-game "Share
        // Save File" entry). Writes the content to a file under cache/share/ and hands the
        // receiving app a FileProvider content:// Uri (see AndroidManifest.xml's <provider> and
        // Resources/xml/file_paths.xml) rather than stuffing it into Intent.ExtraText directly -
        // a plain text extra silently fails once it crosses the ~1MB Binder transaction limit
        // (StartActivity doesn't throw; the chooser just never actually opens), which this
        // project's own small test saves never hit but a real, long-played game's .intel file
        // routinely does - confirmed live as a "Share Save File does nothing" report. A file-based
        // Uri has no such limit.
        private bool ShareText(string chooserTitle, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                string shareDir = Path.Combine(CacheDir!.AbsolutePath, "share");
                Directory.CreateDirectory(shareDir);
                string sharePath = Path.Combine(shareDir, "shared-text.txt");
                File.WriteAllText(sharePath, text);

                global::Android.Net.Uri uri = FileProvider.GetUriForFile(this, PackageName + ".fileprovider", new JavaFile(sharePath));

                Intent sendIntent = new Intent(Intent.ActionSend);
                sendIntent.SetType("text/plain");
                sendIntent.PutExtra(Intent.ExtraStream, uri);
                sendIntent.AddFlags(ActivityFlags.GrantReadUriPermission);

                Intent chooser = Intent.CreateChooser(sendIntent, chooserTitle);
                chooser!.AddFlags(ActivityFlags.NewTask);
                StartActivity(chooser);
                return true;
            }
            catch (Exception ex)
            {
                // Handled, non-fatal - the process carries on regardless, so this goes to the
                // ordinary error log (visible via the same Share Error Log entry) rather than
                // LogCrash's genuine-crash one.
                AppendToErrorLog("ShareText failed: " + ex);
                return false;
            }
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
