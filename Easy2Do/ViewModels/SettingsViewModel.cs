using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Easy2Do.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isAboutVisible = false;

    [ObservableProperty]
    private string _storageLocation = string.Empty;

    public SettingsViewModel()
    {
        System.Diagnostics.Debug.WriteLine("SettingsViewModel constructed");
        StorageLocation = App.SettingsService.GetStorageLocation();
        _restoreBackupCommand = new AsyncRelayCommand(RestoreBackup);
    }

    private static TopLevel? GetTopLevel()
    {
        if (App.MainWindow != null)
            return TopLevel.GetTopLevel(App.MainWindow);

        if (Avalonia.Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime svl)
            return TopLevel.GetTopLevel(svl.MainView);

        return null;
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
        var topLevel = GetTopLevel();
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

    [RelayCommand]
    private void OpenStorageFolder()
    {
        if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid()) return;

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

    private async Task RestoreBackup()
    {
        System.Diagnostics.Debug.WriteLine("RestoreBackup method called");
        var topLevel = GetTopLevel();
        if (topLevel != null)
        {
            System.Diagnostics.Debug.WriteLine("Opening file picker for backup file...");
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Backup File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Backup Files")
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
                await App.BackupService.RestoreBackupAsync(selectedPath);
                System.Diagnostics.Debug.WriteLine("Called RestoreBackupAsync on BackupService.");
                // Reload notes in main view
                if (App.MainViewModel is { } mainVm)
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
}
