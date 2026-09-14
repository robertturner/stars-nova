using System;
using System.Collections.Generic;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One slot in the selected hull - mirrors a single cell in the WinForms HullGrid, but as a
/// plain component-picker + quantity row instead of a drag-and-drop pixel grid (the
/// compatibility/assignment rules HullGrid enforces are plain data logic on HullModule/
/// Component, not graphics - see ShipDesignViewModel.IsCompatible). Directly wraps and
/// mutates a real (already-cloned-from-the-master-hull) HullModule so the parent's live stat
/// preview can just read Hull.Modules straight back off it.
/// </summary>
public class HullSlotRowViewModel : ViewModelBase
{
    private readonly Action onChanged;

    public HullModule Module { get; }

    public string Label { get; }

    public int Maximum => Module.ComponentMaximum;

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
            }
        }
    }

    public HullSlotRowViewModel(HullModule module, IReadOnlyList<ComponentOptionViewModel> options, Action onChanged)
    {
        Module = module;
        Label = $"{module.ComponentType} (max {module.ComponentMaximum})";
        Options = options;
        this.onChanged = onChanged;
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
                quantity = Module.ComponentCount;
                OnPropertyChanged(nameof(Quantity));
                return;
            }
        }
    }
}
