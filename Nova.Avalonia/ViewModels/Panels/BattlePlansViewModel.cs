using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Battle Plans editor - ports the now-working BattlePlans.cs (see PROJECT-STATUS.md's audit
/// against updated specs; this was a read-only stub in the prior session since the WinForms
/// dialog it ported was dead code at the time). Direct mutation of EmpireData.BattlePlans, no
/// ICommand involved - same pattern as Player Relations. The dictionary has no inherent order,
/// but .NET's Dictionary enumerates in insertion order absent removals disturbing it, and the
/// "Default" plan is always the very first one ever added (EmpireData's constructor) - so
/// treating Plans[0] as the protected plan mirrors the exact same assumption the WinForms
/// planList/SelectedIndex==0 check already makes, not a new limitation introduced here.
/// </summary>
public class BattlePlansViewModel : Tool
{
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
                DeleteCommand.NotifyCanExecuteChanged();
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

    public IRelayCommand NewPlanCommand { get; }

    public IRelayCommand DeleteCommand { get; }

    public BattlePlansViewModel(string id, string title, ClientData clientState)
    {
        Id = id;
        Title = title;
        battlePlans = clientState.EmpireState.BattlePlans;

        NewPlanCommand = new RelayCommand(CreatePlan, () => battlePlans.Count < Global.MaxBattlePlans);
        DeleteCommand = new RelayCommand(DeleteSelected, CanDelete);

        RebuildPlans();
        SelectedPlan = Plans.FirstOrDefault();
    }

    private bool CanDelete()
    {
        return SelectedPlan != null && Plans.Count > 0 && !ReferenceEquals(SelectedPlan, Plans[0]);
    }

    private void RebuildPlans()
    {
        Plans = battlePlans.Values
            .Select(plan => new BattlePlanRowViewModel(plan, OnPlanRenamed))
            .ToList();
        NewPlanCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void OnPlanRenamed(string oldName, string newName)
    {
        if (battlePlans.TryGetValue(oldName, out BattlePlan plan) && !battlePlans.ContainsKey(newName))
        {
            battlePlans.Remove(oldName);
            battlePlans[newName] = plan;
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
        };

        battlePlans[copy.Name] = copy;
        RebuildPlans();
        SelectedPlan = Plans.FirstOrDefault(row => ReferenceEquals(row.Plan, copy));
        StatusMessage = $"Created \"{copy.Name}\".";
    }

    private void DeleteSelected()
    {
        if (!CanDelete())
        {
            return;
        }

        string name = SelectedPlan!.Name;
        battlePlans.Remove(name);
        RebuildPlans();
        SelectedPlan = Plans.FirstOrDefault();
        StatusMessage = $"Deleted \"{name}\".";
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
