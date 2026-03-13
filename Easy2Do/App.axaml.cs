using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
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
    public static PowerSyncService PowerSyncService { get; private set; } = null!;
    public static AlarmService AlarmService { get; private set; } = null!;
    public static BackupService BackupService { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Initialize services
        SettingsService = new SettingsService();
        StorageService = new StorageService(SettingsService);
        PowerSyncService = new PowerSyncService(SettingsService, StorageService);
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
            MainViewModel = new MainViewModel();
            MainWindow = new MainWindow
            {
                DataContext = MainViewModel
            };
            desktop.MainWindow = MainWindow;

            // Start alarm service to monitor due dates
            var viewModel = MainViewModel;
            AlarmService.Start(() => viewModel.Notes);

            // Start sync if enabled
            _ = PowerSyncService.StartAsync();

            // Wire up DataRefreshed to reload notes
            PowerSyncService.DataRefreshed += () => Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                if (MainViewModel != null)
                    await MainViewModel.LoadNotesAsync();
            });

            // Handle application exit to save notes
            desktop.Exit += OnExit;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            MainViewModel = new MainViewModel();
            singleViewPlatform.MainView = new MainView
            {
                DataContext = MainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        AlarmService.Stop();
        PowerSyncService.Stop();
        StorageService.StopWatching();

        // Save all notes when application closes
        if (MainViewModel != null)
        {
            foreach (var note in MainViewModel.Notes)
            {
                await StorageService.SaveNoteAsync(note);
            }
            var ids = MainViewModel.Notes.Select(n => n.Id).ToList();
            await StorageService.SaveManifestAsync(ids);
        }
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