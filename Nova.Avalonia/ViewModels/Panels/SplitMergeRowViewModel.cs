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

    /// <summary>This design's own dry mass (no cargo - see ShipDesign.Mass's own comment) in kT -
    /// shown here because it's exactly the figure a Stargate jump checks per ship against both
    /// gates' SafeHullMass rating (TurnGenerator.TryStargateJump), so it's what actually matters
    /// when deciding whether a fleet can gate safely before sending it through one.</summary>
    public string MassLabel => $"{Design.Mass}kT";

    /// <summary>See ComponentListItemViewModel.Icon's own comment - same Bitmap-via-object
    /// pattern, this time the design's own player-chosen ShipIcon rather than a raw component's
    /// image. Also used by the plain (non-slider) "Ships" composition list this same row list
    /// backs - see InspectorView.axaml.</summary>
    public object? Icon => Design.Icon?.Image;

    public int OriginalQuantity { get; }

    /// <summary>Percentage of this token's total armor already lost (ShipToken.Damage) - blank
    /// for an undamaged token rather than "0% damaged", so the common case stays quiet. Confirmed
    /// live as a real gap: nothing in this port's Inspector showed a fleet's own current damage
    /// state at all - the underlying data (Composition/Armor) was already tracked correctly
    /// throughout (battle resolution, merges, saves), only the display was missing. Shown here
    /// (the plain "Ships" composition list this same row list backs - see InspectorView.axaml)
    /// rather than as a separate Overview row, so it sits right next to the design it describes
    /// instead of duplicating this same list a second time.</summary>
    public string DamageLabel { get; }

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

    public SplitMergeRowViewModel(long compositionKey, ShipDesign design, int originalQuantity, double damagePercent = 0)
    {
        CompositionKey = compositionKey;
        Design = design;
        OriginalQuantity = originalQuantity;
        keepInSource = originalQuantity; // default: nothing moves until the user adjusts a row
        DamageLabel = damagePercent > 0.05 ? $"{damagePercent:0}% damaged" : "";
    }
}
