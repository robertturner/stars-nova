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
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Whether the client's pending orders have changed since they were last written to disk -
    /// the "unsaved state" the main frame's shutdown confirmation asks about
    /// (client-ui-dialog-catalog.md "Main workspace frame") and the autosave tick checks.
    /// Compares the order list by object identity, so adding, removing or replacing an order all
    /// count as changes. (Nova keeps every edit as an order object in ClientData.Commands.)
    /// </summary>
    public sealed class UnsavedChangesTracker
    {
        private object[] saved = Array.Empty<object>();

        /// <summary>Records the current orders as written to disk.</summary>
        public void MarkSaved(IEnumerable<object> orders)
        {
            saved = (orders ?? Enumerable.Empty<object>()).ToArray();
        }

        public bool IsDirty(IEnumerable<object> orders)
        {
            object[] current = (orders ?? Enumerable.Empty<object>()).ToArray();
            if (current.Length != saved.Length)
            {
                return true;
            }

            for (int i = 0; i < current.Length; i++)
            {
                if (!ReferenceEquals(current[i], saved[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The player's answer to the shutdown confirmation.</summary>
    public enum ShutdownChoice
    {
        /// <summary>Save the pending orders, then close.</summary>
        Save,

        /// <summary>Close without saving.</summary>
        Discard,

        /// <summary>Stay in the game.</summary>
        Cancel,
    }

    /// <summary>
    /// The main frame's controlled shutdown (behavior-specs-10/client-ui-dialog-catalog.md "Main
    /// workspace frame": "When unsaved state or an active session requires a decision, it opens a
    /// confirmation flow rather than terminating immediately. It may save or prepare pending
    /// session data before completing the close action").
    /// SPEC GAP: the prompt text and its buttons are not given. Stand-ins: <see cref="Prompt"/>
    /// with Save / Don't Save / Cancel. The prompt is shown only when a game is open AND its
    /// orders have unsaved changes (an open game whose orders are all on disk closes directly).
    /// </summary>
    public static class ShutdownConfirmation
    {
        /// <summary>SPEC GAP seam: the prompt text.</summary>
        public const string Prompt = "Your orders have changed since they were last saved. Save them before closing?";

        public static bool IsNeeded(bool gameOpen, bool unsavedChanges)
        {
            return gameOpen && unsavedChanges;
        }

        /// <summary>What to do for an answer: (save first, then close).</summary>
        public static (bool Save, bool Close) Resolve(ShutdownChoice choice)
        {
            switch (choice)
            {
                case ShutdownChoice.Save:
                    return (true, true);
                case ShutdownChoice.Discard:
                    return (false, true);
                default:
                    return (false, false);
            }
        }
    }

    /// <summary>
    /// The progress surface's lifecycle (behavior-specs-10/client-ui-dialog-catalog.md "Progress
    /// and failure feedback": a numeric/visual completion indicator updated repeatedly during
    /// lengthy work; "a guard flag tracks whether its window already exists; if not, it is created
    /// (modeless) and left alive, and a separate update entry point pushes new progress values
    /// into the existing window on subsequent calls. An explicit teardown call destroys the window
    /// and clears the guard flag ..., so a second lengthy operation started before teardown reuses
    /// the same window instead of stacking a duplicate"; "A separate recovery or failure surface
    /// explains that an operation could not proceed and offers only safe follow-up choices; it
    /// must not silently fabricate a success result"; client-interface.md: "Long-running actions
    /// use a progress dialog with a gauge").
    /// The host creates/destroys its window on <see cref="Created"/> / <see cref="Destroyed"/>
    /// and redraws on <see cref="Updated"/>.
    /// </summary>
    public sealed class ProgressSurface
    {
        public event Action Created;

        public event Action Updated;

        public event Action Destroyed;

        /// <summary>The guard flag: the progress window exists.</summary>
        public bool Exists { get; private set; }

        public string Caption { get; private set; } = string.Empty;

        public int Value { get; private set; }

        public int Maximum { get; private set; } = 1;

        /// <summary>Set by <see cref="Fail"/>: the failure surface's explanation.</summary>
        public string FailureMessage { get; private set; }

        public bool HasFailed => FailureMessage != null;

        /// <summary>Completion 0-100.</summary>
        public int Percent => Maximum <= 0 ? 0 : Math.Max(0, Math.Min(100, Value * 100 / Maximum));

        /// <summary>
        /// The update entry point: creates the window on first use (guard flag clear), otherwise
        /// pushes the new values into the existing one. Clears an earlier failure.
        /// </summary>
        public void Report(string caption, int value, int maximum)
        {
            Caption = caption ?? string.Empty;
            Maximum = Math.Max(1, maximum);
            Value = Math.Max(0, Math.Min(Maximum, value));
            FailureMessage = null;

            if (!Exists)
            {
                Exists = true;
                Created?.Invoke();
            }

            Updated?.Invoke();
        }

        /// <summary>Teardown: destroys the window and clears the guard flag.</summary>
        public void End()
        {
            if (!Exists)
            {
                return;
            }

            Exists = false;
            FailureMessage = null;
            Destroyed?.Invoke();
        }

        /// <summary>
        /// Replaces the gauge with the failure surface (the window stays until the player closes
        /// it with <see cref="End"/>). Never reports completion.
        /// </summary>
        public void Fail(string message)
        {
            FailureMessage = string.IsNullOrEmpty(message) ? "The operation could not be completed." : message;
            if (!Exists)
            {
                Exists = true;
                Created?.Invoke();
            }

            Updated?.Invoke();
        }
    }

    /// <summary>
    /// The stages of End Turn shown on the progress surface. SPEC GAP: the original's progress
    /// captions and step counts are not given; these are Nova's own.
    /// </summary>
    public static class TurnProgressStages
    {
        public const int SavingOrders = 0;
        public const int WaitingForComputerPlayers = 1;
        public const int GeneratingTurn = 2;
        public const int LoadingNewTurn = 3;
        public const int Count = 4;

        public static readonly IReadOnlyList<string> Captions = new[]
        {
            "Saving orders...",
            "Waiting for computer players...",
            "Generating the turn...",
            "Loading the new turn...",
        };
    }
}
