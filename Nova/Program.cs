#region Copyright Notice
// ============================================================================
// Copyright (C) 2010-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova
{
    using System;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Windows.Forms;

    using Nova.Common;

    public static class Program
    {
        /// <Summary>
        /// The main entry Point for the application.
        /// </Summary>
        [STAThread]
        public static void Main(string[] args)
        {
            // On .NET Framework, a WinForms app with no manifest entry defaulted to DPI-Unaware:
            // Windows bitmap-stretches the whole rendered window to match the monitor's scale
            // factor, so every hand-drawn pixel rectangle (StarMap's stars, HullGrid's cells,
            // BattleViewer, etc. - all of which compute Graphics.Draw*/FillRectangle coordinates
            // directly in device pixels, not DPI-scaled units) still lined up with the
            // auto-scaled standard controls around it, just blurrier on a scaled display. Modern
            // .NET's WinForms defaults to System DPI Aware instead when nothing says otherwise -
            // no compensating stretch happens, so those same hand-drawn pixel rectangles now
            // render undersized/misaligned relative to everything else on any display that isn't
            // at 100% scaling. Restore the original DPI-Unaware behavior explicitly; this must be
            // the very first WinForms API call in the process, before any window/HWND exists.
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);

            // .NET Framework's ambient default WinForms font (used by every Form/control that
            // doesn't explicitly set its own Font, including NovaGUI's main window itself - its
            // Designer.cs never assigns Font or AutoScaleDimensions at all) was "Microsoft Sans
            // Serif, 8.25pt". Modern .NET changed the built-in default to "Segoe UI, 9pt" as a
            // deliberate visual refresh - measurably bigger, and every auto-sizing control
            // (AutoSize=true labels, buttons, group boxes, etc. - used throughout this Designer-
            // generated UI) grows to fit that bigger text, making the whole app look oversized
            // even at 100% display scaling (a font-substitution effect, not a DPI/scaling one -
            // confirmed distinct from the DPI-awareness fix above by testing on an unscaled
            // display, where DPI mode can't be the cause but this still was). Restore the exact
            // original ambient font via the API .NET 6+ added specifically for this migration
            // scenario, before any Form is constructed.
            Application.SetDefaultFont(new Font("Microsoft Sans Serif", 8.25f));

            string firstArgument = args.FirstOrDefault();
            string[] coreArgs = args.Skip(1).ToArray();

            // On .NET Framework, an unhandled exception on the UI thread showed a
            // recoverable "Continue/Quit" dialog by default. On modern .NET, WinForms
            // instead terminates the whole process with no dialog and no accessible
            // stack trace (Windows Error Reporting only logs a generic native fault
            // code). Restore the old, recoverable behavior, and log full exception
            // details somewhere a developer can actually read them.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => HandleException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => HandleException(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            RegisterPlatformHooks();

            switch (firstArgument)
            {
                case CommandArguments.Option.ConsoleSwitch:
                    Application.Run(new WinForms.Console.NovaConsole());
                    break;
                case CommandArguments.Option.ComponentEditorSwitch:
                    WinForms.ComponentEditor.Program.Main();
                    break;
                case CommandArguments.Option.RaceDesignerSwitch:
                    WinForms.RaceDesigner.RaceDesignerForm.Main();
                    break;
                case CommandArguments.Option.GuiSwitch:
                    Nova.WinForms.Gui.NovaGUI gui = new Nova.WinForms.Gui.NovaGUI(args);            
                    Application.Run(gui);
                    break;
                case CommandArguments.Option.NewGameSwitch:
                    Application.Run(new WinForms.NewGameWizard());
                    break;
                case CommandArguments.Option.AiSwitch:
                    Ai.Program.Main(coreArgs);
                    break;
                case CommandArguments.Option.LauncherSwitch:
                case null:
                    Application.Run(new Nova.WinForms.Launcher.NovaLauncher());
                    break;
                case CommandArguments.Option.HelpSwitch:
                    ShowHelpDialog(null, false);
                    break;
                default:
                    ShowErrorDialog(); 
                    break;
            }
        }

        /// <summary>
        /// Wires Nova.Common/Nova.Server's PlatformHooks (Report's message boxes, and the various
        /// "ask the user to locate/save a file" fallbacks in FileSearcher/Config/GameSettings/
        /// ServerData) to real WinForms dialogs, reproducing exactly what those classes used to do
        /// directly before they were decoupled from System.Windows.Forms for portability (see
        /// PROJECT-STATUS.md's Android-portability section). Must run before any code in those
        /// projects can possibly call into one of these hooks.
        /// </summary>
        private static void RegisterPlatformHooks()
        {
            PlatformHooks.ShowError = text => MessageBox.Show(
                text,
                "Nova - Error ",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly);

            PlatformHooks.ShowInformation = text => MessageBox.Show(
                text,
                "Nova - Information",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly);

            PlatformHooks.ShowFatalError = text => MessageBox.Show(
                text,
                "Nova - Fatal Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Stop,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly);

            PlatformHooks.ShowDebug = text => MessageBox.Show(
                text,
                "Nova - Debug",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly);

            PlatformHooks.AskUserForFile = fileName =>
            {
                OpenFileDialog fileDialog = new OpenFileDialog();
                fileDialog.FileName = fileName;
                fileDialog.Title = "Please locate the file \"" + fileName + "\".";
                return fileDialog.ShowDialog() == DialogResult.Cancel ? null : fileDialog.FileName;
            };

            PlatformHooks.AskUserForSaveFile = title =>
            {
                SaveFileDialog fd = new SaveFileDialog();
                fd.Title = title;
                return fd.ShowDialog() == DialogResult.OK ? fd.FileName : null;
            };

            PlatformHooks.AskUserForFolder = description =>
            {
                FolderBrowserDialog folderBrowser = new FolderBrowserDialog();
                folderBrowser.RootFolder = Environment.SpecialFolder.Desktop;
                folderBrowser.SelectedPath = FileSearcher.GetNovaRoot();
                folderBrowser.Description = description;
                return folderBrowser.ShowDialog() == DialogResult.OK ? folderBrowser.SelectedPath : null;
            };

            PlatformHooks.LoadImage = path => new Bitmap(path);

            PlatformHooks.AskUserToSelectRace = raceNames =>
            {
                var raceDialog = new Nova.Client.SelectRaceDialog();
                foreach (string name in raceNames)
                {
                    raceDialog.RaceList.Items.Add(name);
                }

                raceDialog.RaceList.SelectedIndex = 0;

                string selected = raceDialog.ShowDialog() == DialogResult.Cancel
                    ? null
                    : raceDialog.RaceList.SelectedItem as string;

                raceDialog.Dispose();
                return selected;
            };

            PlatformHooks.RunWithProgressDialog = loadAction =>
            {
                var progress = new Nova.ControlLibrary.ProgressDialog();
                progress.Text = "Loading Components";
                ThreadPool.QueueUserWorkItem(_ => loadAction(progress));
                progress.ShowDialog();
                return progress.Success;
            };
        }

        /// <Summary>
        /// Logs an unhandled exception's full details to a file next to the executable
        /// (Windows Error Reporting only captures a generic native fault code for a .NET
        /// process crash, not the managed exception or its stack trace) and shows the
        /// user a recoverable error dialog rather than letting the process die silently.
        /// </Summary>
        private static void HandleException(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            try
            {
                string logPath = Path.Combine(Application.StartupPath, "nova-crash.log");
                string entry = string.Format(
                    "{0:u}{1}{2}{1}{1}",
                    DateTime.Now,
                    Environment.NewLine,
                    exception);
                File.AppendAllText(logPath, entry);
            }
            catch
            {
                // Logging is best-effort; don't let a failure to write the log
                // prevent the error dialog below from being shown.
            }

            MessageBox.Show(
                exception.Message + Environment.NewLine + Environment.NewLine +
                "Full details have been written to nova-crash.log next to the executable." + Environment.NewLine +
                "You can usually continue, but consider saving and restarting soon.",
                "Stars! Nova - Unexpected Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private static void ShowErrorDialog()
        {
            string message = string.Format(
                "Invalid command line arguments:{0}{1}",
                Environment.NewLine,
                Environment.CommandLine);

            ShowHelpDialog(message, true);
        }

        private static void ShowHelpDialog(string message, bool error)
        {
            if (!string.IsNullOrEmpty(message))
            {
                // Add error message at the top.
                message += Environment.NewLine + Environment.NewLine;
            }

            message += string.Format(
                "Supported command line arguments{0}" + 
                "===================={0}" +
                "Start Launcher{0}" +
                "    [{1}]{0}" +
                "Start New Game Wizard{0}" +
                "    {2}{0}" +
                "Start Race Designer{0}" +
                "    {3}{0}" +
                "Start Component Editor{0}" +
                "    {4}{0}" +
                "Start Console{0}" +
                "    {5}{0}" +
                "Start GUI{0}" +
                "    {6} {9} <race> {10} <turn> {11} <intel file> {12} <state file>{0}" +
                "Run AI{0}" +
                "    {7} {9} <race> {10} <turn> {11} <intel file>{0}" +
                "Display this help screen{0}" +
                "    {8}",
                Environment.NewLine,
                CommandArguments.Option.LauncherSwitch,
                CommandArguments.Option.NewGameSwitch,
                CommandArguments.Option.RaceDesignerSwitch,
                CommandArguments.Option.ComponentEditorSwitch,
                CommandArguments.Option.ConsoleSwitch,
                CommandArguments.Option.GuiSwitch,
                CommandArguments.Option.AiSwitch,
                CommandArguments.Option.HelpSwitch,
                CommandArguments.Option.RaceName,
                CommandArguments.Option.Turn,
                CommandArguments.Option.IntelFileName,
                CommandArguments.Option.StateFileName);

            MessageBox.Show(
                message,
                "Stars! Nova " + (error ? "Error" : "Information"),
                MessageBoxButtons.OK,
                (error ? MessageBoxIcon.Error : MessageBoxIcon.Information),
                MessageBoxDefaultButton.Button1,
                MessageBoxOptions.DefaultDesktopOnly);
        }
    }
}
