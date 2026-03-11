using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Easy2Do.ViewModels;
using Easy2Do.Views;
using Easy2Do.Services;

namespace Easy2Do;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }
    public static MainViewModel? MainViewModel { get; private set; }
    public static SettingsService SettingsService { get; private set; } = null!;
    public static StorageService StorageService { get; private set; } = null!;
    public static AlarmService AlarmService { get; private set; } = null!;
    public static BackupService BackupService { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Initialize services
        SettingsService = new SettingsService();
        StorageService = new StorageService(SettingsService);
        AlarmService = new AlarmService();
        BackupService = new BackupService(SettingsService);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            var desktopViewModel = new MainViewModel();
            MainViewModel = desktopViewModel;
            MainWindow = new MainWindow
            {
                DataContext = desktopViewModel
            };
            desktop.MainWindow = MainWindow;

            // Start alarm service to monitor due dates
            AlarmService.Start(() => desktopViewModel.Notes);

            // Handle application exit to save notes
            desktop.Exit += OnExit;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            var mobileViewModel = new MainViewModel();
            MainViewModel = mobileViewModel;
            singleViewPlatform.MainView = new MainView
            {
                DataContext = mobileViewModel
            };

            // Start alarm service for mobile too
            AlarmService.Start(() => mobileViewModel.Notes);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static async Task SaveAllNotesAsync()
    {
        if (MainViewModel == null) return;
        foreach (var note in MainViewModel.Notes)
            await StorageService.SaveNoteAsync(note);
        var ids = MainViewModel.Notes.Select(n => n.Id).ToList();
        await StorageService.SaveManifestAsync(ids);
    }

    private async void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        AlarmService.Stop();
        StorageService.StopWatching();
        await SaveAllNotesAsync();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}