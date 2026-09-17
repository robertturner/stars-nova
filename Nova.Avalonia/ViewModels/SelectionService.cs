using System;
using Nova.Common;
using Nova.Common.DataStructures;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// Shared mediator between the Navigator, Inspector and Star Map panels: Navigator/the map set
/// <see cref="Selected"/> to whichever planet or fleet the user picked (a <c>Star</c> or
/// <c>Fleet</c> from the loaded <c>ClientData</c>), and Inspector observes it to refresh its
/// own display. Kept as a small standalone object (rather than a direct reference between
/// the two panel view models) since Dock panels are constructed independently by the
/// factory and shouldn't need to know about each other.
/// </summary>
public class SelectionService : ViewModelBase
{
    private object? selected;

    public object? Selected
    {
        get => selected;
        set => SetProperty(ref selected, value);
    }

    /// <summary>
    /// Announces that the currently-selected object's own state changed in place (e.g. a
    /// command was applied to it) without the selection itself changing - <see cref="Selected"/>
    /// only raises a change notification when the reference actually changes, so an in-place
    /// mutation needs this instead. Other panels (e.g. Navigator, whose list items aren't
    /// individually observable) can subscribe to <see cref="ViewModelBase.PropertyChanged"/> and
    /// treat a "Selected" notification with an unchanged value as "something about it changed -
    /// refresh your own display of it".
    /// </summary>
    public void NotifyMutated()
    {
        OnPropertyChanged(nameof(Selected));
    }

    private Action<Mappable, string>? waypointTargetCallback;

    private bool isAddingWaypoint;

    /// <summary>Whether "tap something on the map to add a waypoint there" mode is currently
    /// armed - the Star Map shows a banner while this is true (see StarMapDocumentViewModel),
    /// regardless of which panel actually armed it.</summary>
    public bool IsAddingWaypoint
    {
        get => isAddingWaypoint;
        private set => SetProperty(ref isAddingWaypoint, value);
    }

    /// <summary>
    /// Arms map-tap waypoint targeting: the next Mappable consumed via
    /// <see cref="TryConsumeWaypointTarget"/> (called from MapMarkerViewModel.SelectCommand
    /// before it would otherwise change <see cref="Selected"/>) is handed to onTargetPicked -
    /// along with a best-effort Destination star name (see that method's own comment) - instead
    /// of becoming the new Selected, so the fleet whose orders are being edited stays selected
    /// throughout, the same way ShipDesignViewModel's tap-to-arm-then-tap-to-place keeps the hull
    /// viewport in place while a component is armed. The raw Mappable (not just its Position) is
    /// passed through so a task like "Merge With Fleet" can recover the tapped Fleet's own Key.
    /// </summary>
    public void ArmWaypointTarget(Action<Mappable, string> onTargetPicked)
    {
        measureTargetCallback = null;
        IsMeasuringDistance = false;
        waypointTargetCallback = onTargetPicked;
        IsAddingWaypoint = true;
    }

    public void CancelWaypointTarget()
    {
        waypointTargetCallback = null;
        IsAddingWaypoint = false;
    }

