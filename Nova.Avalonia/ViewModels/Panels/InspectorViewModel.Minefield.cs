using System;
using System.Collections.Generic;
using System.Linq;
using Nova.Client;
using Nova.Common;
using Nova.Common.Commands;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// The minefield half of the Inspector, and the selected fleet's battle-plan assignment:
/// - the read-only field rows (Nova.Client.MinefieldDisplay). behavior-specs-11 retracts the
///   old Field/Transit display selector: "The pane has no display-option selector. Its only child
///   control is one auto-checkbox ... (detonate this minefield next year)."
/// - the Detonate order for an owned Space Demolition standard field (DetonateCommand), queued
///   like every other order and mirrored on the client's own copy of the field;
/// - the fleet's battle plan (a BattlePlansCommand carries the plans and assignments).
/// </summary>
public partial class InspectorViewModel
{
    private Minefield? selectedMinefield;

    private bool isMinefieldSelected;

    public bool IsMinefieldSelected
    {
        get => isMinefieldSelected;
        private set => SetProperty(ref isMinefieldSelected, value);
    }

    private bool canDetonateMinefield;

    /// <summary>The selected field is this empire's own standard field and the empire is Space
    /// Demolition (DetonateCommand.CanDetonate).</summary>
    public bool CanDetonateMinefield
    {
        get => canDetonateMinefield;
        private set => SetProperty(ref canDetonateMinefield, value);
    }

    private bool minefieldDetonate;

    /// <summary>The field's detonate flag; changing it queues a DetonateCommand.</summary>
    public bool MinefieldDetonate
    {
        get => minefieldDetonate;
        set
        {
            if (minefieldDetonate == value)
            {
                return;
            }

            if (selectedMinefield == null || !CanDetonateMinefield)
            {
                OnPropertyChanged();
                return;
            }

            minefieldDetonate = value;
            var command = new DetonateCommand(selectedMinefield.Key, value);
            clientState.Commands.Push(command);
            command.ApplyToMinefields(clientState.InputTurn.AllMinefields, clientState.EmpireState);

            // The selected object may be a different copy of the same field than the turn's.
            if (!ReferenceEquals(clientState.InputTurn.AllMinefields.GetValueOrDefault(selectedMinefield.Key), selectedMinefield))
            {
                selectedMinefield.Detonate = value;
            }

            OnPropertyChanged();
            ShowMinefieldDetails(selectedMinefield);
            selection.NotifyMutated();
        }
    }

    private void ShowMinefieldDetails(Minefield minefield)
    {
        selectedMinefield = minefield;
        IsMinefieldSelected = true;

        string owner = minefield.Owner == clientState.EmpireState.Id
            ? "You"
            : clientState.EmpireState.EmpireReports.TryGetValue(minefield.Owner, out EmpireIntel intel)
                ? intel.RaceName
                : $"Empire #{minefield.Owner}";

        Rows = MinefieldDisplay.Rows(minefield, owner)
            .Select(row => new InspectorRow(row.Key, row.Value))
            .ToList();

        CanDetonateMinefield = DetonateCommand.CanDetonate(minefield, clientState.EmpireState);
        if (minefieldDetonate != minefield.Detonate)
        {
            minefieldDetonate = minefield.Detonate;
            OnPropertyChanged(nameof(MinefieldDetonate));
        }
    }

    private void ClearMinefieldDetails()
    {
        selectedMinefield = null;
        IsMinefieldSelected = false;
        CanDetonateMinefield = false;
    }

    // ---------------------------------------------------------------------------------
    // The selected fleet's battle plan.
    // ---------------------------------------------------------------------------------

    private IReadOnlyList<string> fleetBattlePlanOptions = Array.Empty<string>();

    public IReadOnlyList<string> FleetBattlePlanOptions
    {
        get => fleetBattlePlanOptions;
        private set => SetProperty(ref fleetBattlePlanOptions, value);
    }

    private string? selectedFleetBattlePlan;

    /// <summary>The selected fleet's plan; changing it queues the plans and assignments.</summary>
    public string? SelectedFleetBattlePlan
    {
        get => selectedFleetBattlePlan;
        set
        {
            if (value == null || value == selectedFleetBattlePlan)
            {
                return;
            }

            selectedFleetBattlePlan = value;
            OnPropertyChanged();

            if (selectedFleet != null && selectedFleet.BattlePlan != value && clientState.EmpireState.BattlePlans.ContainsKey(value))
            {
                selectedFleet.BattlePlan = value;
                BattlePlanOrders.Queue(clientState);
                selection.NotifyMutated();
            }
        }
    }

    private void RefreshFleetBattlePlan(Fleet fleet)
    {
        FleetBattlePlanOptions = clientState.EmpireState.BattlePlans.Keys.ToList();
        string current = fleet.BattlePlan != null && clientState.EmpireState.BattlePlans.ContainsKey(fleet.BattlePlan)
            ? fleet.BattlePlan
            : FleetBattlePlanOptions.FirstOrDefault() ?? string.Empty;
        selectedFleetBattlePlan = current;
        OnPropertyChanged(nameof(SelectedFleetBattlePlan));
    }

    private void ClearFleetBattlePlan()
    {
        FleetBattlePlanOptions = Array.Empty<string>();
        selectedFleetBattlePlan = null;
        OnPropertyChanged(nameof(SelectedFleetBattlePlan));
    }
}
