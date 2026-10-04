using System.Collections.Generic;
using Nova.Client;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The details shown for one owned design (TODO-FEATURES #1): a single tap on a "My Designs" row
/// shows this straight away, including cargo capacity and the other summary figures the original
/// game's design screen displayed. The figure list itself is pure formatting in
/// <see cref="Nova.Client.DesignDetails"/> so every front end shares it.
/// </summary>
public sealed class DesignDetailsViewModel
{
    public DesignDetailsViewModel(ShipDesign design)
    {
        Title = design?.Name ?? "";
        Stats = DesignDetails.Build(design);
    }

    public string Title { get; }

    public IReadOnlyList<DesignStat> Stats { get; }

    public bool HasStats => Stats.Count > 0;
}
