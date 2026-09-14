#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010 stars-nova
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

#region Module Description
// ===========================================================================
// Some error reporting utilities.
// ===========================================================================
#endregion

using System;
using System.Diagnostics;

namespace Nova.Common
{
    /// <summary>
    /// Provides a variety of message pop ups, via PlatformHooks so this class carries no direct
    /// UI-toolkit dependency - each host (WinForms, Avalonia, a future Android head) wires these
    /// to its own native message box at startup.
    /// </summary>
    public static class Report
    {
        /// <summary>
        /// Report an error.
        /// </summary>
        /// <param name="text">Message to display.</param>
        public static void Error(string text)
        {
            PlatformHooks.ShowError("Nova has encountered an error, but will continue anyway." + Environment.NewLine + "Details: " + text);
        }

        /// <summary>
        /// Raise a dialog to report an information message.
        /// </summary>
        /// <param name="text">Message to display.</param>
        public static void Information(string text)
        {
            PlatformHooks.ShowInformation(text);
        }

        /// <summary>
        /// Report a fatal error and terminate the application.
        /// </summary>
        /// <param name="text">Message to display.</param>
        public static void FatalError(string text)
        {
            PlatformHooks.ShowFatalError(text + "\r\n\r\n(This error will terminate the program)");

            Environment.Exit(1);
        }

        /// <summary>
        /// Report Debug Messages if in debugging mode. Otherwise do nothing.
        /// </summary>
        /// <param name="text">Message to display.</param>
        [Conditional("DEBUG")]
        public static void Debug(string text)
        {
            PlatformHooks.ShowDebug(text);
        }
    }
}
