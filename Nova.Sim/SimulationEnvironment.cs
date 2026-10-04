namespace Nova.Sim
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>Thrown in place of Report.FatalError's Environment.Exit while a simulation runs,
    /// so a fatal game error becomes a reportable failure instead of killing the process (or the
    /// test runner).</summary>
    public sealed class SimulationFatalErrorException : Exception
    {
        public SimulationFatalErrorException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Isolates a simulation from the process-wide state the game keeps: the nova.conf file
    /// (Gameinitializer and ClientData.Initialize read and write ServerFolder through it, and
    /// nova.conf normally lives in the shared Nova root, where a test or a second sim would trample
    /// it), the PlatformHooks error reporters, and the GameSettings singleton. Enter() points
    /// PlatformHooks.NovaRootOverride at a private folder holding its own nova.conf (with the real
    /// components.xml and Graphics folder registered), captures Report.Error text, turns
    /// Report.FatalError into an exception, and installs a fresh GameSettings; Dispose() puts
    /// everything back. Only one environment can be active per process (GameSettings and
    /// PlatformHooks are static), so simulations in one process run one at a time.
    /// </summary>
    public sealed class SimulationEnvironment : IDisposable
    {
        private static readonly object Gate = new object();
        private static SimulationEnvironment active;

        private readonly Func<string> previousRoot;
        private readonly Action<string> previousError;
        private readonly Action<string> previousFatal;
        private readonly Action<string> previousInformation;
        private readonly Action<string> previousDebug;
        private readonly GameSettings previousSettings;
        private bool disposed;

        /// <summary>Report.Error texts since the last <see cref="TakeErrors"/>.</summary>
        private readonly List<string> errors = new List<string>();

        public string RootFolder { get; }

        public string ComponentsFile { get; }

        private SimulationEnvironment(string rootFolder, string componentsFile)
        {
            RootFolder = rootFolder;
            ComponentsFile = componentsFile;

            previousRoot = PlatformHooks.NovaRootOverride;
            previousError = PlatformHooks.ShowError;
            previousFatal = PlatformHooks.ShowFatalError;
            previousInformation = PlatformHooks.ShowInformation;
            previousDebug = PlatformHooks.ShowDebug;
            previousSettings = GameSettings.Data;
        }

        /// <summary>
        /// Activates an isolated environment rooted at <paramref name="rootFolder"/> (created if
        /// needed). <paramref name="componentsFile"/> null means <see cref="LocateComponentsFile"/>.
        /// </summary>
        public static SimulationEnvironment Enter(string rootFolder, string componentsFile = null)
        {
            lock (Gate)
            {
                if (active != null)
                {
                    throw new InvalidOperationException("A simulation environment is already active in this process; simulations share static game state and must run one at a time.");
                }

                // Locate the real data files BEFORE the override changes where Nova looks.
                string components = componentsFile ?? LocateComponentsFile();
                if (components == null || !File.Exists(components))
                {
                    throw new FileNotFoundException("components.xml not found; pass --components <path> or set NOVA_COMPONENTS.", components ?? "components.xml");
                }

                components = Path.GetFullPath(components);
                Directory.CreateDirectory(rootFolder);

                SimulationEnvironment environment = new SimulationEnvironment(Path.GetFullPath(rootFolder), components);
                environment.Install();
                active = environment;
                return environment;
            }
        }

        private void Install()
        {
            PlatformHooks.NovaRootOverride = () => RootFolder;
            PlatformHooks.ShowError = message =>
            {
                lock (errors)
                {
                    errors.Add(message);
                }
            };
            PlatformHooks.ShowFatalError = message => throw new SimulationFatalErrorException(message);
            PlatformHooks.ShowInformation = _ => { };
            PlatformHooks.ShowDebug = _ => { };

            ResetGameSettings();

            // A private nova.conf that knows where the shared data files are.
            using (Config conf = new Config())
            {
                conf[Global.ComponentFileKey] = ComponentsFile;
                string graphics = LocateGraphicsFolder(ComponentsFile);
                if (graphics != null)
                {
                    conf[Global.GraphicsFolderKey] = graphics;
                }
            }

            // AllComponents is a process-wide cache: if another game/test loaded it first, the
            // components carry image paths resolved against that load's Nova root, and every
            // later game inherits them (changing the saved state). Drop it so this run reloads
            // the definitions through the private nova.conf just written, then lets the next
            // run do the same (Dispose).
            AllComponents.ResetCache();
        }

        /// <summary>Replaces the GameSettings singleton with a fresh default instance (its
        /// constructor is private, hence the reflection).</summary>
        public static void ResetGameSettings()
        {
            GameSettings.Data = (GameSettings)Activator.CreateInstance(typeof(GameSettings), nonPublic: true);
        }

        /// <summary>Points the private nova.conf's ServerFolder at a game folder: the AI's
        /// ClientData.Initialize finds its game folder through it.</summary>
        public void SetGameFolder(string gameFolder)
        {
            using (Config conf = new Config())
            {
                conf[Global.ServerFolderKey] = gameFolder;
            }
        }

        /// <summary>Returns and clears the Report.Error texts captured so far.</summary>
        public List<string> TakeErrors()
        {
            lock (errors)
            {
                List<string> taken = new List<string>(errors);
                errors.Clear();
                return taken;
            }
        }

        /// <summary>
        /// Finds components.xml: the NOVA_COMPONENTS environment variable, then the Nova root the
        /// game itself would use, then every parent of the executable's folder and of the current
        /// directory.
        /// </summary>
        public static string LocateComponentsFile()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable("NOVA_COMPONENTS");
            if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
            {
                return fromEnvironment;
            }

            List<string> starts = new List<string>();
            if (PlatformHooks.NovaRootOverride == null)
            {
                try
                {
                    starts.Add(FileSearcher.GetNovaRoot());
                }
                catch (Exception)
                {
                    // fall through to the directory walk
                }
            }

            starts.Add(AppContext.BaseDirectory);
            starts.Add(Directory.GetCurrentDirectory());

            foreach (string start in starts)
            {
                DirectoryInfo directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, Global.ComponentFileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            return null;
        }

        /// <summary>
        /// The Graphics folder: next to components.xml, else in any parent of it, of the
        /// executable's folder or of the current directory. Ship designs pick their icons from it
        /// when loaded, and a missing folder makes every design load fail (see docs/SIMULATION.md).
        /// </summary>
        public static string LocateGraphicsFolder(string componentsFile)
        {
            List<string> starts = new List<string>();
            if (componentsFile != null)
            {
                starts.Add(Path.GetDirectoryName(Path.GetFullPath(componentsFile)));
            }

            starts.Add(AppContext.BaseDirectory);
            starts.Add(Directory.GetCurrentDirectory());
            foreach (string start in starts)
            {
                DirectoryInfo directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    string candidate = Path.Combine(directory.FullName, Global.GraphicsFolderName);
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }

                    directory = directory.Parent;
                }
            }

            return null;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lock (Gate)
            {
                PlatformHooks.NovaRootOverride = previousRoot;
                PlatformHooks.ShowError = previousError;
                PlatformHooks.ShowFatalError = previousFatal;
                PlatformHooks.ShowInformation = previousInformation;
                PlatformHooks.ShowDebug = previousDebug;
                GameSettings.Data = previousSettings;
                if (active == this)
                {
                    active = null;
                }

                // The run's components were loaded through this environment's private Nova root;
                // drop them so anything after this reloads against the restored root.
                AllComponents.ResetCache();
            }
        }
    }
}
