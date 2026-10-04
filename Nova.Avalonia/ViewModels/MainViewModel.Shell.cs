using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client.Shell;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>One recent-file menu item (File menu, client-interface.md ids 4300-4308).</summary>
public sealed class RecentFileEntry
{
    public RecentFileEntry(int index, string path)
    {
        Path = path;
        Caption = RecentFiles.MenuCaption(index, path);
    }

    public string Path { get; }

    public string Caption { get; }
}

/// <summary>One toolbar button: the same command (and so the same enablement) as its menu
/// item (client-interface.md "Application shell": a toolbar button and its menu command must
/// have the same availability, effect and enablement state).</summary>
public sealed class ToolbarButtonViewModel
{
    public ToolbarButtonViewModel(ToolbarButtonInfo info, ICommand? command, object? parameter)
    {
        Id = info.Id;
        Glyph = info.Glyph;
        Tooltip = info.Tooltip;
        Command = command;
        CommandParameter = parameter;
    }

    public string Id { get; }

    public string Glyph { get; }

    public string Tooltip { get; }

    public ICommand? Command { get; }

    public object? CommandParameter { get; }

    /// <summary>The window-layout preset drop-down rather than a button.</summary>
    public bool IsLayoutPreset => Id == ToolbarLayout.LayoutPresetId;

    public bool IsButton => !IsLayoutPreset;
}

/// <summary>
/// The desktop main window's shell features (behavior-specs-10/client-interface.md and
/// client-ui-dialog-catalog.md; rules in Nova.Client.Shell): the title built from the race name,
/// the 9-slot Recent Files list, the configurable vertical toolbar and View > Toolbar, the
/// three-way Window Layout preset, the two (grayed) sound toggles, File > Print Map, the
/// autosave interval and the global hotkey relay's actions.
/// </summary>
public partial class MainViewModel
{
    private ToolbarLayout toolbarLayout = ToolbarLayout.Default();
    private SoundPreferences soundPreferences = new SoundPreferences(false, null!, null!);
    private DispatcherTimer? autosaveTimer;

    // ---------------- title ----------------

    /// <summary>The window title: the game, the player's race (plural, via the race-name
    /// builder) and year (client-ui-dialog-catalog.md "Title and status area").</summary>
    public string WindowTitle { get; private set; } = "Stars! Nova";

    public string GameFolder => clientState.GameFolder;

    public string RaceName => clientState.EmpireState.Race?.Name ?? "";

    public int TurnYear => clientState.EmpireState.TurnYear;

    /// <summary>Shows a line in the status bar (for host-side actions such as Print Map).</summary>
    public void ReportStatus(string message)
    {
        StatusMessage = message;
    }

    // ---------------- recent files ----------------

    private IReadOnlyList<RecentFileEntry> recentFileEntries = Array.Empty<RecentFileEntry>();

    public IReadOnlyList<RecentFileEntry> RecentFileEntries
    {
        get => recentFileEntries;
        private set => SetProperty(ref recentFileEntries, value);
    }

    /// <summary>A recent-file item: the host checks the file and loads it through the same path
    /// as Open (RecentFileRequested).</summary>
    public IRelayCommand<string> OpenRecentFileCommand { get; private set; } = null!;

    public event Action<string>? RecentFileRequested;

    public void RefreshRecentFiles()
    {
        RecentFiles recent = GameSession.ReadRecentFiles();
        RecentFileEntries = recent.Entries.Select((path, index) => new RecentFileEntry(index, path)).ToList();
    }

    // ---------------- print map ----------------

    /// <summary>File > Print Map (command 213): the host shows the page-count dialog, then writes
    /// the pages (PrintMapRequested).</summary>
    public IRelayCommand PrintMapCommand { get; private set; } = null!;

    public event Action? PrintMapRequested;

    // ---------------- toolbar ----------------

    public ObservableCollection<ToolbarButtonViewModel> ToolbarButtons { get; } = new ObservableCollection<ToolbarButtonViewModel>();

    private bool showToolbar = ToolbarLayout.DefaultVisible;

    /// <summary>View > Toolbar (command 179): shows or hides the strip; persisted.</summary>
    public bool ShowToolbar
    {
        get => showToolbar;
        set
        {
            if (SetProperty(ref showToolbar, value))
            {
                ClientPreferences.Write(ToolbarLayout.VisiblePreferenceKey, ToolbarLayout.FormatVisible(value));
            }
        }
    }

    public IRelayCommand ToggleToolbarCommand { get; private set; } = null!;

    /// <summary>Catalog buttons the user has removed (the toolbar's "Add" menu).</summary>
    public IReadOnlyList<ToolbarButtonInfo> HiddenToolbarButtons => toolbarLayout.Hidden;

