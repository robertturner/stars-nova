using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Nova.Client;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>One line of the selected template.</summary>
public class ProductionTemplateEntryRowViewModel
{
    public ProductionTemplateEntryRowViewModel(ProductionTemplateEntry entry, bool excluded, Action onRemove, Action? onMoveUp, Action? onMoveDown)
    {
        Caption = entry.Caption;
        Quantity = entry.Quantity;
        QuantityText = entry.Type == TemplateItemType.MinTerraform || entry.Type == TemplateItemType.MaxTerraform
            ? $"up to {entry.Quantity}%"
            : $"up to {entry.Quantity}";
        ExcludedNote = excluded ? "skipped for your race" : "";
        RemoveCommand = new RelayCommand(onRemove);
        MoveUpCommand = new RelayCommand(onMoveUp ?? (() => { }), () => onMoveUp != null);
        MoveDownCommand = new RelayCommand(onMoveDown ?? (() => { }), () => onMoveDown != null);
    }

    public string Caption { get; }

    public int Quantity { get; }

    public string QuantityText { get; }

    /// <summary>Set when the race exclusions of production-queue.md 10f drop this line.</summary>
    public string ExcludedNote { get; }

    public bool IsExcluded => ExcludedNote.Length > 0;

    public IRelayCommand RemoveCommand { get; }

    public IRelayCommand MoveUpCommand { get; }

    public IRelayCommand MoveDownCommand { get; }
}

/// <summary>
/// The four-slot production-template manager (behavior-specs-10/production-queue.md section 9:
/// "a 4-slot manager ... each of the 4 slots holds up to 12 auto-build entries plus a single
/// stored flag bit" for "contribute only leftover resources to research"; research-tech-tree.md
/// segment-27 note: one radio button per slot, per-slot rename and clear; 10f: the default
/// template is copied into every new or captured colony, Alternate Reality skipping Mines /
/// Factories / Defenses and Claim Adjuster both terraform entries). Every edit queues a
/// ProductionTemplateCommand (Nova.Client.TemplateOrders), because the default-template copy
/// runs on the server.
/// Not strictly the original screen (the user asked for "something suitable"): a slot can also
/// be filled from the selected planet's queue or from the Player's Guide example of section 9,
/// and the default ("favourite") slot is chosen here, or none.
/// The slot rename is the COMPACT rename surface: validated only on accept, prompt worded by
/// whether the slot was empty (Nova.Client.RenameRules).
/// </summary>
public class ProductionTemplatesViewModel : Tool
{
    private readonly ClientData clientState;
    private readonly SelectionService? selection;

    /// <summary>Raised after any template edit (the Production panel's slot list follows it).</summary>
    public event Action? TemplatesChanged;

