using System.Collections.Generic;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One category group in the Ship Designer's component list - mirrors one top-level node in
/// ShipDesignDialog.cs's category TreeView (Weapon, Shield, Armor, Engine, etc., keyed by
/// Component.Type.ToDescription()), except every category's items are always shown at once in
/// one scrollable list instead of needing a click to expand a node first - simpler, and avoids
/// needing a tree control's own expand/collapse affordance to work reliably via touch.
/// </summary>
public class ComponentCategoryViewModel
{
    public string Name { get; }

    public IReadOnlyList<ComponentListItemViewModel> Items { get; }

    public ComponentCategoryViewModel(string name, IReadOnlyList<ComponentListItemViewModel> items)
    {
        Name = name;
        Items = items;
    }
}
