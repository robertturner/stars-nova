using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using CommunityToolkit.Mvvm.Input;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Race Designer's main view model - the native Avalonia replacement for
/// Nova/WinForms/RaceDesigner/RaceDesigner.cs, exposing every field the original dialog lets a
/// player set. Follows this app's established event-forwarding pattern (see OpenGameViewModel):
/// this class has no TopLevel/StorageProvider access of its own, so Save/Load's actual file
/// pickers live in RaceDesignerView's code-behind, which calls back into <see cref="CompleteSave"/>
/// or <see cref="TryLoadFromStream"/> once a file is chosen.
///
/// behavior-specs-10/race-designer-ui-and-availability.md:
/// - <b>Draft-local working copy</b> ("Cross-stage persistence", "Trait stage"): every control
///   edits a copy of the race; the race passed in is only changed when Save succeeds
///   (accept), and Cancel discards the copy. Moving between sections carries the draft along;
///   "Undo section changes" restores just the current section from the values the designer
///   opened with (a stage-level cancel).
/// - <b>Earlier selections lock later controls</b>: picking Alternate Reality resets and
///   disables economy rows 2-7 and clears the Germanium discount; the research checkbox names
///   tech level 4 for Jack of All Trades.
/// - <b>Non-editable context</b>: constructed with isEditable false, every mutable control is
///   disabled while the values stay visible.
/// - <b>Preset archetypes</b>: the eight Identity-stage choices, Random included (RacePresets).
/// - <b>Steppers</b>: the seven economic rows step by 1, 3 with Shift, with auto-repeat.
/// - <b>Help</b>: available from any section; it only shows the manual's matching topic.
/// </summary>
public class RaceDesignerViewModel : ViewModelBase
{
    /// <summary>
    /// The designer's pages, switched via <see cref="SelectedPageLabel"/>'s dropdown rather than
    /// a TabControl or a row of buttons - TabControl's default tab strip wraps onto several
    /// lines once its header text no longer fits one row (confirmed live on a phone-width
    /// Android emulator: six tabs, one titled "Leftover Points", wrapped to five stacked rows),
    /// crushing the actual page content down to a sliver; a hand-rolled horizontally-scrolling
    /// button row avoids the wrapping but turned out to have its own live-confirmed touch bug -
    /// tapping a button inside that ScrollViewer shifted its scroll position (bring-focused-
    /// element-into-view) without ever raising the button's Click. A ComboBox sidesteps both
    /// problems and is a control already proven to work reliably via touch elsewhere in this
    /// exact screen (the primary-trait and leftover-points pickers).
    ///
    /// <see cref="Identity"/> bundles name/plural name/password/primary trait/icon into one
    /// page - previously these sat in a permanently-visible header above the page selector,
    /// which ate into every other page's screen space. Folding them into their own page (the
    /// dropdown's default selection) means every OTHER page gets the full content area to
    /// itself, and the selector itself moves to the very top of the screen, above all of it.
    /// </summary>
    public enum Page
    {
        Identity,
        Traits,
        Environment,
        Production,
        Research,
        LeftoverPoints,
    }

    private static readonly (string Label, Page Value, RaceDraftSection Section, int HelpTopic)[] PageDefinitions =
    {
        // Help topics are entries of the shipped manual's own HelpContent/topics.tsv.
        ("Identity", Page.Identity, RaceDraftSection.Identity, 249),
        ("Traits", Page.Traits, RaceDraftSection.Traits, 269),
        ("Environment", Page.Environment, RaceDraftSection.Environment, 284),
        ("Production", Page.Production, RaceDraftSection.Production, 287),
        ("Research", Page.Research, RaceDraftSection.Research, 288),
        ("Leftover Points", Page.LeftoverPoints, RaceDraftSection.LeftoverPoints, 249),
    };

    private Page selectedPage = Page.Identity;

    public IReadOnlyList<string> PageLabels { get; } = PageDefinitions.Select(p => p.Label).ToList();

    public string SelectedPageLabel
    {
        get => PageDefinitions.First(p => p.Value == SelectedPage).Label;
        set
        {
            (string Label, Page Value, RaceDraftSection Section, int HelpTopic) match = PageDefinitions.FirstOrDefault(p => p.Label == value);
            if (match.Label != null)
            {
                SelectedPage = match.Value;
            }
        }
    }

