using System;
using Nova.Common;

namespace Nova.Avalonia;

/// <summary>
/// Reads and writes the client's own preferences in its config file (Nova.Common.Config,
/// nova.conf) - the shell settings of behavior-specs-10/client-interface.md "Persisted settings"
/// (toolbar layout and visibility, window layout preset, autosave interval, sound bits, recent
/// files). A preference that cannot be read or written is treated as absent / left unsaved: a
/// lost preference only means the default next session.
/// </summary>
public static class ClientPreferences
{
    public static string? Read(string key)
    {
        try
        {
            // Read without disposing: Config.Dispose writes the file back.
            return new Config()[key];
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Write(string key, string value)
    {
        try
        {
            using var conf = new Config();
            conf[key] = value;
        }
        catch (Exception)
        {
            // Unsaved preference: the default applies next session.
        }
    }
}
