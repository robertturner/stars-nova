using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Nova.Common;
using Nova.Common.Commands;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Applying one of the empire's four saved production templates to the selected planet's queue
/// "in one action" (behavior-specs-10/production-queue.md section 9). The templates themselves
/// are edited in the Production Templates panel (ProductionTemplatesViewModel).
/// AMBIGUITY: whether applying replaces the queue or adds to it is not specified (the
/// colonisation copy of 10f fills an empty queue). Both are offered: Replace (the queue becomes
/// the template, as for a new colony) and Append. The race exclusions of 10f apply to both.
/// SPEC GAP / not done: the template's leftover-to-research flag is not copied by this panel -
/// no order carries a planet's "contribute only leftover resources" setting to the server yet.
/// </summary>
public partial class ProductionViewModel
{
    private IReadOnlyList<string> templateSlotNames = Array.Empty<string>();

    public IReadOnlyList<string> TemplateSlotNames
    {
        get => templateSlotNames;
        private set => SetProperty(ref templateSlotNames, value);
    }

    private int selectedTemplateSlot;

    public int SelectedTemplateSlot
    {
        get => selectedTemplateSlot;
        set
        {
            // A combo box resets to -1 while its item list is replaced; keep the slot.
            if (value >= 0)
            {
                SetProperty(ref selectedTemplateSlot, Math.Clamp(value, 0, ProductionTemplateSet.SlotCount - 1));
            }
        }
    }

    public IRelayCommand ReplaceWithTemplateCommand { get; }

    public IRelayCommand AppendTemplateCommand { get; }

    /// <summary>Rebuilds the slot names (they change in the template manager).</summary>
    public void RefreshTemplateSlots()
    {
        ProductionTemplateSet set = clientState.EmpireState.ProductionTemplates;
        TemplateSlotNames = Enumerable.Range(0, ProductionTemplateSet.SlotCount)
            .Select(slot => SlotCaption(set, slot))
            .ToList();
        OnPropertyChanged(nameof(SelectedTemplateSlot));
        ReplaceWithTemplateCommand.NotifyCanExecuteChanged();
        AppendTemplateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>"1: Name (8 items)", with "(default)" on the default slot.</summary>
    public static string SlotCaption(ProductionTemplateSet set, int slot)
    {
        ProductionTemplate template = set[slot];
        string name = string.IsNullOrEmpty(template.Name) ? "(unnamed)" : template.Name;
        string caption = $"{slot + 1}: {name} ({template.Entries.Count} item{(template.Entries.Count == 1 ? "" : "s")})";
        return set.DefaultSlot == slot ? caption + " (default)" : caption;
    }

    private void ApplyTemplate(bool replace)
    {
        if (selectedStar == null)
        {
            return;
        }

        ProductionTemplate template = clientState.EmpireState.ProductionTemplates[selectedTemplateSlot];
        List<ProductionOrder> orders = template.OrdersFor(clientState.EmpireState.Race);
        if (orders.Count == 0 && !replace)
        {
            return;
        }

        if (replace)
        {
            // Applying a template replaces only its auto-build part and keeps everything else
            // (production-queue.md §9, "Applying"): remove every auto-build entry, leaving manual
            // items, ships and starbases in order; the template's entries are then appended.
            for (int index = selectedStar.ManufacturingQueue.Queue.Count - 1; index >= 0; index--)
            {
                if (ProductionTemplateEntry.TypeOf(selectedStar.ManufacturingQueue.Queue[index]).HasValue)
                {
                    PushQueued(new ProductionCommand(CommandMode.Delete, null!, selectedStar.Name, index));
                }
            }
        }

        foreach (ProductionOrder order in orders)
        {
            PushQueued(new ProductionCommand(CommandMode.Add, order, selectedStar.Name, selectedStar.ManufacturingQueue.Queue.Count));
        }

        RebuildQueueRows(selectedStar);
        selection.NotifyMutated();
    }

    /// <summary>Queues and applies one command without the per-command rebuild.</summary>
    private void PushQueued(ICommand command)
    {
        clientState.Commands.Push(command);
        if (command.IsValid(clientState.EmpireState))
        {
            command.ApplyToState(clientState.EmpireState);
        }
    }
}
