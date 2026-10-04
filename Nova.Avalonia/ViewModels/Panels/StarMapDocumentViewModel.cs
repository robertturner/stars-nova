using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Client.Map;
using Nova.Common;
using Nova.Common.Components;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Star Map document: every star and fleet this empire has a report on, positioned and
/// colored the same way the WinForms StarMap does (see StarMap.cs's DrawStar) - white for an
/// unowned/unexplored star, green for the player's own things, red for anyone else's. Panning is
/// the ScrollViewer's own scrollbars, mouse click-drag, and touch drag; zoom moves through the
/// spec's 9 fixed steps (see <see cref="ZoomLevel"/>) from the view's mouse-wheel handler and the
/// ZoomIn/ZoomOut/Reset buttons (see StarMapDocumentView.axaml.cs) - the buttons exist because a
/// touchscreen never raises a wheel event at all.
///
/// The map-side rules (zoom factors, view-option slots and digit keys, scan-circle sizing and
/// culling, route dashing, the right-click picker, the tooltips, Find, the planet overlays and
/// the tracked-fleet chevron) live in Nova.Client.Map so they are unit tested; this class only
/// feeds them this empire's data and turns their answers into marker view models.
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
    // the ViewModel has no access to the View's actual measured text width). A scanner
    // percentage below 100% enlarges the drawn circles past this margin; those are simply clipped
    // at the panel edge.
    private const double MinimumMargin = 20;

    // Rough average glyph advance width for the star-name TextBlock's FontSize="10" in
    // StarMapDocumentView.axaml - used only to estimate how far a long name's label can reach
    // past its star (see edgeMargin's own comment), not for any actual layout.
    private const double ApproxCharWidthAtFontSize10 = 6.5;

    private readonly double edgeMargin;

    private readonly ClientData clientState;
    private readonly SelectionService selection;
    private readonly List<MapMarkerViewModel> markers = new();

    // Every object Find and the right-click picker can reach, in panel coordinates.
    private readonly List<MapObjectEntry> mapObjects = new();

    // True scan ranges, in panel coordinates, in the order the circles are queued: stars, then
    // fleets. Kept so the circles can be rebuilt when the scanner percentage changes.
    private readonly List<MapCircle> primaryScanSources = new();
    private readonly List<MapCircle> penetratingScanSources = new();

    private static readonly IBrush LongRangeScanBrush = new SolidColorBrush(Color.FromArgb(128, 128, 0, 0));
    private static readonly IBrush PenScanBrush = new SolidColorBrush(Color.FromArgb(128, 128, 128, 0));

    /// <summary>
    /// The map's packed view options (behavior-specs-10/client-interface.md, "Shared view-option
    /// slots"). Static so the choice survives the map being rebuilt for a new turn; persisting it
    /// to disk (client-interface.md row 9, "Persisted settings") is not done here. The same mode
    /// nibble is the object-information panel's content mode, so other panels may read it.
    /// </summary>
    public static MapViewOptions ViewOptions { get; } = new MapViewOptions();

    public IReadOnlyList<StarMapStarViewModel> Stars { get; }

    public IReadOnlyList<StarMapFleetViewModel> Fleets { get; }

    public IReadOnlyList<StarMapWormholeViewModel> Wormholes { get; }

    public IReadOnlyList<StarMapPacketViewModel> Packets { get; }

    private IReadOnlyList<StarMapScanCircleViewModel> scanCircles = Array.Empty<StarMapScanCircleViewModel>();

    /// <summary>Scan-range washes after the scanner-percentage correction and nested-circle
    /// culling (see BuildScanCircles).</summary>
    public IReadOnlyList<StarMapScanCircleViewModel> ScanCircles
    {
        get => scanCircles;
        private set => SetProperty(ref scanCircles, value);
    }

    public IReadOnlyList<StarMapMineFieldViewModel> Minefields { get; }

    private IReadOnlyList<StarMapRouteLegViewModel> routeLegs = Array.Empty<StarMapRouteLegViewModel>();

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

    // ---------------- zoom ----------------

    private int zoomLevel = MapZoom.DefaultLevel;

    /// <summary>One of the 9 fixed zoom steps, -4 (25%) to +4 (400%) - see Nova.Client.Map.MapZoom.
    /// The view recentres on the same world point whenever this changes.</summary>
    public int ZoomLevel
    {
        get => zoomLevel;
        set
        {
            if (SetProperty(ref zoomLevel, MapZoom.Clamp(value)))
            {
                OnPropertyChanged(nameof(Zoom));
                OnPropertyChanged(nameof(ZoomLabel));
                OnPropertyChanged(nameof(ZoomLevelIndex));
                foreach (StarMapStarViewModel star in Stars)
                {
                    star.ApplyZoom(zoomLevel);
                }
            }
        }
    }

    /// <summary>The exact scale factor of <see cref="ZoomLevel"/> (bound to the map's ScaleTransform).</summary>
    public double Zoom => MapZoom.ScaleFactor(zoomLevel);

    public string ZoomLabel => "Zoom " + MapZoom.Label(zoomLevel);

    public IReadOnlyList<string> ZoomLevelLabels { get; } =
        Enumerable.Range(MapZoom.MinLevel, MapZoom.MaxLevel - MapZoom.MinLevel + 1).Select(MapZoom.Label).ToList();

    /// <summary>0-8 index into <see cref="ZoomLevelLabels"/> (the View > Zoom items).</summary>
    public int ZoomLevelIndex
    {
        get => zoomLevel - MapZoom.MinLevel;
        set
        {
            if (value >= 0)
            {
                ZoomLevel = value + MapZoom.MinLevel;
            }
        }
    }

    public IRelayCommand ResetZoomCommand { get; }

    public IRelayCommand ZoomInCommand { get; }

    public IRelayCommand ZoomOutCommand { get; }

    /// <summary>Raised to ask the view to scroll so panel point (X, Y) is centred (Find).</summary>
    public event Action<double, double>? CenterOnRequested;

    // ---------------- view options ----------------

    /// <summary>The "Planets:" selector's captions (see MapViewOptions.ModeLabels' SPEC GAP note).</summary>
    public IReadOnlyList<string> PlanetModeLabels { get; } = MapViewOptions.ModeLabels;

    /// <summary>The 6-way "Planets:" mode (slots 0-5, keys 1-6).</summary>
    public int PlanetMode
    {
        get => ViewOptions.Mode;
        set
        {
            ViewOptions.Mode = value;
            ApplyViewOptions();
        }
    }

    /// <summary>Scan circles (bit 0x20, key 7).</summary>
    public bool ShowScanCircles
    {
        get => ViewOptions.ShowScanCircles;
        set
        {
            ViewOptions.ShowScanCircles = value;
            ApplyViewOptions();
        }
    }

    /// <summary>Minefields (bit 0x40, key 8).</summary>
    public bool ShowMinefields
    {
        get => ViewOptions.ShowMinefields;
        set
        {
            ViewOptions.ShowMinefields = value;
            ApplyViewOptions();
        }
    }

    /// <summary>Route-overlap dashing (bit 0x80, key 9).</summary>
    public bool ShowRouteOverlap
    {
        get => ViewOptions.ShowRouteOverlap;
        set
        {
            ViewOptions.ShowRouteOverlap = value;
            ApplyViewOptions();
        }
    }

    /// <summary>Planet names (second word bit 0x04, key 0).</summary>
    public bool ShowPlanetNames
    {
        get => ViewOptions.ShowPlanetNames;
        set
        {
            ViewOptions.ShowPlanetNames = value;
            ApplyViewOptions();
        }
    }

    /// <summary>Ship-count badges (second word bit 0x10, Shift+0).</summary>
    public bool ShowShipCountBadges
    {
        get => ViewOptions.ShowShipCountBadges;
        set
        {
            ViewOptions.ShowShipCountBadges = value;
            ApplyViewOptions();
        }
    }

    /// <summary>Scanner display percentage, 2-100 (changing it force-enables the scan circles).
    /// The spec puts this control in the planet inspector; it is offered here on the map's own
    /// view-options popup instead, since the inspector is a separate panel.</summary>
    public int ScannerPercentage
    {
        get => ViewOptions.ScannerPercentage;
        set
        {
            ViewOptions.ScannerPercentage = value;
            ApplyViewOptions();
        }
    }

    /// <summary>Applies a top-row digit key (desktop): 1-6 modes, 7/8/9 scan/minefields/route
    /// dashing, 0 planet names, Shift+0 badges. False when the key is not bound.</summary>
    public bool HandleDigitKey(int digit, bool shift)
    {
        if (!ViewOptions.ApplyDigitKey(digit, shift))
        {
            return false;
        }

        ApplyViewOptions();
        return true;
    }

    public StarMapSearchViewModel Search { get; }

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
        this.clientState = clientState;
        this.selection = selection;
        ResetZoomCommand = new RelayCommand(() => ZoomLevel = MapZoom.DefaultLevel);
        // Buttons, not just the mouse-wheel handler in StarMapDocumentView.axaml.cs - a
        // touchscreen never raises a wheel event at all, so without these, zoom was completely
        // unreachable on Android.
        ZoomInCommand = new RelayCommand(() => ZoomLevel++);
        ZoomOutCommand = new RelayCommand(() => ZoomLevel--);
        CancelAddWaypointCommand = new RelayCommand(() => selection.CancelWaypointTarget());
        CancelMeasureCommand = new RelayCommand(() =>
        {
            selection.CancelMeasureTarget();
            MeasureDistanceResult = null;
        });
        MeasureDistanceCommand = new RelayCommand(ArmMeasureDistance, () => selection.Selected is Mappable);
        Search = new StarMapSearchViewModel(() => mapObjects, FocusEntry);

        // GameSettings.Restore() replaces the whole static GameSettings.Data instance
        // (Data = (GameSettings)s.Deserialize(state)) - re-deriving SettingsPathName here from
        // clientState.GameFolder (which GameSession.Load() sets, from the same real,
        // already-known folder) right before every Restore() call makes this self-sufficient
        // regardless of call order or count, rather than relying on a single earlier assignment
        // surviving untouched.
        GameSettings.Data.SettingsPathName = System.IO.Path.Combine(clientState.GameFolder, GameSettings.Data.GameName + ".settings");
        GameSettings.Restore();

        EmpireData empire = clientState.EmpireState;

        // Minefields this empire may be shown, per the engine's own rule (EmpireData.
        // CanSeeMinefield): its own fields, fields that have shown themselves by striking its
        // fleets (Minefield.VisibleTo - these stay visible out of scan range), and fields the
        // server's ScanStep detected this year (EmpireData.VisibleMinefields). Computed up front,
        // since a visible minefield's own radius is one of the things the margin below covers.
        var visibleMinefields = new List<(Minefield Minefield, bool IsOwn)>();
        foreach (Minefield minefield in clientState.InputTurn.AllMinefields.Values)
        {
            if (empire.CanSeeMinefield(minefield))
            {
                visibleMinefields.Add((minefield, minefield.Owner == empire.Id));
            }
        }

        // Widest reach of anything actually drawn on THIS map, so the margin below is only as
        // big as it needs to be (see edgeMargin's own comment).
        double maxScanRadius = empire.OwnedStars.Values.Select(star => (double)star.ScanRange)
            .Concat(empire.OwnedFleets.Values.Select(fleet => (double)fleet.ScanRange))
            .Concat(empire.OwnedFleets.Values.Select(fleet => (double)fleet.PenScanRange))
            .DefaultIfEmpty(0)
            .Max();
        double maxMinefieldRadius = visibleMinefields.Select(entry => (double)entry.Minefield.Radius)
            .DefaultIfEmpty(0)
            .Max();
        double maxNameHalfWidth = empire.StarReports.Values
            .Select(report => EstimateNameHalfWidth(report.Name))
            .DefaultIfEmpty(0)
            .Max();
        edgeMargin = new[] { MinimumMargin, maxScanRadius, maxMinefieldRadius, maxNameHalfWidth }.Max();

        MapWidth = GameSettings.Data.MapWidth + (edgeMargin * 2);
        MapHeight = GameSettings.Data.MapHeight + (edgeMargin * 2);

        Race race = empire.Race;
        bool isClaimAdjuster = race != null && race.HasTrait("CA");

        // The amount-mode bar's "shared reference maximum" (SPEC GAP: its value is not given).
        // Neutral reading: the largest single surface-mineral amount on any of this empire's own
        // planets this turn, so the fullest bar on the map is full-height.
        int mineralReferenceMaximum = empire.OwnedStars.Values
            .Where(star => star.ResourcesOnHand != null)
            .SelectMany(star => new[] { star.ResourcesOnHand.Ironium, star.ResourcesOnHand.Boranium, star.ResourcesOnHand.Germanium })
            .DefaultIfEmpty(0)
            .Max();

        var stars = new List<StarMapStarViewModel>();
        foreach (StarIntel report in empire.StarReports.Values)
        {
            bool explored = report.Year > Global.Unset;
            double diameter = explored ? 8 : 4;

            IBrush color;
            // Clicking an owned star selects the real Star (full detail, same object the
            // Navigator would hand Inspector/Production); anything else only has a report.
            object selectable = report;
            Star? ownStar = null;
            if (report.Owner == empire.Id)
            {
                color = Brushes.GreenYellow;
                if (empire.OwnedStars.TryGetValue(report.Name, out Star? found))
                {
                    selectable = found;
                    ownStar = found;
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
            // "Starbase capability indicators" section. Adopted here for the presence dot's second
            // colour: whether the design's own hull has any DockCapacity at all (spec-10 resolves
            // the original's test to "hull is the Orbital Fort chassis", which this matches for the
            // stock hulls without a hull-name check).
            bool hasStargate = false;
            bool hasMassDriver = false;
            bool isFullStarbase = false;
            if (report.Starbase?.Composition.Values.FirstOrDefault()?.Design is ShipDesign starbaseDesign)
            {
                hasStargate = starbaseDesign.Summary.Properties.ContainsKey("Gate");
                hasMassDriver = starbaseDesign.Summary.Properties.ContainsKey("Mass Driver");
                isFullStarbase = starbaseDesign.Hull.DockCapacity > 0;
            }

            // Orbiting-fleets ring - own half from this empire's own Fleet.InOrbit references
            // (matched by name, excluding our own star's starbase), foreign half from FleetIntel
            // reports marked in orbit at this exact position.
            bool hasOwnFleetInOrbit = empire.OwnedFleets.Values.Any(fleet =>
                fleet.InOrbit != null && fleet.InOrbit.Name == report.Name && fleet != ownStar?.Starbase);
            bool hasForeignFleetInOrbit = empire.FleetReports.Values.Any(fleetReport =>
                fleetReport.InOrbit && fleetReport.Position == report.Position && fleetReport.Owner != empire.Id);

            var star = new StarMapStarViewModel(report.Name, report.Position.X + edgeMargin, report.Position.Y + edgeMargin, diameter, color,
                selectable, selection, report.Starbase != null, isFullStarbase, hasStargate, hasMassDriver, hasOwnFleetInOrbit, hasForeignFleetInOrbit);

            MapOwnership ownership = OwnershipOf(report.Owner);

            // Habitability for the viewing race (Race.HabPercent, the original's integer
            // -45..100 evaluator). "Exact" uses the live Star where this empire owns it; the
            // "estimated" re-derivation uses the report's visible readings. For any planet this
            // empire does not own both come from the same report, so the olive (sign-disagreement)
            // styling can only arise on an own planet whose report lags its live environment.
            int? habitability = null;
            if (explored && race != null)
            {
                int estimated = (int)Math.Round(race.HabitalValue(report) * 100);
                int exact = ownStar != null ? race.HabPercent(ownStar) : estimated;
                habitability = exact;
                star.SetHabitability(exact, PlanetOverlayRules.HabitabilityColour(exact, estimated, isClaimAdjuster), report.Owner == Global.Nobody);
            }

            // Population: own planets read the live colonist count, others the report's coarser
            // scanned figure (only filled in by a penetrating or in-orbit scan).
            int? population = ownStar != null ? ownStar.Colonists : (report.Owner != Global.Nobody && report.Colonists > 0 ? report.Colonists : null);
            if (population != null)
            {
                bool friendly = report.Owner != empire.Id
                    && empire.EmpireReports.TryGetValue(report.Owner, out EmpireIntel? intel)
                    && intel.Relation == PlayerRelation.Friend;
                star.SetPopulation(PlanetOverlayRules.PopulationSteps(population.Value), PlanetOverlayRules.PopulationColour(ownership, friendly));
                star.ApplyZoom(zoomLevel);
            }

            int[]? amounts = ownStar?.ResourcesOnHand == null ? null : new[]
            {
                PlanetOverlayRules.AmountBarSteps(ownStar.ResourcesOnHand.Ironium, mineralReferenceMaximum),
                PlanetOverlayRules.AmountBarSteps(ownStar.ResourcesOnHand.Boranium, mineralReferenceMaximum),
                PlanetOverlayRules.AmountBarSteps(ownStar.ResourcesOnHand.Germanium, mineralReferenceMaximum),
            };
            Resources? concentration = ownStar?.MineralConcentration ?? (explored ? report.MineralConcentration : null);
            int[]? concentrations = concentration == null ? null : new[]
            {
                PlanetOverlayRules.ConcentrationBarSteps(concentration.Ironium),
                PlanetOverlayRules.ConcentrationBarSteps(concentration.Boranium),
                PlanetOverlayRules.ConcentrationBarSteps(concentration.Germanium),
            };
            star.SetMineralBars(amounts, concentrations);

            int? scannedPopulation = ownership == MapOwnership.Other && report.Colonists > 0 ? report.Colonists : null;
            // Mode 7's ownership/habitability sentences replace the old "owned by" tooltip (there is
            // no such wording in the client). The map hover only has the report level's coarse
            // equivalent (explored), no defence reading (nibble -1), and no max-population/growth
            // figures, so those sentences are simply left out here.
            star.ToolTipText = string.Join(Environment.NewLine, MapObjectText.PopulationPopup(new PlanetPopupFacts
            {
                Name = report.Name,
                Ownership = ownership,
                ReportLevel = explored ? 3 : 0,
                Population = ownStar == null ? 0 : ownStar.Colonists / 100,
                PopulationEstimate = scannedPopulation == null ? 0 : scannedPopulation.Value / 100,
                Habitability = habitability ?? 0,
                DefenceNibble = -1,
            }));

            stars.Add(star);
            mapObjects.Add(new MapObjectEntry(selectable, report.Name, MapObjectKind.Planet, star.X, star.Y));
        }

        // Orbiting fleets aren't drawn separately - same as the WinForms StarMap.DrawFleet,
        // which only draws a fleet's own triangle when it's NOT in orbit (an orbiting fleet is
        // implied by the star it's sitting on). They are still reachable through the right-click
        // picker, Find and the Navigator.
        var fleets = new List<StarMapFleetViewModel>();
        var starbases = new HashSet<Fleet>(empire.OwnedStars.Values.Where(star => star.Starbase != null).Select(star => star.Starbase));
        foreach (FleetIntel report in empire.FleetReports.Values)
        {
            bool isOwn = report.Owner == empire.Id;

            // An owned fleet's own self-report can lag the live Fleet by a turn (e.g. one just
            // created by a Split/Merge) - prefer the live, authoritative Fleet for everything the
            // report could be behind on. A foreign fleet has only its report.
            Fleet? ownFleet = isOwn && empire.OwnedFleets.TryGetValue(report.Key, out Fleet? fleet) ? fleet : null;
            if (ownFleet != null && starbases.Contains(ownFleet))
            {
                continue;
            }

            object selectable = ownFleet ?? (object)report;
            NovaPoint position = ownFleet?.Position ?? report.Position;
            mapObjects.Add(new MapObjectEntry(selectable, report.Name, MapObjectKind.Fleet, position.X + edgeMargin, position.Y + edgeMargin));

            bool inOrbit = ownFleet != null ? ownFleet.InOrbit != null : report.InOrbit;
            if (inOrbit)
            {
                continue;
            }

            IBrush color = isOwn ? Brushes.GreenYellow : Brushes.OrangeRed;
            double bearing = ownFleet?.Bearing ?? report.Bearing;
            int shipCount = ownFleet?.Composition.Values.Sum(token => token.Quantity) ?? report.Count;

            var fleetMarker = new StarMapFleetViewModel(report.Name, position.X + edgeMargin, position.Y + edgeMargin, bearing, color, selectable, selection, shipCount, isOwn)
            {
                ToolTipText = MapObjectText.Identify(MapObjectKind.Fleet, report.Name, OwnershipOf(report.Owner), OwnerName(report.Owner)),
            };
            fleets.Add(fleetMarker);
        }

        // Owned fleets that have no self-report yet (the same Split/Merge lag) are still
        // reachable through the picker and Find.
        foreach (Fleet ownFleet in empire.OwnedFleets.Values)
        {
            if (!starbases.Contains(ownFleet) && !mapObjects.Any(entry => ReferenceEquals(entry.Item, ownFleet)))
            {
                mapObjects.Add(new MapObjectEntry(ownFleet, ownFleet.Name, MapObjectKind.Fleet, ownFleet.Position.X + edgeMargin, ownFleet.Position.Y + edgeMargin));
            }
        }

        // Scan-range sources - long-range (stars and fleets) and penetrating (fleets only), only
        // ever for this empire's own things; zero-range scanners draw nothing.
        foreach (Star ownStar in empire.OwnedStars.Values)
        {
            if (ownStar.ScanRange > 0)
            {
                primaryScanSources.Add(new MapCircle(ownStar.Position.X + edgeMargin, ownStar.Position.Y + edgeMargin, ownStar.ScanRange));
            }
        }

        foreach (Fleet ownFleet in empire.OwnedFleets.Values)
        {
            if (ownFleet.ScanRange > 0)
            {
                primaryScanSources.Add(new MapCircle(ownFleet.Position.X + edgeMargin, ownFleet.Position.Y + edgeMargin, ownFleet.ScanRange));
            }

            if (ownFleet.PenScanRange > 0)
            {
                penetratingScanSources.Add(new MapCircle(ownFleet.Position.X + edgeMargin, ownFleet.Position.Y + edgeMargin, ownFleet.PenScanRange));
            }
        }

        // Minefields - visibility already computed above.
        var minefields = new List<StarMapMineFieldViewModel>();
        IBrush ownMineColor = new SolidColorBrush(Color.FromArgb(128, 0, 128, 0));
        IBrush enemyMineColor = new SolidColorBrush(Color.FromArgb(128, 128, 0, 128));

        foreach ((Minefield minefield, bool isOwn) in visibleMinefields)
        {
            var marker = new StarMapMineFieldViewModel(minefield.Name, minefield.Position.X + edgeMargin, minefield.Position.Y + edgeMargin, minefield.Radius, isOwn ? ownMineColor : enemyMineColor, minefield, selection)
            {
                ToolTipText = MapObjectText.Identify(MapObjectKind.Minefield, minefield.Name, OwnershipOf(minefield.Owner), OwnerName(minefield.Owner)),
            };
            minefields.Add(marker);
            mapObjects.Add(new MapObjectEntry(minefield, minefield.Name, MapObjectKind.Minefield, minefield.Position.X + edgeMargin, minefield.Position.Y + edgeMargin));
        }

        // Wormholes this empire has detected (EmpireData.WormholeReports), at their last-seen
        // position.
        var wormholes = new List<StarMapWormholeViewModel>();
        IBrush wormholeColor = new SolidColorBrush(Color.FromRgb(200, 120, 255));
        foreach (WormholeIntel wormhole in empire.WormholeReports.Values)
        {
            string name = string.IsNullOrEmpty(wormhole.Name) ? MapObjectText.CategoryName(MapObjectKind.Wormhole) : wormhole.Name;
            var marker = new StarMapWormholeViewModel(name, wormhole.Position.X + edgeMargin, wormhole.Position.Y + edgeMargin, wormholeColor, wormhole, selection)
            {
                ToolTipText = MapObjectText.Identify(MapObjectKind.Wormhole, name, MapOwnership.Unowned, null) + Environment.NewLine + "Last seen in " + wormhole.Year,
            };
            wormholes.Add(marker);
            mapObjects.Add(new MapObjectEntry(wormhole, name, MapObjectKind.Wormhole, marker.X, marker.Y));
        }

        // Mineral packets in flight this empire sees this year (EmpireData.MineralPacketReports).
        var packets = new List<StarMapPacketViewModel>();
        foreach (MineralPacket packet in empire.MineralPacketReports.Values)
        {
            string name = string.IsNullOrEmpty(packet.Name) ? MapObjectText.CategoryName(MapObjectKind.Packet) : packet.Name;
            IBrush packetColor = packet.Owner == empire.Id ? Brushes.GreenYellow : Brushes.OrangeRed;
            string detail = packet.TotalKilotons + " kT"
                + (string.IsNullOrEmpty(packet.TargetName) ? string.Empty : " to " + packet.TargetName)
                + ", warp " + packet.Warp;
            var marker = new StarMapPacketViewModel(name, packet.Position.X + edgeMargin, packet.Position.Y + edgeMargin, packetColor, packet, selection)
            {
                ToolTipText = MapObjectText.Identify(MapObjectKind.Packet, name, OwnershipOf(packet.Owner), OwnerName(packet.Owner)) + Environment.NewLine + detail,
            };
            packets.Add(marker);
            mapObjects.Add(new MapObjectEntry(packet, name, MapObjectKind.Packet, marker.X, marker.Y));
        }

        Stars = stars;
        Fleets = fleets;
        Minefields = minefields;
        Wormholes = wormholes;
        Packets = packets;
        markers.AddRange(stars);
        markers.AddRange(fleets);
        markers.AddRange(minefields);
        markers.AddRange(wormholes);
        markers.AddRange(packets);

        ApplyViewOptions();

        selection.PropertyChanged += OnSelectionChanged;
        SyncSelection();
    }

    private MapOwnership OwnershipOf(ushort owner)
    {
        if (owner == clientState.EmpireState.Id)
        {
            return MapOwnership.Own;
        }

        return owner == Global.Nobody ? MapOwnership.Unowned : MapOwnership.Other;
    }

    private string? OwnerName(ushort owner)
    {
        if (owner == clientState.EmpireState.Id)
        {
            return clientState.EmpireState.Race?.Name;
        }

        return clientState.EmpireState.EmpireReports.TryGetValue(owner, out EmpireIntel? intel) ? intel.RaceName : null;
    }

    /// <summary>Pushes the current view options into every marker and rebuilds the overlays that
    /// depend on them.</summary>
    private void ApplyViewOptions()
    {
        PlanetOverlayKind overlay = ViewOptions.Overlay;
        foreach (StarMapStarViewModel star in Stars)
        {
            star.Overlay = overlay;
            star.ShowName = ViewOptions.ShowPlanetNames;
        }

        foreach (StarMapFleetViewModel fleet in Fleets)
        {
            fleet.ShowBadge = ViewOptions.ShowShipCountBadges;
        }

        ScanCircles = BuildScanCircles();
        RouteLegs = BuildRouteLegs(selection.Selected as Fleet);

        OnPropertyChanged(nameof(PlanetMode));
        OnPropertyChanged(nameof(ShowScanCircles));
        OnPropertyChanged(nameof(ShowMinefields));
        OnPropertyChanged(nameof(ShowRouteOverlap));
        OnPropertyChanged(nameof(ShowPlanetNames));
        OnPropertyChanged(nameof(ShowShipCountBadges));
        OnPropertyChanged(nameof(ScannerPercentage));
    }

    /// <summary>
    /// The scan washes: each true radius passes the scanner-percentage correction
    /// (ScanCircleRules.DisplayRadius), then any circle fully nested inside an earlier circle of
    /// the same kind is culled (ScanCircleRules.CullNested). Primary and penetrating circles are
    /// culled separately: they are drawn in different colours, and the spec says the culling
    /// "does not otherwise change what is visible".
    /// </summary>
    private IReadOnlyList<StarMapScanCircleViewModel> BuildScanCircles()
    {
        if (!ViewOptions.ShowScanCircles)
        {
            return Array.Empty<StarMapScanCircleViewModel>();
        }

        var circles = new List<StarMapScanCircleViewModel>();
        AddScanCircles(circles, primaryScanSources, LongRangeScanBrush);
        AddScanCircles(circles, penetratingScanSources, PenScanBrush);
        return circles;
    }

    private void AddScanCircles(List<StarMapScanCircleViewModel> circles, List<MapCircle> sources, IBrush fill)
    {
        int percentage = ViewOptions.ScannerPercentage;
        List<MapCircle> scaled = sources
            .Select(source => new MapCircle(source.X, source.Y, ScanCircleRules.DisplayRadius((int)source.Radius, percentage)))
            .ToList();

        foreach (int index in ScanCircleRules.CullNested(scaled))
        {
            circles.Add(new StarMapScanCircleViewModel(scaled[index].X, scaled[index].Y, scaled[index].Radius, fill));
        }
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

    /// <summary>Selects an object the same way a marker tap does: an armed waypoint/measure
    /// gesture consumes it first, otherwise it becomes the selection.</summary>
    public void SelectObject(object item)
    {
        if (!selection.TryConsumeWaypointTarget(item) && !selection.TryConsumeMeasureTarget(item))
        {
            selection.Selected = item;
        }
    }

    /// <summary>
    /// Shift+left-click with one of this empire's planets selected (fleet-movement-scanning-
    /// cargo.md: it "sets the planet's packet destination, but only when its starbase has a mass
    /// driver ... planets only, with no distance limit, and clicking the planet itself clears the
    /// setting"). Panel point (x, y) picks the nearest known planet (Nova.Client.PacketOrders).
    /// False - and nothing happens - when the selection is not such a planet, so the click is
    /// handled as an ordinary one.
    /// </summary>
    public bool TrySetPacketDestinationAt(double x, double y)
    {
        if (selection.Selected is not Star star || !PacketOrders.CanSetDestination(star))
        {
            return false;
        }

        string? target = PacketOrders.NearestPlanet(clientState.EmpireState.StarReports.Values, x - edgeMargin, y - edgeMargin);
        if (target == null)
        {
            return false;
        }

        PacketOrders.Issue(clientState, PacketOrders.DestinationOrder(star, target, star.PacketWarp));
        selection.NotifyMutated();
        return true;
    }

    /// <summary>The right-click object-disambiguation picker's lines for panel point (x, y) -
    /// every fleet there, then every planet there, the current selection checked (see
    /// Nova.Client.Map.MapObjectPicker). Picking a line only selects that object.</summary>
    public IReadOnlyList<MapPickerLine> BuildPickerLines(double x, double y)
    {
        return MapObjectPicker.Build(mapObjects, x, y, MarkerHitRadius, selection.Selected);
    }

    private void FocusEntry(MapObjectEntry entry)
    {
        SelectObject(entry.Item);
        CenterOnRequested?.Invoke(entry.X, entry.Y);
    }

    // Both star and fleet markers share the identical fixed 32x32 (16px-radius) tap target -
    // see StarMapDocumentView.axaml's own comment on each - and fleets are declared after (so
    // drawn on top of) stars in the same Panel, so whenever a fleet sits within that radius of
    // its star, the fleet's Button always won a tap regardless of which one the tap point was
    // actually closer to. This resolves the tie properly - among every star/fleet marker whose own
    // 16px hit-radius contains the tap, whichever CENTER is nearest to the actual tap point wins -
    // called from the map's own Tunnel-phase pointer handler (StarMapDocumentView.axaml.cs), which
    // fires before either marker's Button gets a chance to react on its own. Deliberately
    // excludes minefields: their own hit area is the field's real (and often much larger) radius,
    // already works correctly via its own Button, and sits behind both marker kinds in z-order.
    //
    // A genuine tie (a fleet sitting exactly on its own star) still needs a tiebreaker - unarmed
    // (plain browsing) the star wins; armed (see SelectionService.IsAddingWaypoint/
    // IsMeasuringDistance) a FLEET wins instead, since arming that gesture means the user is very
    // likely aiming for a particular fleet.
    private const double MarkerHitRadius = 16.0;

    // Tracks the full set of candidates the PREVIOUS tap resolved against, and which of them was
    // returned - see FindNearestStarOrFleetMarker's own comment for why. Reset (via SameCandidates
    // failing to match) the moment a tap lands somewhere with a genuinely different set of markers
    // in range, so cycling never persists across an unrelated tap elsewhere on the map.
    private List<MapMarkerViewModel>? lastTapCandidates;
    private int lastTapIndex;

    /// <summary>
    /// Repeatedly tapping the SAME spot cycles through every candidate within range instead of
    /// re-resolving to the same winner every time (client-interface.md: "clicking the same
    /// already-selected position repeatedly cycles through a stack of co-located objects") -
    /// tapping elsewhere (a different candidate set) starts the cycle over from the same
    /// precedence order as before.
    /// </summary>
    public MapMarkerViewModel? FindNearestStarOrFleetMarker(double x, double y)
    {
        bool preferFleets = selection.IsAddingWaypoint || selection.IsMeasuringDistance;
        IEnumerable<MapMarkerViewModel> candidates = preferFleets
            ? Fleets.Cast<MapMarkerViewModel>().Concat(Stars).Concat(Wormholes).Concat(Packets)
            : Stars.Cast<MapMarkerViewModel>().Concat(Fleets).Concat(Wormholes).Concat(Packets);

        // Every marker within range, nearest first - ties keep their relative order from
        // `candidates` (OrderBy is stable).
        List<MapMarkerViewModel> inRange = candidates
            .Select(marker => (marker, distanceSquared: ((marker.X - x) * (marker.X - x)) + ((marker.Y - y) * (marker.Y - y))))
            .Where(t => t.distanceSquared <= MarkerHitRadius * MarkerHitRadius)
            .OrderBy(t => t.distanceSquared)
            .Select(t => t.marker)
            .ToList();

        if (inRange.Count == 0)
        {
            lastTapCandidates = null;
            return null;
        }

        if (lastTapCandidates != null && SameCandidates(lastTapCandidates, inRange))
        {
            lastTapIndex = (lastTapIndex + 1) % inRange.Count;
        }
        else
        {
            lastTapIndex = 0;
        }

        lastTapCandidates = inRange;
        return inRange[lastTapIndex];
    }

    /// <summary>Order-independent same-object-set comparison - a repeated tap doesn't land at the
    /// exact same pixel every time, so cycling is keyed on "the same markers are in range" rather
    /// than raw coordinate equality.</summary>
    private static bool SameCandidates(List<MapMarkerViewModel> a, List<MapMarkerViewModel> b)
    {
        return a.Count == b.Count && a.All(b.Contains);
    }

    private IReadOnlyList<StarMapRouteLegViewModel> BuildRouteLegs(Fleet? selectedFleet)
    {
        if (selectedFleet == null || selectedFleet.Waypoints.Count < 2)
        {
            return Array.Empty<StarMapRouteLegViewModel>();
        }

        List<NovaPoint> points = selectedFleet.Waypoints.Select(waypoint => waypoint.Position).ToList();
        bool[] dashed = ViewOptions.ShowRouteOverlap ? RouteOverlap.DashedLegs(points) : new bool[points.Count - 1];

        var legs = new List<StarMapRouteLegViewModel>();
        for (int i = 1; i < points.Count; i++)
        {
            NovaPoint from = points[i - 1];
            NovaPoint to = points[i];
            bool isFirstLeg = i == 1;
            bool isFinalLeg = i == points.Count - 1;

            legs.Add(new StarMapRouteLegViewModel(from.X + edgeMargin, from.Y + edgeMargin, to.X + edgeMargin, to.Y + edgeMargin, isFirstLeg, isFinalLeg, dashed[i - 1]));
        }

        return legs;
    }

    /// <summary>How far a star's name label can reach past its own center - half of the label's
    /// estimated total width, since StarMapDocumentView.axaml centers it on that point.</summary>
    private static double EstimateNameHalfWidth(string name)
    {
        return string.IsNullOrEmpty(name) ? 0 : name.Length * ApproxCharWidthAtFontSize10 / 2.0;
    }
}
