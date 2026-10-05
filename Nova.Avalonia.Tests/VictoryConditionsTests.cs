using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Avalonia.Views.Panels;
using Nova.Client;
using Nova.Common;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// behavior-specs-11/victory-conditions.md section 3 (coverage row 24; client-ui-dialog-catalog.md
/// row 60's view 1): the victory-conditions view - one line per player, grey when out, blue with
/// the winner mark, with seven check columns from the score record's met bits 6-12 (disabled check
/// grey, enabled black, winner's enabled check blue); and the nine-line condition list, lines 1-7
/// grey when disabled, 8 and 9 always black, with line 1's N = percentage x planets / 100 rounded
/// down.
/// </summary>
[TestFixture]
public class VictoryConditionsTests
{
    // The generated default settings' three enabled conditions: PlanetsOwned, TechLevels and
    // SecondPlaceScore; TotalScore, ProductionCapacity, CapitalShips and HighestScore are off
    // (victory-conditions.md section 1, the confirmed defaults).
    private static GameSettings DefaultSettings()
    {
        return TestGame.FreshSettings();
    }

    [Test]
    public void PlayerRows_DrawOnlyMetBits_AndShadeThemByTheSpecRules()
    {
        GameSettings settings = DefaultSettings();
        ScoreRecord record = new ScoreRecord
        {
            EmpireId = 1,
            MetMask = 0x7F, // every one of the seven conditions is met
            Planets = 4,    // keeps the race in the game
        };

        VictoryPlayerRow row = VictorySummary.PlayerRows(settings, new[] { record }, _ => "Us").Single();

        Assert.That(row.Name, Is.EqualTo("Us"));
        Assert.That(row.Out, Is.False);
        Assert.That(row.Winner, Is.False);
        Assert.That(row.Marks, Has.Count.EqualTo(7));

        Assert.That(row.Marks[0].Shade, Is.EqualTo(VictoryCheckShade.Black), "planets enabled, not the winner");
        Assert.That(row.Marks[1].Shade, Is.EqualTo(VictoryCheckShade.Black), "tech enabled, not the winner");
        Assert.That(row.Marks[2].Shade, Is.EqualTo(VictoryCheckShade.Grey), "score disabled");
        Assert.That(row.Marks[3].Shade, Is.EqualTo(VictoryCheckShade.Black), "second place enabled, not the winner");
        Assert.That(row.Marks[4].Shade, Is.EqualTo(VictoryCheckShade.Grey), "production disabled");
        Assert.That(row.Marks[5].Shade, Is.EqualTo(VictoryCheckShade.Grey), "capital ships disabled");
        Assert.That(row.Marks[6].Shade, Is.EqualTo(VictoryCheckShade.Grey), "highest score disabled");
    }

    [Test]
    public void PlayerRows_AnUnmetBit_DrawsNoCheck()
    {
        GameSettings settings = DefaultSettings();
        ScoreRecord record = new ScoreRecord { EmpireId = 2, MetMask = 0, Planets = 1 };

        VictoryPlayerRow row = VictorySummary.PlayerRows(settings, new[] { record }, _ => "Them").Single();

        Assert.That(row.Marks.Select(mark => mark.Shade), Is.All.EqualTo(VictoryCheckShade.None));
        Assert.That(row.Marks.Select(mark => mark.Met), Is.All.False);
    }

    [Test]
    public void PlayerRows_TheWinnerMark_BluesAnEnabledMetColumn()
    {
        GameSettings settings = DefaultSettings();
        ScoreRecord record = new ScoreRecord { EmpireId = 3, MetMask = 1, Planets = 2, Winner = true };

        VictoryPlayerRow row = VictorySummary.PlayerRows(settings, new[] { record }, _ => "W").Single();

        Assert.That(row.Winner, Is.True);
        Assert.That(row.Marks[0].Shade, Is.EqualTo(VictoryCheckShade.Blue), "blue for the winner's enabled condition");
    }

    [Test]
    public void PlayerRows_AnOutPlayer_GreysEveryCheck()
    {
        GameSettings settings = DefaultSettings();
        ScoreRecord record = new ScoreRecord { EmpireId = 4, MetMask = 1, Planets = 0 };

        VictoryPlayerRow row = VictorySummary.PlayerRows(settings, new[] { record }, _ => "Out").Single();

        Assert.That(row.Out, Is.True, "no planets and no ships of any class");
        Assert.That(row.Marks[0].Shade, Is.EqualTo(VictoryCheckShade.Grey), "out greys even an enabled met condition");
    }

    [Test]
    public void IsOut_IsTheServersFourZeroCountsTest_ShipsOrAPlanetKeepTheRaceIn()
    {
        Assert.That(VictorySummary.IsOut(new ScoreRecord { Planets = 0, EscortShips = 1 }), Is.False, "an escort ship");
        Assert.That(VictorySummary.IsOut(new ScoreRecord { Planets = 0, CapitalShips = 1 }), Is.False, "a capital ship");
        Assert.That(VictorySummary.IsOut(new ScoreRecord { Planets = 1 }), Is.False, "an owned planet");
        Assert.That(VictorySummary.IsOut(new ScoreRecord()), Is.True, "no planets and no ships of any class");
    }

    [Test]
    public void ConditionLines_LineOne_OwnsFloorOfPercentageTimesPlanets()
    {
        GameSettings settings = DefaultSettings();
        settings.PlanetsOwned = new EnabledValue(true, 60);

        List<VictoryConditionLine> seven = VictorySummary.ConditionLines(settings, 7);
        Assert.That(seven[0].Text, Is.EqualTo("Owns 4 planets"), "60% of 7 rounds down to 4");

        List<VictoryConditionLine> ten = VictorySummary.ConditionLines(settings, 10);
        Assert.That(ten[0].Text, Is.EqualTo("Owns 6 planets"), "60% of 10 is exactly 6");
    }

