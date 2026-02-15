using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Easy2Do.Views;

public partial class ColorPickerWindow : Window
{
    public string SelectedColorHex { get; private set; } = "#FFFFFFFF";
    public bool IsSaved { get; private set; }
    private bool _isUpdatingHex;

    public ColorPickerWindow()
    {
        InitializeComponent();
        SetHexText(ColorView.Color);
    }

    public ColorPickerWindow(string existingColorHex) : this()
    {
        if (Color.TryParse(existingColorHex, out var color))
        {
            ColorView.Color = color;
            SetHexText(color);
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

    private void OnColorViewColorChanged(object? sender, Avalonia.Controls.ColorChangedEventArgs e)
    {
        SetHexText(e.NewColor);
    }

    private void OnHexTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingHex || string.IsNullOrWhiteSpace(HexTextBox.Text))
        {
            return;
        }

        if (TryParseHexColor(HexTextBox.Text, out var color))
        {
            ColorView.Color = color;
        }
    }

    private void OnHexTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(HexTextBox.Text))
        {
            return;
        }

        e.Handled = true;
        TryApplyHex(HexTextBox.Text);
    }

    private void TryApplyHex(string hex)
    {
        if (TryParseHexColor(hex, out var color))
        {
            ColorView.Color = color;
            SetHexText(color);
        }
    }

    private static bool TryParseHexColor(string input, out Color color)
    {
        color = default;

        var hex = input.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        foreach (var c in hex)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        if (hex.Length == 6)
        {
            hex = $"FF{hex}";
        }

        return Color.TryParse($"#{hex}", out color);
    }

    private void SetHexText(Color color)
    {
        _isUpdatingHex = true;
        HexTextBox.Text = ToHexArgb(color);
        _isUpdatingHex = false;
    }

    private static string ToHexArgb(Color color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
