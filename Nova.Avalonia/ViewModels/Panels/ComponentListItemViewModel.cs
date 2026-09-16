using CommunityToolkit.Mvvm.Input;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One entry in the Ship Designer's scrollable component list (see ShipDesignViewModel's own
/// comment) - mirrors one item in ShipDesignDialog.cs's ListView. Tapping/clicking it "arms" it
/// as the component the next hull-slot tap will place (see ShipDesignViewModel.ArmComponent) -
/// the touch-friendly equivalent of picking up the item to drag, since a real OS drag gesture
/// doesn't survive well onto a touchscreen (dragging vs. scrolling the same list is ambiguous on
/// touch). Desktop additionally supports a real drag straight from this item onto a hull slot -
/// see ShipDesignView.axaml.cs - both paths end up calling the same
/// ShipDesignViewModel.PlaceArmedComponent/HullSlotRowViewModel.TryPlace logic.
/// </summary>
public class ComponentListItemViewModel : ViewModelBase
{
    public Component Component { get; }

    public string Name => Component.Name;

    public string CostSummary { get; }

    /// <summary>The component's icon - an Avalonia Bitmap already loaded by
    /// PlatformHooks.LoadImage when components.xml was parsed (see Component.ComponentImage),
    /// null only if that component has no <Image> entry or the file failed to load. Typed
    /// `object` here (matching Component.ComponentImage itself) rather than a hard Avalonia.Media
    /// dependency in Common - Avalonia's own binding engine accepts it directly since the boxed
    /// value really is a Bitmap (IImage), the same pattern RaceIcon.Image already uses.</summary>
    public object? Icon => Component.ComponentImage;

    private bool isArmed;

    public bool IsArmed
    {
        get => isArmed;
        set => SetProperty(ref isArmed, value);
    }

    public IRelayCommand SelectCommand { get; }

    public ComponentListItemViewModel(Component component, System.Action<ComponentListItemViewModel> onSelect)
    {
        Component = component;
        CostSummary = ResourceFormat.Cost(component.Cost);
        SelectCommand = new RelayCommand(() => onSelect(this));
    }
}
