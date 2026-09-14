using System.Collections.Generic;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// Compact "12 Iron, 8 Energy" style formatting for a <see cref="Resources"/> cost, showing
/// only the non-zero components.
/// </summary>
public static class ResourceFormat
{
    public static string Cost(Resources cost)
    {
        var parts = new List<string>();
        if (cost.Ironium > 0)
        {
            parts.Add($"{cost.Ironium} Iron");
        }

        if (cost.Boranium > 0)
        {
            parts.Add($"{cost.Boranium} Bor");
        }

        if (cost.Germanium > 0)
        {
            parts.Add($"{cost.Germanium} Ger");
        }

        if (cost.Energy > 0)
        {
            parts.Add($"{cost.Energy} Energy");
        }

        return parts.Count == 0 ? "Free" : string.Join(", ", parts);
    }
}
