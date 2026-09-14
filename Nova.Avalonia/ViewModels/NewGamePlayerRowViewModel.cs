using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// One row in New Game's player list - a race picker and a Human/AI picker edited directly
/// inline, unlike the original WinForms wizard (a shared pair of dropdowns below the list edits
/// whichever row is currently selected). Inline editing needs no separate "selected row" state
/// and works better on touch.
/// </summary>
public class NewGamePlayerRowViewModel : ObservableObject
{
    /// <summary>"Human"/"Default AI" only - a custom AI executable path is deliberately not
    /// offered (PlayerSettings.AiProgram for anything but exactly "Human" is written but
    /// discarded downstream - Nova/WinForms/NovaConsole.cs's RunAI() has a literal
    /// "// TODO: Add support for running custom AIs" and never reads it).</summary>
    public static readonly IReadOnlyList<string> AiOptions = new[] { "Human", "Default AI" };

    private string selectedRaceName;

    private string selectedAiProgram = "Human";

    private int displayNumber;

    public NewGamePlayerRowViewModel(ObservableCollection<string> raceOptions, string initialRaceName)
    {
        RaceOptions = raceOptions;
        selectedRaceName = initialRaceName;

        NewRaceCommand = new RelayCommand(() => NewRaceRequested?.Invoke(this));
        LoadRaceCommand = new RelayCommand(() => LoadRaceRequested?.Invoke(this));
    }

    /// <summary>Shared across every row - the same ObservableCollection instance, so adding a
    /// race from any row's "New Race..." button updates every row's dropdown live.</summary>
    public ObservableCollection<string> RaceOptions { get; }

    /// <summary>Instance-accessible mirror of the static <see cref="AiOptions"/> so the view can
    /// bind to it directly (a static member can't be reached from an instance-typed binding).</summary>
    public IReadOnlyList<string> AiProgramOptions => AiOptions;

    public string SelectedRaceName
    {
        get => selectedRaceName;
        set => SetProperty(ref selectedRaceName, value);
    }

    public string SelectedAiProgram
    {
        get => selectedAiProgram;
        set => SetProperty(ref selectedAiProgram, value);
    }

    /// <summary>1-based position in the player list - renumbered by NewGameViewModel whenever
    /// the list changes (add/remove/move), mirroring the original's RenumberPlayers().</summary>
    public int DisplayNumber
    {
        get => displayNumber;
        set => SetProperty(ref displayNumber, value);
    }

    public IRelayCommand NewRaceCommand { get; }

    public IRelayCommand LoadRaceCommand { get; }

    /// <summary>Raised when this row's "New Race..." button is clicked - the parent
    /// NewGameViewModel forwards this so the view can host a RaceDesignerView, then reports the
    /// result back onto this specific row once saved.</summary>
    public event Action<NewGamePlayerRowViewModel>? NewRaceRequested;

    /// <summary>Raised when this row's "Load Race..." button is clicked - the parent
    /// NewGameViewModel forwards this so the view can open a file picker for an existing .race
    /// file, then reports the result back onto this specific row.</summary>
    public event Action<NewGamePlayerRowViewModel>? LoadRaceRequested;
}
