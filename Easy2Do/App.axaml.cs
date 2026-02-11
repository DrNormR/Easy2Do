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
    public static SettingsService SettingsService { get; private set; } = null!;
    public static StorageService StorageService { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Initialize services
        SettingsService = new SettingsService();
        StorageService = new StorageService(SettingsService);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
            desktop.MainWindow = MainWindow;
            
            // Handle application exit to save notes
            desktop.Exit += OnExit;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        StorageService.StopWatching();

        // Save all notes when application closes
        if (MainWindow?.DataContext is MainViewModel viewModel)
        {
            foreach (var note in viewModel.Notes)
            {
                await StorageService.SaveNoteAsync(note);
            }
            var ids = viewModel.Notes.Select(n => n.Id).ToList();
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