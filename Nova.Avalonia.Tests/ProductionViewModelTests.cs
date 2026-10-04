using System.Linq;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The Production panel against a real generated game (production-queue.md sections 6, 9, 10:
/// the catalog, auto-build, the terraform and mineral-packet items, the saved templates). Tests
/// avoid the questions-document stand-ins: whether packet items are hidden without a mass driver
/// (7.5), what a new empire's template slots hold or whether applying replaces or appends (7.1).
/// </summary>
[TestFixture]
public class ProductionViewModelTests
{
    private static (ClientData Client, SelectionService Selection, ProductionViewModel Production, Star Home) Open(string race = TestGame.PacketRace)
    {
        ClientData client = TestGame.Load(race);
        var selection = new SelectionService();
        var production = new ProductionViewModel("Production", "Production", client, selection);
        Star home = TestGame.HomeStar(client);
        home.ManufacturingQueue.Queue.Clear();
        return (client, selection, production, home);
    }

    [AvaloniaTest]
    public void NoPlanetSelected_ShowsAMessage_AndNoCatalog()
    {
        (ClientData client, SelectionService selection, ProductionViewModel production, Star home) = Open();

        Assert.That(production.HasPlanet, Is.False);
        Assert.That(production.AvailableItems, Is.Empty);
        Assert.That(production.HasMessage, Is.True);
        Assert.That(production.ReplaceWithTemplateCommand.CanExecute(null), Is.False);
        Assert.That(production.AppendTemplateCommand.CanExecute(null), Is.False);

        selection.Selected = TestGame.FleetsAt(client, home)[0];
        Assert.That(production.HasPlanet, Is.False, "a fleet has no production queue");
        Assert.That(production.Message, Is.Not.Empty);
    }

    /// <summary>The fixed installations and the dock-sized ship designs are offered; the planet's
    /// own starbase design is not.</summary>
    [AvaloniaTest]
    public void Catalog_OffersTheInstallationsAndShipDesigns()
    {
        (ClientData client, SelectionService selection, ProductionViewModel production, Star home) = Open();
        selection.Selected = home;

        Assert.That(production.HasPlanet, Is.True);
        Assert.That(production.PlanetName, Is.EqualTo(home.Name));
        var units = production.AvailableItems.Select(item => item.Unit).ToList();
        Assert.That(units, Has.Some.InstanceOf<FactoryProductionUnit>());
        Assert.That(units, Has.Some.InstanceOf<MineProductionUnit>());
        Assert.That(units, Has.Some.InstanceOf<DefenseProductionUnit>());
        Assert.That(units, Has.Some.InstanceOf<AlchemyProductionUnit>());
        Assert.That(units, Has.Some.InstanceOf<ShipProductionUnit>(), "the home starbase has a dock");

        long starbaseDesign = home.Starbase!.Composition.Values.First().Design.Key;
        Assert.That(units.OfType<ShipProductionUnit>().Select(unit => unit.DesignKey), Has.No.Member(starbaseDesign));
        Assert.That(production.AvailableItems.Select(item => item.CostSummary), Has.All.Not.Empty);
    }

    /// <summary>production-queue.md section 6/10k: the auto terraform entry can only be added as an
    /// auto-build order and carries the Min / Max choice.</summary>
    [AvaloniaTest]
    public void AutoTerraformItem_IsAutoOnly_AndCarriesTheMinMaxChoice()
    {
        (_, SelectionService selection, ProductionViewModel production, Star home) = Open();
        selection.Selected = home;

        ProductionCatalogItemViewModel terraform = production.AvailableItems.Single(item => item.IsTerraform && item.AutoOnly);
        production.SelectedAvailableItem = terraform;

        Assert.That(production.AutoBuildOnAdd, Is.True, "selecting it ticks the auto-build box");
        Assert.That(production.CanToggleAutoBuildOnAdd, Is.False, "and the box cannot be cleared");
        production.AutoBuildOnAdd = false;
        Assert.That(production.AutoBuildOnAdd, Is.True);
        Assert.That(production.ShowTerraformAutoChoice, Is.True);
        Assert.That(production.TerraformAutoChoices, Is.EqualTo(new[] { ProductionCaptions.MaxTerraform, ProductionCaptions.MinTerraform }));

        production.TerraformAutoChoiceIndex = 1;
        production.AddToQueueCommand.Execute(null);

        ProductionOrder order = home.ManufacturingQueue.Queue.Single();
        Assert.That(order.IsAutoBuild, Is.True);
        Assert.That(((TerraformProductionUnit)order.Unit).MinimumOnly, Is.True, "Min Terraform was chosen");
        Assert.That(production.Queue.Single().Name, Is.EqualTo(ProductionCaptions.MinTerraform));
    }

    /// <summary>production-queue.md section 10: on a mass-driver planet with a destination the
    /// packet items are offered; the only auto-build packet type is the mixed one.</summary>
    [AvaloniaTest]
    public void PacketItems_OnAMassDriverPlanet_OnlyTheMixedPacketIsAutoBuild()
    {
        ClientData client = TestGame.Load(TestGame.PacketRace);
        Star driverPlanet = client.EmpireState.OwnedStars.Values.First(PacketOrders.CanSetDestination);
        string destination = client.EmpireState.StarReports.Keys.First(name => name != driverPlanet.Name);
        Assert.That(PacketOrders.Issue(client, PacketOrders.DestinationOrder(driverPlanet, destination, driverPlanet.PacketWarp)), Is.True);
        driverPlanet.ManufacturingQueue.Queue.Clear();

        var selection = new SelectionService();
        var production = new ProductionViewModel("Production", "Production", client, selection);
        selection.Selected = driverPlanet;

        var packets = production.AvailableItems.Where(item => item.Unit is PacketProductionUnit).ToList();
        Assert.That(packets, Is.Not.Empty);
        ProductionCatalogItemViewModel auto = packets.Single(item => item.AutoOnly);
        Assert.That(((PacketProductionUnit)auto.Unit).Mineral, Is.EqualTo(PacketMineral.Mixed));
        Assert.That(packets.Where(item => item != auto), Has.All.Matches<ProductionCatalogItemViewModel>(item => item.ManualOnly));

        ProductionCatalogItemViewModel ironium = packets.Single(item => ((PacketProductionUnit)item.Unit).Mineral == PacketMineral.Ironium);
        production.SelectedAvailableItem = ironium;
        production.AutoBuildOnAdd = true;
        Assert.That(production.AutoBuildOnAdd, Is.False, "a single-mineral packet is a manual order only");

        production.AddToQueueCommand.Execute(null);
        Assert.That(driverPlanet.ManufacturingQueue.Queue.Single().IsAutoBuild, Is.False);

        production.Queue.Single().ToggleAutoBuildCommand.Execute(null);
        Assert.That(driverPlanet.ManufacturingQueue.Queue.Single().IsAutoBuild, Is.False, "and cannot be toggled to auto");
    }

    /// <summary>Add, quantity steps, auto-build toggle, reorder and delete each go through a
    /// queued ProductionCommand.</summary>
    [AvaloniaTest]
    public void QueueEditing_AddStepToggleMoveDelete()
    {
        (ClientData client, SelectionService selection, ProductionViewModel production, Star home) = Open();
        selection.Selected = home;
        Assert.That(production.Message, Is.Not.Empty, "an empty queue says so");

        production.SelectedAvailableItem = production.AvailableItems.First(item => item.Unit is FactoryProductionUnit);
        production.AddQuantity = 5;
        production.AddToQueueCommand.Execute(null);
        production.SelectedAvailableItem = production.AvailableItems.First(item => item.Unit is MineProductionUnit);
        production.AddQuantity = 2;
        production.AddToQueueCommand.Execute(null);

        Assert.That(production.Queue, Has.Count.EqualTo(2));
        Assert.That(client.Commands.Peek(), Is.InstanceOf<ProductionCommand>());

        production.Queue[0].IncrementCommand.Execute(null);
        Assert.That(home.ManufacturingQueue.Queue[0].Quantity, Is.EqualTo(6));

        production.Queue[0].ToggleAutoBuildCommand.Execute(null);
        Assert.That(home.ManufacturingQueue.Queue[0].IsAutoBuild, Is.True);

        Assert.That(production.Queue[0].MoveUpCommand.CanExecute(null), Is.False);
        production.Queue[1].MoveUpCommand.Execute(null);
        Assert.That(home.ManufacturingQueue.Queue[0].Unit, Is.InstanceOf<MineProductionUnit>());

        production.Queue[0].DeleteCommand.Execute(null);
        Assert.That(home.ManufacturingQueue.Queue, Has.Count.EqualTo(1));
        Assert.That(production.Queue, Has.Count.EqualTo(1));
    }

    /// <summary>production-queue.md section 9: four named template slots; applying one queues its
    /// orders in one action (on an empty queue, so the replace/append question 7.1 does not matter).</summary>
    [AvaloniaTest]
    public void Templates_FourSlots_ApplyingOneQueuesItsOrders()
    {
        (ClientData client, SelectionService selection, ProductionViewModel production, Star home) = Open();
        var template = new ProductionTemplate { Name = "Growth" };
        Assert.That(template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Factories, 10)), Is.True);
        Assert.That(template.TryAdd(new ProductionTemplateEntry(TemplateItemType.Mines, 7)), Is.True);
        client.EmpireState.ProductionTemplates.SetSlot(2, template);

        selection.Selected = home;
        Assert.That(production.TemplateSlotNames, Has.Count.EqualTo(4));
        Assert.That(production.TemplateSlotNames[2], Does.Contain("Growth"));
        Assert.That(production.AppendTemplateCommand.CanExecute(null), Is.True);

        production.SelectedTemplateSlot = 2;
        production.AppendTemplateCommand.Execute(null);

        Assert.That(home.ManufacturingQueue.Queue, Has.Count.EqualTo(2));
        Assert.That(home.ManufacturingQueue.Queue[0].Unit, Is.InstanceOf<FactoryProductionUnit>());
        Assert.That(home.ManufacturingQueue.Queue[0].Quantity, Is.EqualTo(10));
        Assert.That(home.ManufacturingQueue.Queue[1].Unit, Is.InstanceOf<MineProductionUnit>());
        Assert.That(home.ManufacturingQueue.Queue.All(order => order.IsAutoBuild), Is.True, "template lines are auto-build orders");
        Assert.That(production.Queue, Has.Count.EqualTo(2));
    }
}