    private Page SelectedPage
    {
        get => selectedPage;
        set
        {
            if (SetProperty(ref selectedPage, value))
            {
                OnPropertyChanged(nameof(SelectedPageLabel));
                OnPropertyChanged(nameof(ShowIdentityPage));
                OnPropertyChanged(nameof(ShowTraitsPage));
                OnPropertyChanged(nameof(ShowEnvironmentPage));
                OnPropertyChanged(nameof(ShowProductionPage));
                OnPropertyChanged(nameof(ShowResearchPage));
                OnPropertyChanged(nameof(ShowLeftoverPointsPage));
                if (IsHelpVisible)
                {
                    ShowHelp();
                }
            }
        }
    }

    public bool ShowIdentityPage => SelectedPage == Page.Identity;

    public bool ShowTraitsPage => SelectedPage == Page.Traits;

    public bool ShowEnvironmentPage => SelectedPage == Page.Environment;

    public bool ShowProductionPage => SelectedPage == Page.Production;

    public bool ShowResearchPage => SelectedPage == Page.Research;

    public bool ShowLeftoverPointsPage => SelectedPage == Page.LeftoverPoints;

    /// <summary>
    /// The Leftover Points dropdown's options. Deliberately NOT the original WinForms
    /// control's own item text ("Mineral concentrations", plural) - that string never matches
    /// what ServerState/NewGame/HomeStarLeftoverpointsAdjuster.cs actually checks for
    /// ("Mineral concentration", singular), a real pre-existing bug in the original dialog.
    /// Using the string the consumer actually matches instead of reproducing that bug.
    /// </summary>
    public static readonly IReadOnlyList<string> LeftoverPointTargets = RaceDesignerRules.LeftoverPointTargets;

    /// <summary>The race the designer was opened on; written only when Save succeeds.</summary>
    private readonly Race original;

    /// <summary>The draft-local working copy every control edits.</summary>
    private readonly Race race;

    /// <summary>The draft as it was when the designer opened (for per-section undo).</summary>
    private readonly Race baseline;

    private readonly Random random = new Random();

    private string password = string.Empty;

    private string statusMessage = string.Empty;

    private RaceIcon? selectedIcon;

    private TraitEntry selectedPrimaryTrait;

    private RacePreset selectedPreset = RacePresets.All[RacePresets.All.Count - 1];

    private bool isHelpVisible;

    private string helpTitle = string.Empty;

    private string helpText = string.Empty;

    /// <summary>Creates a brand new, unsaved race, seeded with the economic defaults of the
    /// spec's table (the Humanoid record: 1,000 / 10 / 10 / 10 / 10 / 5 / 10) and a 15% growth
    /// rate (a fresh <see cref="Race"/>'s int fields all default to 0, below every minimum).</summary>
    public RaceDesignerViewModel() : this(CreateDefaultRace())
    {
    }

