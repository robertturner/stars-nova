using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row of the "Expected Research Benefits" list - a component not yet available that
/// researching the target field alone (holding every other field at its current level) would
/// eventually unlock, colored by how many additional levels of the target field are needed -
/// ports WinForms ResearchDialog's PopulateResearchBenefits/ResearchBenefitEntry exactly (see
/// ResearchViewModel.BuildBenefits), just with colors picked to stay readable on this app's dark
/// theme instead of the WinForms dialog's light one (green/blue kept as the same attention-
/// grabbing hues; its "black" tier - indistinguishable from that dialog's own default black text,
/// i.e. deliberately the least emphasized - becomes this panel's own plain white instead of a
/// literal black that would be invisible here).
/// </summary>
public class ResearchBenefitRowViewModel
{
    public string Text { get; }

    public IBrush Color { get; }

    public ResearchBenefitRowViewModel(string text, IBrush color)
    {
        Text = text;
        Color = color;
    }
}
