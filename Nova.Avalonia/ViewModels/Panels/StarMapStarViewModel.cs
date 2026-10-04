using Avalonia.Media;
using MediaColor = Avalonia.Media.Color;
using Nova.Client.Map;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One star on the map. Position is in raw logical (universe) coordinates plus the map's edge
/// margin (see StarMapDocumentViewModel).
/// </summary>
public class StarMapStarViewModel : MapMarkerViewModel
{
    public double Diameter { get; }

    /// <summary>Starbase capability dots - see docs/behavior-specs-3/client-interface.md's
    /// "Starbase capability indicators" section, and the corresponding colors/offsets in
    /// StarMapDocumentView.axaml's star DataTemplate.</summary>
    public bool HasStarbase { get; }

    /// <summary>True when the starbase's own hull has real dock capacity (can build ships) -
    /// see StarMapDocumentViewModel's own comment on why this, rather than a hull-name check,
    /// is what distinguishes a full combat starbase from a small defense-only orbital platform
    /// for the presence dot's color (StarMapDocumentView.axaml's "full"/"small" style classes).
    /// Meaningless when HasStarbase is false.</summary>
    public bool IsFullStarbase { get; }

    public bool HasStargate { get; }

    public bool HasMassDriver { get; }

    /// <summary>Ring drawn around the whole star icon when at least one non-starbase fleet is
    /// sitting in orbit here - computed fresh from live fleet data (see StarMapDocumentViewModel)
    /// rather than from the never-updated Star/StarIntel.HasFleetsInOrbit field.</summary>
    public bool HasFleetsInOrbit => HasOwnFleetInOrbit || HasForeignFleetInOrbit;

    public bool HasOwnFleetInOrbit { get; }

    public bool HasForeignFleetInOrbit { get; }

    // ---- fleet-in-orbit ring: two size classes (behavior-specs-10/client-interface.md,
    // "Fleet-in-orbit ring"): 11x11 normally, 19x19 when this planet is the tracked (selected)
    // object. The small own-race tile is a light-gray/dark-gray bevel, the large one pure white;
    // foreign is a maroon/red bevel (small) or red (large); both present is purple/magenta
    // (small) or magenta (large).

    private static readonly IBrush LightGray = new SolidColorBrush(MediaColor.FromRgb(192, 192, 192));
    private static readonly IBrush DarkGray = new SolidColorBrush(MediaColor.FromRgb(128, 128, 128));
    private static readonly IBrush Maroon = new SolidColorBrush(MediaColor.FromRgb(128, 0, 0));
    private static readonly IBrush BrightRed = new SolidColorBrush(MediaColor.FromRgb(255, 0, 0));
    private static readonly IBrush Purple = new SolidColorBrush(MediaColor.FromRgb(128, 0, 128));
    private static readonly IBrush BrightMagenta = new SolidColorBrush(MediaColor.FromRgb(255, 0, 255));

    public double OrbitRingDiameter => IsSelected ? 19 : 11;

    public double OrbitRingOffset => -OrbitRingDiameter / 2;

    public IBrush OrbitRingStroke => (HasOwnFleetInOrbit, HasForeignFleetInOrbit, IsSelected) switch
    {
        (true, true, true) => BrightMagenta,
        (true, true, false) => BrightMagenta,
        (false, true, _) => BrightRed,
        (true, false, true) => Brushes.White,
        _ => LightGray,
    };

    /// <summary>The bevel's darker tone - only the small tiles are two-tone.</summary>
    public IBrush OrbitRingInnerStroke => IsSelected ? Brushes.Transparent : (HasOwnFleetInOrbit, HasForeignFleetInOrbit) switch
    {
        (true, true) => Purple,
        (false, true) => Maroon,
        _ => DarkGray,
    };

    public double OrbitRingInnerDiameter => OrbitRingDiameter - 2;

    public double OrbitRingInnerOffset => -OrbitRingInnerDiameter / 2;

