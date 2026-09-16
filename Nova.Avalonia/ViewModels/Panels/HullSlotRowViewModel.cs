using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One slot in the selected hull - mirrors a single cell in the WinForms HullGrid, including its
/// actual grid position (Column/Row, from HullModule.CellNumber - see components.xml, which
/// really does define a real 5-wide/5-tall layout per hull, not just a flat list) so the hull is
/// laid out the same shape the original client's own 5x5 slot grid used. Directly wraps and
/// mutates a real (already-cloned-from-the-master-hull) HullModule so the parent's live stat
/// preview can just read Hull.Modules straight back off it.
///
/// Placement itself (TryPlace/Clear) is driven by ShipDesignViewModel, not a ComboBox here
/// anymore - see that class's own comment on why (tap-to-arm-then-tap-to-place, plus real
/// drag-and-drop on desktop, both ending up here). SelectedOption/Quantity are still kept as
/// plain settable properties since SelectExisting (reflecting an already-saved design's
/// allocation back into a freshly rebuilt row) still uses them directly.
/// </summary>
public class HullSlotRowViewModel : ViewModelBase
{
    private readonly Action onChanged;

    public HullModule Module { get; }

    public string Label { get; }

    public int Maximum => Module.ComponentMaximum;

    /// <summary>Position in the hull's 5-wide slot grid - see components.xml's per-hull
    /// CellNumber (0-24, row-major) for each Module, exactly mirroring HullGrid's own
    /// grid0..grid24 WinForms panel layout.</summary>
    public int Column => Module.CellNumber % 5;

    public int Row => Module.CellNumber / 5;

    public IReadOnlyList<ComponentOptionViewModel> Options { get; }

    private ComponentOptionViewModel? selectedOption;

    public ComponentOptionViewModel? SelectedOption
    {
        get => selectedOption;
        set
        {
            if (SetProperty(ref selectedOption, value))
            {
                Module.AllocatedComponent = value?.Component;
                OnPropertyChanged(nameof(IsFilled));
                OnPropertyChanged(nameof(Icon));
                // Default to filling the slot when a component is first picked - the user can
                // still dial it back with the Quantity control afterward.
                Quantity = value?.Component == null ? 0 : Module.ComponentMaximum;
                onChanged();
            }
        }
    }

    private int quantity;

    public int Quantity
    {
        get => quantity;
        set
        {
            int clamped = Math.Clamp(value, 0, Module.ComponentMaximum);
            if (SetProperty(ref quantity, clamped))
            {
                Module.ComponentCount = clamped;
                onChanged();
                IncrementCommand.NotifyCanExecuteChanged();
                DecrementCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsFilled => Module.AllocatedComponent != null;

    /// <summary>See ComponentListItemViewModel.Icon's own comment - the allocated component's
    /// Bitmap, straight off the live HullModule rather than SelectedOption, matching IsFilled's
    /// own source of truth. Raised alongside IsFilled everywhere that changes.</summary>
    public object? Icon => Module.AllocatedComponent?.ComponentImage;

    private bool isCompatibleWithArmed;

    /// <summary>Set by ShipDesignViewModel whenever the armed component changes - true when the
    /// currently-armed list item would actually fit here, for a highlight showing the player
    /// where it's droppable/tappable before they commit to a slot (the original's own drag-enter
    /// compatibility check, but shown proactively rather than only during an active drag).</summary>
    public bool IsCompatibleWithArmed
    {
        get => isCompatibleWithArmed;
        set => SetProperty(ref isCompatibleWithArmed, value);
    }

    public IRelayCommand IncrementCommand { get; }

    public IRelayCommand DecrementCommand { get; }

    public HullSlotRowViewModel(HullModule module, IReadOnlyList<ComponentOptionViewModel> options, Action onChanged)
    {
        Module = module;
        Label = $"{module.ComponentType} (max {module.ComponentMaximum})";
        Options = options;
        this.onChanged = onChanged;

        IncrementCommand = new RelayCommand(() => Quantity++, () => IsFilled && Quantity < Maximum);
        DecrementCommand = new RelayCommand(() => Quantity--, () => IsFilled && Quantity > 0);
    }

    /// <summary>
    /// Places (or adds to) a component in this slot, following the same rules HullGrid's own
    /// Grid_DragDrop uses: dropping a component already here just adds count more (capped at
    /// Maximum); dropping a different component replaces whatever was here and starts fresh at
    /// count. Returns false without changing anything if this slot doesn't accept that
    /// component at all (Options - built from ShipDesignViewModel.IsCompatible - won't contain
    /// it), the same rejection HullGrid's Grid_DragEnter signals by refusing the drop cursor.
    /// </summary>
    public bool TryPlace(Component component, int count)
    {
        ComponentOptionViewModel? match = Options.FirstOrDefault(o => o.Component == component);
        if (match == null)
        {
            return false;
        }

        if (!ReferenceEquals(Module.AllocatedComponent, component))
        {
            selectedOption = match;
            Module.AllocatedComponent = component;
            quantity = 0;
            OnPropertyChanged(nameof(SelectedOption));
            OnPropertyChanged(nameof(IsFilled));
            OnPropertyChanged(nameof(Icon));
        }

        Quantity = Math.Min(Maximum, quantity + count);
        return true;
    }

    /// <summary>Empties this slot - the tap-with-nothing-armed gesture's effect (see
    /// ShipDesignViewModel.OnSlotTapped), replacing the ComboBox's old "(Empty)" option pick.</summary>
    public void Clear()
    {
        selectedOption = Options.Count > 0 ? Options[0] : null;
        Module.AllocatedComponent = null;
        OnPropertyChanged(nameof(SelectedOption));
        OnPropertyChanged(nameof(IsFilled));
        OnPropertyChanged(nameof(Icon));
        Quantity = 0;
    }

    /// <summary>
    /// Reflects an already-allocated component (from the cloned HullModule) into the picker
    /// without going through the SelectedOption setter - that setter resets Quantity and calls
    /// onChanged, which would discard the module's own pre-existing ComponentCount.
    /// </summary>
    public void SelectExisting(Component? allocatedComponent)
    {
        if (allocatedComponent == null)
        {
            return;
        }

        foreach (ComponentOptionViewModel option in Options)
        {
            if (option.Component == allocatedComponent)
            {
                selectedOption = option;
                OnPropertyChanged(nameof(SelectedOption));
                OnPropertyChanged(nameof(IsFilled));
                OnPropertyChanged(nameof(Icon));
                quantity = Module.ComponentCount;
                OnPropertyChanged(nameof(Quantity));
                return;
            }
        }
    }
}
