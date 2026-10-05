using System.Linq;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Common;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The Messages panel's per-type filter wiring (client-ui-dialog-catalog.md "Messages" row 25).
/// The sibling grouping of types is question 9.16, so the tests only rely on "the current
/// message's own type is filtered" and on each row's own IsTypeFiltered flag, never on which
/// other types share a group.
/// </summary>
[TestFixture]
public class MessagesViewModelTests
{
    private const string NoticeA = "UiTestNoticeA";

    private const string NoticeB = "UiTestNoticeB";

    private static MessagesViewModel Open(out ClientData client)
    {
        client = TestGame.Load();
        client.Messages.Clear();
        client.Messages.Add(new Message(client.EmpireState.Id, "first A", NoticeA, null));
        client.Messages.Add(new Message(client.EmpireState.Id, "first B", NoticeB, null));
        client.Messages.Add(new Message(client.EmpireState.Id, "second A", NoticeA, null));
        client.Messages.Add(new Message(client.EmpireState.Id, "second B", NoticeB, null));
        return new MessagesViewModel("Messages", "Messages", client);
    }

    [AvaloniaTest]
    public void Opens_AllClear_OnTheFirstMessage()
    {
        MessagesViewModel messages = Open(out _);

        Assert.That(messages.Messages, Has.Count.EqualTo(4));
        Assert.That(messages.VisibleMessages, Has.Count.EqualTo(4), "the filter starts all-clear");
        Assert.That(messages.Current?.Text, Is.EqualTo("first A"));
        Assert.That(messages.IsCurrentTypeShown, Is.True);
        Assert.That(messages.IsMagnifierVisible, Is.False, "nothing present is filtered");
        Assert.That(messages.PreviousCommand.CanExecute(null), Is.False);
        Assert.That(messages.NextCommand.CanExecute(null), Is.True);
    }

    [AvaloniaTest]
    public void ToggleFilter_HidesTheType_NextSkipsIt_AndTheMagnifierAppears()
    {
        MessagesViewModel messages = Open(out _);

        messages.ToggleFilterCommand.Execute(null);

        Assert.That(messages.IsCurrentTypeFiltered, Is.True, "the cross shows on the current message");
        Assert.That(messages.Current?.Text, Is.EqualTo("first A"), "the current message stays selected");
        Assert.That(messages.IsMagnifierVisible, Is.True);
        Assert.That(messages.HasFilterSummary, Is.True);
        Assert.That(messages.VisibleMessages.Where(m => m.IsTypeFiltered && !m.IsSelected), Is.Empty, "filtered rows are hidden");
        Assert.That(messages.Messages.Where(m => m.Type == NoticeA).Select(m => m.IsTypeFiltered), Is.All.True);

        messages.NextCommand.Execute(null);
        Assert.That(messages.Current, Is.Not.Null);
        Assert.That(messages.Current!.IsTypeFiltered, Is.False, "Next skips filtered types");
        Assert.That(messages.Current.Type, Is.Not.EqualTo(NoticeA));
    }

    [AvaloniaTest]
    public void Magnifier_SwitchesToShowingFilteredMessages()
    {
        MessagesViewModel messages = Open(out _);
        messages.ToggleFilterCommand.Execute(null);
        Assert.That(messages.ToggleShowFilteredCommand.CanExecute(null), Is.True);

        messages.ToggleShowFilteredCommand.Execute(null);

        Assert.That(messages.ShowFiltered, Is.True);
        Assert.That(messages.VisibleMessages, Has.Count.EqualTo(4), "show mode lists every message");
        Assert.That(messages.Messages.Where(m => m.IsTypeFiltered).Select(m => m.RowOpacity), Is.All.LessThan(1.0), "filtered ones are dimmed");

        messages.ToggleShowFilteredCommand.Execute(null);
        Assert.That(messages.ShowFiltered, Is.False);
    }

    /// <summary>The filter is the player's own and survives re-opening the game (it is saved per
    /// player in the game folder).</summary>
    [AvaloniaTest]
    public void Filter_PersistsForThePlayer()
    {
        MessagesViewModel messages = Open(out ClientData client);
        messages.ToggleFilterCommand.Execute(null);

        var reopened = new MessagesViewModel("Messages", "Messages", client);

        Assert.That(reopened.Messages.Where(m => m.Type == NoticeA).Select(m => m.IsTypeFiltered), Is.All.True);
        Assert.That(reopened.Current?.Type, Is.Not.EqualTo(NoticeA), "in hide mode the first shown message is of an unfiltered type");
    }