    /// <param name="race">The race to edit (it is not touched until Save succeeds).</param>
    /// <param name="isEditable">False for a read-only view of a race: every mutable control is
    /// disabled while the stored values stay visible.</param>
    public RaceDesignerViewModel(Race race, bool isEditable = true)
    {
        original = race;
        this.race = RaceDesignerRules.CopyOf(race);

        // race-designer-ui-and-availability.md section 1 / row 3: on opening, an empty race name
        // is filled with the first preset's caption, "Humanoid" (the plural is left as stored).
        if (string.IsNullOrEmpty(this.race.Name))
        {
            this.race.Name = "Humanoid";
        }

        IsEditable = isEditable;

        AllRaceIcons.Restore();
        IconOptions = AllRaceIcons.Data.IconList;
        selectedIcon = IconOptions.FirstOrDefault(icon => icon.Source == this.race.Icon?.Source) ?? IconOptions.FirstOrDefault();
        if (selectedIcon != null)
        {
            this.race.Icon = selectedIcon;
        }

        PrimaryTraitOptions = PrimaryTraits.Traits;
        selectedPrimaryTrait = this.race.Traits.Primary;

        SecondaryTraitOptions = SecondaryTraits.Traits
            .Where(entry => entry.Code != RaceDesignerRules.CheapFactories && entry.Code != RaceDesignerRules.ExtraTech)
            .Select(entry => new SecondaryTraitOptionViewModel(this.race, entry, RecalculateTotals))
            .ToList();
        CheapFactoriesTrait = new SecondaryTraitOptionViewModel(
            this.race, SecondaryTraits.Traits.Single(entry => entry.Code == RaceDesignerRules.CheapFactories), RecalculateTotals);
        ExtraTechTrait = new SecondaryTraitOptionViewModel(
            this.race,
            SecondaryTraits.Traits.Single(entry => entry.Code == RaceDesignerRules.ExtraTech),
            RecalculateTotals,
            () => $"Expensive research fields start at tech level {RaceDesignerRules.ExtraTechStartLevel(this.race)}");

        GravityTolerance = new EnvironmentToleranceViewModel("Gravity", this.race.GravityTolerance, Gravity.FormatWithUnit);
        TemperatureTolerance = new EnvironmentToleranceViewModel("Temperature", this.race.TemperatureTolerance, Temperature.FormatWithUnit);
        RadiationTolerance = new EnvironmentToleranceViewModel(
            "Radiation", this.race.RadiationTolerance, value => value.ToString("F0", CultureInfo.InvariantCulture) + "mR");
        foreach (EnvironmentToleranceViewModel tolerance in Tolerances)
        {
            tolerance.PropertyChanged += (_, _) => RecalculateTotals();
        }

        Biotechnology = new ResearchCostViewModel("Biotechnology", this.race.ResearchCosts, TechLevel.ResearchField.Biotechnology);
        Electronics = new ResearchCostViewModel("Electronics", this.race.ResearchCosts, TechLevel.ResearchField.Electronics);
        Energy = new ResearchCostViewModel("Energy", this.race.ResearchCosts, TechLevel.ResearchField.Energy);
        Propulsion = new ResearchCostViewModel("Propulsion", this.race.ResearchCosts, TechLevel.ResearchField.Propulsion);
        Weapons = new ResearchCostViewModel("Weapons", this.race.ResearchCosts, TechLevel.ResearchField.Weapons);
        Construction = new ResearchCostViewModel("Construction", this.race.ResearchCosts, TechLevel.ResearchField.Construction);
        foreach (ResearchCostViewModel cost in ResearchCosts)
        {
            cost.PropertyChanged += (_, _) => RecalculateTotals();
        }

        if (string.IsNullOrEmpty(this.race.LeftoverPointTarget))
        {
            this.race.LeftoverPointTarget = LeftoverPointTargets[0];
        }

        Func<Race> draft = () => this.race;
        Func<bool> editable = () => IsEditable;
        ColonistsRow = new RaceDesignerEconomyRowViewModel(0, "One resource is generated each year for every this many colonists", draft, editable, RecalculateTotals);
        FactoryOutputRow = new RaceDesignerEconomyRowViewModel(1, "Every 10 factories produce this many resources each year", draft, editable, RecalculateTotals);
        FactoryCostRow = new RaceDesignerEconomyRowViewModel(2, "Factories require this many resources to build", draft, editable, RecalculateTotals);
        FactoriesOperatedRow = new RaceDesignerEconomyRowViewModel(3, "Every 10,000 colonists may operate this many factories", draft, editable, RecalculateTotals);
        MineOutputRow = new RaceDesignerEconomyRowViewModel(4, "Every 10 mines produce this much of each mineral every year", draft, editable, RecalculateTotals);
        MineCostRow = new RaceDesignerEconomyRowViewModel(5, "Mines require this many resources to build", draft, editable, RecalculateTotals);
        MinesOperatedRow = new RaceDesignerEconomyRowViewModel(6, "Every 10,000 colonists may operate this many mines", draft, editable, RecalculateTotals);

        // Taken after the sub view models normalised the draft (research classes, leftover
        // choice, emblem), so an undo restores values the controls can show.
        baseline = RaceDesignerRules.CopyOf(this.race);

        SaveCommand = new RelayCommand(Save, CanSave);
        CancelCommand = new RelayCommand(() => RaceSavedOrCancelled?.Invoke());
        SelectIconCommand = new RelayCommand<RaceIcon>(icon =>
        {
            if (icon != null && IsEditable)
            {
                SelectedIcon = icon;
            }
        });
        ApplyPresetCommand = new RelayCommand(ApplySelectedPreset, () => IsEditable);
        RevertSectionCommand = new RelayCommand(RevertCurrentSection, () => IsEditable);
        HelpCommand = new RelayCommand(ShowHelp);
        CloseHelpCommand = new RelayCommand(() => IsHelpVisible = false);
    }

