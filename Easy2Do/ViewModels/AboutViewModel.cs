using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Easy2Do.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _appName = "Easy2Do";

    [ObservableProperty]
    private string _version = "1.0.0";

    [ObservableProperty]
    private string _description = "A simple and elegant to-do list application with sticky note-style organization.";

    [ObservableProperty]
    private string _copyright = $"© {DateTime.Now.Year} Easy2Do. All rights reserved.";
}
