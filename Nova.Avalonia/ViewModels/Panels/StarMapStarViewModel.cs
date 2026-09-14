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

    public bool HasStargate { get; }

    public bool HasMassDriver { get; }

    /// <summary>Ring drawn around the whole star icon (not an offset dot, unlike the three
    /// above) when at least one non-starbase fleet is sitting in orbit here - see
    /// StarMap.cs.DrawOrbitingFleets' own "orbiting fleets (smaller circle)" white ring for the
    /// WinForms original this ports, and StarMapDocumentViewModel's own comment on why this is
    /// computed fresh from live fleet data rather than from the never-updated
    /// Star/StarIntel.HasFleetsInOrbit field.</summary>
    public bool HasFleetsInOrbit { get; }

    public StarMapStarViewModel(string name, double x, double y, double diameter, IBrush color, object selectable,
        SelectionService selection, bool hasStarbase, bool hasStargate, bool hasMassDriver, bool hasFleetsInOrbit)
        : base(name, x, y, color, selectable, selection)
    {
        Diameter = diameter;
        HasStarbase = hasStarbase;
        HasStargate = hasStargate;
        HasMassDriver = hasMassDriver;
        HasFleetsInOrbit = hasFleetsInOrbit;
    }
}
