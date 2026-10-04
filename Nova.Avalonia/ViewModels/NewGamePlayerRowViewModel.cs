using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Ai;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// One row in New Game's player list - a race picker and a Human/AI picker edited directly
/// inline, unlike the original WinForms wizard (a shared pair of dropdowns below the list edits
/// whichever row is currently selected). Inline editing needs no separate "selected row" state
/// and works better on touch.
///
/// "Built-in AI" is the setup screen's AI opponent menu (behavior-specs-10/ai-opponent-behavior.md
/// section 1a): one of six archetypes or Random, and one of four skill tiers or Random. At game
/// creation (NewGameViewModel.CreateGame) a Random tier is drawn 0-3, then a Random archetype
/// 0-5, the slot plays the archetype x tier template race of section 14 (AiRaceTemplates), and
/// the archetype becomes the slot's AI category (PlayerSettings.AiCategory, which TurnHost passes
/// to DefaultAi as its personality code). "Default AI" keeps the older behaviour: Nova's AI
/// playing the race chosen in this row with its default personality.
/// </summary>
public class NewGamePlayerRowViewModel : ObservableObject
{
    public const string Human = "Human";
    public const string DefaultAi = "Default AI";
    public const string BuiltInAi = "Built-in AI";

    /// <summary>A custom AI executable path is deliberately not offered (PlayerSettings.AiProgram
    /// for anything but exactly "Human" is treated as "run Nova's own AI" - neither TurnHost nor
    /// NovaConsole.RunAI launches anything else).</summary>
    public static readonly IReadOnlyList<string> AiOptions = new[] { Human, DefaultAi, BuiltInAi };

    /// <summary>The six archetypes then "Random" (index 6 = AiRaceTemplates.RandomArchetype).</summary>
    public static readonly IReadOnlyList<string> ArchetypeLabels = AiRaceTemplates.ArchetypeNames.Concat(new[] { "Random" }).ToList();

    /// <summary>The four tiers then "Random" (index 4 = AiRaceTemplates.RandomTier).</summary>
    public static readonly IReadOnlyList<string> TierLabels = AiRaceTemplates.TierNames.Concat(new[] { "Random" }).ToList();

    private string selectedRaceName;

    private string selectedAiProgram = Human;

    private int selectedArchetypeIndex = AiRaceTemplates.RandomArchetype;

    private int selectedTierIndex = AiRaceTemplates.Standard;

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

    public IReadOnlyList<string> ArchetypeOptions => ArchetypeLabels;

    public IReadOnlyList<string> TierOptions => TierLabels;

    public string SelectedRaceName
    {
        get => selectedRaceName;
        set => SetProperty(ref selectedRaceName, value);
    }

    public string SelectedAiProgram
    {
        get => selectedAiProgram;
        set
        {
            if (value != null && SetProperty(ref selectedAiProgram, value))
            {
                OnPropertyChanged(nameof(IsBuiltInAi));
                OnPropertyChanged(nameof(ChoosesOwnRace));
            }
        }
    }

    /// <summary>True when this slot plays one of the 24 built-in AI templates.</summary>
    public bool IsBuiltInAi => SelectedAiProgram == BuiltInAi;

    /// <summary>The race picker and New/Load buttons only apply to a human or "Default AI" slot.</summary>
    public bool ChoosesOwnRace => !IsBuiltInAi;

    /// <summary>0-5 an archetype, 6 Random.</summary>
    public int SelectedArchetypeIndex
    {
        get => selectedArchetypeIndex;
        set => SetProperty(ref selectedArchetypeIndex, Math.Clamp(value, 0, AiRaceTemplates.RandomArchetype));
    }

    /// <summary>0-3 a tier (Easy, Standard, Tough, Expert), 4 Random.</summary>
    public int SelectedTierIndex
    {
        get => selectedTierIndex;
        set => SetProperty(ref selectedTierIndex, Math.Clamp(value, 0, AiRaceTemplates.RandomTier));
    }

    /// <summary>The AiProgram written to PlayerSettings: both AI kinds run Nova's DefaultAi.</summary>
    public string AiProgramForSettings => IsBuiltInAi ? DefaultAi : SelectedAiProgram;

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
