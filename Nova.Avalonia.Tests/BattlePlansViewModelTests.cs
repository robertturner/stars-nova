using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The Battle Plans editor (client-interface.md rows 59-61, 78; race-designer-ui-and-
/// availability.md rows 27/28 "Battle-plan editor"). A deleted plan's fleets move to the plan just
/// above it in the list (BattlePlanRules.Delete; client-interface.md "Battle Plans and Relations
/// dialogs").
/// </summary>
[TestFixture]
public class BattlePlansViewModelTests
{
    private static BattlePlansViewModel Open(out ClientData client)
    {
        client = TestGame.Load();
        return new BattlePlansViewModel("BattlePlans", "Battle Plans", client);
    }

    /// <summary>Row 60 / 27: the first record cannot be removed.</summary>
    [AvaloniaTest]
    public void FirstPlan_CannotBeDeleted()
    {
        BattlePlansViewModel plans = Open(out ClientData client);

        Assert.That(plans.SelectedPlan, Is.SameAs(plans.Plans[0]));
        Assert.That(plans.DeleteCommand.CanExecute(null), Is.False);

        plans.NewPlanCommand.Execute(null);
        Assert.That(plans.DeleteCommand.CanExecute(null), Is.True, "any later plan can be deleted");

        plans.SelectedPlan = plans.Plans[0];
        Assert.That(plans.DeleteCommand.CanExecute(null), Is.False);
    }

    /// <summary>Row 59 / 27: plans can be created up to 15 plus the default (16 records).</summary>
    [AvaloniaTest]
    public void NewPlan_StopsAtTheSixteenRecordLimit()
    {
        BattlePlansViewModel plans = Open(out ClientData client);

        for (int i = 0; i < 40 && plans.NewPlanCommand.CanExecute(null); i++)
        {
            plans.NewPlanCommand.Execute(null);
        }

        Assert.That(client.EmpireState.BattlePlans, Has.Count.EqualTo(16));
        Assert.That(plans.Plans, Has.Count.EqualTo(16));
        Assert.That(plans.NewPlanCommand.CanExecute(null), Is.False);
    }

    /// <summary>Row 28: a new plan copies the active plan, becomes active, and gets a name whose
    /// one-digit "(N)" suffix is incremented with wraparound after nine.</summary>
    [AvaloniaTest]
    public void NewPlan_CopiesTheActivePlan_AndIncrementsItsSuffixWithWraparound()
    {
        BattlePlansViewModel plans = Open(out ClientData client);
        var template = new BattlePlan { Name = "Raid(9)", Tactic = BattlePlan.TacticOptions.Last(), DumpCargo = true };
        client.EmpireState.BattlePlans[template.Name] = template;
        plans = new BattlePlansViewModel("BattlePlans", "Battle Plans", client);
        plans.SelectedPlan = plans.Plans.Single(row => row.Name == "Raid(9)");

        plans.NewPlanCommand.Execute(null);

        Assert.That(plans.SelectedPlan!.Name, Is.EqualTo("Raid(0)"), "the digit wraps after nine");
        Assert.That(plans.SelectedPlan.Tactic, Is.EqualTo(template.Tactic));
        Assert.That(plans.SelectedPlan.DumpCargo, Is.True);

        plans.NewPlanCommand.Execute(null);
        Assert.That(plans.SelectedPlan!.Name, Is.EqualTo("Raid(1)"));
        Assert.That(client.Commands.Peek(), Is.InstanceOf<BattlePlansCommand>(), "the plan list is queued as an order");

        plans.SelectedPlan = plans.Plans[0];
        string first = plans.Plans[0].Name;
        plans.NewPlanCommand.Execute(null);
        Assert.That(plans.SelectedPlan!.Name, Does.StartWith(first).And.Not.EqualTo(first), "a name with no suffix gets one");
    }

    /// <summary>Row 61: deleting a plan still assigned to a fleet asks first; declining changes
    /// nothing; accepting deletes it and leaves no fleet naming it.</summary>
    [AvaloniaTest]
    public void DeleteAssignedPlan_AsksFirst_AndKeepsFleetAssignmentsValid()
    {
        BattlePlansViewModel plans = Open(out ClientData client);
        plans.NewPlanCommand.Execute(null);
        string doomed = plans.SelectedPlan!.Name;
        List<string> orderBefore = client.EmpireState.BattlePlans.Keys.ToList();
        string above = orderBefore[orderBefore.IndexOf(doomed) - 1];
        Fleet fleet = client.EmpireState.OwnedFleets.Values.First(f => !f.IsStarbase);
        fleet.BattlePlan = doomed;

        plans.DeleteCommand.Execute(null);
        Assert.That(plans.IsConfirmingDelete, Is.True, "an assigned plan asks Yes/No");
        Assert.That(plans.ConfirmDeleteText, Does.Contain(doomed));
        Assert.That(client.EmpireState.BattlePlans.ContainsKey(doomed), Is.True);

        plans.CancelDeleteCommand.Execute(null);
        Assert.That(plans.IsConfirmingDelete, Is.False);
        Assert.That(client.EmpireState.BattlePlans.ContainsKey(doomed), Is.True, "declining changes nothing");
        Assert.That(fleet.BattlePlan, Is.EqualTo(doomed));

        plans.DeleteCommand.Execute(null);
        plans.ConfirmDeleteCommand.Execute(null);

        Assert.That(client.EmpireState.BattlePlans.ContainsKey(doomed), Is.False);
        Assert.That(fleet.BattlePlan, Is.EqualTo(above), "the fleet moved to the plan just above the deleted one");
        Assert.That(client.EmpireState.BattlePlans.ContainsKey(fleet.BattlePlan), Is.True, "the fleet names a plan that exists");
        Assert.That(plans.Plans.Select(row => row.Name), Has.No.Member(doomed));
    }

    [AvaloniaTest]
    public void DeleteUnusedPlan_GoesAtOnce()
    {
        BattlePlansViewModel plans = Open(out ClientData client);
        plans.NewPlanCommand.Execute(null);
        string unused = plans.SelectedPlan!.Name;

        plans.DeleteCommand.Execute(null);

        Assert.That(plans.IsConfirmingDelete, Is.False);
        Assert.That(client.EmpireState.BattlePlans.ContainsKey(unused), Is.False);
    }

    /// <summary>Row 78 / 61: a rename is committed on accept, keeps the plan's position and
    /// follows the fleets that use it.</summary>
    [AvaloniaTest]
    public void Rename_CommitsOnAccept_KeepsPosition_AndFollowsTheFleets()
    {
        BattlePlansViewModel plans = Open(out ClientData client);
        plans.NewPlanCommand.Execute(null);
        plans.NewPlanCommand.Execute(null);
        BattlePlanRowViewModel middle = plans.Plans[1];
        plans.SelectedPlan = middle;
        string oldName = middle.Name;
        Fleet fleet = client.EmpireState.OwnedFleets.Values.First(f => !f.IsStarbase);
        fleet.BattlePlan = oldName;

        middle.EditName = "Picket";
        Assert.That(client.EmpireState.BattlePlans.ContainsKey(oldName), Is.True, "typing does not rename");
        plans.RenameCommand.Execute(null);

        Assert.That(client.EmpireState.BattlePlans.Keys.ElementAt(1), Is.EqualTo("Picket"), "the plan keeps its place in the list");
        Assert.That(client.EmpireState.BattlePlans.ContainsKey(oldName), Is.False);
        Assert.That(fleet.BattlePlan, Is.EqualTo("Picket"));
    }

    /// <summary>Editing a plan's targeting writes through and queues the plans.</summary>
    [AvaloniaTest]
    public void EditingAPlan_WritesThrough_AndQueuesTheOrder()
    {
        BattlePlansViewModel plans = Open(out ClientData client);
        BattlePlanRowViewModel row = plans.Plans[0];
        string tactic = row.TacticOptions.First(option => option != row.Tactic);
        int commandsBefore = client.Commands.Count;

        row.Tactic = tactic;

        Assert.That(row.Plan.Tactic, Is.EqualTo(tactic));
        Assert.That(client.Commands.Count, Is.GreaterThan(commandsBefore));
        Assert.That(client.Commands.Peek(), Is.InstanceOf<BattlePlansCommand>());
    }
}
