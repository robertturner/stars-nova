using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Battle Plans editor - ports the now-working BattlePlans.cs. Edits EmpireData.BattlePlans in
/// place and, after every change, queues the whole plan list with every fleet's assignment as
/// one BattlePlansCommand (Nova.Client.BattlePlanOrders) - before that command existed nothing
/// here ever reached the server.
/// behavior-specs-10/client-interface.md "Battle Plans and Relations dialogs":
/// - the first record cannot be removed (Delete is disabled on it);
/// - deleting a plan still assigned to a fleet first asks Yes/No (an inline confirmation, the
///   same arm-then-confirm shape the mobile shell's Close Game uses); declining changes nothing;
///   accepting moves those fleets to the plan just above the deleted one
///   (Nova.Client.BattlePlanRules.Delete) so no fleet is left naming a plan that no longer exists;
/// - renaming is committed on accept and follows the fleets (BattlePlanRules.Rename).
/// The dictionary's insertion order is the list order (Plans[0] is the protected "Default"
/// record EmpireData's constructor adds first); BattlePlanRules.Rename rebuilds the dictionary
/// in order so a rename never moves a plan.
/// </summary>
public class BattlePlansViewModel : Tool
{
    private readonly ClientData clientState;
    private readonly Dictionary<string, BattlePlan> battlePlans;

    private IReadOnlyList<BattlePlanRowViewModel> plans = new List<BattlePlanRowViewModel>();

    public IReadOnlyList<BattlePlanRowViewModel> Plans
    {
        get => plans;
        private set => SetProperty(ref plans, value);
    }

    private BattlePlanRowViewModel? selectedPlan;

    public BattlePlanRowViewModel? SelectedPlan
    {
        get => selectedPlan;
        set
        {
            if (SetProperty(ref selectedPlan, value))
            {
                IsConfirmingDelete = false;
                DeleteCommand.NotifyCanExecuteChanged();
                RenameCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string statusMessage = "";

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
            {
                HasStatusMessage = !string.IsNullOrEmpty(value);
            }
        }
    }

    private bool hasStatusMessage;

    public bool HasStatusMessage
    {
        get => hasStatusMessage;
        private set => SetProperty(ref hasStatusMessage, value);
    }

    private bool isConfirmingDelete;

    /// <summary>True while the "still assigned - delete anyway?" Yes/No is showing.</summary>
    public bool IsConfirmingDelete
    {
        get => isConfirmingDelete;
        private set => SetProperty(ref isConfirmingDelete, value);
    }

    private string confirmDeleteText = "";

    public string ConfirmDeleteText
    {
        get => confirmDeleteText;
        private set => SetProperty(ref confirmDeleteText, value);
    }

    public IRelayCommand NewPlanCommand { get; }

    public IRelayCommand DeleteCommand { get; }

    public IRelayCommand ConfirmDeleteCommand { get; }

    public IRelayCommand CancelDeleteCommand { get; }

    public IRelayCommand RenameCommand { get; }

    public BattlePlansViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        battlePlans = clientState.EmpireState.BattlePlans;

        NewPlanCommand = new RelayCommand(CreatePlan, () => battlePlans.Count < Global.MaxBattlePlans);
        DeleteCommand = new RelayCommand(RequestDelete, CanDelete);
        ConfirmDeleteCommand = new RelayCommand(() => DeleteSelected(confirmed: true));
        CancelDeleteCommand = new RelayCommand(() =>
        {
            IsConfirmingDelete = false;
            StatusMessage = "Not deleted.";
        });
        RenameCommand = new RelayCommand(RenameSelected, () => SelectedPlan != null);

        RebuildPlans();
        SelectedPlan = Plans.FirstOrDefault();
    }

    private IEnumerable<Fleet> OwnFleets => clientState.EmpireState.OwnedFleets.Values;

    private bool CanDelete()
    {
        return SelectedPlan != null && BattlePlanRules.CanDelete(battlePlans, SelectedPlan.Name);
    }