    // ---- "Planets:" view-mode overlay (behavior-specs-10/client-interface.md, "Planet
    // display-mode ring/bar overlay"). All the data is computed once by StarMapDocumentViewModel;
    // only which overlay shows (and the zoom-dependent population ring size) changes later.

    private PlanetOverlayKind overlay;

    public PlanetOverlayKind Overlay
    {
        get => overlay;
        set
        {
            if (SetProperty(ref overlay, value))
            {
                OnPropertyChanged(nameof(ShowHabitabilityRing));
                OnPropertyChanged(nameof(ShowUnownedMarker));
                OnPropertyChanged(nameof(ShowPopulationRing));
                OnPropertyChanged(nameof(ShowMineralBars));
                OnPropertyChanged(nameof(IroniumBarHeight));
                OnPropertyChanged(nameof(BoraniumBarHeight));
                OnPropertyChanged(nameof(GermaniumBarHeight));
                OnPropertyChanged(nameof(IroniumBarTop));
                OnPropertyChanged(nameof(BoraniumBarTop));
                OnPropertyChanged(nameof(GermaniumBarTop));
            }
        }
    }

    private bool showName = true;

    /// <summary>Planet-name label toggle (second view-options word bit 0x04, key 0).</summary>
    public bool ShowName
    {
        get => showName;
        set => SetProperty(ref showName, value);
    }

    /// <summary>Whether the viewing race knows this planet's environment (explored).</summary>
    public bool HasHabitability { get; private set; }

    public bool IsUnowned { get; private set; }

    public double HabitabilityOuterDiameter { get; private set; }

    public double HabitabilityOuterOffset => -HabitabilityOuterDiameter / 2;

    public double HabitabilityInnerDiameter => HabitabilityOuterDiameter * PlanetOverlayRules.InnerRingFraction;

    public double HabitabilityInnerOffset => -HabitabilityInnerDiameter / 2;

    public IBrush HabitabilityFill { get; private set; } = Brushes.Transparent;

    public IBrush HabitabilityStroke { get; private set; } = Brushes.Transparent;

    public bool ShowHabitabilityRing => Overlay == PlanetOverlayKind.Habitability && HasHabitability && HabitabilityOuterDiameter > 0;

    /// <summary>The small square the habitability mode adds for a planet with no owner.</summary>
    public bool ShowUnownedMarker => Overlay == PlanetOverlayKind.Habitability && HasHabitability && IsUnowned;

    public double UnownedMarkerSize => PlanetOverlayRules.UnownedMarkerSize;

    public double UnownedMarkerOffset => -PlanetOverlayRules.UnownedMarkerSize / 2;

    public bool HasPopulation => populationSteps >= 0;

    private int populationSteps = -1;

    public IBrush PopulationBrush { get; private set; } = Brushes.Transparent;

    private double populationRingDiameter;

    public double PopulationRingDiameter
    {
        get => populationRingDiameter;
        private set
        {
            if (SetProperty(ref populationRingDiameter, value))
            {
                OnPropertyChanged(nameof(PopulationRingOffset));
            }
        }
    }

    public double PopulationRingOffset => -PopulationRingDiameter / 2;

    public bool ShowPopulationRing => Overlay == PlanetOverlayKind.Population && HasPopulation;

    private int[]? amountSteps;
    private int[]? concentrationSteps;

    private int[]? CurrentBarSteps => Overlay switch
    {
        PlanetOverlayKind.MineralAmount => amountSteps,
        PlanetOverlayKind.MineralConcentration => concentrationSteps,
        _ => null,
    };

    public bool ShowMineralBars => CurrentBarSteps != null;

    // Bars stand on a common baseline just above the planet icon and grow upward; the spec gives
    // the colours (blue / dark green / yellow, in that order) and the 0-20 step scale, not the
    // exact geometry (see StarMapDocumentView.axaml).
    private const double BarBaseline = -6;

    public double IroniumBarHeight => CurrentBarSteps?[0] ?? 0;