    private static Race CreateDefaultRace()
    {
        Race race = new Race();
        for (int slot = 0; slot < RaceDesignerRules.EconomySlots.Length; slot++)
        {
            RaceDesignerRules.SetSlot(race, slot, RaceDesignerRules.EconomySlots[slot].Default);
        }

        foreach (TechLevel.ResearchField field in RaceDesignerRules.ResearchSlotOrder)
        {
            race.ResearchCosts[field] = 100;
        }

        race.GrowthRate = 15;
        race.LeftoverPointTarget = RaceDesignerRules.LeftoverPointTargets[0];
        return race;
    }

    /// <summary>False in a non-editable context: the view disables every mutable control.</summary>
    public bool IsEditable { get; }

    /// <summary>"Cancel" while editing, "Close" when only viewing.</summary>
    public string CancelLabel => IsEditable ? "Cancel" : "Close";

    /// <summary>Instance-accessible mirror of <see cref="LeftoverPointTargets"/> so the view can
    /// bind to it directly (a static member can't be reached from an instance-typed binding).</summary>
    public IReadOnlyList<string> LeftoverPointTargetOptions => LeftoverPointTargets;

    public IReadOnlyList<RaceIcon> IconOptions { get; }

    public IReadOnlyList<TraitEntry> PrimaryTraitOptions { get; }

    public IReadOnlyList<SecondaryTraitOptionViewModel> SecondaryTraitOptions { get; }

    /// <summary>CF ("Cheap Factories") - shown on the Production tab in the original dialog,
    /// not with the other LRTs (it's explicitly commented "not a normal LRT" in the source).</summary>
    public SecondaryTraitOptionViewModel CheapFactoriesTrait { get; }

    /// <summary>ExtraTech - shown on the Research tab in the original dialog, not with the
    /// other LRTs (also explicitly commented "not a normal LRT" in the source).</summary>
    public SecondaryTraitOptionViewModel ExtraTechTrait { get; }

    public EnvironmentToleranceViewModel GravityTolerance { get; }

    public EnvironmentToleranceViewModel TemperatureTolerance { get; }

    public EnvironmentToleranceViewModel RadiationTolerance { get; }

    private IEnumerable<EnvironmentToleranceViewModel> Tolerances => new[] { GravityTolerance, TemperatureTolerance, RadiationTolerance };

    public ResearchCostViewModel Biotechnology { get; }

    public ResearchCostViewModel Electronics { get; }

    public ResearchCostViewModel Energy { get; }

    public ResearchCostViewModel Propulsion { get; }

    public ResearchCostViewModel Weapons { get; }

    public ResearchCostViewModel Construction { get; }

    private IEnumerable<ResearchCostViewModel> ResearchCosts => new[] { Biotechnology, Electronics, Energy, Propulsion, Weapons, Construction };

    public RaceDesignerEconomyRowViewModel ColonistsRow { get; }

    public RaceDesignerEconomyRowViewModel FactoryOutputRow { get; }

    public RaceDesignerEconomyRowViewModel FactoryCostRow { get; }

    public RaceDesignerEconomyRowViewModel FactoriesOperatedRow { get; }

    public RaceDesignerEconomyRowViewModel MineOutputRow { get; }

    public RaceDesignerEconomyRowViewModel MineCostRow { get; }

    public RaceDesignerEconomyRowViewModel MinesOperatedRow { get; }

    private IEnumerable<RaceDesignerEconomyRowViewModel> EconomyRows => new[]
    {
        ColonistsRow, FactoryOutputRow, FactoryCostRow, FactoriesOperatedRow, MineOutputRow, MineCostRow, MinesOperatedRow
    };

    /// <summary>True when the draft is Alternate Reality (rows 2-7 are locked).</summary>
    public bool IsAlternateReality => RaceDesignerRules.IsAlternateReality(race);

    /// <summary>True while the Germanium-discount (Cheap Factories) checkbox may be toggled:
    /// never for Alternate Reality, whose economy rows 2-7 are locked, and never in a read-only
    /// view (race-designer-ui-and-availability.md section 1; spec-11 answer 4.3).</summary>
    public bool CanEditCheapFactories => IsEditable && !IsAlternateReality;

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand<RaceIcon> SelectIconCommand { get; }

    public IRelayCommand ApplyPresetCommand { get; }

