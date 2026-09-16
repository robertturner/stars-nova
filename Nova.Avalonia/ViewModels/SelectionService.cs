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

    private Action<string>? waypointTargetCallback;

    private bool isAddingWaypoint;

    /// <summary>Whether "tap a planet on the map to add a waypoint there" mode is currently
    /// armed - the Star Map shows a banner while this is true (see StarMapDocumentViewModel),
    /// regardless of which panel actually armed it.</summary>
    public bool IsAddingWaypoint
    {
        get => isAddingWaypoint;
        private set => SetProperty(ref isAddingWaypoint, value);
    }

    /// <summary>
    /// Arms map-tap waypoint targeting: the next Star/StarIntel consumed via
    /// <see cref="TryConsumeWaypointTarget"/> (called from MapMarkerViewModel.SelectCommand
    /// before it would otherwise change <see cref="Selected"/>) is handed to onStarPicked
    /// instead - so the fleet whose orders are being edited stays selected throughout, the same
    /// way ShipDesignViewModel's tap-to-arm-then-tap-to-place keeps the hull viewport in place
    /// while a component is armed.
    /// </summary>
    public void ArmWaypointTarget(Action<string> onStarPicked)
    {
        waypointTargetCallback = onStarPicked;
        IsAddingWaypoint = true;
    }

    public void CancelWaypointTarget()
    {
        waypointTargetCallback = null;
        IsAddingWaypoint = false;
    }

    /// <summary>
    /// Called from every map marker's SelectCommand before it falls back to normal selection -
    /// only a Star or StarIntel report satisfies an armed waypoint target (matching the
    /// Inspector's own destination picker, which offers every known star by name); tapping
    /// anything else, or tapping with nothing armed, leaves selection handling to the caller.
    /// </summary>
    public bool TryConsumeWaypointTarget(object selectable)
    {
        string? starName = selectable switch
        {
            Star star => star.Name,
            StarIntel intel => intel.Name,
            _ => null,
        };

        if (waypointTargetCallback == null || starName == null)
        {
            return false;
        }

        Action<string> callback = waypointTargetCallback;
        waypointTargetCallback = null;
        IsAddingWaypoint = false;
        callback(starName);
        return true;
    }
}
