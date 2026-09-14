using System.Linq;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The Summary panel: empire-wide totals - planets, fleets, population, and resources on
/// hand across every owned planet.
/// </summary>
public class SummaryViewModel : Tool
{
    public int Year { get; }

    public int PlanetCount { get; }

    public int FleetCount { get; }

    public long TotalPopulation { get; }

    public int TotalIronium { get; }

    public int TotalBoranium { get; }

    public int TotalGermanium { get; }

    public SummaryViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;

        EmpireData empire = clientState.EmpireState;
        Year = empire.TurnYear;
        PlanetCount = empire.OwnedStars.Count;
        FleetCount = empire.OwnedFleets.Count;
        TotalPopulation = empire.OwnedStars.Values.Sum(star => (long)star.Colonists);
        TotalIronium = empire.OwnedStars.Values.Sum(star => star.ResourcesOnHand.Ironium);
        TotalBoranium = empire.OwnedStars.Values.Sum(star => star.ResourcesOnHand.Boranium);
        TotalGermanium = empire.OwnedStars.Values.Sum(star => star.ResourcesOnHand.Germanium);
    }
}
