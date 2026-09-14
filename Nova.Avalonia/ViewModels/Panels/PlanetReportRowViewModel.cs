using System;
using System.Globalization;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>One row in the Planet Report - ports PlanetReport.cs's OnLoad column-by-column.</summary>
public class PlanetReportRowViewModel
{
    public string Name { get; }

    public string Starbase { get; }

    public string Population { get; }

    public string Capacity { get; }

    public string Value { get; }

    public string Mines { get; }

    public string Factories { get; }

    public string Defenses { get; }

    public string Minerals { get; }

    public string Concentration { get; }

    public string Resources { get; }

    public PlanetReportRowViewModel(Star star, Race race)
    {
        Name = star.Name;
        Starbase = star.Starbase != null ? star.Starbase.Name : "-";
        Population = star.Colonists.ToString(CultureInfo.InvariantCulture);
        Capacity = star.Capacity(race).ToString(CultureInfo.InvariantCulture);
        Value = Math.Ceiling(race.HabValue(star) * 100).ToString(CultureInfo.InvariantCulture);
        Mines = star.Mines.ToString(CultureInfo.InvariantCulture);
        Factories = star.Factories.ToString(CultureInfo.InvariantCulture);

        Nova.Common.Defenses.ComputeDefenseCoverage(star);
        Defenses = Nova.Common.Defenses.SummaryCoverage.ToString(CultureInfo.InvariantCulture);

        Nova.Common.Resources resourcesOnHand = star.ResourcesOnHand;
        Minerals = $"{resourcesOnHand.Ironium} {resourcesOnHand.Boranium} {resourcesOnHand.Germanium}";

        Nova.Common.Resources concentration = star.MineralConcentration;
        Concentration = $"{concentration.Ironium} {concentration.Boranium} {concentration.Germanium}";

        Resources = ((int)resourcesOnHand.Energy).ToString(CultureInfo.InvariantCulture);
    }
}
