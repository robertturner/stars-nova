namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// One row in the Research panel: the empire's current level and banked resources in a
/// single tech field, plus whether it's the empire's current one-hot research target (see
/// ResearchViewModel - Stars! research has no priority/weight system, just a single active
/// target field at a time).
/// </summary>
public class ResearchFieldRowViewModel
{
    public string Field { get; }

    public int Level { get; }

    public int ResourcesBanked { get; }

    public bool IsCurrentTarget { get; }

    public ResearchFieldRowViewModel(string field, int level, int resourcesBanked, bool isCurrentTarget)
    {
        Field = field;
        Level = level;
        ResourcesBanked = resourcesBanked;
        IsCurrentTarget = isCurrentTarget;
    }
}
