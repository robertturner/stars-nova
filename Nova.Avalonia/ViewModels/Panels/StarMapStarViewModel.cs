using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One star on the map. Position is in raw logical (universe) coordinates for now -
/// no zoom/pan/scroll yet, that's a follow-up once the panel has real data flowing.
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

    /// <summary>Ring drawn around the whole star icon (not an offset dot, unlike the three
    /// above) when at least one non-starbase fleet is sitting in orbit here - see
    /// StarMap.cs.DrawOrbitingFleets' own "orbiting fleets (smaller circle)" for the WinForms
    /// original this ports, and StarMapDocumentViewModel's own comment on why this is computed
    /// fresh from live fleet data rather than from the never-updated Star/StarIntel.
    /// HasFleetsInOrbit field. True whenever either half below is true - i.e. whenever the ring
    /// should show at all, regardless of which color.</summary>
    public bool HasFleetsInOrbit => HasOwnFleetInOrbit || HasForeignFleetInOrbit;

    /// <summary>The ring is white when at least one of the viewer's own fleets is in orbit here -
    /// see docs/behavior-specs-5/client-interface.md's "Fleet-in-orbit ring" note (confirmed
    /// against a fixed emulator build, since an outdated one silently failed to render this ring
    /// at all while still rendering everything else, misleading an earlier pass into concluding
    /// it didn't exist).</summary>
    public bool HasOwnFleetInOrbit { get; }

    /// <summary>The ring is red when at least one foreign fleet is in orbit here (per intel), and
    /// a third, distinct color when both this and <see cref="HasOwnFleetInOrbit"/> are true at
    /// once - see StarMapDocumentView.axaml's "orbitOwn"/"orbitForeign"/"orbitBoth" style
    /// classes.</summary>
    public bool HasForeignFleetInOrbit { get; }

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
}