    [AvaloniaTest]
    public void SelectingARow_MakesItCurrent()
    {
        MessagesViewModel messages = Open(out _);

        messages.Messages[2].ReplayCommand.Execute(null);

        Assert.That(messages.Current?.Text, Is.EqualTo("second A"));
        Assert.That(messages.PositionText, Does.Contain("3"));
        Assert.That(messages.Messages.Count(m => m.IsSelected), Is.EqualTo(1));
    }

    // ---------------- the four category-selection controls (row 53) ----------------

    private static MessagesViewModel OpenWithTypes(out ClientData client, params string[] types)
    {
        client = TestGame.Load();
        client.Messages.Clear();
        int index = 0;
        foreach (string type in types)
        {
            client.Messages.Add(new Message(client.EmpireState.Id, "text " + index, type, null));
            index++;
        }

        return new MessagesViewModel("Messages", "Messages", client);
    }

    [AvaloniaTest]
    public void Offers_ExactlyFourCategorySelectionControls()
    {
        MessagesViewModel messages = Open(out _);

        Assert.That(messages.CategoryOptions, Has.Count.EqualTo(4), "the spec's four category controls");
        Assert.That(messages.CategoryOptions.Select(o => o.Name), Is.EqualTo(new[] { "All", "Planets", "Fleets", "Other" }));
        Assert.That(messages.CategoryOptions.Count(o => o.IsSelected), Is.EqualTo(1), "exactly one is selected");
        Assert.That(messages.CategoryOptions[0].IsSelected, Is.True, "All is the default view");
        Assert.That(messages.SelectedCategoryIndex, Is.EqualTo(0));
    }

    [AvaloniaTest]
    public void ChangingCategory_RefreshesTheCurrentMessageArea()
    {
        MessagesViewModel messages = Open(out _);
        Assert.That(messages.VisibleMessages, Has.Count.EqualTo(4));

        // These test messages route nowhere, so the category seam puts them in "Other"; the
        // Planets view is empty. (The category identities are the spec-unnamed seam - only the
        // presence of four controls and the refresh are spec-guaranteed.)
        messages.SelectedCategoryIndex = 1;
        Assert.That(messages.VisibleMessages, Is.Empty);
        Assert.That(messages.Current, Is.Null);
        Assert.That(messages.CategoryOptions[1].IsSelected, Is.True);
        Assert.That(messages.NextCommand.CanExecute(null), Is.False);

        messages.SelectedCategoryIndex = 3;
        Assert.That(messages.VisibleMessages, Has.Count.EqualTo(4), "Other holds every unrouted message");
        Assert.That(messages.Current, Is.Not.Null);

        messages.SelectedCategoryIndex = 0;
        Assert.That(messages.VisibleMessages, Has.Count.EqualTo(4));
        Assert.That(messages.Current, Is.Not.Null, "changing back refreshes the current-message area");
    }

    [AvaloniaTest]
    public void TogglingAFilterGroup_HidesAndShowsEverySibling()
    {
        MessagesViewModel messages = OpenWithTypes(out _, "Battle", "Notice", "BattleReport", "BattleSummary");
        Assert.That(messages.Current?.Type, Is.EqualTo("Battle"));

        messages.ToggleFilterCommand.Execute(null);

        Assert.That(messages.Messages.Where(m => m.Type == "BattleReport").Select(m => m.IsTypeFiltered), Is.All.True, "the 145-168 run shares a group");
        Assert.That(messages.Messages.Where(m => m.Type == "BattleSummary").Select(m => m.IsTypeFiltered), Is.All.True);
        Assert.That(messages.Messages.Where(m => m.Type == "Notice").Select(m => m.IsTypeFiltered), Is.All.False, "an unlisted type is its own group");

        messages.NextCommand.Execute(null);
        Assert.That(messages.Current?.Type, Is.EqualTo("Notice"), "Next skips the whole filtered group");
    }
}