    public IRelayCommand RevertSectionCommand { get; }

    public IRelayCommand HelpCommand { get; }

    public IRelayCommand CloseHelpCommand { get; }

    /// <summary>Raised once the user picks Save and validation passes, carrying a suggested
    /// file name - the view resolves the actual save location via its own SaveFilePickerAsync
    /// (defaulted to <see cref="DefaultRaceFolder"/>) and then calls <see cref="CompleteSave"/>.</summary>
    public event Action<string>? SaveFileRequested;

    /// <summary>Raised once the race has actually been saved, or the user cancels out of this
    /// screen - the host view/window uses this to navigate back.</summary>
    public event Action? RaceSavedOrCancelled;

    /// <summary>The folder new races default to loading from/saving into - mirrors the
    /// original dialog's own FileSearcher.GetFolder(Global.RaceFolderKey, Global.RaceFolderName)
    /// call, so races saved from either UI land in the same place.</summary>
    public static string DefaultRaceFolder => FileSearcher.GetFolder(Global.RaceFolderKey, Global.RaceFolderName);

    // ---- Presets ---------------------------------------------------------------------------

    /// <summary>The eight Identity-stage archetypes: six named presets, Random and Custom.</summary>
    public IReadOnlyList<RacePreset> PresetOptions => RacePresets.All;

    public RacePreset SelectedPreset
    {
        get => selectedPreset;
        set
        {
            if (value != null)
            {
                SetProperty(ref selectedPreset, value);
            }
        }
    }

    private void ApplySelectedPreset()
    {
        if (!IsEditable || SelectedPreset.IsCustom)
        {
            StatusMessage = "Custom keeps the race as you have drafted it.";
            return;
        }

        if (SelectedPreset.IsRandom)
        {
            RandomRaceResult result = RandomRaceGenerator.Generate(race, random, candidate => candidate.GetAdvantagePoints());
            RaceDesignerRules.CopyAll(result.Race, race);
            StatusMessage = result.UsedFallback
                ? "No random race within 0-50 advantage points was found; the Humanoid preset was used instead."
                : "Generated a random race.";
        }
        else
        {
            RacePresets.Apply(SelectedPreset, race);
            StatusMessage = $"Applied the {SelectedPreset.Name} preset.";
        }

        RefreshAll();
    }

    // ---- Working copy ----------------------------------------------------------------------

    /// <summary>Restores the current section's fields to the values the designer opened with.</summary>
    private void RevertCurrentSection()
    {
        if (!IsEditable)
        {
            return;
        }

        RaceDraftSection section = PageDefinitions.First(p => p.Value == SelectedPage).Section;
        RaceDesignerRules.CopySection(baseline, race, section);
        StatusMessage = $"Undid the changes to {SelectedPageLabel}.";
        RefreshAll();
    }

    /// <summary>Re-reads every control after the draft changed underneath.</summary>
    private void RefreshAll()
    {
        selectedPrimaryTrait = race.Traits.Primary;
        selectedIcon = IconOptions.FirstOrDefault(icon => icon.Source == race.Icon?.Source) ?? selectedIcon;
        foreach (string property in new[]
        {
            nameof(Name), nameof(PluralName), nameof(SelectedPrimaryTrait), nameof(SelectedIcon),
            nameof(LeftoverPointTarget), nameof(GrowthRate), nameof(IsAlternateReality),
        })
        {
            OnPropertyChanged(property);
        }

        foreach (SecondaryTraitOptionViewModel trait in SecondaryTraitOptions.Concat(new[] { CheapFactoriesTrait, ExtraTechTrait }))
        {
            trait.Refresh();
        }

        foreach (EnvironmentToleranceViewModel tolerance in Tolerances)
        {
            tolerance.Refresh();
        }

        foreach (ResearchCostViewModel cost in ResearchCosts)
        {
            cost.Refresh();
        }

        foreach (RaceDesignerEconomyRowViewModel row in EconomyRows)
        {
            row.Refresh();
        }

        RecalculateTotals();
    }

    // ---- Help ------------------------------------------------------------------------------

    /// <summary>True while the help panel is showing; help never changes the draft.</summary>
    public bool IsHelpVisible
    {
        get => isHelpVisible;
        private set => SetProperty(ref isHelpVisible, value);
    }

    public string HelpTitle
    {
        get => helpTitle;
        private set => SetProperty(ref helpTitle, value);
    }