    /// <summary>
    /// Called from every map marker's SelectCommand before it falls back to normal selection -
    /// any Mappable (a star, a fleet - own or another empire's - a minefield, a wormhole)
    /// satisfies an armed waypoint target, resolved to a raw Position plus a best-effort
    /// Destination star name (used for arrival/orbit bookkeeping - see TurnGenerator - and left
    /// blank for anything that isn't sitting at a known star, exactly like a plain deep-space
    /// waypoint already works everywhere else in this codebase). Tapping anything else, or
    /// tapping with nothing armed, leaves selection handling to the caller.
    /// </summary>
    public bool TryConsumeWaypointTarget(object selectable)
    {
        if (selectable is not Mappable mappable)
        {
            return false;
        }

        // A best-effort Destination star name (used for arrival/orbit bookkeeping - see
        // TurnGenerator) for anything sitting at a known star; everything else falls back to the
        // same "Space at (x, y)" label a plain deep-space waypoint already uses everywhere else
        // in this codebase (TurnGenerator.cs/StarMap.cs/FleetReport.cs) - NOT an empty string,
        // which is what this previously did. A genuinely empty Destination round-trips through a
        // save/load as an XML element with no child at all (XmlDocument doesn't preserve
        // insignificant whitespace by default), and Waypoint's own XmlNode constructor does
        // `mainNode.FirstChild.Value` with no null check - confirmed live on a real device as a
        // NullReferenceException that used to terminate the entire app on every subsequent load,
        // for every waypoint ever added by tapping a fleet in deep space (not orbiting a star), a
        // foreign fleet report, a minefield, or a wormhole as the target. A fleet in orbit is a
        // real rendezvous at that star (arriving should register as visiting it, same as a
        // star-targeted waypoint); one in deep space, or another empire's fleet report
        // (FleetIntel.InOrbit is only a bool - whether it's at some star, with no reference to
        // which one), falls back to the position-based label instead.
        string? destination = mappable switch
        {
            Star star => star.Name,
            StarIntel intel => intel.Name,
            Fleet fleet => fleet.InOrbit?.Name ?? "Space at " + fleet.Position,
            FleetIntel fleetIntel => "Space at " + fleetIntel.Position,
            Minefield minefield => "Space at " + minefield.Position,
            Wormhole wormhole => "Space at " + wormhole.Position,
            _ => null,
        };

        if (waypointTargetCallback == null || destination == null)
        {
            return false;
        }

        Action<Mappable, string> callback = waypointTargetCallback;
        waypointTargetCallback = null;
        IsAddingWaypoint = false;
        callback(mappable, destination);
        return true;
    }

    private Action<NovaPoint, string>? measureTargetCallback;

    private bool isMeasuringDistance;

    /// <summary>Whether "tap something on the map to measure the distance to it" mode is
    /// currently armed - mirrors <see cref="IsAddingWaypoint"/>, shown as its own banner on the
    /// Star Map (see StarMapDocumentViewModel) regardless of which panel armed it.</summary>
    public bool IsMeasuringDistance
    {
        get => isMeasuringDistance;
        private set => SetProperty(ref isMeasuringDistance, value);
    }

    /// <summary>
    /// Arms map-tap distance-measuring: the next Mappable consumed via
    /// <see cref="TryConsumeMeasureTarget"/> has its position/name handed to onTargetPicked
    /// instead of becoming the new <see cref="Selected"/> - same tap-to-arm-then-tap-to-pick
    /// shape as <see cref="ArmWaypointTarget"/>, and mutually exclusive with it (arming one
    /// cancels the other, since only one banner/gesture can be "live" on the map at a time).
    /// </summary>
    public void ArmMeasureTarget(Action<NovaPoint, string> onTargetPicked)
    {
        waypointTargetCallback = null;
        IsAddingWaypoint = false;
        measureTargetCallback = onTargetPicked;
        IsMeasuringDistance = true;
    }

    public void CancelMeasureTarget()
    {
        measureTargetCallback = null;
        IsMeasuringDistance = false;
    }

    /// <summary>
    /// Called from every map marker's SelectCommand, after <see cref="TryConsumeWaypointTarget"/>
    /// has already had its chance - any Mappable at all satisfies an armed measure target (unlike
    /// the waypoint case, this just needs a position and a display name, not a turn-processing-
    /// relevant star name), resolved straight from the Mappable base every one of them shares.
    /// </summary>
    public bool TryConsumeMeasureTarget(object selectable)
    {
        if (measureTargetCallback == null || selectable is not Mappable mappable)
        {
            return false;
        }

        Action<NovaPoint, string> callback = measureTargetCallback;
        measureTargetCallback = null;
        IsMeasuringDistance = false;
        callback(mappable.Position, mappable.Name);
        return true;
    }
}
