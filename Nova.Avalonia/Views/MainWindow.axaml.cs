using Avalonia.Controls;
using Nova.Avalonia.ViewModels;

namespace Nova.Avalonia.Views;

/// <summary>
/// Desktop-only Window shell around the portable MainView content. Takes the ViewModel via its
/// constructor (rather than having it assigned to DataContext afterward, as before this split)
/// so it can subscribe to AboutRequested up front - opening a real modal Window here is
/// desktop-specific behavior a single-view host (Android) can't reuse, which is exactly why
/// MainViewModel raises an event instead of showing the About screen itself.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel viewModel)
        : this()
    {
        Attach(viewModel);
    }

    private void Attach(MainViewModel viewModel)
    {
        DataContext = viewModel;
        viewModel.AboutRequested += () => new AboutWindow().ShowDialog(this);
        viewModel.TurnAdvanced += freshState => Attach(new MainViewModel(freshState));
    }
}
