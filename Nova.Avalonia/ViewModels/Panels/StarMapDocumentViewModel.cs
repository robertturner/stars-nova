using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;
using Nova.Common.Components;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Star Map document: every star and fleet this empire has a report on, positioned and
/// colored the same way the WinForms StarMap does (see StarMap.cs's DrawStar) - white for an
/// unowned/unexplored star, green for the player's own things, red for anyone else's. Panning is
/// the ScrollViewer's own scrollbars, mouse click-drag, and (PanningMode="Both") touch drag;
/// <see cref="Zoom"/> is driven by the view's mouse-wheel handler and the ZoomIn/ZoomOut/Reset
/// buttons (see StarMapDocumentView.axaml.cs) - the buttons exist because a touchscreen never
/// raises a wheel event at all.
/// </summary>
public class StarMapDocumentViewModel : Document
{
    private readonly SelectionService selection;
    private readonly List<MapMarkerViewModel> markers = new();

    public IReadOnlyList<StarMapStarViewModel> Stars { get; }

    public IReadOnlyList<StarMapFleetViewModel> Fleets { get; }

    public IReadOnlyList<StarMapScanCircleViewModel> ScanCircles { get; }

    public IReadOnlyList<StarMapMineFieldViewModel> Minefields { get; }

    private IReadOnlyList<StarMapRouteLegViewModel> routeLegs = System.Array.Empty<StarMapRouteLegViewModel>();

    /// <summary>The selected fleet's pending route, empty unless a fleet with 2+ waypoints is
    /// selected - see StarMapRouteLegViewModel, and StarMap.cs's DrawFleetRoute for the WinForms
    /// equivalent this ports.</summary>
    public IReadOnlyList<StarMapRouteLegViewModel> RouteLegs
    {
        get => routeLegs;
        private set => SetProperty(ref routeLegs, value);
    }

    public double MapWidth { get; }
    public double MapHeight { get; }

    public const double MinZoom = 0.25;
    public const double MaxZoom = 3.0;

    private double zoom = 1.0;

    public double Zoom
    {
        get => zoom;
        set => SetProperty(ref zoom, System.Math.Clamp(value, MinZoom, MaxZoom));
    }

    public IRelayCommand ResetZoomCommand { get; }

    public IRelayCommand ZoomInCommand { get; }

    public IRelayCommand ZoomOutCommand { get; }

