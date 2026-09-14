using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>Read-only table of every owned planet - ports PlanetReport.cs. No interactivity
/// beyond the table itself in the original (no sorting/double-click), so none is added here.</summary>
public class PlanetReportViewModel : Tool
{
    public IReadOnlyList<PlanetReportRowViewModel> Planets { get; }

    public PlanetReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;

        Race race = clientState.EmpireState.Race;
        Planets = clientState.EmpireState.OwnedStars.Values
            .Where(star => star.Owner == clientState.EmpireState.Id)
            .Select(star => new PlanetReportRowViewModel(star, race))
            .ToList();
    }
}
