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
}

