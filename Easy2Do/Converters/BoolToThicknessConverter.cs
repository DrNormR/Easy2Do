using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia;

namespace Easy2Do.Converters;

public class BoolToThicknessConverter : IValueConverter
{
    public Thickness TrueThickness { get; set; } = new Thickness(6, 5, 0, 5);
    public Thickness FalseThickness { get; set; } = new Thickness(20, 5, 0, 5);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isHeading = value is bool b && b;
        return isHeading ? TrueThickness : FalseThickness;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