    [Test]
    public void ConditionLines_ShowTheDecodedThresholds_AndTheDerivedMetaSettings()
    {
        GameSettings settings = DefaultSettings();
        settings.TargetsToMeet = 2;
        settings.MinimumGameTime = 50;

        List<VictoryConditionLine> lines = VictorySummary.ConditionLines(settings, 50);

        Assert.That(lines, Has.Count.EqualTo(9));
        Assert.That(lines.Select(line => line.Number), Is.EqualTo(Enumerable.Range(1, 9)));
        Assert.That(lines[1].Text, Is.EqualTo("Attains tech level 22 in 4 fields"));
        Assert.That(lines[2].Text, Is.EqualTo("Exceeds a score of 11000"));
        Assert.That(lines[7].Text, Is.EqualTo("The winner must meet 2 of the selected criteria"),
            "the derived min(TargetsToMeet, enabled count)");
        Assert.That(lines[8].Text, Is.EqualTo("At least 50 years must pass before a winner is declared"));
    }

    [Test]
    public void ConditionLines_OneToSevenGreyWhenDisabled_EightAndNineAlwaysBlack()
    {
        GameSettings settings = DefaultSettings();

        List<VictoryConditionLine> lines = VictorySummary.ConditionLines(settings, 50);

        Assert.That(lines[0].Grey, Is.False, "planets enabled");
        Assert.That(lines[1].Grey, Is.False, "tech enabled");
        Assert.That(lines[2].Grey, Is.True, "score disabled");
        Assert.That(lines[3].Grey, Is.False, "second place enabled");
        Assert.That(lines[4].Grey, Is.True, "production disabled");
        Assert.That(lines[5].Grey, Is.True, "capital ships disabled");
        Assert.That(lines[6].Grey, Is.True, "highest score disabled");
        Assert.That(lines[7].Grey, Is.False, "line 8 is always black");
        Assert.That(lines[8].Grey, Is.False, "line 9 is always black");
    }

    [Test]
    public void ConditionLines_LineEight_CarriesTheExtraGap()
    {
        GameSettings settings = DefaultSettings();

        List<VictoryConditionLine> lines = VictorySummary.ConditionLines(settings, 50);

        Assert.That(lines[7].ExtraGapBefore, Is.True, "the extra half-line gap before line 8");
        Assert.That(lines.Count(line => line.ExtraGapBefore), Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void ViewModel_FillsThePlayerLines_FromTheClientScoreRecords()
    {
        ClientData client = TestGame.Load();
        ushort own = client.EmpireState.Id;
        ushort other = client.EmpireState.EmpireReports.Keys.First();

        client.InputTurn.AllScores.Clear();
        client.InputTurn.AllScores.Add(new ScoreRecord { EmpireId = own, MetMask = 0x7F, Planets = 3 });
        client.InputTurn.AllScores.Add(new ScoreRecord { EmpireId = other, MetMask = 1, Planets = 0 });

        var viewModel = new VictoryConditionsViewModel("VictoryConditions", "Victory Conditions", client);

        Assert.That(viewModel.PlayerRows, Has.Count.EqualTo(2));
        Assert.That(viewModel.PlayerRows[0].Name, Is.EqualTo(client.EmpireState.Race.Name));
        Assert.That(viewModel.PlayerRows[1].Name, Is.EqualTo(client.EmpireState.EmpireReports[other].RaceName));
        Assert.That(viewModel.PlayerRows[1].IsOut, Is.True, "no planets and no ships");
        Assert.That(viewModel.PlayerRows[0].Marks, Has.Count.EqualTo(7));
        Assert.That(viewModel.ConditionLines, Has.Count.EqualTo(9));
        Assert.That(viewModel.HasPlayers, Is.True);
    }

    [AvaloniaTest]
    public void ViewModel_WithoutAClient_ShowsTheConditionListOnly()
    {
        GameSettings previous = GameSettings.Data;
        try
        {
            GameSettings.Data = TestGame.FreshSettings();
            GameSettings.Data.NumberOfStars = 10;

            var viewModel = new VictoryConditionsViewModel("VictoryConditions", "Victory Conditions");

            Assert.That(viewModel.PlayerRows, Is.Empty);
            Assert.That(viewModel.HasPlayers, Is.False);
            Assert.That(viewModel.ConditionLines, Has.Count.EqualTo(9));
            Assert.That(viewModel.ConditionLines[0].Text, Is.EqualTo("Owns 6 planets"), "60% of 10");
        }
        finally
        {
            GameSettings.Data = previous;
        }
    }

    [AvaloniaTest]
    public void View_RendersThePlayerNames_TheCheckMarks_AndTheConditionList()
    {
        ClientData client = TestGame.Load();
        client.InputTurn.AllScores.Clear();
        client.InputTurn.AllScores.Add(new ScoreRecord { EmpireId = client.EmpireState.Id, MetMask = 1, Planets = 3 });

        var viewModel = new VictoryConditionsViewModel("VictoryConditions", "Victory Conditions", client);
        Window window = Headless.Show(new VictoryConditionsView { DataContext = viewModel });

        List<string> texts = Headless.Texts(window);
        Assert.That(texts, Has.Member(client.EmpireState.Race.Name), "the player's name");
        Assert.That(texts, Has.Member("\u2714"), "a check mark for the met bit");
        Assert.That(texts.Any(text => text.StartsWith("Owns ")), Is.True, "line 1 of the condition list");
        window.Close();
    }
}
