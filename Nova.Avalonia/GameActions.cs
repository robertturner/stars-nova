using Nova.Client;

namespace Nova.Avalonia;

/// <summary>
/// Turn-level actions shared across the UI - currently just Submit Turn, mirroring
/// NovaGUI.cs's "Save &amp; Submit Turn" menu item exactly: save the client's own state (so an
/// in-progress command stack survives a restart before submission) and write every queued
/// <c>ICommand</c> out as this empire's <c>.orders</c> file.
/// </summary>
public static class GameActions
{
    public static void SubmitTurn(ClientData clientState)
    {
        clientState.Save();
        new OrderWriter(clientState).WriteOrders();
    }
}
