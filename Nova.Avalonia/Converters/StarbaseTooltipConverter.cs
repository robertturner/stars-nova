using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Nova.Avalonia.Converters;

/// <summary>
/// Star Map starbase-dot tooltip text - matches StarMapStarViewModel.IsFullStarbase, which also
/// drives that same dot's color (see StarMapDocumentView.axaml's "starbaseFull"/"starbaseSmall"
/// style classes) so hovering explains what the color difference means.
/// </summary>
public class StarbaseTooltipConverter : IValueConverter
{
    public static readonly StarbaseTooltipConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "Starbase" : "Orbital defense platform";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
