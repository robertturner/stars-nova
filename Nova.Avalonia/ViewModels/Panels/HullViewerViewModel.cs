using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Read-only hull/component viewer, shared by every place that wants to show what a ship
/// design (or a starbase, which is just a design too - see Star.Starbase) is actually built
/// from: press-and-hold on a fleet's ship-type row (Inspector's composition list, a Navigator
/// row, a Star Map fleet marker) or the "View Starbase Components" button. Reuses the same
/// 5-wide slot-grid layout ShipDesignView.axaml uses to build a design (HullModule.CellNumber-
/// driven), minus every editing affordance - see HullViewerSlotViewModel's own comment on why
/// that's a separate, simpler type from HullSlotRowViewModel.
///
/// Each host panel (InspectorViewModel/NavigatorViewModel/StarMapDocumentViewModel) owns its
/// own instance rather than sharing one through SelectionService - the popup is an overlay
/// rendered by that panel's own View, so there's nothing to gain from a single shared instance
/// and it avoids any cross-panel visibility ambiguity.
/// </summary>
public class HullViewerViewModel : ViewModelBase
{
    private bool isVisible;

    public bool IsVisible
    {
        get => isVisible;
        private set => SetProperty(ref isVisible, value);
    }

    private string designName = "";

    public string DesignName
    {
        get => designName;
        private set => SetProperty(ref designName, value);
    }

    private string summary = "";

    public string Summary
    {
        get => summary;
        private set => SetProperty(ref summary, value);
    }

    private IReadOnlyList<HullViewerSlotViewModel> slots = System.Array.Empty<HullViewerSlotViewModel>();

    public IReadOnlyList<HullViewerSlotViewModel> Slots
    {
        get => slots;
        private set => SetProperty(ref slots, value);
    }

    public IRelayCommand CloseCommand { get; }

    public HullViewerViewModel()
    {
        CloseCommand = new RelayCommand(Hide);
    }

    public void Show(ShipDesign? design)
    {
        if (design == null)
        {
            return;
        }

        DesignName = design.Name;
        Summary = $"{ResourceFormat.Cost(design.Cost)} - {design.Mass}kT - Armor {design.Armor} - Shield {design.Shield}";
        Slots = design.Hull.Modules
            .Where(module => module.AllocatedComponent != null)
            .Select(module => new HullViewerSlotViewModel(module))
            .ToList();
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
    }
}
