using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using CommunityToolkit.Mvvm.Input;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Race Designer's main view model - the native Avalonia replacement for
/// Nova/WinForms/RaceDesigner/RaceDesigner.cs, wrapping a <see cref="Race"/> and exposing every
/// field the original dialog lets a player set. Follows this app's established
/// event-forwarding pattern (see OpenGameViewModel): this class has no TopLevel/StorageProvider
/// access of its own, so Save/Load's actual file pickers live in RaceDesignerView's code-behind,
/// which calls back into <see cref="CompleteSave"/> or <see cref="TryLoadFromStream"/> once a
/// file is chosen.
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

    private static readonly (string Label, Page Value)[] PageDefinitions =
    {
        ("Identity", Page.Identity),
        ("Traits", Page.Traits),
        ("Environment", Page.Environment),
        ("Production", Page.Production),
        ("Research", Page.Research),
        ("Leftover Points", Page.LeftoverPoints),
    };

    private Page selectedPage = Page.Identity;

    public IReadOnlyList<string> PageLabels { get; } = PageDefinitions.Select(p => p.Label).ToList();

    public string SelectedPageLabel
    {
        get => PageDefinitions.First(p => p.Value == SelectedPage).Label;
        set
        {
            (string Label, Page Value) match = PageDefinitions.FirstOrDefault(p => p.Label == value);
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
    public static readonly IReadOnlyList<string> LeftoverPointTargets = new[]
    {
        "Surface minerals",
        "Mineral concentration",
        "Mines",
        "Factories",
        "Defenses",
    };

    private readonly Race race;

    private string password = string.Empty;

    private string statusMessage = string.Empty;

    private RaceIcon? selectedIcon;

    private TraitEntry selectedPrimaryTrait;

    /// <summary>Creates a brand new, unsaved race, seeded with the same starting values the
    /// original dialog's numeric controls open with (a fresh <see cref="Race"/>'s int fields
    /// all default to 0, which is below every one of these fields' real minimum).</summary>
    public RaceDesignerViewModel() : this(new Race())
    {
        ColonistsPerResource = 1000;
        OperableFactories = 10;
        FactoryBuildCost = 10;
        MineBuildCost = 5;
        FactoryProduction = 10;
        MineProductionRate = 10;
        OperableMines = 10;
        GrowthRate = 15;
    }

    public RaceDesignerViewModel(Race race)
    {
        this.race = race;

        AllRaceIcons.Restore();
        IconOptions = AllRaceIcons.Data.IconList;
        selectedIcon = IconOptions.FirstOrDefault(icon => icon.Source == race.Icon.Source) ?? IconOptions.FirstOrDefault();
        if (selectedIcon != null)
        {
            race.Icon = selectedIcon;
        }

        PrimaryTraitOptions = PrimaryTraits.Traits;
        selectedPrimaryTrait = race.Traits.Primary;

        SecondaryTraitOptions = SecondaryTraits.Traits
            .Where(entry => entry.Code != "CF" && entry.Code != "ExtraTech")
            .Select(entry => new SecondaryTraitOptionViewModel(race, entry, RecalculateTotals))
            .ToList();
        CheapFactoriesTrait = new SecondaryTraitOptionViewModel(
            race, SecondaryTraits.Traits.Single(entry => entry.Code == "CF"), RecalculateTotals);
        ExtraTechTrait = new SecondaryTraitOptionViewModel(
            race, SecondaryTraits.Traits.Single(entry => entry.Code == "ExtraTech"), RecalculateTotals);

        GravityTolerance = new EnvironmentToleranceViewModel("Gravity", race.GravityTolerance, Gravity.FormatWithUnit);
        TemperatureTolerance = new EnvironmentToleranceViewModel("Temperature", race.TemperatureTolerance, Temperature.FormatWithUnit);
        RadiationTolerance = new EnvironmentToleranceViewModel(
            "Radiation", race.RadiationTolerance, value => value.ToString("F0", CultureInfo.InvariantCulture) + "mR");
        foreach (EnvironmentToleranceViewModel tolerance in new[] { GravityTolerance, TemperatureTolerance, RadiationTolerance })
        {
            tolerance.PropertyChanged += (_, _) => RecalculateTotals();
        }

        Biotechnology = new ResearchCostViewModel("Biotechnology", race.ResearchCosts, TechLevel.ResearchField.Biotechnology);
        Electronics = new ResearchCostViewModel("Electronics", race.ResearchCosts, TechLevel.ResearchField.Electronics);
        Energy = new ResearchCostViewModel("Energy", race.ResearchCosts, TechLevel.ResearchField.Energy);
        Propulsion = new ResearchCostViewModel("Propulsion", race.ResearchCosts, TechLevel.ResearchField.Propulsion);
        Weapons = new ResearchCostViewModel("Weapons", race.ResearchCosts, TechLevel.ResearchField.Weapons);
        Construction = new ResearchCostViewModel("Construction", race.ResearchCosts, TechLevel.ResearchField.Construction);
        foreach (ResearchCostViewModel cost in new[] { Biotechnology, Electronics, Energy, Propulsion, Weapons, Construction })
        {
            cost.PropertyChanged += (_, _) => RecalculateTotals();
        }

        if (string.IsNullOrEmpty(race.LeftoverPointTarget))
        {
            race.LeftoverPointTarget = LeftoverPointTargets[0];
        }

        SaveCommand = new RelayCommand(Save, CanSave);
        CancelCommand = new RelayCommand(() => RaceSavedOrCancelled?.Invoke());
        SelectIconCommand = new RelayCommand<RaceIcon>(icon =>
        {
            if (icon != null)
            {
                SelectedIcon = icon;
            }
        });
    }

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

    public ResearchCostViewModel Biotechnology { get; }

    public ResearchCostViewModel Electronics { get; }

    public ResearchCostViewModel Energy { get; }

    public ResearchCostViewModel Propulsion { get; }

    public ResearchCostViewModel Weapons { get; }

    public ResearchCostViewModel Construction { get; }

    public IRelayCommand SaveCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand<RaceIcon> SelectIconCommand { get; }

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

    public TraitEntry SelectedPrimaryTrait
    {
        get => selectedPrimaryTrait;
        set
        {
            if (SetProperty(ref selectedPrimaryTrait, value))
            {
                race.Traits.SetPrimary(value);
                RecalculateTotals();
            }
        }
    }

    public string LeftoverPointTarget
    {
        get => race.LeftoverPointTarget;
        set
        {
            if (race.LeftoverPointTarget != value)
            {
                race.LeftoverPointTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public int ColonistsPerResource
    {
        get => race.ColonistsPerResource;
        set => SetRaceField(ref race.ColonistsPerResource, value, 700, 2500);
    }

    public int OperableFactories
    {
        get => race.OperableFactories;
        set => SetRaceField(ref race.OperableFactories, value, 5, 25);
    }

    public int FactoryBuildCost
    {
        get => race.FactoryBuildCost;
        set => SetRaceField(ref race.FactoryBuildCost, value, 5, 25);
    }

    public int MineBuildCost
    {
        get => race.MineBuildCost;
        set => SetRaceField(ref race.MineBuildCost, value, 2, 15);
    }

    public int FactoryProduction
    {
        get => race.FactoryProduction;
        set => SetRaceField(ref race.FactoryProduction, value, 5, 15);
    }

    public int MineProductionRate
    {
        get => race.MineProductionRate;
        set => SetRaceField(ref race.MineProductionRate, value, 5, 25);
    }

    public int OperableMines
    {
        get => race.OperableMines;
        set => SetRaceField(ref race.OperableMines, value, 5, 25);
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

    /// <summary>The race object this screen is editing - exposed read-only so a host that
    /// embeds this screen (e.g. New Game's "New Race..." button) can pick up the finished race
    /// directly once <see cref="WasSaved"/> is true, rather than having to re-scan the race
    /// folder on disk (which would miss a race saved somewhere other than the default folder).</summary>
    public Race Race => race;

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
        return AdvantagePoints >= 0
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

    private void SetRaceField(ref int field, int value, int min, int max, [CallerMemberName] string? propertyName = null)
    {
        int clamped = Math.Clamp(value, min, max);
        if (field != clamped)
        {
            field = clamped;
            OnPropertyChanged(propertyName);
            RecalculateTotals();
        }
    }
}
