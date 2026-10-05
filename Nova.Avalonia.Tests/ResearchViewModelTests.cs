using System;
using System.Linq;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using Nova.Common.Components;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The Research panel (race-designer-ui-and-availability.md rows 24/25: the research-preference
/// editor loads the stored discipline and mode and writes only if changed; research-tech-tree.md
/// section 4: the "next field" setting).
/// </summary>
[TestFixture]
public class ResearchViewModelTests
{
    [AvaloniaTest]
    public void Opens_OnTheStoredTargetFieldBudgetAndNextField()
    {
        ClientData client = TestGame.Load();
        EmpireData empire = client.EmpireState;
        empire.ResearchTopics.Zero();
        empire.ResearchTopics[TechLevel.ResearchField.Weapons] = 1;
        empire.ResearchBudget = 25;
        empire.ResearchNextField = Research.NextFieldLowest;

        var research = new ResearchViewModel("Research", "Research", client);

        Assert.That(research.SelectedTargetField, Is.EqualTo(nameof(TechLevel.ResearchField.Weapons)));
        Assert.That(research.EditableBudget, Is.EqualTo(25));
        Assert.That(research.NextFieldOptions[research.SelectedNextFieldIndex], Is.EqualTo(ResearchNextField.LowestLabel));
        Assert.That(research.Fields, Has.Count.EqualTo(6));
        Assert.That(research.Fields.Single(f => f.IsCurrentTarget).Field, Is.EqualTo(nameof(TechLevel.ResearchField.Weapons)));
    }

    /// <summary>research-tech-tree.md section 4: the next-field values are the six named fields
    /// plus a "lowest field" option (plus the engine's "same field" default).</summary>
    [AvaloniaTest]
    public void NextFieldOptions_ListTheSixFieldsAndLowest()
    {
        var research = new ResearchViewModel("Research", "Research", TestGame.Load());

        foreach (TechLevel.ResearchField field in Enum.GetValues<TechLevel.ResearchField>())
        {
            Assert.That(research.NextFieldOptions, Does.Contain(field.ToString()));
        }

        Assert.That(research.NextFieldOptions, Does.Contain(ResearchNextField.LowestLabel));
    }

    /// <summary>Row 25: Apply with nothing changed writes no order.</summary>
    [AvaloniaTest]
    public void Apply_WithNoChange_QueuesNothing()
    {
        ClientData client = TestGame.Load();
        var research = new ResearchViewModel("Research", "Research", client);
        int commandsBefore = client.Commands.Count;

        research.ApplyCommand.Execute(null);

        Assert.That(client.Commands.Count, Is.EqualTo(commandsBefore));
        Assert.That(research.HasStatusMessage, Is.True);
    }

    /// <summary>Row 25 with a stored target field: Apply, or an edit undone before Apply, writes
    /// nothing. (BUG FOUND: ResearchCommand.IsValid compared the two TechLevels with ==, a
    /// reference comparison, so this always queued an order.)</summary>
    [AvaloniaTest]
    public void Apply_WithTheStoredSettingsUnchanged_QueuesNothing()
    {
        ClientData client = TestGame.Load();
        client.EmpireState.ResearchTopics.Zero();
        client.EmpireState.ResearchTopics[TechLevel.ResearchField.Energy] = 1;
        var research = new ResearchViewModel("Research", "Research", client);
        int commandsBefore = client.Commands.Count;

        research.ApplyCommand.Execute(null);
        research.SelectedTargetField = nameof(TechLevel.ResearchField.Weapons);
        research.SelectedTargetField = nameof(TechLevel.ResearchField.Energy);
        research.ApplyCommand.Execute(null);

        Assert.That(client.Commands.Count, Is.EqualTo(commandsBefore));
    }

    /// <summary>Row 25: a changed preference is written as one ResearchCommand and applied.</summary>
    [AvaloniaTest]
    public void Apply_WithAChange_QueuesOneResearchCommand()
    {
        ClientData client = TestGame.Load();
        var research = new ResearchViewModel("Research", "Research", client);
        string newTarget = research.TargetFieldOptions.First(field => field != research.SelectedTargetField);
        int nextIndex = research.NextFieldOptions.ToList().IndexOf(nameof(TechLevel.ResearchField.Biotechnology));
        int commandsBefore = client.Commands.Count;

        research.SelectedTargetField = newTarget;
        research.EditableBudget = research.EditableBudget == 40 ? 30 : 40;
        research.SelectedNextFieldIndex = nextIndex;
        Assert.That(client.Commands.Count, Is.EqualTo(commandsBefore), "editing alone writes nothing");

        research.ApplyCommand.Execute(null);

        Assert.That(client.Commands.Count, Is.EqualTo(commandsBefore + 1));
        Assert.That(client.Commands.Peek(), Is.InstanceOf<ResearchCommand>());
        Assert.That(client.EmpireState.ResearchTopics[Enum.Parse<TechLevel.ResearchField>(newTarget)], Is.EqualTo(1));
        Assert.That(client.EmpireState.ResearchNextField, Is.EqualTo((int)TechLevel.ResearchField.Biotechnology));
        Assert.That(research.SelectedTargetField, Is.EqualTo(newTarget), "the panel re-reads the applied state");
    }

    [AvaloniaTest]
    public void Preview_FollowsTheBudget()
    {
        var research = new ResearchViewModel("Research", "Research", TestGame.Load());

        research.EditableBudget = 0;
        Assert.That(research.CompletionTimeText, Is.EqualTo("Never"));
        Assert.That(research.BudgetedEnergy, Is.EqualTo(0));

        research.EditableBudget = 50;
        Assert.That(research.BudgetedEnergy, Is.EqualTo(research.AvailableEnergy * 50 / 100));
        Assert.That(research.Benefits, Is.Not.Empty, "the next levels unlock something");
    }

    /// <summary>research-tech-tree.md row 34: the benefits list must contain only parts the
    /// viewing race can actually receive. The Space Dock needs Improved Starbases, which the
    /// Humanoid-derived test race lacks, so it must not appear even though researching
    /// Construction alone would otherwise unlock it.</summary>
    [AvaloniaTest]
    public void Benefits_DoNotListATraitBarredPart()
    {
        ClientData client = TestGame.Load();
        EmpireData empire = client.EmpireState;

        Component spaceDock = new AllComponents().Fetch("Space Dock");
        Assert.That(spaceDock, Is.Not.Null);
        Assert.That(RaceComponents.IsRestrictedFor(spaceDock, empire.Race), Is.True,
            "the test race lacks Improved Starbases");

        var research = ResearchTargeting(client, TechLevel.ResearchField.Construction);

        Assert.That(research.Benefits, Is.Not.Empty, "Construction has obtainable parts to list");
        Assert.That(research.Benefits.Any(row => row.Text.StartsWith("Space Dock", StringComparison.Ordinal)), Is.False);
    }

    /// <summary>research-tech-tree.md row 34: an ungifted one-time battle-gift part (the Mini Morph)
    /// can only come from a battle grant, never from research, so it must not be listed.</summary>
    [AvaloniaTest]
    public void Benefits_DoNotListAnUngiftedSpecialPart()
    {
        ClientData client = TestGame.Load();

        Component miniMorph = new AllComponents().Fetch("Mini Morph");
        Assert.That(miniMorph, Is.Not.Null);
        Assert.That(SpecialComponentGrants.IsSpecialGrant(miniMorph.Name), Is.True);

        var research = ResearchTargeting(client, TechLevel.ResearchField.Construction);

        Assert.That(research.Benefits, Is.Not.Empty, "Construction has obtainable parts to list");
        Assert.That(research.Benefits.Any(row => row.Text.StartsWith("Mini Morph", StringComparison.Ordinal)), Is.False);
    }

    /// <summary>research-tech-tree.md row 36: the whole benefits list shows the detail card's
    /// single status line, not just one row (Nova.Client.TechStatusLine).</summary>
    [AvaloniaTest]
    public void Benefits_EveryRowShowsTheStatusLine()
    {
        var research = ResearchTargeting(TestGame.Load(), TechLevel.ResearchField.Construction);

        Assert.That(research.Benefits, Is.Not.Empty);
        Assert.That(research.Benefits.All(row => !string.IsNullOrEmpty(row.StatusText)), Is.True);
    }

    /// <summary>research-tech-tree.md row 36: a part whose still-missing levels are already covered
    /// by banked resources reads "Available" (0 resources needed), never "Unavailable".</summary>
    [AvaloniaTest]
    public void Benefits_ShowAvailableWhenBankedResourcesCoverTheResearch()
    {
        ClientData client = TestGame.Load();
        var research = ResearchTargeting(client, TechLevel.ResearchField.Construction, banked: int.MaxValue / 2);

        Assert.That(research.Benefits, Is.Not.Empty);
        Assert.That(research.Benefits.Any(row => row.StatusText == TechStatusLine.Format(0)), Is.True);
        Assert.That(research.Benefits.Any(row => row.IsUnavailable), Is.False);
    }

    /// <summary>research-tech-tree.md row 36: a part the race can never receive reports the red
    /// "unavailable" line (TechStatusLine's forbidden case).</summary>
    [AvaloniaTest]
    public void UnavailableStatus_IsTheRedUnavailableLine()
    {
        ClientData client = TestGame.Load();
        Component spaceDock = new AllComponents().Fetch("Space Dock");

        int needed = TechStatusLine.ForComponent(spaceDock, client.EmpireState.Race, new TechLevel(0), new TechLevel(0));
        Assert.That(needed, Is.EqualTo(TechStatusLine.Unavailable));

        var row = new ResearchBenefitRowViewModel("Space Dock Hull", Brushes.White, TechStatusLine.Format(needed), true);
        Assert.That(row.StatusText, Is.EqualTo("Unavailable"));
        Assert.That(row.IsUnavailable, Is.True);
        Assert.That(row.StatusColor, Is.SameAs(Brushes.Red));
    }

    /// <summary>A fresh panel previewing <paramref name="field"/> at level 0 with
    /// <paramref name="banked"/> resources banked in it - a known state for the benefits list.</summary>
    private static ResearchViewModel ResearchTargeting(ClientData client, TechLevel.ResearchField field, int banked = 0)
    {
        EmpireData empire = client.EmpireState;
        empire.ResearchLevels[field] = 0;
        empire.ResearchResources[field] = banked;
        empire.ResearchTopics.Zero();
        empire.ResearchTopics[field] = 1;
        return new ResearchViewModel("Research", "Research", client);
    }
}
