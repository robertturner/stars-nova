using Dock.Model.Core;
using Dock.Model.Controls;
using Nova.Avalonia.Docking;

namespace Nova.Avalonia.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public IFactory Factory { get; }

    public IRootDock Layout { get; }

    public MainViewModel()
    {
        Factory = new NovaDockFactory();
        Layout = Factory.CreateLayout();
        Factory.InitLayout(Layout);
    }
}
