using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Base for anything drawn on the Star Map at a position and clickable to select (a star or
/// a fleet). Clicking publishes <see cref="Selectable"/> to the shared
/// <see cref="SelectionService"/>, the same mediator the Navigator uses, so Inspector/
/// Production react the same way regardless of whether the selection came from a list or the
/// map; <see cref="IsSelected"/> is kept in sync the other way by
/// <see cref="StarMapDocumentViewModel"/> so the map highlights whatever's currently selected
/// no matter where the selection came from.
/// </summary>
public abstract class MapMarkerViewModel : ViewModelBase
{
    public string Name { get; }
    public double X { get; }
    public double Y { get; }
    public IBrush Color { get; }

    /// <summary>
    /// What clicking this marker publishes: the owning empire's own <c>Star</c>/<c>Fleet</c>
    /// if it owns the thing at this position (full detail, same as picking it in the
    /// Navigator), or the <c>StarIntel</c>/<c>FleetIntel</c> report otherwise (only what's
    /// actually known about it).
    /// </summary>
    public object Selectable { get; }

    public IRelayCommand SelectCommand { get; }

    private bool isSelected;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                OnIsSelectedChanged();
            }
        }
    }

    /// <summary>Hook for markers whose look depends on being the tracked (selected) object -
    /// the fleet chevron and the enlarged fleet-in-orbit ring.</summary>
    protected virtual void OnIsSelectedChanged()
    {
    }

    private string? toolTipText;

    /// <summary>Hover text (desktop) - the planet tooltip or the shared object-identification
    /// phrase, built by Nova.Client.Map.MapObjectText. Null shows no tooltip.</summary>
    public string? ToolTipText
    {
        get => toolTipText;
        set => SetProperty(ref toolTipText, value);
    }

    protected MapMarkerViewModel(string name, double x, double y, IBrush color, object selectable, SelectionService selection)
    {
        Name = name;
        X = x;
        Y = y;
        Color = color;
        Selectable = selectable;
        // A tap while "add waypoint" or "measure distance" mode is armed (see
        // SelectionService.ArmWaypointTarget/ArmMeasureTarget) is consumed by whichever one's
        // active instead of changing the selection - keeps the fleet whose orders are being
        // edited (or whatever the distance is being measured from) selected throughout the
        // arm-then-tap gesture. The two are mutually exclusive (arming one cancels the other),
        // so at most one of these ever actually consumes a given tap.
        SelectCommand = new RelayCommand(() =>
        {
            if (!selection.TryConsumeWaypointTarget(selectable) && !selection.TryConsumeMeasureTarget(selectable))
            {
                selection.Selected = selectable;
            }
        });
    }
}
