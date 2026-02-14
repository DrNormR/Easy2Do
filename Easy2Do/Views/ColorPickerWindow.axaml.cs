using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Easy2Do.Views;

public partial class ColorPickerWindow : Window
{
    public string SelectedColorHex { get; private set; } = "#FFFFFFFF";
    public bool IsSaved { get; private set; }

    public ColorPickerWindow()
    {
        InitializeComponent();
    }

    public ColorPickerWindow(string existingColorHex) : this()
    {
        if (Color.TryParse(existingColorHex, out var color))
        {
            ColorView.Color = color;
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        SelectedColorHex = ToHexArgb(ColorView.Color);
        IsSaved = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string ToHexArgb(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