    public void MoveToolbarButton(string id, bool up)
    {
        if (up ? toolbarLayout.MoveUp(id) : toolbarLayout.MoveDown(id))
        {
            ToolbarChanged();
        }
    }

    public void RemoveToolbarButton(string id)
    {
        if (toolbarLayout.Remove(id))
        {
            ToolbarChanged();
        }
    }

    public void AddToolbarButton(string id)
    {
        if (toolbarLayout.Add(id))
        {
            ToolbarChanged();
        }
    }

    public void ResetToolbar()
    {
        toolbarLayout.Reset();
        ToolbarChanged();
    }

    private void ToolbarChanged()
    {
        ClientPreferences.Write(ToolbarLayout.PreferenceKey, toolbarLayout.Format());
        RebuildToolbar();
    }

    private void RebuildToolbar()
    {
        ToolbarButtons.Clear();
        foreach (string id in toolbarLayout.Ids)
        {
            ToolbarButtonInfo? info = ToolbarLayout.Find(id);
            if (info == null)
            {
                continue;
            }

            (ICommand? command, object? parameter) = ToolbarCommand(id);
            ToolbarButtons.Add(new ToolbarButtonViewModel(info, command, parameter));
        }

        OnPropertyChanged(nameof(HiddenToolbarButtons));
    }

    private (ICommand? Command, object? Parameter) ToolbarCommand(string id)
    {
        switch (id)
        {
            case "Save": return (SaveCommand, null);
            case "Generate": return (SubmitTurnCommand, null);
            case "Find": return (StarMap.Search.OpenCommand, null);
            case "ZoomIn": return (StarMap.ZoomInCommand, null);
            case "ZoomOut": return (StarMap.ZoomOutCommand, null);
            case "TechnologyBrowser": return (ToggleTechnologyBrowserCommand, null);
            case ToolbarLayout.LayoutPresetId: return (null, null);
            default: return (ShowPanelCommand, id);
        }
    }

    // ---------------- window layout ----------------

    public IReadOnlyList<string> WindowLayoutLabels => WindowLayout.Labels;

    private WindowLayoutPreset windowLayout = WindowLayout.Default;

    /// <summary>The preset as 0-2 (the toolbar's drop-down; the View menu's radio items).
    /// Changing it relayouts at once and is persisted.</summary>
    public int SelectedWindowLayoutIndex
    {
        get => (int)windowLayout;
        set
        {
            if (value < 0 || value > 2)
            {
                return;
            }

            if (SetProperty(ref windowLayout, (WindowLayoutPreset)value))
            {
                ClientPreferences.Write(WindowLayout.PreferenceKey, WindowLayout.Format(windowLayout));
            }

            dockFactory.ApplyWindowLayout(windowLayout);
            WindowLayoutChecks = Enumerable.Range(0, 3).Select(index => index == (int)windowLayout).ToList();
        }
    }

    private IReadOnlyList<bool> windowLayoutChecks = Array.Empty<bool>();

    public IReadOnlyList<bool> WindowLayoutChecks
    {
        get => windowLayoutChecks;
        private set => SetProperty(ref windowLayoutChecks, value);
    }

    public IRelayCommand<string> SetWindowLayoutCommand { get; private set; } = null!;

    // ---------------- sound ----------------

    /// <summary>Commands > Battle Sound Effects / Music (Ctrl+M): grayed - this client has no
    /// sound support (client-interface.md "Runtime menu state").</summary>
    public IRelayCommand ToggleSoundEffectsCommand { get; private set; } = null!;

    public IRelayCommand ToggleMusicCommand { get; private set; } = null!;

    public bool SoundEffectsChecked => soundPreferences.SoundEffects;

    public bool MusicChecked => soundPreferences.Music;

    // ---------------- setup ----------------

