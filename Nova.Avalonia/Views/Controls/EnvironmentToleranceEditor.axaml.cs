using Avalonia.Controls;

namespace Nova.Avalonia.Views.Controls;

/// <summary>
/// A single Gravity/Temperature/Radiation tolerance editor - see
/// ViewModels/EnvironmentToleranceViewModel.cs for why this replaces the WinForms
/// ControlLibrary.Range drag-bar with two plain Sliders. Used three times in RaceDesignerView,
/// each instance's DataContext set directly to the relevant EnvironmentToleranceViewModel.
/// </summary>
public partial class EnvironmentToleranceEditor : UserControl
{
    public EnvironmentToleranceEditor()
    {
        InitializeComponent();
    }
}
