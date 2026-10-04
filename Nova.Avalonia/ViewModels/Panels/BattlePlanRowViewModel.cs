using System;
using System.Collections.Generic;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One battle plan, live-editable - ports BattlePlans.cs's now-working editor. Each targeting
/// field binds straight to the real BattlePlan and writes through immediately on change; every
/// change is reported to the owning BattlePlansViewModel, which queues the plans as an order
/// (BattlePlansCommand) so the server fights with them too.
/// The name is different: it is edited in <see cref="EditName"/> (the ordinary rename surface,
/// filtered on every keystroke) and only committed by the editor's Rename action, which
/// validates it again and keeps every fleet that uses the plan pointing at it
/// (client-ui-dialog-catalog.md "Rename surfaces"; Nova.Client.BattlePlanRules.Rename). It used
/// to rename the plan on every keystroke, re-keying the dictionary each time and leaving every
/// fleet on the plan naming one that no longer existed.
/// </summary>
public class BattlePlanRowViewModel : ViewModelBase
{
    private readonly BattlePlan plan;
    private readonly Action onChanged;

    public IReadOnlyList<string> TargetOptions => BattlePlan.TargetOptions;

    public IReadOnlyList<string> TacticOptions => BattlePlan.TacticOptions;

    public IReadOnlyList<string> AttackOptions => BattlePlan.AttackOptions;

    public BattlePlan Plan => plan;

    public string Name => plan.Name;

    private string editName;

    /// <summary>The name being typed (live-filtered); committed by Rename.</summary>
    public string EditName
    {
        get => editName;
        set => SetProperty(ref editName, RenameRules.LiveFilter(value));
    }

    /// <summary>Re-reads the plan's name after a rename (or a refused one).</summary>
    public void NameCommitted()
    {
        OnPropertyChanged(nameof(Name));
        EditName = plan.Name;
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
                onChanged();
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
                onChanged();
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
                onChanged();
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
                onChanged();
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
                onChanged();
            }
        }
    }

    public BattlePlanRowViewModel(BattlePlan plan, Action onChanged)
    {
        this.plan = plan;
        this.onChanged = onChanged;
        editName = plan.Name;
    }
}
