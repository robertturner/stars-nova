using CommunityToolkit.Mvvm.Input;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Production panel's queue: a pending build order for the selected planet,
/// editable via the same +1/-1-quantity and delete actions as the WinForms QueueList (which
/// uses Shift/Ctrl-click for x10/x100 instead of separate buttons - a NumericUpDown quantity
/// on the "add" form covers that case here instead, so these per-row buttons just need to
/// nudge or remove an already-queued order).
/// </summary>
public class ProductionItemViewModel
{
    /// <summary>
    /// This row's position in the parent's Queue - needed so the View can keep adjusting the
    /// right order by index while a press-and-hold repeat is in progress, since every tick
    /// rebuilds the whole Queue (and therefore this row itself) from scratch.
    /// </summary>
    public int Index { get; }

    public string Name { get; }

    public int Quantity { get; }

    public bool IsAutoBuild { get; }

    public string CostSummary { get; }

    public IRelayCommand IncrementCommand { get; }

    public IRelayCommand DecrementCommand { get; }

    public IRelayCommand DeleteCommand { get; }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }

    public ProductionItemViewModel(
        int index,
        ProductionOrder order,
        System.Action onIncrement,
        System.Action onDecrement,
        System.Action onDelete,
        System.Action? onMoveUp,
        System.Action? onMoveDown)
    {
        Index = index;
        Name = order.Name;
        Quantity = order.Quantity;
        IsAutoBuild = order.IsAutoBuild;
        CostSummary = ResourceFormat.Cost(order.Unit.Cost);
        IncrementCommand = new RelayCommand(onIncrement);
        DecrementCommand = new RelayCommand(onDecrement);
        DeleteCommand = new RelayCommand(onDelete);
        MoveUpCommand = new RelayCommand(onMoveUp ?? (() => { }), () => onMoveUp != null);
        MoveDownCommand = new RelayCommand(onMoveDown ?? (() => { }), () => onMoveDown != null);
    }
}
