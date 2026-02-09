using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace Easy2Do.Converters;

public class BoolOrMultiConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var result = values.Any(v => v is bool b && b);

        if (targetType == typeof(double) || targetType == typeof(float))
        {
            return result ? 1.0 : 0.0;
        }

        return result;
    }
}
