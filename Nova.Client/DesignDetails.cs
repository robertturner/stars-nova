using System;
using System.Collections.Generic;
using System.Globalization;

using Nova.Common;
using Nova.Common.Components;

namespace Nova.Client;

/// <summary>One label/value line of a design's details.</summary>
public sealed record DesignStat(string Label, string Value);

/// <summary>
/// The Ship Designer's design-summary figures (behavior-specs-11/
/// ship-design-and-components.md "the design summary" and client-ui-dialog-catalog.md "Ship
/// Designer"). Pure formatting of a <see cref="ShipDesign"/> so desktop, Android and the web
/// client show the same list; the original's exact wording is not in the repo, so the labels are
/// the port's own. Includes cargo capacity, which the port's summary previously omitted.
/// </summary>
public static class DesignDetails
{
    public static IReadOnlyList<DesignStat> Build(ShipDesign design)
    {
        var stats = new List<DesignStat>();
        if (design == null)
        {
            return stats;
        }

        stats.Add(new DesignStat("Mass", design.Mass.ToString(CultureInfo.InvariantCulture) + " kT"));
        stats.Add(new DesignStat("Cost", FormatCost(design.Cost)));
        stats.Add(new DesignStat("Cargo capacity", design.CargoCapacity.ToString(CultureInfo.InvariantCulture) + " kT"));
        stats.Add(new DesignStat("Fuel capacity", design.FuelCapacity.ToString(CultureInfo.InvariantCulture) + " mg"));
        stats.Add(new DesignStat("Armor", design.Armor.ToString(CultureInfo.InvariantCulture)));
        stats.Add(new DesignStat("Shield", design.Shield.ToString(CultureInfo.InvariantCulture)));

        if (design.DockCapacity > 0)
        {
            stats.Add(new DesignStat("Dock capacity", design.DockCapacity.ToString(CultureInfo.InvariantCulture) + " kT"));
        }

        if (design.Engine != null)
        {
            stats.Add(new DesignStat("Free warp speed", "Warp " + design.FreeWarpSpeed.ToString(CultureInfo.InvariantCulture)));
        }

        stats.Add(new DesignStat("Initiative", design.Initiative.ToString(CultureInfo.InvariantCulture)));
        stats.Add(new DesignStat("Battle speed", design.BattleSpeed.ToString("0.#", CultureInfo.InvariantCulture)));
        stats.Add(new DesignStat("Battle movement", design.BattleMovement.ToString(CultureInfo.InvariantCulture) + " / 8"));
        stats.Add(new DesignStat("Scanner range", design.NormalScan.ToString(CultureInfo.InvariantCulture)
            + " ly normal, " + design.PenetratingScan.ToString(CultureInfo.InvariantCulture) + " ly penetrating"));
        stats.Add(new DesignStat("Jammer", Percent(design.Jammer)));
        stats.Add(new DesignStat("Beam deflector", design.BeamDeflectorPercent.ToString(CultureInfo.InvariantCulture) + "%"));
        stats.Add(new DesignStat("Capacitor", design.CapacitorPercent.ToString(CultureInfo.InvariantCulture) + "%"));

        if (design.FuelGenerationPerYear > 0)
        {
            stats.Add(new DesignStat("Fuel generation", design.FuelGenerationPerYear.ToString(CultureInfo.InvariantCulture) + " mg/year"));
        }

        if (design.MineCount > 0)
        {
            stats.Add(new DesignStat("Mine laying", design.MineCount.ToString(CultureInfo.InvariantCulture) + " mines/year"));
        }

        if (design.HasWeapons)
        {
            stats.Add(new DesignStat("Weapons", "armed"));
        }

        if (design.IsBomber)
        {
            stats.Add(new DesignStat("Bombing", "armed"));
        }

        if (design.CanColonize)
        {
            stats.Add(new DesignStat("Colonization", "capable"));
        }

        stats.Add(new DesignStat("Starbase", design.IsStarbase ? "yes" : "no"));
        return stats;
    }

    private static string Percent(double value) => value.ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>Compact "12 Iron, 8 Energy" cost, showing only the non-zero parts (mirrors
    /// Nova.Avalonia's ResourceFormat.Cost so the pure library stays UI-free).</summary>
    private static string FormatCost(Resources cost)
    {
        var parts = new List<string>();
        if (cost.Ironium > 0)
        {
            parts.Add(cost.Ironium.ToString(CultureInfo.InvariantCulture) + " Iron");
        }

        if (cost.Boranium > 0)
        {
            parts.Add(cost.Boranium.ToString(CultureInfo.InvariantCulture) + " Bor");
        }

        if (cost.Germanium > 0)
        {
            parts.Add(cost.Germanium.ToString(CultureInfo.InvariantCulture) + " Ger");
        }

        if (cost.Energy > 0)
        {
            parts.Add(cost.Energy.ToString(CultureInfo.InvariantCulture) + " Energy");
        }

        return parts.Count == 0 ? "Free" : string.Join(", ", parts);
    }
}
