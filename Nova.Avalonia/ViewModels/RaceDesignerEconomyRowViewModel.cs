using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// One of the economic stage's seven stepper rows (behavior-specs-10/race-designer-ui-and-
/// availability.md, "Economic-settings stage"): a value stored in one race slot, a pair of step
/// buttons (1 per click, 3 with Shift, auto-repeat while held - RaceDesignerStepper), every step
/// through the slot's clamp (RaceDesignerRules.Step), and only this row redrawn. For an Alternate
/// Reality race rows 2-7 are disabled and row 1 shows AR's raw divisor (no "00" suffix).
/// </summary>
public class RaceDesignerEconomyRowViewModel : ObservableObject
{
    private readonly int slot;
    private readonly string label;
    private readonly Func<Race> race;
    private readonly Func<bool> editable;
    private readonly Action onChanged;

    public RaceDesignerEconomyRowViewModel(int slot, string label, Func<Race> race, Func<bool> editable, Action onChanged)
    {
        this.slot = slot;
        this.label = label;
        this.race = race;
        this.editable = editable;
        this.onChanged = onChanged;
    }

    /// <summary>
    /// The row's sentence. Row 1 of an Alternate Reality race states AR's income rule instead
    /// (resources = planet value x square root of population x Energy tech / this value); the
    /// wording is Nova's, restating the spec's rule.
    /// </summary>
    public string Label => slot == 0 && RaceDesignerRules.IsAlternateReality(race())
        ? "Alternate Reality income: planet value x square root of (population x Energy tech) divided by"
        : label;

    public int SlotValue => RaceDesignerRules.GetSlot(race(), slot);

    /// <summary>Row 1 is shown x 100 colonists ("00" suffix) except for AR, row 5 with "kT".</summary>
    public string DisplayValue
    {
        get
        {
            int value = SlotValue;
            if (slot == 0)
            {
                return RaceDesignerRules.IsAlternateReality(race())
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : (value * 100).ToString("N0", CultureInfo.InvariantCulture);
            }

            return slot == 4 ? value.ToString(CultureInfo.InvariantCulture) + " kT" : value.ToString(CultureInfo.InvariantCulture);
        }
    }

    public bool IsEnabled => RaceDesignerRules.IsEconomySlotEditable(race(), slot, editable());

    /// <summary>One step click: +1 / -1, or 3 with Shift held.</summary>
    public void Step(int direction, bool shiftHeld)
    {
        if (!IsEnabled)
        {
            return;
        }

        int current = SlotValue;
        int next = RaceDesignerRules.Step(slot, current, direction, shiftHeld);
        if (next != current)
        {
            RaceDesignerRules.SetSlot(race(), slot, next);
            OnPropertyChanged(nameof(SlotValue));
            OnPropertyChanged(nameof(DisplayValue));
            onChanged();
        }
    }

    /// <summary>Re-reads the row after the draft changed underneath (a PRT change, a preset, a revert).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(SlotValue));
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(IsEnabled));
    }
}
