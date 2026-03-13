using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Easy2Do.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnBackButtonClick(object? sender, RoutedEventArgs e)
    {
        // On mobile, go back to the main list
        App.MainViewModel?.CloseDetailCommand.Execute(null);
    }
}
