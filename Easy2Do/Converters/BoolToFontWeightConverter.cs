using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Easy2Do.Converters;

public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBold = value is bool b && b;
        return isBold ? FontWeight.Bold : FontWeight.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FontWeight fw)
        {
            return fw >= FontWeight.Bold;
        }
        return false;
    }
}
