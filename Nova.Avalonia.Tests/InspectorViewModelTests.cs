using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.NUnit;
using Nova.Avalonia.ViewModels;
using Nova.Avalonia.ViewModels.Panels;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;
using Nova.Common.DataStructures;
using Nova.Common.Waypoints;
using NUnit.Framework;

namespace Nova.Avalonia.Tests;

/// <summary>
/// The Inspector's fleet orders, cargo, split/merge, rename, minefield and packet controls against
/// a real generated game (client-interface.md rows 11, 73, 74, 76-78; fleet-movement pursuit,
/// Repeat Orders, Patrol and Detonate; production-queue packet destination). Tests do not pin the
/// spec-gap stand-ins of behavior-specs-10-questions.md: where a new waypoint goes relative to
/// the selected one (9.1), which neighbour stays selected after a delete (9.2), what Repeat
/// Orders recycles (5.11) or which warp Patrol uses (5.12).
/// </summary>
[TestFixture]
public class InspectorViewModelTests
{
    private sealed class Context
    {
        public Context(string race = TestGame.PacketRace)
        {
            Client = TestGame.Load(race);
            Selection = new SelectionService();
            Inspector = new InspectorViewModel("Inspector", "Inspector", Client, Selection);
            Home = TestGame.HomeStar(Client);
        }

        public ClientData Client { get; }

        public SelectionService Selection { get; }

        public InspectorViewModel Inspector { get; }

        public Star Home { get; }

        public List<Fleet> HomeFleets => TestGame.FleetsAt(Client, Home);

        public List<StarIntel> OtherStars => Client.EmpireState.StarReports.Values.Where(r => r.Name != Home.Name).ToList();

        /// <summary>Arms "add waypoint" for the selected fleet and taps <paramref name="target"/>.</summary>
        public void AddWaypointAt(Mappable target)
        {
            Inspector.ArmMapWaypointCommand.Execute(null);
            Assert.That(Selection.TryConsumeWaypointTarget(target), Is.True, "the armed tap was consumed");
        }
    }

    /// <summary>Row 11: Add Waypoint is enabled only while one of the empire's own fleets is selected.</summary>
    [AvaloniaTest]
    public void ArmWaypoint_IsEnabledOnlyForAnOwnFleet()
    {
        var context = new Context();
        Assert.That(context.Inspector.ArmMapWaypointCommand.CanExecute(null), Is.False);

        context.Selection.Selected = context.Home;
        Assert.That(context.Inspector.ArmMapWaypointCommand.CanExecute(null), Is.False);
        Assert.That(context.Inspector.IsFleetSelected, Is.False);

        context.Selection.Selected = context.HomeFleets[0];
        Assert.That(context.Inspector.ArmMapWaypointCommand.CanExecute(null), Is.True);
        Assert.That(context.Inspector.IsFleetSelected, Is.True);
    }

    /// <summary>A tapped planet becomes a fixed waypoint, queued as an order; the map stays on the fleet.</summary>
    [AvaloniaTest]
    public void AddWaypoint_OnAPlanet_AppendsAFixedWaypointAndQueuesTheOrder()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        context.Selection.Selected = fleet;
        StarIntel target = context.OtherStars[0];
        int commandsBefore = context.Client.Commands.Count;

        context.AddWaypointAt(target);

