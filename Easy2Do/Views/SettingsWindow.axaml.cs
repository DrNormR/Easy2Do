using Avalonia.Controls;

namespace Easy2Do.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnRestoreBackupButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("Restore Backup button clicked!");
    }
}
