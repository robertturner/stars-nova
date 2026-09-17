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
    // A star map generated before StarMapGenerator's own edge margin (see that class's own
    // comment) can still have stars sitting right at X=0/Y=0/MapWidth/MapHeight - existing saves
    // don't get their star positions retroactively moved just because the generator changed. So
    // this is a second, independent fix that helps regardless of where a star actually sits: the
    // rendered Panel is padded by a margin on every side, and every marker's drawn position is
    // shifted into that padding, so nothing anchored to an edge-hugging star can land in Panel
    // coordinates the map's ScrollViewer could never scroll to (it has no negative scroll range,
    // and - confirmed by a user report - zooming out doesn't help either, since this is a Panel
    // coordinate-space problem, not a viewport-size one).
    //
    // A single small constant isn't enough: a star's own marker decorations (the gold selection
    // ring, starbase/stargate/mass-driver dots) only reach a few pixels past its center, but its
    // scan-range wash and long name label can reach much further - a late-game scanner's range
    // can be hundreds of map units, and this margin has to cover the single worst-case reach of
    // anything actually drawn on this particular map, not just the small fixed case. So the
    // margin is computed per-map in the constructor (see MinimumMargin/EstimateNameHalfWidth
    // below), covering the largest of: the fixed marker-decoration minimum, every owned
    // star/fleet's own scan and pen-scan range (the wash's radius), every visible minefield's
    // radius, and the widest star name label likely to be drawn (estimated from character count -
    // the ViewModel has no access to the View's actual measured text width).
    private const double MinimumMargin = 20;

    // Rough average glyph advance width for the star-name TextBlock's FontSize="10" in
    // StarMapDocumentView.axaml - used only to estimate how far a long name's label can reach
    // past its star (see edgeMargin's own comment), not for any actual layout.
    private const double ApproxCharWidthAtFontSize10 = 6.5;

    private readonly double edgeMargin;

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

    public HullViewerViewModel HullViewer { get; } = new HullViewerViewModel();

    /// <summary>Mirrors SelectionService.IsAddingWaypoint - see OnSelectionChanged. Drives the
    /// "tap a planet to add a waypoint" banner regardless of which panel armed it (the button
    /// that arms it lives in the Inspector, not here).</summary>
    public bool IsAddingWaypoint => selection.IsAddingWaypoint;

    public IRelayCommand CancelAddWaypointCommand { get; }

    /// <summary>Mirrors SelectionService.IsMeasuringDistance - see OnSelectionChanged. Drives
    /// this panel's own "tap something to measure distance" banner.</summary>
    public bool IsMeasuringDistance => selection.IsMeasuringDistance;

    public IRelayCommand CancelMeasureCommand { get; }

    /// <summary>Arms distance measuring FROM whatever's currently selected - enabled only when
    /// that's a real map entity (a Star/StarIntel/Fleet/FleetIntel/Minefield/Wormhole, all
    /// Mappable), since a distance needs two real points.</summary>
    public IRelayCommand MeasureDistanceCommand { get; }

    private string? measureDistanceResult;

    /// <summary>Set once a measurement completes (see MeasureDistanceCommand) - "{from} to {to}:
    /// {distance:0.0} ly". Cleared by SyncSelection whenever the selection itself changes, and
    /// by arming/cancelling a new measurement, so a stale readout never lingers past whatever it
    /// was actually about.</summary>
    public string? MeasureDistanceResult
    {
        get => measureDistanceResult;
        private set => SetProperty(ref measureDistanceResult, value);
    }

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
        CancelAddWaypointCommand = new RelayCommand(() => selection.CancelWaypointTarget());
        CancelMeasureCommand = new RelayCommand(() =>
        {
            selection.CancelMeasureTarget();
            MeasureDistanceResult = null;
        });
        MeasureDistanceCommand = new RelayCommand(ArmMeasureDistance, () => selection.Selected is Mappable);

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

        // Minefields visible to this empire - matches StarMap.cs's DetermineVisibleMinefields:
        // visible if owned, or within the scan range of an owned fleet or star (CirclesOverlap,
        // same test used for the scan-range washes below). Computed up front, before the margin
        // below, since a visible minefield's own radius is one of the things that margin has to
        // cover - the CirclesOverlap checks themselves use the game's real, un-padded positions,
        // so computing this early changes nothing about which minefields end up visible.
        var visibleMinefields = new List<(Minefield Minefield, bool IsOwn)>();
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

            if (visible)
            {
                visibleMinefields.Add((minefield, minefield.Owner == clientState.EmpireState.Id));
            }
        }

        // Widest reach of anything actually drawn on THIS map, so the margin below is only as
        // big as it needs to be (see edgeMargin's own comment) - a fixed guess couldn't cover the
        // full range of possible scanner tech without either clipping high-tech games or wasting
        // a huge, mostly-empty border on every low-tech one.
        double maxScanRadius = clientState.EmpireState.OwnedStars.Values.Select(star => (double)star.ScanRange)
            .Concat(clientState.EmpireState.OwnedFleets.Values.Select(fleet => (double)fleet.ScanRange))
            .Concat(clientState.EmpireState.OwnedFleets.Values.Select(fleet => (double)fleet.PenScanRange))
            .DefaultIfEmpty(0)
            .Max();
        double maxMinefieldRadius = visibleMinefields.Select(entry => (double)entry.Minefield.Radius)
            .DefaultIfEmpty(0)
            .Max();
        double maxNameHalfWidth = clientState.EmpireState.StarReports.Values
            .Select(report => EstimateNameHalfWidth(report.Name))
            .DefaultIfEmpty(0)
            .Max();
        edgeMargin = new[] { MinimumMargin, maxScanRadius, maxMinefieldRadius, maxNameHalfWidth }.Max();

        MapWidth = GameSettings.Data.MapWidth + (edgeMargin * 2);
        MapHeight = GameSettings.Data.MapHeight + (edgeMargin * 2);

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
            // "Starbase capability indicators" section. That spec documents a second,
            // distinguished color for the presence dot itself (some specific starbase design
            // draws differently from an ordinary one), but the exact original criterion couldn't
            // be identified from the analyzed client. Adopted here instead: whether the design's
            // own hull has any DockCapacity at all - a real Stars! game concept (only a proper
            // Starbase-class hull, e.g. "Space Station", can build ships; a defense-only orbital
            // platform like "Orbital Fort" never can) rather than a hardcoded hull-name check, so
            // it generalizes to any future player-designed starbase too. This is exactly what an
            // Interstellar Traveler/Packet Physics start needs distinguished on the map: its home
            // star's full combat "Starbase" and its second planet's small "Stargate"/"Mass Driver
            // Base" (see StarMapInitialiser.PrepareDesigns) both carry the same Gate/Mass Driver
            // component now (see PROJECT-STATUS.md's starbase-split fix), so without this the two
            // planets' dots were reported as looking visually identical despite being genuinely
            // different starbases.
            bool hasStargate = false;
            bool hasMassDriver = false;
            bool isFullStarbase = false;
            if (report.Starbase?.Composition.Values.FirstOrDefault()?.Design is ShipDesign starbaseDesign)
            {
                hasStargate = starbaseDesign.Summary.Properties.ContainsKey("Gate");
                hasMassDriver = starbaseDesign.Summary.Properties.ContainsKey("Mass Driver");
                isFullStarbase = starbaseDesign.Hull.DockCapacity > 0;
            }

            // Orbiting-fleets ring - see StarMap.cs's DrawOrbitingFleets ("orbiting fleets
            // (smaller circle)") for the WinForms original. That code reads a stored
            // Star/StarIntel.HasFleetsInOrbit field, but nothing in this codebase (WinForms or
            // this port) ever actually WRITES that field from real fleet data - only the XML
            // load path sets it, so it's always false in a live game. Computed fresh here
            // instead, and in two halves rather than one bool - docs/behavior-specs-5/
            // client-interface.md's "Fleet-in-orbit ring" note (confirmed via a fixed emulator
            // build after an earlier pass wrongly concluded no such ring existed at all) says
            // this ring is white for the viewer's own fleet, red for a foreign one, and a third
            // color when both are present at once - not one fixed color regardless of ownership.
            //
            // "Own" comes directly from this empire's own Fleet.InOrbit references (matched by
            // name, which works whether or not we own the star itself - a foreign/unowned star's
            // Starbase, if any, is never in OUR OwnedFleets, so excluding "our own star's
            // starbase" specifically is the only starbase exclusion actually needed here).
            // "Foreign" comes from whichever FleetIntel reports we have that are marked in orbit
            // at this exact position and belong to someone else - a report has no direct star
            // reference to check against, unlike an owned Fleet.
            bool hasOwnFleetInOrbit = clientState.EmpireState.OwnedFleets.Values.Any(fleet =>
                fleet.InOrbit != null && fleet.InOrbit.Name == report.Name && fleet != (selectable as Star)?.Starbase);
            bool hasForeignFleetInOrbit = clientState.EmpireState.FleetReports.Values.Any(fleetReport =>
                fleetReport.InOrbit && fleetReport.Position == report.Position && fleetReport.Owner != clientState.EmpireState.Id);

            stars.Add(new StarMapStarViewModel(report.Name, report.Position.X + edgeMargin, report.Position.Y + edgeMargin, diameter, color,
                selectable, selection, report.Starbase != null, isFullStarbase, hasStargate, hasMassDriver, hasOwnFleetInOrbit, hasForeignFleetInOrbit));
        }

        // Orbiting fleets aren't drawn separately - same as the WinForms StarMap.DrawFleet,
        // which only draws a fleet's own triangle when it's NOT in orbit (an orbiting fleet is
        // implied by the star it's sitting on). They're still selectable via the Navigator.
        var fleets = new List<StarMapFleetViewModel>();
        foreach (FleetIntel report in clientState.EmpireState.FleetReports.Values)
        {
            bool isOwn = report.Owner == clientState.EmpireState.Id;

            // A fleet's own self-report is only ever refreshed once a turn (ScanStep, server-
            // side) - if that update is ever missed for a given fleet (e.g. one just created this
            // turn by a Split/Merge, before its own report exists at all), the report's Position/
            // Bearing/InOrbit/Count can lag behind the live Fleet by a turn or more, which showed
            // up live as a real, reported bug: the selected fleet's own route legs (drawn from
            // the live Fleet - see BuildRouteLegs) were correct, but its map marker (drawn from
            // the stale report) rendered nowhere near them. For an owned fleet we always have the
            // live, authoritative Fleet object right here - prefer it over the report for
            // everything the report could possibly be behind on, rather than only for
            // `selectable`. A foreign fleet has no live object to fall back to - its report is
            // genuinely the only thing we know.
            Fleet? ownFleet = isOwn && clientState.EmpireState.OwnedFleets.TryGetValue(report.Key, out Fleet fleet) ? fleet : null;

            bool inOrbit = ownFleet != null ? ownFleet.InOrbit != null : report.InOrbit;
            if (inOrbit)
            {
                continue;
            }

            IBrush color = isOwn ? Brushes.GreenYellow : Brushes.OrangeRed;
            object selectable = ownFleet ?? (object)report;

            NovaPoint position = ownFleet?.Position ?? report.Position;
            double bearing = ownFleet?.Bearing ?? report.Bearing;
            int shipCount = ownFleet?.Composition.Values.Sum(token => token.Quantity) ?? report.Count;

            fleets.Add(new StarMapFleetViewModel(report.Name, position.X + edgeMargin, position.Y + edgeMargin, bearing, color, selectable, selection, shipCount));
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
                scanCircles.Add(new StarMapScanCircleViewModel(ownStar.Position.X + edgeMargin, ownStar.Position.Y + edgeMargin, ownStar.ScanRange, longRangeScan));
            }
        }

        foreach (Fleet ownFleet in clientState.EmpireState.OwnedFleets.Values)
        {
            if (ownFleet.ScanRange > 0)
            {
                scanCircles.Add(new StarMapScanCircleViewModel(ownFleet.Position.X + edgeMargin, ownFleet.Position.Y + edgeMargin, ownFleet.ScanRange, longRangeScan));
            }

            if (ownFleet.PenScanRange > 0)
            {
                scanCircles.Add(new StarMapScanCircleViewModel(ownFleet.Position.X + edgeMargin, ownFleet.Position.Y + edgeMargin, ownFleet.PenScanRange, penScan));
            }
        }

        // Minefields - visibility already computed above (see visibleMinefields' own comment).
        var minefields = new List<StarMapMineFieldViewModel>();
        IBrush ownMineColor = new SolidColorBrush(Color.FromArgb(128, 0, 128, 0));
        IBrush enemyMineColor = new SolidColorBrush(Color.FromArgb(128, 128, 0, 128));

        foreach ((Minefield minefield, bool isOwn) in visibleMinefields)
        {
            minefields.Add(new StarMapMineFieldViewModel(minefield.Name, minefield.Position.X + edgeMargin, minefield.Position.Y + edgeMargin, minefield.Radius, isOwn ? ownMineColor : enemyMineColor, minefield, selection));
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
        else if (e.PropertyName == nameof(SelectionService.IsAddingWaypoint))
        {
            OnPropertyChanged(nameof(IsAddingWaypoint));
        }
        else if (e.PropertyName == nameof(SelectionService.IsMeasuringDistance))
        {
            OnPropertyChanged(nameof(IsMeasuringDistance));
        }
    }

    private void SyncSelection()
    {
        foreach (MapMarkerViewModel marker in markers)
        {
            marker.IsSelected = ReferenceEquals(marker.Selectable, selection.Selected);
        }

        RouteLegs = BuildRouteLegs(selection.Selected as Fleet);
        MeasureDistanceCommand.NotifyCanExecuteChanged();

        // A stale "Nova to Diddley: 92.3 ly" readout would be confusing once the player's moved
        // on to inspecting something else entirely - cleared on every genuine selection change,
        // not just when a new measurement starts.
        MeasureDistanceResult = null;
    }

    /// <summary>
    /// Captures the "from" point/name at arm-time (whatever's selected right now - reading
    /// selection.Selected again inside the callback would risk it having changed by the time
    /// the second tap actually lands), then measures to whatever the next map tap resolves to,
    /// via the same Mappable.Position/Name every selectable thing on the map already exposes.
    /// </summary>
    private void ArmMeasureDistance()
    {
        if (selection.Selected is not Mappable from)
        {
            return;
        }

        MeasureDistanceResult = null;
        string fromName = from.Name;
        NovaPoint fromPosition = from.Position;

        selection.ArmMeasureTarget((toPosition, toName) =>
        {
            double distance = PointUtilities.Distance(fromPosition, toPosition);
            MeasureDistanceResult = $"{fromName} to {toName}: {distance:0.0} ly";
        });
    }

    // Both star and fleet markers share the identical fixed 32x32 (16px-radius) tap target -
    // see StarMapDocumentView.axaml's own comment on each - and fleets are declared after (so
    // drawn on top of) stars in the same Panel, so whenever a fleet sits within that radius of
    // its star, the fleet's Button always won a tap regardless of which one the tap point was
    // actually closer to (confirmed live: a fleet sitting at/near its home star made the star
    // itself nearly unclickable). This resolves the tie properly - among every star/fleet marker
    // whose own 16px hit-radius contains the tap, whichever CENTER is nearest to the actual tap
    // point wins - called from the map's own Tunnel-phase pointer handler (StarMapDocumentView.
    // axaml.cs), which fires before either marker's Button gets a chance to react on its own.
    // Deliberately excludes minefields: their own hit area is the field's real (and often much
    // larger) radius, already works correctly via its own Button, and sits behind both marker
    // kinds in z-order, so it's never part of this specific tie.
    //
    // A genuine tie (a fleet sitting exactly on its own star, distance 0 from both) still needs
    // a tiebreaker, since "nearest" alone can't distinguish them - which one wins depends on
    // whether a waypoint/measure gesture is currently armed: unarmed (plain browsing), the star
    // wins, matching the original bug report ("clicking the star is almost impossible"); armed
    // (see SelectionService.IsAddingWaypoint/IsMeasuringDistance), a FLEET wins instead, since
    // arming that gesture (e.g. picking "Merge With Fleet" as the waypoint task, or measuring
    // distance to a specific ship) means the user is very likely aiming for a particular fleet,
    // not the star it happens to be sitting at - the same star remains reachable by tapping it
    // again once whichever fleet(s) were there have been individually addressed, or by using
    // Navigator/the "Viewing" switcher instead.
    private const double MarkerHitRadius = 16.0;

    public MapMarkerViewModel? FindNearestStarOrFleetMarker(double x, double y)
    {
        bool preferFleets = selection.IsAddingWaypoint || selection.IsMeasuringDistance;
        IEnumerable<MapMarkerViewModel> candidates = preferFleets
            ? Fleets.Cast<MapMarkerViewModel>().Concat(Stars)
            : Stars.Cast<MapMarkerViewModel>().Concat(Fleets);

        MapMarkerViewModel? nearest = null;
        double nearestDistanceSquared = double.MaxValue;

        foreach (MapMarkerViewModel marker in candidates)
        {
            double dx = marker.X - x;
            double dy = marker.Y - y;
            double distanceSquared = (dx * dx) + (dy * dy);

            if (distanceSquared <= MarkerHitRadius * MarkerHitRadius && distanceSquared < nearestDistanceSquared)
            {
                nearest = marker;
                nearestDistanceSquared = distanceSquared;
            }
        }

        return nearest;
    }

    private IReadOnlyList<StarMapRouteLegViewModel> BuildRouteLegs(Fleet selectedFleet)
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

            legs.Add(new StarMapRouteLegViewModel(from.X + edgeMargin, from.Y + edgeMargin, to.X + edgeMargin, to.Y + edgeMargin, isFirstLeg, isFinalLeg));
            from = to;
        }

        return legs;
    }

    /// <summary>How far a star's name label can reach past its own center - half of the label's
    /// estimated total width, since StarMapDocumentView.axaml centers it on that point (a
    /// TranslateTransform bound to the TextBlock's own measured Bounds.Width). Character count
    /// times an approximate glyph width, not a real text measurement - see
    /// ApproxCharWidthAtFontSize10's own comment for why the ViewModel can't do better than
    /// estimate this.</summary>
    private static double EstimateNameHalfWidth(string name)
    {
        return string.IsNullOrEmpty(name) ? 0 : name.Length * ApproxCharWidthAtFontSize10 / 2.0;
    }
}