        Assert.That(context.Selection.IsAddingWaypoint, Is.False, "one tap disarms");
        Assert.That(context.Selection.Selected, Is.SameAs(fleet), "the fleet stays selected");
        Assert.That(fleet.Waypoints, Has.Count.EqualTo(2));
        Assert.That(fleet.Waypoints[1].Destination, Is.EqualTo(target.Name));
        Assert.That(fleet.Waypoints[1].Position, Is.EqualTo(target.Position));
        Assert.That(fleet.Waypoints[1].IsFleetTarget, Is.False, "a planet is a fixed point, not a pursuit");
        Assert.That(context.Client.Commands.Count, Is.GreaterThan(commandsBefore));
        Assert.That(context.Client.Commands.Peek(), Is.InstanceOf<WaypointCommand>());
        Assert.That(context.Inspector.WaypointRows, Has.Count.EqualTo(1));
        Assert.That(context.Inspector.WaypointRows[0].Destination, Is.EqualTo(target.Name));
    }

    /// <summary>fleet-movement-scanning-cargo.md section 5 "Pursuit": a waypoint placed on another
    /// fleet aims at that fleet; placing one on the fleet itself does not.</summary>
    [AvaloniaTest]
    public void AddWaypoint_OnAnotherFleet_MakesAPursuit()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        Fleet quarry = context.HomeFleets[1];
        context.Selection.Selected = fleet;

        context.AddWaypointAt(quarry);

        Waypoint added = fleet.Waypoints.Last();
        Assert.That(added.IsFleetTarget, Is.True);
        Assert.That(added.TargetFleetKey, Is.EqualTo(quarry.Key));
        Assert.That(context.Inspector.WaypointRows.Last().HasTargetNote, Is.True, "the route list says who is being chased");

        context.AddWaypointAt(fleet);
        Assert.That(fleet.Waypoints.Last().IsFleetTarget, Is.False, "a fleet never pursues itself");
    }

    /// <summary>A starbase cannot move, so a waypoint on one stays a fixed point.</summary>
    [AvaloniaTest]
    public void AddWaypoint_OnAStarbase_IsNotAPursuit()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        Assume.That(context.Home.Starbase, Is.Not.Null);
        context.Selection.Selected = fleet;

        context.AddWaypointAt(context.Home.Starbase!);

        Assert.That(fleet.Waypoints.Last().IsFleetTarget, Is.False);
    }

    /// <summary>Repeat Orders: the toggle reflects the fleet, and changing it queues the order.</summary>
    [AvaloniaTest]
    public void RepeatOrders_ReflectsTheFleet_AndQueuesARepeatOrdersCommand()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        context.Selection.Selected = fleet;
        Assert.That(context.Inspector.RepeatOrders, Is.EqualTo(fleet.RepeatOrders));
        int commandsBefore = context.Client.Commands.Count;

        context.Inspector.RepeatOrders = true;

        Assert.That(fleet.RepeatOrders, Is.True);
        Assert.That(context.Client.Commands.Count, Is.EqualTo(commandsBefore + 1));
        Assert.That(context.Client.Commands.Peek(), Is.InstanceOf<RepeatOrdersCommand>());

        // Re-selecting the fleet loads the flag without echoing it as a new order.
        context.Selection.Selected = context.Home;
        context.Selection.Selected = fleet;
        Assert.That(context.Inspector.RepeatOrders, Is.True);
        Assert.That(context.Client.Commands.Count, Is.EqualTo(commandsBefore + 1));
    }

    /// <summary>Patrol carries its speed and range settings, for a new waypoint and for an
    /// already-queued one.</summary>
    [AvaloniaTest]
    public void PatrolWaypoint_CarriesItsSpeedAndRange_AndCanBeEditedInPlace()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        context.Selection.Selected = fleet;

        context.Inspector.NewWaypointTask = WaypointOrders.PatrolOption;
        Assert.That(context.Inspector.IsNewWaypointPatrol, Is.True, "the Patrol settings show for a Patrol waypoint");
        context.Inspector.NewPatrolSpeed = 3;
        context.Inspector.NewPatrolRangeIndex = 2;
        context.AddWaypointAt(context.OtherStars[0]);

        PatrolTask patrol = (PatrolTask)fleet.Waypoints[1].Task;
        Assert.That(patrol.Speed, Is.EqualTo(3));
        Assert.That(patrol.RangeIndex, Is.EqualTo(2));

        context.Inspector.WaypointRows[0].SelectCommand.Execute(null);
        Assert.That(context.Inspector.IsSelectedWaypointPatrol, Is.True);
        Assert.That(context.Inspector.SelectedPatrolSpeed, Is.EqualTo(3));

        context.Inspector.SelectedPatrolRangeIndex = 5;
        Assert.That(((PatrolTask)fleet.Waypoints[1].Task).RangeIndex, Is.EqualTo(5));

        context.Inspector.SelectedWaypointTaskOption = "None";
        Assert.That(fleet.Waypoints[1].Task, Is.Not.InstanceOf<PatrolTask>());
        Assert.That(context.Inspector.IsSelectedWaypointPatrol, Is.False);
    }

    /// <summary>Row 73: Move Up / Move Down reorder the pending waypoints (waypoint 0, the current
    /// position, never moves).</summary>
    [AvaloniaTest]
    public void MoveUpAndDown_ReorderThePendingWaypoints()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        context.Selection.Selected = fleet;
        context.AddWaypointAt(context.OtherStars[0]);
        context.AddWaypointAt(context.OtherStars[1]);
        string first = fleet.Waypoints[1].Destination;
        string second = fleet.Waypoints[2].Destination;

        Assert.That(context.Inspector.WaypointRows[0].MoveUpCommand.CanExecute(null), Is.False, "the first pending waypoint cannot move above the current position");
        context.Inspector.WaypointRows[1].MoveUpCommand.Execute(null);

        Assert.That(fleet.Waypoints[1].Destination, Is.EqualTo(second));
        Assert.That(fleet.Waypoints[2].Destination, Is.EqualTo(first));

        context.Inspector.WaypointRows[0].MoveDownCommand.Execute(null);
        Assert.That(fleet.Waypoints[1].Destination, Is.EqualTo(first));
    }

    /// <summary>Row 74: Delete removes the selected waypoint and the selection stays on a
    /// neighbouring waypoint (which neighbour is question 9.2, so either is accepted).</summary>
    [AvaloniaTest]
    public void DeleteSelectedWaypoint_RemovesIt_AndKeepsANeighbourSelected()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        context.Selection.Selected = fleet;
        for (int i = 0; i < 3; i++)
        {
            context.AddWaypointAt(context.OtherStars[i]);
        }

        string deleted = fleet.Waypoints[2].Destination;
        string before = fleet.Waypoints[1].Destination;
        string after = fleet.Waypoints[3].Destination;
        context.Inspector.WaypointRows[1].SelectCommand.Execute(null);
        Assert.That(context.Inspector.HasSelectedWaypoint, Is.True);

        context.Inspector.DeleteSelectedWaypointCommand.Execute(null);

        Assert.That(fleet.Waypoints.Select(w => w.Destination), Has.No.Member(deleted));
        Assert.That(fleet.Waypoints, Has.Count.EqualTo(3));
        Assert.That(context.Inspector.HasSelectedWaypoint, Is.True, "the selection moves to a neighbour");
        FleetWaypointRowViewModel selected = context.Inspector.WaypointRows.Single(row => row.IsSelected);
        Assert.That(selected.Destination, Is.EqualTo(before).Or.EqualTo(after));
        Assert.That(context.Client.Commands.Peek(), Is.InstanceOf<WaypointCommand>());
    }

    /// <summary>Delete with no waypoint selected does nothing.</summary>
    [AvaloniaTest]
    public void DeleteSelectedWaypoint_WithNothingSelected_DoesNothing()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        context.Selection.Selected = fleet;
        context.AddWaypointAt(context.OtherStars[0]);

        context.Inspector.DeleteSelectedWaypointCommand.Execute(null);

        Assert.That(fleet.Waypoints, Has.Count.EqualTo(2));
    }

    /// <summary>Row 76 / dialog catalog row 11: the planet cargo editor clamps every category to
    /// the fleet's hold, shared across the categories, and applies the transfer.</summary>
    [AvaloniaTest]
    public void CargoTransfer_IsCapacityClamped_AndApplies()
    {
        var context = new Context();
        Fleet? hauler = context.HomeFleets.FirstOrDefault(f => f.TotalCargoCapacity > 0);
        Assume.That(hauler, Is.Not.Null, "a starting fleet with a cargo hold");
        context.Selection.Selected = hauler;

        Assert.That(context.Inspector.CanTransferCargo, Is.True, "an orbiting fleet can trade with the planet");
        IReadOnlyList<CargoResourceRowViewModel> rows = context.Inspector.CargoRows;
        Assert.That(rows, Has.Count.EqualTo(4));

        rows[0].FleetAmount = int.MaxValue;
        rows[1].FleetAmount = int.MaxValue;
        Assert.That(rows.Sum(row => row.FleetAmount), Is.LessThanOrEqualTo(hauler!.TotalCargoCapacity), "the hold is shared by every category");
        Assert.That(rows[0].FleetAmount, Is.LessThanOrEqualTo(rows[0].Total));
        Assert.That(rows[0].PlanetAmount, Is.EqualTo(rows[0].Total - rows[0].FleetAmount));

        int wanted = rows[0].FleetAmount;
        Assume.That(wanted, Is.GreaterThan(0), "the planet has ironium to load");
        context.Inspector.ApplyCargoCommand.Execute(null);

        Assert.That(hauler.Cargo.Ironium, Is.EqualTo(wanted));
        Assert.That(context.Inspector.CargoStatusMessage, Is.Not.Empty);
    }

    /// <summary>Row 77: two fleets together can be merged: every ship ends in one surviving fleet,
    /// the other disappears and the survivor is shown. (Which of the two survives is not
    /// specified, so either is accepted.)</summary>
    [AvaloniaTest]
    public void SplitMerge_FullMergeLeavesOneFleetWithEveryShip()
    {
        var context = new Context();
        Fleet source = context.HomeFleets[0];
        Fleet target = context.HomeFleets[1];
        int totalShips = source.Composition.Values.Sum(token => token.Quantity) + target.Composition.Values.Sum(token => token.Quantity);
        context.Selection.Selected = source;

        Assert.That(context.Inspector.CanSplitMerge, Is.True);
        SplitMergeTargetOption option = context.Inspector.SplitMergeTargets.Single(t => t.Fleet == target);
        Assert.That(context.Inspector.SplitMergeTargets.Any(t => t.IsNewFleet), Is.True, "a split into a new fleet is offered too");
        context.Inspector.SelectedSplitMergeTarget = option;
        foreach (SplitMergeRowViewModel row in context.Inspector.SplitMergeRows)
        {
            row.KeepInSource = 0;
        }

        context.Inspector.ApplySplitMergeCommand.Execute(null);

        Fleet[] survivors = new[] { source, target }.Where(f => context.Client.EmpireState.OwnedFleets.ContainsKey(f.Key)).ToArray();
        Assert.That(survivors, Has.Length.EqualTo(1), "exactly one of the two fleets remains");
        Assert.That(survivors[0].Composition.Values.Sum(token => token.Quantity), Is.EqualTo(totalShips), "no ship is lost");
        Assert.That(context.Selection.Selected, Is.SameAs(survivors[0]), "the survivor is shown");
    }

    /// <summary>
    /// BUG (not fixed here - needs a change to Common's SplitMergeTask): moving only SOME ships to
    /// an existing fleet on the Split/Merge tab. The tab builds a SplitMergeTask with the
    /// per-design counts and the target's key, but SplitMergeTask.Perform's merge branch
    /// (OtherFleetKey != 0) ignores the counts and merges the whole TARGET fleet into the source
    /// - the counts are lost and a partial transfer becomes a full merge in the other direction.
    /// </summary>
    [AvaloniaTest]
    [Ignore("BUG: SplitMergeTask.Perform ignores the Split/Merge tab's counts when the target is an existing fleet")]
    public void SplitMerge_PartialMoveToAnExistingFleet_MovesOnlyTheChosenShips()
    {
        var context = new Context();
        Fleet source = context.HomeFleets[0];
        Fleet target = context.HomeFleets[1];
        source.Composition.Values.First().Quantity = 2;
        int targetShips = target.Composition.Values.Sum(token => token.Quantity);
        context.Selection.Selected = source;
        context.Inspector.SelectedSplitMergeTarget = context.Inspector.SplitMergeTargets.Single(t => t.Fleet == target);
        SplitMergeRowViewModel row = context.Inspector.SplitMergeRows.Single();
        row.KeepInSource = 1;

        context.Inspector.ApplySplitMergeCommand.Execute(null);

        Assert.That(source.Composition.Values.Sum(token => token.Quantity), Is.EqualTo(1), "one ship stays");
        Assert.That(target.Composition.Values.Sum(token => token.Quantity), Is.EqualTo(targetShips + 1), "one ship moves");
    }

    /// <summary>Row 78: renaming a fleet is committed on accept.</summary>
    [AvaloniaTest]
    public void Rename_CommitsTheNewFleetName()
    {
        var context = new Context();
        Fleet fleet = context.HomeFleets[0];
        context.Selection.Selected = fleet;

        context.Inspector.NewFleetName = "Pathfinder";
        Assert.That(fleet.Name, Is.Not.EqualTo("Pathfinder"), "typing alone renames nothing");
        context.Inspector.SubmitRenameCommand.Execute(null);

        Assert.That(fleet.Name, Is.EqualTo("Pathfinder"));
        Assert.That(context.Inspector.Name, Is.EqualTo("Pathfinder"));
    }

    /// <summary>The fleet's battle plan is chosen from the empire's plans and queued as an order.</summary>
    [AvaloniaTest]
    public void FleetBattlePlan_ListsThePlans_AndQueuesTheAssignment()
    {
        var context = new Context();
        context.Client.EmpireState.BattlePlans["Skirmish"] = new BattlePlan { Name = "Skirmish" };
        Fleet fleet = context.HomeFleets[0];
        context.Selection.Selected = fleet;

        Assert.That(context.Inspector.FleetBattlePlanOptions, Does.Contain("Skirmish"));
        context.Inspector.SelectedFleetBattlePlan = "Skirmish";

        Assert.That(fleet.BattlePlan, Is.EqualTo("Skirmish"));
        Assert.That(context.Client.Commands.Peek(), Is.InstanceOf<BattlePlansCommand>());
    }

    /// <summary>An owned Space Demolition standard minefield offers Detonate; setting it queues
    /// a DetonateCommand and marks the field.</summary>
    [AvaloniaTest]
    public void Minefield_SpaceDemolitionCanDetonateItsOwnStandardField()
    {
        var context = new Context(TestGame.DemolitionRace);
        Minefield field = AddOwnMinefield(context);
        context.Selection.Selected = field;

        Assert.That(context.Inspector.IsMinefieldSelected, Is.True);
        Assert.That(context.Inspector.Kind, Is.EqualTo("Minefield"));
        Assert.That(context.Inspector.CanDetonateMinefield, Is.True);

        context.Inspector.MinefieldDetonate = true;

        Assert.That(field.Detonate, Is.True);
        Assert.That(context.Client.Commands.Peek(), Is.InstanceOf<DetonateCommand>());
    }

    /// <summary>Any other race's own field offers no Detonate, and the flag cannot be set.</summary>
    [AvaloniaTest]
    public void Minefield_NonSpaceDemolitionCannotDetonate()
    {
        var context = new Context(TestGame.PacketRace);
        Minefield field = AddOwnMinefield(context);
        context.Selection.Selected = field;
        int commandsBefore = context.Client.Commands.Count;

        Assert.That(context.Inspector.CanDetonateMinefield, Is.False);
        context.Inspector.MinefieldDetonate = true;

        Assert.That(field.Detonate, Is.False);
        Assert.That(context.Inspector.MinefieldDetonate, Is.False);
        Assert.That(context.Client.Commands.Count, Is.EqualTo(commandsBefore));
    }

    /// <summary>A planet whose starbase carries a mass driver takes a packet destination (a
    /// queued order); a planet without one shows no packet controls.</summary>
    [AvaloniaTest]
    public void PacketDestination_SetOnAMassDriverPlanet_QueuesTheOrder()
    {
        var context = new Context(TestGame.PacketRace);
        Star? driverPlanet = context.Client.EmpireState.OwnedStars.Values.FirstOrDefault(PacketOrders.CanSetDestination);
        Assume.That(driverPlanet, Is.Not.Null, "a Packet Physics empire starts with a mass driver");
        context.Selection.Selected = driverPlanet;

        Assert.That(context.Inspector.CanSetPacketDestination, Is.True);
        Assert.That(context.Inspector.PacketDestinationChoices, Does.Contain(PacketOrders.NoDestination));
        Assert.That(context.Inspector.PacketDestinationChoices, Has.No.Member(driverPlanet!.Name), "a planet cannot target itself");
        Assert.That(context.Inspector.PacketSpeedChoices, Is.Not.Empty);

        string destination = context.Inspector.PacketDestinationChoices.First(choice => choice != PacketOrders.NoDestination);
        context.Inspector.SelectedPacketDestination = destination;

        Assert.That(driverPlanet.PacketDestination, Is.EqualTo(destination));
        Assert.That(context.Inspector.HasPacketDestination, Is.True);
        Assert.That(context.Client.Commands.Peek(), Is.InstanceOf<PacketDestinationCommand>());
        Assert.That(context.Inspector.Rows.Select(row => row.Value), Has.Some.Contains(destination));

        context.Inspector.SelectedPacketDestination = PacketOrders.NoDestination;
        Assert.That(context.Inspector.HasPacketDestination, Is.False, "(none) clears the destination");
    }

    [AvaloniaTest]
    public void PacketControls_HiddenOnAPlanetWithoutAMassDriver()
    {
        var context = new Context(TestGame.DemolitionRace);
        Star? plain = context.Client.EmpireState.OwnedStars.Values.FirstOrDefault(star => !PacketOrders.CanSetDestination(star));
        Assume.That(plain, Is.Not.Null);
        context.Selection.Selected = plain;

        Assert.That(context.Inspector.CanSetPacketDestination, Is.False);
    }

    private static Minefield AddOwnMinefield(Context context)
    {
        var field = new Minefield
        {
            Owner = context.Client.EmpireState.Id,
            Id = 900,
            NumberOfMines = 400,
            FieldType = MinefieldType.Standard,
        };
        field.Position = new NovaPoint(context.Home.Position);
        context.Client.InputTurn.AllMinefields[field.Key] = field;
        return field;
    }
}
