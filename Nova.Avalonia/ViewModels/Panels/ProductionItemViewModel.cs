using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Production panel's queue: a pending build order for the selected planet,
/// editable via +/- RepeatButtons and a delete button, instead of the WinForms QueueList's
/// Shift/Ctrl-click x10/x100 - see ProductionViewModel.NextStep's own comment for how the +/-
/// step size ramps up instead.
///
/// An ObservableObject with an <see cref="Update"/> method, not a bag of get-only properties
/// rebuilt fresh every time - ProductionViewModel.RebuildQueueRows updates an existing row's
/// properties in place whenever the queue's own shape (item count) hasn't changed, rather than
/// replacing it (and therefore the ItemsControl's container for it) with a brand new instance.
/// That distinction turned out to matter for real, live-reproduced reasons: a RepeatButton held
/// down keeps re-invoking its Command every tick, but each tick previously rebuilt the ENTIRE
/// Queue list from scratch (see the old comment this replaced) - which made the ItemsControl
/// discard and recreate every row's containers, including the very RepeatButton mid-hold. Losing
/// its container mid-gesture ends the hold outright, which is exactly why holding "+" only ever
/// managed a single +1 no matter how long the press lasted. Reusing the same row objects (and
/// therefore the same Button/RepeatButton instances) keeps the gesture alive across every tick.
/// </summary>
public class ProductionItemViewModel : ViewModelBase
{
    private string name = "";

    public string Name
    {
        get => name;
        private set => SetProperty(ref name, value);
    }

    private int quantity;

    public int Quantity
    {
        get => quantity;
        private set => SetProperty(ref quantity, value);
    }

    private bool isAutoBuild;

    public bool IsAutoBuild
    {
        get => isAutoBuild;
        private set
        {
            if (SetProperty(ref isAutoBuild, value))
            {
                OnPropertyChanged(nameof(AutoBuildLabel));
            }
        }
    }

    /// <summary>Label for the per-row Auto Build toggle button - see docs/behavior-specs-4/
    /// production-queue.md §9's "Factories (Auto Build) Up to N" phrasing, which this mirrors
    /// (an auto-build order never blocks the queue and is simply skipped in a year it can't be
    /// afforded - see ProductionOrder.IsBlocking - unlike an ordinary manual order).</summary>
    public string AutoBuildLabel => IsAutoBuild ? "Auto" : "Manual";

    private string costSummary = "";

    public string CostSummary
    {
        get => costSummary;
        private set => SetProperty(ref costSummary, value);
    }

    private double percentComplete;

    /// <summary>0-100: how much of the next single unit's cost has already been paid - see
    /// ProductionCompletionEstimator's own comment on why this is Energy-based.</summary>
    public double PercentComplete
    {
        get => percentComplete;
        private set
        {
            if (SetProperty(ref percentComplete, value))
            {
                OnPropertyChanged(nameof(PercentCompleteDisplay));
                OnPropertyChanged(nameof(ProgressSummary));
            }
        }
    }

    public string PercentCompleteDisplay => $"{PercentComplete:0}%";

    private int yearsToFinish;

    /// <summary>Estimated years until this line next completes a unit (100 = "practically
    /// never" - docs/behavior-specs-5/production-queue.md §8). Recomputed fresh every time the
    /// queue is rebuilt (ProductionViewModel.RebuildQueueRows), same as this row's other fields -
    /// the estimate depends on every row ahead of this one too, so any queue edit anywhere can
    /// change it.</summary>
    public int YearsToFinish
    {
        get => yearsToFinish;
        private set
        {
            if (SetProperty(ref yearsToFinish, value))
            {
                OnPropertyChanged(nameof(YearsToFinishDisplay));
                OnPropertyChanged(nameof(ProgressSummary));
            }
        }
    }

    public string YearsToFinishDisplay => YearsToFinish >= 100 ? "100+ yrs" : $"{YearsToFinish} yr{(YearsToFinish == 1 ? "" : "s")}";

    public string ProgressSummary => $"{PercentCompleteDisplay} done · {YearsToFinishDisplay}";

    private IBrush textColor = Brushes.White;

    /// <summary>Text color per docs/behavior-specs-5/production-queue.md §8's confirmed scheme.
    /// Green/Blue are brightened from the doc's literal RGB (dark green 0,127,0 / dark blue
    /// 0,0,127) - those were tuned for the original client's light-colored listbox, and would be
    /// nearly invisible against this app's dark theme (every panel in this app - see any other
    /// ViewModel's own Brushes usage - renders on black, not white). Red and Gray are visible
    /// against either background, so those keep values close to the original.</summary>
    public IBrush TextColor
    {
        get => textColor;
        private set => SetProperty(ref textColor, value);
    }

    private static IBrush ToBrush(ProductionQueueColor color) => color switch
    {
        ProductionQueueColor.Green => Brushes.LimeGreen,
        ProductionQueueColor.Blue => Brushes.DodgerBlue,
        ProductionQueueColor.Red => Brushes.Red,
        ProductionQueueColor.Gray => new SolidColorBrush(Color.FromRgb(160, 160, 160)),
        _ => Brushes.White,
    };

    public IRelayCommand IncrementCommand { get; }

    public IRelayCommand DecrementCommand { get; }

    public IRelayCommand DeleteCommand { get; }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public IRelayCommand ToggleAutoBuildCommand { get; }

    public ProductionItemViewModel(
        ProductionOrder order,
        ProductionCompletionEstimate estimate,
        System.Action onIncrement,
        System.Action onDecrement,
        System.Action onDelete,
        System.Action? onMoveUp,
        System.Action? onMoveDown,
        System.Action onToggleAutoBuild)
    {
        Update(order, estimate);
        IncrementCommand = new RelayCommand(onIncrement);
        DecrementCommand = new RelayCommand(onDecrement);
        DeleteCommand = new RelayCommand(onDelete);
        MoveUpCommand = new RelayCommand(onMoveUp ?? (() => { }), () => onMoveUp != null);
        MoveDownCommand = new RelayCommand(onMoveDown ?? (() => { }), () => onMoveDown != null);
        ToggleAutoBuildCommand = new RelayCommand(onToggleAutoBuild);
    }

    /// <summary>Refreshes every displayed field from a (possibly different) order/estimate at
    /// this same queue position, without replacing this object itself - see this class's own
    /// top comment for why that distinction matters. MoveUp/MoveDown's own enabled state
    /// (whether this position has a neighbor to swap with) is intentionally NOT updated here -
    /// only ProductionViewModel.RebuildQueueRows' full-rebuild path ever needs to change that,
    /// since it only changes when the queue's length itself changes, which already forces a
    /// full rebuild anyway.</summary>
    public void Update(ProductionOrder order, ProductionCompletionEstimate estimate)
    {
        Name = order.Name;
        Quantity = order.Quantity;
        IsAutoBuild = order.IsAutoBuild;
        CostSummary = ResourceFormat.Cost(order.Unit.Cost);
        PercentComplete = estimate.PercentComplete;
        YearsToFinish = estimate.YearsToFinish;
        TextColor = ToBrush(estimate.Color);
    }
}
