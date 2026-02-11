using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Easy2Do.Models;

namespace Easy2Do.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isAboutVisible = false;

    [ObservableProperty]
    private string _storageLocation = string.Empty;

    [ObservableProperty]
    private string _restoreStatusMessage = string.Empty;

    public SettingsViewModel()
    {
        StorageLocation = App.SettingsService.GetStorageLocation();
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
            else if (OperatingSystem.IsLinux())
            {
                System.Diagnostics.Process.Start("xdg-open", location);
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", location);
            }
        }
    }

    [RelayCommand]
    private async Task RestoreFromBackup()
    {
        RestoreStatusMessage = string.Empty;

        if (App.MainWindow == null)
            return;

        var topLevel = TopLevel.GetTopLevel(App.MainWindow);
        if (topLevel == null)
            return;

        var backupsDir = App.StorageService.BackupService.GetBackupsDirectoryPath();

        IStorageFolder? startFolder = null;
        try
        {
            startFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(backupsDir));
        }
        catch { /* fallback to default */ }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a Backup File to Restore",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON Backup Files") { Patterns = new[] { "*.json" } }
            }
        });

        if (files.Count == 0)
            return;

        var backupPath = files[0].Path.LocalPath;
        var restoredNote = await App.StorageService.RestoreNoteFromBackupAsync(backupPath);

        if (restoredNote == null)
        {
            RestoreStatusMessage = "Failed to restore from the selected backup file.";
            return;
        }

        // Reload the note in the MainViewModel if it exists, or add it
        if (App.MainWindow?.DataContext is MainViewModel mainVm)
        {
            await mainVm.ReloadOrAddNoteAsync(restoredNote);
        }

        RestoreStatusMessage = $"Restored \"{restoredNote.Title}\" from backup successfully.";
    }
}