    public double BoraniumBarHeight => CurrentBarSteps?[1] ?? 0;

    public double GermaniumBarHeight => CurrentBarSteps?[2] ?? 0;

    public double IroniumBarTop => BarBaseline - IroniumBarHeight;

    public double BoraniumBarTop => BarBaseline - BoraniumBarHeight;

    public double GermaniumBarTop => BarBaseline - GermaniumBarHeight;

    public StarMapStarViewModel(string name, double x, double y, double diameter, IBrush color, object selectable,
        SelectionService selection, bool hasStarbase, bool isFullStarbase, bool hasStargate, bool hasMassDriver,
        bool hasOwnFleetInOrbit, bool hasForeignFleetInOrbit)
        : base(name, x, y, color, selectable, selection)
    {
        Diameter = diameter;
        HasStarbase = hasStarbase;
        IsFullStarbase = isFullStarbase;
        HasStargate = hasStargate;
        HasMassDriver = hasMassDriver;
        HasOwnFleetInOrbit = hasOwnFleetInOrbit;
        HasForeignFleetInOrbit = hasForeignFleetInOrbit;
    }

    /// <summary>The bullseye's data (null value = environment unknown).</summary>
    public void SetHabitability(int? value, HabitabilityRingColour colour, bool isUnowned)
    {
        IsUnowned = isUnowned;
        HasHabitability = value != null;
        if (value == null)
        {
            return;
        }

        HabitabilityOuterDiameter = PlanetOverlayRules.HabitabilityRingRadius(value.Value) * 2;
        (MediaColor dark, MediaColor bright) = colour switch
        {
            HabitabilityRingColour.Red => (MediaColor.FromRgb(128, 0, 0), MediaColor.FromRgb(255, 0, 0)),
            HabitabilityRingColour.Olive => (MediaColor.FromRgb(128, 128, 0), MediaColor.FromRgb(255, 255, 0)),
            _ => (MediaColor.FromRgb(0, 128, 0), MediaColor.FromRgb(0, 255, 0)),
        };
        HabitabilityFill = new SolidColorBrush(dark);
        HabitabilityStroke = new SolidColorBrush(bright);
    }

    /// <summary>The population ring's data (null steps = no known population).</summary>
    public void SetPopulation(int? steps, PopulationRingColour colour)
    {
        populationSteps = steps ?? -1;
        PopulationBrush = colour switch
        {
            PopulationRingColour.Green => new SolidColorBrush(MediaColor.FromRgb(0, 255, 0)),
            PopulationRingColour.Yellow => new SolidColorBrush(MediaColor.FromRgb(255, 255, 0)),
            _ => new SolidColorBrush(MediaColor.FromRgb(255, 0, 0)),
        };
    }

    /// <summary>The bars' step counts (I, B, G), null when the values are not known.</summary>
    public void SetMineralBars(int[]? amounts, int[]? concentrations)
    {
        amountSteps = amounts;
        concentrationSteps = concentrations;
    }

    /// <summary>The population ring is sized in screen pixels (see PlanetOverlayRules), so its
    /// world-unit diameter follows the zoom.</summary>
    public void ApplyZoom(int zoomLevel)
    {
        if (populationSteps < 0)
        {
            return;
        }

        double screenRadius = PlanetOverlayRules.PopulationRingScreenRadius(populationSteps, zoomLevel);
        PopulationRingDiameter = screenRadius * 2 / MapZoom.ScaleFactor(zoomLevel);
    }

    protected override void OnIsSelectedChanged()
    {
        OnPropertyChanged(nameof(OrbitRingDiameter));
        OnPropertyChanged(nameof(OrbitRingOffset));
        OnPropertyChanged(nameof(OrbitRingStroke));
        OnPropertyChanged(nameof(OrbitRingInnerStroke));
        OnPropertyChanged(nameof(OrbitRingInnerDiameter));
        OnPropertyChanged(nameof(OrbitRingInnerOffset));
    }
}
