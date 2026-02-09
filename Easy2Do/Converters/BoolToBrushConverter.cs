using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Easy2Do.Converters
{
    public class BoolToBrushConverter : IValueConverter
    {
        public string TrueBrush { get; set; } = "#D32F2F";
        public string FalseBrush { get; set; } = "#000000";

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            var brushCode = flag ? TrueBrush : (parameter as string ?? FalseBrush);
            return Brush.Parse(brushCode);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
