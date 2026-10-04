using System;
using System.IO;
using System.Linq;
using Avalonia.Headless.NUnit;
using Nova.Ai;
using Nova.Avalonia.ViewModels;
using Nova.Client;
using Nova.Common;
using Nova.Server;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The New Game screen (client-ui-dialog-catalog.md rows 17/18; client-interface.md rows 65/66;
/// ai-opponent-behavior.md section 1a's AI picker). The Simplified path's player-count ranges and
/// year gate are stand-ins (question 3.1/3.2), so they are read through NewGameSetup's seams, and
/// which archetype/tier each Simplified computer slot gets (4.4) is not pinned.
/// </summary>
[TestFixture]
public class NewGameViewModelTests
{
    private static NewGameViewModel Open()
    {
        TestGame.PrepareEnvironment();
        GameSettings.Data = TestGame.FreshSettings();
        var viewModel = new NewGameViewModel();
        viewModel.GameName = "UiNewGame_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        viewModel.GameFolder = Path.Combine(TestGame.ScratchRoot, viewModel.GameName);
        return viewModel;
    }

    private static ServerData LoadServer(string folder)
    {
        string statePath = Directory.GetFiles(folder, "*" + Global.ServerStateExtension).Single();
        var server = new ServerData { StatePathName = statePath };
        server.Restore();
        return server;
    }

    [AvaloniaTest]
    public void RaceOptions_ComeFromTheShippedRaceFiles()
    {
        NewGameViewModel newGame = Open();

        Assert.That(newGame.RaceOptions, Does.Contain("Humanoid"));
        Assert.That(newGame.Players, Has.Count.EqualTo(2), "two players to start with");
        Assert.That(newGame.Players.Select(p => p.DisplayNumber), Is.EqualTo(new[] { 1, 2 }));
    }

    /// <summary>Row 17: the Simplified path seeds the player count and year gate from the galaxy
    /// size (values read through NewGameSetup's seams).</summary>
    [AvaloniaTest]
    public void SimplifiedPath_SeedsPlayerCountAndYearGateFromTheGalaxySize()
    {
        NewGameViewModel newGame = Open();
        newGame.SelectedSetupMode = NewGameViewModel.SimplifiedMode;
        Assert.That(newGame.IsSimplified, Is.True);
        Assert.That(newGame.IsDetailed, Is.False);

        foreach (GalaxySize size in new[] { GalaxySize.Small, GalaxySize.Tiny, GalaxySize.Large })
        {
            newGame.GalaxySizeIndex = (int)size;
            (int min, int max) = NewGameSetup.SimplifiedPlayerCountRange(size, newGame.SimplifiedDifficultyIndex);
            Assert.That(newGame.SimplifiedPlayerCount, Is.InRange(min, max), size.ToString());
            Assert.That(newGame.MinimumGameTime, Is.EqualTo(NewGameSetup.SimplifiedYearGate(size)), size.ToString());
            Assert.That(newGame.SimplifiedYearGate, Is.EqualTo(NewGameSetup.SimplifiedYearGate(size)));
        }

        Assert.That(newGame.DifficultyOptions, Has.Count.EqualTo(4));
    }

    /// <summary>Row 17: a Simplified game is you plus computer players, all created at once.</summary>
    [AvaloniaTest]
    public void SimplifiedPath_CreatesYouPlusComputerPlayers()
    {
        NewGameViewModel newGame = Open();
        newGame.SelectedSetupMode = NewGameViewModel.SimplifiedMode;
        newGame.GalaxySizeIndex = (int)GalaxySize.Tiny;
        newGame.SimplifiedRaceName = "Humanoid";
        ClientData? opened = null;
        newGame.GameOpened += client => opened = client;

        Assert.That(newGame.CreateGameCommand.CanExecute(null), Is.True);
        newGame.CreateGameCommand.Execute(null);

        Assert.That(opened, Is.Not.Null, newGame.StatusMessage);
        Assert.That(opened!.EmpireState.Race.Name, Is.EqualTo("Humanoid"));
        ServerData server = LoadServer(newGame.GameFolder);
        Assert.That(server.AllPlayers, Has.Count.EqualTo(newGame.SimplifiedPlayerCount));
        Assert.That(server.AllPlayers.Count(p => p.AiProgram == NewGamePlayerRowViewModel.Human), Is.EqualTo(1));
        Assert.That(server.AllPlayers.Where(p => p.AiProgram != NewGamePlayerRowViewModel.Human).Select(p => p.AiCategory),
            Is.All.InRange(0, AiRaceTemplates.ArchetypeNames.Count() - 1), "every computer player has an archetype");
    }

    /// <summary>The AI picker: six archetypes and four tiers, each plus Random; a built-in AI slot
    /// needs no race file and is stored with its archetype as the AI category.</summary>
    [AvaloniaTest]
    public void DetailedPath_BuiltInAiSlot_IsCreatedWithItsArchetype()
    {
        NewGameViewModel newGame = Open();
        newGame.SelectedSetupMode = NewGameViewModel.DetailedMode;
        newGame.UseGalaxyPresets = true;
        newGame.GalaxySizeIndex = (int)GalaxySize.Tiny;
        NewGamePlayerRowViewModel human = newGame.Players[0];
        human.SelectedRaceName = "Humanoid";
        NewGamePlayerRowViewModel computer = newGame.Players[1];
        computer.SelectedAiProgram = NewGamePlayerRowViewModel.BuiltInAi;

        Assert.That(computer.IsBuiltInAi, Is.True);
        Assert.That(computer.ChoosesOwnRace, Is.False);
        Assert.That(computer.ArchetypeOptions, Has.Count.EqualTo(7), "six archetypes and Random");
        Assert.That(computer.TierOptions, Has.Count.EqualTo(5), "four tiers and Random");

        computer.SelectedArchetypeIndex = 2;
        computer.SelectedTierIndex = 1;
        newGame.CreateGameCommand.Execute(null);

        ServerData server = LoadServer(newGame.GameFolder);
        Assert.That(server.AllPlayers, Has.Count.EqualTo(2), newGame.StatusMessage);
        PlayerSettings ai = server.AllPlayers.Single(p => p.AiProgram != NewGamePlayerRowViewModel.Human);
        Assert.That(ai.AiProgram, Is.EqualTo(NewGamePlayerRowViewModel.DefaultAi));
        Assert.That(ai.AiCategory, Is.EqualTo(2));
    }

    /// <summary>Validation: Create needs a name, a folder, at least one player and a known race
    /// for every human/Default AI slot.</summary>
    [AvaloniaTest]
    public void DetailedPath_CreateIsEnabledOnlyForAValidSetup()
    {
        NewGameViewModel newGame = Open();
        newGame.SelectedSetupMode = NewGameViewModel.DetailedMode;
        newGame.Players[0].SelectedRaceName = "Humanoid";
        newGame.Players[1].SelectedRaceName = "Humanoid";
        Assert.That(newGame.CreateGameCommand.CanExecute(null), Is.True);

        string name = newGame.GameName;
        newGame.GameName = " ";
        Assert.That(newGame.CreateGameCommand.CanExecute(null), Is.False, "a game needs a name");
        newGame.GameName = name;

        newGame.Players[1].SelectedRaceName = "No Such Race";
        Assert.That(newGame.CreateGameCommand.CanExecute(null), Is.False, "every chosen race must exist");
        newGame.Players[1].SelectedAiProgram = NewGamePlayerRowViewModel.BuiltInAi;
        Assert.That(newGame.CreateGameCommand.CanExecute(null), Is.True, "a built-in AI needs no race file");

        while (newGame.Players.Count > 0)
        {
            newGame.RemovePlayerCommand.Execute(newGame.Players[0]);
        }

        Assert.That(newGame.CreateGameCommand.CanExecute(null), Is.False, "a game needs players");
        newGame.AddPlayerCommand.Execute(null);
        Assert.That(newGame.Players.Single().DisplayNumber, Is.EqualTo(1));
    }

    /// <summary>The simplified path needs a known race too.</summary>
    [AvaloniaTest]
    public void SimplifiedPath_NeedsAKnownRace()
    {
        NewGameViewModel newGame = Open();
        newGame.SelectedSetupMode = NewGameViewModel.SimplifiedMode;
        newGame.SimplifiedRaceName = "No Such Race";
        Assert.That(newGame.CreateGameCommand.CanExecute(null), Is.False);
        newGame.SimplifiedRaceName = "Humanoid";
        Assert.That(newGame.CreateGameCommand.CanExecute(null), Is.True);
    }

    /// <summary>Client-interface row 65: the long-lived draft keeps every value across pages and
    /// across a switch between the two setup paths.</summary>
    [AvaloniaTest]
    public void Values_AreRetainedAcrossPagesAndPaths()
    {
        NewGameViewModel newGame = Open();
        newGame.SelectedSetupMode = NewGameViewModel.DetailedMode;
        newGame.Players[0].SelectedRaceName = "Humanoid";
        newGame.SlowTechAdvance = true;
        newGame.Seed = 31337;
        string name = newGame.GameName;

        for (int page = 0; page < 3; page++)
        {
            newGame.SelectedTabIndex = page;
        }

        newGame.SelectedSetupMode = NewGameViewModel.SimplifiedMode;
        newGame.SelectedSetupMode = NewGameViewModel.DetailedMode;
        newGame.SelectedTabIndex = 0;

        Assert.That(newGame.GameName, Is.EqualTo(name));
        Assert.That(newGame.SlowTechAdvance, Is.True);
        Assert.That(newGame.Seed, Is.EqualTo(31337));
        Assert.That(newGame.Players[0].SelectedRaceName, Is.EqualTo("Humanoid"));
    }

    /// <summary>Client-interface row 66 / dialog catalog row 18: the seed can be entered or
    /// randomised and is the one the game is generated (and recorded) with.</summary>
    [AvaloniaTest]
    public void Seed_EnteredOrRandomised_IsTheGamesSeed()
    {
        NewGameViewModel newGame = Open();
        newGame.Seed = 1;
        newGame.RandomizeSeedCommand.Execute(null);
        Assert.That(newGame.Seed, Is.Not.EqualTo(1));

        newGame.Seed = 2024;
        newGame.SelectedSetupMode = NewGameViewModel.SimplifiedMode;
        newGame.GalaxySizeIndex = (int)GalaxySize.Tiny;
        newGame.SimplifiedRaceName = "Humanoid";
        newGame.CreateGameCommand.Execute(null);

        Assert.That(GameSettings.Data.Seed, Is.EqualTo(2024), newGame.StatusMessage);
        string settings = File.ReadAllText(Directory.GetFiles(newGame.GameFolder, "*" + Global.SettingsExtension).Single());
        Assert.That(settings, Does.Contain("2024"));
    }
}
