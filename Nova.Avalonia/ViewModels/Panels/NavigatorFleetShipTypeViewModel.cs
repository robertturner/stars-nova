using Nova.Common.Components;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One distinct ship design within a fleet's composition, shown as its own icon in the
/// Navigator's Fleets list - a multi-design fleet (e.g. an escorted colonizer) previously only
/// ever showed/exposed its FIRST design (see Fleet.Icon's own comment: "choose an image from ONE
/// of the ships"), so press-and-hold could never reveal any type but that one. Read-only and
/// rebuilt fresh alongside its owning NavigatorFleetItemViewModel.
/// </summary>
public class NavigatorFleetShipTypeViewModel
{
    public ShipDesign Design { get; }

    public int Quantity { get; }

    public object? Icon => Design.Icon?.Image;

    public NavigatorFleetShipTypeViewModel(ShipDesign design, int quantity)
    {
        Design = design;
        Quantity = quantity;
    }
}
