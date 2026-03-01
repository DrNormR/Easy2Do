using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Easy2Do.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isAboutVisible = false;

    [ObservableProperty]
    private string _storageLocation = string.Empty;

    [ObservableProperty]
    private bool _syncEnabled;

    [ObservableProperty]
    private string _powerSyncUrl = string.Empty;

    [ObservableProperty]
    private string _powerSyncDevToken = string.Empty;

    [ObservableProperty]
    private string _syncBackendUrl = string.Empty;

    [ObservableProperty]
    private string _supabaseUrl = string.Empty;

    [ObservableProperty]
    private string _supabaseApiKey = string.Empty;

    public SettingsViewModel()
    {
        System.Diagnostics.Debug.WriteLine("SettingsViewModel constructed");
        StorageLocation = App.SettingsService.GetStorageLocation();
        SyncEnabled = App.SettingsService.GetSyncEnabled();
        PowerSyncUrl = App.SettingsService.GetPowerSyncUrl();
        PowerSyncDevToken = App.SettingsService.GetPowerSyncDevToken();
        SyncBackendUrl = App.SettingsService.GetSyncBackendUrl();
        SupabaseUrl = App.SettingsService.GetSupabaseUrl();
        SupabaseApiKey = App.SettingsService.GetSupabaseApiKey();
        _restoreBackupCommand = new AsyncRelayCommand(RestoreBackup);
        _reconnectSyncCommand = new AsyncRelayCommand(ReconnectSyncAsync);
        App.StorageService.SyncStatusChanged += OnSyncStatusChanged;
    }

    [RelayCommand]
    private void ShowAbout()
    {
        IsAboutVisible = true;
    }

    [RelayCommand]
    private void CloseAbout()
    {
        IsAboutVisible = false;
    }

    [RelayCommand]
    private async Task BrowseStorageLocation()
    {
        if (App.MainWindow != null)
        {
            var topLevel = TopLevel.GetTopLevel(App.MainWindow);
            if (topLevel != null)
            {
                var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Storage Location",
                    AllowMultiple = false
                });

                if (folders.Count > 0)
                {
                    var selectedPath = folders[0].Path.LocalPath;
                    StorageLocation = selectedPath;
                    App.SettingsService.SetStorageLocation(selectedPath);
                }
            }
        }
    }

    [RelayCommand]
    private void OpenStorageFolder()
    {
        var location = App.SettingsService.GetStorageLocation();
        if (Directory.Exists(location))
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = location,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", location);
            }
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", location);
            }
        }
    }

    public IRelayCommand RestoreBackupCommand {
        get {
            System.Diagnostics.Debug.WriteLine("RestoreBackupCommand property getter called");
            return _restoreBackupCommand;
        }
    }
    private readonly IRelayCommand _restoreBackupCommand;
    private readonly IRelayCommand _reconnectSyncCommand;

    public IRelayCommand ReconnectSyncCommand => _reconnectSyncCommand;

    partial void OnSyncEnabledChanged(bool value)
    {
        App.SettingsService.SetSyncEnabled(value);
        if (value)
            _ = App.PowerSyncService.StartAsync();
    }

    partial void OnPowerSyncUrlChanged(string value)
    {
        App.SettingsService.SetPowerSyncUrl(value);
    }

    partial void OnPowerSyncDevTokenChanged(string value)
    {
        App.SettingsService.SetPowerSyncDevToken(value);
    }

    partial void OnSyncBackendUrlChanged(string value)
    {
        App.SettingsService.SetSyncBackendUrl(value);
    }

    partial void OnSupabaseUrlChanged(string value)
    {
        App.SettingsService.SetSupabaseUrl(value);
    }

    partial void OnSupabaseApiKeyChanged(string value)
    {
        App.SettingsService.SetSupabaseApiKey(value);
    }

    private async Task ReconnectSyncAsync()
    {
        Console.WriteLine("[Sync] Reconnect requested.");
        var result = await App.PowerSyncService.TryStartAsync();
        Console.WriteLine($"[Sync] {result.Message}");
    }

    private void OnSyncStatusChanged(string message)
    {
        Console.WriteLine($"[Sync] {message}");
    }

    private async Task RestoreBackup()
    {
        System.Diagnostics.Debug.WriteLine("RestoreBackup method called");
        if (App.MainWindow != null)
        {
            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(App.MainWindow);
            if (topLevel != null)
            {
                System.Diagnostics.Debug.WriteLine("Opening file picker for backup file...");
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Select Backup File",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("Backup Files")
                        {
                            Patterns = new[] { "*.bak", "*.json" }
                        }
                    }
                });
                System.Diagnostics.Debug.WriteLine($"File picker returned {files.Count} files.");
                if (files.Count > 0)
                {
                    var selectedPath = files[0].Path.LocalPath;
                    System.Diagnostics.Debug.WriteLine($"Selected backup file: {selectedPath}");
                    await App.StorageService.RestoreBackupAsync(selectedPath);
                    System.Diagnostics.Debug.WriteLine("Called RestoreBackupAsync on StorageService.");
                    // Reload notes in main view
                    if (App.MainWindow?.DataContext is Easy2Do.ViewModels.MainViewModel mainVm)
                    {
                        System.Diagnostics.Debug.WriteLine("Reloading notes in MainViewModel...");
                        await mainVm.LoadNotesAsync();
                        System.Diagnostics.Debug.WriteLine("Notes reloaded.");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No backup file selected.");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("TopLevel is null.");
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("App.MainWindow is null.");
        }
    }
}