    public ProductionTemplatesViewModel(string id, string title, ClientData clientState, SelectionService? selection = null)
    {
        Id = id;
        Title = title;
        this.clientState = clientState;
        this.selection = selection;

        AddEntryCommand = new RelayCommand(AddEntry, () => Current.Entries.Count < ProductionTemplate.MaxEntries);
        ClearCommand = new RelayCommand(ClearSlot);
        RenameCommand = new RelayCommand(RenameSlot);
        UseExampleCommand = new RelayCommand(() => Store(ProductionTemplate.ManualExample().WithName(Current.Name)));
        CopyFromPlanetCommand = new RelayCommand(CopyFromPlanet, () => SelectedOwnStar != null);
        if (selection != null)
        {
            selection.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SelectionService.Selected))
                {
                    CopyFromPlanetCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(CopyFromPlanetLabel));
                }
            };
        }

        Rebuild();
        renameText = Current.Name;
    }

    private ProductionTemplateSet Set => clientState.EmpireState.ProductionTemplates;

    private ProductionTemplate Current => Set[selectedSlot];

    public IReadOnlyList<string> EntryTypeOptions { get; } =
        Enum.GetValues<TemplateItemType>().Select(ProductionTemplateEntry.CaptionOf).ToList();

    private IReadOnlyList<string> slotNames = Array.Empty<string>();

    public IReadOnlyList<string> SlotNames
    {
        get => slotNames;
        private set => SetProperty(ref slotNames, value);
    }

    private int selectedSlot;

    public int SelectedSlot
    {
        get => selectedSlot;
        set
        {
            // A list control resets to -1 while its item list is replaced; keep the slot.
            if (value < 0)
            {
                return;
            }

            int clamped = Math.Clamp(value, 0, ProductionTemplateSet.SlotCount - 1);
            if (SetProperty(ref selectedSlot, clamped))
            {
                RenameText = Current.Name;
                RebuildEntries();
            }
        }
    }

    /// <summary>"None" then the four slots: the template copied into new colonies.</summary>
    public IReadOnlyList<string> DefaultOptions { get; } =
        new[] { "None" }.Concat(Enumerable.Range(1, ProductionTemplateSet.SlotCount).Select(slot => $"Slot {slot}")).ToList();

    public int DefaultOptionIndex
    {
        get => Set.DefaultSlot + 1;
        set
        {
            int slot = Math.Clamp(value, 0, ProductionTemplateSet.SlotCount) - 1;
            if (slot != Set.DefaultSlot)
            {
                TemplateOrders.SetDefault(clientState, slot);
                Changed($"Default template for new colonies: {DefaultOptions[slot + 1]}.");
            }
        }
    }

    private IReadOnlyList<ProductionTemplateEntryRowViewModel> entries = Array.Empty<ProductionTemplateEntryRowViewModel>();

    public IReadOnlyList<ProductionTemplateEntryRowViewModel> Entries
    {
        get => entries;
        private set => SetProperty(ref entries, value);
    }

    public bool OnlyLeftover
    {
        get => Current.OnlyLeftover;
        set
        {
            if (value != Current.OnlyLeftover)
            {
                ProductionTemplate edited = Current.Clone();
                edited.OnlyLeftover = value;
                Store(edited);
            }
        }
    }

    private int newEntryTypeIndex = (int)TemplateItemType.Factories;

    public int NewEntryTypeIndex
    {
        get => newEntryTypeIndex;
        set => SetProperty(ref newEntryTypeIndex, Math.Clamp(value, 0, EntryTypeOptions.Count - 1));
    }

    private int newEntryQuantity = 10;

    public int NewEntryQuantity
    {
        get => newEntryQuantity;
        set => SetProperty(ref newEntryQuantity, Math.Clamp(value, 1, ProductionTemplateEntry.MaxQuantity));
    }

    private string renameText = "";

    /// <summary>The compact rename field (validated only on accept).</summary>
    public string RenameText
    {
        get => renameText;
        set => SetProperty(ref renameText, value ?? "");
    }

    public string RenamePrompt => RenameRules.CompactPrompt(string.IsNullOrEmpty(Current.Name));

    public string CopyFromPlanetLabel => SelectedOwnStar != null ? $"Copy from {SelectedOwnStar.Name}'s queue" : "Copy from a planet's queue (select one)";

    private string statusMessage = "";

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetProperty(ref statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(statusMessage);

    public IRelayCommand AddEntryCommand { get; }

    public IRelayCommand ClearCommand { get; }

    public IRelayCommand RenameCommand { get; }

    public IRelayCommand UseExampleCommand { get; }

    public IRelayCommand CopyFromPlanetCommand { get; }

    private Star? SelectedOwnStar => selection?.Selected is Star star && star.Owner == clientState.EmpireState.Id ? star : null;

    private void AddEntry()
    {
        ProductionTemplate edited = Current.Clone();
        if (!edited.TryAdd(new ProductionTemplateEntry((TemplateItemType)newEntryTypeIndex, newEntryQuantity)))
        {
            StatusMessage = $"A template holds at most {ProductionTemplate.MaxEntries} entries.";
            return;
        }

        Store(edited);
    }

    private void RemoveEntry(int index)
    {
        ProductionTemplate edited = Current.Clone();
        edited.Entries.RemoveAt(index);
        Store(edited);
    }

    private void MoveEntry(int index, int otherIndex)
    {
        ProductionTemplate edited = Current.Clone();
        (edited.Entries[index], edited.Entries[otherIndex]) = (edited.Entries[otherIndex], edited.Entries[index]);
        Store(edited);
    }

    private void ClearSlot()
    {
        Store(new ProductionTemplate());
        RenameText = "";
    }

    private void RenameSlot()
    {
        string? error = RenameRules.ValidateOnAccept(RenameText);
        if (error != null)
        {
            StatusMessage = error;
            return;
        }

        Store(Current.WithName(RenameRules.Normalise(RenameText)));
    }

    private void CopyFromPlanet()
    {
        Star? star = SelectedOwnStar;
        if (star == null)
        {
            return;
        }

        ProductionTemplate copied = ProductionTemplate.FromQueue(Current.Name, star);
        Store(copied);
        StatusMessage = copied.IsEmpty
            ? $"{star.Name}'s queue has no auto-build entries to copy."
            : $"Copied {copied.Entries.Count} auto-build entr{(copied.Entries.Count == 1 ? "y" : "ies")} from {star.Name}.";
    }

    private void Store(ProductionTemplate template)
    {
        TemplateOrders.SetSlot(clientState, selectedSlot, template);
        Changed("");
    }

    private void Changed(string status)
    {
        Rebuild();
        StatusMessage = status;
        TemplatesChanged?.Invoke();
    }

    private void Rebuild()
    {
        SlotNames = Enumerable.Range(0, ProductionTemplateSet.SlotCount)
            .Select(slot => ProductionViewModel.SlotCaption(Set, slot))
            .ToList();
        OnPropertyChanged(nameof(SelectedSlot));
        OnPropertyChanged(nameof(DefaultOptionIndex));
        RebuildEntries();
    }

    private void RebuildEntries()
    {
        Race race = clientState.EmpireState.Race;
        List<ProductionTemplateEntry> list = Current.Entries;
        Entries = list
            .Select((entry, index) => new ProductionTemplateEntryRowViewModel(
                entry,
                ProductionTemplateEntry.IsExcludedFor(entry.Type, race),
                () => RemoveEntry(index),
                index > 0 ? () => MoveEntry(index, index - 1) : null,
                index < list.Count - 1 ? () => MoveEntry(index, index + 1) : null))
            .ToList();
        OnPropertyChanged(nameof(OnlyLeftover));
        OnPropertyChanged(nameof(RenamePrompt));
        AddEntryCommand.NotifyCanExecuteChanged();
    }
}
