using System;
using System.IO;
using Android.Content;
using Android.Content.Res;

namespace Nova.Avalonia.Android;

/// <summary>
/// Copies this app's bundled game data (components.xml, Graphics/, HelpContent/ - packaged as
/// AndroidAsset items, see Nova.Avalonia.Android.csproj) out of the read-only APK asset store and
/// into the app's private writable storage (Context.FilesDir), so everything downstream
/// (AllComponents, HelpViewModel) can keep reading it through ordinary File.Open/Path.Combine
/// exactly as it already does on desktop - see PlatformHooks.NovaRootOverride's own comment for
/// why this extraction step exists at all: Android's AssetManager (stream-based, path-listing
/// only) isn't a drop-in replacement for plain file I/O, so Common was never taught to speak it
/// directly, and doing so would mean threading an Android-specific API through a project that's
/// deliberately kept portable.
/// </summary>
public static class AssetExtractor
{
    /// <summary>
    /// Extracts every bundled asset under "Assets\" (see the csproj's AndroidAsset Link paths,
    /// which drop that prefix once packaged - the in-APK root actually read here is "") into
    /// <paramref name="context"/>'s FilesDir, overwriting any copy already there. Always
    /// re-copying (rather than skipping files that look already-extracted) is a deliberate
    /// simplification, not an oversight - see TryExtractFile's own comment for the real bug an
    /// earlier length-comparison "skip if unchanged" attempt caused. The data bundled here is
    /// small enough (a few tens of MB at most) that re-copying every launch is not a noticeable
    /// startup cost; a smarter cache (e.g. a version-marker file compared against the app's own
    /// PackageInfo) would be a reasonable future optimization if that ever changes.
    /// </summary>
    public static void ExtractAll(Context context)
    {
        string destinationRoot = context.FilesDir!.AbsolutePath;
        ExtractDirectory(context.Assets!, "", destinationRoot);
    }

    private static void ExtractDirectory(AssetManager assets, string assetPath, string destinationRoot)
    {
        string[]? entries = assets.List(assetPath);
        if (entries == null)
        {
            return;
        }

        foreach (string entry in entries)
        {
            string childAssetPath = assetPath.Length == 0 ? entry : $"{assetPath}/{entry}";
            string destinationPath = Path.Combine(destinationRoot, childAssetPath.Replace('/', Path.DirectorySeparatorChar));

            if (TryExtractFile(assets, childAssetPath, destinationPath))
            {
                continue;
            }

            // AssetManager has no direct "is this a file or a directory" query - Open() throwing
            // is the standard way to tell the two apart (a directory's List() still succeeds).
            Directory.CreateDirectory(destinationPath);
            ExtractDirectory(assets, childAssetPath, destinationRoot);
        }
    }

    private static bool TryExtractFile(AssetManager assets, string assetPath, string destinationPath)
    {
        try
        {
            using Stream source = assets.Open(assetPath);

            // A previous version of this method tried to skip re-copying a file whose extracted
            // copy already existed with a matching length ("if (File.Exists(...) &&
            // new FileInfo(...).Length == source.Length) return true;"), to avoid redoing the
            // work on every launch. That broke in a way that only showed up on the *second*
            // launch: AssetManager.Open() returns a stream that doesn't support .Length at all
            // for assets the APK stores compressed (confirmed live - it threw for a bundled
            // .ico), which this method's own catch-all swallowed and misread as "not a file, must
            // be a directory" - colliding with the real file already there and crashing the app
            // outright via Directory.CreateDirectory. Always copying sidesteps that class of bug
            // entirely rather than trying to patch the length check (see ExtractAll's own comment
            // for why the resulting extra I/O is an acceptable, deliberate tradeoff).
            using FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            source.CopyTo(destination);
            return true;
        }
        catch (Exception)
        {
            // Open() throws for a directory path (and for any genuinely missing entry, which
            // shouldn't happen for anything List() itself just returned) - either way, this
            // wasn't a file, so let the caller recurse into it as a directory instead.
            return false;
        }
    }
}
