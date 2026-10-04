using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels;
using Nova.Avalonia.Views;
using Nova.Client;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>The test harness itself: a game loads, and the full desktop game screen renders.</summary>
[TestFixture]
public class SmokeTests
{
    [AvaloniaTest]
    public void GeneratedGame_LoadsForEitherPlayer()
    {
        ClientData packet = TestGame.Load(TestGame.PacketRace);
        Assert.That(packet.EmpireState.Race.Name, Is.EqualTo(TestGame.PacketRace));
        Assert.That(packet.EmpireState.OwnedStars, Is.Not.Empty);
        Assert.That(packet.EmpireState.OwnedFleets, Is.Not.Empty);
        Assert.That(packet.InputTurn, Is.Not.Null);

        ClientData demolition = TestGame.Load(TestGame.DemolitionRace);
        Assert.That(demolition.EmpireState.Race.Name, Is.EqualTo(TestGame.DemolitionRace));
    }

    [AvaloniaTest]
    public void MainView_RendersTheDockedGameScreen()
    {
        var viewModel = new MainViewModel(TestGame.Load());
        Window window = Headless.Show(new MainView { DataContext = viewModel });

        Assert.That(Headless.All<Menu>(window), Has.Count.EqualTo(1));
        Assert.That(Headless.DistinctColours(window), Is.GreaterThan(4), "the rendered frame is blank");
        window.Close();
    }
}
