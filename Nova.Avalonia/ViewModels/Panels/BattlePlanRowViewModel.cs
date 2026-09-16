using System;
using System.Collections.Generic;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One battle plan, live-editable - ports BattlePlans.cs's now-working editor (see PROJECT-
/// STATUS.md's audit-against-updated-specs entry for the WinForms side of this fix). Unlike the
/// WinForms dialog, there's no separate "working copy"/dirty-flag machinery here: each field
/// binds straight to the real BattlePlan and writes through immediately on change, matching the
/// direct-mutation pattern already established for Player Relations/Ship Design in this app -
/// Avalonia's two-way bindings make WinForms' manual commit-on-switch tracking unnecessary.
/// </summary>
public class BattlePlanRowViewModel : ViewModelBase
{
    private readonly BattlePlan plan;
    private readonly Action<string, string> onRename;

    public IReadOnlyList<string> TargetOptions => BattlePlan.TargetOptions;

    public IReadOnlyList<string> TacticOptions => BattlePlan.TacticOptions;

    public IReadOnlyList<string> AttackOptions => BattlePlan.AttackOptions;

    public BattlePlan Plan => plan;

    public string Name
    {
        get => plan.Name;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == plan.Name)
            {
                return;
            }

            string oldName = plan.Name;
            plan.Name = value;
            onRename(oldName, value);
            OnPropertyChanged();
        }
    }

    public string PrimaryTarget
    {
        get => plan.PrimaryTarget;
        set
        {
            if (value != null && value != plan.PrimaryTarget)
            {
                plan.PrimaryTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public string SecondaryTarget
    {
        get => plan.SecondaryTarget;
        set
        {
            if (value != null && value != plan.SecondaryTarget)
            {
                plan.SecondaryTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public string Tactic
    {
        get => plan.Tactic;
        set
        {
            if (value != null && value != plan.Tactic)
            {
                plan.Tactic = value;
                OnPropertyChanged();
            }
        }
    }

    public string Attack
    {
        get => plan.Attack;
        set
        {
            if (value != null && value != plan.Attack)
            {
                plan.Attack = value;
                OnPropertyChanged();
            }
        }
    }

    public bool DumpCargo
    {
        get => plan.DumpCargo;
        set
        {
            if (value != plan.DumpCargo)
            {
                plan.DumpCargo = value;
                OnPropertyChanged();
            }
        }
    }

    public BattlePlanRowViewModel(BattlePlan plan, Action<string, string> onRename)
    {
        this.plan = plan;
        this.onRename = onRename;
    }
}
