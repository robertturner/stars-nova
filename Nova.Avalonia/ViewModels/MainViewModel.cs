using Dock.Model.Core;
using Dock.Model.Controls;
using Nova.Avalonia.Docking;
using Nova.Client;

namespace Nova.Avalonia.ViewModels;

/// <summary>
/// The desktop game screen - the AvaloniaDock multi-panel layout (see NovaDockFactory). Only
/// ever used by the desktop head (MainWindow); the single-view Android host uses
/// MobileMainViewModel/MobileMainView instead - a docked, freely-resizable panel layout designed
/// for a mouse and a large screen turned out not to translate to a phone at all (confirmed live:
/// panels shrank to unreadable slivers, and dock splitters are far too small a drag target for
/// touch) - see MobileMainViewModel's own comment for that screen's very different structure.
/// Turn-submission/About plumbing lives in the shared GameShellViewModelBase instead of here, so
/// both screens get the exact same behavior without duplicating it.
/// </summary>
public partial class MainViewModel : GameShellViewModelBase
{
    public IFactory Factory { get; }

    public IRootDock Layout { get; }

    public MainViewModel(ClientData clientState) : base(clientState)
    {
        Factory = new NovaDockFactory(clientState);
        Layout = Factory.CreateLayout();
        Factory.InitLayout(Layout);
    }
}
