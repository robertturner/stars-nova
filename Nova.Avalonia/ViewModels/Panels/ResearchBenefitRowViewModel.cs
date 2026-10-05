using Avalonia.Media;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row of the "Expected Research Benefits" list - a component not yet available that
/// researching the target field alone (holding every other field at its current level) would
/// eventually unlock. The text is the component's name and type; the status line is the detail
/// card's single line (behavior-specs-11/research-tech-tree.md section 7a, via
/// Nova.Client.TechStatusLine): "Unavailable" in red, "Available", or the research resources
/// still needed (in thousands with a "k" from 100,000 up). See ResearchViewModel.BuildBenefits.
/// </summary>
public class ResearchBenefitRowViewModel
{
    public string Text { get; }

    public IBrush Color { get; }

    /// <summary>The detail card's one status line (behavior-specs-10/research-tech-tree.md §4):
    /// "Unavailable", "Available", the research resources still needed, or that figure in
    /// thousands with a "k" from 100,000 up - see Nova.Client.TechStatusLine.</summary>
    public string StatusText { get; }

    /// <summary>True for the red "unavailable" variant of the status line.</summary>
    public bool IsUnavailable { get; }

    public IBrush StatusColor => IsUnavailable ? Brushes.Red : Brushes.Gray;

    public ResearchBenefitRowViewModel(string text, IBrush color)
        : this(text, color, "", false)
    {
    }

    public ResearchBenefitRowViewModel(string text, IBrush color, string statusText, bool isUnavailable)
    {
        Text = text;
        Color = color;
        StatusText = statusText;
        IsUnavailable = isUnavailable;
    }
}
