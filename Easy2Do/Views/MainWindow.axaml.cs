using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;

namespace Easy2Do.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Windows.Count == 0)
                desktop.Shutdown();
        }
    }
}
