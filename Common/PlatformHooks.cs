#region Copyright Notice
// ============================================================================
// Copyright (C) 2010-2012 The Stars-Nova Project
//
// This file is part of Stars-Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Injectable hooks for the handful of things Common/ServerState need from the host UI
    /// platform that can't be expressed portably - showing a native file/folder picker, or
    /// reporting a message to the user - when a path or a game-halting error can't be handled any
    /// other way. Common itself has no WinForms/Avalonia/Android dependency; each host (Nova.exe's
    /// WinForms Program.Main, Nova.Avalonia's App, a future Android head) sets these once at
    /// startup to reproduce that platform's native behavior. Left at their defaults (a no-op that
    /// returns null, and writing to <see cref="Console"/>), the affected fallback simply reports
    /// failure/logs instead of prompting - safe for headless/server contexts and any platform that
    /// hasn't wired a real implementation yet.
    /// </summary>
    public static class PlatformHooks
    {
        /// <summary>Ask the user to pick an existing file to open, given a filename/hint to show. Returns the chosen path, or null if cancelled/unavailable.</summary>
        public static Func<string, string> AskUserForFile = _ => null;

        /// <summary>Ask the user where to save a new file, given a dialog title. Returns the chosen path, or null if cancelled/unavailable.</summary>
        public static Func<string, string> AskUserForSaveFile = _ => null;

        /// <summary>Ask the user to pick a folder, given a description. Returns the chosen path, or null if cancelled/unavailable.</summary>
        public static Func<string, string> AskUserForFolder = _ => null;

        /// <summary>Ask the user to pick which race to play from a list of race names found in the game folder. Returns the chosen name, or null if cancelled/unavailable.</summary>
        public static Func<IReadOnlyList<string>, string> AskUserToSelectRace = _ => null;

        /// <summary>Report an informational message to the user (non-fatal).</summary>
        public static Action<string> ShowInformation = message => Console.WriteLine("INFO: " + message);

        /// <summary>Report an error message to the user (non-fatal, execution continues).</summary>
        public static Action<string> ShowError = message => Console.Error.WriteLine("ERROR: " + message);

        /// <summary>Report a fatal error message to the user. The caller still terminates the process afterward.</summary>
        public static Action<string> ShowFatalError = message => Console.Error.WriteLine("FATAL: " + message);

        /// <summary>Report a debug-only message to the user (only ever called from DEBUG builds).</summary>
        public static Action<string> ShowDebug = message => Console.WriteLine("DEBUG: " + message);

        /// <summary>
        /// Offers whatever error history the host has persisted (if any) to the user for sharing
        /// elsewhere - e.g. the mobile burger menu's "Share Error Log" entry, which fires
        /// Android's native Share sheet with the log's contents so a Report.Error the user only
        /// saw as a brief, non-persistent Toast can still be sent on afterward. Returns true if
        /// there was anything to share, false if the log is empty/missing or this host hasn't
        /// wired persistence at all (the default here does neither - see ShowError's own default,
        /// which never writes anywhere a later "share" could read back from).
        /// </summary>
        public static Func<bool> ShareErrorLog = () => false;

        /// <summary>
        /// Same idea as <see cref="ShareErrorLog"/>, but for the separate crash log a genuinely
        /// unhandled exception writes (nova-avalonia-crash.log on Android, nova-avalonia-crash.log
        /// next to the exe on desktop) - unlike a Report.Error/Report.FatalError call, this covers
        /// a crash the app never recovered from at all. Deliberately exposed from the startup
        /// screen (see OpenGameViewModel.ShareCrashLogCommand), not just the in-game burger menu
        /// ShareErrorLog lives in - a crash on "Continue"/"Open" itself means the in-game menu is
        /// never reached, and the startup screen is the one place still guaranteed reachable
        /// right after reproducing it. Returns true if there was anything to share.
        /// </summary>
        public static Func<bool> ShareCrashLog = () => false;

        /// <summary>
        /// Offers arbitrary text to the user for sharing elsewhere - specifically, the mobile
        /// burger menu's "Share Save File" entry, which reads the currently-open game's own
        /// .intel file (plain XML, containing this empire's full state - fleets, stars, starbase
        /// references, everything) and fires this so it can be sent on (e.g. to support/
        /// diagnosis) without needing file-system access to the app's private storage, which
        /// Android's scoped storage otherwise makes awkward to reach even for the user who owns
        /// the data. Takes the text itself (not a path) since only the platform host knows how to
        /// actually invoke a share sheet, while reading the file itself is ordinary, portable
        /// I/O the caller already has to do anyway (to know if there's anything to share at all).
        /// Returns true if the share sheet was actually offered, false if this host hasn't wired
        /// one (the default here does nothing).
        /// </summary>
        public static Func<string, bool> ShareText = _ => false;

        /// <summary>
        /// Loads a previously saved UI theme preference ("Dark" or "Light"), or null if the user
        /// has never chosen one - in which case the host's own default (following the system
        /// theme) should be left alone. Purely a display preference with no effect on game state,
        /// so this stays a plain string rather than an Avalonia ThemeVariant - Common itself has
        /// no Avalonia (or any UI-toolkit) dependency and never interprets the value beyond
        /// passing it back.
        /// </summary>
        public static Func<string> LoadThemePreference = () => null;

        /// <summary>Persists a UI theme preference ("Dark" or "Light") for LoadThemePreference to
        /// read back on the next launch.</summary>
        public static Action<string> SaveThemePreference = _ => { };

        /// <summary>
        /// Load an image from disk, given its path, for a component/ship/race icon. The image
        /// type itself is platform-specific (System.Drawing.Bitmap on WinForms, an Avalonia
        /// Bitmap on Avalonia, etc.) so Common only ever sees it as a plain object - it stores
        /// and hands the reference back to whichever UI layer knows what to do with it, never
        /// interprets it itself. Returns null if unregistered or if loading fails.
        /// </summary>
        public static Func<string, object> LoadImage = _ => null;

        /// <summary>
        /// Runs a loader action against a host-supplied progress UI, blocking until it completes,
        /// and returns whether it succeeded (see <see cref="IProgressCallback"/>). Registered by
        /// the WinForms host to show its ProgressDialog; left null on hosts with no such UI
        /// (Avalonia, Android, headless), in which case AllComponents.Restore() falls back to
        /// AllComponents.RestoreHeadless()'s synchronous, no-progress-UI loading.
        /// </summary>
        public static Func<Action<IProgressCallback>, bool> RunWithProgressDialog = null;

        /// <summary>
        /// Overrides where <see cref="FileSearcher.GetNovaRoot"/> looks for game data
        /// (components.xml, the Graphics folder, nova.conf) instead of its default heuristic of
        /// walking up from the running assembly's own location. Left null (the default) on every
        /// host where that heuristic already works - WinForms and desktop Avalonia both ship
        /// components.xml/Graphics/HelpContent as plain files copied next to the executable, so
        /// "near the running assembly" is already correct there.
        ///
        /// Android has no such "files next to the executable" concept at all - game data ships
        /// bundled inside the APK as assets (read via Android.Content.Res.AssetManager, not plain
        /// File.Open) and the running assembly's own location resolves to an internal runtime
        /// cache path with no meaningful relationship to where any game data lives. The Android
        /// head extracts its bundled assets to the app's private writable storage
        /// (Context.FilesDir) once at startup and points this hook there, so everything
        /// downstream of GetNovaRoot() (component/graphics loading, nova.conf) keeps working
        /// through ordinary File.Open/Path.Combine calls exactly as it already does on desktop -
        /// see Nova.Avalonia.Android's own AssetExtractor and Application.OnCreate.
        /// </summary>
        public static Func<string> NovaRootOverride = null;

        /// <summary>
        /// Gives the current UI a chance to handle an OS-level back-navigation request itself
        /// before the platform applies its own default. Returns true if it did (the platform
        /// should do nothing further), false to let the platform's own default behavior proceed.
        ///
        /// Only meaningful on hosts with their own OS-level back button/gesture and no window
        /// stack of their own to fall back on - i.e. Android. Desktop hosts have real Windows (a
        /// dialog's own title-bar close, Alt+F4, etc.) and never call this. Android's single
        /// Activity has no back *stack* of Activities to pop by default - every screen in this
        /// app (OpenGame's choices/browse sub-screens, the game itself, About) is just swapped
        /// *content* within that one Activity (see ShellView) - so without this hook, Android's
        /// default back behavior is simply to finish the Activity, i.e. exit the whole app,
        /// regardless of which in-app screen is showing. ShellView registers itself here so
        /// Nova.Avalonia.Android's MainActivity can route the OS back button/gesture into the
        /// same in-app navigation its own on-screen "&lt; Back"/Close controls already use.
        /// Left at its default (always "not handled") on every host that never sets it.
        /// </summary>
        public static Func<bool> TryHandleBackRequest = () => false;

        /// <summary>
        /// Overrides where a *new* game's default folder is created, mirroring
        /// <see cref="NovaRootOverride"/>'s exact shape and reasoning. Left null (the default) on
        /// desktop, where a real folder picker/`SpecialFolder.Personal` already makes sense.
        ///
        /// Android's Storage Access Framework folder picker returns a `content://` URI, not a
        /// real filesystem path - <c>IStorageFolder.TryGetLocalPath()</c> on that result comes
        /// back null, and none of `Gameinitializer`/`GameSettings.Save`/`IntelWriter` can write
        /// through a content URI (they all use plain `File`/`Directory` APIs). The Android head
        /// points new games at the same private writable storage (`Context.FilesDir`)
        /// `AssetExtractor`/`NovaRootOverride` already use, for the same reason.
        /// </summary>
        public static Func<string> GamesRootOverride = null;
    }
}
