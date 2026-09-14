using Avalonia.Controls;

namespace Nova.Avalonia.Views.Controls;

/// <summary>
/// One field's 50%/standard/75%-extra research-cost selector - see
/// ViewModels/ResearchCostViewModel.cs. Used six times in RaceDesignerView, one per
/// TechLevel.ResearchField, each instance's DataContext set directly to the relevant
/// ResearchCostViewModel.
/// </summary>
public partial class ResearchCostSelector : UserControl
{
    public ResearchCostSelector()
    {
        InitializeComponent();
    }
}
