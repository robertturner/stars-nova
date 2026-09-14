using System;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One ship-design row in the Split/Merge editor - a slider from 0 to
/// <see cref="OriginalQuantity"/> for how many of this design stay in the fleet being split
/// (the rest go to whatever's picked as the other side); mirrors SplitFleetsDialog's paired
/// NumericUpDowns, one design per row, but as a single slider per row instead of two synced
/// controls since "how many stay" fully determines "how many go".
/// </summary>
public class SplitMergeRowViewModel : ViewModelBase
{
    /// <summary>The key this design is stored under in Fleet.Composition.</summary>
    public long CompositionKey { get; }

    public ShipDesign Design { get; }

    public string Label => Design.Name;

    public int OriginalQuantity { get; }

    private int keepInSource;

    public int KeepInSource
    {
        get => keepInSource;
        set
        {
            int clamped = Math.Clamp(value, 0, OriginalQuantity);
            if (SetProperty(ref keepInSource, clamped))
            {
                OnPropertyChanged(nameof(OtherQuantity));
            }
        }
    }

    public int OtherQuantity => OriginalQuantity - KeepInSource;

    public SplitMergeRowViewModel(long compositionKey, ShipDesign design, int originalQuantity)
    {
        CompositionKey = compositionKey;
        Design = design;
        OriginalQuantity = originalQuantity;
        keepInSource = originalQuantity; // default: nothing moves until the user adjusts a row
    }
}
