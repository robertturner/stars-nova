using System.Collections.Generic;
using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>Read-only table of every owned fleet (excluding starbases) - ports FleetReport.cs.</summary>
public class FleetReportViewModel : Tool
{
    public IReadOnlyList<FleetReportRowViewModel> Fleets { get; }

    public FleetReportViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;

        Fleets = clientState.EmpireState.OwnedFleets.Values
            .Where(fleet => fleet.Owner == clientState.EmpireState.Id && fleet.Type != ItemType.Starbase)
            .Select(fleet => new FleetReportRowViewModel(fleet))
            .ToList();
    }
}