    private void RebuildPlans()
    {
        Plans = battlePlans.Values
            .Select(plan => new BattlePlanRowViewModel(plan, OnPlanEdited))
            .ToList();
        NewPlanCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void OnPlanEdited()
    {
        BattlePlanOrders.Queue(clientState);
    }

    private void RenameSelected()
    {
        if (SelectedPlan == null)
        {
            return;
        }

        string oldName = SelectedPlan.Name;
        string? error = BattlePlanRules.Rename(battlePlans, oldName, SelectedPlan.EditName, OwnFleets);
        SelectedPlan.NameCommitted();
        if (error != null)
        {
            StatusMessage = error;
            return;
        }

        if (SelectedPlan.Name != oldName)
        {
            BattlePlanOrders.Queue(clientState);
            StatusMessage = $"Renamed \"{oldName}\" to \"{SelectedPlan.Name}\".";
        }
    }

    /// <summary>Copies the active plan as a template, auto-incrementing a trailing "(N)" numeric
    /// suffix with wraparound after nine (or appending one if there isn't one) - mirrors
    /// BattlePlans.cs's NextPlanName exactly.</summary>
    private void CreatePlan()
    {
        if (battlePlans.Count >= Global.MaxBattlePlans)
        {
            return;
        }

        BattlePlan template = SelectedPlan?.Plan ?? battlePlans.Values.First();
        var copy = new BattlePlan
        {
            Name = NextPlanName(template.Name),
            PrimaryTarget = template.PrimaryTarget,
            SecondaryTarget = template.SecondaryTarget,
            Tactic = template.Tactic,
            Attack = template.Attack,
            TargetId = template.TargetId,
            DumpCargo = template.DumpCargo,
        };

        battlePlans[copy.Name] = copy;
        RebuildPlans();
        SelectedPlan = Plans.FirstOrDefault(row => ReferenceEquals(row.Plan, copy));
        BattlePlanOrders.Queue(clientState);
        StatusMessage = $"Created \"{copy.Name}\".";
    }

    /// <summary>Delete: an unused plan goes at once, an assigned one asks first.</summary>
    private void RequestDelete()
    {
        if (!CanDelete())
        {
            return;
        }

        int users = BattlePlanRules.FleetsUsing(SelectedPlan!.Name, OwnFleets).Count;
        if (users == 0)
        {
            DeleteSelected(confirmed: false);
            return;
        }

        ConfirmDeleteText = $"\"{SelectedPlan.Name}\" is assigned to {users} fleet(s). Delete it anyway? They will use \"{BattlePlanRules.PlanAbove(battlePlans, SelectedPlan.Name)}\".";
        IsConfirmingDelete = true;
    }

    private void DeleteSelected(bool confirmed)
    {
        IsConfirmingDelete = false;
        if (!CanDelete())
        {
            return;
        }

        string name = SelectedPlan!.Name;
        if (!confirmed && BattlePlanRules.DeleteNeedsConfirmation(name, OwnFleets))
        {
            return;
        }

        string above = BattlePlanRules.PlanAbove(battlePlans, name);
        List<Fleet> moved = BattlePlanRules.Delete(battlePlans, name, OwnFleets);
        RebuildPlans();
        SelectedPlan = Plans.FirstOrDefault();
        BattlePlanOrders.Queue(clientState);
        StatusMessage = moved.Count > 0
            ? $"Deleted \"{name}\"; {moved.Count} fleet(s) now use \"{above}\"."
            : $"Deleted \"{name}\".";
    }

    private string NextPlanName(string baseName)
    {
        string candidate;

        if (baseName.Length >= 3 && baseName[^1] == ')' && baseName[^3] == '(' && char.IsDigit(baseName[^2]))
        {
            int digit = baseName[^2] - '0';
            int next = (digit + 1) % 10;
            candidate = baseName[..^2] + next + ")";
        }
        else
        {
            candidate = baseName + "(1)";
        }

        int suffix = 2;
        string unique = candidate;
        while (battlePlans.ContainsKey(unique))
        {
            unique = candidate + suffix;
            suffix++;
        }

        return unique;
    }
}
