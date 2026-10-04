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
/// The smaller panels: Technology Browser (client-interface row 49, dialog catalog row 45),
/// Player Relations (client-interface row 63, dialog catalog row 24), Score (row 67) and the
/// manual (row 79). The Technology Browser's category list and order are Nova's own (question
/// 7.10), so the tests only page within whatever the first category is.
/// </summary>
[TestFixture]
public class PanelTests
{
    [AvaloniaTest]
    public void TechnologyBrowser_PagesThroughACategory()
    {
        var browser = new TechnologyBrowserViewModel("TechnologyBrowser", "Technology Browser", TestGame.Load());

        Assert.That(browser.Categories, Is.Not.Empty);
        Assert.That(browser.HasEntry, Is.True);
        Assert.That(browser.EntryName, Is.Not.Empty);
        Assert.That(browser.PreviousCommand.CanExecute(null), Is.False, "the first entry has no previous");

        string first = browser.EntryName;
        if (browser.NextCommand.CanExecute(null))
        {
            browser.NextCommand.Execute(null);
            Assert.That(browser.EntryName, Is.Not.EqualTo(first));
            Assert.That(browser.PositionText, Does.StartWith("2 of"));
            Assert.That(browser.PreviousCommand.CanExecute(null), Is.True);
            browser.PreviousCommand.Execute(null);
            Assert.That(browser.EntryName, Is.EqualTo(first));
        }

        foreach ((string label, TechLevel.ResearchField _) in TechBrowser.LevelLabels)
        {
            Assert.That(browser.LevelsLine, Does.Contain(label), "the race's six tech levels are shown");
        }
    }

    [AvaloniaTest]
    public void TechnologyBrowser_ShowOnlyAvailable_ListsOnlyWhatTheRaceCanBuild()
    {
        ClientData client = TestGame.Load();
        var browser = new TechnologyBrowserViewModel("TechnologyBrowser", "Technology Browser", client);

        browser.ShowOnlyAvailable = true;

        for (int category = 0; category < browser.Categories.Count; category++)
        {
            browser.CategoryIndex = category;
            for (int guard = 0; guard < 500 && browser.HasEntry; guard++)
            {
                Assert.That(client.EmpireState.AvailableComponents.Contains(browser.EntryName), Is.True, browser.EntryName);
                if (!browser.NextCommand.CanExecute(null))
                {
                    break;
                }

                browser.NextCommand.Execute(null);
            }
        }
    }

    /// <summary>Row 63 / catalog row 24: the three relation states; a change is a queued order.</summary>
    [AvaloniaTest]
    public void PlayerRelations_ListsTheOtherEmpire_AndQueuesARelationOrder()
    {
        ClientData client = TestGame.Load();
        var relations = new PlayerRelationsViewModel("PlayerRelations", "Player Relations", client);

        Assert.That(relations.HasEmpires, Is.True);
        EmpireRelationRowViewModel other = relations.Empires.Single();
        Assert.That(other.Id, Is.Not.EqualTo(client.EmpireState.Id));
        Assert.That(EmpireRelationRowViewModel.RelationOptions, Is.EquivalentTo(new[] { PlayerRelation.Enemy, PlayerRelation.Neutral, PlayerRelation.Friend }));

        PlayerRelation target = other.Relation == PlayerRelation.Friend ? PlayerRelation.Enemy : PlayerRelation.Friend;
        other.Relation = target;

        Assert.That(client.EmpireState.EmpireReports[other.Id].Relation, Is.EqualTo(target));
        Assert.That(client.Commands.Peek(), Is.InstanceOf<RelationCommand>());
    }

    /// <summary>Row 67: the score view lists the turn's standings.</summary>
    [AvaloniaTest]
    public void ScoreReport_ListsTheTurnsScores()
    {
        ClientData client = TestGame.Load();
        client.InputTurn.AllScores.Clear();
        client.InputTurn.AllScores.Add(new ScoreRecord { EmpireId = 1, Rank = 1, Score = 120, Planets = 2 });
        client.InputTurn.AllScores.Add(new ScoreRecord { EmpireId = 2, Rank = 2, Score = 80, Planets = 1 });

        var scores = new ScoreReportViewModel("ScoreReport", "Score", client);

        Assert.That(scores.Scores, Has.Count.EqualTo(2));
        Assert.That(scores.Scores.Select(s => s.Score), Is.EquivalentTo(new[] { 120, 80 }));
    }

    /// <summary>Row 79: the manual opens with its topic list, and topic search filters it.</summary>
    [AvaloniaTest]
    public void Help_LoadsTheManual_AndSearchFiltersTopics()
    {
        TestGame.PrepareEnvironment();
        var help = new HelpViewModel("Help", "Manual");

        Assert.That(help.Topics, Is.Not.Empty, "HelpContent/topics.tsv is shipped with the tests");
        Assert.That(help.ContentText, Is.Not.Empty);
        int all = help.Topics.Count;

        string word = help.Topics.First().Title.Split(' ').First();
        help.SearchText = word;
        Assert.That(help.Topics.Count, Is.LessThanOrEqualTo(all));
        Assert.That(help.Topics.Select(t => t.Title), Has.All.Contains(word).IgnoreCase);
    }
}
