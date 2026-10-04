using System.Linq;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The Ship Design tab (TODO-FEATURES #1): a single tap on an owned design shows its details
/// straight away, including cargo capacity and the other figures the original design screen
/// showed. The figure list itself is pure formatting in <see cref="DesignDetails"/>.
/// </summary>
[TestFixture]
public class ShipDesignViewModelTests
{
    private static ShipDesignViewModel Open(out ClientData client)
    {
        client = TestGame.Load();
        var selection = new SelectionService();
        return new ShipDesignViewModel("ShipDesign", "Ship Design", client, selection);
    }

    [AvaloniaTest]
    public void OwnedDesigns_AreListed()
    {
        ShipDesignViewModel viewModel = Open(out ClientData client);

        Assert.That(viewModel.OwnedDesigns, Is.Not.Empty);
        Assert.That(viewModel.OwnedDesigns.Count, Is.EqualTo(client.EmpireState.Designs.Count));
        Assert.That(viewModel.HasSelectedDesignDetails, Is.False, "nothing is shown until a design is tapped");
    }

    [AvaloniaTest]
    public void SelectingADesign_ShowsItsDetailsWithCargoCapacity()
    {
        ShipDesignViewModel viewModel = Open(out _);
        OwnedDesignRowViewModel row = viewModel.OwnedDesigns.First();

        viewModel.SelectedDesignRow = row;

        Assert.That(viewModel.HasSelectedDesignDetails, Is.True);
        Assert.That(viewModel.SelectedDesignDetails!.Title, Is.EqualTo(row.Name));

        DesignStat cargo = viewModel.SelectedDesignDetails.Stats.Single(stat => stat.Label == "Cargo capacity");
        Assert.That(cargo.Value, Is.EqualTo(row.Design.CargoCapacity + " kT"));
        Assert.That(viewModel.SelectedDesignDetails.Stats.Any(stat => stat.Label == "Cost"), Is.True);
        Assert.That(viewModel.SelectedDesignDetails.Stats.Any(stat => stat.Label == "Mass"), Is.True);
    }

    [AvaloniaTest]
    public void SelectingADesignByItsCommand_ShowsItsDetails()
    {
        ShipDesignViewModel viewModel = Open(out _);
        OwnedDesignRowViewModel row = viewModel.OwnedDesigns.First();

        row.SelectCommand.Execute(null);

        Assert.That(viewModel.SelectedDesignDetails, Is.Not.Null);
        Assert.That(viewModel.SelectedDesignDetails!.Title, Is.EqualTo(row.Name));
    }
}
