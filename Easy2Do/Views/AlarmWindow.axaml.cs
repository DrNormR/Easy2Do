using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Easy2Do.Views;

public partial class AlarmWindow : Window
{
    public AlarmWindow()
    {
        InitializeComponent();
    }

    public AlarmWindow(string message) : this()
    {
        AlarmMessage.Text = message;
    }

    private void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
