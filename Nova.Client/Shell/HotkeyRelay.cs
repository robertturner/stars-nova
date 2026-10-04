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
    using System;

    /// <summary>The keys the relay looks at (anything else is <see cref="Other"/>).</summary>
    public enum RelayKey
    {
        Other,
        Escape,
        Digit,
        Delete,
        Backspace,
        LeftBracket,
        RightBracket,
        Comma,
        Period,
    }

    /// <summary>What the relay does with a key.</summary>
    public enum RelayAction
    {
        /// <summary>Not intercepted: the key reaches the focused control normally.</summary>
        None,

        /// <summary>Escape: close the tracked popup (Find, an open failure box).</summary>
        ClosePopup,

        /// <summary>A top-row digit: the shared view-option slots (Nova.Client.Map.MapViewOptions).</summary>
        ViewOptionDigit,

        /// <summary>Delete/Backspace: remove the selected waypoint.</summary>
        DeleteWaypoint,

        /// <summary>[ : previous message.</summary>
        PreviousMessage,

        /// <summary>] : next message.</summary>
        NextMessage,

        /// <summary>, : step the selected row's 0-11 field down.</summary>
        StepFieldDown,

        /// <summary>. : step the selected row's 0-11 field up.</summary>
        StepFieldUp,
    }

    /// <summary>
    /// The fixed-key global hotkey relay (behavior-specs-10/client-interface.md "Reusable control
    /// behavior": keeps a handful of shortcuts working while the focus sits in a side panel -
    /// Escape closes a tracked popup; the digit keys 1-9, 0 and Shift+0 go to the view-option
    /// slots "rather than to whichever control currently has focus"; Delete and Backspace remove
    /// the selected waypoint "only while a specific route-editing mode is active"; [ and ] step to
    /// the previous/next message; , and . "step a bounded (0-11) per-item field of the currently
    /// selected production/order-list row up or down, again only while the same route-editing
    /// mode is active"; keys outside this set are not intercepted).
    /// Choices made here: a focused text field keeps every key except Escape (typing must still
    /// type); Ctrl/Alt chords are never intercepted (they are menu accelerators); the
    /// "route-editing mode" is "a fleet waypoint row is selected".
    /// Ambiguity: the 0-11 field is read as the selected waypoint's speed (warp 0-10 plus the
    /// Stargate setting 11), and which of , and . is "up" is not stated: , steps down and . up.
    /// Ambiguity: the Escape/Enter accept-cancel forwarding into the object-information panel is
    /// not relayed (the panel has no accept/cancel of its own in this client).
    /// </summary>
    public static class HotkeyRelay
    {
        /// <summary>The 0-11 bound of the stepped field.</summary>
        public const int FieldMinimum = 0;

        public const int FieldMaximum = 11;

        public static RelayAction Classify(RelayKey key, bool ctrlOrAlt, bool focusInTextEntry, bool routeEditing)
        {
            if (ctrlOrAlt)
            {
                return RelayAction.None;
            }

            if (key == RelayKey.Escape)
            {
                return RelayAction.ClosePopup;
            }

            if (focusInTextEntry)
            {
                return RelayAction.None;
            }

            switch (key)
            {
                case RelayKey.Digit:
                    return RelayAction.ViewOptionDigit;
                case RelayKey.Delete:
                case RelayKey.Backspace:
                    return routeEditing ? RelayAction.DeleteWaypoint : RelayAction.None;
                case RelayKey.LeftBracket:
                    return RelayAction.PreviousMessage;
                case RelayKey.RightBracket:
                    return RelayAction.NextMessage;
                case RelayKey.Comma:
                    return routeEditing ? RelayAction.StepFieldDown : RelayAction.None;
                case RelayKey.Period:
                    return routeEditing ? RelayAction.StepFieldUp : RelayAction.None;
                default:
                    return RelayAction.None;
            }
        }

        /// <summary>One , / . step of the bounded field.</summary>
        public static int StepField(int value, bool up)
        {
            int next = value + (up ? 1 : -1);
            return Math.Max(FieldMinimum, Math.Min(FieldMaximum, next));
        }
    }
}