    public string HelpText
    {
        get => helpText;
        private set => SetProperty(ref helpText, value);
    }

    private void ShowHelp()
    {
        int topicNumber = PageDefinitions.First(p => p.Value == SelectedPage).HelpTopic;
        try
        {
            HelpViewModel manual = new HelpViewModel("RaceDesignerHelp", "Help");
            HelpTopicViewModel? topic = manual.Topics.FirstOrDefault(t => t.Number == topicNumber);
            if (topic != null)
            {
                manual.SelectedTopic = topic;
                HelpTitle = topic.Title;
                HelpText = string.IsNullOrEmpty(manual.ContentText) ? "This help topic is not available." : manual.ContentText;
            }
            else
            {
                HelpTitle = SelectedPageLabel;
                HelpText = "The manual is not available on this installation.";
            }
        }
        catch (Exception ex)
        {
            HelpTitle = SelectedPageLabel;
            HelpText = "The manual could not be opened: " + ex.Message;
        }

        IsHelpVisible = true;
    }

    // ---- Identity --------------------------------------------------------------------------

    public string Name
    {
        get => race.Name ?? string.Empty;
        set
        {
            if (race.Name != value)
            {
                race.Name = value;
                OnPropertyChanged();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string PluralName
    {
        get => race.PluralName ?? string.Empty;
        set
        {
            if (race.PluralName != value)
            {
                race.PluralName = value;
                OnPropertyChanged();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The plain-text password, hashed into <see cref="Race.Password"/> only at Save
    /// time (matching the original - a saved race only ever stores the hash, so re-editing a
    /// loaded race always starts with this field blank and requires retyping the password
    /// before it can be saved again).</summary>
    public string Password
    {
        get => password;
        set
        {
            if (password != value)
            {
                password = value;
                OnPropertyChanged();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    /// <summary>
    /// Lets the view's code-behind surface a problem it hit (e.g. a Load file picker that
    /// failed) through this screen's own visible status line, rather than through
    /// Report.Error - which is never actually wired to anything visible on this port (neither
    /// desktop nor Android registers PlatformHooks.ShowError), so it silently vanishes into
    /// Console/logcat and the user sees nothing happen at all.
    /// </summary>
    public void ReportError(string message)
    {
        StatusMessage = message;
    }

    public RaceIcon? SelectedIcon
    {
        get => selectedIcon;
        set
        {
            if (SetProperty(ref selectedIcon, value) && value != null)
            {
                race.Icon = value;
            }
        }
    }

    /// <summary>Selecting a PRT goes through RaceDesignerRules.ApplyPrimaryTrait, so picking
    /// Alternate Reality resets and locks the economy rows it ignores.</summary>
    public TraitEntry SelectedPrimaryTrait
    {
        get => selectedPrimaryTrait;
        set
        {
            if (value is not null && SetProperty(ref selectedPrimaryTrait, value))
            {
                RaceDesignerRules.ApplyPrimaryTrait(race, value.Code);
                foreach (RaceDesignerEconomyRowViewModel row in EconomyRows)
                {
                    row.Refresh();
                }

                CheapFactoriesTrait.Refresh();
                ExtraTechTrait.Refresh();
                OnPropertyChanged(nameof(IsAlternateReality));
                OnPropertyChanged(nameof(CanEditCheapFactories));
                RecalculateTotals();
            }
        }
    }

    public string LeftoverPointTarget
    {
        get => race.LeftoverPointTarget;
        set
        {
            if (value != null && race.LeftoverPointTarget != value)
            {
                race.LeftoverPointTarget = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>1-20 percent per year, not normalized (Race.GrowthRate's own doc comment).</summary>
    public double GrowthRate
    {
        get => race.GrowthRate;
        set
        {
            double clamped = Math.Clamp(value, 1, 20);
            if (race.GrowthRate != clamped)
            {
                race.GrowthRate = clamped;
                OnPropertyChanged();
                RecalculateTotals();
            }
        }
    }

    /// <summary>The race this screen committed to - exposed read-only so a host that embeds this
    /// screen (e.g. New Game's "New Race..." button) can pick up the finished race directly once
    /// <see cref="WasSaved"/> is true, rather than having to re-scan the race folder on disk
    /// (which would miss a race saved somewhere other than the default folder). Until a Save
    /// succeeds it still holds the values the designer was opened with.</summary>
    public Race Race => original;

    /// <summary>True once <see cref="CompleteSave"/> has actually written a file - lets a host
    /// distinguish a genuine save from a Cancel, since both raise <see cref="RaceSavedOrCancelled"/>.</summary>
    public bool WasSaved { get; private set; }

    public int AdvantagePoints => race.GetAdvantagePoints();

    public int LeftoverPoints => race.GetLeftoverAdvantagePoints();

    public double WorldAvailabilityPercent => WorldAvailabilityEstimator.EstimateCompatibleWorldsPercent(
        (race.GravityTolerance.MinimumValue, race.GravityTolerance.MaximumValue, race.GravityTolerance.Immune),
        (race.TemperatureTolerance.MinimumValue, race.TemperatureTolerance.MaximumValue, race.TemperatureTolerance.Immune),
        (race.RadiationTolerance.MinimumValue, race.RadiationTolerance.MaximumValue, race.RadiationTolerance.Immune));

    /// <summary>
    /// Called by the view's code-behind once its SaveFilePickerAsync call resolves to a file and
    /// it's been opened for writing - this class has no TopLevel/StorageProvider access of its
    /// own, matching how the other panels in this app keep platform-dialog concerns in the view
    /// layer. Takes a stream rather than a local file path since the destination file, on
    /// Android, may only be reachable via a content:// handle - notably, ACTION_CREATE_DOCUMENT
    /// (what SaveFilePickerAsync uses under the hood) creates the empty destination file the
    /// moment the user confirms the save location, before the app ever writes a single byte to
    /// it. The earlier file-path version silently skipped writing whenever that handle couldn't
    /// resolve to a local path, leaving that already-created file at 0 bytes - confirmed live as
    /// "the race file I created previously is zero length".
    ///
    /// Save is the designer's accept: only once the file is written is the working copy
    /// committed onto the race the designer was opened with.
    /// </summary>
    public void CompleteSave(Stream stream)
    {
        try
        {
            race.Password = new PasswordUtility().CalculateHash(Password);

            // Some storage-provider write streams open for writing without truncating -
            // re-saving shorter content over a previously larger file would then leave old
            // trailing bytes after the new XML's closing tag, which XmlDocument.Load chokes on
            // when re-reading. Harmless (and a no-op) for the far more common brand-new-file case.
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }

            XmlDocument xmldoc = new XmlDocument();
            XmlElement xmlRoot = Global.InitializeXmlDocument(xmldoc);
            xmlRoot.AppendChild(race.ToXml(xmldoc));

            xmldoc.Save(stream);

            RaceDesignerRules.CopyAll(race, original);
            StatusMessage = $"Saved \"{race.Name}\".";
            WasSaved = true;
            RaceSavedOrCancelled?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save that race: {ex.Message}";
        }
    }

    /// <summary>
    /// Called by the view's code-behind once its OpenFilePickerAsync call resolves to a file and
    /// it's been opened for reading (for editing an existing race). A successful load replaces
    /// the view's whole DataContext with the returned instance rather than mutating this one in
    /// place, since nearly every sub-view-model here is constructed once, up front, against a
    /// specific Race. Takes a stream rather than a local file path since the picked file may
    /// only be reachable via a content:// handle (e.g. Android's SAF document picker for a
    /// location outside this app's own storage), which a plain FileStream/File-based path can't
    /// open at all.
    /// </summary>
    public static bool TryLoadFromStream(Stream stream, out RaceDesignerViewModel? viewModel, out string error)
    {
        try
        {
            viewModel = new RaceDesignerViewModel(Race.LoadFromStream(stream));
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            viewModel = null;
            error = ex.Message;
            return false;
        }
    }

    private bool CanSave()
    {
        return IsEditable
            && AdvantagePoints >= 0
            && !string.IsNullOrWhiteSpace(Name)
            && !string.IsNullOrWhiteSpace(PluralName)
            && !string.IsNullOrWhiteSpace(Password);
    }

    private void Save()
    {
        SaveFileRequested?.Invoke((string.IsNullOrWhiteSpace(Name) ? "Race" : Name) + Global.RaceExtension);
    }

    private void RecalculateTotals()
    {
        OnPropertyChanged(nameof(AdvantagePoints));
        OnPropertyChanged(nameof(LeftoverPoints));
        OnPropertyChanged(nameof(WorldAvailabilityPercent));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
