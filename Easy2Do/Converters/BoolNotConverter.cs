using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Easy2Do.Converters;

public class BoolNotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool val && val;
        return !b;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool val && val;
        return !b;
    }
}
