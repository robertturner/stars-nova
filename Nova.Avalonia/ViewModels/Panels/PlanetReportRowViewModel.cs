using System;
using System.Globalization;
using System.Linq;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Planet Report - ports PlanetReport.cs's OnLoad column-by-column. The column set
/// and order follow behavior-specs-11/client-ui-dialog-catalog.md "Reports" (dynamic strings
/// 1113-1127: planet name, starbase, population, cap, value, production, mines, factories,
/// defense, minerals, mining rate, mineral concentration, resources, driver destination, routing
/// destination).
/// </summary>
public class PlanetReportRowViewModel
{
    public string Name { get; }

    public string Starbase { get; }

    public string Population { get; }

    /// <summary>The spec's "cap" column: the planet's population capacity in colonists
    /// (client-ui-dialog-catalog.md Reports "Planets" and the export's "Cap" row, FUN_1048_476c).
    /// BUG FOUND: the port's previous column used Star.Capacity(), which is the UTILISATION
    /// percentage, not the cap; this now reports the capacity itself.</summary>
    public string Cap { get; }

    public string Value { get; }

    public string Production { get; }

    public string Mines { get; }

    public string Factories { get; }

    public string Defenses { get; }

    public string Minerals { get; }

    public string MiningRate { get; }

    public string Concentration { get; }

    public string Resources { get; }

    public string DriverDestination { get; }

    public string RoutingDestination { get; }

    public PlanetReportRowViewModel(Star star, Race race)
    {
        Name = star.Name;
        Starbase = star.Starbase != null ? star.Starbase.Name : "-";
        Population = star.Colonists.ToString(CultureInfo.InvariantCulture);
        Cap = ((int)star.CapacityColonists(race)).ToString(CultureInfo.InvariantCulture);
        Value = Math.Ceiling(race.HabValue(star) * 100).ToString(CultureInfo.InvariantCulture);

        // "production": the queue summary. SPEC GAP: the report window's production cell wording is
        // not given; the spec's export uses a queue summary (FUN_1048_44de). This shows the first
        // queued order's name, empty when the queue is empty.
        Production = star.ManufacturingQueue.Queue.FirstOrDefault()?.Name ?? string.Empty;

        Mines = star.Mines.ToString(CultureInfo.InvariantCulture);
        Factories = star.Factories.ToString(CultureInfo.InvariantCulture);

        Nova.Common.Defenses.ComputeDefenseCoverage(star);
        Defenses = Nova.Common.Defenses.SummaryCoverage.ToString(CultureInfo.InvariantCulture);

        Nova.Common.Resources resourcesOnHand = star.ResourcesOnHand;
        Minerals = $"{resourcesOnHand.Ironium} {resourcesOnHand.Boranium} {resourcesOnHand.Germanium}";

        // "mining rate": kT per year for each mineral (the spec export's "MR", FUN_1028_3a74; the
        // port's own Star.GetMiningRate reads the same mining engine).
        MiningRate = string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} {2}",
            star.GetMiningRate(star.MineralConcentration.Ironium),
            star.GetMiningRate(star.MineralConcentration.Boranium),
            star.GetMiningRate(star.MineralConcentration.Germanium));

        Nova.Common.Resources concentration = star.MineralConcentration;
        Concentration = $"{concentration.Ironium} {concentration.Boranium} {concentration.Germanium}";

        Resources = ((int)resourcesOnHand.Energy).ToString(CultureInfo.InvariantCulture);

        // "driver destination": the planet's packet destination (spec export "Driver"; planet word
        // +0x2e bits 0-9 hold the target's planet number plus one). Empty when none is set.
        DriverDestination = star.PacketDestination ?? string.Empty;

        // "routing destination": the planet's route target (behavior-specs-11/client-interface.md
        // line 381, planet word +0x30 bits 0-9). SPEC GAP: the port's Star models no route target,
        // so there is no data source. Named seam: always empty until that field is added.
        RoutingDestination = string.Empty;
    }
}
