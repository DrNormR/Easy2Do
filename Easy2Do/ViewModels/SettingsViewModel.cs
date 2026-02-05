using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Easy2Do.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isAboutVisible = false;

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
}
