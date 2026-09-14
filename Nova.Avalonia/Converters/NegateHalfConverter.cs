using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Nova.Avalonia.Converters;

/// <summary>
/// Converts a measured width/height to its negative half (-w/2) - used to center an auto-sized
/// element (the Star Map's star-name label) on a Canvas anchor point via a TranslateTransform
/// bound to the element's own Bounds.Width. This is the Avalonia equivalent of GDI+'s
/// StringFormat.Alignment=Center (used by the WinForms StarMap.DrawStar this ports): Avalonia's
/// TransformOperations parser has no CSS-style percentage-unit support ("translate(-50%,0)"
/// throws FormatException: Invalid unit: %), so the offset has to be computed from the element's
/// actual measured size instead.
/// </summary>
public class NegateHalfConverter : IValueConverter
{
    public static readonly NegateHalfConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is double width ? -width / 2.0 : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
