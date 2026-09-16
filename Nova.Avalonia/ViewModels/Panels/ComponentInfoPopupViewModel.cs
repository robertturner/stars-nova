using System;
using System.Collections.Generic;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Backs the Ship Designer's press-and-hold info popup (see ShipDesignView.axaml.cs's long-press
/// handling and ShipDesignViewModel.ShowComponentInfo) - the touch-friendly equivalent of the
/// original ShipDesignDialog's always-visible cost/mass/description panel, shown on demand
/// instead of taking up permanent screen space.
/// </summary>
public class ComponentInfoPopupViewModel : ViewModelBase
{
    private bool isVisible;

    public bool IsVisible
    {
        get => isVisible;
        set => SetProperty(ref isVisible, value);
    }

    private string name = "";

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    private object? icon;

    /// <summary>See ComponentListItemViewModel.Icon's own comment - same Component.ComponentImage
    /// Bitmap, just surfaced here too for the popup's own larger icon.</summary>
    public object? Icon
    {
        get => icon;
        set => SetProperty(ref icon, value);
    }

    private string typeAndCost = "";

    public string TypeAndCost
    {
        get => typeAndCost;
        set => SetProperty(ref typeAndCost, value);
    }

    private string description = "";

    public string Description
    {
        get => description;
        set
        {
            if (SetProperty(ref description, value))
            {
                OnPropertyChanged(nameof(HasDescription));
            }
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(description);

    private IReadOnlyList<string> detailLines = Array.Empty<string>();

    /// <summary>Type-specific facts - an Engine's speed/fuel-usage figures, a Weapon's power/
    /// range/accuracy, and so on. See ShipDesignViewModel.BuildDetailLines.</summary>
    public IReadOnlyList<string> DetailLines
    {
        get => detailLines;
        set => SetProperty(ref detailLines, value);
    }
}
