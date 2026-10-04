#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

namespace Nova.Client.Shell
{
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>The View > Window Layout presets (client-interface.md, commands 130-132).</summary>
    public enum WindowLayoutPreset
    {
        LargeScreen = 0,
        MediumScreen = 1,
        SmallScreen = 2,
    }

    /// <summary>
    /// The three-way window-layout preset (behavior-specs-10/client-interface.md "Navigation
    /// controls": "Large Screen" / "Medium Screen" / "Small Screen", chosen from the View menu's
    /// Window Layout sub-popup or the toolbar's preset drop-down; selecting one stores mode 0-2,
    /// redraws the map and recomputes the workspace layout; the current layout is checked).
    /// SPEC GAP: what each preset changes in the layout, and which one a fresh client starts
    /// with, are not given (only that "Small Screen" passes an extra flag to a layout helper).
    /// Stand-ins: <see cref="SidePaneProportion"/> (the share of the window each side/bottom
    /// panel group gets) and <see cref="Default"/>.
    /// </summary>
    public static class WindowLayout
    {
        public const string PreferenceKey = "WindowLayout";

        /// <summary>The spec's captions, index = mode.</summary>
        public static readonly IReadOnlyList<string> Labels = new[] { "Large Screen", "Medium Screen", "Small Screen" };

        /// <summary>SPEC GAP seam: the starting preset.</summary>
        public const WindowLayoutPreset Default = WindowLayoutPreset.LargeScreen;

        /// <summary>SPEC GAP seam: the panel-group proportion per preset (Large keeps the layout
        /// this client always had; a smaller screen gives the panels a larger share).</summary>
        public static double SidePaneProportion(WindowLayoutPreset preset)
        {
            switch (preset)
            {
                case WindowLayoutPreset.MediumScreen:
                    return 0.26;
                case WindowLayoutPreset.SmallScreen:
                    return 0.30;
                default:
                    return 0.22;
            }
        }

        public static WindowLayoutPreset Parse(string stored)
        {
            if (int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                && value >= 0 && value <= 2)
            {
                return (WindowLayoutPreset)value;
            }

            return Default;
        }

        public static string Format(WindowLayoutPreset preset)
        {
            return ((int)preset).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The autosave/backup interval preference (behavior-specs-10/client-interface.md "Navigation
    /// controls": "clamped to the range 100-30,000 ..., defaulting to 5,000; ... uses its own
    /// previous value as the fallback default when the stored value is out of range, rather than
    /// always resetting to 5,000").
    /// SPEC GAP: the unit ("milliseconds or an equivalent internal unit") and what the original
    /// actually saves on each tick. Stand-ins: milliseconds; the client saves its orders/state file
    /// only when it has unsaved changes (<see cref="UnsavedChangesTracker"/>).
    /// Ambiguity: "clamped" and "falls back to the previous value when out of range" disagree for
    /// an out-of-range value; the more specific fallback rule is used (an out-of-range value is
    /// ignored, not pulled to the nearest limit).
    /// </summary>
    public static class AutosaveInterval
    {
        public const string PreferenceKey = "AutosaveInterval";

        public const int Minimum = 100;

        public const int Maximum = 30000;

        public const int Default = 5000;

        /// <summary>The stored value when it is a number in range, otherwise <paramref name="previous"/>.</summary>
        public static int Resolve(string stored, int previous)
        {
            if (int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                && value >= Minimum && value <= Maximum)
            {
                return value;
            }

            return previous;
        }
    }

    /// <summary>
    /// The two persisted sound toggles (behavior-specs-10/client-interface.md command table:
    /// Commands > Battle Sound Effects (2450) and Music (2451, Ctrl+M 2452), each toggling one
    /// persisted bit and checked when on; "The two sound items are grayed when no sound support is
    /// available"). This client has no audio, so <see cref="HasSoundSupport"/> is false and both
    /// items stay grayed; the stored bits are kept so a client with audio can honour them.
    /// SPEC GAP: the bits' starting values. Stand-in: both on.
    /// </summary>
    public sealed class SoundPreferences
    {
        public const string SoundEffectsKey = "SoundEffects";

        public const string MusicKey = "Music";

        public SoundPreferences(bool hasSoundSupport, string storedSoundEffects, string storedMusic)
        {
            HasSoundSupport = hasSoundSupport;
            SoundEffects = storedSoundEffects != "0";
            Music = storedMusic != "0";
        }

        public bool HasSoundSupport { get; }

        public bool SoundEffects { get; private set; }

        public bool Music { get; private set; }

        /// <summary>Flips the sound-effects bit; refused (false) without sound support.</summary>
        public bool ToggleSoundEffects()
        {
            if (!HasSoundSupport)
            {
                return false;
            }

            SoundEffects = !SoundEffects;
            return true;
        }

        /// <summary>Flips the music bit; refused (false) without sound support.</summary>
        public bool ToggleMusic()
        {
            if (!HasSoundSupport)
            {
                return false;
            }

            Music = !Music;
            return true;
        }

        public static string Format(bool value) => value ? "1" : "0";
    }
}