    public StarMapDocumentViewModel(string id, string title, ClientData clientState, SelectionService selection)
    {
        Id = id;
        Title = title;
        this.selection = selection;
        ResetZoomCommand = new RelayCommand(() => Zoom = 1.0);
        // Buttons, not just the mouse-wheel handler in StarMapDocumentView.axaml.cs - a
        // touchscreen never raises a wheel event at all, so without these, zoom was completely
        // unreachable on Android (the map's only interaction was its ScrollViewer's own
        // scrollbars, which is also what "draggable" turned out to mean in practice - see
        // PanningMode="Both" added to that ScrollViewer for real touch-drag panning).
        const double zoomStep = 1.15;
        ZoomInCommand = new RelayCommand(() => Zoom *= zoomStep);
        ZoomOutCommand = new RelayCommand(() => Zoom /= zoomStep);

        // GameSettings.Restore() replaces the whole static GameSettings.Data instance
        // (Data = (GameSettings)s.Deserialize(state)) - re-deriving SettingsPathName here from
        // clientState.GameFolder (which GameSession.Load() sets, from the same real,
        // already-known folder) right before every Restore() call makes this self-sufficient
        // regardless of call order or count, rather than relying on a single earlier assignment
        // surviving untouched. Confirmed via temporary diagnostic logging that this path resolves
        // and deserializes correctly every time; a separate "please locate...settings" dialog was
        // observed appearing regardless, from a call site not yet identified - see
        // PROJECT-STATUS.md's "Unresolved" note for that one, since this fix, while safe and
        // worth keeping, is confirmed NOT to be its cause.
        GameSettings.Data.SettingsPathName = System.IO.Path.Combine(clientState.GameFolder, GameSettings.Data.GameName + ".settings");
        GameSettings.Restore();
        MapWidth = GameSettings.Data.MapWidth;
        MapHeight = GameSettings.Data.MapHeight;

        var stars = new List<StarMapStarViewModel>();
        foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
        {
            bool explored = report.Year > Global.Unset;
            double diameter = explored ? 8 : 4;

            IBrush color;
            // Clicking an owned star selects the real Star (full detail, same object the
            // Navigator would hand Inspector/Production); anything else only has a report.
            object selectable = report;
            if (report.Owner == clientState.EmpireState.Id)
            {
                color = Brushes.GreenYellow;
                if (clientState.EmpireState.OwnedStars.TryGetValue(report.Name, out Star ownStar))
                {
                    selectable = ownStar;
                }
            }
            else if (report.Owner != Global.Nobody)
            {
                color = Brushes.Red;
            }
            else
            {
                color = Brushes.White;
            }

            // Starbase capability dots - see docs/behavior-specs-3/client-interface.md's
            // "Starbase capability indicators" section. That spec also documents a second,
            // distinguished color for the presence dot itself (some specific starbase design
            // draws differently from an ordinary one), but the exact design that represents
            // couldn't be identified from the analyzed client - only plain presence is shown
            // here until that's pinned down.
            bool hasStargate = false;
            bool hasMassDriver = false;
            if (report.Starbase?.Composition.Values.FirstOrDefault()?.Design is ShipDesign starbaseDesign)
            {
                hasStargate = starbaseDesign.Summary.Properties.ContainsKey("Gate");
                hasMassDriver = starbaseDesign.Summary.Properties.ContainsKey("Mass Driver");
            }

            // Orbiting-fleets ring - see StarMap.cs's DrawOrbitingFleets ("orbiting fleets
            // (smaller circle)") for the WinForms original. That code reads a stored
            // Star/StarIntel.HasFleetsInOrbit field, but nothing in this codebase (WinForms or
            // this port) ever actually WRITES that field from real fleet data - only the XML
            // load path sets it, so it's always false in a live game. Computed fresh here
            // instead: for an owned star, directly from this empire's own Fleet.InOrbit
            // references (excluding the starbase itself, which already has its own dot); for
            // any other star, from whichever FleetIntel reports we have that are both marked
            // in orbit and positioned exactly at this star (a report has no direct star
            // reference to check against, unlike an owned Fleet).
            bool hasFleetsInOrbit = selectable is Star realStar
                ? clientState.EmpireState.OwnedFleets.Values.Any(fleet => fleet.InOrbit == realStar && fleet != realStar.Starbase)
                : clientState.EmpireState.FleetReports.Values.Any(fleetReport => fleetReport.InOrbit && fleetReport.Position == report.Position);

            stars.Add(new StarMapStarViewModel(report.Name, report.Position.X, report.Position.Y, diameter, color,
                selectable, selection, report.Starbase != null, hasStargate, hasMassDriver, hasFleetsInOrbit));
        }

        // Orbiting fleets aren't drawn separately - same as the WinForms StarMap.DrawFleet,
        // which only draws a fleet's own triangle when it's NOT in orbit (an orbiting fleet is
        // implied by the star it's sitting on). They're still selectable via the Navigator.
        var fleets = new List<StarMapFleetViewModel>();
        foreach (FleetIntel report in clientState.EmpireState.FleetReports.Values)
        {
            if (report.InOrbit)
            {
                continue;
            }

            bool isOwn = report.Owner == clientState.EmpireState.Id;
            IBrush color = isOwn ? Brushes.GreenYellow : Brushes.OrangeRed;

            object selectable = report;
            if (isOwn && clientState.EmpireState.OwnedFleets.TryGetValue(report.Key, out Fleet ownFleet))
            {
                selectable = ownFleet;
            }

            fleets.Add(new StarMapFleetViewModel(report.Name, report.Position.X, report.Position.Y, report.Bearing, color, selectable, selection));
        }

        // Scan-range washes - long-range (dark red, stars and fleets) and penetrating
        // (olive, fleets only) - only ever for this empire's own things, matching
        // StarMap.cs's "(1a)/(1b)/(2)" comments; zero-range scanners draw nothing.
        IBrush longRangeScan = new SolidColorBrush(Color.FromArgb(128, 128, 0, 0));
        IBrush penScan = new SolidColorBrush(Color.FromArgb(128, 128, 128, 0));
        var scanCircles = new List<StarMapScanCircleViewModel>();

        foreach (Star ownStar in clientState.EmpireState.OwnedStars.Values)
        {
            if (ownStar.ScanRange > 0)
            {
                scanCircles.Add(new StarMapScanCircleViewModel(ownStar.Position.X, ownStar.Position.Y, ownStar.ScanRange, longRangeScan));
            }
        }

        foreach (Fleet ownFleet in clientState.EmpireState.OwnedFleets.Values)
        {
            if (ownFleet.ScanRange > 0)
            {
                scanCircles.Add(new StarMapScanCircleViewModel(ownFleet.Position.X, ownFleet.Position.Y, ownFleet.ScanRange, longRangeScan));
            }

            if (ownFleet.PenScanRange > 0)
            {
                scanCircles.Add(new StarMapScanCircleViewModel(ownFleet.Position.X, ownFleet.Position.Y, ownFleet.PenScanRange, penScan));
            }
        }

        // Minefields - matches StarMap.cs's DetermineVisibleMinefields: visible if owned, or
        // within the scan range of an owned fleet or star (CirclesOverlap, same test used for
        // the scan-range washes above).
        var minefields = new List<StarMapMineFieldViewModel>();
        IBrush ownMineColor = new SolidColorBrush(Color.FromArgb(128, 0, 128, 0));
        IBrush enemyMineColor = new SolidColorBrush(Color.FromArgb(128, 128, 0, 128));

        foreach (Minefield minefield in clientState.InputTurn.AllMinefields.Values)
        {
            bool visible = minefield.Owner == clientState.EmpireState.Id;

            if (!visible)
            {
                foreach (Fleet ownFleet in clientState.EmpireState.OwnedFleets.Values)
                {
                    if (PointUtilities.CirclesOverlap(ownFleet.Position, minefield.Position, ownFleet.ScanRange, minefield.Radius))
                    {
                        visible = true;
                        break;
                    }
                }
            }

            if (!visible)
            {
                foreach (Star ownStar in clientState.EmpireState.OwnedStars.Values)
                {
                    if (PointUtilities.CirclesOverlap(ownStar.Position, minefield.Position, ownStar.ScanRange, minefield.Radius))
                    {
                        visible = true;
                        break;
                    }
                }
            }

            if (!visible)
            {
                continue;
            }

            bool isOwn = minefield.Owner == clientState.EmpireState.Id;
            minefields.Add(new StarMapMineFieldViewModel(minefield.Name, minefield.Position.X, minefield.Position.Y, minefield.Radius, isOwn ? ownMineColor : enemyMineColor, minefield, selection));
        }

        Stars = stars;
        Fleets = fleets;
        ScanCircles = scanCircles;
        Minefields = minefields;
        markers.AddRange(stars);
        markers.AddRange(fleets);
        markers.AddRange(minefields);

        selection.PropertyChanged += OnSelectionChanged;
        SyncSelection();
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionService.Selected))
        {
            SyncSelection();
        }
    }

    private void SyncSelection()
    {
        foreach (MapMarkerViewModel marker in markers)
        {
            marker.IsSelected = ReferenceEquals(marker.Selectable, selection.Selected);
        }

        RouteLegs = BuildRouteLegs(selection.Selected as Fleet);
    }

    private static IReadOnlyList<StarMapRouteLegViewModel> BuildRouteLegs(Fleet selectedFleet)
    {
        if (selectedFleet == null || selectedFleet.Waypoints.Count < 2)
        {
            return System.Array.Empty<StarMapRouteLegViewModel>();
        }

        var legs = new List<StarMapRouteLegViewModel>();
        NovaPoint from = selectedFleet.Waypoints[0].Position;

        for (int i = 1; i < selectedFleet.Waypoints.Count; i++)
        {
            NovaPoint to = selectedFleet.Waypoints[i].Position;
            bool isFirstLeg = i == 1;
            bool isFinalLeg = i == selectedFleet.Waypoints.Count - 1;

            legs.Add(new StarMapRouteLegViewModel(from.X, from.Y, to.X, to.Y, isFirstLeg, isFinalLeg));
            from = to;
        }

        return legs;
    }
}