    private void InitializeShell()
    {
        Race race = clientState.EmpireState.Race;
        WindowTitle = RaceNameText.WindowTitle(
            "Stars! Nova",
            GameSettings.Data?.GameName,
            race?.Name,
            race?.PluralName,
            clientState.EmpireState.TurnYear);

        OpenRecentFileCommand = new RelayCommand<string>(path =>
        {
            if (!string.IsNullOrEmpty(path))
            {
                RecentFileRequested?.Invoke(path);
            }
        });
        RefreshRecentFiles();

        PrintMapCommand = new RelayCommand(() => PrintMapRequested?.Invoke());

        toolbarLayout = ToolbarLayout.Parse(ClientPreferences.Read(ToolbarLayout.PreferenceKey));
        showToolbar = ToolbarLayout.ParseVisible(ClientPreferences.Read(ToolbarLayout.VisiblePreferenceKey));
        ToggleToolbarCommand = new RelayCommand(() => ShowToolbar = !ShowToolbar);
        RebuildToolbar();

        SetWindowLayoutCommand = new RelayCommand<string>(index =>
        {
            if (int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                SelectedWindowLayoutIndex = value;
            }
        });
        windowLayout = WindowLayout.Parse(ClientPreferences.Read(WindowLayout.PreferenceKey));
        SelectedWindowLayoutIndex = (int)windowLayout;

        soundPreferences = new SoundPreferences(
            hasSoundSupport: false,
            ClientPreferences.Read(SoundPreferences.SoundEffectsKey)!,
            ClientPreferences.Read(SoundPreferences.MusicKey)!);
        ToggleSoundEffectsCommand = new RelayCommand(
            () =>
            {
                if (soundPreferences.ToggleSoundEffects())
                {
                    ClientPreferences.Write(SoundPreferences.SoundEffectsKey, SoundPreferences.Format(soundPreferences.SoundEffects));
                    OnPropertyChanged(nameof(SoundEffectsChecked));
                }
            },
            () => soundPreferences.HasSoundSupport);
        ToggleMusicCommand = new RelayCommand(
            () =>
            {
                if (soundPreferences.ToggleMusic())
                {
                    ClientPreferences.Write(SoundPreferences.MusicKey, SoundPreferences.Format(soundPreferences.Music));
                    OnPropertyChanged(nameof(MusicChecked));
                }
            },
            () => soundPreferences.HasSoundSupport);

        StartAutosave();
    }

    // ---------------- autosave ----------------

    /// <summary>
    /// Saves the orders every AutosaveInterval milliseconds while they have unsaved changes
    /// (client-interface.md "Navigation controls"; the interval is read from the config file's
    /// AutosaveInterval key, falling back to the previous value - here the 5000 default - when it
    /// is out of range). Not while a turn is being submitted.
    /// </summary>
    private void StartAutosave()
    {
        int interval = AutosaveInterval.Resolve(ClientPreferences.Read(AutosaveInterval.PreferenceKey), AutosaveInterval.Default);
        autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
        autosaveTimer.Tick += (_, _) =>
        {
            if (SubmitTurnCommand.IsRunning || !HasUnsavedChanges)
            {
                return;
            }

            try
            {
                SaveOrders();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Autosave failed: {ex.Message}";
            }
        };
        autosaveTimer.Start();
    }

    /// <summary>Stops this screen's timers when the host replaces it (a new turn, another game)
    /// - an old screen must never autosave over the new turn's files.</summary>
    public void Detach()
    {
        autosaveTimer?.Stop();
        autosaveTimer = null;
    }

    // ---------------- hotkey relay ----------------

    private InspectorViewModel? Inspector => dockFactory.GetPanel<InspectorViewModel>("Inspector");

    private MessagesViewModel? Messages => dockFactory.GetPanel<MessagesViewModel>(MessagesPanel);

    /// <summary>The relay's "route-editing mode": a fleet waypoint row is selected.</summary>
    public bool IsRouteEditing => Inspector is { IsFleetSelected: true, HasSelectedWaypoint: true };

    /// <summary>Carries out one hotkey-relay action (Nova.Client.Shell.HotkeyRelay); false when
    /// nothing was done (the key then goes on to the focused control).</summary>
    public bool HandleRelayAction(RelayAction action)
    {
        switch (action)
        {
            case RelayAction.ClosePopup:
                if (StarMap.Search.IsOpen)
                {
                    StarMap.Search.IsOpen = false;
                    return true;
                }

                if (Progress.HasFailed)
                {
                    Progress.End();
                    return true;
                }

                return false;

            case RelayAction.DeleteWaypoint:
                return Execute(Inspector?.DeleteSelectedWaypointCommand);

            case RelayAction.PreviousMessage:
                return Execute(Messages?.PreviousCommand);

            case RelayAction.NextMessage:
                return Execute(Messages?.NextCommand);

            case RelayAction.StepFieldUp:
            case RelayAction.StepFieldDown:
                InspectorViewModel? inspector = Inspector;
                if (inspector == null || !IsRouteEditing)
                {
                    return false;
                }

                inspector.SelectedWaypointWarp = HotkeyRelay.StepField(inspector.SelectedWaypointWarp, action == RelayAction.StepFieldUp);
                return true;

            default:
                return false;
        }
    }

    private static bool Execute(ICommand? command)
    {
        if (command == null || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }
}
